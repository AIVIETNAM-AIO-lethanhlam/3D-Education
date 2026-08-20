using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Lưu thời điểm người dùng mở từng lớp trên thiết bị.
/// Dùng để sắp xếp "Recently Viewed" theo tương tác gần nhất.
/// </summary>
public static class ClassInteractionHistory
{
    private const string KeyPrefix =
        "class_last_interaction_";

    public static void Record(string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
            return;

        PlayerPrefs.SetString(
            KeyPrefix + classId.Trim(),
            DateTime.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture
            )
        );

        PlayerPrefs.Save();
    }

    public static DateTime GetLastInteraction(
        string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
            return DateTime.MinValue;

        string value = PlayerPrefs.GetString(
            KeyPrefix + classId.Trim(),
            string.Empty
        );

        if (string.IsNullOrWhiteSpace(value))
            return DateTime.MinValue;

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTime parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;
    }

    public static void Remove(string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
            return;

        PlayerPrefs.DeleteKey(
            KeyPrefix + classId.Trim()
        );
    }
}
