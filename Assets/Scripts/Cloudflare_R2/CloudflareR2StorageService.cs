using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Service quản lý Upload File trực tiếp lên Cloudflare R2 bằng S3 API Standard.
/// </summary>
public class CloudflareR2StorageService : MonoBehaviour
{
    [Header("Cloudflare R2 Configurations")]
    [SerializeField] private string accountId = "YOUR_CLOUDFLARE_ACCOUNT_ID";
    [SerializeField] private string accessKeyId = "YOUR_R2_ACCESS_KEY_ID";
    [SerializeField] private string secretAccessKey = "YOUR_R2_SECRET_ACCESS_KEY";
    [SerializeField] private string customDomainOrCdn = "https://pub-xxxx.r2.dev"; // Hoặc domain tùy chỉnh của bạn

    /// <summary>
    /// Coroutine Upload file cục bộ lên Cloudflare R2.
    /// </summary>
    public IEnumerator UploadFile(
        string bucketName,
        string objectKey,
        string localFilePath,
        string contentType,
        Action<string> onSuccess,
        Action<string> onError)
    {
        if (!File.Exists(localFilePath))
        {
            onError?.Invoke($"Local file not found at path: {localFilePath}");
            yield break;
        }

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

        string host = $"{accountId}.r2.cloudflarestorage.com";
        string url = $"https://{host}/{bucketName}/{objectKey}";

        DateTime now = DateTime.UtcNow;
        string amzDate = now.ToString("yyyyMMddTHHmmssZ");
        string dateStamp = now.ToString("yyyyMMdd");
        string region = "auto";
        string service = "s3";

        // Tính Hash SHA256 của Payload
        string payloadHash = ComputeSHA256Hex(payloadBytes);

        using (UnityWebRequest www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT))
        {
            www.uploadHandler = new UploadHandlerRaw(payloadBytes);
            www.downloadHandler = new DownloadHandlerBuffer();

            www.SetRequestHeader("Content-Type", contentType);
            www.SetRequestHeader("Host", host);
            www.SetRequestHeader("x-amz-date", amzDate);
            www.SetRequestHeader("x-amz-content-sha256", payloadHash);

            // Tạo AWS SigV4 Header
            string canonicalHeaders = $"content-type:{contentType}\nhost:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n";
            string signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";

            string canonicalRequest = $"PUT\n/{bucketName}/{objectKey}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
            string credentialScope = $"{dateStamp}/{region}/{service}/aws4_request";
            string stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{credentialScope}\n{ComputeSHA256Hex(Encoding.UTF8.GetBytes(canonicalRequest))}";

            byte[] signingKey = GetSignatureKey(secretAccessKey, dateStamp, region, service);
            string signature = ComputeHmacHex(signingKey, stringToSign);

            string authorizationHeader = $"AWS4-HMAC-SHA256 Credential={accessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";
            www.SetRequestHeader("Authorization", authorizationHeader);

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                // Trả về R2 Storage Path / Public URL
                string resultUrl = string.IsNullOrWhiteSpace(customDomainOrCdn) 
                    ? $"{bucketName}/{objectKey}" 
                    : $"{customDomainOrCdn.TrimEnd('/')}/{objectKey}";

                onSuccess?.Invoke(resultUrl);
            }
            else
            {
                string errDetail = www.downloadHandler != null ? www.downloadHandler.text : www.error;
                onError?.Invoke($"R2 Upload Error ({www.responseCode}): {errDetail}");
            }
        }
    }

    #region SigV4 Helpers
    private static string ComputeSHA256Hex(byte[] data)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(data);
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.AppendFormat("{0:x2}", b);
            return sb.ToString();
        }
    }

    private static string ComputeHmacHex(byte[] key, string data)
    {
        using (HMACSHA256 hmac = new HMACSHA256(key))
        {
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.AppendFormat("{0:x2}", b);
            return sb.ToString();
        }
    }

    private static byte[] HmacSHA256(byte[] key, byte[] data)
    {
        using (HMACSHA256 hmac = new HMACSHA256(key))
        {
            return hmac.ComputeHash(data);
        }
    }

    private static byte[] GetSignatureKey(string key, string dateStamp, string regionName, string serviceName)
    {
        byte[] kSecret = Encoding.UTF8.GetBytes("AWS4" + key);
        byte[] kDate = HmacSHA256(kSecret, Encoding.UTF8.GetBytes(dateStamp));
        byte[] kRegion = HmacSHA256(kDate, Encoding.UTF8.GetBytes(regionName));
        byte[] kService = HmacSHA256(kRegion, Encoding.UTF8.GetBytes(serviceName));
        byte[] kSigning = HmacSHA256(kService, Encoding.UTF8.GetBytes("aws4_request"));
        return kSigning;
    }
    #endregion
}