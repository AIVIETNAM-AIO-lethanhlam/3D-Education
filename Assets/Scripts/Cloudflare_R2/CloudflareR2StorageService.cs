using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Uploads files to Cloudflare R2 WITHOUT keeping R2 access keys in the app.
///
/// Flow (2026-09 security update):
/// 1. Ask the Supabase Edge Function "r2-upload-url" for a presigned PUT URL.
///    The function checks that the caller is a teacher, that the object key
///    starts with the caller's user id and that the caller teaches the class.
/// 2. PUT the file bytes directly to that URL (valid for 15 minutes).
/// 3. Return the public URL of the object (same behaviour as before).
///
/// The public signature of UploadFile is unchanged, so existing callers
/// (CreateLessonPageController, ARUIManagerUIToolkit) keep working.
/// </summary>
public class CloudflareR2StorageService : MonoBehaviour
{
    private const string UploadUrlFunctionName = "r2-upload-url";

    [Header("Public Domains")]
    [Tooltip("Public Development URL/custom domain của bucket lesson-models.")]
    [SerializeField] private string lessonModelsPublicDomain =
        "https://pub-d18240b07b8944fabf89fcb8663dcf5f.r2.dev";

    [Tooltip("Public URL/domain dùng cho các bucket khác nếu project đang cần.")]
    [SerializeField] private string customDomainOrCdn = string.Empty;

    [Tooltip("Upload timeout in seconds for large GLB/PDF files.")]
    [SerializeField, Min(30)] private int uploadTimeoutSeconds = 300;

    public string LessonModelsPublicDomain =>
        lessonModelsPublicDomain?.Trim().TrimEnd('/') ?? string.Empty;

    public IEnumerator UploadFile(
        string bucketName,
        string objectKey,
        string localFilePath,
        string contentType,
        Action<string> onSuccess,
        Action<string> onError)
    {
        localFilePath = NormalizeLocalFilePath(localFilePath);

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            onError?.Invoke("R2 bucket name is empty.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            onError?.Invoke("R2 object key is empty.");
            yield break;
        }

        if (!File.Exists(localFilePath))
        {
            onError?.Invoke($"Local file not found at path: {localFilePath}");
            yield break;
        }

        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke("Không có phiên đăng nhập hợp lệ. Hãy đăng nhập lại.");
            yield break;
        }

        bucketName = bucketName.Trim();
        objectKey = NormalizeObjectKey(objectKey);
        contentType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType.Trim();

        byte[] payloadBytes;
        try
        {
            payloadBytes = File.ReadAllBytes(localFilePath);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Failed to read local file: {ex.Message}");
            yield break;
        }

        // Make sure the access token is still valid before calling the function.
        yield return SupabaseTokenRefresher.EnsureFreshToken();

        // ---------- 1. Request a presigned PUT URL ----------
        string uploadUrl = null;
        string requestError = null;

        yield return RequestUploadUrl(
            bucketName,
            objectKey,
            contentType,
            url => uploadUrl = url,
            error => requestError = error);

        if (!string.IsNullOrWhiteSpace(requestError) || string.IsNullOrWhiteSpace(uploadUrl))
        {
            onError?.Invoke(string.IsNullOrWhiteSpace(requestError)
                ? "Không nhận được URL upload từ server."
                : requestError);
            yield break;
        }

        // ---------- 2. Upload bytes directly to R2 ----------
        using (UnityWebRequest www = new UnityWebRequest(uploadUrl, UnityWebRequest.kHttpVerbPUT))
        {
            www.uploadHandler = new UploadHandlerRaw(payloadBytes);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.timeout = uploadTimeoutSeconds;
            // Content-Type is part of the presigned signature, so it must match.
            www.SetRequestHeader("Content-Type", contentType);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                string detail = www.downloadHandler != null ? www.downloadHandler.text : www.error;
                onError?.Invoke($"R2 Upload Error ({www.responseCode}): {detail}");
                yield break;
            }
        }

        string resultUrl = BuildPublicUrl(bucketName, objectKey);

        Debug.Log(
            "[CloudflareR2StorageService] Upload success." +
            $"\nBucket: {bucketName}" +
            $"\nObject key: {objectKey}" +
            $"\nSaved URL: {resultUrl}");

        onSuccess?.Invoke(resultUrl);
    }

    private IEnumerator RequestUploadUrl(
        string bucketName,
        string objectKey,
        string contentType,
        Action<string> onSuccess,
        Action<string> onError)
    {
        string endpoint = $"{SupabaseConfig.FunctionsUrl}/{UploadUrlFunctionName}";

        UploadUrlRequest payload = new UploadUrlRequest
        {
            bucket = bucketName,
            key = objectKey,
            content_type = contentType
        };

        using UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = SupabaseConfig.RequestTimeoutSeconds;
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");
        request.SetRequestHeader("apikey", SupabaseConfig.PublishableKey);
        request.SetRequestHeader("Authorization", $"Bearer {SupabaseSession.AccessToken}");

        yield return request.SendWebRequest();

        string text = request.downloadHandler?.text ?? string.Empty;
        UploadUrlResponse response = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(text))
                response = JsonUtility.FromJson<UploadUrlResponse>(text);
        }
        catch
        {
            // handled below
        }

        if (request.result != UnityWebRequest.Result.Success || response == null || !response.success)
        {
            string message = !string.IsNullOrWhiteSpace(response?.error)
                ? response.error
                : (string.IsNullOrWhiteSpace(text) ? request.error : text);
            onError?.Invoke($"Không lấy được URL upload R2 ({request.responseCode}): {message}");
            yield break;
        }

        onSuccess?.Invoke(response.upload_url);
    }

    private string BuildPublicUrl(string bucketName, string objectKey)
    {
        string publicBase;

        if (string.Equals(bucketName, "lesson-models", StringComparison.OrdinalIgnoreCase))
        {
            publicBase = LessonModelsPublicDomain;
        }
        else
        {
            publicBase = customDomainOrCdn?.Trim().TrimEnd('/') ?? string.Empty;
        }

        // If no public domain is configured for a non-model bucket,
        // keep the object key instead of inventing a bad URL.
        if (string.IsNullOrWhiteSpace(publicBase))
            return objectKey;

        return publicBase + "/" + objectKey.TrimStart('/');
    }

    private static string NormalizeObjectKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace("\\", "/").Trim().TrimStart('/');
    }

    /// <summary>
    /// Native file pickers normally return a regular cache path on Android.
    /// Also accept file:// URIs in case a picker/platform version returns one.
    /// </summary>
    private static string NormalizeLocalFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        string trimmed = path.Trim();
        if (!trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        try
        {
            return new Uri(trimmed).LocalPath;
        }
        catch
        {
            return trimmed.Replace("file://", string.Empty);
        }
    }

    [Serializable]
    private class UploadUrlRequest
    {
        public string bucket;
        public string key;
        public string content_type;
    }

    [Serializable]
    private class UploadUrlResponse
    {
        public bool success;
        public string upload_url;
        public string error;
    }
}
