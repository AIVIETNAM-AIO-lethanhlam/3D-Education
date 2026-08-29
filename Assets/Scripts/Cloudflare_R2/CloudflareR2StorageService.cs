using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Upload file trực tiếp lên Cloudflare R2 bằng S3 API.
/// 
/// Fix quan trọng:
/// - lesson-models dùng Public Development URL riêng của chính bucket lesson-models.
/// - Không dùng customDomainOrCdn cũ cho model nữa.
/// - Callback trả đúng public URL theo bucket.
/// </summary>
public class CloudflareR2StorageService : MonoBehaviour
{
    [Header("Cloudflare R2 Credentials")]
    [SerializeField] private string accountId = "YOUR_CLOUDFLARE_ACCOUNT_ID";
    [SerializeField] private string accessKeyId = "YOUR_R2_ACCESS_KEY_ID";
    [SerializeField] private string secretAccessKey = "YOUR_R2_SECRET_ACCESS_KEY";

    [Header("Public Domains")]
    [Tooltip("Public Development URL/custom domain của bucket lesson-models.")]
    [SerializeField] private string lessonModelsPublicDomain =
        "https://pub-d18240b07b8944fabf89fcb8663dcf5f.r2.dev";

    [Tooltip("Public URL/domain dùng cho các bucket khác nếu project đang cần.")]
    [SerializeField] private string customDomainOrCdn = string.Empty;

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

        if (string.IsNullOrWhiteSpace(accountId) ||
            string.IsNullOrWhiteSpace(accessKeyId) ||
            string.IsNullOrWhiteSpace(secretAccessKey))
        {
            onError?.Invoke("Cloudflare R2 credentials are missing.");
            yield break;
        }

        objectKey = NormalizeObjectKey(objectKey);

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

        string host = $"{accountId.Trim()}.r2.cloudflarestorage.com";
        string encodedObjectKey = EncodeObjectKeyForRequest(objectKey);
        string canonicalUri = $"/{bucketName.Trim()}/{encodedObjectKey}";
        string url = $"https://{host}{canonicalUri}";

        DateTime now = DateTime.UtcNow;
        string amzDate = now.ToString("yyyyMMddTHHmmssZ");
        string dateStamp = now.ToString("yyyyMMdd");
        const string region = "auto";
        const string service = "s3";

        string payloadHash = ComputeSHA256Hex(payloadBytes);

        using UnityWebRequest www =
            new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT);

        www.uploadHandler = new UploadHandlerRaw(payloadBytes);
        www.downloadHandler = new DownloadHandlerBuffer();

        www.SetRequestHeader("Content-Type", contentType);
        www.SetRequestHeader("Host", host);
        www.SetRequestHeader("x-amz-date", amzDate);
        www.SetRequestHeader("x-amz-content-sha256", payloadHash);

        string canonicalHeaders =
            $"content-type:{contentType}\n" +
            $"host:{host}\n" +
            $"x-amz-content-sha256:{payloadHash}\n" +
            $"x-amz-date:{amzDate}\n";

        const string signedHeaders =
            "content-type;host;x-amz-content-sha256;x-amz-date";

        string canonicalRequest =
            $"PUT\n{canonicalUri}\n\n" +
            $"{canonicalHeaders}\n" +
            $"{signedHeaders}\n" +
            $"{payloadHash}";

        string credentialScope =
            $"{dateStamp}/{region}/{service}/aws4_request";

        string stringToSign =
            $"AWS4-HMAC-SHA256\n" +
            $"{amzDate}\n" +
            $"{credentialScope}\n" +
            $"{ComputeSHA256Hex(Encoding.UTF8.GetBytes(canonicalRequest))}";

        byte[] signingKey =
            GetSignatureKey(
                secretAccessKey.Trim(),
                dateStamp,
                region,
                service);

        string signature =
            ComputeHmacHex(signingKey, stringToSign);

        string authorizationHeader =
            $"AWS4-HMAC-SHA256 Credential={accessKeyId.Trim()}/{credentialScope}, " +
            $"SignedHeaders={signedHeaders}, Signature={signature}";

        www.SetRequestHeader("Authorization", authorizationHeader);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            string detail =
                www.downloadHandler != null
                    ? www.downloadHandler.text
                    : www.error;

            onError?.Invoke(
                $"R2 Upload Error ({www.responseCode}): {detail}");
            yield break;
        }

        string resultUrl =
            BuildPublicUrl(bucketName, objectKey);

        Debug.Log(
            "[CloudflareR2StorageService] Upload success." +
            $"\nBucket: {bucketName}" +
            $"\nObject key: {objectKey}" +
            $"\nSaved URL: {resultUrl}");

        onSuccess?.Invoke(resultUrl);
    }

    private string BuildPublicUrl(
        string bucketName,
        string objectKey)
    {
        string publicBase;

        if (string.Equals(
                bucketName,
                "lesson-models",
                StringComparison.OrdinalIgnoreCase))
        {
            publicBase = LessonModelsPublicDomain;
        }
        else
        {
            publicBase =
                customDomainOrCdn?.Trim().TrimEnd('/')
                ?? string.Empty;
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

        return value
            .Replace("\\", "/")
            .Trim()
            .TrimStart('/');
    }

    private static string EncodeObjectKeyForRequest(string objectKey)
    {
        string[] segments =
            NormalizeObjectKey(objectKey)
                .Split('/');

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] =
                UnityWebRequest.EscapeURL(segments[i])
                    .Replace("+", "%20");
        }

        return string.Join("/", segments);
    }

    private static string ComputeSHA256Hex(byte[] data)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(data);

        StringBuilder sb =
            new StringBuilder(hash.Length * 2);

        foreach (byte b in hash)
            sb.AppendFormat("{0:x2}", b);

        return sb.ToString();
    }

    private static string ComputeHmacHex(
        byte[] key,
        string data)
    {
        using HMACSHA256 hmac =
            new HMACSHA256(key);

        byte[] hash =
            hmac.ComputeHash(
                Encoding.UTF8.GetBytes(data));

        StringBuilder sb =
            new StringBuilder(hash.Length * 2);

        foreach (byte b in hash)
            sb.AppendFormat("{0:x2}", b);

        return sb.ToString();
    }

    private static byte[] HmacSHA256(
        byte[] key,
        byte[] data)
    {
        using HMACSHA256 hmac =
            new HMACSHA256(key);

        return hmac.ComputeHash(data);
    }

    private static byte[] GetSignatureKey(
        string key,
        string dateStamp,
        string regionName,
        string serviceName)
    {
        byte[] kSecret =
            Encoding.UTF8.GetBytes("AWS4" + key);

        byte[] kDate =
            HmacSHA256(
                kSecret,
                Encoding.UTF8.GetBytes(dateStamp));

        byte[] kRegion =
            HmacSHA256(
                kDate,
                Encoding.UTF8.GetBytes(regionName));

        byte[] kService =
            HmacSHA256(
                kRegion,
                Encoding.UTF8.GetBytes(serviceName));

        return HmacSHA256(
            kService,
            Encoding.UTF8.GetBytes("aws4_request"));
    }
}
