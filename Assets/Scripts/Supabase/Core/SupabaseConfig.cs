using System;

public static class SupabaseConfig
{
    public const string ProjectUrl =
        "https://nfribubvehdzjyguxejq.supabase.co";

    public const string PublishableKey =
        "sb_publishable_1QTg4NVH-lYBBAt1qOQHYw_th1pWtIf";

    public const int RequestTimeoutSeconds = 30;

    public static string AuthUrl =>
        ProjectUrl.TrimEnd('/') + "/auth/v1";

    public static string RestUrl =>
        ProjectUrl.TrimEnd('/') + "/rest/v1";

    public static bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(ProjectUrl))
        {
            error = "Supabase Project URL chưa được cấu hình.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(PublishableKey))
        {
            error = "Supabase Publishable Key chưa được cấu hình.";
            return false;
        }

        if (!Uri.TryCreate(
                ProjectUrl,
                UriKind.Absolute,
                out Uri parsedUri) ||
            parsedUri.Scheme != Uri.UriSchemeHttps)
        {
            error =
                "Supabase Project URL không hợp lệ hoặc không dùng HTTPS.";
            return false;
        }

        error = null;
        return true;
    }
}
