using System;
using System.Collections;
using UnityEngine;

public static class SupabaseProfileService
{
    public static IEnumerator GetCurrentProfile(
        Action<SupabaseProfile> onSuccess,
        Action<string> onError)
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke(
                "Không có phiên đăng nhập hợp lệ.");
            yield break;
        }

        string userId =
            Uri.EscapeDataString(
                SupabaseSession.UserId);

        string query =
            $"profiles?id=eq.{userId}&select=*";

        yield return SupabaseRestService.Get(
            query,
            json =>
            {
                if (!TryParseFirstProfile(
                        json,
                        out SupabaseProfile profile,
                        out string parseError))
                {
                    onError?.Invoke(parseError);
                    return;
                }

                SupabaseSession.SaveProfile(profile);
                onSuccess?.Invoke(profile);
            },
            onError);
    }

    public static IEnumerator UpdateCurrentProfile(
        string fullName,
        string dateOfBirth,
        Action<SupabaseProfile> onSuccess,
        Action<string> onError)
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke(
                "Không có phiên đăng nhập hợp lệ.");
            yield break;
        }

        // Built by hand so an empty date becomes JSON null
        // (an empty string is not a valid PostgreSQL date).
        string trimmedDate = dateOfBirth?.Trim() ?? string.Empty;
        string payloadJson =
            "{\"full_name\":\"" + EscapeJson(fullName?.Trim() ?? string.Empty) + "\"," +
            "\"date_of_birth\":" +
            (string.IsNullOrWhiteSpace(trimmedDate) ? "null" : "\"" + EscapeJson(trimmedDate) + "\"") +
            "}";

        string userId =
            Uri.EscapeDataString(
                SupabaseSession.UserId);

        string query =
            $"profiles?id=eq.{userId}&select=*";

        yield return SupabaseRestService.Patch(
            query,
            payloadJson,
            json =>
            {
                if (!TryParseFirstProfile(
                        json,
                        out SupabaseProfile profile,
                        out string parseError))
                {
                    onError?.Invoke(parseError);
                    return;
                }

                SupabaseSession.SaveProfile(profile);
                onSuccess?.Invoke(profile);
            },
            onError,
            true);
    }

    /// <summary>
    /// Reads the authoritative role from public.profiles (users cannot change it).
    /// For an account that has never chosen a role (first Google login) the
    /// selected role is stored once through the choose_initial_role RPC.
    /// The session must already be saved (SupabaseSession.SaveAuthResponse).
    /// </summary>
    public static IEnumerator SyncRoleFromProfile(
        string selectedRole,
        Action<string> onRole,
        Action<string> onError)
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke("Không có phiên đăng nhập hợp lệ.");
            yield break;
        }

        string normalizedRole =
            selectedRole?.Trim().ToLowerInvariant() == "teacher"
                ? "teacher"
                : "student";

        string rpcError = null;

        yield return SupabaseRestService.Post(
            "rpc/choose_initial_role",
            "{\"p_role\":\"" + normalizedRole + "\"}",
            _ => { },
            error => rpcError = error,
            false);

        if (!string.IsNullOrWhiteSpace(rpcError))
        {
            // Not fatal: the profile read below still returns the real role.
            Debug.LogWarning("[SupabaseProfileService] choose_initial_role failed: " + rpcError);
        }

        SupabaseProfile profile = null;
        string profileError = null;

        yield return GetCurrentProfile(
            value => profile = value,
            error => profileError = error);

        if (profile == null)
        {
            onError?.Invoke(string.IsNullOrWhiteSpace(profileError)
                ? "Không đọc được profile."
                : profileError);
            yield break;
        }

        string profileRole = profile.role?.Trim().ToLowerInvariant();
        onRole?.Invoke(
            profileRole == "admin"
                ? "admin"
                : profileRole == "teacher"
                    ? "teacher"
                    : "student");
    }

    private static string EscapeJson(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private static bool TryParseFirstProfile(
        string json,
        out SupabaseProfile profile,
        out string error)
    {
        profile = null;

        string trimmed =
            json?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed == "[]")
        {
            error = "Profile không tồn tại.";
            return false;
        }

        try
        {
            string wrapped =
                "{\"items\":" + trimmed + "}";

            SupabaseProfileArrayWrapper wrapper =
                JsonUtility.FromJson<SupabaseProfileArrayWrapper>(
                    wrapped);

            if (wrapper?.items == null ||
                wrapper.items.Length == 0)
            {
                error =
                    "Không đọc được profile từ Supabase.";
                return false;
            }

            profile = wrapper.items[0];
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error =
                "Không parse được profile: " +
                exception.Message;

            return false;
        }
    }
}
