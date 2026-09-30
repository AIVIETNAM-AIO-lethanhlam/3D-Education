using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// ChatAIScene controller.
///
/// IMPORTANT:
/// - This controller is intentionally independent from ChatScene.
/// - It does NOT read selected_chat_user_id.
/// - It does NOT read/write selected_chat_conversation_id.
/// - It does NOT use chat_conversations, chat_messages, user_presence or chat_typing.
/// - Each signed-in user gets a private Supabase AI history.
/// - PlayerPrefs remains an offline/cache fallback per user.
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
    private VisualElement topSafeBackground;

    private Button backButton;
    private Button moreButton;
    private Button attachmentButton;
    private Button sendButton;
    private VisualElement sendIcon;

    private TextField messageInput;
    private Label inputPlaceholder;
    private VisualElement messageInputSection;
    private VisualElement composerSection;
    private VisualElement selectedImagePreview;
    private VisualElement selectedImageThumbnail;
    private Label selectedImageName;
    private Button removeSelectedImageButton;
    private VisualElement imageGalleryPanel;
    private VisualElement imageGalleryGrid;
    private ScrollView imageGalleryScroll;
    private Label imageGalleryTitle;
    private Label imageGalleryStatus;
    private Button closeImageGalleryButton;
    private ScrollView messageScrollView;
    private VisualElement messageContainer;
    private VisualElement emptyChat;
    private VisualElement typingRow;

    private Label courseLabel;
    private Label dateLabel;
    private Label emptyChatTitleLabel;
    private Label emptyChatDescriptionLabel;
    private Label typingLabel;

    private Label assistantNameLabel;
    private Label assistantPositionLabel;
    private Label assistantStatusLabel;
    private Label assistantAvatarLabel;
    private Label typingAvatarLabel;
    private VisualElement headerOnlineDot;
    private VisualElement statusDot;

    private string currentUserId;
    private string historyKey;

    // BUG-016: only one AI request at a time (the send button used to be
    // re-enabled by picking an image while a request was still running).
    private bool isAwaitingAI;

    // BUG-012: popup opened by the header "more" (hamburger) button.
    private VisualElement moreMenuOverlay;
    private bool initialized;
    private Coroutine keyboardMonitor;
    private float lastKeyboardHeight = -1f;
    private const int MaxSelectedImages = 4;
    private readonly List<SelectedImageData> selectedImages =
        new List<SelectedImageData>();
    private readonly List<Texture2D> galleryThumbnailTextures =
        new List<Texture2D>();
    private readonly HashSet<string> loadedGalleryUris = new HashSet<string>();
    private const int GalleryPageSize = 16;
    private int galleryNextOffset;
    private int galleryLoadGeneration;
    private bool galleryHasMore = true;
    private bool galleryPageLoading;
    private Coroutine galleryScrollMonitor;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject androidActivity;
    private bool androidKeyboardProbeWarningLogged;
    private PermissionCallbacks galleryPermissionCallbacks;
#endif

    private readonly List<AIChatMessage> messages = new List<AIChatMessage>();

    private List<AIService.AIHistoryItem> BuildHistoryForAI(AIChatMessage currentMessage)
    {
        List<AIService.AIHistoryItem> history = new List<AIService.AIHistoryItem>();

        foreach (AIChatMessage message in messages)
        {
            if (message == null || ReferenceEquals(message, currentMessage))
                continue;

            if (string.IsNullOrWhiteSpace(message.content))
                continue;

            history.Add(new AIService.AIHistoryItem
            {
                role = string.Equals(message.role, "user", StringComparison.OrdinalIgnoreCase)
                    ? "user"
                    : "model",
                text = message.content
            });
        }

        return history;
    }

    [Serializable]
    private class AIChatMessage
    {
        public string id;
        public string role;       // "user" or "assistant"
        public string content;
        public string created_at;
        public bool imageAttached;
        public List<string> imagePaths = new List<string>();

        [NonSerialized]
        public Texture2D runtimeImage;

        [NonSerialized]
        public List<Texture2D> runtimeImages;
    }

    [Serializable]
    private class AIChatHistory
    {
        public List<AIChatMessage> items = new List<AIChatMessage>();
    }

    [Serializable]
    private class GalleryImageItem
    {
        public string uri;
        public string displayName;
        public string thumbnailPath;
    }

    [Serializable]
    private class GalleryImageResult
    {
        public GalleryImageItem[] items;
        public string error;
        public int nextOffset;
        public bool hasMore;
    }

    private class SelectedImageData
    {
        public string uri;
        public string fileName;
        public string base64;
        public string mimeType;
        public Texture2D texture;
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

        AppLanguageManager.LanguageChanged += OnLanguageChanged;

        ConfigureInitialUi();
        ApplyCurrentLanguage();

#if UNITY_ANDROID && !UNITY_EDITOR
        ConfigureAndroidSoftInput();
#endif

        ApplySafeArea();
        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

        keyboardMonitor = StartCoroutine(MonitorKeyboard());

        InitializeAIChat();
    }

    private void OnDisable()
    {
        isAwaitingAI = false;
        CloseMoreMenu();
        AppLanguageManager.LanguageChanged -= OnLanguageChanged;
        UnregisterCallbacks();

        if (root != null)
            root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

        if (keyboardMonitor != null)
        {
            StopCoroutine(keyboardMonitor);
            keyboardMonitor = null;
        }

        ClearSelectedImage();
        CloseInlineGallery();

        foreach (AIChatMessage message in messages)
        {
            if (message?.runtimeImages != null)
            {
                foreach (Texture2D texture in message.runtimeImages)
                {
                    if (texture != null)
                        Destroy(texture);
                }
                message.runtimeImages.Clear();
            }
            else if (message?.runtimeImage != null)
            {
                Destroy(message.runtimeImage);
                message.runtimeImage = null;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        androidActivity?.Dispose();
        androidActivity = null;
#endif
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
        LocalizeDefaultSystemMessages();

        // First AI conversation for this user only.
        if (messages.Count == 0)
        {
            AIChatMessage greeting = new AIChatMessage
            {
                id = Guid.NewGuid().ToString(),
                role = "assistant",
                content = GetDefaultGreeting(),
                created_at = DateTime.UtcNow.ToString("o")
            };
            messages.Add(greeting);

            SaveLocalHistory();
            if (SupabaseSession.IsLoggedIn)
                StartCoroutine(PersistMessage(greeting, null));
        }

        initialized = true;
        RenderMessages();
        UpdateInputState();

        if (SupabaseSession.IsLoggedIn)
            StartCoroutine(LoadSupabaseHistory());

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
        if (!initialized || messageInput == null || isAwaitingAI)
            return;

        string text = messageInput.value?.Trim();

        if (string.IsNullOrWhiteSpace(text) && selectedImages.Count == 0)
            return;

        // Keep the visible user message truly empty when the user sends only
        // images. Language/context instructions are added only to the hidden
        // AI request by BuildLanguageAwarePrompt().
        string effectiveText = text ?? string.Empty;

        // Transfer ownership of all selected textures to the message bubble.
        List<SelectedImageData> imagesToSend =
            new List<SelectedImageData>(selectedImages);
        selectedImages.Clear();
        CloseInlineGallery();
        UpdateInputState();

        StartCoroutine(
            SendMessageRoutine(
                effectiveText,
                imagesToSend
            )
        );
    }

    private IEnumerator SendMessageRoutine(
        string text,
        List<SelectedImageData> attachedImages = null)
    {
        if (messageInput == null)
            yield break;

        // BUG-009: keep the input editable while the AI answers, so the next
        // question can be drafted; only sending is blocked until the answer arrives.
        isAwaitingAI = true;

        if (sendButton != null)
            sendButton.SetEnabled(false);

        attachmentButton?.SetEnabled(false);

        bool hasImage = attachedImages != null && attachedImages.Count > 0;
        List<Texture2D> messageTextures = hasImage
            ? attachedImages.ConvertAll(image => image.texture)
            : new List<Texture2D>();

        AIChatMessage userMessage = new AIChatMessage
        {
            id = Guid.NewGuid().ToString(),
            role = "user",
            content = text,
            created_at = DateTime.UtcNow.ToString("o"),
            imageAttached = hasImage,
            runtimeImages = messageTextures
        };
        messages.Add(userMessage);

        if (SupabaseSession.IsLoggedIn)
        {
            StartCoroutine(
                PersistMessage(
                    userMessage,
                    hasImage ? attachedImages : null
                )
            );
        }

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

        // Send the previous turns so the AI understands follow-up questions.
        AIService.SetConversationHistory(BuildHistoryForAI(userMessage));

        IEnumerator aiRequest = hasImage
            ? AIService.SendMessageWithImages(
                BuildLanguageAwarePrompt(text),
                attachedImages.ConvertAll(image => image.base64).ToArray(),
                attachedImages.ConvertAll(image => image.mimeType).ToArray(),
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
            )
            : AIService.SendMessage(
                BuildLanguageAwarePrompt(text),
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

        yield return aiRequest;

        if (!requestFinished)
        {
            requestError =
                "AI request finished without a response.";
        }

        if (typingRow != null)
            typingRow.style.display = DisplayStyle.None;

        if (!string.IsNullOrWhiteSpace(aiResponse))
        {
            AIChatMessage assistantMessage = new AIChatMessage
            {
                id = Guid.NewGuid().ToString(),
                role = "assistant",
                content = aiResponse.Trim(),
                created_at = DateTime.UtcNow.ToString("o")
            };
            messages.Add(assistantMessage);
            if (SupabaseSession.IsLoggedIn)
                StartCoroutine(PersistMessage(assistantMessage, null));
        }
        else
        {
            Debug.LogError(
                "[ChatAIPageController] AI request failed: " +
                requestError
            );

            bool rateLimited = AIService.IsRateLimitError(requestError);

            AIChatMessage assistantMessage = new AIChatMessage
            {
                id = Guid.NewGuid().ToString(),
                role = "assistant",
                content = rateLimited
                    ? T(
                        "The AI assistant is receiving too many requests. Please try again in about 30 seconds.",
                        "Trợ lý AI đang tiếp nhận nhiều yêu cầu, vui lòng thử lại sau khoảng 30 giây.")
                    : T(
                    "Sorry, I couldn't get an AI response right now. Please try again.",
                    "Xin lỗi, hiện tại tôi chưa thể nhận được phản hồi từ AI. Vui lòng thử lại."
                ),
                created_at = DateTime.UtcNow.ToString("o")
            };
            messages.Add(assistantMessage);
            if (SupabaseSession.IsLoggedIn)
                StartCoroutine(PersistMessage(assistantMessage, null));
        }

        SaveLocalHistory();
        RenderMessages();

        isAwaitingAI = false;
        attachmentButton?.SetEnabled(true);
        messageInput.SetEnabled(true);
        messageInput.Focus();
        UpdateInputState();
    }

    private IEnumerator PersistMessage(
        AIChatMessage message,
        List<SelectedImageData> attachedImages)
    {
        List<string> base64Values = attachedImages == null
            ? new List<string>()
            : attachedImages.ConvertAll(image => image.base64);
        List<string> mimeTypes = attachedImages == null
            ? new List<string>()
            : attachedImages.ConvertAll(image => image.mimeType);

        string[] storedPaths = null;
        string saveError = string.Empty;

        yield return SupabaseAIChatHistoryService.SaveMessage(
            message.id,
            currentUserId,
            message.role,
            message.content,
            message.created_at,
            base64Values,
            mimeTypes,
            paths => storedPaths = paths,
            error => saveError = error
        );

        if (!string.IsNullOrWhiteSpace(saveError))
        {
            Debug.LogWarning("[ChatAIPageController] " + saveError);
            yield break;
        }

        message.imagePaths = storedPaths == null
            ? new List<string>()
            : new List<string>(storedPaths);
        SaveLocalHistory();
    }

    private IEnumerator LoadSupabaseHistory()
    {
        SupabaseAIChatHistoryService.MessageRow[] rows = null;
        string loadError = string.Empty;

        yield return SupabaseAIChatHistoryService.LoadMessages(
            currentUserId,
            result => rows = result,
            error => loadError = error
        );

        if (!string.IsNullOrWhiteSpace(loadError))
        {
            Debug.LogWarning("[ChatAIPageController] " + loadError);
            yield break;
        }

        if (rows == null || rows.Length == 0)
            yield break;

        List<AIChatMessage> remoteMessages = new List<AIChatMessage>();
        foreach (SupabaseAIChatHistoryService.MessageRow row in rows)
        {
            AIChatMessage message = new AIChatMessage
            {
                id = row.id,
                role = row.role,
                content = row.content ?? string.Empty,
                created_at = row.created_at,
                imagePaths = row.image_paths == null
                    ? new List<string>()
                    : new List<string>(row.image_paths),
                imageAttached = row.image_paths != null && row.image_paths.Length > 0,
                runtimeImages = new List<Texture2D>()
            };

            foreach (string path in message.imagePaths)
            {
                Texture2D downloaded = null;
                string imageError = string.Empty;
                yield return SupabaseAIChatHistoryService.DownloadImage(
                    path,
                    texture => downloaded = texture,
                    error => imageError = error
                );

                if (downloaded != null)
                    message.runtimeImages.Add(downloaded);
                else if (!string.IsNullOrWhiteSpace(imageError))
                    Debug.LogWarning("[ChatAIPageController] Image restore failed: " + imageError);
            }

            remoteMessages.Add(message);
        }

        foreach (AIChatMessage oldMessage in messages)
        {
            if (oldMessage?.runtimeImages == null) continue;
            foreach (Texture2D texture in oldMessage.runtimeImages)
                if (texture != null) Destroy(texture);
        }

        messages.Clear();
        messages.AddRange(remoteMessages);
        LocalizeDefaultSystemMessages();
        SaveLocalHistory();
        RenderMessages();
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

                VisualElement avatarIcon = new VisualElement();
                avatarIcon.AddToClassList("student-avatar-text");
                avatarIcon.AddToClassList("ai-avatar-icon");
                avatarIcon.AddToClassList("ai-avatar-icon-small");

                avatar.Add(avatarIcon);
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

        List<Texture2D> messageImages = message.runtimeImages;
        if ((messageImages == null || messageImages.Count == 0) &&
            message.runtimeImage != null)
        {
            messageImages = new List<Texture2D> { message.runtimeImage };
        }

        if (messageImages != null && messageImages.Count > 0)
        {
            VisualElement imageGrid = new VisualElement();
            imageGrid.AddToClassList("message-images-grid");
            imageGrid.EnableInClassList("single", messageImages.Count == 1);
            imageGrid.EnableInClassList(
                "with-caption",
                !string.IsNullOrWhiteSpace(message.content)
            );

            foreach (Texture2D texture in messageImages)
            {
                if (texture == null)
                    continue;

                VisualElement image = new VisualElement();
                image.AddToClassList("message-image");
                image.style.backgroundImage = new StyleBackground(texture);
                imageGrid.Add(image);
            }
            // Images are siblings of the text bubble, not children of it.
            // This prevents the outgoing blue bubble from becoming a large
            // frame around both the image and its optional caption.
            group.Add(imageGrid);
        }

        if (!string.IsNullOrWhiteSpace(message.content))
        {
            VisualElement bubble = new VisualElement();
            bubble.AddToClassList("message-bubble");
            bubble.AddToClassList(
                outgoing ? "outgoing-bubble" : "incoming-bubble"
            );

            Label content = new Label(message.content);
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
            assistantNameLabel.text =
                T("AI Assistant", "Trợ lý AI");

        if (assistantPositionLabel != null)
            assistantPositionLabel.text =
                T("AI Assistant", "Trợ lý AI");

        if (assistantStatusLabel != null)
        {
            assistantStatusLabel.text =
                T("Ready to help", "Sẵn sàng");

            assistantStatusLabel.EnableInClassList(
                "offline",
                false
            );
        }

        if (assistantAvatarLabel != null)
            assistantAvatarLabel.text = string.Empty;

        if (typingAvatarLabel != null)
            typingAvatarLabel.text = string.Empty;

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
        topSafeBackground = root.Q<VisualElement>("chat-top-safe-background");

        backButton = root.Q<Button>("back-button");
        moreButton = root.Q<Button>("more-button");
        attachmentButton = root.Q<Button>("attachment-button");
        sendButton = root.Q<Button>("send-button");
        sendIcon = root.Q<VisualElement>("send-icon");

        messageInput = root.Q<TextField>("message-input");
        inputPlaceholder = root.Q<Label>("input-placeholder");
        messageInputSection = root.Q<VisualElement>("message-input-section");
        composerSection = root.Q<VisualElement>("composer-section");
        selectedImagePreview = root.Q<VisualElement>("selected-image-preview");
        selectedImageThumbnail = root.Q<VisualElement>("selected-image-thumbnail");
        selectedImageName = root.Q<Label>("selected-image-name");
        removeSelectedImageButton = root.Q<Button>("remove-selected-image-button");
        imageGalleryPanel = root.Q<VisualElement>("image-gallery-panel");
        imageGalleryGrid = root.Q<VisualElement>("image-gallery-grid");
        imageGalleryScroll = root.Q<ScrollView>("image-gallery-scroll");
        imageGalleryTitle = root.Q<Label>("image-gallery-title");
        imageGalleryStatus = root.Q<Label>("image-gallery-status");
        closeImageGalleryButton = root.Q<Button>("close-image-gallery-button");
        messageScrollView = root.Q<ScrollView>("message-scroll-view");
        messageContainer = root.Q<VisualElement>("message-container");
        emptyChat = root.Q<VisualElement>("empty-chat");
        typingRow = root.Q<VisualElement>("typing-row");

        courseLabel = root.Q<Label>("course-label");
        dateLabel = root.Q<Label>("date-label");
        emptyChatTitleLabel = root.Q<Label>("empty-chat-title");
        emptyChatDescriptionLabel = root.Q<Label>("empty-chat-description");
        typingLabel = root.Q<Label>("typing-label");

        assistantNameLabel = root.Q<Label>("teacher-name");
        assistantPositionLabel = root.Q<Label>("teacher-position");
        assistantStatusLabel = root.Q<Label>("teacher-status");
        assistantAvatarLabel = root.Q<Label>("partner-avatar-text");
        typingAvatarLabel = root.Q<Label>("typing-avatar-text");

        headerOnlineDot = root.Q<VisualElement>("header-online-dot");
        statusDot = root.Q<VisualElement>("status-dot");
    }


    private static string T(
        string english,
        string vietnamese)
    {
        return AppLanguageManager.IsVietnamese
            ? vietnamese
            : english;
    }

    private void OnLanguageChanged(
        string language)
    {
        ApplyCurrentLanguage();
        LocalizeDefaultSystemMessages();

        if (initialized)
            RenderMessages();
    }

    private void ApplyCurrentLanguage()
    {
        SetAssistantInformation();

        if (courseLabel != null)
        {
            courseLabel.text =
                T(
                    "AI learning assistant",
                    "Trợ lý học tập AI"
                );
        }

        if (dateLabel != null)
            dateLabel.text = T("Today", "Hôm nay");

        if (emptyChatTitleLabel != null)
        {
            emptyChatTitleLabel.text =
                T(
                    "Start a conversation with AI",
                    "Bắt đầu trò chuyện với AI"
                );
        }

        if (emptyChatDescriptionLabel != null)
        {
            emptyChatDescriptionLabel.text =
                T(
                    "Your AI chat history is securely synced for this signed-in account.",
                    "Lịch sử trò chuyện AI được đồng bộ an toàn cho tài khoản đang đăng nhập."
                );
        }

        if (typingLabel != null)
            typingLabel.text = T("Typing...", "AI đang trả lời...");

        if (inputPlaceholder != null)
        {
            inputPlaceholder.text =
                T(
                    "Ask AI Assistant...",
                    "Hỏi Trợ lý AI..."
                );
        }
    }

    private static string GetDefaultGreeting()
    {
        return T(
            "Hello! I’m AI Assistant. How can I help you with your learning today?",
            "Xin chào! Tôi là Trợ lý AI. Hôm nay tôi có thể hỗ trợ bạn học tập như thế nào?"
        );
    }

    private void LocalizeDefaultSystemMessages()
    {
        bool changed = false;

        const string EnglishGreeting =
            "Hello! I’m AI Assistant. How can I help you with your learning today?";

        const string VietnameseGreeting =
            "Xin chào! Tôi là Trợ lý AI. Hôm nay tôi có thể hỗ trợ bạn học tập như thế nào?";

        const string EnglishError =
            "Sorry, I couldn't get an AI response right now. Please try again.";

        const string VietnameseError =
            "Xin lỗi, hiện tại tôi chưa thể nhận được phản hồi từ AI. Vui lòng thử lại.";

        foreach (AIChatMessage message in messages)
        {
            if (message == null ||
                !string.Equals(
                    message.role,
                    "assistant",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (message.content == EnglishGreeting ||
                message.content == VietnameseGreeting)
            {
                string localizedGreeting =
                    AppLanguageManager.IsVietnamese
                        ? VietnameseGreeting
                        : EnglishGreeting;

                if (message.content != localizedGreeting)
                {
                    message.content = localizedGreeting;
                    changed = true;
                }

                continue;
            }

            if (message.content == EnglishError ||
                message.content == VietnameseError)
            {
                string localizedError =
                    AppLanguageManager.IsVietnamese
                        ? VietnameseError
                        : EnglishError;

                if (message.content != localizedError)
                {
                    message.content = localizedError;
                    changed = true;
                }
            }
        }

        if (changed)
            SaveLocalHistory();
    }

    private void RegisterCallbacks()
    {
        if (backButton != null)
            backButton.clicked += HandleBackClicked;

        if (moreButton != null)
            moreButton.clicked += HandleMoreClicked;

        if (attachmentButton != null)
            attachmentButton.clicked += HandleAttachmentClicked;

        if (removeSelectedImageButton != null)
            removeSelectedImageButton.clicked += HandleRemoveSelectedImage;

        if (closeImageGalleryButton != null)
            closeImageGalleryButton.clicked += CloseInlineGallery;

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

            messageInput.RegisterCallback<FocusInEvent>(
                HandleInputFocusIn
            );

            messageInput.RegisterCallback<FocusOutEvent>(
                HandleInputFocusOut
            );
        }

        if (messageScrollView != null)
            messageScrollView.RegisterCallback<PointerDownEvent>(
                HandleMessageAreaPointerDown
            );
    }

    private void UnregisterCallbacks()
    {
        if (backButton != null)
            backButton.clicked -= HandleBackClicked;

        if (moreButton != null)
            moreButton.clicked -= HandleMoreClicked;

        if (attachmentButton != null)
            attachmentButton.clicked -= HandleAttachmentClicked;

        if (removeSelectedImageButton != null)
            removeSelectedImageButton.clicked -= HandleRemoveSelectedImage;

        if (closeImageGalleryButton != null)
            closeImageGalleryButton.clicked -= CloseInlineGallery;

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

            messageInput.UnregisterCallback<FocusInEvent>(
                HandleInputFocusIn
            );

            messageInput.UnregisterCallback<FocusOutEvent>(
                HandleInputFocusOut
            );
        }

        if (messageScrollView != null)
            messageScrollView.UnregisterCallback<PointerDownEvent>(
                HandleMessageAreaPointerDown
            );
    }

    private void HandleMessageAreaPointerDown(PointerDownEvent evt)
    {
        VisualElement target = evt.target as VisualElement;
        bool tappedEmptyMessageArea =
            target == messageScrollView ||
            target == messageScrollView?.contentContainer ||
            target == messageContainer;

        if (tappedEmptyMessageArea && IsImageGalleryOpen())
            CloseInlineGallery();
    }

    private void HandleInputFocusIn(FocusInEvent evt)
    {
        // Android can emit pointer/focus and window-resize events in different
        // orders while opening the keyboard. If images are already selected,
        // the inline gallery must remain visible so the user can continue
        // reviewing/changing the selection while typing a prompt.
        EnsureSelectedImageGalleryVisible();

        // The native keyboard reports its final size a few frames after focus.
        // Monitoring continuously keeps the composer aligned during animation,
        // orientation changes and different keyboard modes.
        root?.schedule.Execute(() =>
        {
            EnsureSelectedImageGalleryVisible();
            ApplyKeyboardInset(force: true);
            ScrollToBottom();
        }).ExecuteLater(100);

        // Some Android keyboards perform a second resize after their opening
        // animation. Re-assert the gallery state after that resize as well.
        root?.schedule.Execute(() =>
        {
            EnsureSelectedImageGalleryVisible();
            ApplyKeyboardInset(force: true);
        }).ExecuteLater(350);
    }

    private void HandleInputFocusOut(FocusOutEvent evt)
    {
        root?.schedule.Execute(() =>
        {
            ApplyKeyboardInset(force: true);
        }).ExecuteLater(100);
    }

    private void ConfigureInitialUi()
    {
        if (messageScrollView != null)
        {
            // Prevent Android's elastic overscroll from moving a short chat
            // history away from its resting position.
            messageScrollView.touchScrollBehavior =
                ScrollView.TouchScrollBehavior.Clamped;
        }

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

        bool canSend = hasText || selectedImages.Count > 0;

        if (inputPlaceholder != null)
        {
            inputPlaceholder.style.display =
                hasText
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
        }


        if (imageGalleryTitle != null)
        {
            string title = T("Recent photos", "Ảnh gần đây");
            imageGalleryTitle.text = selectedImages.Count > 0
                ? $"{title} ({selectedImages.Count}/{MaxSelectedImages})"
                : title;
        }

        if (sendButton != null)
        {
            sendButton.EnableInClassList(
                "enabled",
                canSend
            );

            sendButton.SetEnabled(
                canSend && initialized && !isAwaitingAI
            );
        }

        if (sendIcon != null)
        {
            sendIcon.EnableInClassList(
                "send-icon-active",
                canSend
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

    // =========================================================
    // BUG-012: header "more" (hamburger) menu
    // =========================================================

    private void HandleMoreClicked()
    {
        if (moreMenuOverlay != null)
        {
            CloseMoreMenu();
            return;
        }

        OpenMoreMenu();
    }

    private void OpenMoreMenu()
    {
        if (root == null)
            return;

        // Full-screen transparent layer: a tap outside the card closes the menu.
        moreMenuOverlay = new VisualElement { name = "ai-more-menu-overlay" };
        moreMenuOverlay.style.position = Position.Absolute;
        moreMenuOverlay.style.left = 0;
        moreMenuOverlay.style.right = 0;
        moreMenuOverlay.style.top = 0;
        moreMenuOverlay.style.bottom = 0;
        moreMenuOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.18f);
        moreMenuOverlay.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == moreMenuOverlay)
                CloseMoreMenu();
        });

        VisualElement card = new VisualElement { name = "ai-more-menu" };
        card.style.position = Position.Absolute;
        card.style.right = 16;
        card.style.top = GetMoreMenuTop();
        card.style.minWidth = 220;
        card.style.paddingTop = 6;
        card.style.paddingBottom = 6;
        card.style.backgroundColor = Color.white;
        card.style.borderTopLeftRadius = 14;
        card.style.borderTopRightRadius = 14;
        card.style.borderBottomLeftRadius = 14;
        card.style.borderBottomRightRadius = 14;
        card.style.borderLeftWidth = 1;
        card.style.borderRightWidth = 1;
        card.style.borderTopWidth = 1;
        card.style.borderBottomWidth = 1;
        Color border = new Color32(220, 228, 242, 255);
        card.style.borderLeftColor = border;
        card.style.borderRightColor = border;
        card.style.borderTopColor = border;
        card.style.borderBottomColor = border;

        card.Add(CreateMoreMenuItem(
            T("New conversation", "Cuộc trò chuyện mới"),
            () =>
            {
                CloseMoreMenu();
                StartCoroutine(StartNewConversationRoutine());
            }));

        card.Add(CreateMoreMenuItem(
            T("Scroll to latest message", "Đến tin nhắn mới nhất"),
            () =>
            {
                CloseMoreMenu();
                ScrollToBottom();
            }));

        card.Add(CreateMoreMenuItem(T("Close", "Đóng"), CloseMoreMenu));

        moreMenuOverlay.Add(card);
        root.Add(moreMenuOverlay);
        moreMenuOverlay.BringToFront();
    }

    private float GetMoreMenuTop()
    {
        if (moreButton != null && root != null)
        {
            Rect bounds = moreButton.worldBound;
            Rect rootBounds = root.worldBound;
            if (bounds.height > 0f)
                return bounds.yMax - rootBounds.yMin + 6f;
        }

        return 96f;
    }

    private static Button CreateMoreMenuItem(string text, Action onClick)
    {
        Button item = new Button(onClick) { text = text };
        item.style.marginLeft = 0;
        item.style.marginRight = 0;
        item.style.marginTop = 0;
        item.style.marginBottom = 0;
        item.style.paddingLeft = 16;
        item.style.paddingRight = 16;
        item.style.height = 44;
        item.style.unityTextAlign = TextAnchor.MiddleLeft;
        item.style.fontSize = 15;
        item.style.color = (Color)new Color32(14, 35, 73, 255);
        item.style.backgroundColor = Color.clear;
        item.style.borderLeftWidth = 0;
        item.style.borderRightWidth = 0;
        item.style.borderTopWidth = 0;
        item.style.borderBottomWidth = 0;
        return item;
    }

    private void CloseMoreMenu()
    {
        if (moreMenuOverlay == null)
            return;

        moreMenuOverlay.RemoveFromHierarchy();
        moreMenuOverlay = null;
    }

    private IEnumerator StartNewConversationRoutine()
    {
        if (isAwaitingAI)
            yield break;

        if (SupabaseSession.IsLoggedIn &&
            !string.IsNullOrWhiteSpace(currentUserId) &&
            currentUserId != "guest")
        {
            string deleteError = null;
            yield return SupabaseAIChatHistoryService.DeleteAllMessages(
                currentUserId,
                message => deleteError = message);

            if (!string.IsNullOrWhiteSpace(deleteError))
            {
                Debug.LogWarning(
                    "[ChatAIPageController] Could not clear AI chat history: " + deleteError);

                messages.Add(new AIChatMessage
                {
                    id = Guid.NewGuid().ToString(),
                    role = "assistant",
                    content = T(
                        "Could not start a new conversation right now. Please try again.",
                        "Chưa thể bắt đầu cuộc trò chuyện mới lúc này. Vui lòng thử lại."),
                    created_at = DateTime.UtcNow.ToString("o")
                });
                RenderMessages();
                ScrollToBottom();
                yield break;
            }
        }

        messages.Clear();

        AIChatMessage greeting = new AIChatMessage
        {
            id = Guid.NewGuid().ToString(),
            role = "assistant",
            content = GetDefaultGreeting(),
            created_at = DateTime.UtcNow.ToString("o")
        };
        messages.Add(greeting);

        SaveLocalHistory();
        if (SupabaseSession.IsLoggedIn)
            StartCoroutine(PersistMessage(greeting, null));

        RenderMessages();
        UpdateInputState();
        ScrollToBottom();
    }

    private void HandleAttachmentClicked()
    {
        messageInput?.Blur();

        // The attachment button works as a toggle while the in-app gallery is
        // visible. Selected images stay selected after the tray is closed.
        if (IsImageGalleryOpen())
        {
            CloseInlineGallery();
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        RequestAndroidGalleryPermission();
#else
        OpenImagePicker();
#endif
    }

    private void HandleRemoveSelectedImage()
    {
        ClearSelectedImage();
        UpdateInputState();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void RequestAndroidGalleryPermission()
    {
        string permission = GetAndroidSdkInt() >= 33
            ? "android.permission.READ_MEDIA_IMAGES"
            : Permission.ExternalStorageRead;

        if (Permission.HasUserAuthorizedPermission(permission))
        {
            OpenImagePicker();
            return;
        }

        galleryPermissionCallbacks = new PermissionCallbacks();
        galleryPermissionCallbacks.PermissionGranted += _ => OpenImagePicker();
        galleryPermissionCallbacks.PermissionDenied += _ =>
            ShowAttachmentStatus(
                T(
                    "Photo access was denied.",
                    "Quyền truy cập hình ảnh đã bị từ chối."
                )
            );
        galleryPermissionCallbacks.PermissionDeniedAndDontAskAgain += _ =>
            ShowAttachmentStatus(
                T(
                    "Enable photo access in Android Settings.",
                    "Hãy bật quyền hình ảnh trong Cài đặt Android."
                )
            );

        Permission.RequestUserPermission(permission, galleryPermissionCallbacks);
    }

    private static int GetAndroidSdkInt()
    {
        using AndroidJavaClass version =
            new AndroidJavaClass("android.os.Build$VERSION");
        return version.GetStatic<int>("SDK_INT");
    }
#endif

    private void OpenImagePicker()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        OpenInlineGallery();
#else
        if (NativeFilePicker.IsFilePickerBusy())
            return;

        NativeFilePicker.PickFile(path =>
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            LoadSelectedImage(path);
        }, "image/*");
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
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
            // The gallery is intentionally the last child in UXML. It stays
            // below the composer and reduces the message viewport, moving the
            // composer upward just like the software keyboard.
            imageGalleryPanel?.AddToClassList("visible");
            imageGalleryPanel?.BringToFront();

            if (messageInput != null)
                messageInput.Blur();

            if (imageGalleryStatus != null)
            {
                imageGalleryStatus.text = T("Loading photos...", "Đang tải ảnh...");
                imageGalleryStatus.style.display = DisplayStyle.Flex;
            }

            StartCoroutine(LoadGalleryPage(generation));
            if (galleryScrollMonitor != null)
                StopCoroutine(galleryScrollMonitor);
            galleryScrollMonitor = StartCoroutine(MonitorGalleryScroll());
        }
        catch (Exception exception)
        {
            Debug.LogError("[ChatAIPageController] In-app gallery failed: " + exception);
            if (imageGalleryStatus != null)
                imageGalleryStatus.text = T(
                    "Could not load photos.",
                    "Không thể tải hình ảnh."
                );
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
            Debug.LogError("[ChatAIPageController] Gallery page failed: " + exception);
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
        tile.style.backgroundColor = new StyleColor(new Color32(242, 246, 252, 255));
        tile.tooltip = item.displayName;

        // A Button background can be cached by UI Toolkit before Android has
        // uploaded a newly decoded texture, causing black thumbnails on the
        // first gallery open. Render the photo through a dedicated Image and
        // rebind it after layout instead.
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

    private IEnumerator MonitorGalleryScroll()
    {
        while (IsImageGalleryOpen())
        {
            MaybeLoadMoreGalleryImages();
            yield return null;
        }
        galleryScrollMonitor = null;
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

    private bool IsImageSelected(string uri)
    {
        return selectedImages.Exists(image => image.uri == uri);
    }

    private void ToggleGalleryImage(GalleryImageItem item, Button tile)
    {
        try
        {
            int existingIndex = selectedImages.FindIndex(
                image => image.uri == item.uri
            );

            if (existingIndex >= 0)
            {
                SelectedImageData removed = selectedImages[existingIndex];
                selectedImages.RemoveAt(existingIndex);
                if (removed.texture != null)
                    Destroy(removed.texture);

                tile?.RemoveFromClassList("selected");
                UpdateInputState();
                return;
            }

            if (selectedImages.Count >= MaxSelectedImages)
            {
                ShowAttachmentStatus(T(
                    "You can select up to 4 images.",
                    "Bạn có thể chọn tối đa 4 ảnh."
                ));
                return;
            }

            using AndroidJavaClass picker =
                new AndroidJavaClass("com.virtualeducation.chat.ChatImagePicker");

            string localPath = picker.CallStatic<string>(
                "copyImageToCache",
                androidActivity,
                item.uri,
                item.displayName
            );

            if (string.IsNullOrWhiteSpace(localPath))
            {
                ShowAttachmentStatus(T(
                    "Could not open this image.",
                    "Không thể mở hình ảnh này."
                ));
                return;
            }

            SelectedImageData selected = PrepareSelectedImage(
                localPath,
                item.uri
            );

            if (selected == null)
                return;

            selectedImages.Add(selected);
            tile?.AddToClassList("selected");
            UpdateInputState();
        }
        catch (Exception exception)
        {
            Debug.LogError("[ChatAIPageController] Gallery selection failed: " + exception);
            ShowAttachmentStatus(T(
                "Could not open this image.",
                "Không thể mở hình ảnh này."
            ));
        }
    }
#else
    private void OpenInlineGallery() {}
#endif

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
            Debug.LogWarning("[ChatAIPageController] Thumbnail load failed: " + exception.Message);
        }

        return null;
    }

    private void CloseInlineGallery()
    {
        galleryLoadGeneration++;
        galleryPageLoading = false;
        if (galleryScrollMonitor != null)
        {
            StopCoroutine(galleryScrollMonitor);
            galleryScrollMonitor = null;
        }
        imageGalleryPanel?.RemoveFromClassList("visible");
        imageGalleryGrid?.Clear();
        ClearGalleryThumbnails();
    }

    private bool IsImageGalleryOpen()
    {
        return imageGalleryPanel != null &&
               imageGalleryPanel.ClassListContains("visible");
    }

    private void ClearGalleryThumbnails()
    {
        foreach (Texture2D texture in galleryThumbnailTextures)
        {
            if (texture != null)
                Destroy(texture);
        }
        galleryThumbnailTextures.Clear();
    }

    private void LoadSelectedImage(string path)
    {
        if (selectedImages.Count >= MaxSelectedImages)
        {
            ShowAttachmentStatus(T(
                "You can select up to 4 images.",
                "Bạn có thể chọn tối đa 4 ảnh."
            ));
            return;
        }

        SelectedImageData selected = PrepareSelectedImage(path, path);
        if (selected != null)
        {
            selectedImages.Add(selected);
            UpdateInputState();
        }
    }

    private SelectedImageData PrepareSelectedImage(string path, string uri)
    {
        try
        {
            byte[] sourceBytes = File.ReadAllBytes(path);
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!ImageConversion.LoadImage(source, sourceBytes, false))
            {
                Destroy(source);
                ShowAttachmentStatus(T("Could not read this image.", "Không thể đọc hình ảnh này."));
                return null;
            }

            Texture2D prepared = ResizeImage(source, 1280);
            if (prepared != source)
                Destroy(source);

            byte[] jpegBytes = ImageConversion.EncodeToJPG(prepared, 76);

            // A second reduction protects mobile memory and Edge Function body size.
            // Keep each Base64 item below the Edge Function's 5 MB string
            // limit (roughly 3.75 MB of JPEG bytes before Base64 expansion).
            if (jpegBytes.Length > 3 * 1024 * 1024)
            {
                Texture2D smaller = ResizeImage(prepared, 960);
                if (smaller != prepared)
                    Destroy(prepared);
                prepared = smaller;
                jpegBytes = ImageConversion.EncodeToJPG(prepared, 68);
            }

            return new SelectedImageData
            {
                uri = uri,
                fileName = Path.GetFileName(path),
                base64 = Convert.ToBase64String(jpegBytes),
                mimeType = "image/jpeg",
                texture = prepared
            };
        }
        catch (Exception exception)
        {
            Debug.LogError("[ChatAIPageController] Image load failed: " + exception);
            ShowAttachmentStatus(T("Could not open this image.", "Không thể mở hình ảnh này."));
            return null;
        }
    }

    private static Texture2D ResizeImage(Texture2D source, int maxDimension)
    {
        int largest = Mathf.Max(source.width, source.height);
        if (largest <= maxDimension)
            return source;

        float scale = maxDimension / (float)largest;
        int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
        int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;

        Graphics.Blit(source, target);
        RenderTexture.active = target;

        Texture2D result = new Texture2D(width, height, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply(false, false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        return result;
    }

    private void ClearSelectedImage(bool destroyTexture = true)
    {
        if (destroyTexture)
        {
            foreach (SelectedImageData image in selectedImages)
            {
                if (image.texture != null)
                    Destroy(image.texture);
            }
        }
        selectedImages.Clear();
        selectedImagePreview?.RemoveFromClassList("visible");

        if (selectedImageThumbnail != null)
            selectedImageThumbnail.style.backgroundImage = StyleKeyword.None;
    }

    private void ShowAttachmentStatus(string status)
    {
        if (inputPlaceholder == null)
            return;

        inputPlaceholder.text = status;
        inputPlaceholder.style.display = DisplayStyle.Flex;
        inputPlaceholder.schedule.Execute(() =>
        {
            inputPlaceholder.text = T("Ask AI Assistant...", "Hỏi Trợ lý AI...");
            UpdateInputState();
        }).ExecuteLater(2500);
    }

    private void ScrollToBottom()
    {
        if (messageScrollView == null)
            return;

        messageScrollView.schedule.Execute(() =>
        {
            float low = messageScrollView.verticalScroller.lowValue;
            float high = messageScrollView.verticalScroller.highValue;

            // A short conversation has no real scroll range. Keep it fixed
            // instead of preserving a stale drag/overscroll offset.
            float target = high > low + 1f ? high : low;
            messageScrollView.scrollOffset = new Vector2(0f, target);
        }).ExecuteLater(10);
    }

    private void OnRootGeometryChanged(
        GeometryChangedEvent evt)
    {
        ApplySafeArea();
        ApplyKeyboardInset(force: true);
    }

    private IEnumerator MonitorKeyboard()
    {
        while (enabled)
        {
            ApplyKeyboardInset();
            yield return null;
        }
    }

    private void ApplyKeyboardInset(bool force = false)
    {
        if (safeArea == null || root == null)
            return;

        float keyboardScreenHeight = GetKeyboardScreenHeight();

        if (!force && Mathf.Abs(keyboardScreenHeight - lastKeyboardHeight) < 1f)
            return;

        lastKeyboardHeight = keyboardScreenHeight;

        float screenHeight = Mathf.Max(Screen.height, 1f);
        float panelHeight = root.resolvedStyle.height;
        if (panelHeight <= 0f)
            return;

        float systemBottomInset = Screen.safeArea.yMin / screenHeight * panelHeight;
        float keyboardPanelHeight = keyboardScreenHeight / screenHeight * panelHeight;
        bool keyboardVisible = keyboardPanelHeight > 1f;

        if (keyboardVisible)
            EnsureSelectedImageGalleryVisible();

        safeArea.style.paddingBottom = keyboardVisible
            ? Mathf.Max(systemBottomInset, keyboardPanelHeight)
            : systemBottomInset;

        safeArea.EnableInClassList("keyboard-visible", keyboardVisible);

        // Do not move the composer below an open image tray. The image tray
        // must remain the final element at the bottom of the screen.
        if (imageGalleryPanel != null &&
            imageGalleryPanel.ClassListContains("visible"))
        {
            imageGalleryPanel.BringToFront();
        }
        else if (composerSection != null)
        {
            composerSection.BringToFront();
        }
        else if (messageInputSection != null)
        {
            messageInputSection.BringToFront();
        }

        if (keyboardVisible)
            root.schedule.Execute(ScrollToBottom).ExecuteLater(25);
    }

    private void EnsureSelectedImageGalleryVisible()
    {
        if (selectedImages.Count == 0 || imageGalleryPanel == null)
            return;

        imageGalleryPanel.AddToClassList("visible");
        imageGalleryPanel.BringToFront();
    }

    /// <summary>
    /// Unity returns a zero-sized TouchScreenKeyboard.area on a number of
    /// Android devices/keyboard combinations. In that case, measure the part
    /// of the Android window hidden by the IME so the composer can still be
    /// lifted above the keyboard.
    /// </summary>
    private float GetKeyboardScreenHeight()
    {
#if UNITY_ANDROID || UNITY_IOS
        if (TouchScreenKeyboard.visible)
        {
            float unityKeyboardHeight =
                Mathf.Max(0f, TouchScreenKeyboard.area.height);

            if (unityKeyboardHeight > 1f)
                return unityKeyboardHeight;
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (androidActivity == null)
                CacheAndroidActivity();

            if (androidActivity == null)
                return 0f;

            using AndroidJavaObject window =
                androidActivity.Call<AndroidJavaObject>("getWindow");
            using AndroidJavaObject decorView =
                window.Call<AndroidJavaObject>("getDecorView");
            using AndroidJavaObject visibleFrame =
                new AndroidJavaObject("android.graphics.Rect");

            decorView.Call("getWindowVisibleDisplayFrame", visibleFrame);

            int decorHeight = decorView.Call<int>("getHeight");
            int visibleBottom = visibleFrame.Get<int>("bottom");
            int obscuredHeight = Mathf.Max(0, decorHeight - visibleBottom);

            // Ignore the navigation/gesture bar. An IME occupies a much
            // larger portion of the display (normally at least 15%).
            if (obscuredHeight > Screen.height * 0.15f)
                return obscuredHeight;
        }
        catch (Exception exception)
        {
            if (!androidKeyboardProbeWarningLogged)
            {
                androidKeyboardProbeWarningLogged = true;
                Debug.LogWarning(
                    "[ChatAIPageController] Could not measure the Android " +
                    "keyboard inset: " + exception.Message
                );
            }
        }
#endif

        return 0f;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void CacheAndroidActivity()
    {
        if (androidActivity != null)
            return;

        using AndroidJavaClass unityPlayer =
            new AndroidJavaClass("com.unity3d.player.UnityPlayer");

        androidActivity =
            unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
    }

    /// <summary>
    /// Ask Android to resize Unity's visible window for the software keyboard.
    /// The native visible-window probe above remains as a fallback for devices
    /// whose edge-to-edge mode ignores adjustResize.
    /// </summary>
    private void ConfigureAndroidSoftInput()
    {
        try
        {
            CacheAndroidActivity();

            if (androidActivity == null)
                return;

            androidActivity.Call(
                "runOnUiThread",
                new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using AndroidJavaObject window =
                            androidActivity.Call<AndroidJavaObject>("getWindow");

                        // android.view.WindowManager.LayoutParams
                        //     .SOFT_INPUT_ADJUST_RESIZE
                        window.Call("setSoftInputMode", 0x10);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            "[ChatAIPageController] Could not enable Android " +
                            "adjustResize: " + exception.Message
                        );
                    }
                })
            );
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[ChatAIPageController] Android keyboard setup failed: " +
                exception.Message
            );
        }
    }
#endif

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

        // Keep the original safe-area padding: it determines the existing
        // vertical position of the avatar, title and action buttons.
        float topPadding = Mathf.Max(
            (sh - area.yMax) / sh * ph,
            minimumTopSafePadding,
            18f // Shared fallback on devices without a top inset.
        );
        safeArea.style.paddingTop = topPadding;

        // Paint the otherwise transparent Android status/notch strip white,
        // without adding/removing padding or moving the actual chat header.
        if (topSafeBackground != null)
            topSafeBackground.style.height = topPadding;

        // Keyboard monitoring owns the bottom padding while the keyboard is
        // visible. Without a keyboard, this resolves to the normal safe inset.
        ApplyKeyboardInset(force: true);
    }

    private static string BuildLanguageAwarePrompt(string userText)
    {
        string fallbackLanguage = AppLanguageManager.IsVietnamese
            ? "Vietnamese"
            : "English";

        string normalizedText = userText?.Trim() ?? string.Empty;
        string userSection = string.IsNullOrEmpty(normalizedText)
            ? "The user attached one or more images without a text message. " +
              "Analyze the attached images directly."
            : normalizedText;

        return
            "Language instruction: Reply in the same language as the user's " +
            "latest message. If the message is mixed, only numbers, a name, or " +
            "too short to identify its language, reply in " + fallbackLanguage +
            ", which is the language selected in the app settings. " +
            "Do not mention this language instruction in the answer.\n\n" +
            "User message:\n" + userSection;
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
