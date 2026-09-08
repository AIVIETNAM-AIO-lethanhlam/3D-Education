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

    // =========================================================
    // GOOGLE OAUTH
    // =========================================================

    /// <summary>
    /// Deep link that Supabase uses to return to the Unity Android app
    /// after Google OAuth completes. This exact URL must exist in
    /// Authentication -> URL Configuration -> Redirect URLs.
    /// </summary>
    public const string GoogleOAuthRedirectUrl =
        "virtualeducation://auth/callback";

    /// <summary>
    /// Stores the pending Google-auth intent locally, then opens Supabase's
    /// Google OAuth endpoint in the system browser.
    ///
    /// mode should be "login" or "register". For register, role is kept so
    /// AuthPageController can create the user's profile with the selected role
    /// after the deep-link callback returns to Unity.
    /// </summary>
    public static bool OpenGoogleOAuth(
        string mode,
        string role,
        Action<string> onError = null)
    {
        if (!SupabaseConfig.TryValidate(out string configError))
        {
            onError?.Invoke(configError);
            return false;
        }

        string normalizedMode =
            string.Equals(
                NormalizePlainText(mode),
                "register",
                StringComparison.OrdinalIgnoreCase)
                ? "register"
                : "login";

        string normalizedRole = NormalizeRole(role);

        // Keep only non-sensitive UI state locally. Never store Google Client
        // Secret here; the secret belongs only in Google Cloud / Supabase.
        PlayerPrefs.SetString(
            "google_oauth_pending_mode",
            normalizedMode);

        PlayerPrefs.SetString(
            "google_oauth_pending_role",
            normalizedRole);

        PlayerPrefs.Save();

        string oauthUrl = BuildGoogleOAuthUrl();

        if (string.IsNullOrWhiteSpace(oauthUrl))
        {
            onError?.Invoke(
                "Không thể tạo URL đăng nhập Google.");
            return false;
        }

        Debug.Log(
            "Opening Google OAuth via Supabase.\n" +
            $"Redirect: {GoogleOAuthRedirectUrl}");

        Application.OpenURL(oauthUrl);
        return true;
    }

    /// <summary>
    /// Builds the Supabase Auth authorize URL for Google.
    /// Supabase will send the user to Google, receive Google's callback at
    /// /auth/v1/callback, then redirect back to GoogleOAuthRedirectUrl.
    /// </summary>
    public static string BuildGoogleOAuthUrl()
    {
        if (!SupabaseConfig.TryValidate(out _))
        {
            return string.Empty;
        }

        string authUrl =
            (SupabaseConfig.AuthUrl ?? string.Empty)
                .TrimEnd('/');

        if (string.IsNullOrWhiteSpace(authUrl))
        {
            return string.Empty;
        }

        return authUrl +
               "/authorize?provider=google&redirect_to=" +
               Uri.EscapeDataString(GoogleOAuthRedirectUrl);
    }

    /// <summary>
    /// Clears temporary UI state left by a Google OAuth attempt.
    /// Call this after the callback has been processed or when the flow is
    /// cancelled.
    /// </summary>
    public static void ClearPendingGoogleOAuthState()
    {
        PlayerPrefs.DeleteKey("google_oauth_pending_mode");
        PlayerPrefs.DeleteKey("google_oauth_pending_role");
        PlayerPrefs.Save();
    }


    /// <summary>
    /// Completes the implicit Google OAuth flow after Supabase redirects back
    /// to the Unity app. The access token from the deep link is used to fetch
    /// the authenticated Supabase user, then a normal SupabaseAuthResponse is
    /// reconstructed so the rest of the app can reuse SupabaseSession.
    /// </summary>
    public static IEnumerator CompleteGoogleOAuthSession(
        string accessToken,
        string refreshToken,
        int expiresIn,
        string tokenType,
        Action<SupabaseAuthResponse> onSuccess,
        Action<string> onError)
    {
        if (!SupabaseConfig.TryValidate(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            onError?.Invoke(
                "Google OAuth callback không chứa access token.");
            yield break;
        }

        string userJson = null;
        string userError = null;

        yield return GetAuthenticatedUserJson(
            accessToken,
            value => userJson = value,
            error => userError = error);

        if (!string.IsNullOrWhiteSpace(userError))
        {
            onError?.Invoke(userError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(userJson))
        {
            onError?.Invoke(
                "Supabase không trả về thông tin người dùng Google.");
            yield break;
        }

        SupabaseAuthResponse response =
            BuildOAuthAuthResponse(
                accessToken,
                refreshToken,
                expiresIn,
                tokenType,
                userJson,
                onError);

        if (response == null)
            yield break;

        onSuccess?.Invoke(response);
    }

    /// <summary>
    /// Ensures that a Google-authenticated user has a role in user_metadata.
    /// Existing roles are never overwritten. For a first-time Google user,
    /// the role selected in AuthScene is saved through PUT /auth/v1/user.
    /// </summary>
    public static IEnumerator EnsureGoogleUserRole(
        SupabaseAuthResponse authResponse,
        string selectedRole,
        int expiresIn,
        string tokenType,
        Action<SupabaseAuthResponse> onSuccess,
        Action<string> onError)
    {
        if (authResponse == null ||
            authResponse.user == null ||
            string.IsNullOrWhiteSpace(authResponse.access_token))
        {
            onError?.Invoke(
                "Phiên Google OAuth không hợp lệ.");
            yield break;
        }

        string existingRole =
            authResponse.user.user_metadata?.role;

        if (!string.IsNullOrWhiteSpace(existingRole))
        {
            onSuccess?.Invoke(authResponse);
            yield break;
        }

        string normalizedRole = NormalizeRole(selectedRole);

        string payload =
            "{\"data\":{\"role\":\"" +
            EscapeJsonString(normalizedRole) +
            "\"}}";

        string updatedUserJson = null;
        string updateError = null;

        yield return SendRawAuthRequest(
            "PUT",
            "/user",
            payload,
            authResponse.access_token,
            value => updatedUserJson = value,
            error => updateError = error);

        if (!string.IsNullOrWhiteSpace(updateError))
        {
            onError?.Invoke(updateError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(updatedUserJson))
        {
            onError?.Invoke(
                "Không nhận được user sau khi lưu role Google.");
            yield break;
        }

        SupabaseAuthResponse updatedResponse =
            BuildOAuthAuthResponse(
                authResponse.access_token,
                authResponse.refresh_token,
                expiresIn,
                tokenType,
                updatedUserJson,
                onError);

        if (updatedResponse == null)
            yield break;

        onSuccess?.Invoke(updatedResponse);
    }

    private static IEnumerator GetAuthenticatedUserJson(
        string accessToken,
        Action<string> onSuccess,
        Action<string> onError)
    {
        string requestUrl =
            (SupabaseConfig.AuthUrl ?? string.Empty).TrimEnd('/') +
            "/user";

        using UnityWebRequest request =
            UnityWebRequest.Get(requestUrl);

        request.timeout =
            SupabaseConfig.RequestTimeoutSeconds;

        request.downloadHandler =
            new DownloadHandlerBuffer();

        request.SetRequestHeader(
            "Accept",
            "application/json");

        request.SetRequestHeader(
            "apikey",
            SupabaseConfig.PublishableKey);

        request.SetRequestHeader(
            "Authorization",
            $"Bearer {accessToken}");

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
                "Supabase Google OAuth /user request failed\n" +
                $"HTTP status: {request.responseCode}\n" +
                $"Unity error: {request.error}\n" +
                $"Response: {responseText}");

            onError?.Invoke(errorMessage);
            yield break;
        }

        onSuccess?.Invoke(responseText);
    }

    private static SupabaseAuthResponse BuildOAuthAuthResponse(
        string accessToken,
        string refreshToken,
        int expiresIn,
        string tokenType,
        string userJson,
        Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(userJson))
        {
            onError?.Invoke(
                "Không có dữ liệu user để tạo phiên Google.");
            return null;
        }

        string normalizedTokenType =
            string.IsNullOrWhiteSpace(tokenType)
                ? "bearer"
                : tokenType.Trim();

        int normalizedExpiresIn =
            expiresIn > 0 ? expiresIn : 3600;

        string responseJson =
            "{" +
            "\"access_token\":\"" +
            EscapeJsonString(accessToken) +
            "\"," +
            "\"refresh_token\":\"" +
            EscapeJsonString(refreshToken) +
            "\"," +
            "\"token_type\":\"" +
            EscapeJsonString(normalizedTokenType) +
            "\"," +
            "\"expires_in\":" +
            normalizedExpiresIn +
            "," +
            "\"user\":" +
            userJson +
            "}";

        try
        {
            SupabaseAuthResponse response =
                JsonUtility.FromJson<SupabaseAuthResponse>(
                    responseJson);

            if (response == null ||
                response.user == null ||
                string.IsNullOrWhiteSpace(response.access_token))
            {
                onError?.Invoke(
                    "Không thể tạo phiên Supabase từ Google OAuth.");
                return null;
            }

            return response;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Không thể parse Google OAuth Supabase session.\n" +
                exception);

            onError?.Invoke(
                "Dữ liệu Google OAuth trả về không hợp lệ.");
            return null;
        }
    }

    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
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



    /// <summary>
    /// Checks whether an email exists in Supabase Auth through the
    /// public.check_email_exists RPC. The RPC must be created in Supabase SQL.
    /// This avoids exposing auth.users directly to the Unity client.
    /// </summary>
    public static IEnumerator CheckEmailExists(
        string email,
        Action<bool> onSuccess,
        Action<string> onError)
    {
        if (!SupabaseConfig.TryValidate(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        string normalizedEmail = NormalizeEmail(email);

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            onSuccess?.Invoke(false);
            yield break;
        }

        // SupabaseConfig.AuthUrl normally ends with /auth/v1.
        // Derive the same project's REST endpoint without adding another
        // project-wide configuration dependency.
        string authUrl = (SupabaseConfig.AuthUrl ?? string.Empty).TrimEnd('/');
        const string authSuffix = "/auth/v1";

        string projectBaseUrl =
            authUrl.EndsWith(authSuffix, StringComparison.OrdinalIgnoreCase)
                ? authUrl.Substring(0, authUrl.Length - authSuffix.Length)
                : authUrl.Replace("/auth/v1", string.Empty);

        string requestUrl =
            projectBaseUrl.TrimEnd('/') +
            "/rest/v1/rpc/check_email_exists";

        CheckEmailExistsRequest payload = new CheckEmailExistsRequest
        {
            p_email = normalizedEmail
        };

        using UnityWebRequest request =
            new UnityWebRequest(
                requestUrl,
                UnityWebRequest.kHttpVerbPOST);

        request.timeout =
            SupabaseConfig.RequestTimeoutSeconds;

        request.uploadHandler =
            new UploadHandlerRaw(
                Encoding.UTF8.GetBytes(
                    JsonUtility.ToJson(payload)));

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

        yield return request.SendWebRequest();

        string responseText =
            request.downloadHandler?.text ?? string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError(
                "Supabase email existence check failed\n" +
                $"URL: {requestUrl}\n" +
                $"HTTP status: {request.responseCode}\n" +
                $"Unity error: {request.error}\n" +
                $"Response: {responseText}");

            onError?.Invoke(
                string.IsNullOrWhiteSpace(responseText)
                    ? request.error
                    : responseText);

            yield break;
        }

        string normalizedResponse =
            responseText.Trim().Trim('"');

        if (bool.TryParse(
                normalizedResponse,
                out bool exists))
        {
            onSuccess?.Invoke(exists);
            yield break;
        }

        Debug.LogError(
            "Unexpected check_email_exists response: " +
            responseText);

        onError?.Invoke(
            "Supabase returned an invalid email-check response.");
    }

    // =========================================================
    // PASSWORD RECOVERY
    // =========================================================

    /// <summary>
    /// Requests a Supabase password-recovery email.
    /// PasswordRecoveryFlow expects the recovery email template to expose
    /// Supabase's 6-digit Token ({{ .Token }}) so the user can type the OTP.
    /// </summary>
    public static IEnumerator SendPasswordRecoveryCode(
        string email,
        Action onSuccess,
        Action<string> onError)
    {
        RecoveryRequest payload = new RecoveryRequest
        {
            email = NormalizeEmail(email)
        };

        bool succeeded = false;

        yield return SendRawAuthRequest(
            UnityWebRequest.kHttpVerbPOST,
            "/recover",
            JsonUtility.ToJson(payload),
            null,
            _ => succeeded = true,
            onError);

        if (succeeded)
            onSuccess?.Invoke();
    }

    /// <summary>
    /// Verifies the 6-digit recovery OTP and returns the temporary recovery
    /// session. PasswordRecoveryFlow keeps its access token only in memory.
    /// </summary>
    public static IEnumerator VerifyRecoveryCode(
        string email,
        string code,
        Action<SupabaseAuthResponse> onSuccess,
        Action<string> onError)
    {
        VerifyRecoveryRequest payload = new VerifyRecoveryRequest
        {
            email = NormalizeEmail(email),
            token = (code ?? string.Empty).Trim(),
            type = "recovery"
        };

        string responseText = null;
        string requestError = null;

        yield return SendRawAuthRequest(
            UnityWebRequest.kHttpVerbPOST,
            "/verify",
            JsonUtility.ToJson(payload),
            null,
            value => responseText = value,
            error => requestError = error);

        if (!string.IsNullOrWhiteSpace(requestError))
        {
            onError?.Invoke(requestError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            onError?.Invoke("Supabase không trả về phiên khôi phục mật khẩu.");
            yield break;
        }

        SupabaseAuthResponse response = null;

        try
        {
            response = JsonUtility.FromJson<SupabaseAuthResponse>(responseText);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Không thể parse Supabase recovery verification response.\n" +
                $"Response: {responseText}\n" +
                exception);

            onError?.Invoke("Supabase trả về dữ liệu xác thực không hợp lệ.");
            yield break;
        }

        if (response == null ||
            string.IsNullOrWhiteSpace(response.access_token))
        {
            onError?.Invoke(
                "Mã xác minh không hợp lệ, đã hết hạn hoặc không tạo được phiên khôi phục.");
            yield break;
        }

        onSuccess?.Invoke(response);
    }

    /// <summary>
    /// Updates the user's password using ONLY the temporary recovery access
    /// token. This method does not save that token into SupabaseSession.
    /// </summary>
    public static IEnumerator UpdatePasswordWithAccessToken(
        string newPassword,
        string accessToken,
        Action onSuccess,
        Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            onError?.Invoke("Phiên khôi phục mật khẩu không hợp lệ.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            onError?.Invoke("Mật khẩu mới không được để trống.");
            yield break;
        }

        UpdatePasswordRequest payload = new UpdatePasswordRequest
        {
            password = newPassword
        };

        bool succeeded = false;

        // PUT /user returns a User object, not SupabaseAuthResponse, therefore
        // use the raw request helper instead of SendAuthRequest.
        yield return SendRawAuthRequest(
            "PUT",
            "/user",
            JsonUtility.ToJson(payload),
            accessToken,
            _ => succeeded = true,
            onError);

        if (succeeded)
            onSuccess?.Invoke();
    }

    public static void SignOutLocally()
    {
        SupabaseSession.Clear();
    }


    private static IEnumerator SendRawAuthRequest(
        string method,
        string endpoint,
        string requestJson,
        string accessToken,
        Action<string> onSuccess,
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
                Encoding.UTF8.GetBytes(requestJson ?? "{}"));

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

        onSuccess?.Invoke(responseText);
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


    [Serializable]
    private sealed class CheckEmailExistsRequest
    {
        public string p_email;
    }

    [Serializable]
    private sealed class RecoveryRequest
    {
        public string email;
    }

    [Serializable]
    private sealed class VerifyRecoveryRequest
    {
        public string email;
        public string token;
        public string type;
    }

}
