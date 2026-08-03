// using System;
// using System.Collections;
// using System.Text;
// using UnityEngine;
// using UnityEngine.Networking;

// /// <summary>
// /// REST service dành cho các MonoBehaviour runtime như:
// /// SupabaseLessonService và SupabaseStorageService.
// ///
// /// Toàn bộ cấu hình được dùng chung từ:
// /// - SupabaseConfig
// /// - SupabaseSession
// ///
// /// Vì vậy không cần nhập lại Project URL hoặc Publishable Key
// /// trong Inspector của từng Scene.
// /// </summary>
// public class SupabaseRuntimeRestService : MonoBehaviour
// {
//     public string ProjectUrl =>
//         SupabaseConfig.ProjectUrl?.TrimEnd('/') ?? string.Empty;

//     public string AnonKey =>
//         SupabaseConfig.PublishableKey ?? string.Empty;

//     public string AccessToken =>
//         SupabaseSession.AccessToken ?? string.Empty;

//     private void Awake()
//     {
//         if (!IsConfigured(out string error))
//         {
//             Debug.LogError(
//                 $"[SupabaseRuntimeRestService] {error}"
//             );
//         }
//     }

//     public bool IsConfigured(out string error)
//     {
//         if (!SupabaseConfig.TryValidate(out string configError))
//         {
//             error = configError;
//             return false;
//         }

//         if (!SupabaseSession.IsLoggedIn)
//         {
//             error =
//                 "Không có phiên đăng nhập Supabase hợp lệ. " +
//                 "Hãy đăng nhập lại trước khi gọi REST API.";
//             return false;
//         }

//         if (string.IsNullOrWhiteSpace(AccessToken))
//         {
//             error =
//                 "Supabase access token đang trống. " +
//                 "Hãy đăng nhập lại.";
//             return false;
//         }

//         error = string.Empty;
//         return true;
//     }

//     public IEnumerator SendJson(
//         string method,
//         string relativeUrl,
//         string jsonBody,
//         string preferHeader,
//         Action<string> onSuccess,
//         Action<string> onError)
//     {
//         if (!IsConfigured(out string configError))
//         {
//             onError?.Invoke(configError);
//             yield break;
//         }

//         if (string.IsNullOrWhiteSpace(method))
//         {
//             onError?.Invoke("HTTP method đang trống.");
//             yield break;
//         }

//         string requestUrl = BuildRequestUrl(relativeUrl);

//         using UnityWebRequest request =
//             new UnityWebRequest(requestUrl, method);

//         request.timeout =
//             SupabaseConfig.RequestTimeoutSeconds;

//         request.downloadHandler =
//             new DownloadHandlerBuffer();

//         if (!string.IsNullOrWhiteSpace(jsonBody))
//         {
//             byte[] body =
//                 Encoding.UTF8.GetBytes(jsonBody);

//             request.uploadHandler =
//                 new UploadHandlerRaw(body);

//             request.SetRequestHeader(
//                 "Content-Type",
//                 "application/json");
//         }

//         ApplyAuthHeaders(request);

//         request.SetRequestHeader(
//             "Accept",
//             "application/json");

//         if (!string.IsNullOrWhiteSpace(preferHeader))
//         {
//             request.SetRequestHeader(
//                 "Prefer",
//                 preferHeader.Trim());
//         }

//         yield return request.SendWebRequest();

//         string responseText =
//             request.downloadHandler?.text ??
//             string.Empty;

//         if (request.result !=
//             UnityWebRequest.Result.Success)
//         {
//             string message =
//                 BuildErrorMessage(
//                     request,
//                     responseText);

//             Debug.LogError(
//                 "[SupabaseRuntimeRestService]\n" +
//                 message +
//                 $"\nMethod: {method}" +
//                 $"\nURL: {requestUrl}" +
//                 $"\nBody: {jsonBody}");

//             onError?.Invoke(message);
//             yield break;
//         }

//         onSuccess?.Invoke(responseText);
//     }

//     public void ApplyAuthHeaders(
//         UnityWebRequest request)
//     {
//         if (request == null)
//         {
//             Debug.LogError(
//                 "[SupabaseRuntimeRestService] " +
//                 "UnityWebRequest đang null.");
//             return;
//         }

//         request.SetRequestHeader(
//             "apikey",
//             SupabaseConfig.PublishableKey);

//         request.SetRequestHeader(
//             "Authorization",
//             $"Bearer {SupabaseSession.AccessToken}");
//     }

//     private static string BuildRequestUrl(
//         string relativeUrl)
//     {
//         string baseUrl =
//             SupabaseConfig.ProjectUrl.TrimEnd('/');

//         string path =
//             string.IsNullOrWhiteSpace(relativeUrl)
//                 ? string.Empty
//                 : relativeUrl.TrimStart('/');

//         return string.IsNullOrWhiteSpace(path)
//             ? baseUrl
//             : $"{baseUrl}/{path}";
//     }

//     private static string BuildErrorMessage(
//         UnityWebRequest request,
//         string responseText)
//     {
//         if (!string.IsNullOrWhiteSpace(responseText))
//         {
//             try
//             {
//                 SupabaseRuntimeErrorResponse error =
//                     JsonUtility.FromJson<
//                         SupabaseRuntimeErrorResponse>(
//                         responseText);

//                 if (error != null)
//                 {
//                     if (!string.IsNullOrWhiteSpace(
//                             error.message))
//                     {
//                         return error.message;
//                     }

//                     if (!string.IsNullOrWhiteSpace(
//                             error.msg))
//                     {
//                         return error.msg;
//                     }

//                     if (!string.IsNullOrWhiteSpace(
//                             error.details))
//                     {
//                         return error.details;
//                     }

//                     if (!string.IsNullOrWhiteSpace(
//                             error.hint))
//                     {
//                         return error.hint;
//                     }
//                 }
//             }
//             catch
//             {
//                 // Nếu response không khớp JSON dự kiến,
//                 // trả nguyên nội dung từ Supabase.
//             }

//             return responseText;
//         }

//         if (!string.IsNullOrWhiteSpace(request.error))
//         {
//             return request.error;
//         }

//         return
//             $"Supabase request thất bại " +
//             $"({request.responseCode}).";
//     }

//     [Serializable]
//     private class SupabaseRuntimeErrorResponse
//     {
//         public string code;
//         public string message;
//         public string msg;
//         public string details;
//         public string hint;
//     }
// }

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class SupabaseRuntimeRestService : MonoBehaviour
{
    public string ProjectUrl => SupabaseConfig.ProjectUrl?.TrimEnd('/') ?? string.Empty;
    public string AnonKey => SupabaseConfig.PublishableKey ?? string.Empty;
    public string AccessToken => SupabaseSession.AccessToken ?? string.Empty;

    private void Awake()
    {
        if (!IsConfigured(out string error))
        {
            Debug.LogError($"[SupabaseRuntimeRestService] {error}");
        }
    }

    public bool IsConfigured(out string error)
    {
        if (!SupabaseConfig.TryValidate(out string configError))
        {
            error = configError;
            return false;
        }

        if (!SupabaseSession.IsLoggedIn)
        {
            error = "Không có phiên đăng nhập Supabase hợp lệ. Hãy đăng nhập lại.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            error = "Supabase access token đang trống. Hãy đăng nhập lại.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public IEnumerator SendJson(
        string method,
        string relativeUrl,
        string jsonBody,
        string preferHeader,
        Action<string> onSuccess,
        Action<string> onError)
    {
        if (!IsConfigured(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        string requestUrl = BuildRequestUrl(relativeUrl);

        using UnityWebRequest request = new UnityWebRequest(requestUrl, method);
        request.timeout = SupabaseConfig.RequestTimeoutSeconds;
        request.downloadHandler = new DownloadHandlerBuffer();

        if (!string.IsNullOrWhiteSpace(jsonBody))
        {
            byte[] body = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.SetRequestHeader("Content-Type", "application/json");
        }

        ApplyAuthHeaders(request);
        request.SetRequestHeader("Accept", "application/json");

        if (!string.IsNullOrWhiteSpace(preferHeader))
        {
            request.SetRequestHeader("Prefer", preferHeader.Trim());
        }

        yield return request.SendWebRequest();

        string responseText = request.downloadHandler?.text ?? string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string message = BuildErrorMessage(request, responseText);
            Debug.LogError($"[SupabaseRuntimeRestService]\n{message}\nMethod: {method}\nURL: {requestUrl}");
            onError?.Invoke(message);
            yield break;
        }

        onSuccess?.Invoke(responseText);
    }

    public void ApplyAuthHeaders(UnityWebRequest request)
    {
        if (request == null) return;
        request.SetRequestHeader("apikey", SupabaseConfig.PublishableKey);
        request.SetRequestHeader("Authorization", $"Bearer {SupabaseSession.AccessToken}");
    }

    private static string BuildRequestUrl(string relativeUrl)
    {
        string baseUrl = SupabaseConfig.ProjectUrl.TrimEnd('/');
        string path = string.IsNullOrWhiteSpace(relativeUrl) ? string.Empty : relativeUrl.TrimStart('/');
        return string.IsNullOrWhiteSpace(path) ? baseUrl : $"{baseUrl}/{path}";
    }

    private static string BuildErrorMessage(UnityWebRequest request, string responseText)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                SupabaseRuntimeErrorResponse error = JsonUtility.FromJson<SupabaseRuntimeErrorResponse>(responseText);
                if (error != null)
                {
                    if (!string.IsNullOrWhiteSpace(error.message)) return error.message;
                    if (!string.IsNullOrWhiteSpace(error.msg)) return error.msg;
                }
            }
            catch { }
            return responseText;
        }

        return request.error ?? $"Supabase request thất bại ({request.responseCode}).";
    }

    [Serializable]
    private class SupabaseRuntimeErrorResponse
    {
        public string code;
        public string message;
        public string msg;
    }
}