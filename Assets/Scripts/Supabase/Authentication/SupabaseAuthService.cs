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
        SignUpRequest payload = new SignUpRequest
        {
            email = NormalizeEmail(email),
            password = password,
            data = new SignUpUserMetadata
            {
                full_name = NormalizePlainText(fullName),
                display_name = NormalizePlainText(fullName),
                role = NormalizeRole(role),
                avatar_url = string.Empty
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
        SignInRequest payload = new SignInRequest
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

    /// <summary>
    /// Requests Supabase's password-recovery email.
    /// The Recovery email template must include {{ .Token }} so the user receives
    /// the 6-digit OTP used by VerifyRecoveryCode().
    /// </summary>
    public static IEnumerator SendPasswordRecoveryCode(
        string email,
        Action onSuccess,
        Action<string> onError)
    {
        RecoverPasswordRequest payload = new RecoverPasswordRequest
        {
            email = NormalizeEmail(email)
        };

        yield return SendAuthCommand(
            UnityWebRequest.kHttpVerbPOST,
            "/recover",
            JsonUtility.ToJson(payload),
            null,
            onSuccess,
            onError);
    }

    /// <summary>
    /// Verifies the email recovery OTP. On success Supabase returns a temporary
    /// authenticated recovery session. Keep the access token in memory only and
    /// use it immediately with UpdatePasswordWithAccessToken().
    /// </summary>
    public static IEnumerator VerifyRecoveryCode(
        string email,
        string token,
        Action<SupabaseAuthResponse> onSuccess,
        Action<string> onError)
    {
        VerifyRecoveryOtpRequest payload = new VerifyRecoveryOtpRequest
        {
            email = NormalizeEmail(email),
            token = NormalizePlainText(token),
            type = "recovery"
        };

        yield return SendAuthRequest(
            UnityWebRequest.kHttpVerbPOST,
            "/verify",
            JsonUtility.ToJson(payload),
            null,
            onSuccess,
            onError);
    }

    /// <summary>
    /// Updates the password with the temporary access token returned by
    /// VerifyRecoveryCode(). This intentionally does not save a normal app session.
    /// </summary>
    public static IEnumerator UpdatePasswordWithAccessToken(
        string newPassword,
        string accessToken,
        Action onSuccess,
        Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            onError?.Invoke("The password recovery session is no longer valid. Please request a new code.");
            yield break;
        }

        UpdatePasswordRequest payload = new UpdatePasswordRequest
        {
            password = newPassword
        };

        yield return SendAuthCommand(
            "PUT",
            "/user",
            JsonUtility.ToJson(payload),
            accessToken,
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
            onError?.Invoke("Không có phiên đăng nhập hợp lệ.");
            yield break;
        }

        UpdatePasswordRequest payload = new UpdatePasswordRequest
        {
            password = newPassword
        };

        yield return SendAuthCommand(
            "PUT",
            "/user",
            JsonUtility.ToJson(payload),
            SupabaseSession.AccessToken,
            onSuccess,
            onError);
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

        string requestUrl = SupabaseConfig.AuthUrl + endpoint;

        using UnityWebRequest request = CreateAuthRequest(
            requestUrl,
            method,
            requestJson,
            accessToken);

        yield return request.SendWebRequest();

        string responseText = request.downloadHandler?.text ?? string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorMessage = ExtractAuthError(responseText, request.error);

            Debug.LogError(
                "Supabase Auth request failed\n" +
                $"URL: {requestUrl}\n" +
                $"HTTP status: {request.responseCode}\n" +
                $"Unity error: {request.error}\n" +
                $"Response: {responseText}");

            onError?.Invoke(errorMessage);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            onError?.Invoke("Supabase returned an empty authentication response.");
            yield break;
        }

        SupabaseAuthResponse response;

        try
        {
            response = JsonUtility.FromJson<SupabaseAuthResponse>(responseText);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Không thể parse Supabase Auth response.\n" +
                $"Response: {responseText}\n" +
                exception);

            onError?.Invoke("Supabase trả về dữ liệu không hợp lệ.");
            yield break;
        }

        if (response == null)
        {
            onError?.Invoke("Không nhận được dữ liệu xác thực từ Supabase.");
            yield break;
        }

        onSuccess?.Invoke(response);
    }

    private static IEnumerator SendAuthCommand(
        string method,
        string endpoint,
        string requestJson,
        string accessToken,
        Action onSuccess,
        Action<string> onError)
    {
        if (!SupabaseConfig.TryValidate(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        string requestUrl = SupabaseConfig.AuthUrl + endpoint;

        using UnityWebRequest request = CreateAuthRequest(
            requestUrl,
            method,
            requestJson,
            accessToken);

        yield return request.SendWebRequest();

        string responseText = request.downloadHandler?.text ?? string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorMessage = ExtractAuthError(responseText, request.error);

            Debug.LogError(
                "Supabase Auth command failed\n" +
                $"URL: {requestUrl}\n" +
                $"HTTP status: {request.responseCode}\n" +
                $"Unity error: {request.error}\n" +
                $"Response: {responseText}");

            onError?.Invoke(errorMessage);
            yield break;
        }

        onSuccess?.Invoke();
    }

    private static UnityWebRequest CreateAuthRequest(
        string requestUrl,
        string method,
        string requestJson,
        string accessToken)
    {
        UnityWebRequest request = new UnityWebRequest(requestUrl, method)
        {
            timeout = SupabaseConfig.RequestTimeoutSeconds,
            downloadHandler = new DownloadHandlerBuffer()
        };

        if (!string.IsNullOrWhiteSpace(requestJson))
        {
            request.uploadHandler = new UploadHandlerRaw(
                Encoding.UTF8.GetBytes(requestJson));
        }

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");
        request.SetRequestHeader("apikey", SupabaseConfig.PublishableKey);

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
        }

        return request;
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
                    JsonUtility.FromJson<SupabaseErrorResponse>(responseText);

                if (error != null)
                {
                    if (!string.IsNullOrWhiteSpace(error.msg))
                        return error.msg;

                    if (!string.IsNullOrWhiteSpace(error.message))
                        return error.message;

                    if (!string.IsNullOrWhiteSpace(error.error_description))
                        return error.error_description;

                    if (!string.IsNullOrWhiteSpace(error.error))
                        return error.error;

                    if (!string.IsNullOrWhiteSpace(error.error_code))
                        return error.error_code;

                    if (!string.IsNullOrWhiteSpace(error.code))
                        return error.code;
                }
            }
            catch
            {
                // Fall through and return the raw response.
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
            return string.Empty;

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
        return NormalizePlainText(role).ToLowerInvariant() == "teacher"
            ? "teacher"
            : "student";
    }
}
