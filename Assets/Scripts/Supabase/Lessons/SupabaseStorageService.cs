using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(SupabaseRuntimeRestService))]
public class SupabaseStorageService : MonoBehaviour
{
    [Header("Signed URL")]
    [SerializeField, Min(60)]
    private int defaultSignedUrlLifetimeSeconds = 3600;

    private SupabaseRuntimeRestService rest;

    public int DefaultSignedUrlLifetimeSeconds =>
        Mathf.Max(60, defaultSignedUrlLifetimeSeconds);

    private void Awake()
    {
        ResolveRestService();
    }

    private bool ResolveRestService()
    {
        if (rest == null)
            rest = GetComponent<SupabaseRuntimeRestService>();

        return rest != null;
    }

    public IEnumerator UploadFile(
        string bucket,
        string storagePath,
        string localPath,
        string contentType,
        Action<string> onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(bucket) ||
            string.IsNullOrWhiteSpace(storagePath) ||
            string.IsNullOrWhiteSpace(localPath))
        {
            onError?.Invoke("Storage upload parameters are incomplete.");
            yield break;
        }

        if (!rest.IsConfigured(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(localPath);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Cannot read local file: {ex.Message}");
            yield break;
        }

        string encodedBucket =
            UnityWebRequest.EscapeURL(bucket.Trim());

        string encodedPath =
            EncodeStoragePath(storagePath.Trim());

        string url =
            $"{rest.ProjectUrl.TrimEnd('/')}/storage/v1/object/" +
            $"{encodedBucket}/{encodedPath}";

        using UnityWebRequest request =
            new(url, UnityWebRequest.kHttpVerbPOST);

        request.timeout = SupabaseConfig.RequestTimeoutSeconds;
        request.uploadHandler = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();

        rest.ApplyAuthHeaders(request);
        request.SetRequestHeader(
            "Content-Type",
            string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType.Trim());

        request.SetRequestHeader("x-upsert", "false");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string response =
                request.downloadHandler?.text ??
                string.Empty;

            onError?.Invoke(
                $"Storage upload failed ({request.responseCode}): " +
                $"{GetUsefulError(request, response)}"
            );

            yield break;
        }

        onSuccess?.Invoke(storagePath);
    }

    /// <summary>
    /// Creates a temporary URL for downloading an object from a private
    /// Supabase Storage bucket.
    ///
    /// The authenticated user must have SELECT permission on storage.objects
    /// for the requested object.
    /// </summary>
    public IEnumerator CreateSignedDownloadUrl(
        string bucket,
        string storagePath,
        int expiresInSeconds,
        Action<string> onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        if (!rest.IsConfigured(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(bucket))
        {
            onError?.Invoke("Storage bucket is empty.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(storagePath))
        {
            onError?.Invoke("Storage path is empty.");
            yield break;
        }

        int lifetime = Mathf.Max(60, expiresInSeconds);

        string encodedBucket =
            UnityWebRequest.EscapeURL(bucket.Trim());

        string encodedPath =
            EncodeStoragePath(storagePath.Trim());

        string url =
            $"{rest.ProjectUrl.TrimEnd('/')}/storage/v1/object/sign/" +
            $"{encodedBucket}/{encodedPath}";

        SignedUrlRequest payload = new()
        {
            expiresIn = lifetime
        };

        string json = JsonUtility.ToJson(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using UnityWebRequest request =
            new(url, UnityWebRequest.kHttpVerbPOST);

        request.timeout = SupabaseConfig.RequestTimeoutSeconds;
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();

        rest.ApplyAuthHeaders(request);
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");

        yield return request.SendWebRequest();

        string responseText =
            request.downloadHandler?.text ??
            string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(
                $"Cannot create signed Storage URL " +
                $"({request.responseCode}): " +
                $"{GetUsefulError(request, responseText)}"
            );

            yield break;
        }

        SignedUrlResponse response;

        try
        {
            response =
                JsonUtility.FromJson<SignedUrlResponse>(
                    responseText);
        }
        catch (Exception exception)
        {
            onError?.Invoke(
                "Cannot parse the signed URL response: " +
                exception.Message);

            yield break;
        }

        string signedPath =
            response?.signedURL;

        if (string.IsNullOrWhiteSpace(signedPath))
        {
            // Compatibility with responses that use snake_case.
            signedPath = response?.signed_url;
        }

        if (string.IsNullOrWhiteSpace(signedPath))
        {
            onError?.Invoke(
                "Supabase returned an empty signed URL. " +
                $"Response: {responseText}");

            yield break;
        }

        string absoluteUrl =
            MakeAbsoluteStorageUrl(signedPath);

        Debug.Log(
            "[SupabaseStorageService] Signed URL created for " +
            $"{bucket}/{storagePath}. Lifetime: {lifetime}s.");

        onSuccess?.Invoke(absoluteUrl);
    }

    public IEnumerator CreateSignedDownloadUrl(
        string bucket,
        string storagePath,
        Action<string> onSuccess,
        Action<string> onError)
    {
        yield return CreateSignedDownloadUrl(
            bucket,
            storagePath,
            DefaultSignedUrlLifetimeSeconds,
            onSuccess,
            onError);
    }

    /// <summary>
    /// Use only when the bucket is configured as public.
    /// </summary>
    public string BuildPublicUrl(
        string bucket,
        string storagePath)
    {
        if (!ResolveRestService())
            return string.Empty;

        if (string.IsNullOrWhiteSpace(bucket) ||
            string.IsNullOrWhiteSpace(storagePath))
        {
            return string.Empty;
        }

        string encodedBucket =
            UnityWebRequest.EscapeURL(bucket.Trim());

        string encodedPath =
            EncodeStoragePath(storagePath.Trim());

        return
            $"{rest.ProjectUrl.TrimEnd('/')}/storage/v1/object/public/" +
            $"{encodedBucket}/{encodedPath}";
    }

    private string MakeAbsoluteStorageUrl(
        string signedPath)
    {
        string value = signedPath.Trim();

        if (Uri.TryCreate(
                value,
                UriKind.Absolute,
                out Uri absoluteUri))
        {
            return absoluteUri.ToString();
        }

        string normalizedPath =
            value.StartsWith("/")
                ? value
                : "/" + value;

        // Supabase usually returns /object/sign/... as a path
        // relative to the Storage API base (/storage/v1).
        if (normalizedPath.StartsWith(
                "/object/",
                StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath =
                "/storage/v1" + normalizedPath;
        }
        else if (!normalizedPath.StartsWith(
                     "/storage/v1/",
                     StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath =
                "/storage/v1" + normalizedPath;
        }

        return
            rest.ProjectUrl.TrimEnd('/') +
            normalizedPath;
    }

    public static string EncodeStoragePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string normalized =
            path.Trim().Replace("\\", "/");

        string[] parts =
            normalized.Split(
                new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] =
                UnityWebRequest.EscapeURL(parts[i]);
        }

        return string.Join("/", parts);
    }

    private static string GetUsefulError(
        UnityWebRequest request,
        string responseText)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
            return responseText;

        if (!string.IsNullOrWhiteSpace(request.error))
            return request.error;

        return "Unknown Storage error.";
    }

    [Serializable]
    private class SignedUrlRequest
    {
        public int expiresIn;
    }

    [Serializable]
    private class SignedUrlResponse
    {
        public string signedURL;
        public string signed_url;
    }
}