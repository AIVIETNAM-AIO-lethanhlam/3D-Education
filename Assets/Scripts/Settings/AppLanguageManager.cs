using System;
using UnityEngine;

/// <summary>
/// Central language state for the whole app.
/// Store only the language code (EN / VI) here.
/// Individual scenes/controllers use T(...) or subscribe to LanguageChanged.
/// </summary>
public static class AppLanguageManager
{
    private const string LanguageKey = "app_language";

    public static event Action<string> LanguageChanged;

    public static string CurrentLanguage =>
        NormalizeLanguage(
            PlayerPrefs.GetString(LanguageKey, "EN"));

    public static bool IsVietnamese =>
        CurrentLanguage == "VI";

    public static void SetLanguage(string language)
    {
        string normalized =
            NormalizeLanguage(language);

        PlayerPrefs.SetString(
            LanguageKey,
            normalized);

        PlayerPrefs.Save();

        LanguageChanged?.Invoke(
            normalized);
    }

    public static string T(
        string english,
        string vietnamese)
    {
        return IsVietnamese
            ? vietnamese
            : english;
    }

    private static string NormalizeLanguage(
        string language)
    {
        return string.Equals(
                language,
                "VI",
                StringComparison.OrdinalIgnoreCase)
            ? "VI"
            : "EN";
    }
}
