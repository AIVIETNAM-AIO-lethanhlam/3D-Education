using System.Text.RegularExpressions;
using UnityEngine;

public static class ModelNameFormatter
{
    public static string Format(string rawName)
    {
        if (string.IsNullOrEmpty(rawName))
            return "";

        string name = rawName;

        // bỏ (Clone)
        name = name.Replace("(Clone)", "");

        // bỏ (1), (2)...
        name = Regex.Replace(name, @"\(\d+\)", "");

        // bỏ _ và -
        name = name.Replace("_", " ");
        name = name.Replace("-", " ");

        // bỏ khoảng trắng thừa
        name = Regex.Replace(name, @"\s+", " ").Trim();

        // Viết hoa chữ đầu mỗi từ
        name = System.Globalization.CultureInfo
            .CurrentCulture
            .TextInfo
            .ToTitleCase(name.ToLower());

        return name;
    }
}