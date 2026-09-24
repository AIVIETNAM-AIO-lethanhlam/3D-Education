using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent incoming-call watcher for the whole logged-in Unity process.
///
/// It does NOT depend on ChatScene. While the app process is alive, it watches
/// Supabase voice_calls for status=ringing where receiver_id == current user.
/// When a call is found it prepares ChatScene and opens/reloads it so the
/// incoming-call UI can be shown immediately.
///
/// For a fully killed Android app, use FCM/Android notifications; a Unity
/// coroutine cannot wake a process that Android has killed.
/// </summary>
public class GlobalVoiceCallManager : MonoBehaviour
{
    public static GlobalVoiceCallManager Instance { get; private set; }

    [SerializeField, Min(0.8f)] private float pollInterval = 1.0f;
    [SerializeField] private string chatSceneName = "ChatScene";
    [SerializeField, Min(10f)] private float maxRingingAgeSeconds = 105f;

    private bool requestRunning;
    private bool appFocused = true;
    private string lastHandledCallId = string.Empty;
    private float lastConfigWarningTime = -100f;
    private VoiceCallRecord bannerCall;
    private string bannerCallerName = string.Empty;
    private string bannerCallerRole = string.Empty;
    private bool bannerActionRunning;
    private GUIStyle bannerBoxStyle;
    private GUIStyle bannerTitleStyle;
    private GUIStyle bannerSubtitleStyle;
    private GUIStyle answerButtonStyle;
    private GUIStyle declineButtonStyle;
    private Texture2D answerButtonTexture;
    private Texture2D declineButtonTexture;

    [Serializable]
    private class VoiceCallRecord
    {
        public string id;
        public string conversation_id;
        public string caller_id;
        public string receiver_id;
        public string channel_name;
        public string status;
        public string started_at;
    }

    [Serializable] private class VoiceCallArray { public VoiceCallRecord[] items; }

    [Serializable]
    private class ProfileRecord
    {
        public string id;
        public string full_name;
        public string role;
    }

    [Serializable] private class ProfileArray { public ProfileRecord[] items; }

    [Serializable]
    private class RefreshTokenRequest
    {
        public string refresh_token;
    }

    [Serializable]
    private class RefreshTokenResponse
    {
        public string access_token;
        public string refresh_token;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_2023_1_OR_NEWER
        GlobalVoiceCallManager existing =
            FindFirstObjectByType<GlobalVoiceCallManager>(FindObjectsInactive.Include);
#else
        GlobalVoiceCallManager existing =
            FindObjectOfType<GlobalVoiceCallManager>(true);
#endif
        if (existing != null)
            return;

        GameObject go = new GameObject("GlobalVoiceCallManager");
        go.AddComponent<GlobalVoiceCallManager>();
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

        // Helpful on desktop/editor. Android can still suspend a background app.
        Application.runInBackground = true;

        Debug.Log("[GlobalVoiceCallManager] Started.");
    }

    private void Start()
    {
        StartCoroutine(PollLoop());
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        appFocused = hasFocus;

        if (hasFocus && SupabaseSession.IsLoggedIn && !requestRunning)
            StartCoroutine(CheckIncomingCall());
    }

    private void OnApplicationPause(bool pause)
    {
        if (!pause)
        {
            appFocused = true;

            if (SupabaseSession.IsLoggedIn && !requestRunning)
                StartCoroutine(CheckIncomingCall());
        }
    }

    private IEnumerator PollLoop()
    {
        while (true)
        {
            if (SupabaseSession.IsLoggedIn && !requestRunning)
            {
                if (bannerCall != null) yield return CheckBannerStatus();
                else yield return CheckIncomingCall();
            }
            else if (!SupabaseSession.IsLoggedIn) ClearBanner();

            yield return new WaitForSecondsRealtime(pollInterval);
        }
    }

    private IEnumerator CheckIncomingCall()
    {
        requestRunning = true;

        if (!TryResolveSupabaseConfiguration(out string baseUrl, out string anonKey))
        {
            if (Time.unscaledTime - lastConfigWarningTime > 10f)
            {
                lastConfigWarningTime = Time.unscaledTime;
                Debug.LogWarning(
                    "[GlobalVoiceCallManager] Supabase URL/key not found yet. " +
                    "Trying PlayerPrefs + SupabaseConfig automatically on the next poll.");
            }

            requestRunning = false;
            yield break;
        }

        string userId = SupabaseSession.UserId;
        string accessToken = SupabaseSession.AccessToken;

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(accessToken))
        {
            requestRunning = false;
            yield break;
        }

        string path =
            "/rest/v1/voice_calls?receiver_id=eq." +
            UnityWebRequest.EscapeURL(userId) +
            "&status=eq.ringing" +
            "&select=id,conversation_id,caller_id,receiver_id,channel_name,status,started_at" +
            "&order=started_at.desc&limit=1";

        string response = null;
        long responseCode = 0;

        yield return SendAuthorizedGet(
            baseUrl,
            anonKey,
            accessToken,
            path,
            (body, code) =>
            {
                response = body;
                responseCode = code;
            });

        // If JWT expired outside ChatScene, refresh it here too.
        if (responseCode == 401)
        {
            bool refreshed = false;
            yield return RefreshSupabaseSession(
                baseUrl,
                anonKey,
                ok => refreshed = ok);

            if (refreshed)
            {
                response = null;
                responseCode = 0;
                accessToken = SupabaseSession.AccessToken;

                yield return SendAuthorizedGet(
                    baseUrl,
                    anonKey,
                    accessToken,
                    path,
                    (body, code) =>
                    {
                        response = body;
                        responseCode = code;
                    });
            }
        }

        if (responseCode < 200 || responseCode >= 300 || string.IsNullOrWhiteSpace(response))
        {
            if (responseCode != 0)
            {
                Debug.LogWarning(
                    $"[GlobalVoiceCallManager] voice_calls poll failed HTTP {responseCode}: {response}");
            }

            requestRunning = false;
            yield break;
        }

        VoiceCallRecord[] calls =
            ParseArray<VoiceCallArray, VoiceCallRecord>(
                response,
                x => x.items);

        if (calls.Length == 0 || calls[0] == null)
        {
            requestRunning = false;
            yield break;
        }

        VoiceCallRecord call = calls[0];

        if (string.IsNullOrWhiteSpace(call.id) ||
            string.Equals(call.id, lastHandledCallId, StringComparison.OrdinalIgnoreCase))
        {
            requestRunning = false;
            yield break;
        }

        if (IsTooOld(call.started_at))
        {
            requestRunning = false;
            yield break;
        }

        // Resolve caller data before navigating.
        string callerName = "User";
        string callerRole = "User";

        string profilePath =
            "/rest/v1/profiles?id=eq." +
            UnityWebRequest.EscapeURL(call.caller_id ?? string.Empty) +
            "&select=id,full_name,role&limit=1";

        string profileResponse = null;
        long profileCode = 0;

        yield return SendAuthorizedGet(
            baseUrl,
            anonKey,
            SupabaseSession.AccessToken,
            profilePath,
            (body, code) =>
            {
                profileResponse = body;
                profileCode = code;
            });

        if (profileCode >= 200 &&
            profileCode < 300 &&
            !string.IsNullOrWhiteSpace(profileResponse))
        {
            ProfileRecord[] profiles =
                ParseArray<ProfileArray, ProfileRecord>(
                    profileResponse,
                    x => x.items);

            if (profiles.Length > 0 && profiles[0] != null)
            {
                if (!string.IsNullOrWhiteSpace(profiles[0].full_name))
                    callerName = profiles[0].full_name.Trim();

                if (!string.IsNullOrWhiteSpace(profiles[0].role))
                    callerRole = profiles[0].role.Trim();
            }
        }

        string currentScene = SceneManager.GetActiveScene().name;
        string previouslySelectedPartner =
            PlayerPrefs.GetString("selected_chat_user_id", string.Empty);

        // Incoming calls stay in the banner. Do not change chat navigation or
        // leave stale pending-call PlayerPrefs merely because polling found a call.
        // Only mark handled after all incoming-call state is safely persisted.
        lastHandledCallId = call.id;

        Debug.Log(
            $"[GlobalVoiceCallManager] INCOMING CALL. " +
            $"caller={callerName}, callerId={call.caller_id}, callId={call.id}, " +
            $"scene={currentScene}, focused={appFocused}");

        // Do NOT force the receiver into a full-screen call page.
        // Keep the current scene visible and show a compact incoming-call banner
        // at the top, like Messenger/Zalo. ChatScene is opened only after Answer.
        bannerCall = call;
        bannerCallerName = callerName;
        bannerCallerRole = callerRole;
        bannerActionRunning = false;

        requestRunning = false;
    }


    private void OnGUI()
    {
        if (bannerCall == null ||
            string.IsNullOrWhiteSpace(bannerCall.id) ||
            !string.Equals(bannerCall.status, "ringing", StringComparison.OrdinalIgnoreCase))
            return;

        EnsureBannerStyles();

        float scale = Mathf.Max(1f, Screen.width / 420f);
        float margin = 12f * scale;
        float height = 92f * scale;
        float y = Mathf.Max(24f * scale, Screen.safeArea.yMin + 22f * scale);
        Rect box = new Rect(margin, y, Screen.width - margin * 2f, height);

        GUI.Box(box, GUIContent.none, bannerBoxStyle);

        float buttonW = 76f * scale;
        float buttonH = 42f * scale;
        float gap = 8f * scale;
        float right = box.xMax - 12f * scale;

        Rect answerRect = new Rect(
            right - buttonW,
            box.y + (box.height - buttonH) * 0.5f,
            buttonW,
            buttonH);

        Rect declineRect = new Rect(
            answerRect.x - gap - buttonW,
            answerRect.y,
            buttonW,
            buttonH);

        float textRight = declineRect.x - 10f * scale;
        Rect titleRect = new Rect(
            box.x + 16f * scale,
            box.y + 17f * scale,
            Mathf.Max(40f, textRight - box.x - 22f * scale),
            28f * scale);

        Rect subtitleRect = new Rect(
            titleRect.x,
            box.y + 48f * scale,
            titleRect.width,
            22f * scale);

        GUI.Label(
            titleRect,
            string.IsNullOrWhiteSpace(bannerCallerName) ? "Cuộc gọi thoại đến" : bannerCallerName,
            bannerTitleStyle);

        GUI.Label(
            subtitleRect,
            "Cuộc gọi thoại đến",
            bannerSubtitleStyle);

        GUI.enabled = !bannerActionRunning;

        if (GUI.Button(declineRect, "Từ chối", declineButtonStyle))
            StartCoroutine(DeclineBannerCall());

        if (GUI.Button(answerRect, "Trả lời", answerButtonStyle))
            AnswerBannerCall();

        GUI.enabled = true;
    }

    private void EnsureBannerStyles()
    {
        if (bannerBoxStyle != null)
            return;

        Texture2D white = Texture2D.whiteTexture;
        answerButtonTexture = CreateRoundedTexture(new Color(0.14f, 0.71f, 0.36f, 1f));
        declineButtonTexture = CreateRoundedTexture(new Color(0.95f, 0.25f, 0.29f, 1f));

        bannerBoxStyle = new GUIStyle(GUI.skin.box);
        bannerBoxStyle.normal.background = white;

        bannerTitleStyle = new GUIStyle(GUI.skin.label);
        bannerTitleStyle.fontSize = Mathf.RoundToInt(17f * Mathf.Max(1f, Screen.width / 420f));
        bannerTitleStyle.fontStyle = FontStyle.Bold;
        bannerTitleStyle.normal.textColor = new Color(0.10f, 0.15f, 0.24f);
        bannerTitleStyle.alignment = TextAnchor.MiddleLeft;

        bannerSubtitleStyle = new GUIStyle(GUI.skin.label);
        bannerSubtitleStyle.fontSize = Mathf.RoundToInt(12f * Mathf.Max(1f, Screen.width / 420f));
        bannerSubtitleStyle.normal.textColor = new Color(0.43f, 0.50f, 0.61f);
        bannerSubtitleStyle.alignment = TextAnchor.MiddleLeft;

        answerButtonStyle = new GUIStyle(GUI.skin.button);
        answerButtonStyle.fontSize = Mathf.RoundToInt(12f * Mathf.Max(1f, Screen.width / 420f));
        answerButtonStyle.fontStyle = FontStyle.Bold;
        answerButtonStyle.alignment = TextAnchor.MiddleCenter;
        answerButtonStyle.border = new RectOffset(18, 18, 18, 18);
        answerButtonStyle.normal.background = answerButtonTexture;
        answerButtonStyle.hover.background = answerButtonTexture;
        answerButtonStyle.active.background = answerButtonTexture;
        answerButtonStyle.focused.background = answerButtonTexture;
        answerButtonStyle.normal.textColor = Color.white;
        answerButtonStyle.hover.textColor = Color.white;
        answerButtonStyle.active.textColor = Color.white;
        answerButtonStyle.focused.textColor = Color.white;

        declineButtonStyle = new GUIStyle(answerButtonStyle);
        declineButtonStyle.normal.background = declineButtonTexture;
        declineButtonStyle.hover.background = declineButtonTexture;
        declineButtonStyle.active.background = declineButtonTexture;
        declineButtonStyle.focused.background = declineButtonTexture;
    }

    private static Texture2D CreateRoundedTexture(Color color)
    {
        const int size = 48;
        const float radius = 13f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(0, Mathf.Abs(x - (size - 1) / 2f) - (size / 2f - radius));
            float dy = Mathf.Max(0, Mathf.Abs(y - (size - 1) / 2f) - (size / 2f - radius));
            float alpha = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
            texture.SetPixel(x, y, new Color(color.r, color.g, color.b, alpha));
        }
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    private static Texture2D CreateSolidTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    private void AnswerBannerCall()
    {
        if (bannerCall == null || bannerActionRunning)
            return;

        bannerActionRunning = true;
        StartCoroutine(AcceptBannerCall());
    }

    private IEnumerator AcceptBannerCall()
    {
        if (bannerCall == null) yield break;
        string callId = bannerCall.id;
        if (!TryResolveSupabaseConfiguration(out string baseUrl, out string anonKey))
        {
            bannerActionRunning = false;
            yield break;
        }
        bool accepted = false;
        yield return PatchBannerStatus(baseUrl, anonKey, callId, "accepted", ok => accepted = ok);
        if (!accepted)
        {
            bannerActionRunning = false;
            yield break;
        }
        bannerCall.status = "accepted";
        PlayerPrefs.SetString(
            "selected_chat_conversation_id",
            bannerCall.conversation_id ?? string.Empty);
        PlayerPrefs.SetString(
            "selected_chat_user_id",
            bannerCall.caller_id ?? string.Empty);
        PlayerPrefs.SetString(
            "selected_chat_user_name",
            string.IsNullOrWhiteSpace(bannerCallerName) ? "User" : bannerCallerName);
        PlayerPrefs.SetString(
            "selected_chat_user_role",
            string.IsNullOrWhiteSpace(bannerCallerRole) ? "User" : bannerCallerRole);
        PlayerPrefs.SetString(
            "pending_voice_call_id",
            bannerCall.id ?? string.Empty);
        PlayerPrefs.SetString(
            "pending_voice_call_channel",
            bannerCall.channel_name ?? string.Empty);
        PlayerPrefs.SetInt("auto_accept_pending_voice_call", 1);

        string currentScene = SceneManager.GetActiveScene().name;
        if (!string.Equals(currentScene, chatSceneName, StringComparison.Ordinal))
            PlayerPrefs.SetString("previous_scene", currentScene);

        PlayerPrefs.Save();

        Debug.Log(
            $"[GlobalVoiceCallManager] Answer tapped for call={bannerCall.id}. Opening ChatScene.");

        if (Application.CanStreamedLevelBeLoaded(chatSceneName))
        {
            bannerCall = null;
            bannerActionRunning = false;
            if (!string.Equals(currentScene, chatSceneName, StringComparison.Ordinal))
                SceneManager.LoadScene(chatSceneName);
            else
            {
                // ChatScene is already loaded: let its controller consume the accepted call.
                ChatPageController chat = FindFirstObjectByType<ChatPageController>();
                if (chat != null) chat.OpenAcceptedCallFromBanner(callId);
            }
        }
        else
        {
            bannerActionRunning = false;
            Debug.LogError(
                "[GlobalVoiceCallManager] ChatScene is not in Build Profiles.");
        }
    }

    private IEnumerator DeclineBannerCall()
    {
        if (bannerCall == null || bannerActionRunning) yield break;
        bannerActionRunning = true;
        if (!TryResolveSupabaseConfiguration(out string baseUrl, out string anonKey))
        {
            bannerActionRunning = false;
            yield break;
        }
        bool success = false;
        yield return PatchBannerStatus(baseUrl, anonKey, bannerCall.id, "declined", ok => success = ok);
        if (success) ClearBanner();
        bannerActionRunning = false;
    }

    private IEnumerator PatchBannerStatus(string baseUrl, string anonKey, string callId,
        string status, Action<bool> done)
    {
        string json = status == "accepted" ?
            "{\"status\":\"accepted\",\"answered_at\":\"" + DateTime.UtcNow.ToString("o") + "\"}" :
            "{\"status\":\"" + status + "\"}";
        string url = baseUrl.TrimEnd('/') + "/rest/v1/voice_calls?id=eq." +
            UnityWebRequest.EscapeURL(callId) + "&status=eq.ringing";
        using (UnityWebRequest request = new UnityWebRequest(url, "PATCH"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 10;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", anonKey);
            request.SetRequestHeader("Authorization", "Bearer " + SupabaseSession.AccessToken);
            request.SetRequestHeader("Prefer", "return=representation");
            yield return request.SendWebRequest();
            bool success = request.result == UnityWebRequest.Result.Success &&
                !string.IsNullOrWhiteSpace(request.downloadHandler.text) &&
                request.downloadHandler.text != "[]";
            if (!success) Debug.LogWarning("[GlobalVoiceCallManager] Call action failed: " +
                request.responseCode + " " + request.downloadHandler.text);
            done?.Invoke(success);
        }
    }

    private IEnumerator CheckBannerStatus()
    {
        if (bannerCall == null || bannerActionRunning) yield break;
        if (!TryResolveSupabaseConfiguration(out string baseUrl, out string anonKey)) yield break;
        string id = bannerCall.id;
        string response = null;
        long code = 0;
        yield return SendAuthorizedGet(baseUrl, anonKey, SupabaseSession.AccessToken,
            "/rest/v1/voice_calls?id=eq." + UnityWebRequest.EscapeURL(id) +
            "&select=id,conversation_id,caller_id,receiver_id,channel_name,status,started_at&limit=1",
            (body, status) => { response = body; code = status; });
        if (code < 200 || code >= 300) yield break;
        VoiceCallRecord[] records = ParseArray<VoiceCallArray, VoiceCallRecord>(response, x => x.items);
        if (records.Length == 0 || records[0].status != "ringing" || IsTooOld(records[0].started_at))
            ClearBanner();
    }

    private void ClearBanner()
    {
        bannerCall = null;
        bannerActionRunning = false;
        PlayerPrefs.DeleteKey("pending_voice_call_id");
        PlayerPrefs.DeleteKey("pending_voice_call_channel");
        PlayerPrefs.DeleteKey("auto_accept_pending_voice_call");
        PlayerPrefs.Save();
    }

    private IEnumerator SendAuthorizedGet(
        string baseUrl,
        string anonKey,
        string accessToken,
        string path,
        Action<string, long> onCompleted)
    {
        using (UnityWebRequest request =
               UnityWebRequest.Get(baseUrl.TrimEnd('/') + path))
        {
            request.timeout = 10;
            request.SetRequestHeader("apikey", anonKey);
            request.SetRequestHeader(
                "Authorization",
                "Bearer " + accessToken);
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            string body =
                request.downloadHandler != null
                    ? request.downloadHandler.text
                    : string.Empty;

            onCompleted?.Invoke(body, request.responseCode);
        }
    }

    private IEnumerator RefreshSupabaseSession(
        string baseUrl,
        string anonKey,
        Action<bool> onCompleted)
    {
        string refreshToken = SupabaseSession.RefreshToken;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            onCompleted?.Invoke(false);
            yield break;
        }

        string url =
            baseUrl.TrimEnd('/') +
            "/auth/v1/token?grant_type=refresh_token";

        RefreshTokenRequest payload =
            new RefreshTokenRequest
            {
                refresh_token = refreshToken
            };

        using (UnityWebRequest request =
               new UnityWebRequest(
                   url,
                   UnityWebRequest.kHttpVerbPOST))
        {
            byte[] body =
                System.Text.Encoding.UTF8.GetBytes(
                    JsonUtility.ToJson(payload));

            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 10;

            request.SetRequestHeader(
                "Content-Type",
                "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("apikey", anonKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"[GlobalVoiceCallManager] Token refresh failed HTTP {request.responseCode}: " +
                    (request.downloadHandler?.text ?? string.Empty));

                onCompleted?.Invoke(false);
                yield break;
            }

            RefreshTokenResponse response = null;

            try
            {
                response =
                    JsonUtility.FromJson<RefreshTokenResponse>(
                        request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[GlobalVoiceCallManager] Refresh parse error: " +
                    exception.Message);
            }

            if (response == null ||
                string.IsNullOrWhiteSpace(response.access_token))
            {
                onCompleted?.Invoke(false);
                yield break;
            }

            PlayerPrefs.SetString(
                "access_token",
                response.access_token.Trim());

            PlayerPrefs.SetString(
                "supabase_access_token",
                response.access_token.Trim());

            PlayerPrefs.SetString(
                "session_access_token",
                response.access_token.Trim());

            if (!string.IsNullOrWhiteSpace(response.refresh_token))
            {
                PlayerPrefs.SetString(
                    "refresh_token",
                    response.refresh_token.Trim());
            }

            PlayerPrefs.Save();

            Debug.Log(
                "[GlobalVoiceCallManager] Supabase session refreshed.");
            onCompleted?.Invoke(true);
        }
    }

    private bool TryResolveSupabaseConfiguration(
        out string baseUrl,
        out string anonKey)
    {
        baseUrl = FirstNonEmpty(
            PlayerPrefs.GetString("supabase_url", string.Empty),
            PlayerPrefs.GetString("supabaseUrl", string.Empty),
            PlayerPrefs.GetString("SUPABASE_URL", string.Empty),
            ReadStaticString(
                "SupabaseConfig",
                "SupabaseUrl",
                "SUPABASE_URL",
                "ProjectUrl",
                "Url",
                "BaseUrl"));

        anonKey = FirstNonEmpty(
            PlayerPrefs.GetString("supabase_anon_key", string.Empty),
            PlayerPrefs.GetString("supabaseAnonKey", string.Empty),
            PlayerPrefs.GetString("anon_key", string.Empty),
            PlayerPrefs.GetString("SUPABASE_ANON_KEY", string.Empty),
            ReadStaticString(
                "SupabaseConfig",
                "SupabaseAnonKey",
                "SUPABASE_ANON_KEY",
                "PublishableKey",
                "SupabasePublishableKey",
                "AnonKey",
                "ApiKey",
                "PublicAnonKey"));

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = baseUrl.Trim().TrimEnd('/');

            if (!baseUrl.StartsWith(
                    "http://",
                    StringComparison.OrdinalIgnoreCase) &&
                !baseUrl.StartsWith(
                    "https://",
                    StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "https://" + baseUrl;
            }

            // Cache for future scenes/runs.
            PlayerPrefs.SetString("supabase_url", baseUrl);
        }

        if (!string.IsNullOrWhiteSpace(anonKey))
        {
            anonKey = anonKey.Trim();
            PlayerPrefs.SetString("supabase_anon_key", anonKey);
        }

        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            !string.IsNullOrWhiteSpace(anonKey))
        {
            PlayerPrefs.Save();
            return true;
        }

        return false;
    }

    private static string ReadStaticString(
        string typeName,
        params string[] memberNames)
    {
        Type type = FindType(typeName);

        if (type == null)
            return string.Empty;

        BindingFlags flags =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Static;

        foreach (string memberName in memberNames)
        {
            FieldInfo field =
                type.GetField(memberName, flags);

            if (field != null &&
                field.FieldType == typeof(string))
            {
                string value = field.GetValue(null) as string;

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            PropertyInfo property =
                type.GetProperty(memberName, flags);

            if (property != null &&
                property.PropertyType == typeof(string) &&
                property.GetIndexParameters().Length == 0)
            {
                string value = property.GetValue(null) as string;

                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return string.Empty;
    }

    private static Type FindType(string typeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(typeName);

            if (type != null)
                return type;

            try
            {
                Type[] types = assembly.GetTypes();

                foreach (Type candidate in types)
                {
                    if (candidate != null &&
                        string.Equals(
                            candidate.Name,
                            typeName,
                            StringComparison.Ordinal))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // Some Unity assemblies do not allow GetTypes safely.
            }
        }

        return null;
    }

    private static string FirstNonEmpty(
        params string[] values)
    {
        if (values == null)
            return string.Empty;

        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private bool IsTooOld(string startedAt)
    {
        if (string.IsNullOrWhiteSpace(startedAt))
            return false;

        if (!DateTime.TryParse(
                startedAt,
                null,
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out DateTime started))
        {
            return false;
        }

        double age =
            (DateTime.UtcNow - started.ToUniversalTime())
            .TotalSeconds;

        return age > maxRingingAgeSeconds;
    }

    private static TItem[] ParseArray<TWrapper, TItem>(
        string json,
        Func<TWrapper, TItem[]> selector)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            json.Trim() == "[]")
        {
            return Array.Empty<TItem>();
        }

        try
        {
            TWrapper wrapper =
                JsonUtility.FromJson<TWrapper>(
                    "{\"items\":" + json + "}");

            if (wrapper == null)
                return Array.Empty<TItem>();

            TItem[] items = selector(wrapper);
            return items ?? Array.Empty<TItem>();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[GlobalVoiceCallManager] JSON parse error: " +
                exception.Message +
                "\nJSON: " + json);

            return Array.Empty<TItem>();
        }
    }
}
