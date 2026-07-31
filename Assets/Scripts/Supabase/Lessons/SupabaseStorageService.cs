using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(SupabaseRuntimeRestService))]
public class SupabaseStorageService : MonoBehaviour
{
    private SupabaseRuntimeRestService rest;

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

        string encodedPath = EncodeStoragePath(storagePath);
        string url =
            $"{rest.ProjectUrl.TrimEnd('/')}/storage/v1/object/" +
            $"{UnityWebRequest.EscapeURL(bucket)}/{encodedPath}";

        using UnityWebRequest request =
            new(url, UnityWebRequest.kHttpVerbPOST);

        request.uploadHandler = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();

        rest.ApplyAuthHeaders(request);
        request.SetRequestHeader("Content-Type", contentType);
        request.SetRequestHeader("x-upsert", "false");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string response = request.downloadHandler?.text ?? string.Empty;
            onError?.Invoke(
                $"Storage upload failed ({request.responseCode}): {response}"
            );
            yield break;
        }

        onSuccess?.Invoke(storagePath);
    }

    private static string EncodeStoragePath(string path)
    {
        string[] parts = path.Split('/');

        for (int i = 0; i < parts.Length; i++)
            parts[i] = UnityWebRequest.EscapeURL(parts[i]);

        return string.Join("/", parts);
    }
}