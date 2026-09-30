using System.Collections.Generic;

/// <summary>
/// Password rules for new accounts and password changes (2026-09):
///  - at least 8 characters
///  - at least 1 uppercase letter
///  - contains both letters and numbers
/// Existing accounts can still sign in with their old password; the rules
/// are checked when a password is created or changed.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public struct Result
    {
        public bool HasLength;
        public bool HasUppercase;
        public bool HasLetter;
        public bool HasNumber;

        public bool HasLettersAndNumbers => HasLetter && HasNumber;
        public bool IsValid => HasLength && HasUppercase && HasLettersAndNumbers;
    }

    public static Result Evaluate(string password)
    {
        Result result = new Result();
        string value = password ?? string.Empty;

        result.HasLength = value.Length >= MinLength;
        foreach (char c in value)
        {
            if (char.IsLetter(c)) result.HasLetter = true;
            if (char.IsUpper(c)) result.HasUppercase = true;
            if (char.IsDigit(c)) result.HasNumber = true;
        }
        return result;
    }

    public static bool IsValid(string password) => Evaluate(password).IsValid;

    /// <summary>Message describing every rule that is not met yet (current app language).</summary>
    public static string ErrorMessage(string password)
    {
        Result r = Evaluate(password);
        if (r.IsValid) return string.Empty;

        List<string> missing = new List<string>();
        if (!r.HasLength) missing.Add(AppLanguageManager.T($"at least {MinLength} characters", $"ít nhất {MinLength} ký tự"));
        if (!r.HasUppercase) missing.Add(AppLanguageManager.T("an uppercase letter", "1 chữ in hoa"));
        if (!r.HasLettersAndNumbers) missing.Add(AppLanguageManager.T("both letters and numbers", "cả chữ và số"));

        return AppLanguageManager.T("Password needs ", "Mật khẩu cần có ") + string.Join(", ", missing) + ".";
    }

    public static string RuleLengthText => AppLanguageManager.T($"{MinLength}+ characters", $"Ít nhất {MinLength} ký tự");
    public static string RuleUppercaseText => AppLanguageManager.T("1 uppercase letter", "Có chữ in hoa");
    public static string RuleLettersNumbersText => AppLanguageManager.T("Letters and numbers", "Có cả chữ và số");
}
