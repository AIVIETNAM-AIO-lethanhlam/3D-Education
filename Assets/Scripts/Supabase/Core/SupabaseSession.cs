using UnityEngine;

public static class SupabaseSession
{
    private const string AccessTokenKey = "access_token";
    private const string RefreshTokenKey = "refresh_token";
    private const string UserIdKey = "user_id";
    private const string EmailKey = "email";
    private const string FullNameKey = "full_name";
    private const string RoleKey = "current_role";
    private const string AvatarUrlKey = "avatar_url";
    private const string LoggedInKey = "is_logged_in";

    public static string AccessToken =>
        PlayerPrefs.GetString(AccessTokenKey, string.Empty);

    public static string RefreshToken =>
        PlayerPrefs.GetString(RefreshTokenKey, string.Empty);

    public static string UserId =>
        PlayerPrefs.GetString(UserIdKey, string.Empty);

    public static string Email =>
        PlayerPrefs.GetString(EmailKey, string.Empty);

    public static string FullName =>
        PlayerPrefs.GetString(FullNameKey, string.Empty);

    public static string Role =>
        PlayerPrefs.GetString(RoleKey, string.Empty);

    public static string AvatarUrl =>
        PlayerPrefs.GetString(AvatarUrlKey, string.Empty);

    public static bool IsLoggedIn =>
        PlayerPrefs.GetInt(LoggedInKey, 0) == 1 &&
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(UserId);

    public static bool IsTeacher =>
        Role.Trim().ToLowerInvariant() == "teacher";

    public static bool IsStudent =>
        Role.Trim().ToLowerInvariant() == "student";

    public static void SaveAuthResponse(
        SupabaseAuthResponse response,
        string fallbackRole = "")
    {
        if (response == null || response.user == null)
        {
            Debug.LogError(
                "Không thể lưu session vì auth response không hợp lệ.");
            return;
        }

        SupabaseUserMetadata metadata =
            response.user.user_metadata;

        string role =
            metadata != null &&
            !string.IsNullOrWhiteSpace(metadata.role)
                ? metadata.role
                : fallbackRole;

        PlayerPrefs.SetString(
            AccessTokenKey,
            response.access_token ?? string.Empty);

        PlayerPrefs.SetString(
            RefreshTokenKey,
            response.refresh_token ?? string.Empty);

        PlayerPrefs.SetString(
            UserIdKey,
            response.user.id ?? string.Empty);

        PlayerPrefs.SetString(
            EmailKey,
            response.user.email ?? string.Empty);

        PlayerPrefs.SetString(
            FullNameKey,
            metadata?.full_name ?? string.Empty);

        PlayerPrefs.SetString(
            RoleKey,
            NormalizeRole(role));

        PlayerPrefs.SetString(
            AvatarUrlKey,
            metadata?.avatar_url ?? string.Empty);

        PlayerPrefs.SetInt(
            LoggedInKey,
            string.IsNullOrWhiteSpace(response.access_token)
                ? 0
                : 1);

        PlayerPrefs.Save();
    }

    public static void SaveProfile(
        SupabaseProfile profile)
    {
        if (profile == null)
        {
            return;
        }

        PlayerPrefs.SetString(
            UserIdKey,
            profile.id ?? UserId);

        PlayerPrefs.SetString(
            EmailKey,
            profile.email ?? Email);

        PlayerPrefs.SetString(
            FullNameKey,
            profile.full_name ?? string.Empty);

        PlayerPrefs.SetString(
            RoleKey,
            NormalizeRole(profile.role));

        PlayerPrefs.SetString(
            AvatarUrlKey,
            profile.avatar_url ?? string.Empty);

        PlayerPrefs.Save();
    }

    public static void UpdateFullName(string fullName)
    {
        PlayerPrefs.SetString(
            FullNameKey,
            fullName?.Trim() ?? string.Empty);

        PlayerPrefs.Save();
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(AccessTokenKey);
        PlayerPrefs.DeleteKey(RefreshTokenKey);
        PlayerPrefs.DeleteKey(UserIdKey);
        PlayerPrefs.DeleteKey(EmailKey);
        PlayerPrefs.DeleteKey(FullNameKey);
        PlayerPrefs.DeleteKey(RoleKey);
        PlayerPrefs.DeleteKey(AvatarUrlKey);
        PlayerPrefs.DeleteKey(LoggedInKey);
        PlayerPrefs.Save();
    }

    private static string NormalizeRole(string role)
    {
        return role?.Trim().ToLowerInvariant() == "teacher"
            ? "teacher"
            : "student";
    }
}
