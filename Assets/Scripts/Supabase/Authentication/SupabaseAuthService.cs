using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class SupabaseAuthService
{
    public static IEnumerator SignUp(
        string fullName,
        string email,
        string password,
        string role,
        Action<SupabaseAuthResponse> onSuccess,
        Action<string> onError)
    {
        SignUpRequest payload =
            new SignUpRequest
            {
                email = NormalizeEmail(email),
                password = password,
                data = new SignUpUserMetadata
                {
                    full_name =
                        NormalizePlainText(fullName),

                    display_name =
                        NormalizePlainText(fullName),

                    role =
                        NormalizeRole(role),

                    avatar_url =
                        string.Empty
                }
            };

        yield return SendAuthRequest(
            UnityWebRequest.kHttpVerbPOST,
            "/signup",
            JsonUtility.ToJson(payload),
            null,
            onSuccess,
            onError);
    }

    public static IEnumerator SignIn(
        string email,
        string password,
        Action<SupabaseAuthResponse> onSuccess,
        Action<string> onError)
    {
        SignInRequest payload =
            new SignInRequest
            {
                email = NormalizeEmail(email),
                password = password
            };

        yield return SendAuthRequest(
            UnityWebRequest.kHttpVerbPOST,
            "/token?grant_type=password",
            JsonUtility.ToJson(payload),
            null,
            onSuccess,
            onError);
    }

    public static IEnumerator UpdatePassword(
        string newPassword,
        Action onSuccess,
        Action<string> onError)
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke(
                "Không có phiên đăng nhập hợp lệ.");
            yield break;
        }

        UpdatePasswordRequest payload =
            new UpdatePasswordRequest
            {
                password = newPassword
            };

        bool succeeded = false;

        yield return SendAuthRequest(
            "PUT",
            "/user",
            JsonUtility.ToJson(payload),
            SupabaseSession.AccessToken,
            _ => succeeded = true,
            onError);

        if (succeeded)
        {
            onSuccess?.Invoke();
        }
    }

    public static void SignOutLocally()
    {
        SupabaseSession.Clear();
    }

    private static IEnumerator SendAuthRequest(
        string method,
        string endpoint,
        string requestJson,
        string accessToken,
        Action<SupabaseAuthResponse> onSuccess,
        Action<string> onError)
    {
        if (!SupabaseConfig.TryValidate(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        string requestUrl =
            SupabaseConfig.AuthUrl +
            endpoint;

        using UnityWebRequest request =
            new UnityWebRequest(requestUrl, method);

        request.timeout =
            SupabaseConfig.RequestTimeoutSeconds;

        request.uploadHandler =
            new UploadHandlerRaw(
                Encoding.UTF8.GetBytes(requestJson));

        request.downloadHandler =
            new DownloadHandlerBuffer();

        request.SetRequestHeader(
            "Content-Type",
            "application/json");

        request.SetRequestHeader(
            "Accept",
            "application/json");

        request.SetRequestHeader(
            "apikey",
            SupabaseConfig.PublishableKey);

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.SetRequestHeader(
                "Authorization",
                $"Bearer {accessToken}");
        }

        yield return request.SendWebRequest();

        string responseText =
            request.downloadHandler?.text ?? string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorMessage =
                ExtractAuthError(
                    responseText,
                    request.error);

            Debug.LogError(
                "Supabase Auth request failed\n" +
                $"URL: {requestUrl}\n" +
                $"HTTP status: {request.responseCode}\n" +
                $"Unity error: {request.error}\n" +
                $"Response: {responseText}");

            onError?.Invoke(errorMessage);
            yield break;
        }

        SupabaseAuthResponse response;

        try
        {
            response =
                JsonUtility.FromJson<SupabaseAuthResponse>(
                    responseText);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Không thể parse Supabase Auth response.\n" +
                $"Response: {responseText}\n" +
                exception);

            onError?.Invoke(
                "Supabase trả về dữ liệu không hợp lệ.");
            yield break;
        }

        if (response == null)
        {
            onError?.Invoke(
                "Không nhận được dữ liệu xác thực từ Supabase.");
            yield break;
        }

        onSuccess?.Invoke(response);
    }

    private static string ExtractAuthError(
        string responseText,
        string unityError)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                SupabaseErrorResponse error =
                    JsonUtility.FromJson<SupabaseErrorResponse>(
                        responseText);

                if (error != null)
                {
                    if (!string.IsNullOrWhiteSpace(error.msg))
                        return error.msg;

                    if (!string.IsNullOrWhiteSpace(error.message))
                        return error.message;

                    if (!string.IsNullOrWhiteSpace(
                            error.error_description))
                    {
                        return error.error_description;
                    }

                    if (!string.IsNullOrWhiteSpace(error.error))
                        return error.error;
                }
            }
            catch
            {
                // Trả nguyên response nếu parse thất bại.
            }

            return responseText;
        }

        return string.IsNullOrWhiteSpace(unityError)
            ? "Yêu cầu xác thực thất bại."
            : unityError;
    }

    private static string NormalizeEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Trim()
            .ToLowerInvariant()
            .Replace("\u200B", string.Empty)
            .Replace("\u200C", string.Empty)
            .Replace("\u200D", string.Empty)
            .Replace("\u2060", string.Empty)
            .Replace("\uFEFF", string.Empty);
    }

    private static string NormalizePlainText(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string NormalizeRole(string role)
    {
        return NormalizePlainText(role)
                   .ToLowerInvariant() == "teacher"
            ? "teacher"
            : "student";
    }
}
