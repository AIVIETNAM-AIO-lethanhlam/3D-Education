using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Put this component on one GameObject in SplashScene.
/// It survives scene changes and keeps the signed-in user presence current across the whole app.
/// </summary>
public class AppPresenceManager : MonoBehaviour
{
    public static AppPresenceManager Instance { get; private set; }

    [SerializeField] private string supabaseUrl = "https://YOUR_PROJECT.supabase.co";
    [SerializeField] private string supabaseAnonKey = "YOUR_SUPABASE_ANON_KEY";
    [SerializeField, Min(5f)] private float heartbeatInterval = 20f;

    private Coroutine heartbeatCoroutine;

    [Serializable]
    private class PresenceBody
    {
        public string user_id;
        public bool is_online;
        public string last_seen_at;
        public string updated_at;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        heartbeatCoroutine = StartCoroutine(HeartbeatLoop());
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        StartCoroutine(UpdatePresence(hasFocus));
    }

    private void OnApplicationPause(bool paused)
    {
        StartCoroutine(UpdatePresence(!paused));
    }

    private void OnApplicationQuit()
    {
        // Mobile operating systems may terminate the process before this request completes.
        // The UI also treats an old heartbeat as offline, so stale online rows do not remain active forever.
        StartCoroutine(UpdatePresence(false));
    }

    public void RefreshNow()
    {
        StartCoroutine(UpdatePresence(true));
    }

    public void MarkOffline()
    {
        StartCoroutine(UpdatePresence(false));
    }

    private IEnumerator HeartbeatLoop()
    {
        while (true)
        {
            yield return UpdatePresence(true);
            yield return new WaitForSecondsRealtime(heartbeatInterval);
        }
    }

    private IEnumerator UpdatePresence(bool online)
    {
        string userId = PlayerPrefs.GetString("user_id", string.Empty);
        string token = GetAccessToken();

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            yield break;

        if (supabaseUrl.Contains("YOUR_PROJECT") || supabaseAnonKey.Contains("YOUR_SUPABASE"))
            yield break;

        string now = DateTime.UtcNow.ToString("o");
        PresenceBody body = new PresenceBody
        {
            user_id = userId,
            is_online = online,
            updated_at = now,
            last_seen_at = online ? null : now
        };

        string json = JsonUtility.ToJson(body);
        string url = supabaseUrl.TrimEnd('/') + "/rest/v1/user_presence";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", supabaseAnonKey);
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.SetRequestHeader("Prefer", "resolution=merge-duplicates,return=minimal");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogWarning("[AppPresenceManager] Presence update failed: " + request.downloadHandler.text);
        }
    }

    private static string GetAccessToken()
    {
        string token = PlayerPrefs.GetString("access_token", string.Empty);
        if (string.IsNullOrWhiteSpace(token)) token = PlayerPrefs.GetString("supabase_access_token", string.Empty);
        if (string.IsNullOrWhiteSpace(token)) token = PlayerPrefs.GetString("session_access_token", string.Empty);
        return token;
    }
}