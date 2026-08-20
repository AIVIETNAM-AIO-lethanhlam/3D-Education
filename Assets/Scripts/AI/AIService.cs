using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class AIService
{
    [Serializable]
    private class AIChatRequest
    {
        public string message;

        // Nguồn mà student đang hỏi AI.
        // chat | 3d | ar | vr
        public string mode;

        // Context bài học - hiện tại có thể để trống.
        public string classId;
        public string lessonId;

        // Context mô hình - dùng sau này cho 3D/AR/VR.
        public string modelId;
        public string selectedPart;
    }

    [Serializable]
    private class AIChatResponse
    {
        public bool success;
        public string answer;
        public string error;
    }

    public static IEnumerator SendMessage(
        string message,
        Action<string> onSuccess,
        Action<string> onError)
    {
        // ---------------------------------------------------------
        // 1. Validate Supabase configuration
        // ---------------------------------------------------------

        if (!SupabaseConfig.TryValidate(out string configError))
        {
            Debug.LogError(
                "[AIService] Supabase config error: " +
                configError
            );

            onError?.Invoke(configError);
            yield break;
        }

        // ---------------------------------------------------------
        // 2. Check login session
        // ---------------------------------------------------------

        if (!SupabaseSession.IsLoggedIn)
        {
            const string error =
                "Không có phiên đăng nhập hợp lệ. Hãy đăng nhập lại.";

            Debug.LogError(
                "[AIService] " + error
            );

            onError?.Invoke(error);
            yield break;
        }

        // ---------------------------------------------------------
        // 3. Validate message
        // ---------------------------------------------------------

        if (string.IsNullOrWhiteSpace(message))
        {
            onError?.Invoke(
                "Message is empty."
            );

            yield break;
        }

        // ---------------------------------------------------------
        // 4. Build request body
        // ---------------------------------------------------------

        string currentClassId =
            PlayerPrefs.GetString(
                "selected_class_id",
                string.Empty
            );

        string currentLessonId =
            PlayerPrefs.GetString(
                "selected_lesson_id",
                string.Empty
            );

        AIChatRequest payload =
            new AIChatRequest
            {
                message = message.Trim(),

                mode = "chat",

                classId =
                    currentClassId?.Trim() ??
                    string.Empty,

                lessonId =
                    currentLessonId?.Trim() ??
                    string.Empty,

                modelId = string.Empty,
                selectedPart = string.Empty
            };

        string json =
            JsonUtility.ToJson(payload);

        byte[] bodyRaw =
            Encoding.UTF8.GetBytes(json);

        Debug.Log(
            "[AIService] AI Context\n" +
            "Mode: chat\n" +
            "Class ID: " + currentClassId + "\n" +
            "Lesson ID: " + currentLessonId
        );

        // ---------------------------------------------------------
        // 5. Create request
        // ---------------------------------------------------------

        string requestUrl =
            SupabaseConfig.AIChatFunctionUrl;

        using UnityWebRequest request =
            new UnityWebRequest(
                requestUrl,
                UnityWebRequest.kHttpVerbPOST
            );

        request.timeout =
            SupabaseConfig.RequestTimeoutSeconds;

        request.uploadHandler =
            new UploadHandlerRaw(bodyRaw);

        request.downloadHandler =
            new DownloadHandlerBuffer();

        // ---------------------------------------------------------
        // 6. Headers
        // ---------------------------------------------------------

        request.SetRequestHeader(
            "Content-Type",
            "application/json"
        );

        request.SetRequestHeader(
            "Accept",
            "application/json"
        );

        request.SetRequestHeader(
            "apikey",
            SupabaseConfig.PublishableKey
        );

        request.SetRequestHeader(
            "Authorization",
            "Bearer " + SupabaseSession.AccessToken
        );

        // ---------------------------------------------------------
        // 7. Debug
        // ---------------------------------------------------------

        Debug.Log(
            "[AIService] Sending AI request\n" +
            "URL: " + requestUrl + "\n" +
            "User: " + SupabaseSession.UserId
        );

        // IMPORTANT:
        // Never log AccessToken or GEMINI_API_KEY.

        // ---------------------------------------------------------
        // 8. Send request
        // ---------------------------------------------------------

        yield return request.SendWebRequest();

        string responseText =
            request.downloadHandler?.text ??
            string.Empty;

        // ---------------------------------------------------------
        // 9. HTTP error
        // ---------------------------------------------------------

        if (request.result !=
            UnityWebRequest.Result.Success)
        {
            Debug.LogError(
                "[AIService] AI request failed\n" +
                "HTTP Status: " +
                request.responseCode + "\n" +
                "Unity Error: " +
                request.error + "\n" +
                "Response: " +
                responseText
            );

            string errorMessage =
                ExtractErrorMessage(
                    responseText,
                    request.error,
                    request.responseCode
                );

            onError?.Invoke(errorMessage);
            yield break;
        }

        // ---------------------------------------------------------
        // 10. Validate response
        // ---------------------------------------------------------

        if (string.IsNullOrWhiteSpace(responseText))
        {
            const string error =
                "AI backend returned an empty response.";

            Debug.LogError(
                "[AIService] " + error
            );

            onError?.Invoke(error);
            yield break;
        }

        // ---------------------------------------------------------
        // 11. Parse JSON response
        // ---------------------------------------------------------

        AIChatResponse response;

        try
        {
            response =
                JsonUtility.FromJson<AIChatResponse>(
                    responseText
                );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[AIService] Could not parse AI response.\n" +
                "Response: " +
                responseText + "\n" +
                exception
            );

            onError?.Invoke(
                "Could not parse AI response."
            );

            yield break;
        }

        if (response == null)
        {
            onError?.Invoke(
                "Invalid AI response."
            );

            yield break;
        }

        // ---------------------------------------------------------
        // 12. Backend reported failure
        // ---------------------------------------------------------

        if (!response.success)
        {
            string error =
                string.IsNullOrWhiteSpace(response.error)
                    ? "AI request failed."
                    : response.error;

            Debug.LogError(
                "[AIService] Backend error: " +
                error
            );

            onError?.Invoke(error);
            yield break;
        }

        // ---------------------------------------------------------
        // 13. Validate AI answer
        // ---------------------------------------------------------

        if (string.IsNullOrWhiteSpace(response.answer))
        {
            const string error =
                "Gemini returned an empty answer.";

            Debug.LogError(
                "[AIService] " + error
            );

            onError?.Invoke(error);
            yield break;
        }

        // ---------------------------------------------------------
        // 14. Success
        // ---------------------------------------------------------

        Debug.Log(
            "[AIService] AI response received successfully."
        );

        onSuccess?.Invoke(
            response.answer.Trim()
        );
    }

    // =============================================================
    // Error parser
    // =============================================================

    private static string ExtractErrorMessage(
        string responseText,
        string unityError,
        long responseCode)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                AIChatResponse response =
                    JsonUtility.FromJson<AIChatResponse>(
                        responseText
                    );

                if (response != null &&
                    !string.IsNullOrWhiteSpace(
                        response.error))
                {
                    return response.error;
                }
            }
            catch
            {
                // Fall back to raw response below.
            }

            return responseText;
        }

        if (!string.IsNullOrWhiteSpace(unityError))
        {
            return unityError;
        }

        return
            $"AI request failed ({responseCode}).";
    }
}