using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// ChatAIScene controller.
///
/// IMPORTANT:
/// - This controller is intentionally independent from ChatScene.
/// - It does NOT read selected_chat_user_id.
/// - It does NOT read/write selected_chat_conversation_id.
/// - It does NOT use chat_conversations, chat_messages, user_presence or chat_typing.
/// - Each signed-in user gets a separate local AI chat history:
///       ai_chat_history_<userId>
///
/// Later, when an AI backend is connected, replace SendMessageRoutine()
/// with the API call while keeping the same UI/history model.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ChatAIPageController : MonoBehaviour
{
    [Header("Scene Navigation")]
    [Tooltip("Final fallback only. Normally the exact previous scene is saved by BottomNavigationController.")]
    [SerializeField] private string previousSceneName = "MainHomeScene";

    [Header("Safe Area")]
    [SerializeField] private float minimumTopSafePadding = 6f;

    private const string ChatAIPreviousSceneKey = "chat_ai_previous_scene";
    private const string AIHistoryPrefix = "ai_chat_history_";

    private UIDocument uiDocument;
    private VisualElement root;
    private VisualElement safeArea;

    private Button backButton;
    private Button moreButton;
    private Button attachmentButton;
    private Button sendButton;
    private VisualElement sendIcon;

    private TextField messageInput;
    private Label inputPlaceholder;
    private ScrollView messageScrollView;
    private VisualElement messageContainer;
    private VisualElement emptyChat;
    private VisualElement typingRow;

    private Label assistantNameLabel;
    private Label assistantPositionLabel;
    private Label assistantStatusLabel;
    private Label assistantAvatarLabel;
    private Label typingAvatarLabel;
    private VisualElement headerOnlineDot;
    private VisualElement statusDot;

    private string currentUserId;
    private string historyKey;
    private bool initialized;

    private readonly List<AIChatMessage> messages = new List<AIChatMessage>();

    [Serializable]
    private class AIChatMessage
    {
        public string id;
        public string role;       // "user" or "assistant"
        public string content;
        public string created_at;
    }

    [Serializable]
    private class AIChatHistory
    {
        public List<AIChatMessage> items = new List<AIChatMessage>();
    }

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogError("[ChatAIPageController] UIDocument was not found.");
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (uiDocument == null)
            return;

        root = uiDocument.rootVisualElement;

        FindVisualElements();
        RegisterCallbacks();
        ConfigureInitialUi();

        ApplySafeArea();
        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

        InitializeAIChat();
    }

    private void OnDisable()
    {
        UnregisterCallbacks();

        if (root != null)
            root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
    }

    private void InitializeAIChat()
    {
        currentUserId = ResolveCurrentUserId();

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            // This is only a fallback for editor testing before login.
            currentUserId = "guest";
            Debug.LogWarning(
                "[ChatAIPageController] No signed-in user id found. " +
                "Using a temporary guest AI chat history."
            );
        }

        historyKey = AIHistoryPrefix + currentUserId;

        SetAssistantInformation();
        LoadLocalHistory();

        // First AI conversation for this user only.
        if (messages.Count == 0)
        {
            messages.Add(new AIChatMessage
            {
                id = Guid.NewGuid().ToString(),
                role = "assistant",
                content = "Hello! I’m AI Assistant. How can I help you with your learning today?",
                created_at = DateTime.UtcNow.ToString("o")
            });

            SaveLocalHistory();
        }

        initialized = true;
        RenderMessages();
        UpdateInputState();

        Debug.Log(
            $"[ChatAIPageController] Loaded AI chat for user {currentUserId}. " +
            $"Messages: {messages.Count}"
        );
    }

    private static string ResolveCurrentUserId()
    {
        if (!string.IsNullOrWhiteSpace(SupabaseSession.UserId))
            return SupabaseSession.UserId.Trim();

        string id = PlayerPrefs.GetString("user_id", string.Empty);

        if (string.IsNullOrWhiteSpace(id))
            id = PlayerPrefs.GetString("current_user_id", string.Empty);

        return id?.Trim() ?? string.Empty;
    }

    private void LoadLocalHistory()
    {
        messages.Clear();

        if (string.IsNullOrWhiteSpace(historyKey))
            return;

        string json = PlayerPrefs.GetString(historyKey, string.Empty);

        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            AIChatHistory history = JsonUtility.FromJson<AIChatHistory>(json);

            if (history?.items != null)
                messages.AddRange(history.items);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[ChatAIPageController] Could not read AI history. Starting fresh. " +
                exception.Message
            );
        }
    }

    private void SaveLocalHistory()
    {
        if (string.IsNullOrWhiteSpace(historyKey))
            return;

        AIChatHistory history = new AIChatHistory
        {
            items = new List<AIChatMessage>(messages)
        };

        PlayerPrefs.SetString(
            historyKey,
            JsonUtility.ToJson(history)
        );

        PlayerPrefs.Save();
    }

    private void SendCurrentMessage()
    {
        if (!initialized || messageInput == null)
            return;

        string text = messageInput.value?.Trim();

        if (string.IsNullOrWhiteSpace(text))
            return;

        StartCoroutine(SendMessageRoutine(text));
    }

    private IEnumerator SendMessageRoutine(string text)
    {
        if (messageInput == null)
            yield break;

        messageInput.SetEnabled(false);

        if (sendButton != null)
            sendButton.SetEnabled(false);

        messages.Add(new AIChatMessage
        {
            id = Guid.NewGuid().ToString(),
            role = "user",
            content = text,
            created_at = DateTime.UtcNow.ToString("o")
        });

        SaveLocalHistory();

        messageInput.value = string.Empty;

        if (typingRow != null)
            typingRow.style.display = DisplayStyle.Flex;

        RenderMessages();
        UpdateInputState();
        ScrollToBottom();

        bool requestFinished = false;
        string aiResponse = string.Empty;
        string requestError = string.Empty;

        yield return AIService.SendMessage(
            text,
            answer =>
            {
                aiResponse = answer;
                requestFinished = true;
            },
            error =>
            {
                requestError = error;
                requestFinished = true;
            }
        );

        if (!requestFinished)
        {
            requestError =
                "AI request finished without a response.";
        }

        if (typingRow != null)
            typingRow.style.display = DisplayStyle.None;

        if (!string.IsNullOrWhiteSpace(aiResponse))
        {
            messages.Add(new AIChatMessage
            {
                id = Guid.NewGuid().ToString(),
                role = "assistant",
                content = aiResponse.Trim(),
                created_at = DateTime.UtcNow.ToString("o")
            });
        }
        else
        {
            Debug.LogError(
                "[ChatAIPageController] AI request failed: " +
                requestError
            );

            messages.Add(new AIChatMessage
            {
                id = Guid.NewGuid().ToString(),
                role = "assistant",
                content =
                    "Sorry, I couldn't get an AI response right now. " +
                    "Please try again.",
                created_at = DateTime.UtcNow.ToString("o")
            });
        }

        SaveLocalHistory();
        RenderMessages();

        messageInput.SetEnabled(true);
        messageInput.Focus();
        UpdateInputState();
    }

    private void RenderMessages()
    {
        if (messageContainer == null)
            return;

        List<VisualElement> remove = new List<VisualElement>();

        foreach (VisualElement child in messageContainer.Children())
        {
            if (child.ClassListContains("message-row"))
                remove.Add(child);
        }

        foreach (VisualElement child in remove)
            child.RemoveFromHierarchy();

        bool hasMessages = messages.Count > 0;

        if (emptyChat != null)
            emptyChat.style.display =
                hasMessages ? DisplayStyle.None : DisplayStyle.Flex;

        for (int i = 0; i < messages.Count; i++)
        {
            AIChatMessage message = messages[i];

            if (message == null)
                continue;

            bool outgoing =
                string.Equals(
                    message.role,
                    "user",
                    StringComparison.OrdinalIgnoreCase
                );

            bool showAssistantAvatar =
                !outgoing &&
                IsLastAssistantMessageInGroup(i);

            VisualElement row =
                CreateMessageElement(
                    message,
                    outgoing,
                    showAssistantAvatar
                );

            messageContainer.Insert(
                Mathf.Max(1, messageContainer.childCount - 1),
                row
            );
        }

        root.schedule.Execute(ScrollToBottom).ExecuteLater(50);
    }

    private VisualElement CreateMessageElement(
        AIChatMessage message,
        bool outgoing,
        bool showAssistantAvatar)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("message-row");
        row.AddToClassList(
            outgoing ? "outgoing-row" : "incoming-row"
        );

        if (!outgoing)
        {
            VisualElement avatarSlot = new VisualElement();
            avatarSlot.AddToClassList("incoming-avatar-slot");

            if (showAssistantAvatar)
            {
                VisualElement avatar = new VisualElement();
                avatar.AddToClassList("student-avatar");

                Label initials = new Label("AI");
                initials.AddToClassList("student-avatar-text");

                avatar.Add(initials);
                avatarSlot.Add(avatar);
            }

            row.Add(avatarSlot);
        }

        VisualElement group = new VisualElement();
        group.AddToClassList(
            outgoing
                ? "outgoing-message-group"
                : "incoming-message-group"
        );

        VisualElement bubble = new VisualElement();
        bubble.AddToClassList("message-bubble");
        bubble.AddToClassList(
            outgoing ? "outgoing-bubble" : "incoming-bubble"
        );

        Label content = new Label(message.content ?? string.Empty);
        content.AddToClassList("message-text");
        content.AddToClassList(
            outgoing
                ? "outgoing-message-text"
                : "incoming-message-text"
        );

        bubble.Add(content);
        group.Add(bubble);

        VisualElement meta = new VisualElement();
        meta.AddToClassList("message-meta-row");

        Label time = new Label(FormatTime(message.created_at));
        time.AddToClassList("message-time");
        time.AddToClassList(
            outgoing ? "outgoing-time" : "incoming-time"
        );

        meta.Add(time);
        group.Add(meta);
        row.Add(group);

        return row;
    }

    private bool IsLastAssistantMessageInGroup(int index)
    {
        if (index < 0 || index >= messages.Count)
            return false;

        if (index == messages.Count - 1)
            return true;

        AIChatMessage next = messages[index + 1];

        if (next == null)
            return true;

        return !string.Equals(
            next.role,
            "assistant",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private void SetAssistantInformation()
    {
        if (assistantNameLabel != null)
            assistantNameLabel.text = "AI Assistant";

        if (assistantPositionLabel != null)
            assistantPositionLabel.text = "AI Assistant";

        if (assistantStatusLabel != null)
        {
            assistantStatusLabel.text = "Ready to help";
            assistantStatusLabel.EnableInClassList("offline", false);
        }

        if (assistantAvatarLabel != null)
            assistantAvatarLabel.text = "AI";

        if (typingAvatarLabel != null)
            typingAvatarLabel.text = "AI";

        if (headerOnlineDot != null)
            headerOnlineDot.EnableInClassList("offline", false);

        if (statusDot != null)
            statusDot.EnableInClassList("offline", false);

        if (typingRow != null)
            typingRow.style.display = DisplayStyle.None;
    }

    private void FindVisualElements()
    {
        safeArea = root.Q<VisualElement>("safe-area");

        backButton = root.Q<Button>("back-button");
        moreButton = root.Q<Button>("more-button");
        attachmentButton = root.Q<Button>("attachment-button");
        sendButton = root.Q<Button>("send-button");
        sendIcon = root.Q<VisualElement>("send-icon");

        messageInput = root.Q<TextField>("message-input");
        inputPlaceholder = root.Q<Label>("input-placeholder");
        messageScrollView = root.Q<ScrollView>("message-scroll-view");
        messageContainer = root.Q<VisualElement>("message-container");
        emptyChat = root.Q<VisualElement>("empty-chat");
        typingRow = root.Q<VisualElement>("typing-row");

        assistantNameLabel = root.Q<Label>("teacher-name");
        assistantPositionLabel = root.Q<Label>("teacher-position");
        assistantStatusLabel = root.Q<Label>("teacher-status");
        assistantAvatarLabel = root.Q<Label>("partner-avatar-text");
        typingAvatarLabel = root.Q<Label>("typing-avatar-text");

        headerOnlineDot = root.Q<VisualElement>("header-online-dot");
        statusDot = root.Q<VisualElement>("status-dot");
    }

    private void RegisterCallbacks()
    {
        if (backButton != null)
            backButton.clicked += HandleBackClicked;

        if (moreButton != null)
            moreButton.clicked += HandleMoreClicked;

        if (attachmentButton != null)
            attachmentButton.clicked += HandleAttachmentClicked;

        if (sendButton != null)
            sendButton.clicked += SendCurrentMessage;

        if (messageInput != null)
        {
            messageInput.RegisterValueChangedCallback(
                HandleInputValueChanged
            );

            messageInput.RegisterCallback<KeyDownEvent>(
                HandleInputKeyDown
            );
        }
    }

    private void UnregisterCallbacks()
    {
        if (backButton != null)
            backButton.clicked -= HandleBackClicked;

        if (moreButton != null)
            moreButton.clicked -= HandleMoreClicked;

        if (attachmentButton != null)
            attachmentButton.clicked -= HandleAttachmentClicked;

        if (sendButton != null)
            sendButton.clicked -= SendCurrentMessage;

        if (messageInput != null)
        {
            messageInput.UnregisterValueChangedCallback(
                HandleInputValueChanged
            );

            messageInput.UnregisterCallback<KeyDownEvent>(
                HandleInputKeyDown
            );
        }
    }

    private void ConfigureInitialUi()
    {
        if (messageInput != null)
        {
            messageInput.value = string.Empty;
            messageInput.isDelayed = false;
        }

        if (typingRow != null)
            typingRow.style.display = DisplayStyle.None;

        UpdateInputState();
    }

    private void HandleInputValueChanged(
        ChangeEvent<string> evt)
    {
        UpdateInputState();
    }

    private void HandleInputKeyDown(
        KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return &&
            evt.keyCode != KeyCode.KeypadEnter)
        {
            return;
        }

        evt.StopPropagation();
        SendCurrentMessage();
    }

    private void UpdateInputState()
    {
        bool hasText =
            messageInput != null &&
            !string.IsNullOrWhiteSpace(messageInput.value);

        if (inputPlaceholder != null)
        {
            inputPlaceholder.style.display =
                hasText
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
        }

        if (sendButton != null)
        {
            sendButton.EnableInClassList(
                "enabled",
                hasText
            );

            sendButton.SetEnabled(
                hasText && initialized
            );
        }

        if (sendIcon != null)
        {
            sendIcon.EnableInClassList(
                "send-icon-active",
                hasText
            );
        }
    }

    private void HandleBackClicked()
    {
        string scene = PlayerPrefs.GetString(
            ChatAIPreviousSceneKey,
            string.Empty
        );

        if (string.IsNullOrWhiteSpace(scene) ||
            string.Equals(
                scene,
                SceneManager.GetActiveScene().name,
                StringComparison.Ordinal))
        {
            scene = PlayerPrefs.GetString(
                "previous_scene",
                string.Empty
            );
        }

        if (string.IsNullOrWhiteSpace(scene) ||
            string.Equals(
                scene,
                SceneManager.GetActiveScene().name,
                StringComparison.Ordinal))
        {
            scene = previousSceneName;
        }

        if (!Application.CanStreamedLevelBeLoaded(scene))
        {
            Debug.LogError(
                "[ChatAIPageController] Previous scene is not in Build Profiles: " +
                scene
            );

            return;
        }

        PlayerPrefs.DeleteKey(ChatAIPreviousSceneKey);
        PlayerPrefs.Save();

        SceneManager.LoadScene(scene);
    }

    private void HandleMoreClicked()
    {
        Debug.Log(
            "[ChatAIPageController] More button clicked."
        );
    }

    private void HandleAttachmentClicked()
    {
        Debug.Log(
            "[ChatAIPageController] AI attachment picker is not connected yet."
        );
    }

    private void ScrollToBottom()
    {
        if (messageScrollView == null)
            return;

        messageScrollView.schedule.Execute(() =>
        {
            messageScrollView.scrollOffset =
                new Vector2(
                    0,
                    messageScrollView.verticalScroller.highValue
                );
        }).ExecuteLater(10);
    }

    private void OnRootGeometryChanged(
        GeometryChangedEvent evt)
    {
        ApplySafeArea();
    }

    private void ApplySafeArea()
    {
        if (safeArea == null || root == null)
            return;

        Rect area = Screen.safeArea;

        float sw = Mathf.Max(Screen.width, 1);
        float sh = Mathf.Max(Screen.height, 1);
        float pw = root.resolvedStyle.width;
        float ph = root.resolvedStyle.height;

        if (pw <= 0 || ph <= 0)
            return;

        safeArea.style.paddingLeft =
            area.xMin / sw * pw;

        safeArea.style.paddingRight =
            (sw - area.xMax) / sw * pw;

        safeArea.style.paddingTop =
            Mathf.Max(
                (sh - area.yMax) / sh * ph,
                minimumTopSafePadding
            );

        safeArea.style.paddingBottom =
            area.yMin / sh * ph;
    }

    private static string FormatTime(string iso)
    {
        return DateTime.TryParse(
            iso,
            out DateTime time)
                ? time.ToLocalTime().ToString("h:mm tt")
                : string.Empty;
    }
}
