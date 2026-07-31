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

        UpdateProfileRequest payload =
            new UpdateProfileRequest
            {
                full_name =
                    fullName?.Trim() ?? string.Empty,

                date_of_birth =
                    dateOfBirth?.Trim() ?? string.Empty
            };

        string userId =
            Uri.EscapeDataString(
                SupabaseSession.UserId);

        string query =
            $"profiles?id=eq.{userId}&select=*";

        yield return SupabaseRestService.Patch(
            query,
            JsonUtility.ToJson(payload),
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
