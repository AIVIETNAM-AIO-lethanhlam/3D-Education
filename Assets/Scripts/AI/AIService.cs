using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class AIService
{
    /// <summary>
    /// BUG-016: returned through onError when Gemini's quota / rate limit is hit
    /// (HTTP 429, RESOURCE_EXHAUSTED...). Screens show a friendly message for it
    /// instead of the raw provider error.
    /// </summary>
    public const string RateLimitError = "AI_RATE_LIMIT";

    public static bool IsRateLimitError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;

        string value = error.ToLowerInvariant();
        return value.Contains("ai_rate_limit") ||
               value.Contains("429") ||
               value.Contains("resource_exhausted") ||
               value.Contains("resource has been exhausted") ||
               value.Contains("quota") ||
               value.Contains("rate limit") ||
               value.Contains("too many requests");
    }

    [Serializable]
    private class AIImagePayload
    {
        public string imageBase64;
        public string imageMimeType;
    }

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

        // Optional Gemini vision input. Base64 must not include a data: prefix.
        public string imageBase64;
        public string imageMimeType;
        public AIImagePayload[] images;

        // Previous turns of the conversation (oldest first), 2026-09.
        public AIHistoryItem[] history;
    }

    /// <summary>One previous chat turn. role = "user" or "model".</summary>
    [Serializable]
    public class AIHistoryItem
    {
        public string role;
        public string text;
    }

    private const int MaxHistoryItems = 12;
    private static AIHistoryItem[] pendingHistory = Array.Empty<AIHistoryItem>();

    /// <summary>
    /// Sets the conversation history for the NEXT request only (it is consumed
    /// by that request). Callers that do not set it send no history.
    /// </summary>
    public static void SetConversationHistory(IList<AIHistoryItem> items)
    {
        if (items == null || items.Count == 0)
        {
            pendingHistory = Array.Empty<AIHistoryItem>();
            return;
        }

        int start = Mathf.Max(0, items.Count - MaxHistoryItems);
        List<AIHistoryItem> result = new List<AIHistoryItem>();
        for (int i = start; i < items.Count; i++)
        {
            AIHistoryItem item = items[i];
            if (item == null || string.IsNullOrWhiteSpace(item.text))
                continue;

            result.Add(new AIHistoryItem
            {
                role = string.Equals(item.role, "user", StringComparison.OrdinalIgnoreCase) ? "user" : "model",
                text = item.text.Trim()
            });
        }

        pendingHistory = result.ToArray();
    }

    private static AIHistoryItem[] TakeHistory()
    {
        AIHistoryItem[] history = pendingHistory ?? Array.Empty<AIHistoryItem>();
        pendingHistory = Array.Empty<AIHistoryItem>();
        return history;
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
        return SendMessageInternal(
            message,
            string.Empty,
            string.Empty,
            onSuccess,
            onError
        );
    }

    public static IEnumerator SendMessageWithImage(
        string message,
        string imageBase64,
        string imageMimeType,
        Action<string> onSuccess,
        Action<string> onError)
    {
        return SendMessageInternal(
            message,
            imageBase64,
            imageMimeType,
            onSuccess,
            onError
        );
    }

    public static IEnumerator SendMessageWithImages(
        string message,
        string[] imageBase64Values,
        string[] imageMimeTypes,
        Action<string> onSuccess,
        Action<string> onError)
    {
        int count = imageBase64Values == null ? 0 : imageBase64Values.Length;
        if (count == 0 || imageMimeTypes == null || imageMimeTypes.Length != count)
        {
            onError?.Invoke("Invalid image selection.");
            return EmptyRoutine();
        }

        if (count > 4)
        {
            onError?.Invoke("You can send up to 4 images.");
            return EmptyRoutine();
        }

        AIImagePayload[] images = new AIImagePayload[count];
        for (int i = 0; i < count; i++)
        {
            images[i] = new AIImagePayload
            {
                imageBase64 = imageBase64Values[i],
                imageMimeType = imageMimeTypes[i]
            };
        }

        return SendMessageInternal(
            message,
            string.Empty,
            string.Empty,
            onSuccess,
            onError,
            images
        );
    }

    private static IEnumerator EmptyRoutine()
    {
        yield break;
    }

    private static IEnumerator SendMessageInternal(
        string message,
        string imageBase64,
        string imageMimeType,
        Action<string> onSuccess,
        Action<string> onError,
        AIImagePayload[] images = null)
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

        bool hasImage = !string.IsNullOrWhiteSpace(imageBase64) ||
                        (images != null && images.Length > 0);

        if (string.IsNullOrWhiteSpace(message) && !hasImage)
        {
            onError?.Invoke(
                "Message is empty."
            );

            yield break;
        }

        if (hasImage &&
            images == null &&
            imageMimeType != "image/jpeg" &&
            imageMimeType != "image/png" &&
            imageMimeType != "image/webp")
        {
            onError?.Invoke("Unsupported image format.");
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
                selectedPart = string.Empty,
                imageBase64 = hasImage ? imageBase64.Trim() : string.Empty,
                imageMimeType = !string.IsNullOrWhiteSpace(imageBase64)
                    ? imageMimeType.Trim()
                    : string.Empty,
                images = images ?? Array.Empty<AIImagePayload>(),
                history = TakeHistory()
            };

        string json =
            JsonUtility.ToJson(payload);

        byte[] bodyRaw =
            Encoding.UTF8.GetBytes(json);

        Debug.Log(
            "[AIService] AI Context\n" +
            "Mode: chat\n" +
            "Class ID: " + currentClassId + "\n" +
            "Lesson ID: " + currentLessonId + "\n" +
            "Has image: " + hasImage
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

            if (request.responseCode == 429 ||
                IsRateLimitError(errorMessage))
            {
                errorMessage = RateLimitError;
            }

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

            onError?.Invoke(IsRateLimitError(error) ? RateLimitError : error);
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
