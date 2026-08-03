using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class R2StorageService : MonoBehaviour
{
    [Header("Cloudflare R2 Config")]
    [SerializeField]
    private string r2PublicDomain = "https://pub-d59e63aee46741a5a142e117e0aa60de.r2.dev";

    public string R2PublicDomain => r2PublicDomain?.TrimEnd('/') ?? string.Empty;

    /// <summary>
    /// Tải dữ liệu thô (Bytes) của file 3D (.glb/.gltf) hoặc PDF từ Cloudflare R2
    /// </summary>
    public IEnumerator DownloadAsset(
        string fileUrlOrPath,
        Action<byte[]> onSuccess,
        Action<string> onError,
        Action<float> onProgress = null)
    {
        if (string.IsNullOrWhiteSpace(fileUrlOrPath))
        {
            onError?.Invoke("File URL hoặc Path bị trống.");
            yield break;
        }

        string fullUrl = BuildFullR2Url(fileUrlOrPath);

        using UnityWebRequest request = UnityWebRequest.Get(fullUrl);
        
        // Tắt cache nếu muốn luôn lấy file mới nhất
        request.SetRequestHeader("Cache-Control", "no-cache");

        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        while (!operation.isDone)
        {
            onProgress?.Invoke(operation.progress);
            yield return null;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorMsg = $"[R2StorageService] Tải file thất bại ({request.responseCode}): {request.error}\nURL: {fullUrl}";
            Debug.LogError(errorMsg);
            onError?.Invoke(errorMsg);
            yield break;
        }

        byte[] downloadedData = request.downloadHandler.data;
        onSuccess?.Invoke(downloadedData);
    }

    /// <summary>
    /// Tải file dưới dạng Text (dùng cho JSON, config, v.v.)
    /// </summary>
    public IEnumerator DownloadText(
        string fileUrlOrPath,
        Action<string> onSuccess,
        Action<string> onError)
    {
        string fullUrl = BuildFullR2Url(fileUrlOrPath);

        using UnityWebRequest request = UnityWebRequest.Get(fullUrl);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke($"Tải text thất bại từ R2: {request.error}");
            yield break;
        }

        onSuccess?.Invoke(request.downloadHandler.text);
    }

    private string BuildFullR2Url(string pathOrUrl)
    {
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return pathOrUrl;
        }

        string cleanPath = pathOrUrl.TrimStart('/');
        return $"{R2PublicDomain}/{cleanPath}";
    }
}