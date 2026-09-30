using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class SupabaseRestService
{
    public static IEnumerator Get(
        string tableAndQuery,
        Action<string> onSuccess,
        Action<string> onError)
    {
        yield return Send(
            UnityWebRequest.kHttpVerbGET,
            tableAndQuery,
            null,
            false,
            onSuccess,
            onError);
    }

    public static IEnumerator Post(
        string tableAndQuery,
        string json,
        Action<string> onSuccess,
        Action<string> onError,
        bool returnRepresentation = true)
    {
        yield return Send(
            UnityWebRequest.kHttpVerbPOST,
            tableAndQuery,
            json,
            returnRepresentation,
            onSuccess,
            onError);
    }

    public static IEnumerator Patch(
        string tableAndQuery,
        string json,
        Action<string> onSuccess,
        Action<string> onError,
        bool returnRepresentation = true)
    {
        yield return Send(
            "PATCH",
            tableAndQuery,
            json,
            returnRepresentation,
            onSuccess,
            onError);
    }

    public static IEnumerator Delete(
        string tableAndQuery,
        Action<string> onSuccess,
        Action<string> onError)
    {
        yield return Send(
            UnityWebRequest.kHttpVerbDELETE,
            tableAndQuery,
            null,
            false,
            onSuccess,
            onError);
    }

    /// <summary>
    /// Sends a PostgREST request. Refreshes the access token before the request
    /// when it is about to expire, and retries once after an HTTP 401.
    /// </summary>
    private static IEnumerator Send(
        string method,
        string tableAndQuery,
        string json,
        bool returnRepresentation,
        Action<string> onSuccess,
        Action<string> onError)
    {
        yield return SupabaseTokenRefresher.EnsureFreshToken();

        bool succeeded = false;
        string result = null;
        string error = null;
        long statusCode = 0;

        yield return SendOnce(method, tableAndQuery, json, returnRepresentation,
            value => { succeeded = true; result = value; },
            value => error = value,
            code => statusCode = code);

        if (!succeeded && statusCode == 401 && SupabaseSession.HasStoredSession)
        {
            bool refreshed = false;
            yield return SupabaseTokenRefresher.RefreshSession(value => refreshed = value);

            if (refreshed)
            {
                error = null;
                yield return SendOnce(method, tableAndQuery, json, returnRepresentation,
                    value => { succeeded = true; result = value; },
                    value => error = value,
                    code => statusCode = code);
            }
        }

        if (succeeded)
            onSuccess?.Invoke(result);
        else
            onError?.Invoke(error);
    }

    private static IEnumerator SendOnce(
        string method,
        string tableAndQuery,
        string json,
        bool returnRepresentation,
        Action<string> onSuccess,
        Action<string> onError,
        Action<long> onStatus)
    {
        if (!SupabaseConfig.TryValidate(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke(
                "Không có phiên đăng nhập hợp lệ. Hãy đăng nhập lại.");
            yield break;
        }

        string path =
            string.IsNullOrWhiteSpace(tableAndQuery)
                ? string.Empty
                : tableAndQuery.TrimStart('/');

        string requestUrl =
            $"{SupabaseConfig.RestUrl}/{path}";

        using UnityWebRequest request =
            new UnityWebRequest(requestUrl, method);

        request.timeout =
            SupabaseConfig.RequestTimeoutSeconds;

        request.downloadHandler =
            new DownloadHandlerBuffer();

        if (!string.IsNullOrWhiteSpace(json))
        {
            request.uploadHandler =
                new UploadHandlerRaw(
                    Encoding.UTF8.GetBytes(json));
        }

        request.SetRequestHeader(
            "apikey",
            SupabaseConfig.PublishableKey);

        request.SetRequestHeader(
            "Authorization",
            $"Bearer {SupabaseSession.AccessToken}");

        request.SetRequestHeader(
            "Accept",
            "application/json");

        if (!string.IsNullOrWhiteSpace(json))
        {
            request.SetRequestHeader(
                "Content-Type",
                "application/json");
        }

        if (returnRepresentation)
        {
            request.SetRequestHeader(
                "Prefer",
                "return=representation");
        }

        yield return request.SendWebRequest();

        onStatus?.Invoke(request.responseCode);

        string responseText =
            request.downloadHandler?.text ?? string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string message =
                ExtractRestError(
                    responseText,
                    request.error,
                    request.responseCode);

            Debug.LogError(
                "Supabase REST request failed\n" +
                $"Method: {method}\n" +
                $"URL: {requestUrl}\n" +
                $"HTTP status: {request.responseCode}\n" +
                $"Response: {responseText}");

            onError?.Invoke(message);
            yield break;
        }

        onSuccess?.Invoke(responseText);
    }

    private static string ExtractRestError(
        string responseText,
        string unityError,
        long responseCode)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                SupabaseErrorResponse error =
                    JsonUtility.FromJson<SupabaseErrorResponse>(
                        responseText);

                if (error != null)
                {
                    if (!string.IsNullOrWhiteSpace(error.message))
                        return error.message;

                    if (!string.IsNullOrWhiteSpace(error.msg))
                        return error.msg;

                    if (!string.IsNullOrWhiteSpace(error.details))
                        return error.details;

                    if (!string.IsNullOrWhiteSpace(error.hint))
                        return error.hint;
                }
            }
            catch
            {
                // Trả nguyên response nếu JSON không đúng cấu trúc dự kiến.
            }

            return responseText;
        }

        return string.IsNullOrWhiteSpace(unityError)
            ? $"Supabase REST request thất bại ({responseCode})."
            : unityError;
    }
}
