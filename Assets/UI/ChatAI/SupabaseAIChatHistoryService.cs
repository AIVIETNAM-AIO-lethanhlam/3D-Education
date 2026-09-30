using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class SupabaseAIChatHistoryService
{
    public const string BucketName = "ai-chat-images";

    [Serializable]
    public class MessageRow
    {
        public string id;
        public string user_id;
        public string role;
        public string content;
        public string[] image_paths;
        public string created_at;
    }

    [Serializable]
    private class MessageRows { public MessageRow[] items; }

    public static IEnumerator SaveMessage(
        string id,
        string userId,
        string role,
        string content,
        string createdAt,
        IList<string> imageBase64,
        IList<string> imageMimeTypes,
        Action<string[]> onSuccess,
        Action<string> onError)
    {
        if (!Validate(out string error)) { onError?.Invoke(error); yield break; }

        List<string> paths = new List<string>();
        int imageCount = imageBase64 == null ? 0 : imageBase64.Count;

        for (int i = 0; i < imageCount; i++)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(imageBase64[i]); }
            catch (Exception ex) { onError?.Invoke("Invalid image data: " + ex.Message); yield break; }

            string mime = imageMimeTypes != null && i < imageMimeTypes.Count
                ? imageMimeTypes[i]
                : "image/jpeg";
            string extension = mime.IndexOf("png", StringComparison.OrdinalIgnoreCase) >= 0 ? "png" : "jpg";
            string path = userId + "/" + id + "/" + i + "." + extension;
            string url = ProjectBaseUrl() + "/storage/v1/object/" + BucketName + "/" + EncodePath(path);

            using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                SetHeaders(request);
                request.SetRequestHeader("Content-Type", mime);
                request.SetRequestHeader("x-upsert", "true");
                request.timeout = SupabaseConfig.RequestTimeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke("Image upload failed: " + ResponseError(request));
                    yield break;
                }
            }
            paths.Add(path);
        }

        MessageRow row = new MessageRow
        {
            id = id,
            user_id = userId,
            role = role,
            content = content ?? string.Empty,
            image_paths = paths.ToArray(),
            created_at = createdAt
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(row));
        using (UnityWebRequest request = new UnityWebRequest(
            SupabaseConfig.RestUrl.TrimEnd('/') + "/ai_chat_messages",
            UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            SetHeaders(request);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Prefer", "resolution=merge-duplicates,return=minimal");
            request.timeout = SupabaseConfig.RequestTimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke("Message save failed: " + ResponseError(request));
                yield break;
            }
        }

        onSuccess?.Invoke(paths.ToArray());
    }

    public static IEnumerator LoadMessages(
        string userId,
        Action<MessageRow[]> onSuccess,
        Action<string> onError)
    {
        if (!Validate(out string error)) { onError?.Invoke(error); yield break; }
        string url = SupabaseConfig.RestUrl.TrimEnd('/') +
            "/ai_chat_messages?user_id=eq." + UnityWebRequest.EscapeURL(userId) +
            "&select=id,user_id,role,content,image_paths,created_at&order=created_at.asc";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            SetHeaders(request);
            request.timeout = SupabaseConfig.RequestTimeoutSeconds;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke("History load failed: " + ResponseError(request));
                yield break;
            }

            try
            {
                MessageRows wrapper = JsonUtility.FromJson<MessageRows>(
                    "{\"items\":" + request.downloadHandler.text + "}");
                onSuccess?.Invoke(wrapper?.items ?? Array.Empty<MessageRow>());
            }
            catch (Exception ex) { onError?.Invoke("Invalid history response: " + ex.Message); }
        }
    }

    /// <summary>Deletes every AI chat message of this user (BUG-012 "New conversation").</summary>
    public static IEnumerator DeleteAllMessages(
        string userId,
        Action<string> onError)
    {
        if (!Validate(out string error)) { onError?.Invoke(error); yield break; }

        string url = SupabaseConfig.RestUrl.TrimEnd('/') +
            "/ai_chat_messages?user_id=eq." + UnityWebRequest.EscapeURL(userId);

        using (UnityWebRequest request = UnityWebRequest.Delete(url))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            SetHeaders(request);
            request.SetRequestHeader("Prefer", "return=minimal");
            request.timeout = SupabaseConfig.RequestTimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
                onError?.Invoke("History delete failed: " + ResponseError(request));
        }
    }

    public static IEnumerator DownloadImage(
        string path,
        Action<Texture2D> onSuccess,
        Action<string> onError)
    {
        string url = ProjectBaseUrl() + "/storage/v1/object/authenticated/" +
            BucketName + "/" + EncodePath(path);
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            SetHeaders(request);
            request.timeout = SupabaseConfig.RequestTimeoutSeconds;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                onError?.Invoke(ResponseError(request));
            else
                onSuccess?.Invoke(DownloadHandlerTexture.GetContent(request));
        }
    }

    private static bool Validate(out string error)
    {
        if (!SupabaseConfig.TryValidate(out error)) return false;
        if (!SupabaseSession.IsLoggedIn) { error = "User is not signed in."; return false; }
        error = string.Empty;
        return true;
    }

    private static string ProjectBaseUrl()
    {
        string rest = SupabaseConfig.RestUrl.TrimEnd('/');
        const string suffix = "/rest/v1";
        return rest.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? rest.Substring(0, rest.Length - suffix.Length)
            : rest;
    }

    private static void SetHeaders(UnityWebRequest request)
    {
        request.SetRequestHeader("apikey", SupabaseConfig.PublishableKey);
        request.SetRequestHeader("Authorization", "Bearer " + SupabaseSession.AccessToken);
    }

    private static string EncodePath(string path)
    {
        string[] parts = path.Split('/');
        for (int i = 0; i < parts.Length; i++) parts[i] = UnityWebRequest.EscapeURL(parts[i]);
        return string.Join("/", parts);
    }

    private static string ResponseError(UnityWebRequest request)
    {
        string body = request.downloadHandler?.text;
        return string.IsNullOrWhiteSpace(body) ? request.error : body;
    }
}
