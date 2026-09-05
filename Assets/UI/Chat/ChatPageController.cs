using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

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

    private UIDocument uiDocument;
    private VisualElement root;
    private VisualElement safeArea;
    private Button backButton;
    private Button callButton;
    private Button moreButton;
    private Button attachmentButton;
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
    private VisualElement headerOnlineDot;
    private VisualElement statusDot;

    private string currentUserId;
    private string partnerUserId;
    private string classId;
    private string conversationId;
    private string accessToken;
    private string partnerName;
    private string partnerRole;
    private string latestRenderedSignature;
    private bool initialized;
    private bool localTypingState;
    private float lastInputTime;
    private int currentTypingDotIndex;

    private Coroutine messagePollingCoroutine;
    private Coroutine presencePollingCoroutine;
    private Coroutine heartbeatCoroutine;
    private Coroutine typingAnimationCoroutine;
    private Coroutine typingTimeoutCoroutine;

    [Serializable] private class ConversationRpcBody { public string p_other_user_id; public string p_class_id; }
    [Serializable] private class MessageInsertBody { public string conversation_id; public string sender_id; public string receiver_id; public string content; public string message_type = "text"; }
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
    [Serializable] private class PresenceRecord { public string user_id; public bool is_online; public string last_seen_at; public string updated_at; }
    [Serializable] private class PresenceArray { public PresenceRecord[] items; }
    [Serializable] private class TypingRecord { public string conversation_id; public string user_id; public bool is_typing; public string updated_at; }
    [Serializable] private class TypingArray { public TypingRecord[] items; }

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
        root = uiDocument.rootVisualElement;
        FindVisualElements();
        RegisterCallbacks();
        ApplySafeArea();
        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        StartCoroutine(InitializeChat());
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
        if (root != null) root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        StopAllRunningCoroutines();
        if (initialized)
        {
            StartCoroutine(SetTypingState(false));
            StartCoroutine(SetPresence(false));
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (!initialized) return;
        StartCoroutine(SetPresence(!pauseStatus));
        if (pauseStatus) StartCoroutine(SetTypingState(false));
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (initialized) StartCoroutine(SetPresence(hasFocus));
    }

    private IEnumerator InitializeChat()
    {
        ConfigureInitialUi();
        ReadSessionData();

        if (!ValidateConfiguration()) yield break;

        yield return ResolveConversation();
        if (string.IsNullOrWhiteSpace(conversationId)) yield break;

        initialized = true;
        yield return SetPresence(true);
        yield return LoadMessages(true);
        yield return PollPartnerPresenceAndTyping();

        messagePollingCoroutine = StartCoroutine(MessagePollingLoop());
        presencePollingCoroutine = StartCoroutine(PresencePollingLoop());
        heartbeatCoroutine = StartCoroutine(PresenceHeartbeatLoop());
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
            ShowError("You cannot start a direct chat with yourself.");
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
            ShowError("Could not create or load the chat conversation.");
            yield break;
        }

        conversationId = response.Trim().Trim('"');
        if (conversationId.StartsWith("[") && conversationId.EndsWith("]"))
            conversationId = conversationId.Trim('[', ']', ' ', '"');

        if (!Guid.TryParse(conversationId, out _))
        {
            Debug.LogError("[ChatPageController] Unexpected RPC result: " + response);
            conversationId = string.Empty;
            ShowError("Invalid conversation ID returned by Supabase.");
            yield break;
        }

        PlayerPrefs.SetString("selected_chat_conversation_id", conversationId);
        PlayerPrefs.Save();
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
        string signature = BuildMessageSignature(messages);

        if (forceRender || signature != latestRenderedSignature)
        {
            latestRenderedSignature = signature;
            RenderMessages(messages);
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
        if (string.IsNullOrWhiteSpace(text)) return;
        StartCoroutine(SendMessageRoutine(text));
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
            content = text
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
            if (child.ClassListContains("message-row")) remove.Add(child);
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

        VisualElement bubble = new VisualElement();
        bubble.AddToClassList("message-bubble");
        bubble.AddToClassList(outgoing ? "outgoing-bubble" : "incoming-bubble");

        Label content = new Label(message.content ?? string.Empty);
        content.AddToClassList("message-text");
        content.AddToClassList(outgoing ? "outgoing-message-text" : "incoming-message-text");
        bubble.Add(content);
        group.Add(bubble);

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
                        ? "Sent"
                        : "Delivered");
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
        if (typingLabel != null) typingLabel.text = partnerName + " is typing...";

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
        partnerName = string.IsNullOrWhiteSpace(displayName) ? "Chat user" : displayName;
        partnerRole = string.IsNullOrWhiteSpace(role) ? "User" : role;
        if (teacherNameLabel != null) teacherNameLabel.text = partnerName;
        if (teacherPositionLabel != null) teacherPositionLabel.text = Capitalize(partnerRole);
        string initials = GetInitials(partnerName);
        if (partnerAvatarLabel != null) partnerAvatarLabel.text = initials;
        if (typingAvatarLabel != null) typingAvatarLabel.text = initials;

        if (teacherStatusLabel != null)
        {
            teacherStatusLabel.text = online ? "Active now" : FormatLastSeen(lastSeenIso);
            teacherStatusLabel.EnableInClassList("offline", !online);
        }
        if (headerOnlineDot != null) headerOnlineDot.EnableInClassList("offline", !online);
        if (statusDot != null) statusDot.EnableInClassList("offline", !online);
    }

    private IEnumerator SendRequest(string method, string path, string jsonBody, Action<string> onSuccess, string prefer = null)
    {
        string url = supabaseUrl.TrimEnd('/') + path;
        using (UnityWebRequest request = new UnityWebRequest(url, method))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.SetRequestHeader("apikey", supabaseAnonKey);
            request.SetRequestHeader("Authorization", "Bearer " + accessToken);
            if (!string.IsNullOrWhiteSpace(prefer)) request.SetRequestHeader("Prefer", prefer);

            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke(request.downloadHandler.text);
            }
            else
            {
                string responseText = request.downloadHandler != null
                    ? request.downloadHandler.text
                    : string.Empty;

                Debug.LogError(
                    $"[ChatPageController] {method} {path} failed " +
                    $"({request.responseCode}): {responseText}"
                );

                // Helpful diagnostic for the direct-conversation RPC.
                if (path.Contains("get_or_create_direct_conversation") &&
                    responseText.IndexOf(
                        "Invalid chat participant",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0)
                {
                    Debug.LogError(
                        "[ChatPageController] Supabase rejected the chat participant. " +
                        $"currentUserId={currentUserId}, partnerUserId={partnerUserId}, " +
                        $"classId={classId}. Check that the partner is another user " +
                        "and is an allowed participant in this class."
                    );
                }
            }
        }
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
        backButton = root.Q<Button>("back-button");
        callButton = root.Q<Button>("call-button");
        moreButton = root.Q<Button>("more-button");
        attachmentButton = root.Q<Button>("attachment-button");
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
        headerOnlineDot = root.Q<VisualElement>("header-online-dot");
        statusDot = root.Q<VisualElement>("status-dot");
    }

    private void RegisterCallbacks()
    {
        if (backButton != null) backButton.clicked += HandleBackClicked;
        if (callButton != null) callButton.clicked += HandleCallClicked;
        if (moreButton != null) moreButton.clicked += HandleMoreClicked;
        if (attachmentButton != null) attachmentButton.clicked += HandleAttachmentClicked;
        if (sendButton != null) sendButton.clicked += SendCurrentMessage;
        if (messageInput != null)
        {
            messageInput.RegisterValueChangedCallback(HandleInputValueChanged);
            messageInput.RegisterCallback<KeyDownEvent>(HandleInputKeyDown);
        }
    }

    private void UnregisterCallbacks()
    {
        if (backButton != null) backButton.clicked -= HandleBackClicked;
        if (callButton != null) callButton.clicked -= HandleCallClicked;
        if (moreButton != null) moreButton.clicked -= HandleMoreClicked;
        if (attachmentButton != null) attachmentButton.clicked -= HandleAttachmentClicked;
        if (sendButton != null) sendButton.clicked -= SendCurrentMessage;
        if (messageInput != null)
        {
            messageInput.UnregisterValueChangedCallback(HandleInputValueChanged);
            messageInput.UnregisterCallback<KeyDownEvent>(HandleInputKeyDown);
        }
    }

    private void ConfigureInitialUi()
    {
        if (messageInput != null) { messageInput.value = string.Empty; messageInput.isDelayed = false; }
        if (typingRow != null) typingRow.style.display = DisplayStyle.None;
        UpdateInputState();
    }

    private void UpdateInputState()
    {
        bool hasText =
            messageInput != null &&
            !string.IsNullOrWhiteSpace(messageInput.value);

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
            sendButton.EnableInClassList("enabled", hasText);
            sendButton.SetEnabled(hasText && initialized);
        }

        if (sendIcon != null)
            sendIcon.EnableInClassList("send-icon-active", hasText);
    }

    private void HandleBackClicked()
    {
        StartCoroutine(SetTypingState(false));
        StartCoroutine(SetPresence(false));
        string scene = PlayerPrefs.GetString("previous_scene", previousSceneName);
        if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
        else Debug.LogError("[ChatPageController] Scene not found in Build Profiles: " + scene);
    }

    private void HandleCallClicked() => Debug.Log("[ChatPageController] Call feature is not connected yet.");
    private void HandleMoreClicked() => Debug.Log("[ChatPageController] More button clicked.");
    private void HandleAttachmentClicked() => Debug.Log("[ChatPageController] Connect attachment button to the mobile file picker.");

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
        safeArea.style.paddingTop = Mathf.Max((sh - area.yMax) / sh * ph, minimumTopSafePadding);
        safeArea.style.paddingBottom = area.yMin / sh * ph;
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
        if (!DateTime.TryParse(iso, out DateTime time)) return "Offline";
        TimeSpan gap = DateTime.Now - time.ToLocalTime();
        if (gap.TotalMinutes < 1) return "Active just now";
        if (gap.TotalMinutes < 60) return "Active " + Mathf.FloorToInt((float)gap.TotalMinutes) + "m ago";
        if (gap.TotalHours < 24) return "Active " + Mathf.FloorToInt((float)gap.TotalHours) + "h ago";
        return "Active " + time.ToLocalTime().ToString("dd/MM/yyyy");
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
