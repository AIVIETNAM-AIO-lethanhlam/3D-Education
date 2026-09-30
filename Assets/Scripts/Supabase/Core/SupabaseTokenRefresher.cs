using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Keeps the Supabase access token valid (2026-09).
///
/// - EnsureFreshToken(): call before a request; refreshes the token with the
///   stored refresh_token when it expires within RefreshMarginSeconds.
/// - RefreshSession(): forces a refresh (used after an HTTP 401 and by the splash
///   screen for auto-login).
/// - A hidden DontDestroyOnLoad "keeper" object checks the token every 30 s and
///   when the app returns from background, so every scene (including code that
///   reads SupabaseSession.AccessToken directly) gets a valid token.
///
/// Only one refresh runs at a time, because Supabase rotates refresh tokens and
/// reusing an old one in parallel could revoke the session.
/// </summary>
public static class SupabaseTokenRefresher
{
    public const int RefreshMarginSeconds = 120;

    private static bool isRefreshing;
    private static bool lastRefreshSucceeded;
    private static bool lastRefreshWasRejected;

    /// <summary>True when the last refresh failed because Supabase rejected the refresh token.</summary>
    public static bool LastRefreshWasRejected => lastRefreshWasRejected;

    public static bool NeedsRefresh
    {
        get
        {
            if (!SupabaseSession.IsLoggedIn) return false;
            long exp = GetTokenExpiryUnixSeconds(SupabaseSession.AccessToken);
            if (exp <= 0) return false; // unknown format -> let the server decide
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return exp - now <= RefreshMarginSeconds;
        }
    }

    public static IEnumerator EnsureFreshToken(bool force = false)
    {
        if (!force && !NeedsRefresh) yield break;
        yield return RefreshSession(null);
    }

    public static IEnumerator RefreshSession(Action<bool> onDone)
    {
        // Another coroutine is already refreshing: wait for its result.
        if (isRefreshing)
        {
            while (isRefreshing) yield return null;
            onDone?.Invoke(lastRefreshSucceeded);
            yield break;
        }

        string refreshToken = SupabaseSession.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            lastRefreshSucceeded = false;
            lastRefreshWasRejected = true;
            onDone?.Invoke(false);
            yield break;
        }

        isRefreshing = true;
        lastRefreshSucceeded = false;
        lastRefreshWasRejected = false;

        string url = SupabaseConfig.AuthUrl + "/token?grant_type=refresh_token";
        string body = "{\"refresh_token\":\"" + EscapeJson(refreshToken) + "\"}";

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = SupabaseConfig.RequestTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("apikey", SupabaseConfig.PublishableKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                SupabaseAuthResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<SupabaseAuthResponse>(request.downloadHandler.text);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[SupabaseTokenRefresher] Cannot parse refresh response: " + exception.Message);
                }

                if (response != null && !string.IsNullOrWhiteSpace(response.access_token))
                {
                    SupabaseSession.UpdateTokens(response.access_token, response.refresh_token);
                    lastRefreshSucceeded = true;
                    Debug.Log("[SupabaseTokenRefresher] Access token refreshed.");
                }
            }
            else
            {
                // 400/401 = refresh token invalid or revoked -> user must log in again.
                lastRefreshWasRejected = request.responseCode == 400 || request.responseCode == 401;
                Debug.LogWarning(
                    $"[SupabaseTokenRefresher] Refresh failed ({request.responseCode}): " +
                    (request.downloadHandler?.text ?? request.error));
            }
        }

        isRefreshing = false;
        onDone?.Invoke(lastRefreshSucceeded);
    }

    /// <summary>Reads the "exp" claim of a JWT. Returns 0 when it cannot be read.</summary>
    public static long GetTokenExpiryUnixSeconds(string jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return 0;
        string[] parts = jwt.Split('.');
        if (parts.Length < 2) return 0;

        try
        {
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            string json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            JwtPayload data = JsonUtility.FromJson<JwtPayload>(json);
            return data != null ? data.exp : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string EscapeJson(string value)
    {
        return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    [Serializable]
    private class JwtPayload
    {
        public long exp;
    }

    // ------------------------------------------------------------------
    // Background keeper
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateKeeper()
    {
        if (UnityEngine.Object.FindAnyObjectByType<SupabaseSessionKeeper>() != null) return;
        GameObject keeper = new GameObject("SupabaseSessionKeeper");
        keeper.hideFlags = HideFlags.HideInHierarchy;
        UnityEngine.Object.DontDestroyOnLoad(keeper);
        keeper.AddComponent<SupabaseSessionKeeper>();
    }
}
