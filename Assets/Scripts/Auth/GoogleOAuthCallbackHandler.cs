using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GoogleOAuthCallbackHandler : MonoBehaviour
{
    private const string GoogleCallbackPrefix =
        "virtualeducation://auth/callback";

    private const string MainHomeSceneName =
        "MainHomeScene";

    private static GoogleOAuthCallbackHandler instance;

    private bool isProcessingCallback;
    private string lastProcessedUrl = string.Empty;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        Application.deepLinkActivated += OnDeepLinkActivated;

        // Cold-start case: Android launches the app from the OAuth deep link.
        string startupUrl = Application.absoluteURL;

        if (!string.IsNullOrWhiteSpace(startupUrl))
        {
            HandleDeepLink(startupUrl);
        }
    }

    private void OnDisable()
    {
        Application.deepLinkActivated -= OnDeepLinkActivated;
    }

    private void OnDeepLinkActivated(string url)
    {
        HandleDeepLink(url);
    }

    private void HandleDeepLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!url.StartsWith(
                GoogleCallbackPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            // Other deep links, such as reset-password, are not handled here.
            return;
        }

        if (isProcessingCallback ||
            string.Equals(
                lastProcessedUrl,
                url,
                StringComparison.Ordinal))
        {
            return;
        }

        lastProcessedUrl = url;

        // Do not print the full callback URL because it contains auth tokens.
        Debug.Log(
            "[Google OAuth] Callback received successfully.");

        StartCoroutine(ProcessGoogleOAuthCallback(url));
    }

    private IEnumerator ProcessGoogleOAuthCallback(string url)
    {
        isProcessingCallback = true;

        Dictionary<string, string> parameters =
            ParseCallbackParameters(url);

        string oauthError =
            GetParameter(parameters, "error_description");

        if (string.IsNullOrWhiteSpace(oauthError))
        {
            oauthError =
                GetParameter(parameters, "error");
        }

        if (!string.IsNullOrWhiteSpace(oauthError))
        {
            FinishWithError(
                "Google OAuth thất bại: " + oauthError);
            yield break;
        }

        string accessToken =
            GetParameter(parameters, "access_token");

        string refreshToken =
            GetParameter(parameters, "refresh_token");

        string tokenType =
            GetParameter(parameters, "token_type");

        int expiresIn = 3600;

        string expiresInText =
            GetParameter(parameters, "expires_in");

        if (!string.IsNullOrWhiteSpace(expiresInText))
        {
            int.TryParse(
                expiresInText,
                out expiresIn);

            if (expiresIn <= 0)
                expiresIn = 3600;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            FinishWithError(
                "Google OAuth callback không chứa access_token.");
            yield break;
        }

        string pendingMode =
            PlayerPrefs.GetString(
                "google_oauth_pending_mode",
                "login");

        string selectedRole =
            NormalizeRole(
                PlayerPrefs.GetString(
                    "google_oauth_pending_role",
                    "student"));

        SupabaseAuthResponse authResponse = null;
        string sessionError = null;

        yield return SupabaseAuthService.CompleteGoogleOAuthSession(
            accessToken,
            refreshToken,
            expiresIn,
            tokenType,
            response => authResponse = response,
            error => sessionError = error);

        if (!string.IsNullOrWhiteSpace(sessionError) ||
            authResponse == null ||
            authResponse.user == null)
        {
            FinishWithError(
                string.IsNullOrWhiteSpace(sessionError)
                    ? "Không thể tạo phiên đăng nhập Google."
                    : sessionError);

            yield break;
        }

        string existingRole =
            NormalizeNullableRole(
                authResponse.user.user_metadata?.role);

        // Existing accounts keep their role. A first-time Google account gets
        // the role selected by the user before opening Google OAuth.
        if (string.IsNullOrWhiteSpace(existingRole))
        {
            SupabaseAuthResponse roleUpdatedResponse = null;
            string roleError = null;

            yield return SupabaseAuthService.EnsureGoogleUserRole(
                authResponse,
                selectedRole,
                expiresIn,
                tokenType,
                response => roleUpdatedResponse = response,
                error => roleError = error);

            if (!string.IsNullOrWhiteSpace(roleError) ||
                roleUpdatedResponse == null)
            {
                FinishWithError(
                    string.IsNullOrWhiteSpace(roleError)
                        ? "Không thể lưu role cho tài khoản Google."
                        : roleError);

                yield break;
            }

            authResponse = roleUpdatedResponse;
            existingRole = selectedRole;
        }

        // Same behavior as email/password login: the selected role must match
        // an existing account's stored role.
        if (!string.Equals(
                existingRole,
                selectedRole,
                StringComparison.OrdinalIgnoreCase))
        {
            FinishWithError(
                $"Tài khoản Google này có role '{existingRole}', " +
                $"không phải '{selectedRole}'.");

            yield break;
        }

        SupabaseSession.SaveAuthResponse(
            authResponse,
            existingRole);

        SaveLegacySessionKeys();

        SupabaseAuthService.ClearPendingGoogleOAuthState();

        Debug.Log(
            "[Google OAuth] Supabase session created successfully.\n" +
            $"Mode: {pendingMode}\n" +
            $"User ID: {SupabaseSession.UserId}\n" +
            $"Role: {SupabaseSession.Role}");

        if (!Application.CanStreamedLevelBeLoaded(
                MainHomeSceneName))
        {
            FinishWithError(
                $"Scene {MainHomeSceneName} chưa được thêm vào Build Profiles.");
            yield break;
        }

        isProcessingCallback = false;

        SceneManager.LoadScene(
            MainHomeSceneName);
    }

    private static Dictionary<string, string> ParseCallbackParameters(
        string url)
    {
        Dictionary<string, string> result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(url))
            return result;

        int queryIndex = url.IndexOf('?');
        int fragmentIndex = url.IndexOf('#');

        if (queryIndex >= 0)
        {
            int queryStart = queryIndex + 1;

            int queryLength =
                fragmentIndex > queryIndex
                    ? fragmentIndex - queryStart
                    : url.Length - queryStart;

            if (queryLength > 0)
            {
                ParseParameterSection(
                    url.Substring(
                        queryStart,
                        queryLength),
                    result);
            }
        }

        if (fragmentIndex >= 0 &&
            fragmentIndex + 1 < url.Length)
        {
            ParseParameterSection(
                url.Substring(fragmentIndex + 1),
                result);
        }

        return result;
    }

    private static void ParseParameterSection(
        string section,
        Dictionary<string, string> destination)
    {
        if (string.IsNullOrWhiteSpace(section))
            return;

        string[] pairs =
            section.Split('&');

        foreach (string pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair))
                continue;

            int equalsIndex =
                pair.IndexOf('=');

            string rawKey =
                equalsIndex >= 0
                    ? pair.Substring(0, equalsIndex)
                    : pair;

            string rawValue =
                equalsIndex >= 0 &&
                equalsIndex + 1 < pair.Length
                    ? pair.Substring(equalsIndex + 1)
                    : string.Empty;

            string key =
                UrlDecode(rawKey);

            string value =
                UrlDecode(rawValue);

            if (!string.IsNullOrWhiteSpace(key))
            {
                destination[key] = value;
            }
        }
    }

    private static string UrlDecode(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        try
        {
            return Uri.UnescapeDataString(
                value.Replace("+", " "));
        }
        catch
        {
            return value;
        }
    }

    private static string GetParameter(
        Dictionary<string, string> parameters,
        string key)
    {
        if (parameters == null ||
            string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return parameters.TryGetValue(
                key,
                out string value)
            ? value
            : string.Empty;
    }

    private static string NormalizeNullableRole(
        string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return string.Empty;

        string normalized =
            role.Trim().ToLowerInvariant();

        if (normalized == "teacher")
            return "teacher";

        if (normalized == "student")
            return "student";

        return string.Empty;
    }

    private static string NormalizeRole(string role)
    {
        return string.Equals(
                role?.Trim(),
                "teacher",
                StringComparison.OrdinalIgnoreCase)
            ? "teacher"
            : "student";
    }

    private static void SaveLegacySessionKeys()
    {
        PlayerPrefs.SetString(
            "current_user_id",
            SupabaseSession.UserId ?? string.Empty);

        PlayerPrefs.SetString(
            "current_email",
            SupabaseSession.Email ?? string.Empty);

        PlayerPrefs.SetString(
            "current_full_name",
            SupabaseSession.FullName ?? string.Empty);

        PlayerPrefs.SetString(
            "current_role",
            SupabaseSession.Role ?? string.Empty);

        PlayerPrefs.SetString(
            "current_avatar_url",
            SupabaseSession.AvatarUrl ?? string.Empty);

        // Google sign-in does not use a local password.
        PlayerPrefs.SetInt(
            "remember_login",
            1);

        PlayerPrefs.DeleteKey(
            "current_password");

        PlayerPrefs.Save();
    }

    private void FinishWithError(string message)
    {
        Debug.LogError(
            "[Google OAuth] " + message);

        PlayerPrefs.SetString(
            "google_oauth_last_error",
            message ?? "Google OAuth thất bại.");

        PlayerPrefs.Save();

        SupabaseAuthService.ClearPendingGoogleOAuthState();

        isProcessingCallback = false;
    }
}
