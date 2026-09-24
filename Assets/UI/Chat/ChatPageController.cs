using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Direct chat controller using Supabase REST + short polling.
/// Features: online/offline presence, typing indicator, message delivery and seen status.
///
/// Required PlayerPrefs:
/// - user_id
/// - selected_chat_user_id
/// - access_token (or supabase_access_token / session_access_token)
/// Optional:
/// - selected_class_id
/// - selected_chat_user_name
/// - selected_chat_user_role
/// - selected_chat_conversation_id
/// </summary>
public class ChatPageController : MonoBehaviour
{
    [Header("Supabase")]
    [SerializeField] private string supabaseUrl = "https://YOUR_PROJECT.supabase.co";
    [SerializeField] private string supabaseAnonKey = "YOUR_SUPABASE_ANON_KEY";

    [Header("Scene Navigation")]
    [SerializeField] private string previousSceneName = "ClassDetailScene";

    [Header("Polling")]
    [SerializeField, Min(0.8f)] private float messagePollInterval = 1.5f;
    [SerializeField, Min(1f)] private float presencePollInterval = 3f;
    [SerializeField, Min(5f)] private float presenceHeartbeatInterval = 20f;
    [SerializeField, Min(0.5f)] private float typingTimeout = 1.5f;
    [SerializeField, Min(0.1f)] private float typingAnimationInterval = 0.35f;

    [Header("Safe Area")]
    [SerializeField] private float minimumTopSafePadding = 20f;

    [Header("Chat Images")]
    [SerializeField] private string chatImageBucket = "chat-images";
    [SerializeField, Min(1)] private int maxImageSizeMb = 10;

    [Header("In-App Voice Call")]
    [SerializeField, Min(0.5f)] private float voiceCallPollInterval = 1.0f;
    [SerializeField, Min(10f)] private float voiceCallRingTimeoutSeconds = 90f;

    private UIDocument uiDocument;
    private VisualElement root;
    private VisualElement safeArea;
    private VisualElement chatHeader;
    private bool chatInputFocused;
    private float keyboardInsetPoints;
    private int baselineVisibleBottom = -1;
    private float baselineRootHeight;
    private Button backButton;
    private Button callButton;
    private Button moreButton;
    private Button attachmentButton;
    private VisualElement imageGalleryPanel;
    private VisualElement imageGalleryGrid;
    private ScrollView imageGalleryScroll;
    private Label imageGalleryTitle;
    private Label imageGalleryStatus;
    private Button closeImageGalleryButton;
    private Button sendButton;
    private VisualElement sendIcon;
    private TextField messageInput;
    private Label inputPlaceholder;
    private ScrollView messageScrollView;
    private VisualElement messageContainer;
    private VisualElement typingRow;
    private VisualElement emptyChat;
    private Label teacherNameLabel;
    private Label teacherPositionLabel;
    private Label teacherStatusLabel;
    private Label partnerAvatarLabel;
    private Label typingAvatarLabel;
    private Label typingLabel;
    private Button messageSearchButton;
    private TextField messageSearchInput;
    private Label messageSearchPlaceholder;
    private Label dateLabel;
    private Label emptyChatTitleLabel;
    private Label emptyChatDescriptionLabel;
    private VisualElement headerOnlineDot;
    private VisualElement statusDot;

    // Figma-style overflow menu.
    private VisualElement moreMenuScrim;
    private VisualElement moreMenu;
    private Button viewProfileMenuItem;
    private Button sharedFilesMenuItem;
    private Button notificationsMenuItem;
    private Button reportProblemMenuItem;
    private Label viewProfileMenuLabel;
    private Label sharedFilesMenuLabel;
    private Label notificationsMenuLabel;
    private VisualElement notificationsMenuIcon;
    private Label reportProblemMenuLabel;
    private bool moreMenuVisible;

    // Report bottom-sheet.
    private VisualElement reportOverlay;
    private Button reportCancelButton;
    private Button reportConfirmButton;
    private Label reportTitleLabel;
    private Label reportDescriptionLabel;

    // Instructor profile overlay.
    private VisualElement profileOverlay;
    private Button profileBackButton;
    private Button profileMessageButton;
    private Button profileCallButton;
    private Label profileHeaderTitleLabel;
    private Label profileNameLabel;
    private Label profileRoleLabel;
    private Label profileAvatarTextLabel;
    private Label profileStatusTextLabel;
    private VisualElement profileOnlineDot;
    private VisualElement profileStatusDot;
    private Label profileAboutHeadingLabel;
    private Label profileAboutTextLabel;
    private Label profileTeachingHeadingLabel;
    private Label profileCourseNameLabel;
    private Label profileCourseSubtitleLabel;
    private Label profileStatusHeadingLabel;
    private Label profileMessageLabel;
    private Label profileCallLabel;

    // In-app Agora voice call + Supabase signaling.
    // Legacy browser-call messages may still exist in chat history, but new calls stay in-app.
    private const string LegacyVoiceCallPrefix = "__VOICE_CALL__|";

    private VisualElement voiceCallOverlay;
    private Label voiceCallHeaderLabel;
    private Label voiceCallAvatarTextLabel;
    private Label voiceCallPartnerNameLabel;
    private Label voiceCallStatusLabel;
    private Label voiceCallTimerLabel;
    private Label voiceCallDeclineLabel;
    private Label voiceCallAnswerLabel;
    private Label voiceCallEndLabel;
    private Label voiceCallSpeakerLabel;
    private Label voiceCallMuteLabel;
    private Label voiceCallNoteLabel;

    private VisualElement voiceCallActiveControls;
    private VisualElement voiceCallIncomingActions;
    private VisualElement voiceCallOutgoingActions;

    private Button voiceCallDeclineButton;
    private Button voiceCallAnswerButton;
    private Button voiceCallEndButton;
    private Button voiceCallSpeakerButton;
    private Button voiceCallMuteButton;

    private AgoraTest agoraVoiceService;
    private Coroutine voiceCallPollingCoroutine;
    private VoiceCallRecord activeVoiceCall;
    private bool activeVoiceCallIsIncoming;
    private bool voiceCallConnected;
    private bool voiceCallRemoteUserPresent;
    private bool voiceCallMuted;
    private bool voiceCallSpeakerEnabled = true;
    private DateTime voiceCallConnectedAt;
    private bool voiceCallActionRunning;
    private bool voiceCallStarting;
    private bool agoraTokenRequestRunning;
    private string agoraTokenRequestedCallId = string.Empty;
    private float agoraTokenRetryAfter;
    private string agoraLastError = string.Empty;
    private string lastFinishedCallId = string.Empty;
    private VoiceCallRecord[] callHistory = Array.Empty<VoiceCallRecord>();

    // Shared content / File manager overlay.
    private VisualElement fileManagerOverlay;
    private Button fileManagerBackButton;
    private TextField fileManagerSearchInput;
    private Label fileManagerSearchPlaceholder;
    private Button fileManagerPhotosTab;
    private Button fileManagerFilesTab;
    private Button fileManagerLinksTab;
    private Label fileManagerTitleLabel;
    private Label fileManagerPhotosLabel;
    private Label fileManagerFilesLabel;
    private Label fileManagerLinksLabel;
    private Label fileManagerPhotosCountLabel;
    private Label fileManagerFilesCountLabel;
    private Label fileManagerLinksCountLabel;
    private VisualElement fileManagerContent;
    private int fileManagerSelectedTab = 0; // 0 photos, 1 files, 2 links
    private string fileManagerSearchQuery = string.Empty;

    private bool notificationsMuted = true;
    private string messageSearchQuery = string.Empty;
    private ChatMessage[] latestMessages = Array.Empty<ChatMessage>();

    private string currentUserId;
    private string partnerUserId;
    private string classId;
    private string currentClassName;
    private string conversationId;
    private string accessToken;
    private string partnerName;
    private string partnerRole;
    private string latestRenderedSignature;
    private bool initialized;
    private bool localTypingState;
    private float lastInputTime;
    private int currentTypingDotIndex;
    private bool imageUploadInProgress;
    private const int MaxSelectedImages = 4;
    private readonly List<SelectedChatImage> selectedChatImages = new List<SelectedChatImage>();
    private readonly List<Texture2D> galleryThumbnailTextures = new List<Texture2D>();
    private readonly HashSet<string> loadedGalleryUris = new HashSet<string>();
    private const int GalleryPageSize = 16;
    private int galleryNextOffset;
    private int galleryLoadGeneration;
    private bool galleryHasMore = true;
    private bool galleryPageLoading;
#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject androidActivity;
    private PermissionCallbacks galleryPermissionCallbacks;
#endif

    // Supabase auth/session recovery. Access tokens expire, while the refresh
    // token is kept by SupabaseSession in PlayerPrefs. REST requests retry once
    // after a successful refresh so polling does not spam 401/JWT expired errors.
    private bool authRefreshInProgress;
    private bool sessionExpired;

    private readonly Dictionary<string, Texture2D> imageTextureCache =
        new Dictionary<string, Texture2D>();

    private Coroutine messagePollingCoroutine;
    private Coroutine presencePollingCoroutine;
    private Coroutine heartbeatCoroutine;
    private Coroutine typingAnimationCoroutine;
    private Coroutine typingTimeoutCoroutine;

    [Serializable] private class ConversationRpcBody { public string p_other_user_id; public string p_class_id; }
    [Serializable] private class MessageInsertBody
    {
        public string conversation_id;
        public string sender_id;
        public string receiver_id;
        public string content;
        public string message_type = "text";
    }
    [Serializable] private class PresenceBody { public string user_id; public bool is_online; public string last_seen_at; public string updated_at; }
    [Serializable] private class TypingBody { public string conversation_id; public string user_id; public bool is_typing; public string updated_at; }
    [Serializable] private class SeenPatchBody { public string seen_at; }

    [Serializable]
    private class ChatMessage
    {
        public string id;
        public string conversation_id;
        public string sender_id;
        public string receiver_id;
        public string content;
        public string message_type;
        public string created_at;
        public string delivered_at;
        public string seen_at;
    }

    [Serializable] private class ChatMessageArray { public ChatMessage[] items; }
    [Serializable] private class GalleryImageItem { public string uri; public string displayName; public string thumbnailPath; }
    [Serializable] private class GalleryImageResult { public GalleryImageItem[] items; public string error; public int nextOffset; public bool hasMore; }
    private class SelectedChatImage { public string uri; public string localPath; public string displayName; }
    [Serializable] private class PresenceRecord { public string user_id; public bool is_online; public string last_seen_at; public string updated_at; }
    [Serializable] private class PresenceArray { public PresenceRecord[] items; }
    [Serializable] private class TypingRecord { public string conversation_id; public string user_id; public bool is_typing; public string updated_at; }
    [Serializable] private class TypingArray { public TypingRecord[] items; }
    [Serializable] private class ClassNameRecord { public string class_name; }
    [Serializable] private class ClassNameArray { public ClassNameRecord[] items; }

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
        public string token_type;
        public int expires_in;
    }

    [Serializable] private class AgoraTokenRequestBody { public string call_id; }
    [Serializable] private class AgoraTokenResponse { public string token; public string app_id; public string channel_name; public uint uid; public int expires_in; }

    [Serializable]
    private class VoiceCallInsertBody
    {
        public string conversation_id;
        public string caller_id;
        public string receiver_id;
        public string channel_name;
        public string status = "ringing";
    }

    [Serializable]
    private class VoiceCallStatusPatchBody
    {
        public string status;
    }

    [Serializable]
    private class VoiceCallAcceptedPatchBody
    {
        public string status = "accepted";
        public string answered_at;
    }

    [Serializable]
    private class VoiceCallEndedPatchBody
    {
        public string status = "ended";
        public string ended_at;
    }

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
        public string answered_at;
        public string ended_at;
    }

    [Serializable]
    private class VoiceCallArray
    {
        public VoiceCallRecord[] items;
    }

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("[ChatPageController] UIDocument was not found.");
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (uiDocument == null) return;

        // Persist the REST configuration once so the global incoming-call watcher
        // can keep checking voice_calls even when ChatScene is not loaded.
        if (!string.IsNullOrWhiteSpace(supabaseUrl) &&
            !supabaseUrl.Contains("YOUR_PROJECT", StringComparison.OrdinalIgnoreCase))
            PlayerPrefs.SetString("supabase_url", supabaseUrl.Trim().TrimEnd('/'));

        if (!string.IsNullOrWhiteSpace(supabaseAnonKey) &&
            !supabaseAnonKey.Contains("YOUR_SUPABASE", StringComparison.OrdinalIgnoreCase))
            PlayerPrefs.SetString("supabase_anon_key", supabaseAnonKey.Trim());

        PlayerPrefs.Save();

        root = uiDocument.rootVisualElement;
        FindVisualElements();
        RegisterCallbacks();

        AppLanguageManager.LanguageChanged += OnLanguageChanged;
        ApplyCurrentLanguage();

        ApplySafeArea();
        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        StartCoroutine(InitializeChat());
    }

    private void OnDisable()
    {
        AppLanguageManager.LanguageChanged -= OnLanguageChanged;
        UnregisterCallbacks();

        if (root != null)
            root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

        DetachAgoraVoiceEvents();
        if (agoraVoiceService != null)
            agoraVoiceService.LeaveVoiceChannel();

        chatInputFocused = false;
        keyboardInsetPoints = 0f;
        baselineVisibleBottom = -1;
        if (safeArea != null) safeArea.style.paddingBottom = StyleKeyword.Null;
        StopAllRunningCoroutines();

        foreach (Texture2D texture in imageTextureCache.Values)
        {
            if (texture != null)
                Destroy(texture);
        }

        imageTextureCache.Clear();
        ClearGalleryThumbnails();
        selectedChatImages.Clear();
#if UNITY_ANDROID && !UNITY_EDITOR
        androidActivity?.Dispose();
        androidActivity = null;
#endif

        // Không StartCoroutine ở đây vì GameObject đã inactive.
        initialized = false;
        localTypingState = false;
    }

    private void Update()
    {
        UpdateKeyboardLayout();
        MaybeLoadMoreGalleryImages();
        if (activeVoiceCall != null &&
            string.Equals(activeVoiceCall.status, "accepted", StringComparison.OrdinalIgnoreCase))
            UpdateVoiceCallTimer();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!initialized) return;
        StartCoroutine(SetPresence(!pauseStatus));
        if (pauseStatus) StartCoroutine(SetTypingState(false));
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        // if (initialized) StartCoroutine(SetPresence(hasFocus));
    }

    private IEnumerator InitializeChat()
    {
        ConfigureInitialUi();
        ReadSessionData();

        if (!ValidateConfiguration()) yield break;

        yield return ResolveConversation();
        if (string.IsNullOrWhiteSpace(conversationId)) yield break;

        yield return ResolveCurrentClassName();

        InitializeVoiceCallService();

        initialized = true;
        UpdateAttachmentButtonState();
        yield return SetPresence(true);
        yield return LoadMessages(true);
        yield return PollPartnerPresenceAndTyping();
        // A normal ChatScene opens to messages. Only a banner Answer action opens a call.
        yield return ConsumePendingAnsweredCall();
        yield return PollVoiceCallState();
        yield return LoadCallHistory();

        messagePollingCoroutine = StartCoroutine(MessagePollingLoop());
        presencePollingCoroutine = StartCoroutine(PresencePollingLoop());
        heartbeatCoroutine = StartCoroutine(PresenceHeartbeatLoop());
        voiceCallPollingCoroutine = StartCoroutine(VoiceCallPollingLoop());
    }

    private void ReadSessionData()
    {
        // Prefer the live SupabaseSession. This is important when testing by logging
        // out of a teacher account and immediately logging into a student account:
        // an old PlayerPrefs "user_id" must never make the new user send/read as
        // the previous account.
        currentUserId = !string.IsNullOrWhiteSpace(SupabaseSession.UserId)
            ? SupabaseSession.UserId.Trim()
            : PlayerPrefs.GetString("user_id", string.Empty);

        if (string.IsNullOrWhiteSpace(currentUserId))
            currentUserId = PlayerPrefs.GetString("current_user_id", string.Empty);

        partnerUserId = PlayerPrefs.GetString("selected_chat_user_id", string.Empty);
        classId = PlayerPrefs.GetString("selected_class_id", string.Empty);
        currentClassName = PlayerPrefs.GetString("selected_class_name", string.Empty);
        if (string.IsNullOrWhiteSpace(currentClassName))
            currentClassName = PlayerPrefs.GetString("current_class_name", string.Empty);
        conversationId = PlayerPrefs.GetString("selected_chat_conversation_id", string.Empty);
        partnerName = PlayerPrefs.GetString("selected_chat_user_name", "Chat user");
        partnerRole = PlayerPrefs.GetString("selected_chat_user_role", "User");

        accessToken = !string.IsNullOrWhiteSpace(SupabaseSession.AccessToken)
            ? SupabaseSession.AccessToken.Trim()
            : PlayerPrefs.GetString("access_token", string.Empty);

        if (string.IsNullOrWhiteSpace(accessToken)) accessToken = PlayerPrefs.GetString("supabase_access_token", string.Empty);
        if (string.IsNullOrWhiteSpace(accessToken)) accessToken = PlayerPrefs.GetString("session_access_token", string.Empty);

        // Keep compatibility keys synchronized for older scenes/services.
        if (!string.IsNullOrWhiteSpace(currentUserId))
            PlayerPrefs.SetString("user_id", currentUserId);
        if (!string.IsNullOrWhiteSpace(accessToken))
            PlayerPrefs.SetString("access_token", accessToken);
        PlayerPrefs.Save();

        SetPartnerInformation(partnerName, partnerRole, false, null);
    }

    private bool ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(currentUserId) || string.IsNullOrWhiteSpace(partnerUserId))
        {
            ShowError("Missing user_id or selected_chat_user_id in PlayerPrefs.");
            return false;
        }

        if (string.Equals(
                currentUserId,
                partnerUserId,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            Debug.LogWarning(
                "[ChatPageController] Self-chat was blocked before calling " +
                "get_or_create_direct_conversation."
            );
            ShowError(T("You cannot start a direct chat with yourself.", "Bạn không thể nhắn tin trực tiếp với chính mình."));
            return false;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            ShowError("Missing Supabase access token. Save the login session token to PlayerPrefs key access_token.");
            return false;
        }

        if (supabaseUrl.Contains("YOUR_PROJECT") || supabaseAnonKey.Contains("YOUR_SUPABASE"))
        {
            ShowError("Set Supabase URL and anon key in the ChatScene Inspector.");
            return false;
        }

        return true;
    }

    private IEnumerator ResolveConversation()
    {
        if (!string.IsNullOrWhiteSpace(conversationId)) yield break;

        ConversationRpcBody body = new ConversationRpcBody
        {
            p_other_user_id = partnerUserId,
            p_class_id = string.IsNullOrWhiteSpace(classId) ? null : classId
        };

        string response = null;
        yield return SendRequest("POST", "/rest/v1/rpc/get_or_create_direct_conversation", JsonUtility.ToJson(body),
            value => response = value, "return=representation");

        if (string.IsNullOrWhiteSpace(response))
        {
            ShowError(T("Could not create or load the chat conversation.", "Không thể tạo hoặc tải cuộc trò chuyện."));
            yield break;
        }

        conversationId = response.Trim().Trim('"');
        if (conversationId.StartsWith("[") && conversationId.EndsWith("]"))
            conversationId = conversationId.Trim('[', ']', ' ', '"');

        if (!Guid.TryParse(conversationId, out _))
        {
            Debug.LogError("[ChatPageController] Unexpected RPC result: " + response);
            conversationId = string.Empty;
            ShowError(T("Invalid conversation ID returned by Supabase.", "Supabase trả về mã cuộc trò chuyện không hợp lệ."));
            yield break;
        }

        PlayerPrefs.SetString("selected_chat_conversation_id", conversationId);
        PlayerPrefs.Save();
    }

    private IEnumerator ResolveCurrentClassName()
    {
        // Prefer a class name already saved by ClassDetail/MyClasses scenes.
        if (!string.IsNullOrWhiteSpace(currentClassName))
        {
            UpdateProfileClassName();
            yield break;
        }

        if (string.IsNullOrWhiteSpace(classId))
        {
            UpdateProfileClassName();
            yield break;
        }

        string response = null;
        string path = "/rest/v1/classes?class_id=eq." +
                      UnityWebRequest.EscapeURL(classId) +
                      "&select=class_name&limit=1";

        yield return SendRequest("GET", path, null, value => response = value);

        if (!string.IsNullOrWhiteSpace(response))
        {
            ClassNameRecord[] records =
                ParseArray<ClassNameArray, ClassNameRecord>(response, x => x.items);

            if (records.Length > 0 &&
                records[0] != null &&
                !string.IsNullOrWhiteSpace(records[0].class_name))
            {
                currentClassName = records[0].class_name.Trim();
                PlayerPrefs.SetString("selected_class_name", currentClassName);
                PlayerPrefs.Save();
            }
        }

        UpdateProfileClassName();
    }

    private void UpdateProfileClassName()
    {
        if (profileCourseNameLabel != null)
        {
            profileCourseNameLabel.text =
                string.IsNullOrWhiteSpace(currentClassName)
                    ? T("Current Class", "Lớp học hiện tại")
                    : currentClassName;
        }

    }

    private IEnumerator MessagePollingLoop()
    {
        while (initialized)
        {
            yield return new WaitForSeconds(messagePollInterval);
            yield return LoadMessages(false);
        }
    }

    private IEnumerator PresencePollingLoop()
    {
        while (initialized)
        {
            yield return new WaitForSeconds(presencePollInterval);
            yield return PollPartnerPresenceAndTyping();
        }
    }

    private IEnumerator PresenceHeartbeatLoop()
    {
        while (initialized)
        {
            yield return new WaitForSeconds(presenceHeartbeatInterval);
            yield return SetPresence(true);
        }
    }

    private IEnumerator LoadMessages(bool forceRender)
    {
        string path = "/rest/v1/chat_messages?conversation_id=eq." + UnityWebRequest.EscapeURL(conversationId) +
                      "&select=id,conversation_id,sender_id,receiver_id,content,message_type,created_at,delivered_at,seen_at" +
                      "&order=created_at.asc";

        string response = null;
        yield return SendRequest("GET", path, null, value => response = value);
        if (response == null) yield break;

        ChatMessage[] messages = ParseArray<ChatMessageArray, ChatMessage>(response, x => x.items);
        latestMessages = messages ?? Array.Empty<ChatMessage>();
        if (fileManagerOverlay != null && fileManagerOverlay.ClassListContains("visible"))
            RenderFileManager();
        string signature = BuildMessageSignature(messages);

        if (forceRender || signature != latestRenderedSignature)
        {
            latestRenderedSignature = signature;
            RenderMessages(FilterMessages(latestMessages, messageSearchQuery));
        }

        yield return MarkIncomingMessagesSeen(messages);
    }

    private IEnumerator MarkIncomingMessagesSeen(ChatMessage[] messages)
    {
        bool hasUnread = false;
        if (messages != null)
        {
            foreach (ChatMessage message in messages)
            {
                if (message != null && message.receiver_id == currentUserId && string.IsNullOrWhiteSpace(message.seen_at))
                {
                    hasUnread = true;
                    break;
                }
            }
        }

        if (!hasUnread) yield break;

        SeenPatchBody body = new SeenPatchBody { seen_at = DateTime.UtcNow.ToString("o") };
        string path = "/rest/v1/chat_messages?conversation_id=eq." + UnityWebRequest.EscapeURL(conversationId) +
                      "&receiver_id=eq." + UnityWebRequest.EscapeURL(currentUserId) + "&seen_at=is.null";
        yield return SendRequest("PATCH", path, JsonUtility.ToJson(body), null, "return=minimal");
    }

    private IEnumerator PollPartnerPresenceAndTyping()
    {
        string presenceResponse = null;
        string presencePath = "/rest/v1/user_presence?user_id=eq." + UnityWebRequest.EscapeURL(partnerUserId) +
                              "&select=user_id,is_online,last_seen_at,updated_at&limit=1";
        yield return SendRequest("GET", presencePath, null, value => presenceResponse = value);

        bool isOnline = false;
        string lastSeen = null;
        if (!string.IsNullOrWhiteSpace(presenceResponse))
        {
            PresenceRecord[] records = ParseArray<PresenceArray, PresenceRecord>(presenceResponse, x => x.items);
            if (records.Length > 0)
            {
                PresenceRecord record = records[0];
                isOnline = record.is_online && IsRecent(record.updated_at, presenceHeartbeatInterval * 2.5f);
                lastSeen = record.last_seen_at;
            }
        }
        SetPartnerInformation(partnerName, partnerRole, isOnline, lastSeen);

        string typingResponse = null;
        string typingPath = "/rest/v1/chat_typing?conversation_id=eq." + UnityWebRequest.EscapeURL(conversationId) +
                            "&user_id=eq." + UnityWebRequest.EscapeURL(partnerUserId) +
                            "&select=conversation_id,user_id,is_typing,updated_at&limit=1";
        yield return SendRequest("GET", typingPath, null, value => typingResponse = value);

        bool partnerTyping = false;
        if (!string.IsNullOrWhiteSpace(typingResponse))
        {
            TypingRecord[] records = ParseArray<TypingArray, TypingRecord>(typingResponse, x => x.items);
            if (records.Length > 0)
                partnerTyping = records[0].is_typing && IsRecent(records[0].updated_at, typingTimeout + 2f);
        }
        SetPartnerTyping(partnerTyping);
    }

    private IEnumerator SetPresence(bool online)
    {
        if (string.IsNullOrWhiteSpace(currentUserId)) yield break;
        string now = DateTime.UtcNow.ToString("o");
        PresenceBody body = new PresenceBody
        {
            user_id = currentUserId,
            is_online = online,
            updated_at = now,
            // Supabase/PostgREST does not serialize null string fields correctly with JsonUtility.
            // Always send a valid ISO timestamp.
            last_seen_at = now
        };
        yield return SendRequest("POST", "/rest/v1/user_presence", JsonUtility.ToJson(body), null,
            "resolution=merge-duplicates,return=minimal");
    }

    private IEnumerator SetTypingState(bool typing)
    {
        if (!initialized || string.IsNullOrWhiteSpace(conversationId)) yield break;
        if (localTypingState == typing) yield break;
        localTypingState = typing;

        string nowIso = DateTime.UtcNow.ToString("o");
        TypingBody body = new TypingBody
        {
            conversation_id = conversationId,
            user_id = currentUserId,
            is_typing = typing,
            updated_at = nowIso
        };
        yield return SendRequest("POST", "/rest/v1/chat_typing", JsonUtility.ToJson(body), null,
            "resolution=merge-duplicates,return=minimal");
    }

    private IEnumerator TypingTimeoutRoutine()
    {
        float snapshot = lastInputTime;
        yield return new WaitForSeconds(typingTimeout);
        if (Mathf.Approximately(snapshot, lastInputTime)) yield return SetTypingState(false);
        typingTimeoutCoroutine = null;
    }

    private void SendCurrentMessage()
    {
        if (!initialized || messageInput == null) return;
        string text = messageInput.value?.Trim();
        if (string.IsNullOrWhiteSpace(text) && selectedChatImages.Count == 0) return;

        List<SelectedChatImage> imagesToSend =
            new List<SelectedChatImage>(selectedChatImages);
        selectedChatImages.Clear();
        CloseInlineGallery();
        UpdateInputState();
        StartCoroutine(SendImagesAndTextRoutine(text ?? string.Empty, imagesToSend));
    }

    private IEnumerator SendImagesAndTextRoutine(string text, List<SelectedChatImage> images)
    {
        if (imageUploadInProgress)
            yield break;

        imageUploadInProgress = true;
        messageInput?.SetEnabled(false);
        sendButton?.SetEnabled(false);
        UpdateAttachmentButtonState();
        UpdateInputState();
        yield return SetTypingState(false);

        bool sentAnything = false;

        foreach (SelectedChatImage image in images)
        {
            if (image == null || string.IsNullOrWhiteSpace(image.localPath))
                continue;

            byte[] bytes = ReadSelectedImageBytes(image.localPath);
            if (bytes == null || bytes.Length == 0)
                continue;

            string extension = GetSafeImageExtension(image.localPath);
            string storagePath =
                currentUserId + "/" + conversationId + "/" +
                Guid.NewGuid().ToString("N") + extension;

            bool uploaded = false;
            yield return UploadImageToSupabaseStorage(
                storagePath,
                bytes,
                GetImageMimeType(extension),
                success => uploaded = success
            );

            if (!uploaded)
            {
                TryDeleteCacheFile(image.localPath);
                continue;
            }

            bool saved = false;
            MessageInsertBody imageBody = new MessageInsertBody
            {
                conversation_id = conversationId,
                sender_id = currentUserId,
                receiver_id = partnerUserId,
                content = storagePath,
                message_type = "image"
            };

            yield return SendRequest(
                "POST",
                "/rest/v1/chat_messages",
                JsonUtility.ToJson(imageBody),
                _ => saved = true,
                "return=representation"
            );
            sentAnything |= saved;
            TryDeleteCacheFile(image.localPath);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            bool textSaved = false;
            MessageInsertBody textBody = new MessageInsertBody
            {
                conversation_id = conversationId,
                sender_id = currentUserId,
                receiver_id = partnerUserId,
                content = text,
                message_type = "text"
            };

            yield return SendRequest(
                "POST",
                "/rest/v1/chat_messages",
                JsonUtility.ToJson(textBody),
                _ => textSaved = true,
                "return=representation"
            );
            sentAnything |= textSaved;
        }

        imageUploadInProgress = false;
        messageInput?.SetEnabled(true);
        if (sentAnything)
        {
            messageInput.value = string.Empty;
            yield return LoadMessages(true);
        }

        UpdateAttachmentButtonState();
        UpdateInputState();
        messageInput?.Focus();
    }

    private byte[] ReadSelectedImageBytes(string localPath)
    {
        try
        {
            if (!File.Exists(localPath))
            {
                ShowError(T("The selected image could not be read.", "Không thể đọc ảnh đã chọn."));
                return null;
            }

            long maxBytes = (long)maxImageSizeMb * 1024L * 1024L;
            FileInfo info = new FileInfo(localPath);
            if (info.Length > maxBytes)
            {
                ShowError(T($"Image is larger than {maxImageSizeMb} MB.", $"Ảnh lớn hơn {maxImageSizeMb} MB."));
                return null;
            }

            return File.ReadAllBytes(localPath);
        }
        catch (Exception exception)
        {
            Debug.LogError("[ChatPageController] Could not read selected image: " + exception);
            ShowError(T("Could not read the selected image.", "Không thể đọc ảnh đã chọn."));
            return null;
        }
    }

    private static void TryDeleteCacheFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private IEnumerator SendMessageRoutine(string text)
    {
        messageInput.SetEnabled(false);
        if (sendButton != null) sendButton.SetEnabled(false);
        yield return SetTypingState(false);

        MessageInsertBody body = new MessageInsertBody
        {
            conversation_id = conversationId,
            sender_id = currentUserId,
            receiver_id = partnerUserId,
            content = text,
            message_type = "text"
        };

        bool success = false;
        yield return SendRequest("POST", "/rest/v1/chat_messages", JsonUtility.ToJson(body), _ => success = true,
            "return=representation");

        messageInput.SetEnabled(true);
        messageInput.Focus();

        if (success)
        {
            messageInput.value = string.Empty;
            UpdateInputState();
            yield return LoadMessages(true);
        }
        else
        {
            UpdateInputState();
        }
    }

    private void RenderMessages(ChatMessage[] messages)
    {
        if (messageContainer == null) return;

        List<VisualElement> remove = new List<VisualElement>();
        foreach (VisualElement child in messageContainer.Children())
            if (child.ClassListContains("message-row") || child.ClassListContains("voice-call-history-row")) remove.Add(child);
        foreach (VisualElement child in remove) child.RemoveFromHierarchy();

        bool hasMessages = messages != null && messages.Length > 0;
        if (emptyChat != null) emptyChat.style.display = hasMessages ? DisplayStyle.None : DisplayStyle.Flex;

        if (hasMessages)
        {
            for (int i = 0; i < messages.Length; i++)
            {
                ChatMessage message = messages[i];
                if (message == null) continue;

                bool outgoing = message.sender_id == currentUserId;
                bool isLatestOutgoing = outgoing && IsLatestOutgoing(messages, i);

                // Messenger-style avatar rule:
                // for consecutive incoming messages, show the partner avatar only
                // beside the LAST message in that incoming group.
                bool showIncomingAvatar = !outgoing && IsLastIncomingMessageInGroup(messages, i);

                VisualElement row = CreateMessageElement(
                    message,
                    outgoing,
                    isLatestOutgoing,
                    showIncomingAvatar);

                messageContainer.Insert(Mathf.Max(1, messageContainer.childCount - 1), row);
            }
        }

        RenderCallHistory();
        root.schedule.Execute(ScrollToBottom).ExecuteLater(50);
    }

    private VisualElement CreateMessageElement(
        ChatMessage message,
        bool outgoing,
        bool showStatus,
        bool showIncomingAvatar)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("message-row");
        row.AddToClassList(outgoing ? "outgoing-row" : "incoming-row");

        if (!outgoing)
        {
            // Always reserve the avatar column so every incoming bubble lines up.
            // The actual avatar is visible only on the newest message in a
            // consecutive group. Because incoming-row uses align-items:flex-end,
            // long message bubbles also keep the avatar at their bottom edge.
            VisualElement avatarSlot = new VisualElement();
            avatarSlot.AddToClassList("incoming-avatar-slot");

            if (showIncomingAvatar)
            {
                VisualElement avatar = new VisualElement();
                avatar.AddToClassList("student-avatar");

                Label initials = new Label(GetInitials(partnerName));
                initials.AddToClassList("student-avatar-text");
                avatar.Add(initials);
                avatarSlot.Add(avatar);
            }

            row.Add(avatarSlot);
        }

        VisualElement group = new VisualElement();
        group.AddToClassList(outgoing ? "outgoing-message-group" : "incoming-message-group");

        bool isVoiceCallMessage =
            !string.IsNullOrWhiteSpace(message.content) &&
            message.content.StartsWith(LegacyVoiceCallPrefix, StringComparison.Ordinal);

        bool isImageMessage =
            string.Equals(
                message.message_type,
                "image",
                StringComparison.OrdinalIgnoreCase
            );

        if (isVoiceCallMessage)
        {
            // Legacy browser-call invitation from older builds. Keep a harmless history card,
            // but never open the browser/room again.
            VisualElement callCard = new VisualElement();
            callCard.AddToClassList("voice-call-message-card");

            Label title = new Label(T("Previous voice call", "Cuộc gọi thoại trước đây"));
            title.AddToClassList("voice-call-message-title");
            callCard.Add(title);

            Label subtitle = new Label(
                T(
                    "This call was created by an older app version.",
                    "Cuộc gọi này được tạo bởi phiên bản ứng dụng cũ."
                )
            );
            subtitle.AddToClassList("voice-call-message-subtitle");
            callCard.Add(subtitle);

            group.Add(callCard);
        }
        else if (isImageMessage)
        {
            VisualElement imageBubble = new VisualElement();
            imageBubble.AddToClassList("message-image-bubble");
            imageBubble.AddToClassList(
                outgoing
                    ? "outgoing-image-bubble"
                    : "incoming-image-bubble"
            );

            Label loadingLabel = new Label(
                T("Loading image...", "Đang tải ảnh...")
            );
            loadingLabel.AddToClassList("message-image-loading");
            imageBubble.Add(loadingLabel);

            group.Add(imageBubble);

            StartCoroutine(
                LoadChatImageIntoElement(
                    message.content,
                    imageBubble,
                    loadingLabel
                )
            );
        }
        else
        {
            VisualElement bubble = new VisualElement();
            bubble.AddToClassList("message-bubble");
            bubble.AddToClassList(
                outgoing
                    ? "outgoing-bubble"
                    : "incoming-bubble"
            );

            Label content =
                new Label(message.content ?? string.Empty);

            content.AddToClassList("message-text");
            content.AddToClassList(
                outgoing
                    ? "outgoing-message-text"
                    : "incoming-message-text"
            );

            bubble.Add(content);
            group.Add(bubble);
        }

        VisualElement meta = new VisualElement();
        meta.AddToClassList("message-meta-row");

        Label time = new Label(FormatTime(message.created_at));
        time.AddToClassList("message-time");
        time.AddToClassList(outgoing ? "outgoing-time" : "incoming-time");
        meta.Add(time);

        if (outgoing && showStatus)
        {
            if (!string.IsNullOrWhiteSpace(message.seen_at))
            {
                // Messenger-like read receipt: once the partner has opened the
                // conversation and seen this message, show their tiny avatar
                // at the lower-right side of the latest outgoing message.
                VisualElement seenAvatar = new VisualElement();
                seenAvatar.AddToClassList("seen-avatar");

                Label seenInitials = new Label(GetInitials(partnerName));
                seenInitials.AddToClassList("seen-avatar-text");
                seenAvatar.Add(seenInitials);
                meta.Add(seenAvatar);
            }
            else
            {
                Label state = new Label(
                    string.IsNullOrWhiteSpace(message.delivered_at)
                        ? T("Sent", "Đã gửi")
                        : T("Delivered", "Đã nhận"));
                state.AddToClassList("message-delivered-status");
                meta.Add(state);
            }
        }

        group.Add(meta);
        row.Add(group);
        return row;
    }

    private static bool IsLatestOutgoing(ChatMessage[] messages, int index)
    {
        if (messages == null || index < 0 || index >= messages.Length || messages[index] == null)
            return false;

        string senderId = messages[index].sender_id;
        for (int i = index + 1; i < messages.Length; i++)
        {
            if (messages[i] != null && messages[i].sender_id == senderId)
                return false;
        }
        return true;
    }

    private static bool IsLastIncomingMessageInGroup(ChatMessage[] messages, int index)
    {
        if (messages == null || index < 0 || index >= messages.Length || messages[index] == null)
            return false;

        // Last item in the conversation -> show avatar.
        if (index == messages.Length - 1)
            return true;

        ChatMessage next = messages[index + 1];
        if (next == null)
            return true;

        // If the next message is from someone else, the current incoming group ends here.
        return next.sender_id != messages[index].sender_id;
    }

    private void HandleInputValueChanged(ChangeEvent<string> evt)
    {
        UpdateInputState();
        if (!initialized) return;
        bool hasText = !string.IsNullOrWhiteSpace(evt.newValue);
        lastInputTime = Time.unscaledTime;
        StartCoroutine(SetTypingState(hasText));
        if (typingTimeoutCoroutine != null) StopCoroutine(typingTimeoutCoroutine);
        if (hasText) typingTimeoutCoroutine = StartCoroutine(TypingTimeoutRoutine());
    }

    private void HandleInputKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
        evt.StopPropagation();
        SendCurrentMessage();
    }

    private void SetPartnerTyping(bool typing)
    {
        if (typingRow == null) return;
        typingRow.style.display = typing ? DisplayStyle.Flex : DisplayStyle.None;
        if (typingLabel != null)
            typingLabel.text = T(
                partnerName + " is typing...",
                partnerName + " đang nhập..."
            );

        if (typing)
        {
            if (typingAnimationCoroutine == null) typingAnimationCoroutine = StartCoroutine(AnimateTypingIndicator());
            root.schedule.Execute(ScrollToBottom).ExecuteLater(40);
        }
        else if (typingAnimationCoroutine != null)
        {
            StopCoroutine(typingAnimationCoroutine);
            typingAnimationCoroutine = null;
        }
    }

    private IEnumerator AnimateTypingIndicator()
    {
        while (typingRow != null && typingRow.resolvedStyle.display != DisplayStyle.None)
        {
            List<VisualElement> dots = typingRow.Query<VisualElement>(className: "typing-dot").ToList();
            for (int i = 0; i < dots.Count; i++) dots[i].style.opacity = i == currentTypingDotIndex ? 1f : 0.35f;
            currentTypingDotIndex = dots.Count == 0 ? 0 : (currentTypingDotIndex + 1) % dots.Count;
            yield return new WaitForSeconds(typingAnimationInterval);
        }
        typingAnimationCoroutine = null;
    }

    private void SetPartnerInformation(string displayName, string role, bool online, string lastSeenIso)
    {
        partnerName =
            string.IsNullOrWhiteSpace(displayName)
                ? T("Chat user", "Người dùng")
                : displayName;

        partnerRole =
            string.IsNullOrWhiteSpace(role)
                ? "User"
                : role;
        if (teacherNameLabel != null) teacherNameLabel.text = partnerName;
        if (teacherPositionLabel != null)
            teacherPositionLabel.text = LocalizeRole(partnerRole);
        string initials = GetInitials(partnerName);
        if (partnerAvatarLabel != null) partnerAvatarLabel.text = initials;
        if (typingAvatarLabel != null) typingAvatarLabel.text = initials;
        if (profileAvatarTextLabel != null) profileAvatarTextLabel.text = initials;
        if (profileNameLabel != null) profileNameLabel.text = partnerName;
        if (profileRoleLabel != null) profileRoleLabel.text = LocalizeRole(partnerRole);
        if (profileStatusTextLabel != null)
        {
            profileStatusTextLabel.text = online ? T("Online", "Trực tuyến") : T("Offline", "Ngoại tuyến");
            profileStatusTextLabel.EnableInClassList("offline", !online);
        }
        if (profileOnlineDot != null) profileOnlineDot.EnableInClassList("offline", !online);
        if (profileStatusDot != null) profileStatusDot.EnableInClassList("offline", !online);

        if (teacherStatusLabel != null)
        {
            teacherStatusLabel.text =
                online
                    ? T("Active now", "Đang hoạt động")
                    : FormatLastSeen(lastSeenIso);
            teacherStatusLabel.EnableInClassList("offline", !online);
        }
        if (headerOnlineDot != null) headerOnlineDot.EnableInClassList("offline", !online);
        if (statusDot != null) statusDot.EnableInClassList("offline", !online);
    }

    private IEnumerator SendRequest(
        string method,
        string path,
        string jsonBody,
        Action<string> onSuccess,
        string prefer = null)
    {
        if (sessionExpired) yield break;

        bool shouldRetry = false;
        yield return SendRequestOnce(
            method,
            path,
            jsonBody,
            onSuccess,
            prefer,
            expired => shouldRetry = expired);

        if (!shouldRetry || sessionExpired)
            yield break;

        // Several polling coroutines can discover an expired JWT at almost the
        // same time. Only one of them performs the refresh; the others wait.
        if (authRefreshInProgress)
        {
            while (authRefreshInProgress && !sessionExpired)
                yield return null;
        }
        else
        {
            yield return RefreshSupabaseSession();
        }

        if (sessionExpired || string.IsNullOrWhiteSpace(accessToken))
            yield break;

        // Retry the original REST request exactly once using the new JWT.
        yield return SendRequestOnce(
            method,
            path,
            jsonBody,
            onSuccess,
            prefer,
            null);
    }

    private IEnumerator SendRequestOnce(
        string method,
        string path,
        string jsonBody,
        Action<string> onSuccess,
        string prefer,
        Action<bool> onFinished)
    {
        string url = supabaseUrl.TrimEnd('/') + path;
        using (UnityWebRequest request = new UnityWebRequest(url, method))
        {
            request.downloadHandler = new DownloadHandlerBuffer();

            if (jsonBody != null)
            {
                request.uploadHandler =
                    new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            request.SetRequestHeader("apikey", supabaseAnonKey);
            request.SetRequestHeader("Authorization", "Bearer " + accessToken);
            if (!string.IsNullOrWhiteSpace(prefer))
                request.SetRequestHeader("Prefer", prefer);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke(request.downloadHandler.text);
                onFinished?.Invoke(false);
                yield break;
            }

            string responseText = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            bool jwtExpired = IsExpiredJwtResponse(request.responseCode, responseText);
            if (jwtExpired)
            {
                // Do not log every polling request as an error. The caller will
                // refresh the session and retry this request once.
                Debug.LogWarning(
                    $"[ChatPageController] Supabase JWT expired while calling {method} {path}. " +
                    "Refreshing session...");
                onFinished?.Invoke(true);
                yield break;
            }

            Debug.LogError(
                $"[ChatPageController] {method} {path} failed " +
                $"({request.responseCode}): {responseText}");

            // Helpful diagnostic for the direct-conversation RPC.
            if (path.Contains("get_or_create_direct_conversation") &&
                responseText.IndexOf(
                    "Invalid chat participant",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.LogError(
                    "[ChatPageController] Supabase rejected the chat participant. " +
                    $"currentUserId={currentUserId}, partnerUserId={partnerUserId}, " +
                    $"classId={classId}. Check that the partner is another user " +
                    "and is an allowed participant in this class.");
            }

            onFinished?.Invoke(false);
        }
    }

    private static bool IsExpiredJwtResponse(long responseCode, string responseText)
    {
        if (responseCode != 401 || string.IsNullOrWhiteSpace(responseText))
            return false;

        return responseText.IndexOf("PGRST303", StringComparison.OrdinalIgnoreCase) >= 0 ||
               responseText.IndexOf("JWT expired", StringComparison.OrdinalIgnoreCase) >= 0 ||
               responseText.IndexOf("token is expired", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private IEnumerator RefreshSupabaseSession()
    {
        if (authRefreshInProgress || sessionExpired)
            yield break;

        authRefreshInProgress = true;

        string refreshToken = !string.IsNullOrWhiteSpace(SupabaseSession.RefreshToken)
            ? SupabaseSession.RefreshToken.Trim()
            : PlayerPrefs.GetString("refresh_token", string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            authRefreshInProgress = false;
            HandleExpiredSession(
                "Supabase access token expired and no refresh_token is available. " +
                "Please sign in again.");
            yield break;
        }

        string url = supabaseUrl.TrimEnd('/') +
                     "/auth/v1/token?grant_type=refresh_token";

        RefreshTokenRequest payload = new RefreshTokenRequest
        {
            refresh_token = refreshToken
        };

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.uploadHandler = new UploadHandlerRaw(
                Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("apikey", supabaseAnonKey);

            yield return request.SendWebRequest();

            string responseText = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            if (request.result != UnityWebRequest.Result.Success)
            {
                authRefreshInProgress = false;
                Debug.LogError(
                    $"[ChatPageController] Supabase token refresh failed " +
                    $"({request.responseCode}): {responseText}");
                HandleExpiredSession(
                    "Your login session has expired. Please sign in again.");
                yield break;
            }

            RefreshTokenResponse refreshed = null;
            try
            {
                refreshed = JsonUtility.FromJson<RefreshTokenResponse>(responseText);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[ChatPageController] Could not parse Supabase refresh response: " +
                    exception.Message);
            }

            if (refreshed == null ||
                string.IsNullOrWhiteSpace(refreshed.access_token))
            {
                authRefreshInProgress = false;
                HandleExpiredSession(
                    "Supabase did not return a valid refreshed access token. " +
                    "Please sign in again.");
                yield break;
            }

            accessToken = refreshed.access_token.Trim();
            PlayerPrefs.SetString("access_token", accessToken);
            PlayerPrefs.SetString("supabase_access_token", accessToken);
            PlayerPrefs.SetString("session_access_token", accessToken);

            if (!string.IsNullOrWhiteSpace(refreshed.refresh_token))
                PlayerPrefs.SetString("refresh_token", refreshed.refresh_token.Trim());

            PlayerPrefs.Save();
            authRefreshInProgress = false;

            Debug.Log("[ChatPageController] Supabase session refreshed successfully.");
        }
    }

    private void HandleExpiredSession(string reason)
    {
        if (sessionExpired) return;

        sessionExpired = true;
        initialized = false;
        localTypingState = false;

        Debug.LogError("[ChatPageController] " + reason);
        ShowError(T(
            "Your login session has expired. Please sign in again.",
            "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại."));
    }

    private static TItem[] ParseArray<TWrapper, TItem>(string json, Func<TWrapper, TItem[]> selector)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]") return Array.Empty<TItem>();
        string wrapped = "{\"items\":" + json + "}";
        TWrapper wrapper = JsonUtility.FromJson<TWrapper>(wrapped);
        TItem[] items = selector(wrapper);
        return items ?? Array.Empty<TItem>();
    }

    private void FindVisualElements()
    {
        safeArea = root.Q<VisualElement>("safe-area");
        chatHeader = root.Q<VisualElement>("chat-header");
        backButton = root.Q<Button>("back-button");
        callButton = root.Q<Button>("call-button");
        moreButton = root.Q<Button>("more-button");
        attachmentButton = root.Q<Button>("attachment-button");
        imageGalleryPanel = root.Q<VisualElement>("image-gallery-panel");
        imageGalleryGrid = root.Q<VisualElement>("image-gallery-grid");
        imageGalleryScroll = root.Q<ScrollView>("image-gallery-scroll");
        imageGalleryTitle = root.Q<Label>("image-gallery-title");
        imageGalleryStatus = root.Q<Label>("image-gallery-status");
        closeImageGalleryButton = root.Q<Button>("close-image-gallery-button");
        sendButton = root.Q<Button>("send-button");
        sendIcon = root.Q<VisualElement>("send-icon");
        messageInput = root.Q<TextField>("message-input");
        inputPlaceholder = root.Q<Label>("input-placeholder");
        messageScrollView = root.Q<ScrollView>("message-scroll-view");
        messageContainer = root.Q<VisualElement>("message-container");
        typingRow = root.Q<VisualElement>("typing-row");
        emptyChat = root.Q<VisualElement>("empty-chat");
        teacherNameLabel = root.Q<Label>("teacher-name");
        teacherPositionLabel = root.Q<Label>("teacher-position");
        teacherStatusLabel = root.Q<Label>("teacher-status");
        partnerAvatarLabel = root.Q<Label>("partner-avatar-text");
        typingAvatarLabel = root.Q<Label>("typing-avatar-text");
        typingLabel = root.Q<Label>("typing-label");
        messageSearchButton = root.Q<Button>("message-search-button");
        messageSearchInput = root.Q<TextField>("message-search-input");
        messageSearchPlaceholder = root.Q<Label>("message-search-placeholder");
        dateLabel = root.Q<Label>("date-label");
        emptyChatTitleLabel = root.Q<Label>("empty-chat-title");
        emptyChatDescriptionLabel = root.Q<Label>("empty-chat-description");
        headerOnlineDot = root.Q<VisualElement>("header-online-dot");
        statusDot = root.Q<VisualElement>("status-dot");

        moreMenuScrim = root.Q<VisualElement>("more-menu-scrim");
        moreMenu = root.Q<VisualElement>("more-menu");
        viewProfileMenuItem = root.Q<Button>("view-profile-menu-item");
        sharedFilesMenuItem = root.Q<Button>("shared-files-menu-item");
        notificationsMenuItem = root.Q<Button>("notifications-menu-item");
        reportProblemMenuItem = root.Q<Button>("report-problem-menu-item");
        viewProfileMenuLabel = root.Q<Label>("view-profile-menu-label");
        sharedFilesMenuLabel = root.Q<Label>("shared-files-menu-label");
        notificationsMenuLabel = root.Q<Label>("notifications-menu-label");
        notificationsMenuIcon = root.Q<VisualElement>(className: "notifications-menu-icon");
        reportProblemMenuLabel = root.Q<Label>("report-problem-menu-label");

        reportOverlay = root.Q<VisualElement>("report-overlay");
        reportCancelButton = root.Q<Button>("report-cancel-button");
        reportConfirmButton = root.Q<Button>("report-confirm-button");
        reportTitleLabel = root.Q<Label>("report-title");
        reportDescriptionLabel = root.Q<Label>("report-description");

        profileOverlay = root.Q<VisualElement>("profile-overlay");
        profileBackButton = root.Q<Button>("profile-back-button");
        profileMessageButton = root.Q<Button>("profile-message-button");
        profileCallButton = root.Q<Button>("profile-call-button");
        profileHeaderTitleLabel = root.Q<Label>("profile-header-title");
        profileNameLabel = root.Q<Label>("profile-name");
        profileRoleLabel = root.Q<Label>("profile-role");
        profileAvatarTextLabel = root.Q<Label>("profile-avatar-text");
        profileStatusTextLabel = root.Q<Label>("profile-status-text");
        profileOnlineDot = root.Q<VisualElement>("profile-online-dot");
        profileStatusDot = root.Q<VisualElement>("profile-status-dot");
        profileAboutHeadingLabel = root.Q<Label>("profile-about-heading");
        profileAboutTextLabel = root.Q<Label>("profile-about-text");
        profileTeachingHeadingLabel = root.Q<Label>("profile-teaching-heading");
        profileCourseNameLabel = root.Q<Label>("profile-course-name");
        profileCourseSubtitleLabel = root.Q<Label>("profile-course-subtitle");
        profileStatusHeadingLabel = root.Q<Label>("profile-status-heading");
        profileMessageLabel = root.Q<Label>("profile-message-label");
        profileCallLabel = root.Q<Label>("profile-call-label");

        voiceCallOverlay = root.Q<VisualElement>("voice-call-overlay");
        voiceCallHeaderLabel = root.Q<Label>("voice-call-header-label");
        voiceCallAvatarTextLabel = root.Q<Label>("voice-call-avatar-text");
        voiceCallPartnerNameLabel = root.Q<Label>("voice-call-partner-name");
        voiceCallStatusLabel = root.Q<Label>("voice-call-status");
        voiceCallTimerLabel = root.Q<Label>("voice-call-timer");
        voiceCallDeclineLabel = root.Q<Label>("voice-call-decline-label");
        voiceCallAnswerLabel = root.Q<Label>("voice-call-answer-label");
        voiceCallEndLabel = root.Q<Label>("voice-call-end-label");
        voiceCallSpeakerLabel = root.Q<Label>("voice-call-speaker-label");
        voiceCallMuteLabel = root.Q<Label>("voice-call-mute-label");
        voiceCallNoteLabel = root.Q<Label>("voice-call-note");
        voiceCallActiveControls = root.Q<VisualElement>("voice-call-active-controls");
        voiceCallIncomingActions = root.Q<VisualElement>("voice-call-incoming-actions");
        voiceCallOutgoingActions = root.Q<VisualElement>("voice-call-outgoing-actions");
        voiceCallDeclineButton = root.Q<Button>("voice-call-decline-button");
        voiceCallAnswerButton = root.Q<Button>("voice-call-answer-button");
        voiceCallEndButton = root.Q<Button>("voice-call-end-button");
        voiceCallSpeakerButton = root.Q<Button>("voice-call-speaker-button");
        voiceCallMuteButton = root.Q<Button>("voice-call-mute-button");

        fileManagerOverlay = root.Q<VisualElement>("file-manager-overlay");
        fileManagerBackButton = root.Q<Button>("file-manager-back-button");
        fileManagerSearchInput = root.Q<TextField>("file-manager-search-input");
        fileManagerSearchPlaceholder = root.Q<Label>("file-manager-search-placeholder");
        fileManagerPhotosTab = root.Q<Button>("file-manager-photos-tab");
        fileManagerFilesTab = root.Q<Button>("file-manager-files-tab");
        fileManagerLinksTab = root.Q<Button>("file-manager-links-tab");
        fileManagerTitleLabel = root.Q<Label>("file-manager-title");
        fileManagerPhotosLabel = root.Q<Label>("file-manager-photos-label");
        fileManagerFilesLabel = root.Q<Label>("file-manager-files-label");
        fileManagerLinksLabel = root.Q<Label>("file-manager-links-label");
        fileManagerPhotosCountLabel = root.Q<Label>("file-manager-photos-count");
        fileManagerFilesCountLabel = root.Q<Label>("file-manager-files-count");
        fileManagerLinksCountLabel = root.Q<Label>("file-manager-links-count");
        fileManagerContent = root.Q<VisualElement>("file-manager-content");
    }


    private static string T(string english, string vietnamese)
    {
        return AppLanguageManager.IsVietnamese
            ? vietnamese
            : english;
    }

    private void OnLanguageChanged(string language)
    {
        ApplyCurrentLanguage();

        // Re-render dynamic labels such as Sent/Delivered and presence text.
        if (initialized && !string.IsNullOrWhiteSpace(conversationId))
        {
            StartCoroutine(LoadMessages(true));
            StartCoroutine(PollPartnerPresenceAndTyping());
        }
    }

    private void ApplyCurrentLanguage()
    {
        if (messageSearchPlaceholder != null)
            messageSearchPlaceholder.text = T("Search messages...", "Tìm kiếm tin nhắn...");

        if (dateLabel != null)
            dateLabel.text = T("Today", "Hôm nay");

        if (emptyChatTitleLabel != null)
            emptyChatTitleLabel.text = T("No messages yet", "Chưa có tin nhắn");

        if (emptyChatDescriptionLabel != null)
        {
            emptyChatDescriptionLabel.text = T(
                "Send a message to start the conversation.",
                "Gửi tin nhắn để bắt đầu cuộc trò chuyện."
            );
        }

        if (inputPlaceholder != null)
            inputPlaceholder.text = T("Type a message...", "Nhập tin nhắn...");

        if (viewProfileMenuLabel != null)
            viewProfileMenuLabel.text = T("View Instructor Profile", "Xem hồ sơ giảng viên");
        if (sharedFilesMenuLabel != null)
            sharedFilesMenuLabel.text = T("Shared Files & Materials", "Tệp & tài liệu đã chia sẻ");
        if (notificationsMenuLabel != null)
            notificationsMenuLabel.text = notificationsMuted
                ? T("Notifications Muted", "Đã tắt thông báo")
                : T("Notifications On", "Đã bật thông báo");
        UpdateNotificationMenuVisual();
        if (reportProblemMenuLabel != null)
            reportProblemMenuLabel.text = T("Report a Problem", "Báo cáo sự cố");

        if (reportTitleLabel != null)
            reportTitleLabel.text = T("Report conversation?", "Báo cáo cuộc trò chuyện?");
        if (reportDescriptionLabel != null)
            reportDescriptionLabel.text = T(
                "Use this option to report inappropriate content or a technical issue related to this conversation.",
                "Sử dụng tùy chọn này để báo cáo nội dung không phù hợp hoặc sự cố kỹ thuật liên quan đến cuộc trò chuyện này.");
        if (reportCancelButton != null) reportCancelButton.text = T("Cancel", "Hủy");
        if (reportConfirmButton != null) reportConfirmButton.text = T("Report", "Báo cáo");

        if (profileHeaderTitleLabel != null) profileHeaderTitleLabel.text = T("Instructor Profile", "Hồ sơ giảng viên");
        if (profileAboutHeadingLabel != null) profileAboutHeadingLabel.text = T("ABOUT", "GIỚI THIỆU");
        if (profileAboutTextLabel != null) profileAboutTextLabel.text = T(
            "Instructor in this class. Use chat to ask questions and discuss course materials.",
            "Giảng viên của lớp học này. Bạn có thể nhắn tin để đặt câu hỏi và trao đổi về tài liệu học tập.");
        if (profileTeachingHeadingLabel != null) profileTeachingHeadingLabel.text = T("TEACHING", "GIẢNG DẠY");
        UpdateProfileClassName();
        if (profileCourseSubtitleLabel != null)
            profileCourseSubtitleLabel.text = T("Current class", "Lớp học hiện tại");
        if (profileStatusHeadingLabel != null) profileStatusHeadingLabel.text = T("CURRENT STATUS", "TRẠNG THÁI HIỆN TẠI");
        if (profileMessageLabel != null) profileMessageLabel.text = T("Message", "Nhắn tin");
        if (profileCallLabel != null) profileCallLabel.text = T("Voice Call", "Gọi thoại");

        if (voiceCallHeaderLabel != null)
            voiceCallHeaderLabel.text = T("Voice Call", "Cuộc gọi thoại");
        if (voiceCallPartnerNameLabel != null)
            voiceCallPartnerNameLabel.text = string.IsNullOrWhiteSpace(partnerName)
                ? T("Chat user", "Người dùng")
                : partnerName;
        if (voiceCallDeclineLabel != null) voiceCallDeclineLabel.text = T("Decline", "Từ chối");
        if (voiceCallAnswerLabel != null) voiceCallAnswerLabel.text = T("Answer", "Trả lời");
        if (voiceCallEndLabel != null) voiceCallEndLabel.text = T("End Call", "Kết thúc");
        if (voiceCallSpeakerLabel != null)
            voiceCallSpeakerLabel.text = voiceCallSpeakerEnabled
                ? T("Speaker", "Loa")
                : T("Earpiece", "Tai nghe");
        if (voiceCallMuteLabel != null)
            voiceCallMuteLabel.text = voiceCallMuted
                ? T("Unmute", "Bật mic")
                : T("Mute", "Tắt mic");
        if (voiceCallNoteLabel != null)
            voiceCallNoteLabel.text = T(
                "Voice is connected securely inside the app.",
                "Cuộc gọi thoại được kết nối trực tiếp trong ứng dụng."
            );
        UpdateVoiceCallOverlayText();

        if (fileManagerTitleLabel != null) fileManagerTitleLabel.text = T("File Manager", "Quản lý tập tin");
        if (fileManagerSearchPlaceholder != null)
            fileManagerSearchPlaceholder.text = T("Search shared content...", "Tìm kiếm nội dung đã chia sẻ...");
        if (fileManagerPhotosLabel != null) fileManagerPhotosLabel.text = T("Photos", "Ảnh");
        if (fileManagerFilesLabel != null) fileManagerFilesLabel.text = T("Files", "Tệp");
        if (fileManagerLinksLabel != null) fileManagerLinksLabel.text = T("Links", "Liên kết");
        if (fileManagerOverlay != null && fileManagerOverlay.ClassListContains("visible"))
            RenderFileManager();

        UpdateAttachmentButtonState();
        UpdateInputState();

        if (typingLabel != null &&
            typingRow != null &&
            typingRow.resolvedStyle.display != DisplayStyle.None)
        {
            typingLabel.text = T(
                partnerName + " is typing...",
                partnerName + " đang nhập..."
            );
        }

        if (teacherPositionLabel != null)
            teacherPositionLabel.text = LocalizeRole(partnerRole);

        if (teacherStatusLabel != null && !initialized)
            teacherStatusLabel.text = T("Offline", "Ngoại tuyến");
    }

    private static string LocalizeRole(string role)
    {
        string normalized =
            string.IsNullOrWhiteSpace(role)
                ? "user"
                : role.Trim().ToLowerInvariant();

        return normalized switch
        {
            "teacher" => T("Teacher", "Giáo viên"),
            "giáo viên" => T("Teacher", "Giáo viên"),

            "student" => T("Student", "Sinh viên"),
            "học sinh" => T("Student", "Sinh viên"),
            "sinh viên" => T("Student", "Sinh viên"),

            "admin" => T("Admin", "Quản trị"),
            "quản trị viên" => T("Admin", "Quản trị"),

            "user" => T("User", "Người dùng"),
            "người dùng" => T("User", "Người dùng"),

            _ => role
        };
    }

    private void RegisterCallbacks()
    {
        if (backButton != null) backButton.clicked += HandleBackClicked;
        if (callButton != null) callButton.clicked += HandleCallClicked;
        if (moreButton != null) moreButton.clicked += HandleMoreClicked;
        if (moreMenuScrim != null) moreMenuScrim.RegisterCallback<PointerDownEvent>(HandleMoreMenuScrimPointerDown);
        if (viewProfileMenuItem != null) viewProfileMenuItem.clicked += HandleViewProfileMenuClicked;
        if (sharedFilesMenuItem != null) sharedFilesMenuItem.clicked += HandleSharedFilesMenuClicked;
        if (notificationsMenuItem != null) notificationsMenuItem.clicked += HandleNotificationsMenuClicked;
        if (reportProblemMenuItem != null) reportProblemMenuItem.clicked += HandleReportProblemMenuClicked;
        if (reportCancelButton != null) reportCancelButton.clicked += HandleReportCancelClicked;
        if (reportConfirmButton != null) reportConfirmButton.clicked += HandleReportConfirmClicked;
        if (profileBackButton != null) profileBackButton.clicked += HandleProfileBackClicked;
        if (profileMessageButton != null) profileMessageButton.clicked += HandleProfileMessageClicked;
        if (profileCallButton != null) profileCallButton.clicked += HandleCallClicked;
        if (voiceCallDeclineButton != null) voiceCallDeclineButton.clicked += HandleVoiceCallDeclineClicked;
        if (voiceCallAnswerButton != null) voiceCallAnswerButton.clicked += HandleVoiceCallAnswerClicked;
        if (voiceCallEndButton != null) voiceCallEndButton.clicked += HandleVoiceCallEndClicked;
        if (voiceCallSpeakerButton != null) voiceCallSpeakerButton.clicked += HandleVoiceCallSpeakerClicked;
        if (voiceCallMuteButton != null) voiceCallMuteButton.clicked += HandleVoiceCallMuteClicked;
        if (fileManagerBackButton != null) fileManagerBackButton.clicked += HandleFileManagerBackClicked;
        if (fileManagerPhotosTab != null) fileManagerPhotosTab.clicked += HandleFileManagerPhotosTabClicked;
        if (fileManagerFilesTab != null) fileManagerFilesTab.clicked += HandleFileManagerFilesTabClicked;
        if (fileManagerLinksTab != null) fileManagerLinksTab.clicked += HandleFileManagerLinksTabClicked;
        if (fileManagerSearchInput != null) fileManagerSearchInput.RegisterValueChangedCallback(HandleFileManagerSearchChanged);
        if (attachmentButton != null) attachmentButton.clicked += HandleAttachmentClicked;
        if (closeImageGalleryButton != null) closeImageGalleryButton.clicked += CloseInlineGallery;
        if (sendButton != null) sendButton.clicked += SendCurrentMessage;
        if (messageInput != null)
        {
            messageInput.RegisterValueChangedCallback(HandleInputValueChanged);
            messageInput.RegisterCallback<KeyDownEvent>(HandleInputKeyDown);
            messageInput.RegisterCallback<FocusInEvent>(OnChatInputFocused);
            messageInput.RegisterCallback<FocusOutEvent>(OnChatInputBlurred);
        }
        if (messageSearchButton != null) messageSearchButton.clicked += HandleMessageSearchClicked;
        if (messageSearchInput != null)
        {
            messageSearchInput.RegisterValueChangedCallback(HandleMessageSearchChanged);
            messageSearchInput.RegisterCallback<KeyDownEvent>(HandleMessageSearchKeyDown);
        }
    }

    private void UnregisterCallbacks()
    {
        if (backButton != null) backButton.clicked -= HandleBackClicked;
        if (callButton != null) callButton.clicked -= HandleCallClicked;
        if (moreButton != null) moreButton.clicked -= HandleMoreClicked;
        if (moreMenuScrim != null) moreMenuScrim.UnregisterCallback<PointerDownEvent>(HandleMoreMenuScrimPointerDown);
        if (viewProfileMenuItem != null) viewProfileMenuItem.clicked -= HandleViewProfileMenuClicked;
        if (sharedFilesMenuItem != null) sharedFilesMenuItem.clicked -= HandleSharedFilesMenuClicked;
        if (notificationsMenuItem != null) notificationsMenuItem.clicked -= HandleNotificationsMenuClicked;
        if (reportProblemMenuItem != null) reportProblemMenuItem.clicked -= HandleReportProblemMenuClicked;
        if (reportCancelButton != null) reportCancelButton.clicked -= HandleReportCancelClicked;
        if (reportConfirmButton != null) reportConfirmButton.clicked -= HandleReportConfirmClicked;
        if (profileBackButton != null) profileBackButton.clicked -= HandleProfileBackClicked;
        if (profileMessageButton != null) profileMessageButton.clicked -= HandleProfileMessageClicked;
        if (profileCallButton != null) profileCallButton.clicked -= HandleCallClicked;
        if (voiceCallDeclineButton != null) voiceCallDeclineButton.clicked -= HandleVoiceCallDeclineClicked;
        if (voiceCallAnswerButton != null) voiceCallAnswerButton.clicked -= HandleVoiceCallAnswerClicked;
        if (voiceCallEndButton != null) voiceCallEndButton.clicked -= HandleVoiceCallEndClicked;
        if (voiceCallSpeakerButton != null) voiceCallSpeakerButton.clicked -= HandleVoiceCallSpeakerClicked;
        if (voiceCallMuteButton != null) voiceCallMuteButton.clicked -= HandleVoiceCallMuteClicked;
        if (fileManagerBackButton != null) fileManagerBackButton.clicked -= HandleFileManagerBackClicked;
        if (fileManagerPhotosTab != null) fileManagerPhotosTab.clicked -= HandleFileManagerPhotosTabClicked;
        if (fileManagerFilesTab != null) fileManagerFilesTab.clicked -= HandleFileManagerFilesTabClicked;
        if (fileManagerLinksTab != null) fileManagerLinksTab.clicked -= HandleFileManagerLinksTabClicked;
        if (fileManagerSearchInput != null) fileManagerSearchInput.UnregisterValueChangedCallback(HandleFileManagerSearchChanged);
        if (attachmentButton != null) attachmentButton.clicked -= HandleAttachmentClicked;
        if (closeImageGalleryButton != null) closeImageGalleryButton.clicked -= CloseInlineGallery;
        if (sendButton != null) sendButton.clicked -= SendCurrentMessage;
        if (messageInput != null)
        {
            messageInput.UnregisterValueChangedCallback(HandleInputValueChanged);
            messageInput.UnregisterCallback<KeyDownEvent>(HandleInputKeyDown);
            messageInput.UnregisterCallback<FocusInEvent>(OnChatInputFocused);
            messageInput.UnregisterCallback<FocusOutEvent>(OnChatInputBlurred);
        }
        if (messageSearchButton != null) messageSearchButton.clicked -= HandleMessageSearchClicked;
        if (messageSearchInput != null)
        {
            messageSearchInput.UnregisterValueChangedCallback(HandleMessageSearchChanged);
            messageSearchInput.UnregisterCallback<KeyDownEvent>(HandleMessageSearchKeyDown);
        }
    }

    private void ConfigureInitialUi()
    {
        if (messageInput != null) { messageInput.value = string.Empty; messageInput.isDelayed = false; }
        if (messageSearchInput != null) { messageSearchInput.value = string.Empty; messageSearchInput.isDelayed = false; }
        messageSearchQuery = string.Empty;
        UpdateMessageSearchPlaceholder();
        if (typingRow != null) typingRow.style.display = DisplayStyle.None;
        SetMoreMenuVisible(false);
        if (reportOverlay != null) reportOverlay.EnableInClassList("visible", false);
        if (profileOverlay != null) profileOverlay.EnableInClassList("visible", false);
        if (voiceCallOverlay != null) voiceCallOverlay.EnableInClassList("visible", false);
        if (fileManagerOverlay != null) fileManagerOverlay.EnableInClassList("visible", false);
        if (fileManagerSearchInput != null) { fileManagerSearchInput.value = string.Empty; fileManagerSearchInput.isDelayed = false; }
        CloseInlineGallery();
        fileManagerSearchQuery = string.Empty;
        UpdateInputState();
        UpdateAttachmentButtonState();
    }

    private void UpdateInputState()
    {
        bool hasText =
            messageInput != null &&
            !string.IsNullOrWhiteSpace(messageInput.value);
        bool canSend = hasText || selectedChatImages.Count > 0;

        if (inputPlaceholder != null)
            inputPlaceholder.style.display =
                hasText ? DisplayStyle.None : DisplayStyle.Flex;

        if (sendButton != null)
        {
            // Không có text:
            // - nền nút nhạt
            // - send.png (đen)
            //
            // Có text:
            // - nền nút xanh tròn
            // - send-white.png
            sendButton.EnableInClassList("enabled", canSend);
            sendButton.SetEnabled(canSend && initialized && !imageUploadInProgress);
        }

        if (sendIcon != null)
            sendIcon.EnableInClassList("send-icon-active", canSend);

        if (imageGalleryTitle != null)
        {
            string title = T("Recent photos", "Ảnh gần đây");
            imageGalleryTitle.text = selectedChatImages.Count > 0
                ? $"{title} ({selectedChatImages.Count}/{MaxSelectedImages})"
                : title;
        }
    }

    private void HandleMessageSearchClicked()
    {
        ExecuteMessageSearch();

        if (messageSearchInput != null)
            messageSearchInput.Focus();
    }

    private void HandleMessageSearchKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            return;

        evt.StopPropagation();
        ExecuteMessageSearch();
    }

    private void ExecuteMessageSearch()
    {
        messageSearchQuery = messageSearchInput != null
            ? messageSearchInput.value?.Trim() ?? string.Empty
            : string.Empty;

        UpdateMessageSearchPlaceholder();
        RenderMessages(FilterMessages(latestMessages, messageSearchQuery));
    }

    private void HandleMessageSearchChanged(ChangeEvent<string> evt)
    {
        messageSearchQuery = evt.newValue?.Trim() ?? string.Empty;
        UpdateMessageSearchPlaceholder();
        RenderMessages(FilterMessages(latestMessages, messageSearchQuery));
    }

    private void UpdateMessageSearchPlaceholder()
    {
        if (messageSearchPlaceholder == null) return;

        bool hasSearchText =
            messageSearchInput != null &&
            !string.IsNullOrWhiteSpace(messageSearchInput.value);

        messageSearchPlaceholder.style.display =
            hasSearchText ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private static ChatMessage[] FilterMessages(ChatMessage[] messages, string query)
    {
        if (messages == null || messages.Length == 0)
            return Array.Empty<ChatMessage>();

        if (string.IsNullOrWhiteSpace(query))
            return messages;

        string normalizedQuery = query.Trim();
        List<ChatMessage> matches = new List<ChatMessage>();

        foreach (ChatMessage message in messages)
        {
            if (message == null) continue;

            // Search text messages by their visible content. Image-only messages are
            // ignored unless their content/path itself happens to match the query.
            if (!string.IsNullOrWhiteSpace(message.content) &&
                message.content.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matches.Add(message);
            }
        }

        return matches.ToArray();
    }

    private void HandleBackClicked()
    {
        StartCoroutine(SetTypingState(false));
        StartCoroutine(SetPresence(false));
        string scene = PlayerPrefs.GetString("previous_scene", previousSceneName);
        if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
        else Debug.LogError("[ChatPageController] Scene not found in Build Profiles: " + scene);
    }

    private void HandleCallClicked()
    {
        if (profileOverlay != null)
            profileOverlay.EnableInClassList("visible", false);

        if (!initialized || string.IsNullOrWhiteSpace(conversationId))
        {
            Debug.LogWarning("[ChatPageController] Cannot start a call before the conversation is ready.");
            return;
        }

        if (activeVoiceCall != null &&
            !IsTerminalVoiceCallStatus(activeVoiceCall.status))
        {
            ShowVoiceCallOverlay(activeVoiceCallIsIncoming);
            return;
        }

        StartCoroutine(StartInAppVoiceCallRoutine());
    }

    private void InitializeVoiceCallService()
    {
        if (agoraVoiceService == null)
            agoraVoiceService = FindFirstObjectByType<AgoraTest>();

        if (agoraVoiceService == null)
        {
            Debug.LogError(
                "[ChatPageController] AgoraTest component was not found. " +
                "Keep the AgoraTest GameObject active in ChatScene.");
            return;
        }

        DetachAgoraVoiceEvents();
        agoraVoiceService.JoinedChannel += HandleAgoraJoinedChannel;
        agoraVoiceService.RemoteUserJoined += HandleAgoraRemoteUserJoined;
        agoraVoiceService.RemoteUserLeft += HandleAgoraRemoteUserLeft;
        agoraVoiceService.AgoraError += HandleAgoraVoiceError;
    }

    private void DetachAgoraVoiceEvents()
    {
        if (agoraVoiceService == null)
            return;

        agoraVoiceService.JoinedChannel -= HandleAgoraJoinedChannel;
        agoraVoiceService.RemoteUserJoined -= HandleAgoraRemoteUserJoined;
        agoraVoiceService.RemoteUserLeft -= HandleAgoraRemoteUserLeft;
        agoraVoiceService.AgoraError -= HandleAgoraVoiceError;
    }

    private IEnumerator VoiceCallPollingLoop()
    {
        while (initialized)
        {
            yield return new WaitForSeconds(voiceCallPollInterval);
            yield return PollVoiceCallState();
        }
    }

    private IEnumerator PollVoiceCallState()
    {
        if (!initialized ||
            string.IsNullOrWhiteSpace(currentUserId) ||
            string.IsNullOrWhiteSpace(conversationId))
            yield break;

        string response = null;

        if (voiceCallActionRunning || voiceCallStarting) yield break;

        if (activeVoiceCall != null && !string.IsNullOrWhiteSpace(activeVoiceCall.id))
        {
            string activePath =
                "/rest/v1/voice_calls?id=eq." +
                UnityWebRequest.EscapeURL(activeVoiceCall.id) +
                "&select=id,conversation_id,caller_id,receiver_id,channel_name,status,started_at,answered_at,ended_at&limit=1";

            yield return SendRequest("GET", activePath, null, value => response = value);

            if (response == null) yield break; // Network/auth failure is not a hang-up.
            VoiceCallRecord[] activeRecords =
                ParseArray<VoiceCallArray, VoiceCallRecord>(response, x => x.items);

            if (activeRecords.Length == 0)
            {
                ResetVoiceCallLocalState(true);
                yield break;
            }

            ApplyVoiceCallRecord(activeRecords[0]);

            if (IsTerminalVoiceCallStatus(activeVoiceCall.status))
            {
                FinishCallLocally(activeVoiceCall);
                yield break;
            }

            if (string.Equals(activeVoiceCall.status, "accepted", StringComparison.OrdinalIgnoreCase))
            {
                EnsureAgoraJoinedForActiveCall();
            }
            else if (string.Equals(activeVoiceCall.status, "ringing", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(activeVoiceCall.caller_id, currentUserId, StringComparison.OrdinalIgnoreCase) &&
                     IsVoiceCallOlderThan(activeVoiceCall, voiceCallRingTimeoutSeconds))
            {
                // Caller owns the timeout transition so both devices converge on "missed".
                // Recheck the server before timing out: the receiver may have
                // accepted while this device was waiting for its previous GET.
                string timedOutCallId = activeVoiceCall.id;
                string latestResponse = null;
                yield return SendRequest("GET", activePath, null, value => latestResponse = value);
                VoiceCallRecord[] latest = ParseArray<VoiceCallArray, VoiceCallRecord>(
                    latestResponse, x => x.items);
                if (latestResponse == null || latest.Length == 0) yield break;
                if (!string.Equals(latest[0].status, "ringing", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyVoiceCallRecord(latest[0]);
                    if (string.Equals(latest[0].status, "accepted", StringComparison.OrdinalIgnoreCase))
                        EnsureAgoraJoinedForActiveCall();
                    else if (IsTerminalVoiceCallStatus(latest[0].status))
                        FinishCallLocally(latest[0]);
                    yield break;
                }
                string timeoutResponse = null;
                yield return SendRequest("PATCH",
                    "/rest/v1/voice_calls?id=eq." + UnityWebRequest.EscapeURL(timedOutCallId) +
                    "&status=eq.ringing",
                    JsonUtility.ToJson(new VoiceCallStatusPatchBody { status = "missed" }),
                    value => timeoutResponse = value, "return=representation");
                // An empty representation means the receiver already answered.
                if (!string.IsNullOrWhiteSpace(timeoutResponse) && timeoutResponse != "[]")
                {
                    VoiceCallRecord[] timedOut = ParseArray<VoiceCallArray, VoiceCallRecord>(
                        timeoutResponse, x => x.items);
                    if (timedOut.Length > 0) FinishCallLocally(timedOut[0]);
                }
            }

            yield break;
        }

        string path =
            "/rest/v1/voice_calls?conversation_id=eq." +
            UnityWebRequest.EscapeURL(conversationId) +
            "&or=(caller_id.eq." + UnityWebRequest.EscapeURL(currentUserId) +
            ",receiver_id.eq." + UnityWebRequest.EscapeURL(currentUserId) + ")" +
            "&status=in.(ringing,accepted)" +
            "&select=id,conversation_id,caller_id,receiver_id,channel_name,status,started_at,answered_at,ended_at" +
            "&order=started_at.desc&limit=1";

        yield return SendRequest("GET", path, null, value => response = value);

        VoiceCallRecord[] records =
            ParseArray<VoiceCallArray, VoiceCallRecord>(response, x => x.items);

        if (records.Length == 0)
            yield break;

        if (string.Equals(records[0].id, lastFinishedCallId, StringComparison.OrdinalIgnoreCase))
            yield break;
        if (string.Equals(records[0].status, "ringing", StringComparison.OrdinalIgnoreCase) &&
            IsVoiceCallOlderThan(records[0], voiceCallRingTimeoutSeconds + 15f))
            yield break;
        ApplyVoiceCallRecord(records[0]);

        if (string.Equals(activeVoiceCall.status, "accepted", StringComparison.OrdinalIgnoreCase))
            EnsureAgoraJoinedForActiveCall();
    }

    private IEnumerator StartInAppVoiceCallRoutine()
    {
        if (voiceCallStarting || voiceCallActionRunning) yield break;
        voiceCallStarting = true;
        if (agoraVoiceService == null)
            InitializeVoiceCallService();

        if (agoraVoiceService == null)
        {
            ShowError(T(
                "Agora voice service is not available.",
                "Dịch vụ gọi thoại Agora chưa sẵn sàng."));
            voiceCallStarting = false;
            yield break;
        }

        VoiceCallInsertBody body = new VoiceCallInsertBody
        {
            conversation_id = conversationId,
            caller_id = currentUserId,
            receiver_id = partnerUserId,
            // Current temporary RTC tokens were generated for this configured channel.
            channel_name = "call_" + Guid.NewGuid().ToString("N"),
            status = "ringing"
        };

        string response = null;
        yield return SendRequest(
            "POST",
            "/rest/v1/voice_calls",
            JsonUtility.ToJson(body),
            value => response = value,
            "return=representation"
        );

        VoiceCallRecord[] records =
            ParseArray<VoiceCallArray, VoiceCallRecord>(response, x => x.items);

        if (records.Length == 0)
        {
            voiceCallStarting = false;
            ShowError(T(
                "Could not start the voice call.",
                "Không thể bắt đầu cuộc gọi thoại."));
            yield break;
        }

        voiceCallStarting = false;
        ApplyVoiceCallRecord(records[0]);
        ShowVoiceCallOverlay(false);

        // Wait until the receiver accepts before requesting the microphone / joining RTC.
        // Signaling (Supabase) and media (Agora) must not be conflated.
        Debug.Log("[VoiceCall] Ringing; waiting for receiver acceptance before Agora join.");
    }

    private void ApplyVoiceCallRecord(VoiceCallRecord record)
    {
        if (record == null)
            return;

        bool callChanged =
            activeVoiceCall == null ||
            !string.Equals(activeVoiceCall.id, record.id, StringComparison.OrdinalIgnoreCase);

        activeVoiceCall = record;
        activeVoiceCallIsIncoming =
            string.Equals(record.receiver_id, currentUserId, StringComparison.OrdinalIgnoreCase);

        if (callChanged)
        {
            voiceCallConnected = false;
            voiceCallRemoteUserPresent = false;
            voiceCallMuted = false;
            voiceCallSpeakerEnabled = true;
            voiceCallConnectedAt = default;
        }

        // Global banner owns ringing incoming UI; do not open a second Accept screen.
        if (!activeVoiceCallIsIncoming ||
            string.Equals(record.status, "accepted", StringComparison.OrdinalIgnoreCase))
            ShowVoiceCallOverlay(activeVoiceCallIsIncoming);
        UpdateVoiceCallOverlayText();
    }

    private void ShowVoiceCallOverlay(bool incoming)
    {
        if (voiceCallOverlay == null)
            return;

        activeVoiceCallIsIncoming = incoming;

        if (voiceCallPartnerNameLabel != null)
            voiceCallPartnerNameLabel.text =
                string.IsNullOrWhiteSpace(partnerName)
                    ? T("Chat user", "Người dùng")
                    : partnerName;

        if (voiceCallAvatarTextLabel != null)
            voiceCallAvatarTextLabel.text = GetInitials(partnerName);

        bool ringing =
            activeVoiceCall != null &&
            string.Equals(activeVoiceCall.status, "ringing", StringComparison.OrdinalIgnoreCase);

        bool accepted =
            activeVoiceCall != null &&
            string.Equals(activeVoiceCall.status, "accepted", StringComparison.OrdinalIgnoreCase);

        bool showIncomingButtons = incoming && ringing;
        bool showEndButton = !showIncomingButtons;
        bool showAudioControls = accepted;

        if (voiceCallIncomingActions != null)
            voiceCallIncomingActions.style.display =
                showIncomingButtons ? DisplayStyle.Flex : DisplayStyle.None;

        if (voiceCallOutgoingActions != null)
            voiceCallOutgoingActions.style.display =
                showEndButton ? DisplayStyle.Flex : DisplayStyle.None;

        if (voiceCallActiveControls != null)
            voiceCallActiveControls.EnableInClassList("visible", showAudioControls);

        UpdateVoiceCallControlVisuals();
        UpdateVoiceCallOverlayText();
        voiceCallOverlay.EnableInClassList("visible", true);
    }

    private void UpdateVoiceCallOverlayText()
    {
        if (voiceCallStatusLabel == null)
            return;

        if (activeVoiceCall == null)
        {
            voiceCallStatusLabel.text = T("Voice call", "Cuộc gọi thoại");
            if (voiceCallTimerLabel != null) voiceCallTimerLabel.text = string.Empty;
            return;
        }

        string status = activeVoiceCall.status ?? string.Empty;

        if (string.Equals(status, "ringing", StringComparison.OrdinalIgnoreCase))
        {
            voiceCallStatusLabel.text = activeVoiceCallIsIncoming
                ? T("Incoming voice call", "Cuộc gọi thoại đến")
                : T("Calling...", "Đang gọi...");
        }
        else if (string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            voiceCallStatusLabel.text = voiceCallConnected && agoraVoiceService != null && agoraVoiceService.IsJoined
                ? T("Connected", "Đã kết nối")
                : T("Call answered · Connecting audio...", "Đã nhận cuộc gọi · Đang kết nối âm thanh...");
        }
        else if (string.Equals(status, "declined", StringComparison.OrdinalIgnoreCase))
        {
            voiceCallStatusLabel.text = T("Call declined", "Cuộc gọi bị từ chối");
        }
        else if (string.Equals(status, "missed", StringComparison.OrdinalIgnoreCase))
        {
            voiceCallStatusLabel.text = T("No answer", "Không có phản hồi");
        }
        else
        {
            voiceCallStatusLabel.text = T("Call ended", "Cuộc gọi đã kết thúc");
        }

        UpdateVoiceCallTimer();
    }

    private void UpdateVoiceCallTimer()
    {
        if (voiceCallTimerLabel == null)
            return;

        if (activeVoiceCall == null ||
            !string.Equals(activeVoiceCall.status, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            voiceCallTimerLabel.text = string.Empty;
            return;
        }

        // The signaling acceptance time is NOT proof that Agora audio is connected.
        // Keep the timer empty until the remote RTC participant is observed.
        if (!voiceCallConnected || agoraVoiceService == null || !agoraVoiceService.IsJoined)
        {
            voiceCallTimerLabel.text = "--:--";
            return;
        }
        DateTime answeredAt;
        if (!DateTime.TryParse(activeVoiceCall.answered_at, out answeredAt))
        {
            voiceCallTimerLabel.text = "00:00";
            return;
        }
        voiceCallConnectedAt = answeredAt.ToUniversalTime();
        TimeSpan elapsed = DateTime.UtcNow - voiceCallConnectedAt;
        int totalSeconds = Mathf.Max(0, (int)elapsed.TotalSeconds);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        voiceCallTimerLabel.text = $"{minutes:00}:{seconds:00}";
    }

    private void EnsureAgoraJoinedForActiveCall()
    {
        if (activeVoiceCall == null || agoraVoiceService == null)
            return;

        if (!agoraVoiceService.IsJoined && !agoraTokenRequestRunning &&
            Time.unscaledTime >= agoraTokenRetryAfter)
            StartCoroutine(RequestAgoraTokenAndJoin(activeVoiceCall.id));

        bool accepted =
            activeVoiceCall != null &&
            string.Equals(activeVoiceCall.status, "accepted", StringComparison.OrdinalIgnoreCase);

        if (voiceCallActiveControls != null)
            voiceCallActiveControls.EnableInClassList("visible", accepted);
    }

    private IEnumerator RequestAgoraTokenAndJoin(string callId)
    {
        if (agoraTokenRequestRunning || activeVoiceCall == null ||
            !string.Equals(activeVoiceCall.id, callId, StringComparison.OrdinalIgnoreCase))
            yield break;
        agoraTokenRequestRunning = true;
        agoraTokenRequestedCallId = callId;
        Debug.Log("[VoiceCall] Requesting Agora token for call=" + callId);
        // Prevent polling from launching another request while the current join is in progress.
        agoraTokenRetryAfter = Time.unscaledTime + 15f;
        string response = null;
        yield return SendRequest("POST", "/functions/v1/agora-token",
            JsonUtility.ToJson(new AgoraTokenRequestBody { call_id = callId }),
            value => response = value);
        agoraTokenRequestRunning = false;
        if (activeVoiceCall == null || activeVoiceCall.id != callId ||
            !string.Equals(activeVoiceCall.status, "accepted", StringComparison.OrdinalIgnoreCase))
            yield break;
        if (string.IsNullOrWhiteSpace(response))
        {
            if (voiceCallStatusLabel != null)
                voiceCallStatusLabel.text = T("Cannot get Agora token; retrying...", "Không lấy được token Agora; đang thử lại...");
            yield break;
        }
        AgoraTokenResponse credentials = null;
        try { credentials = JsonUtility.FromJson<AgoraTokenResponse>(response); }
        catch (Exception ex) { Debug.LogError("[VoiceCall] Invalid token response: " + ex.Message); }
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.token) ||
            string.IsNullOrWhiteSpace(credentials.app_id) || credentials.uid == 0 ||
            !string.Equals(credentials.channel_name, activeVoiceCall.channel_name, StringComparison.Ordinal))
        {
            Debug.LogError("[VoiceCall] Agora token response missing fields or channel mismatch.");
            yield break;
        }
        Debug.Log($"[VoiceCall] Agora token received: channel={credentials.channel_name}, uid={credentials.uid}, expires_in={credentials.expires_in}s");
        agoraVoiceService.JoinVoiceChannel(credentials.channel_name, credentials.token,
            credentials.uid, credentials.app_id);
    }

    private void HandleVoiceCallAnswerClicked()
    {
        if (activeVoiceCall == null ||
            !activeVoiceCallIsIncoming ||
            !string.Equals(activeVoiceCall.status, "ringing", StringComparison.OrdinalIgnoreCase))
            return;

        if (voiceCallActionRunning) return;
        StartCoroutine(AcceptVoiceCallRoutine());
    }

    private IEnumerator AcceptVoiceCallRoutine()
    {
        if (voiceCallActionRunning || activeVoiceCall == null) yield break;
        voiceCallActionRunning = true;
        VoiceCallAcceptedPatchBody body = new VoiceCallAcceptedPatchBody
        {
            status = "accepted",
            answered_at = DateTime.UtcNow.ToString("o")
        };

        bool success = false;
        string path =
            "/rest/v1/voice_calls?id=eq." +
            UnityWebRequest.EscapeURL(activeVoiceCall.id);

        yield return SendRequest(
            "PATCH",
            path,
            JsonUtility.ToJson(body),
            _ => success = true,
            "return=representation"
        );

        if (!success)
        {
            voiceCallActionRunning = false;
            ShowError(T(
                "Could not answer the call.",
                "Không thể trả lời cuộc gọi."));
            yield break;
        }

        activeVoiceCall.status = "accepted";
        activeVoiceCall.answered_at = body.answered_at;
        voiceCallActionRunning = false;
        ShowVoiceCallOverlay(true);
        EnsureAgoraJoinedForActiveCall();
    }

    private void HandleVoiceCallDeclineClicked()
    {
        if (activeVoiceCall == null)
        {
            ResetVoiceCallLocalState(true);
            return;
        }

        if (voiceCallActionRunning) return;
        voiceCallActionRunning = true;
        string callId = activeVoiceCall.id;
        activeVoiceCall.status = "declined";
        FinishCallLocally(activeVoiceCall);
        StartCoroutine(DeclineVoiceCallRoutine(callId));
    }

    private IEnumerator DeclineVoiceCallRoutine(string callId)
    {
        yield return PatchVoiceCallStatus(callId, "declined");
        voiceCallActionRunning = false;
        yield return LoadCallHistory();
    }

    private void HandleVoiceCallEndClicked()
    {
        if (activeVoiceCall == null)
        {
            ResetVoiceCallLocalState(true);
            return;
        }

        if (voiceCallActionRunning) return;
        voiceCallActionRunning = true;
        string callId = activeVoiceCall.id;
        activeVoiceCall.status = voiceCallConnected || !string.IsNullOrWhiteSpace(activeVoiceCall.answered_at)
            ? "ended" : "missed";
        string status = activeVoiceCall.status;
        FinishCallLocally(activeVoiceCall);
        if (status == "missed") StartCoroutine(DeclineVoiceCallRoutineForMissed(callId));
        else StartCoroutine(EndVoiceCallRoutine(callId));
    }

    private IEnumerator EndVoiceCallRoutine(string callId)
    {
        VoiceCallEndedPatchBody body = new VoiceCallEndedPatchBody
        {
            status = "ended",
            ended_at = DateTime.UtcNow.ToString("o")
        };

        string path =
            "/rest/v1/voice_calls?id=eq." +
            UnityWebRequest.EscapeURL(callId);

        yield return SendRequest(
            "PATCH",
            path,
            JsonUtility.ToJson(body),
            null,
            "return=minimal"
        );

        voiceCallActionRunning = false;
        yield return LoadCallHistory();
    }

    private IEnumerator DeclineVoiceCallRoutineForMissed(string callId)
    {
        yield return PatchVoiceCallStatus(callId, "missed");
        voiceCallActionRunning = false;
        yield return LoadCallHistory();
    }

    private IEnumerator PatchVoiceCallStatus(string callId, string status)
    {
        if (string.IsNullOrWhiteSpace(callId))
            yield break;

        VoiceCallStatusPatchBody body = new VoiceCallStatusPatchBody
        {
            status = status
        };

        string path =
            "/rest/v1/voice_calls?id=eq." +
            UnityWebRequest.EscapeURL(callId) +
            (string.Equals(status, "missed", StringComparison.OrdinalIgnoreCase)
                ? "&status=eq.ringing" : string.Empty);

        yield return SendRequest(
            "PATCH",
            path,
            JsonUtility.ToJson(body),
            null,
            "return=minimal"
        );
    }

    private void HandleVoiceCallSpeakerClicked()
    {
        voiceCallSpeakerEnabled = !voiceCallSpeakerEnabled;

        if (agoraVoiceService != null)
            agoraVoiceService.SetSpeakerEnabled(voiceCallSpeakerEnabled);

        UpdateVoiceCallControlVisuals();
    }

    private void HandleVoiceCallMuteClicked()
    {
        voiceCallMuted = !voiceCallMuted;

        if (agoraVoiceService != null)
            agoraVoiceService.SetMuted(voiceCallMuted);

        UpdateVoiceCallControlVisuals();
    }

    private void UpdateVoiceCallControlVisuals()
    {
        if (voiceCallSpeakerButton != null)
            voiceCallSpeakerButton.EnableInClassList("active", voiceCallSpeakerEnabled);

        if (voiceCallMuteButton != null)
            voiceCallMuteButton.EnableInClassList("active", voiceCallMuted);

        if (voiceCallSpeakerLabel != null)
            voiceCallSpeakerLabel.text = voiceCallSpeakerEnabled
                ? T("Speaker", "Loa")
                : T("Earpiece", "Tai nghe");

        if (voiceCallMuteLabel != null)
            voiceCallMuteLabel.text = voiceCallMuted
                ? T("Unmute", "Bật mic")
                : T("Mute", "Tắt mic");
    }

    private void HandleAgoraJoinedChannel(string channel, uint uid)
    {
        Debug.Log($"[ChatPageController] Agora joined channel={channel}, uid={uid}");

        bool accepted =
            activeVoiceCall != null &&
            string.Equals(activeVoiceCall.status, "accepted", StringComparison.OrdinalIgnoreCase);

        if (voiceCallActiveControls != null)
            voiceCallActiveControls.EnableInClassList("visible", accepted);

        UpdateVoiceCallControlVisuals();
        UpdateVoiceCallOverlayText();
    }

    private void HandleAgoraRemoteUserJoined(uint uid)
    {
        if (activeVoiceCall == null || voiceCallActionRunning) return;
        voiceCallRemoteUserPresent = true;

        if (!voiceCallConnected && agoraVoiceService != null && agoraVoiceService.IsJoined)
        {
            voiceCallConnected = true;
            voiceCallConnectedAt = DateTime.UtcNow;
        }

        Debug.Log($"[ChatPageController] Voice call connected to remote Agora UID={uid}");
        ShowVoiceCallOverlay(activeVoiceCallIsIncoming);
        UpdateVoiceCallOverlayText();
    }

    private void HandleAgoraRemoteUserLeft(uint uid)
    {
        voiceCallRemoteUserPresent = false;

        if (activeVoiceCall != null &&
            string.Equals(activeVoiceCall.status, "accepted", StringComparison.OrdinalIgnoreCase))
        {
            string callId = activeVoiceCall.id;
            if (voiceCallActionRunning) return;
            voiceCallActionRunning = true;
            activeVoiceCall.status = "ended";
            FinishCallLocally(activeVoiceCall);
            StartCoroutine(EndVoiceCallRoutine(callId));
        }
    }

    private void HandleAgoraVoiceError(int code, string message)
    {
        // An RTC failure must not change a Supabase ringing/accepted call to ended.
        // In particular, expired temporary Agora tokens must not be reported as
        // "unanswered" calls. Keep the call screen open so the user can hang up.
        Debug.LogError($"[ChatPageController] Agora voice error {code}: {message}");
        agoraTokenRetryAfter = Time.unscaledTime + 12f;
        if (voiceCallStatusLabel != null && activeVoiceCall != null)
        {
            string detail = string.IsNullOrWhiteSpace(message)
                ? $"Agora {code}" : $"Agora {code}: {message}";
            string explanation = code == 109
                ? T("Agora token expired. Generate new RTC tokens for this channel and both UIDs.",
                    "Token Agora đã hết hạn. Tạo lại RTC token cho đúng kênh và cả hai UID.")
                : code == -17 || code == 17
                    ? T("Agora rejected the join request. Check token, channel, UID and whether a join is already running.",
                        "Agora từ chối tham gia kênh. Kiểm tra token, kênh, UID và yêu cầu tham gia đang chạy.")
                    : T("Voice connection error", "Lỗi kết nối âm thanh");
            voiceCallStatusLabel.text = explanation + " (" + detail + ")";
        }
    }

    private void ResetVoiceCallLocalState(bool hideOverlay)
    {
        if (agoraVoiceService != null)
            agoraVoiceService.LeaveVoiceChannel();

        activeVoiceCall = null;
        agoraTokenRequestRunning = false;
        agoraTokenRequestedCallId = string.Empty;
        agoraTokenRetryAfter = 0f;
        PlayerPrefs.DeleteKey("pending_voice_call_id");
        PlayerPrefs.DeleteKey("pending_voice_call_channel");
        PlayerPrefs.DeleteKey("auto_accept_pending_voice_call");
        PlayerPrefs.Save();
        activeVoiceCallIsIncoming = false;
        voiceCallConnected = false;
        voiceCallRemoteUserPresent = false;
        voiceCallConnectedAt = default;
        voiceCallMuted = false;
        voiceCallSpeakerEnabled = true;

        if (voiceCallTimerLabel != null)
            voiceCallTimerLabel.text = string.Empty;

        if (voiceCallActiveControls != null)
            voiceCallActiveControls.EnableInClassList("visible", false);

        if (hideOverlay && voiceCallOverlay != null)
            voiceCallOverlay.EnableInClassList("visible", false);

        UpdateVoiceCallControlVisuals();
    }

    private void FinishCallLocally(VoiceCallRecord record)
    {
        if (record != null)
        {
            lastFinishedCallId = record.id;
            if (string.IsNullOrWhiteSpace(record.ended_at))
                record.ended_at = DateTime.UtcNow.ToString("o");
        }
        ResetVoiceCallLocalState(true);
        // History refresh is done after the server PATCH, not while ending locally.
    }

    public void OpenAcceptedCallFromBanner(string callId)
    {
        if (!initialized || string.IsNullOrWhiteSpace(callId)) return;
        StartCoroutine(ConsumePendingAnsweredCall());
    }

    private IEnumerator ConsumePendingAnsweredCall()
    {
        string pendingId = PlayerPrefs.GetString("pending_voice_call_id", string.Empty);
        bool autoAccept = PlayerPrefs.GetInt("auto_accept_pending_voice_call", 0) == 1;
        PlayerPrefs.DeleteKey("auto_accept_pending_voice_call");
        PlayerPrefs.Save();
        if (!autoAccept || string.IsNullOrWhiteSpace(pendingId)) yield break;
        string response = null;
        string path = "/rest/v1/voice_calls?id=eq." + UnityWebRequest.EscapeURL(pendingId) +
            "&select=id,conversation_id,caller_id,receiver_id,channel_name,status,started_at,answered_at,ended_at&limit=1";
        yield return SendRequest("GET", path, null, value => response = value);
        VoiceCallRecord[] records = ParseArray<VoiceCallArray, VoiceCallRecord>(response, x => x.items);
        if (records.Length == 0 || IsTerminalVoiceCallStatus(records[0].status))
        {
            ResetVoiceCallLocalState(true);
            yield break;
        }
        ApplyVoiceCallRecord(records[0]);
        if (records[0].status == "ringing") yield return AcceptVoiceCallRoutine();
        else if (records[0].status == "accepted") EnsureAgoraJoinedForActiveCall();
    }

    private IEnumerator LoadCallHistory()
    {
        if (string.IsNullOrWhiteSpace(conversationId)) yield break;
        string response = null;
        string path = "/rest/v1/voice_calls?conversation_id=eq." +
            UnityWebRequest.EscapeURL(conversationId) +
            "&status=in.(ended,missed,declined)&select=id,conversation_id,caller_id,receiver_id,channel_name,status,started_at,answered_at,ended_at&order=started_at.asc&limit=100";
        yield return SendRequest("GET", path, null, value => response = value);
        if (response == null) yield break;
        callHistory = ParseArray<VoiceCallArray, VoiceCallRecord>(response, x => x.items);
        RenderMessages(FilterMessages(latestMessages, messageSearchQuery));
    }

    private void RenderCallHistory()
    {
        if (messageContainer == null || callHistory == null) return;
        foreach (VoiceCallRecord call in callHistory)
        {
            if (call == null || !IsTerminalVoiceCallStatus(call.status)) continue;
            // The bubble side is determined by who INITIATED the call, not who answered or ended it.
            bool outgoing = string.Equals(call.caller_id, currentUserId, StringComparison.OrdinalIgnoreCase);
            bool incoming = !outgoing;
            bool answered = !string.IsNullOrWhiteSpace(call.answered_at);
            string title;
            string detail;
            if (string.Equals(call.status, "declined", StringComparison.OrdinalIgnoreCase))
            {
                title = incoming ? T("Declined audio call", "Đã từ chối cuộc gọi") :
                    T("Audio call declined", "Cuộc gọi bị từ chối");
                detail = T("Call declined", "Đã từ chối");
            }
            else if (!answered || string.Equals(call.status, "missed", StringComparison.OrdinalIgnoreCase))
            {
                title = incoming ? T("Missed audio call", "Cuộc gọi thoại nhỡ") :
                    T("Unanswered audio call", "Cuộc gọi không được trả lời");
                detail = T("No answer", "Không trả lời");
            }
            else
            {
                DateTime start = default(DateTime);
                DateTime end = default(DateTime);
                bool hasStart = DateTime.TryParse(call.answered_at, out start);
                bool hasEnd = DateTime.TryParse(call.ended_at, out end);
                bool valid = hasStart && hasEnd;
                TimeSpan duration = valid ? end.ToUniversalTime() - start.ToUniversalTime() : TimeSpan.Zero;
                title = T("Audio call", "Cuộc gọi thoại");
                detail = !valid || duration.TotalSeconds < 0 || duration.TotalHours > 12
                    ? T("Duration unavailable", "Không rõ thời lượng")
                    : ((int)duration.TotalMinutes).ToString("00") + ":" + duration.Seconds.ToString("00");
            }
            VisualElement row = new VisualElement();
            row.AddToClassList("voice-call-history-row");
            row.AddToClassList(incoming ? "voice-call-history-incoming" : "voice-call-history-outgoing");
            Label icon = new Label(incoming ? "↙" : "↗");
            icon.AddToClassList("voice-call-history-icon");
            VisualElement textColumn = new VisualElement();
            textColumn.AddToClassList("voice-call-history-content");
            Label titleLabel = new Label(title);
            titleLabel.AddToClassList("voice-call-history-title");
            Label detailLabel = new Label(detail);
            detailLabel.AddToClassList("voice-call-history-detail");
            textColumn.Add(titleLabel);
            textColumn.Add(detailLabel);
            row.Add(icon);
            row.Add(textColumn);
            messageContainer.Insert(Mathf.Max(1, messageContainer.childCount - 1), row);
        }
    }

    private static bool IsTerminalVoiceCallStatus(string status)
    {
        return string.Equals(status, "declined", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "ended", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "missed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVoiceCallOlderThan(VoiceCallRecord record, float seconds)
    {
        if (record == null ||
            string.IsNullOrWhiteSpace(record.started_at) ||
            !DateTime.TryParse(
                record.started_at,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTime started))
            return false;

        return (DateTime.UtcNow - started.ToUniversalTime()).TotalSeconds >= seconds;
    }

    private void HandleMoreClicked()
    {
        SetMoreMenuVisible(!moreMenuVisible);
    }

    private void SetMoreMenuVisible(bool visible)
    {
        moreMenuVisible = visible;
        if (moreMenuScrim != null)
            moreMenuScrim.EnableInClassList("visible", visible);
    }

    private void HandleMoreMenuScrimPointerDown(PointerDownEvent evt)
    {
        // The scrim covers the whole screen behind the white popup.
        // Any tap outside the popup closes it immediately.
        // Taps on popup items are handled by their own callbacks and also close it.
        if (evt.target == moreMenuScrim)
            SetMoreMenuVisible(false);
    }

    private void HandleViewProfileMenuClicked()
    {
        SetMoreMenuVisible(false);
        if (profileOverlay != null)
            profileOverlay.EnableInClassList("visible", true);
    }

    private void HandleProfileBackClicked()
    {
        if (profileOverlay != null)
            profileOverlay.EnableInClassList("visible", false);
    }

    private void HandleProfileMessageClicked()
    {
        if (profileOverlay != null)
            profileOverlay.EnableInClassList("visible", false);
        root?.schedule.Execute(() => messageInput?.Focus()).ExecuteLater(30);
    }

    private void HandleSharedFilesMenuClicked()
    {
        SetMoreMenuVisible(false);
        fileManagerSelectedTab = 0;
        fileManagerSearchQuery = string.Empty;
        if (fileManagerSearchInput != null) fileManagerSearchInput.SetValueWithoutNotify(string.Empty);
        UpdateFileManagerSearchPlaceholder();
        if (fileManagerOverlay != null) fileManagerOverlay.EnableInClassList("visible", true);
        RenderFileManager();
    }

    private void HandleFileManagerBackClicked()
    {
        if (fileManagerOverlay != null) fileManagerOverlay.EnableInClassList("visible", false);
    }

    private void HandleFileManagerPhotosTabClicked() => SelectFileManagerTab(0);
    private void HandleFileManagerFilesTabClicked() => SelectFileManagerTab(1);
    private void HandleFileManagerLinksTabClicked() => SelectFileManagerTab(2);

    private void SelectFileManagerTab(int tab)
    {
        fileManagerSelectedTab = Mathf.Clamp(tab, 0, 2);
        fileManagerPhotosTab?.EnableInClassList("selected", fileManagerSelectedTab == 0);
        fileManagerFilesTab?.EnableInClassList("selected", fileManagerSelectedTab == 1);
        fileManagerLinksTab?.EnableInClassList("selected", fileManagerSelectedTab == 2);
        RenderFileManager();
    }

    private void HandleFileManagerSearchChanged(ChangeEvent<string> evt)
    {
        fileManagerSearchQuery = (evt.newValue ?? string.Empty).Trim();
        UpdateFileManagerSearchPlaceholder();
        RenderFileManager();
    }

    private void UpdateFileManagerSearchPlaceholder()
    {
        if (fileManagerSearchPlaceholder == null) return;
        bool hasText = fileManagerSearchInput != null && !string.IsNullOrWhiteSpace(fileManagerSearchInput.value);
        fileManagerSearchPlaceholder.style.display = hasText ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void RenderFileManager()
    {
        if (fileManagerContent == null) return;
        fileManagerContent.Clear();

        ChatMessage[] all = latestMessages ?? Array.Empty<ChatMessage>();
        List<ChatMessage> photos = new List<ChatMessage>();
        List<ChatMessage> files = new List<ChatMessage>();
        List<ChatMessage> links = new List<ChatMessage>();

        foreach (ChatMessage message in all)
        {
            if (message == null) continue;
            string type = (message.message_type ?? string.Empty).Trim().ToLowerInvariant();
            if (type == "image") photos.Add(message);
            else if (type == "file" || LooksLikeFile(message.content)) files.Add(message);
            if (type == "link" || TryExtractUrl(message.content, out _)) links.Add(message);
        }

        if (fileManagerPhotosCountLabel != null) fileManagerPhotosCountLabel.text = photos.Count.ToString();
        if (fileManagerFilesCountLabel != null) fileManagerFilesCountLabel.text = files.Count.ToString();
        if (fileManagerLinksCountLabel != null) fileManagerLinksCountLabel.text = links.Count.ToString();

        List<ChatMessage> source = fileManagerSelectedTab == 0 ? photos : (fileManagerSelectedTab == 1 ? files : links);
        List<ChatMessage> filtered = new List<ChatMessage>();
        foreach (ChatMessage message in source)
        {
            if (MatchesFileManagerSearch(message)) filtered.Add(message);
        }

        if (filtered.Count == 0)
        {
            AddFileManagerEmptyState();
            return;
        }

        if (fileManagerSelectedTab == 0) RenderPhotoItems(filtered);
        else if (fileManagerSelectedTab == 1) RenderFileItems(filtered);
        else RenderLinkItems(filtered);
    }

    private bool MatchesFileManagerSearch(ChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(fileManagerSearchQuery)) return true;
        string haystack = (message.content ?? string.Empty) + " " + GetSharedItemDisplayName(message);
        return haystack.IndexOf(fileManagerSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void AddFileManagerEmptyState()
    {
        VisualElement empty = new VisualElement();
        empty.AddToClassList("file-manager-empty");
        VisualElement icon = new VisualElement(); icon.AddToClassList("file-manager-empty-icon");
        Label title = new Label(T("Nothing shared here yet", "Chưa có nội dung được chia sẻ"));
        title.AddToClassList("file-manager-empty-title");
        Label desc = new Label(T("Shared photos, files or links from this conversation will appear here.", "Ảnh, tệp hoặc liên kết được chia sẻ trong cuộc trò chuyện sẽ xuất hiện tại đây."));
        desc.AddToClassList("file-manager-empty-description");
        empty.Add(icon); empty.Add(title); empty.Add(desc); fileManagerContent.Add(empty);
    }

    private void RenderPhotoItems(List<ChatMessage> items)
    {
        AddFileManagerSectionTitle(items[0].created_at);
        VisualElement grid = new VisualElement(); grid.AddToClassList("file-manager-photo-grid");
        foreach (ChatMessage message in items)
        {
            VisualElement card = new VisualElement(); card.AddToClassList("file-manager-photo-card");
            Label loading = new Label(T("Loading...", "Đang tải...")); loading.AddToClassList("file-manager-photo-loading");
            card.Add(loading); grid.Add(card);
            StartCoroutine(LoadChatImageIntoElement(message.content, card, loading));
        }
        fileManagerContent.Add(grid);
    }

    private void RenderFileItems(List<ChatMessage> items)
    {
        AddFileManagerSectionTitle(items[0].created_at);
        foreach (ChatMessage message in items)
        {
            VisualElement card = new VisualElement(); card.AddToClassList("file-manager-list-card");
            VisualElement iconWrap = new VisualElement(); iconWrap.AddToClassList("file-manager-item-icon-wrap");
            VisualElement icon = new VisualElement(); icon.AddToClassList("file-manager-item-icon"); icon.AddToClassList("file-item-icon"); iconWrap.Add(icon);
            VisualElement texts = new VisualElement(); texts.AddToClassList("file-manager-item-texts");
            Label title = new Label(GetSharedItemDisplayName(message)); title.AddToClassList("file-manager-item-title");
            Label meta = new Label(FormatSharedItemMeta(message)); meta.AddToClassList("file-manager-item-meta");
            texts.Add(title); texts.Add(meta);
            Button action = new Button(); action.AddToClassList("file-manager-item-action");
            VisualElement down = new VisualElement(); down.AddToClassList("file-manager-download-icon"); action.Add(down);
            string target = message.content;
            action.clicked += () => OpenSharedTarget(target);
            card.Add(iconWrap); card.Add(texts); card.Add(action); fileManagerContent.Add(card);
        }
    }

    private void RenderLinkItems(List<ChatMessage> items)
    {
        AddFileManagerSectionTitle(items[0].created_at);
        foreach (ChatMessage message in items)
        {
            TryExtractUrl(message.content, out string url);
            VisualElement card = new VisualElement(); card.AddToClassList("file-manager-link-card");
            VisualElement top = new VisualElement(); top.AddToClassList("file-manager-link-top");
            VisualElement iconWrap = new VisualElement(); iconWrap.AddToClassList("file-manager-item-icon-wrap");
            VisualElement icon = new VisualElement(); icon.AddToClassList("file-manager-item-icon"); icon.AddToClassList("link-item-icon"); iconWrap.Add(icon);
            VisualElement texts = new VisualElement(); texts.AddToClassList("file-manager-item-texts");
            Label title = new Label(GetLinkTitle(url)); title.AddToClassList("file-manager-item-title");
            Label urlLabel = new Label(url); urlLabel.AddToClassList("file-manager-link-url");
            Label meta = new Label(FormatSharedItemMeta(message)); meta.AddToClassList("file-manager-item-meta");
            texts.Add(title); texts.Add(urlLabel); texts.Add(meta); top.Add(iconWrap); top.Add(texts); card.Add(top);
            Button open = new Button(() => OpenSharedTarget(url)); open.text = T("Open Link", "Mở liên kết"); open.AddToClassList("file-manager-open-link-button");
            card.Add(open); fileManagerContent.Add(card);
        }
    }

    private void AddFileManagerSectionTitle(string iso)
    {
        string label = T("SHARED CONTENT", "NỘI DUNG ĐÃ CHIA SẺ");
        if (DateTime.TryParse(iso, out DateTime date))
            label = date.ToLocalTime().ToString("MMMM yyyy").ToUpperInvariant();
        Label heading = new Label(label); heading.AddToClassList("file-manager-section-title"); fileManagerContent.Add(heading);
    }

    private string FormatSharedItemMeta(ChatMessage message)
    {
        string sender = message.sender_id == currentUserId ? T("You", "Bạn") : partnerName;
        string date = DateTime.TryParse(message.created_at, out DateTime time) ? time.ToLocalTime().ToString("dd/MM/yyyy") : string.Empty;
        return string.IsNullOrWhiteSpace(date) ? sender : sender + " · " + date;
    }

    private static bool LooksLikeFile(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        string value = content.Split('?')[0].ToLowerInvariant();
        return value.EndsWith(".pdf") || value.EndsWith(".doc") || value.EndsWith(".docx") || value.EndsWith(".ppt") || value.EndsWith(".pptx") || value.EndsWith(".xls") || value.EndsWith(".xlsx") || value.EndsWith(".zip");
    }

    private static string GetSharedItemDisplayName(ChatMessage message)
    {
        string value = message?.content ?? string.Empty;
        if (TryExtractUrl(value, out string url)) value = url;
        value = value.TrimEnd('/');
        int slash = value.LastIndexOf('/');
        string name = slash >= 0 ? value.Substring(slash + 1) : value;
        int q = name.IndexOf('?'); if (q >= 0) name = name.Substring(0, q);
        return string.IsNullOrWhiteSpace(name) ? "Shared item" : Uri.UnescapeDataString(name);
    }

    private static bool TryExtractUrl(string text, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;
        int http = text.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        int https = text.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        int start = http < 0 ? https : (https < 0 ? http : Mathf.Min(http, https));
        if (start < 0) return false;
        int end = start;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        url = text.Substring(start, end - start).TrimEnd('.', ',', ';', ')', ']', '}');
        return Uri.IsWellFormedUriString(url, UriKind.Absolute);
    }

    private static string GetLinkTitle(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && !string.IsNullOrWhiteSpace(uri.Host)) return uri.Host;
        return "Link";
    }

    private void OpenSharedTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        if (TryExtractUrl(target, out string url) || Uri.IsWellFormedUriString(target, UriKind.Absolute))
        {
            Application.OpenURL(string.IsNullOrWhiteSpace(url) ? target : url);
            return;
        }
        Debug.Log("[ChatPageController] Shared file target is a private storage path: " + target);
    }

    private void HandleNotificationsMenuClicked()
    {
        notificationsMuted = !notificationsMuted;
        UpdateNotificationMenuVisual();

        // Keep the overflow popup open when toggling notification state.
        // This lets the user immediately see the label/icon change and
        // toggle it again without reopening the menu.
        // The popup will still close when the user taps outside it or
        // selects another menu action such as Profile, Shared Files, or Report.
    }

    private void UpdateNotificationMenuVisual()
    {
        if (notificationsMenuLabel != null)
            notificationsMenuLabel.text = notificationsMuted
                ? T("Notifications Muted", "Đã tắt thông báo")
                : T("Notifications On", "Đã bật thông báo");

        // notification.png = notifications ON
        // non-notification.png = notifications OFF / muted
        notificationsMenuIcon?.EnableInClassList("notifications-muted", notificationsMuted);

        VisualElement stateDot = root?.Q<VisualElement>("notifications-menu-dot");
        if (stateDot != null)
            stateDot.style.display = notificationsMuted ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void HandleReportProblemMenuClicked()
    {
        SetMoreMenuVisible(false);
        if (reportOverlay != null)
            reportOverlay.EnableInClassList("visible", true);
    }

    private void HandleReportCancelClicked()
    {
        if (reportOverlay != null)
            reportOverlay.EnableInClassList("visible", false);
    }

    private void HandleReportConfirmClicked()
    {
        // UI is complete; connect this callback to your report table/API later.
        Debug.Log($"[ChatPageController] Report confirmed for conversation {conversationId}.");
        if (reportOverlay != null)
            reportOverlay.EnableInClassList("visible", false);
    }

    private void HandleAttachmentClicked()
    {
        if (!initialized || imageUploadInProgress)
            return;

        if (IsImageGalleryOpen())
        {
            CloseInlineGallery();
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        RequestAndroidGalleryPermission();
#else
        ShowError(
            T(
                "Photo picking is available in the Android build.",
                "Chức năng chọn ảnh hoạt động trên bản Android."
            )
        );
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void RequestAndroidGalleryPermission()
    {
        string permission = GetAndroidSdkInt() >= 33
            ? "android.permission.READ_MEDIA_IMAGES"
            : Permission.ExternalStorageRead;

        if (Permission.HasUserAuthorizedPermission(permission))
        {
            OpenInlineGallery();
            return;
        }

        galleryPermissionCallbacks = new PermissionCallbacks();
        galleryPermissionCallbacks.PermissionGranted += _ => OpenInlineGallery();
        galleryPermissionCallbacks.PermissionDenied += _ =>
            ShowError(T("Photo access was denied.", "Quyền truy cập hình ảnh đã bị từ chối."));
        galleryPermissionCallbacks.PermissionDeniedAndDontAskAgain += _ =>
            ShowError(T("Enable photo access in Android Settings.", "Hãy bật quyền hình ảnh trong Cài đặt Android."));
        Permission.RequestUserPermission(permission, galleryPermissionCallbacks);
    }

    private static int GetAndroidSdkInt()
    {
        using AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION");
        return version.GetStatic<int>("SDK_INT");
    }

    private void OpenInlineGallery()
    {
        try
        {
            CacheAndroidActivity();
            if (androidActivity == null)
                return;

            ClearGalleryThumbnails();
            loadedGalleryUris.Clear();
            imageGalleryGrid?.Clear();
            galleryNextOffset = 0;
            galleryHasMore = true;
            galleryPageLoading = false;
            int generation = ++galleryLoadGeneration;
            imageGalleryPanel?.AddToClassList("visible");
            imageGalleryPanel?.BringToFront();
            messageInput?.Blur();

            if (imageGalleryStatus != null)
            {
                imageGalleryStatus.text = T("Loading photos...", "Đang tải ảnh...");
                imageGalleryStatus.style.display = DisplayStyle.Flex;
            }

            StartCoroutine(LoadGalleryPage(generation));
        }
        catch (Exception exception)
        {
            Debug.LogError("[ChatPageController] In-app gallery failed: " + exception);
            if (imageGalleryStatus != null)
                imageGalleryStatus.text = T("Could not load photos.", "Không thể tải hình ảnh.");
        }
    }

    private IEnumerator LoadGalleryPage(int generation)
    {
        if (galleryPageLoading || !galleryHasMore || !IsImageGalleryOpen())
            yield break;

        galleryPageLoading = true;
        GalleryImageResult result = null;

        try
        {
            using AndroidJavaClass picker =
                new AndroidJavaClass("com.virtualeducation.chat.ChatImagePicker");
            string json = picker.CallStatic<string>(
                "getRecentImagesPage", androidActivity, galleryNextOffset, GalleryPageSize);
            result = JsonUtility.FromJson<GalleryImageResult>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError("[ChatPageController] Gallery page failed: " + exception);
        }

        if (generation != galleryLoadGeneration || !IsImageGalleryOpen())
        {
            galleryPageLoading = false;
            yield break;
        }

        if (result == null || !string.IsNullOrWhiteSpace(result.error))
        {
            galleryPageLoading = false;
            if (imageGalleryStatus != null)
                imageGalleryStatus.text = T("Could not load photos.", "Không thể tải hình ảnh.");
            yield break;
        }

        galleryNextOffset = result.nextOffset;
        galleryHasMore = result.hasMore;
        GalleryImageItem[] items = result.items ?? Array.Empty<GalleryImageItem>();

        if (items.Length == 0 && loadedGalleryUris.Count == 0)
        {
            galleryPageLoading = false;
            if (imageGalleryStatus != null)
                imageGalleryStatus.text = T("No photos found.", "Không tìm thấy hình ảnh.");
            yield break;
        }

        if (imageGalleryStatus != null)
            imageGalleryStatus.style.display = DisplayStyle.None;

        int renderedThisFrame = 0;
        foreach (GalleryImageItem item in items)
        {
            if (generation != galleryLoadGeneration || !IsImageGalleryOpen())
                yield break;

            if (item == null || string.IsNullOrWhiteSpace(item.uri) ||
                !loadedGalleryUris.Add(item.uri))
                continue;

            Texture2D thumbnail = LoadTextureFromPath(item.thumbnailPath);
            if (thumbnail == null)
                continue;

            galleryThumbnailTextures.Add(thumbnail);
            Button tile = CreateGalleryTile(item, thumbnail);
            imageGalleryGrid?.Add(tile);
            tile.MarkDirtyRepaint();

            if (++renderedThisFrame >= 4)
            {
                renderedThisFrame = 0;
                yield return null;
            }
        }

        yield return null;
        if (generation != galleryLoadGeneration || !IsImageGalleryOpen())
            yield break;
        galleryPageLoading = false;
        UpdateInputState();
        MaybeLoadMoreGalleryImages();
    }

    private Button CreateGalleryTile(GalleryImageItem item, Texture2D thumbnail)
    {
        Button tile = new Button();
        tile.AddToClassList("image-gallery-item");
        tile.tooltip = item.displayName;

        // Do not use the Button background for gallery thumbnails. On some
        // Android GPUs UI Toolkit can cache that background before the newly
        // decoded texture has been uploaded, leaving a permanently black tile
        // until the gallery is reopened. A dedicated Image element is
        // repainted correctly once the panel has completed its first layout.
        Image thumbnailImage = new Image
        {
            image = thumbnail,
            scaleMode = ScaleMode.ScaleAndCrop,
            pickingMode = PickingMode.Ignore
        };
        thumbnailImage.AddToClassList("image-gallery-thumbnail");
        tile.Add(thumbnailImage);

        VisualElement badge = new VisualElement();
        badge.AddToClassList("image-selection-badge");
        badge.pickingMode = PickingMode.Ignore;
        tile.Add(badge);
        tile.EnableInClassList("selected", IsImageSelected(item.uri));
        tile.clicked += () => ToggleGalleryImage(item, tile);

        // Rebind after Android/UITK has produced geometry and again shortly
        // afterwards. This fixes the first-open black-thumbnail issue seen on
        // devices while remaining harmless on later opens.
        tile.schedule.Execute(() =>
        {
            if (thumbnailImage.panel == null || thumbnail == null)
                return;
            thumbnailImage.image = thumbnail;
            thumbnailImage.MarkDirtyRepaint();
            tile.MarkDirtyRepaint();
        }).ExecuteLater(1);
        tile.schedule.Execute(() =>
        {
            if (thumbnailImage.panel == null || thumbnail == null)
                return;
            thumbnailImage.image = thumbnail;
            thumbnailImage.MarkDirtyRepaint();
        }).ExecuteLater(100);
        return tile;
    }

    private void MaybeLoadMoreGalleryImages()
    {
        if (!IsImageGalleryOpen() || galleryPageLoading || !galleryHasMore ||
            imageGalleryScroll == null)
            return;

        float viewportHeight = imageGalleryScroll.contentViewport.resolvedStyle.height;
        float contentHeight = imageGalleryScroll.contentContainer.resolvedStyle.height;
        float remaining = contentHeight - viewportHeight - imageGalleryScroll.scrollOffset.y;

        if (contentHeight <= viewportHeight + 8f || remaining <= 120f)
            StartCoroutine(LoadGalleryPage(galleryLoadGeneration));
    }

    private void CacheAndroidActivity()
    {
        if (androidActivity != null)
            return;
        using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        androidActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
    }

    private void ToggleGalleryImage(GalleryImageItem item, Button tile)
    {
        int index = selectedChatImages.FindIndex(image => image.uri == item.uri);
        if (index >= 0)
        {
            TryDeleteCacheFile(selectedChatImages[index].localPath);
            selectedChatImages.RemoveAt(index);
            tile?.RemoveFromClassList("selected");
            UpdateInputState();
            return;
        }

        if (selectedChatImages.Count >= MaxSelectedImages)
        {
            ShowError(T("You can select up to 4 images.", "Bạn có thể chọn tối đa 4 ảnh."));
            return;
        }

        try
        {
            using AndroidJavaClass picker =
                new AndroidJavaClass("com.virtualeducation.chat.ChatImagePicker");
            string localPath = picker.CallStatic<string>(
                "copyImageToCache", androidActivity, item.uri, item.displayName);

            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                ShowError(T("Could not open this image.", "Không thể mở hình ảnh này."));
                return;
            }

            selectedChatImages.Add(new SelectedChatImage
            {
                uri = item.uri,
                localPath = localPath,
                displayName = item.displayName
            });
            tile?.AddToClassList("selected");
            UpdateInputState();
        }
        catch (Exception exception)
        {
            Debug.LogError("[ChatPageController] Gallery selection failed: " + exception);
            ShowError(T("Could not open this image.", "Không thể mở hình ảnh này."));
        }
    }
#else
    private void MaybeLoadMoreGalleryImages() { }
#endif

    private bool IsImageSelected(string uri)
    {
        return selectedChatImages.Exists(image => image.uri == uri);
    }

    private static Texture2D LoadTextureFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
                return texture;
            Destroy(texture);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[ChatPageController] Thumbnail load failed: " + exception.Message);
        }
        return null;
    }

    private void CloseInlineGallery()
    {
        galleryLoadGeneration++;
        galleryPageLoading = false;
        imageGalleryPanel?.RemoveFromClassList("visible");
        imageGalleryGrid?.Clear();
        ClearGalleryThumbnails();
    }

    private bool IsImageGalleryOpen()
    {
        return imageGalleryPanel != null && imageGalleryPanel.ClassListContains("visible");
    }

    private void ClearGalleryThumbnails()
    {
        foreach (Texture2D texture in galleryThumbnailTextures)
            if (texture != null) Destroy(texture);
        galleryThumbnailTextures.Clear();
    }

    /// <summary>
    /// Called by the Android Java picker through UnitySendMessage.
    /// The picker copies the selected image into this app's cache,
    /// so Unity can safely read the file bytes.
    /// </summary>
    public void OnChatImagePicked(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            OnChatImagePickCancelled(string.Empty);
            return;
        }

        StartCoroutine(
            UploadAndSendImageRoutine(localPath)
        );
    }

    public void OnChatImagePickCancelled(string unused)
    {
        imageUploadInProgress = false;
        UpdateAttachmentButtonState();
    }

    private IEnumerator UploadAndSendImageRoutine(
        string localPath)
    {
        if (!initialized ||
            imageUploadInProgress ||
            string.IsNullOrWhiteSpace(localPath))
        {
            yield break;
        }

        imageUploadInProgress = true;
        UpdateAttachmentButtonState();

        byte[] imageBytes = null;

        try
        {
            if (!File.Exists(localPath))
            {
                ShowError(
                    T(
                        "The selected image could not be read.",
                        "Không thể đọc ảnh đã chọn."
                    )
                );
                yield break;
            }

            FileInfo fileInfo =
                new FileInfo(localPath);

            long maxBytes =
                (long)maxImageSizeMb *
                1024L *
                1024L;

            if (fileInfo.Length > maxBytes)
            {
                ShowError(
                    T(
                        $"Image is larger than {maxImageSizeMb} MB.",
                        $"Ảnh lớn hơn {maxImageSizeMb} MB."
                    )
                );
                yield break;
            }

            imageBytes =
                File.ReadAllBytes(localPath);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[ChatPageController] Could not read selected image: " +
                exception
            );

            ShowError(
                T(
                    "Could not read the selected image.",
                    "Không thể đọc ảnh đã chọn."
                )
            );
            yield break;
        }

        if (imageBytes == null ||
            imageBytes.Length == 0)
        {
            ShowError(
                T(
                    "The selected image is empty.",
                    "Ảnh đã chọn không có dữ liệu."
                )
            );
            yield break;
        }

        string extension =
            GetSafeImageExtension(localPath);

        string storagePath =
            currentUserId + "/" +
            conversationId + "/" +
            Guid.NewGuid().ToString("N") +
            extension;

        string mimeType =
            GetImageMimeType(extension);

        bool uploadSuccess = false;

        yield return UploadImageToSupabaseStorage(
            storagePath,
            imageBytes,
            mimeType,
            success => uploadSuccess = success
        );

        if (!uploadSuccess)
            yield break;

        MessageInsertBody body =
            new MessageInsertBody
            {
                conversation_id = conversationId,
                sender_id = currentUserId,
                receiver_id = partnerUserId,

                // For image messages, content stores the permanent
                // object path in the private chat-images bucket.
                content = storagePath,
                message_type = "image"
            };

        bool messageSaved = false;

        yield return SendRequest(
            "POST",
            "/rest/v1/chat_messages",
            JsonUtility.ToJson(body),
            _ => messageSaved = true,
            "return=representation"
        );

        if (!messageSaved)
        {
            ShowError(
                T(
                    "The image uploaded, but the chat message could not be saved.",
                    "Ảnh đã tải lên nhưng không thể lưu tin nhắn."
                )
            );
            yield break;
        }

        yield return LoadMessages(true);

        try
        {
            File.Delete(localPath);
        }
        catch
        {
            // Cache cleanup is best effort only.
        }

        imageUploadInProgress = false;
        UpdateAttachmentButtonState();
    }

    private IEnumerator UploadImageToSupabaseStorage(
        string storagePath,
        byte[] imageBytes,
        string mimeType,
        Action<bool> onComplete)
    {
        string encodedPath =
            EncodeStoragePath(storagePath);

        string url =
            supabaseUrl.TrimEnd('/') +
            "/storage/v1/object/" +
            UnityWebRequest.EscapeURL(chatImageBucket) +
            "/" +
            encodedPath;

        using UnityWebRequest request =
            new UnityWebRequest(
                url,
                UnityWebRequest.kHttpVerbPOST
            );

        request.uploadHandler =
            new UploadHandlerRaw(imageBytes);

        request.downloadHandler =
            new DownloadHandlerBuffer();

        request.SetRequestHeader(
            "Content-Type",
            mimeType
        );

        request.SetRequestHeader(
            "apikey",
            supabaseAnonKey
        );

        request.SetRequestHeader(
            "Authorization",
            "Bearer " + accessToken
        );

        request.SetRequestHeader(
            "x-upsert",
            "false"
        );

        yield return request.SendWebRequest();

        bool success =
            request.result ==
            UnityWebRequest.Result.Success;

        if (!success)
        {
            Debug.LogError(
                "[ChatPageController] Image upload failed " +
                $"({request.responseCode}): " +
                request.downloadHandler.text
            );

            ShowError(
                T(
                    "Could not upload the image.",
                    "Không thể tải ảnh lên."
                )
            );
        }

        onComplete?.Invoke(success);

        if (!success)
        {
            imageUploadInProgress = false;
            UpdateAttachmentButtonState();
        }
    }

    private IEnumerator LoadChatImageIntoElement(
        string storagePath,
        VisualElement imageElement,
        Label loadingLabel)
    {
        if (imageElement == null ||
            string.IsNullOrWhiteSpace(storagePath))
        {
            yield break;
        }

        if (imageTextureCache.TryGetValue(
                storagePath,
                out Texture2D cachedTexture) &&
            cachedTexture != null)
        {
            ApplyChatImageTexture(
                imageElement,
                loadingLabel,
                cachedTexture
            );
            yield break;
        }

        string encodedPath =
            EncodeStoragePath(storagePath);

        string url =
            supabaseUrl.TrimEnd('/') +
            "/storage/v1/object/authenticated/" +
            UnityWebRequest.EscapeURL(chatImageBucket) +
            "/" +
            encodedPath;

        using UnityWebRequest request =
            UnityWebRequestTexture.GetTexture(
                url,
                true
            );

        request.SetRequestHeader(
            "apikey",
            supabaseAnonKey
        );

        request.SetRequestHeader(
            "Authorization",
            "Bearer " + accessToken
        );

        yield return request.SendWebRequest();

        if (request.result !=
            UnityWebRequest.Result.Success)
        {
            Debug.LogWarning(
                "[ChatPageController] Could not load chat image " +
                storagePath +
                ": " +
                request.error
            );

            if (loadingLabel != null)
            {
                loadingLabel.text =
                    T(
                        "Image unavailable",
                        "Không thể tải ảnh"
                    );

                loadingLabel.AddToClassList(
                    "message-image-error"
                );
            }

            yield break;
        }

        Texture2D texture =
            DownloadHandlerTexture.GetContent(
                request
            );

        if (texture == null)
            yield break;

        imageTextureCache[storagePath] = texture;

        ApplyChatImageTexture(
            imageElement,
            loadingLabel,
            texture
        );
    }

    private static void ApplyChatImageTexture(
        VisualElement imageElement,
        Label loadingLabel,
        Texture2D texture)
    {
        if (imageElement == null ||
            texture == null)
        {
            return;
        }

        imageElement.style.backgroundImage =
            new StyleBackground(texture);

        float aspect =
            texture.height > 0
                ? (float)texture.width /
                  texture.height
                : 1f;

        const float targetWidth = 230f;

        float targetHeight =
            Mathf.Clamp(
                targetWidth /
                Mathf.Max(aspect, 0.1f),
                130f,
                300f
            );

        imageElement.style.width =
            targetWidth;

        imageElement.style.height =
            targetHeight;

        if (loadingLabel != null)
            loadingLabel.style.display =
                DisplayStyle.None;
    }

    private void UpdateAttachmentButtonState()
    {
        if (attachmentButton == null)
            return;

        attachmentButton.SetEnabled(
            initialized &&
            !imageUploadInProgress
        );

        attachmentButton.EnableInClassList(
            "uploading",
            imageUploadInProgress
        );

        attachmentButton.tooltip =
            imageUploadInProgress
                ? T(
                    "Uploading image...",
                    "Đang tải ảnh..."
                )
                : T(
                    "Send photo",
                    "Gửi ảnh"
                );
    }

    private static string GetSafeImageExtension(
        string filePath)
    {
        string extension =
            Path.GetExtension(filePath)?
                .ToLowerInvariant();

        return extension switch
        {
            ".png" => ".png",
            ".webp" => ".webp",
            ".gif" => ".gif",
            ".jpeg" => ".jpg",
            ".jpg" => ".jpg",
            _ => ".jpg"
        };
    }

    private static string GetImageMimeType(
        string extension)
    {
        return extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }

    private static string EncodeStoragePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string[] segments =
            path.Split('/');

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] =
                UnityWebRequest.EscapeURL(
                    segments[i]
                );
        }

        return string.Join("/", segments);
    }

    private void StopAllRunningCoroutines()
    {
        if (messagePollingCoroutine != null) StopCoroutine(messagePollingCoroutine);
        if (presencePollingCoroutine != null) StopCoroutine(presencePollingCoroutine);
        if (heartbeatCoroutine != null) StopCoroutine(heartbeatCoroutine);
        if (typingAnimationCoroutine != null) StopCoroutine(typingAnimationCoroutine);
        if (typingTimeoutCoroutine != null) StopCoroutine(typingTimeoutCoroutine);
        messagePollingCoroutine = presencePollingCoroutine = heartbeatCoroutine = typingAnimationCoroutine = typingTimeoutCoroutine = null;
    }

    private void ShowError(string text)
    {
        Debug.LogError("[ChatPageController] " + text);
        if (messageContainer == null) return;
        Label error = messageContainer.Q<Label>(className: "message-load-error");
        if (error == null)
        {
            error = new Label();
            error.AddToClassList("message-load-error");
            messageContainer.Add(error);
        }
        error.text = text;
    }

    private void ScrollToBottom()
    {
        if (messageScrollView == null) return;
        messageScrollView.schedule.Execute(() =>
        {
            messageScrollView.scrollOffset = new Vector2(0, messageScrollView.verticalScroller.highValue);
        }).ExecuteLater(10);
    }

    private void OnChatInputFocused(FocusInEvent evt) { chatInputFocused = true; }
    private void OnChatInputBlurred(FocusOutEvent evt) { chatInputFocused = false; }

    // Android may resize the Unity view when the IME opens, or overlay the IME.
    // Subtract any resize already applied by Android to avoid moving the composer twice.
    private void UpdateKeyboardLayout()
    {
        if (safeArea == null || root == null || Application.platform != RuntimePlatform.Android) return;
        float rootHeight = root.resolvedStyle.height;
        if (rootHeight <= 0f) return;
        float pixelHeight = Mathf.Max(1f, Screen.height);
        int visibleBottom = -1;
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
            using (var decor = window.Call<AndroidJavaObject>("getDecorView"))
            using (var frame = new AndroidJavaObject("android.graphics.Rect"))
            {
                decor.Call("getWindowVisibleDisplayFrame", frame);
                visibleBottom = frame.Get<int>("bottom");
            }
        }
        catch (Exception) { /* TouchScreenKeyboard.area fallback below. */ }

        bool keyboardOpen = TouchScreenKeyboard.visible;
        if (!keyboardOpen && !chatInputFocused && keyboardInsetPoints <= 0f)
        {
            baselineRootHeight = rootHeight;
            if (visibleBottom > 0) baselineVisibleBottom = visibleBottom;
            return;
        }
        float keyboardPixels = 0f;
        if (visibleBottom > 0 && baselineVisibleBottom > 0)
            keyboardPixels = Mathf.Max(0, baselineVisibleBottom - visibleBottom);
        if (keyboardOpen)
            keyboardPixels = Mathf.Max(keyboardPixels, TouchScreenKeyboard.area.height);

        // Do not keep a stale keyboard offset after the Android Back key closes IME.
        if (!keyboardOpen && keyboardPixels < pixelHeight * 0.12f)
            keyboardPixels = 0f;
        float resizedPixels = baselineRootHeight > 0f
            ? Mathf.Max(0f, baselineRootHeight - rootHeight) * pixelHeight / Mathf.Max(baselineRootHeight, 1f)
            : 0f;
        float inset = Mathf.Max(0f, keyboardPixels - resizedPixels) * rootHeight / pixelHeight;
        if (Mathf.Abs(inset - keyboardInsetPoints) > 1f)
        {
            keyboardInsetPoints = inset;
            ApplySafeArea();
            if (inset > 0f) root.schedule.Execute(ScrollToBottom).ExecuteLater(60);
        }
        if (keyboardPixels == 0f && !keyboardOpen)
        {
            baselineRootHeight = rootHeight;
            if (visibleBottom > 0) baselineVisibleBottom = visibleBottom;
        }
    }

    private void OnRootGeometryChanged(GeometryChangedEvent evt) => ApplySafeArea();

    private void ApplySafeArea()
    {
        if (safeArea == null || root == null) return;
        Rect area = Screen.safeArea;
        float sw = Mathf.Max(Screen.width, 1);
        float sh = Mathf.Max(Screen.height, 1);
        float pw = root.resolvedStyle.width;
        float ph = root.resolvedStyle.height;
        if (pw <= 0 || ph <= 0) return;
        safeArea.style.paddingLeft = area.xMin / sw * pw;
        safeArea.style.paddingRight = (sw - area.xMax) / sw * pw;
        float safeTopPadding = Mathf.Max((sh - area.yMax) / sh * ph, minimumTopSafePadding);
        safeArea.style.paddingTop = safeTopPadding;
        // The safe-area strip above the header must be white, not the chat canvas gray.
        // Header content stays at its original safe-area offset.
        if (chatHeader != null) chatHeader.style.backgroundColor = Color.white;
        safeArea.style.paddingBottom = area.yMin / sh * ph + keyboardInsetPoints;

        // file-manager-overlay uses absolute positioning, so it bypasses the normal
        // vertical flow created by safeArea.paddingTop. Move the overlay down by the
        // SAME safe-top amount as the main ChatScene content so both headers align.
        if (fileManagerOverlay != null)
        {
            fileManagerOverlay.style.top = safeTopPadding;
            fileManagerOverlay.style.bottom = 0;
        }

        if (voiceCallOverlay != null)
        {
            voiceCallOverlay.style.top = safeTopPadding;
            voiceCallOverlay.style.bottom = 0;
        }
    }

    private static string BuildMessageSignature(ChatMessage[] messages)
    {
        if (messages == null || messages.Length == 0) return "0";

        // Include seen/delivered state for every message so a read receipt update
        // triggers a re-render even when the seen outgoing message is not the
        // very last row in the conversation.
        StringBuilder signature = new StringBuilder(messages.Length * 48);
        signature.Append(messages.Length);

        foreach (ChatMessage message in messages)
        {
            if (message == null) continue;
            signature.Append('|')
                     .Append(message.id)
                     .Append(':')
                     .Append(message.message_type)
                     .Append(':')
                     .Append(message.content)
                     .Append(':')
                     .Append(message.delivered_at)
                     .Append(':')
                     .Append(message.seen_at);
        }

        return signature.ToString();
    }

    private static bool IsRecent(string iso, float seconds)
    {
        if (!DateTime.TryParse(iso, out DateTime time)) return false;
        return (DateTime.UtcNow - time.ToUniversalTime()).TotalSeconds <= seconds;
    }

    private static string FormatTime(string iso)
    {
        return DateTime.TryParse(iso, out DateTime time) ? time.ToLocalTime().ToString("h:mm tt") : string.Empty;
    }

    private static string FormatLastSeen(string iso)
    {
        if (!DateTime.TryParse(iso, out DateTime time))
            return T("Offline", "Ngoại tuyến");

        TimeSpan gap =
            DateTime.Now - time.ToLocalTime();

        if (gap.TotalMinutes < 1)
            return T("Active just now", "Vừa hoạt động");

        if (gap.TotalMinutes < 60)
        {
            int minutes =
                Mathf.FloorToInt(
                    (float)gap.TotalMinutes);

            return T(
                $"Active {minutes}m ago",
                $"Hoạt động {minutes} phút trước"
            );
        }

        if (gap.TotalHours < 24)
        {
            int hours =
                Mathf.FloorToInt(
                    (float)gap.TotalHours);

            return T(
                $"Active {hours}h ago",
                $"Hoạt động {hours} giờ trước"
            );
        }

        string date =
            time.ToLocalTime().ToString("dd/MM/yyyy");

        // On the compact mobile chat header, the Vietnamese phrase
        // "Hoạt động dd/MM/yyyy" + role is too wide. For older activity,
        // showing the date alone is clearer and keeps the action buttons safe.
        return T(
            "Active " + date,
            date
        );
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "U";
        string[] parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();
        return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "User";
        value = value.Trim();
        return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
    }
}
