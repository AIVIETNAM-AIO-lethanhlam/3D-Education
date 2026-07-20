using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class SupabaseAuthService
{
    private const int RequestTimeoutSeconds = 30;

    public static IEnumerator SignUp(
        string fullName,
        string email,
        string password,
        string role,
        Action<SupabaseSignUpResponse> onSuccess,
        Action<string> onError)
    {
        string normalizedName = NormalizePlainText(fullName);
        string normalizedEmail = NormalizeEmail(email);
        string normalizedRole = NormalizeRole(role);

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            onError?.Invoke("Full name is required.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            onError?.Invoke("Email is required.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            onError?.Invoke("Password must contain at least 6 characters.");
            yield break;
        }

        if (!TryGetSupabaseConfiguration(
                out string supabaseUrl,
                out string publishableKey,
                out string configError))
        {
            Debug.LogError(configError);
            onError?.Invoke(configError);
            yield break;
        }

        SignUpRequestPayload payload = new SignUpRequestPayload
        {
            email = normalizedEmail,
            password = password,
            data = new SignUpMetadataPayload
            {
                full_name = normalizedName,
                display_name = normalizedName,
                role = normalizedRole
            }
        };

        string requestJson = JsonUtility.ToJson(payload);
        string requestUrl = $"{supabaseUrl.TrimEnd('/')}/auth/v1/signup";

        using UnityWebRequest request =
            new UnityWebRequest(requestUrl, UnityWebRequest.kHttpVerbPOST);

        request.uploadHandler =
            new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));

        request.downloadHandler =
            new DownloadHandlerBuffer();

        request.timeout = RequestTimeoutSeconds;

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");
        request.SetRequestHeader("apikey", publishableKey);

        Debug.Log(
            "Supabase sign-up request\n" +
            $"URL: {requestUrl}\n" +
            $"Email: >{normalizedEmail}<\n" +
            $"Role: {normalizedRole}"
        );

        yield return request.SendWebRequest();

        string responseText =
            request.downloadHandler?.text ?? string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorMessage =
                ExtractErrorMessage(responseText, request.error);

            Debug.LogError(
                "Supabase sign-up failed\n" +
                $"HTTP status: {request.responseCode}\n" +
                $"Unity error: {request.error}\n" +
                $"Response: {responseText}"
            );

            onError?.Invoke(errorMessage);
            yield break;
        }

        SupabaseSignUpResponse response;

        try
        {
            response =
                JsonUtility.FromJson<SupabaseSignUpResponse>(
                    responseText
                );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Không thể parse response đăng ký từ Supabase.\n" +
                $"Response: {responseText}\n" +
                exception
            );

            onError?.Invoke(
                "Supabase trả về dữ liệu không hợp lệ."
            );

            yield break;
        }

        if (response == null || response.user == null)
        {
            Debug.LogError(
                $"Supabase response không có user: {responseText}"
            );

            onError?.Invoke(
                "Không nhận được thông tin người dùng từ Supabase."
            );

            yield break;
        }

        onSuccess?.Invoke(response);
    }

    private static bool TryGetSupabaseConfiguration(
        out string supabaseUrl,
        out string publishableKey,
        out string error)
    {
        supabaseUrl = GetStaticStringFromConfig(
            "ProjectUrl",
            "SupabaseUrl",
            "SUPABASE_URL",
            "Url",
            "BaseUrl"
        );

        publishableKey = GetStaticStringFromConfig(
            "PublishableKey",
            "SupabasePublishableKey",
            "SUPABASE_PUBLISHABLE_KEY",
            "SupabaseAnonKey",
            "SUPABASE_ANON_KEY",
            "AnonKey",
            "PublicAnonKey",
            "ApiKey"
        );

        if (string.IsNullOrWhiteSpace(supabaseUrl))
        {
            error =
                "Không đọc được Supabase URL từ SupabaseConfig.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(publishableKey))
        {
            error =
                "Không đọc được Supabase publishable key từ SupabaseConfig.";
            return false;
        }

        supabaseUrl = supabaseUrl.Trim();
        publishableKey = publishableKey.Trim();

        if (!supabaseUrl.StartsWith(
                "https://",
                StringComparison.OrdinalIgnoreCase))
        {
            error =
                "Supabase URL không hợp lệ.";
            return false;
        }

        error = null;
        return true;
    }

    private static string GetStaticStringFromConfig(
        params string[] candidateNames)
    {
        Type configType =
            FindTypeInLoadedAssemblies("SupabaseConfig");

        if (configType == null)
            return null;

        const BindingFlags flags =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Static;

        foreach (string candidateName in candidateNames)
        {
            FieldInfo field =
                configType.GetField(candidateName, flags);

            if (field != null &&
                field.FieldType == typeof(string))
            {
                return field.GetValue(null) as string;
            }

            PropertyInfo property =
                configType.GetProperty(candidateName, flags);

            if (property != null &&
                property.PropertyType == typeof(string) &&
                property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(null, null) as string;
            }
        }

        return null;
    }

    private static Type FindTypeInLoadedAssemblies(
        string typeName)
    {
        foreach (Assembly assembly
                 in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type directType = assembly.GetType(typeName);

            if (directType != null)
                return directType;

            try
            {
                foreach (Type candidate in assembly.GetTypes())
                {
                    if (candidate.Name == typeName)
                        return candidate;
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // Bỏ qua assembly không đọc được toàn bộ type.
            }
        }

        return null;
    }

    private static string ExtractErrorMessage(
        string responseText,
        string unityError)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                SignUpErrorPayload errorPayload =
                    JsonUtility.FromJson<SignUpErrorPayload>(
                        responseText
                    );

                if (errorPayload != null)
                {
                    if (!string.IsNullOrWhiteSpace(errorPayload.msg))
                        return errorPayload.msg;

                    if (!string.IsNullOrWhiteSpace(errorPayload.message))
                        return errorPayload.message;

                    if (!string.IsNullOrWhiteSpace(
                            errorPayload.error_description))
                    {
                        return errorPayload.error_description;
                    }

                    if (!string.IsNullOrWhiteSpace(errorPayload.error))
                        return errorPayload.error;
                }
            }
            catch
            {
                // Trả nguyên response nếu không parse được.
            }

            return responseText;
        }

        return string.IsNullOrWhiteSpace(unityError)
            ? "Đăng ký Supabase thất bại."
            : unityError;
    }

    private static string NormalizeEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Trim()
            .ToLowerInvariant()
            .Replace("\u200B", string.Empty)
            .Replace("\u200C", string.Empty)
            .Replace("\u200D", string.Empty)
            .Replace("\u2060", string.Empty)
            .Replace("\uFEFF", string.Empty);
    }

    private static string NormalizePlainText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Trim()
            .Replace("\u200B", string.Empty)
            .Replace("\u200C", string.Empty)
            .Replace("\u200D", string.Empty)
            .Replace("\u2060", string.Empty)
            .Replace("\uFEFF", string.Empty);
    }

    private static string NormalizeRole(string role)
    {
        string normalizedRole =
            NormalizePlainText(role).ToLowerInvariant();

        return normalizedRole == "teacher"
            ? "teacher"
            : "student";
    }

    [Serializable]
    private sealed class SignUpRequestPayload
    {
        public string email;
        public string password;
        public SignUpMetadataPayload data;
    }

    [Serializable]
    private sealed class SignUpMetadataPayload
    {
        public string full_name;
        public string display_name;
        public string role;
    }

    [Serializable]
    private sealed class SignUpErrorPayload
    {
        public string error;
        public string error_description;
        public string message;
        public string msg;
        public string code;
    }
}