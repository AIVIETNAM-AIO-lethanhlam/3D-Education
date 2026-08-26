using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Quiz service for hosted Supabase quiz APIs.
/// Unity Editor and Android APK use the same HTTPS endpoints.
/// </summary>
public class SupabaseQuizService : MonoBehaviour
{
    private const long MaxPdfBytes = 25L * 1024L * 1024L;

    private static string ParseQuizPdfFunctionUrl =>
        SupabaseConfig.FunctionsUrl.TrimEnd('/') + "/parse-quiz-pdf";

    private static string QuizQuestionsUrl =>
        SupabaseConfig.RestUrl.TrimEnd('/') + "/quiz_questions";

    /// <summary>
    /// Compatibility wrapper for the current CreateLessonPageController.
    /// The fourth argument must now be a LOCAL PDF path.
    /// teacherId is kept only so existing callers continue to compile.
    /// </summary>
    /// <summary>
    /// Full parse/upload/database pipeline.
    /// The Edge Function now:
    /// 1) parses the PDF with Gemini,
    /// 2) uploads the original PDF to Cloudflare R2,
    /// 3) creates lesson_assets,
    /// 4) creates quizzes / quiz_questions / quiz_options,
    /// 5) returns quiz_id + lesson_asset_id + R2 storage info.
    /// </summary>
    public IEnumerator CallParseQuizFunctionDetailed(
        string lessonId,
        string quizTitle,
        string localPdfPath,
        Action<ParseQuizPdfResponse> onSuccess,
        Action<string> onError)
    {
        yield return ParseLocalQuizPdf(
            lessonId,
            quizTitle,
            localPdfPath,
            onSuccess,
            onError
        );
    }

    public IEnumerator CallParseQuizFunction(
        string lessonId,
        string teacherId,
        string quizTitle,
        string localPdfPath,
        Action<bool> onComplete)
    {
        ParseQuizPdfResponse parsedResponse = null;
        string error = null;

        yield return ParseLocalQuizPdf(
            lessonId,
            quizTitle,
            localPdfPath,
            response => parsedResponse = response,
            message => error = message
        );

        bool success =
            string.IsNullOrWhiteSpace(error) &&
            parsedResponse != null &&
            parsedResponse.success &&
            parsedResponse.quiz != null &&
            parsedResponse.quiz.questions != null &&
            parsedResponse.quiz.questions.Length > 0;

        if (!success)
        {
            Debug.LogError(
                "[SupabaseQuizService] Quiz PDF parse failed.\n" +
                $"Lesson ID: {lessonId}\n" +
                $"PDF: {localPdfPath}\n" +
                $"Error: {error}"
            );
        }

        onComplete?.Invoke(success);
    }

    /// <summary>
    /// Sends a LOCAL PDF file to parse-quiz-pdf as multipart/form-data.
    /// Authentication is taken from the current SupabaseSession.
    /// </summary>
    public IEnumerator ParseLocalQuizPdf(
        string lessonId,
        string quizTitle,
        string localPdfPath,
        Action<ParseQuizPdfResponse> onSuccess,
        Action<string> onError)
    {
        if (!SupabaseConfig.TryValidate(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke(
                "Không có phiên đăng nhập Supabase hợp lệ. Hãy đăng nhập lại."
            );
            yield break;
        }

        if (string.IsNullOrWhiteSpace(SupabaseSession.AccessToken))
        {
            onError?.Invoke(
                "Supabase access token đang trống. Hãy đăng nhập lại."
            );
            yield break;
        }

        if (string.IsNullOrWhiteSpace(localPdfPath))
        {
            onError?.Invoke("Đường dẫn PDF đang trống.");
            yield break;
        }

        if (!File.Exists(localPdfPath))
        {
            onError?.Invoke(
                "Không tìm thấy PDF local. parse-quiz-pdf hiện yêu cầu file PDF thật " +
                "từ thiết bị, không phải storage_path trên Cloudflare R2.\n" +
                $"Path nhận được: {localPdfPath}"
            );
            yield break;
        }

        if (!string.Equals(
                Path.GetExtension(localPdfPath),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            onError?.Invoke("Chỉ hỗ trợ file .pdf.");
            yield break;
        }

        FileInfo fileInfo = new FileInfo(localPdfPath);

        if (fileInfo.Length <= 0)
        {
            onError?.Invoke("PDF đang rỗng.");
            yield break;
        }

        if (fileInfo.Length > MaxPdfBytes)
        {
            onError?.Invoke(
                $"PDF vượt quá giới hạn {MaxPdfBytes / (1024 * 1024)} MB."
            );
            yield break;
        }

        byte[] pdfBytes;

        try
        {
            pdfBytes = File.ReadAllBytes(localPdfPath);
        }
        catch (Exception exception)
        {
            onError?.Invoke(
                "Không thể đọc PDF local: " + exception.Message
            );
            yield break;
        }

        List<IMultipartFormSection> form = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection(
                "file",
                pdfBytes,
                Path.GetFileName(localPdfPath),
                "application/pdf"
            ),
            new MultipartFormDataSection(
                "quiz_title",
                string.IsNullOrWhiteSpace(quizTitle)
                    ? Path.GetFileNameWithoutExtension(localPdfPath)
                    : quizTitle.Trim()
            )
        };

        if (!string.IsNullOrWhiteSpace(lessonId))
        {
            form.Add(
                new MultipartFormDataSection(
                    "lesson_id",
                    lessonId.Trim()
                )
            );
        }

        Debug.Log(
            "[SupabaseQuizService] Sending quiz PDF to parse-quiz-pdf...\n" +
            $"URL: {ParseQuizPdfFunctionUrl}\n" +
            $"File: {Path.GetFileName(localPdfPath)}\n" +
            $"Size: {pdfBytes.Length} bytes\n" +
            $"Lesson ID: {lessonId}"
        );

        using UnityWebRequest request =
            UnityWebRequest.Post(ParseQuizPdfFunctionUrl, form);

        request.timeout = Math.Max(
            SupabaseConfig.RequestTimeoutSeconds,
            120
        );

        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader(
            "apikey",
            SupabaseConfig.PublishableKey
        );

        request.SetRequestHeader(
            "Authorization",
            $"Bearer {SupabaseSession.AccessToken}"
        );

        request.SetRequestHeader(
            "Accept",
            "application/json"
        );

        // Do not set Content-Type manually here.
        // Unity generates multipart/form-data + boundary automatically.

        yield return request.SendWebRequest();

        string responseText =
            request.downloadHandler?.text ?? string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorMessage = ExtractEdgeFunctionError(
                responseText,
                request.error,
                request.responseCode
            );

            Debug.LogError(
                "[SupabaseQuizService] parse-quiz-pdf request failed.\n" +
                $"HTTP: {request.responseCode}\n" +
                $"Unity error: {request.error}\n" +
                $"Response: {responseText}"
            );

            onError?.Invoke(errorMessage);
            yield break;
        }

        ParseQuizPdfResponse response;

        try
        {
            response =
                JsonUtility.FromJson<ParseQuizPdfResponse>(
                    responseText
                );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[SupabaseQuizService] Cannot parse Edge Function JSON.\n" +
                $"Response: {responseText}\n" +
                exception
            );

            onError?.Invoke(
                "Edge Function trả về JSON không hợp lệ."
            );
            yield break;
        }

        if (response == null)
        {
            onError?.Invoke(
                "Không nhận được response hợp lệ từ parse-quiz-pdf."
            );
            yield break;
        }

        if (!response.success)
        {
            onError?.Invoke(
                string.IsNullOrWhiteSpace(response.error)
                    ? "parse-quiz-pdf trả về success=false."
                    : response.error
            );
            yield break;
        }

        if (response.quiz == null ||
            response.quiz.questions == null ||
            response.quiz.questions.Length == 0)
        {
            onError?.Invoke(
                "AI không tách được câu hỏi nào từ PDF."
            );
            yield break;
        }

        Debug.Log(
            "[SupabaseQuizService] Quiz pipeline completed successfully.\n" +
            $"Quiz ID: {response.quiz_id}\n" +
            $"Lesson Asset ID: {response.lesson_asset_id}\n" +
            $"Quiz: {response.quiz.title}\n" +
            $"Questions: {response.quiz.total_questions}\n" +
            $"R2 Bucket: {response.storage?.bucket}\n" +
            $"R2 Path: {response.storage?.path}\n" +
            BuildQuestionPreview(response.quiz)
        );

        onSuccess?.Invoke(response);
    }

    /// <summary>
    /// Convenience overload if the caller only needs true/false.
    /// </summary>
    public IEnumerator ParseLocalQuizPdf(
        string lessonId,
        string quizTitle,
        string localPdfPath,
        Action<bool> onComplete)
    {
        bool succeeded = false;

        yield return ParseLocalQuizPdf(
            lessonId,
            quizTitle,
            localPdfPath,
            _ => succeeded = true,
            error =>
            {
                succeeded = false;
                Debug.LogError(
                    "[SupabaseQuizService] " + error
                );
            }
        );

        onComplete?.Invoke(succeeded);
    }

    /// <summary>
    /// Legacy-compatible loader. anonKey is ignored now.
    /// Authentication uses PublishableKey + current user access token.
    /// </summary>
    public IEnumerator LoadQuizQuestions(
        string quizId,
        string anonKey,
        Action<List<QuizQuestion>> onSuccess)
    {
        yield return LoadQuizQuestions(
            quizId,
            onSuccess,
            error =>
            {
                Debug.LogError(
                    "[SupabaseQuizService] LoadQuizQuestions failed: " +
                    error
                );
            }
        );
    }

    /// <summary>
    /// Loads quiz questions using the current logged-in Supabase session.
    /// This keeps the current project models for compatibility.
    /// DoQuizScene will later be refactored to use quiz_options_student.
    /// </summary>
    public IEnumerator LoadQuizQuestions(
        string quizId,
        Action<List<QuizQuestion>> onSuccess,
        Action<string> onError)
    {
        if (!SupabaseConfig.TryValidate(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke(
                "Không có phiên đăng nhập hợp lệ."
            );
            yield break;
        }

        if (string.IsNullOrWhiteSpace(quizId))
        {
            onError?.Invoke("quizId đang trống.");
            yield break;
        }

        string encodedQuizId =
            UnityWebRequest.EscapeURL(quizId.Trim());

        string url =
            $"{QuizQuestionsUrl}" +
            "?select=id,question_text,question_order," +
            "quiz_options(id,option_key,option_text)" +
            $"&quiz_id=eq.{encodedQuizId}" +
            "&order=question_order.asc";

        using UnityWebRequest request =
            UnityWebRequest.Get(url);

        request.timeout =
            SupabaseConfig.RequestTimeoutSeconds;

        request.SetRequestHeader(
            "apikey",
            SupabaseConfig.PublishableKey
        );

        request.SetRequestHeader(
            "Authorization",
            $"Bearer {SupabaseSession.AccessToken}"
        );

        request.SetRequestHeader(
            "Accept",
            "application/json"
        );

        yield return request.SendWebRequest();

        string responseText =
            request.downloadHandler?.text ?? string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string message = ExtractRestError(
                responseText,
                request.error,
                request.responseCode
            );

            Debug.LogError(
                "[SupabaseQuizService] Cannot load quiz questions.\n" +
                $"HTTP: {request.responseCode}\n" +
                $"URL: {url}\n" +
                $"Response: {responseText}"
            );

            onError?.Invoke(message);
            yield break;
        }

        try
        {
            string wrappedJson =
                "{\"items\":" + responseText + "}";

            QuestionListWrapper wrapper =
                JsonUtility.FromJson<QuestionListWrapper>(
                    wrappedJson
                );

            onSuccess?.Invoke(
                wrapper?.items ?? new List<QuizQuestion>()
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[SupabaseQuizService] Cannot parse quiz questions.\n" +
                exception
            );

            onError?.Invoke(
                "Không thể parse dữ liệu câu hỏi từ Supabase."
            );
        }
    }

    private static string BuildQuestionPreview(
        ParseQuizResult quiz)
    {
        if (quiz?.questions == null)
        {
            return string.Empty;
        }

        int previewCount = Mathf.Min(
            quiz.questions.Length,
            10
        );

        System.Text.StringBuilder builder =
            new System.Text.StringBuilder();

        for (int i = 0; i < previewCount; i++)
        {
            ParsedQuizQuestion question =
                quiz.questions[i];

            if (question == null)
            {
                continue;
            }

            builder.AppendLine(
                $"Q{question.question_order}: " +
                $"{question.question_text} " +
                $"[Correct: {question.correct_answer}]"
            );
        }

        if (quiz.questions.Length > previewCount)
        {
            builder.AppendLine(
                $"... {quiz.questions.Length - previewCount} more question(s)"
            );
        }

        return builder.ToString();
    }

    private static string ExtractEdgeFunctionError(
        string responseText,
        string unityError,
        long responseCode)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                ParseQuizPdfResponse error =
                    JsonUtility.FromJson<ParseQuizPdfResponse>(
                        responseText
                    );

                if (error != null &&
                    !string.IsNullOrWhiteSpace(error.error))
                {
                    string message = error.error;

                    if (!string.IsNullOrWhiteSpace(error.details))
                    {
                        message += "\nGemini details: " + error.details;
                    }

                    if (error.gemini_http_status > 0)
                    {
                        message +=
                            $"\nGemini HTTP: {error.gemini_http_status}";
                    }

                    if (!string.IsNullOrWhiteSpace(error.gemini_model))
                    {
                        message +=
                            "\nGemini model: " + error.gemini_model;
                    }

                    return message;
                }
            }
            catch
            {
                // Fall back to raw response.
            }

            return responseText;
        }

        return string.IsNullOrWhiteSpace(unityError)
            ? $"parse-quiz-pdf thất bại ({responseCode})."
            : unityError;
    }

    private static string ExtractRestError(
        string responseText,
        string unityError,
        long responseCode)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                SupabaseErrorResponse error =
                    JsonUtility.FromJson<SupabaseErrorResponse>(
                        responseText
                    );

                if (error != null)
                {
                    if (!string.IsNullOrWhiteSpace(error.message))
                        return error.message;

                    if (!string.IsNullOrWhiteSpace(error.msg))
                        return error.msg;

                    if (!string.IsNullOrWhiteSpace(error.details))
                        return error.details;

                    if (!string.IsNullOrWhiteSpace(error.hint))
                        return error.hint;
                }
            }
            catch
            {
                // Fall back to raw response.
            }

            return responseText;
        }

        return string.IsNullOrWhiteSpace(unityError)
            ? $"Supabase request thất bại ({responseCode})."
            : unityError;
    }
}

[Serializable]
public class ParseQuizPdfResponse
{
    public bool success;
    public int parser_version;
    public string user_id;
    public string lesson_id;

    // Created by parse-quiz-pdf backend.
    public string quiz_id;
    public string lesson_asset_id;

    public string original_file_name;
    public long original_file_size;
    public string error;

    // Diagnostic fields returned by parse-quiz-pdf when Gemini fails.
    public string details;
    public int gemini_http_status;
    public string gemini_model;

    public ParseQuizStorage storage;
    public ParseQuizResult quiz;
}

[Serializable]
public class ParseQuizStorage
{
    public string provider;
    public string bucket;
    public string path;
}

[Serializable]
public class ParseQuizResult
{
    public string title;
    public int total_questions;
    public ParsedQuizQuestion[] questions;
}

[Serializable]
public class ParsedQuizQuestion
{
    public int question_order;
    public string question_text;
    public string option_a;
    public string option_b;
    public string option_c;
    public string option_d;
    public string correct_answer;
    public string explanation;
}
