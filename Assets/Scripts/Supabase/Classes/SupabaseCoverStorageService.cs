using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Class cover images (2026-09).
/// Files are stored in the public Supabase Storage bucket "class-covers" under
/// "<teacher user id>/<guid>.jpg". Storage RLS only lets a teacher write inside
/// their own folder. The public URL is saved in classes.cover_image_url.
/// </summary>
public static class SupabaseCoverStorageService
{
    public const string BucketName = "class-covers";
    private const int MaxCoverSize = 1280;

    private static readonly Dictionary<string, Texture2D> textureCache = new();

    public static IEnumerator UploadCover(
        Texture2D texture,
        Action<string> onSuccess,
        Action<string> onError)
    {
        if (texture == null)
        {
            onError?.Invoke("No cover image selected.");
            yield break;
        }

        if (!SupabaseSession.IsLoggedIn || string.IsNullOrWhiteSpace(SupabaseSession.UserId))
        {
            onError?.Invoke("Không có phiên đăng nhập hợp lệ.");
            yield break;
        }

        byte[] jpg;
        try
        {
            jpg = EncodeResizedJpg(texture);
        }
        catch (Exception exception)
        {
            onError?.Invoke("Cannot encode the cover image: " + exception.Message);
            yield break;
        }

        yield return SupabaseTokenRefresher.EnsureFreshToken();

        string objectPath = $"{SupabaseSession.UserId}/{Guid.NewGuid():N}.jpg";
        string url = $"{SupabaseConfig.ProjectUrl.TrimEnd('/')}/storage/v1/object/{BucketName}/{objectPath}";

        using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(jpg);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = 60;
        request.SetRequestHeader("Content-Type", "image/jpeg");
        request.SetRequestHeader("x-upsert", "true");
        request.SetRequestHeader("apikey", SupabaseConfig.PublishableKey);
        request.SetRequestHeader("Authorization", $"Bearer {SupabaseSession.AccessToken}");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(
                $"Cover upload failed ({request.responseCode}): " +
                (request.downloadHandler?.text ?? request.error));
            yield break;
        }

        onSuccess?.Invoke(GetPublicUrl(objectPath));
    }

    public static string GetPublicUrl(string objectPath)
    {
        return $"{SupabaseConfig.ProjectUrl.TrimEnd('/')}/storage/v1/object/public/{BucketName}/{objectPath.TrimStart('/')}";
    }

    /// <summary>Downloads (and caches) a cover image. Calls onLoaded only on success.</summary>
    public static IEnumerator LoadCover(string url, Action<Texture2D> onLoaded)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        if (textureCache.TryGetValue(url, out Texture2D cached) && cached != null)
        {
            onLoaded?.Invoke(cached);
            yield break;
        }

        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        request.timeout = 30;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[CoverStorage] Cannot load cover {url}: {request.error}");
            yield break;
        }

        Texture2D texture = DownloadHandlerTexture.GetContent(request);
        if (texture == null)
            yield break;

        textureCache[url] = texture;
        onLoaded?.Invoke(texture);
    }

    private static byte[] EncodeResizedJpg(Texture2D source)
    {
        int width = source.width;
        int height = source.height;
        float scale = Mathf.Min(1f, (float)MaxCoverSize / Mathf.Max(width, height));

        if (scale >= 1f && source.isReadable)
            return source.EncodeToJPG(85);

        int targetWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
        int targetHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));

        RenderTexture renderTexture = RenderTexture.GetTemporary(targetWidth, targetHeight, 0);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;

            Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            resized.Apply();

            byte[] bytes = resized.EncodeToJPG(85);
            UnityEngine.Object.Destroy(resized);
            return bytes;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }
}
