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
        var payload = new SignUpRequestPayload
        {
            email = NormalizeEmail(email),
            password = password,
            data = new SignUpMetadataPayload
            {
                full_name = NormalizePlainText(fullName),
                display_name = NormalizePlainText(fullName),
                role = NormalizeRole(role)
            }
        };

        yield return SendAuthRequest(
            "/auth/v1/signup",
            JsonUtility.ToJson(payload),
            onSuccess,
            onError
        );
    }

    public static IEnumerator SignIn(
        string email,
        string password,
        Action<SupabaseSignUpResponse> onSuccess,
        Action<string> onError)
    {
        var payload = new SignInRequestPayload
        {
            email = NormalizeEmail(email),
            password = password
        };

        yield return SendAuthRequest(
            "/auth/v1/token?grant_type=password",
            JsonUtility.ToJson(payload),
            onSuccess,
            onError
        );
    }

    private static IEnumerator SendAuthRequest(
        string endpoint,
        string requestJson,
        Action<SupabaseSignUpResponse> onSuccess,
        Action<string> onError)
    {
        if (!TryGetSupabaseConfiguration(
                out string supabaseUrl,
                out string publishableKey,
                out string configError))
        {
            Debug.LogError(configError);
            onError?.Invoke(configError);
            yield break;
        }

        string requestUrl = supabaseUrl.TrimEnd('/') + endpoint;

        using (UnityWebRequest request =
               new UnityWebRequest(requestUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler =
                new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));

            request.downloadHandler =
                new DownloadHandlerBuffer();

            request.timeout = RequestTimeoutSeconds;

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("apikey", publishableKey);

            yield return request.SendWebRequest();

            string responseText =
                request.downloadHandler != null
                    ? request.downloadHandler.text
                    : string.Empty;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorMessage =
                    ExtractErrorMessage(responseText, request.error);

                Debug.LogError(
                    "Supabase request failed\n" +
                    $"URL: {requestUrl}\n" +
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
                    "Không thể parse Supabase response.\n" +
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
                onError?.Invoke(
                    "Không nhận được thông tin người dùng từ Supabase."
                );
                yield break;
            }

            onSuccess?.Invoke(response);
        }
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
                var payload =
                    JsonUtility.FromJson<AuthErrorPayload>(
                        responseText
                    );

                if (payload != null)
                {
                    if (!string.IsNullOrWhiteSpace(payload.msg))
                        return payload.msg;

                    if (!string.IsNullOrWhiteSpace(payload.message))
                        return payload.message;

                    if (!string.IsNullOrWhiteSpace(
                            payload.error_description))
                    {
                        return payload.error_description;
                    }

                    if (!string.IsNullOrWhiteSpace(payload.error))
                        return payload.error;
                }
            }
            catch
            {
                // Nếu parse thất bại thì trả nguyên response.
            }

            return responseText;
        }

        return string.IsNullOrWhiteSpace(unityError)
            ? "Yêu cầu xác thực thất bại."
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
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string NormalizeRole(string role)
    {
        return NormalizePlainText(role)
                   .ToLowerInvariant() == "teacher"
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
    private sealed class SignInRequestPayload
    {
        public string email;
        public string password;
    }

    [Serializable]
    private sealed class SignUpMetadataPayload
    {
        public string full_name;
        public string display_name;
        public string role;
    }

    [Serializable]
    private sealed class AuthErrorPayload
    {
        public string error;
        public string error_description;
        public string message;
        public string msg;
        public string code;
    }
}