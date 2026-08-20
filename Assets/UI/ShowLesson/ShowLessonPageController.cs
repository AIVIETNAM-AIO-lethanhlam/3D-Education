using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
[RequireComponent(typeof(SupabaseRuntimeRestService))]
public class ShowLessonPageController : MonoBehaviour
{
    [Header("Scene navigation")]
    [SerializeField] private string previousSceneName = "ClassDetailScene";
    [SerializeField] private string editLessonSceneName = "CreateLessonScene";
    [SerializeField] private string quizSceneName = "DoExerciseScene";

    [SerializeField] private string interactiveModelSceneName = "InteractiveModelScene";
    [SerializeField] private string arModelsSceneName = "ARScene";

    [SerializeField] private string aiSceneName = "ChatAIScene";

    [Header("3D model storage")]
    [SerializeField, Min(60)] private int modelSignedUrlLifetimeSeconds = 3600;

    [Header("Current user")]
    [SerializeField] private bool showTeacherControls = true;

    private const string HiddenClass = "hidden";

    private UIDocument uiDocument;
    private SupabaseRuntimeRestService restService;
    private VisualElement root;
    private IYouTubePlayerBridge youtubeBridge;

    private Button backButton;
    private Button editLessonButton;
    private Button playVideoButton;
    private Button replayButton;
    private Button volumeButton;
    private Button fullscreenButton;
    private Button lectureSlidesButton;
    private Button exerciseFilesButton;
    private Button launchModelButton;
    private Button vrModeButton;
    private Button arModeButton;
    private Button aiAssistantButton;
    private Button resourceModalClose;

    private Label loadErrorLabel;
    private Label lessonCodeLabel;
    private Label classTitleLabel;
    private Label lessonTitleLabel;
    private Label lessonTypeLabel;
    private Label lessonStatusLabel;
    private Label videoNameLabel;
    private Label videoTimeLabel;
    private Label videoFallbackLabel;
    private Label lessonDescriptionLabel;
    private Label lectureSlidesLabel;
    private Label exerciseFilesLabel;
    private Label quizEmptyLabel;
    private Label resourceModalTitle;
    private Label resourceModalMessage;
    private Label downloadStatusLabel;
    private Label modelPreviewLabel;
    private Label interactiveTitleLabel;
    private Label interactiveDescriptionLabel;

    private VisualElement videoWrapper;
    private VisualElement videoProgressFill;
    private VisualElement objectivesContainer;
    private VisualElement quizContainer;
    private VisualElement interactiveModelCard;
    private VisualElement resourceModalOverlay;
    private ScrollView resourceFileList;

    private readonly List<LessonAssetView> documentAssets = new();
    private readonly List<LessonAssetView> quizAssets = new();
    private readonly List<LessonAssetView> modelAssets = new();

    private LessonView currentLesson;
    private string selectedLessonId;
    private bool isMuted;
    private bool fallbackPlaying;
    private float fallbackCurrentTime;
    private bool isOpeningModelScene;
    private MonoBehaviour nativeWebViewComponent;
    private bool resumeVideoAfterModal;

    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        restService = GetComponent<SupabaseRuntimeRestService>();
        if (uiDocument == null || restService == null)
        {
            Debug.LogError(
                "ShowLessonScene is missing UIDocument or " +
                "SupabaseRuntimeRestService.");
            return;
        }

        root = uiDocument.rootVisualElement;
        QueryElements();
        FindYouTubeBridge();
        FindNativeWebViewComponent();
        RegisterEvents();
        ConfigureRoleUI();
        HideScrollbars();
        StartCoroutine(LoadLessonRoutine());
    }

    private void OnDisable()
    {
        UnregisterEvents();
    }

    private void Update()
    {
        if (youtubeBridge != null && youtubeBridge.IsReady)
        {
            UpdateVideoUI(
                youtubeBridge.CurrentTimeSeconds,
                youtubeBridge.DurationSeconds
            );
            return;
        }

        if (fallbackPlaying)
        {
            fallbackCurrentTime += Time.deltaTime;
            UpdateVideoUI(fallbackCurrentTime, 0f);
        }
    }

    private void QueryElements()
    {
        backButton = root.Q<Button>("back-button");
        editLessonButton = root.Q<Button>("edit-lesson-button");
        playVideoButton = root.Q<Button>("play-video-button");
        replayButton = root.Q<Button>("replay-button");
        volumeButton = root.Q<Button>("volume-button");
        fullscreenButton = root.Q<Button>("fullscreen-button");
        lectureSlidesButton = root.Q<Button>("lecture-slides-button");
        exerciseFilesButton = root.Q<Button>("exercise-files-button");
        launchModelButton = root.Q<Button>("launch-model-button");
        vrModeButton = root.Q<Button>("vr-mode-button");
        arModeButton = root.Q<Button>("ar-mode-button");
        aiAssistantButton = root.Q<Button>("ai-assistant-button");
        resourceModalClose = root.Q<Button>("resource-modal-close");

        loadErrorLabel = root.Q<Label>("load-error-label");
        lessonCodeLabel = root.Q<Label>("lesson-code-label");
        classTitleLabel = root.Q<Label>("class-title-label");
        lessonTitleLabel = root.Q<Label>("lesson-title-label");
        lessonTypeLabel = root.Q<Label>("lesson-type-label");
        lessonStatusLabel = root.Q<Label>("lesson-status-label");
        videoNameLabel = root.Q<Label>("video-name-label");
        videoTimeLabel = root.Q<Label>("video-time-label");
        videoFallbackLabel = root.Q<Label>("video-fallback-label");
        lessonDescriptionLabel = root.Q<Label>("lesson-description-label");
        lectureSlidesLabel = root.Q<Label>("lecture-slides-label");
        exerciseFilesLabel = root.Q<Label>("exercise-files-label");
        quizEmptyLabel = root.Q<Label>("quiz-empty-label");
        resourceModalTitle = root.Q<Label>("resource-modal-title");
        resourceModalMessage = root.Q<Label>("resource-modal-message");
        downloadStatusLabel = root.Q<Label>("download-status-label");
        modelPreviewLabel = root.Q<Label>("model-preview-label");
        interactiveTitleLabel = root.Q<Label>("interactive-title-label");
        interactiveDescriptionLabel = root.Q<Label>("interactive-description-label");

        videoWrapper = root.Q<VisualElement>("video-wrapper");
        videoProgressFill = root.Q<VisualElement>("video-progress-fill");
        objectivesContainer = root.Q<VisualElement>("objectives-container");
        quizContainer = root.Q<VisualElement>("quiz-container");
        interactiveModelCard = root.Q<VisualElement>("interactive-model-card");
        resourceModalOverlay = root.Q<VisualElement>("resource-modal-overlay");
        resourceFileList = root.Q<ScrollView>("resource-file-list");
    }

    private void FindYouTubeBridge()
    {
        MonoBehaviour[] components = GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour component in components)
        {
            if (component is IYouTubePlayerBridge bridge)
            {
                youtubeBridge = bridge;
                break;
            }
        }
    }


    private void FindNativeWebViewComponent()
    {
        nativeWebViewComponent = null;

        MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour component in components)
        {
            if (component == null) continue;

            Type type = component.GetType();
            if (string.Equals(type.Name, "WebViewObject", StringComparison.Ordinal))
            {
                nativeWebViewComponent = component;
                break;
            }
        }

        if (nativeWebViewComponent == null)
        {
            Debug.LogWarning(
                "[ShowLessonPageController] WebViewObject was not found. " +
                "The resource popup can still open, but a native WebView may remain above UI Toolkit."
            );
        }
    }

    private void SetNativeWebViewVisible(bool visible)
    {
        if (nativeWebViewComponent == null)
            FindNativeWebViewComponent();

        if (nativeWebViewComponent == null) return;

        Type type = nativeWebViewComponent.GetType();
        var method = type.GetMethod(
            "SetVisibility",
            new[] { typeof(bool) }
        );

        if (method != null)
        {
            try
            {
                method.Invoke(nativeWebViewComponent, new object[] { visible });
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[ShowLessonPageController] Cannot change WebView visibility: " +
                    exception.Message
                );
            }
            return;
        }

        nativeWebViewComponent.gameObject.SetActive(visible);
    }

    private void RegisterEvents()
    {
        Register(backButton, HandleBackClicked);
        Register(editLessonButton, HandleEditClicked);
        Register(playVideoButton, HandlePlayVideoClicked);
        Register(replayButton, HandleReplayClicked);
        Register(volumeButton, HandleVolumeClicked);
        Register(fullscreenButton, HandleFullscreenClicked);
        Register(lectureSlidesButton, HandleLectureSlidesClicked);
        Register(exerciseFilesButton, HandleExerciseFilesClicked);
        Register(launchModelButton, HandleLaunchModelClicked);
        Register(vrModeButton, HandleVrModeClicked);
        Register(arModeButton, HandleArModeClicked);
        Register(aiAssistantButton, HandleAiAssistantClicked);
        Register(resourceModalClose, CloseResourceModal);
    }

    private void UnregisterEvents()
    {
        Unregister(backButton, HandleBackClicked);
        Unregister(editLessonButton, HandleEditClicked);
        Unregister(playVideoButton, HandlePlayVideoClicked);
        Unregister(replayButton, HandleReplayClicked);
        Unregister(volumeButton, HandleVolumeClicked);
        Unregister(fullscreenButton, HandleFullscreenClicked);
        Unregister(lectureSlidesButton, HandleLectureSlidesClicked);
        Unregister(exerciseFilesButton, HandleExerciseFilesClicked);
        Unregister(launchModelButton, HandleLaunchModelClicked);
        Unregister(vrModeButton, HandleVrModeClicked);
        Unregister(arModeButton, HandleArModeClicked);
        Unregister(aiAssistantButton, HandleAiAssistantClicked);
        Unregister(resourceModalClose, CloseResourceModal);
    }

    private static void Register(Button button, Action action)
    {
        if (button != null) button.clicked += action;
    }

    private static void Unregister(Button button, Action action)
    {
        if (button != null) button.clicked -= action;
    }

    private void ConfigureRoleUI()
    {
        string role = PlayerPrefs.GetString(
            "current_role",
            showTeacherControls ? "teacher" : "student"
        );

        bool isTeacher = string.Equals(role, "teacher", StringComparison.OrdinalIgnoreCase);
        SetVisible(editLessonButton, isTeacher);
    }

    private void HideScrollbars()
    {
        ScrollView lessonScroll = root.Q<ScrollView>("lesson-scroll-view");
        if (lessonScroll != null)
            lessonScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;

        if (resourceFileList != null)
            resourceFileList.verticalScrollerVisibility = ScrollerVisibility.Hidden;
    }

    private IEnumerator LoadLessonRoutine()
    {
        ClearError();
        selectedLessonId = PlayerPrefs.GetString("selected_lesson_id", string.Empty);

        if (!Guid.TryParse(selectedLessonId, out _))
        {
            ShowError("selected_lesson_id is missing or invalid. Open this scene from ClassDetailScene.");
            yield break;
        }

        string encodedLessonId = UnityWebRequest.EscapeURL(selectedLessonId);
        string response = null;
        string error = null;

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/lessons" +
            "?select=id,chapter_id,teacher_id,title,description,youtube_url,has_video,status,created_at,updated_at" +
            $"&id=eq.{encodedLessonId}&limit=1",
            null,
            null,
            value => response = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            ShowError(error);
            yield break;
        }

        LessonViewList lessonWrapper = ParseList<LessonViewList>(response);
        if (lessonWrapper?.items == null || lessonWrapper.items.Length == 0)
        {
            ShowError("The selected lesson could not be found.");
            yield break;
        }

        currentLesson = lessonWrapper.items[0];
        RenderLessonMainInformation();

        yield return LoadObjectivesRoutine(encodedLessonId);
        yield return LoadAssetsRoutine(encodedLessonId);

        RenderResourceButtons();
        RenderQuizzes();
        RenderModelCard();
        InitializeVideo();
    }

    private IEnumerator LoadObjectivesRoutine(string encodedLessonId)
    {
        string response = null;
        string error = null;

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/lesson_objectives" +
            "?select=id,lesson_id,objective_text,objective_order" +
            $"&lesson_id=eq.{encodedLessonId}&order=objective_order.asc",
            null,
            null,
            value => response = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            ShowError(error);
            yield break;
        }

        LessonObjectiveViewList wrapper = ParseList<LessonObjectiveViewList>(response);
        RenderObjectives(wrapper?.items);
    }

    private IEnumerator LoadAssetsRoutine(string encodedLessonId)
    {
        string response = null;
        string error = null;

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/lesson_assets" +
            "?select=id,lesson_id,asset_type,file_name,storage_bucket,storage_path,mime_type,file_extension,file_size_bytes,display_order" +
            $"&lesson_id=eq.{encodedLessonId}&order=display_order.asc,created_at.asc",
            null,
            null,
            value => response = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            ShowError(error);
            yield break;
        }

        LessonAssetViewList wrapper = ParseList<LessonAssetViewList>(response);
        documentAssets.Clear();
        quizAssets.Clear();
        modelAssets.Clear();

        if (wrapper?.items == null) yield break;

        foreach (LessonAssetView asset in wrapper.items)
        {
            if (asset == null) continue;

            switch ((asset.asset_type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "document":
                    documentAssets.Add(asset);
                    break;
                case "quiz_pdf":
                    quizAssets.Add(asset);
                    break;
                case "model_3d":
                    modelAssets.Add(asset);
                    break;
            }
        }
    }

    private void RenderLessonMainInformation()
    {
        string classTitle = PlayerPrefs.GetString("selected_class_title", "Class Lesson");
        int chapterOrder = PlayerPrefs.GetInt("selected_chapter_order", 0);

        if (lessonCodeLabel != null)
            lessonCodeLabel.text = chapterOrder > 0 ? $"CHAPTER {chapterOrder} · LESSON" : "LESSON";

        if (classTitleLabel != null)
            classTitleLabel.text = classTitle;

        if (lessonTitleLabel != null)
            lessonTitleLabel.text = string.IsNullOrWhiteSpace(currentLesson.title) ? "Untitled Lesson" : currentLesson.title;

        if (videoNameLabel != null)
            videoNameLabel.text = lessonTitleLabel?.text ?? "Video Lesson";

        if (lessonDescriptionLabel != null)
            lessonDescriptionLabel.text = string.IsNullOrWhiteSpace(currentLesson.description)
                ? "No lesson description has been added yet."
                : currentLesson.description;

        if (lessonStatusLabel != null)
            lessonStatusLabel.text = ToDisplayStatus(currentLesson.status);

        if (lessonTypeLabel != null)
            lessonTypeLabel.text = currentLesson.has_video ? "Video Lesson" : "Lesson";
    }

    private void RenderObjectives(LessonObjectiveView[] objectives)
    {
        objectivesContainer?.Clear();

        if (objectivesContainer == null) return;

        if (objectives == null || objectives.Length == 0)
        {
            Label empty = new("No learning objectives have been added yet.");
            empty.AddToClassList("empty-section-label");
            objectivesContainer.Add(empty);
            return;
        }

        for (int i = 0; i < objectives.Length; i++)
        {
            LessonObjectiveView objective = objectives[i];
            VisualElement row = new();
            row.AddToClassList("objective-row");

            Label number = new((i + 1).ToString());
            number.AddToClassList("objective-number");

            Label text = new(objective.objective_text ?? string.Empty);
            text.AddToClassList("objective-text");

            row.Add(number);
            row.Add(text);
            objectivesContainer.Add(row);
        }
    }

    private void RenderResourceButtons()
    {
        SetVisible(lectureSlidesButton, documentAssets.Count > 0);
        SetVisible(exerciseFilesButton, quizAssets.Count > 0);

        if (lectureSlidesLabel != null)
            lectureSlidesLabel.text = $"Lecture Slides ({documentAssets.Count})";

        if (exerciseFilesLabel != null)
            exerciseFilesLabel.text = $"Exercises ({quizAssets.Count})";
    }

    private void RenderQuizzes()
    {
        quizContainer?.Clear();

        bool hasQuizzes = quizAssets.Count > 0;
        SetVisible(quizContainer, hasQuizzes);
        SetVisible(quizEmptyLabel, !hasQuizzes);

        if (quizContainer == null || !hasQuizzes) return;

        for (int i = 0; i < quizAssets.Count; i++)
        {
            LessonAssetView asset = quizAssets[i];
            Button row = new();
            row.AddToClassList("quiz-row");

            Label number = new((i + 1).ToString("00"));
            number.AddToClassList("quiz-number");
            number.AddToClassList("quiz-number-inactive");

            VisualElement info = new();
            info.AddToClassList("quiz-information");

            Label title = new($"Quiz {i + 1:00}");
            title.AddToClassList("quiz-title");

            Label subtitle = new(string.IsNullOrWhiteSpace(asset.file_name) ? "Exercise PDF" : asset.file_name);
            subtitle.AddToClassList("quiz-subtitle");

            VisualElement status = new();
            status.AddToClassList("quiz-status");
            status.AddToClassList("quiz-status-not-attempted");

            VisualElement dot = new();
            dot.AddToClassList("quiz-status-dot");
            dot.AddToClassList("quiz-status-dot-muted");

            Label statusText = new("Not attempted");
            statusText.AddToClassList("quiz-status-text");

            VisualElement arrow = new();
            arrow.AddToClassList("quiz-arrow");
            arrow.AddToClassList("icon-chevron-right");

            info.Add(title);
            info.Add(subtitle);
            status.Add(dot);
            status.Add(statusText);
            row.Add(number);
            row.Add(info);
            row.Add(status);
            row.Add(arrow);

            LessonAssetView captured = asset;
            int capturedIndex = i;
            row.clicked += () => OpenQuiz(captured, capturedIndex);
            quizContainer.Add(row);

            if (i < quizAssets.Count - 1)
            {
                VisualElement divider = new();
                divider.AddToClassList("quiz-divider");
                quizContainer.Add(divider);
            }
        }
    }

    private void RenderModelCard()
    {
        bool hasModel = modelAssets.Count > 0;
        SetVisible(interactiveModelCard, hasModel);
        if (!hasModel) return;

        string modelName = string.IsNullOrWhiteSpace(modelAssets[0].file_name)
            ? "3D Model"
            : Path.GetFileNameWithoutExtension(modelAssets[0].file_name);

        if (modelPreviewLabel != null) modelPreviewLabel.text = modelName;
        if (interactiveTitleLabel != null) interactiveTitleLabel.text = modelName;
        if (interactiveDescriptionLabel != null)
            interactiveDescriptionLabel.text = $"Interactive 3D model for {lessonTitleLabel?.text ?? "this lesson"}.";
    }

    private void InitializeVideo()
    {
        bool hasYoutube = !string.IsNullOrWhiteSpace(currentLesson?.youtube_url);
        SetVisible(videoWrapper, hasYoutube);
        if (!hasYoutube) return;

        fallbackCurrentTime = 0f;
        UpdateVideoUI(0f, 0f);

        if (youtubeBridge != null)
        {
            youtubeBridge.Load(currentLesson.youtube_url);
            if (videoFallbackLabel != null)
                videoFallbackLabel.text = "Loading YouTube video inside the app...";
        }
        else if (videoFallbackLabel != null)
        {
            videoFallbackLabel.text =
                "Embedded player is not configured. Add UnityWebViewYouTubeBridge to the same GameObject as this controller.";
        }
    }

    private void HandlePlayVideoClicked()
    {
        if (currentLesson == null || string.IsNullOrWhiteSpace(currentLesson.youtube_url))
            return;

        if (youtubeBridge == null)
        {
            Debug.LogError(
                "[ShowLessonPageController] UnityWebViewYouTubeBridge is missing. " +
                "Add it to the same GameObject as UIDocument and ShowLessonPageController."
            );

            if (videoFallbackLabel != null)
                videoFallbackLabel.text = "YouTube WebView is missing from this scene.";

            return;
        }

        if (youtubeBridge.IsReady && youtubeBridge.IsPlaying)
            youtubeBridge.Pause();
        else
            youtubeBridge.Play();
    }

    private void HandleReplayClicked()
    {
        fallbackCurrentTime = 0f;
        if (youtubeBridge != null)
            youtubeBridge.Replay();
        else
            UpdateVideoUI(0f, 0f);
    }

    private void HandleVolumeClicked()
    {
        isMuted = !isMuted;
        if (youtubeBridge != null)
            youtubeBridge.SetMuted(isMuted);
        else
            AudioListener.volume = isMuted ? 0f : 1f;
    }

    private void HandleFullscreenClicked()
    {
        if (youtubeBridge != null)
            youtubeBridge.SetFullscreen(true);
        else
            Screen.fullScreen = !Screen.fullScreen;
    }

    private void UpdateVideoUI(float currentSeconds, float durationSeconds)
    {
        float normalized = durationSeconds > 0f
            ? Mathf.Clamp01(currentSeconds / durationSeconds)
            : 0f;

        if (videoProgressFill != null)
            videoProgressFill.style.width = Length.Percent(normalized * 100f);

        if (videoTimeLabel != null)
        {
            string durationText = durationSeconds > 0f ? FormatTime(durationSeconds) : "--:--";
            videoTimeLabel.text = $"{FormatTime(currentSeconds)} / {durationText}";
        }
    }

    private void OpenResourceModal(string title, List<LessonAssetView> assets)
    {
        if (resourceFileList == null || resourceModalOverlay == null) return;

        resumeVideoAfterModal = youtubeBridge != null && youtubeBridge.IsReady && youtubeBridge.IsPlaying;
        if (resumeVideoAfterModal)
            youtubeBridge.Pause();

        // Native Android WebView is rendered above UI Toolkit. Hide it while the popup is open.
        SetNativeWebViewVisible(false);

        resourceFileList.Clear();
        if (resourceModalTitle != null) resourceModalTitle.text = title;
        if (resourceModalMessage != null)
            resourceModalMessage.text = assets.Count == 0
                ? "No files are available."
                : "Choose a file and tap Download.";
        if (downloadStatusLabel != null) downloadStatusLabel.text = string.Empty;

        foreach (LessonAssetView asset in assets)
            resourceFileList.Add(CreateResourceFileRow(asset));

        SetVisible(resourceModalOverlay, true);
        resourceModalOverlay.BringToFront();
        resourceModalOverlay.pickingMode = PickingMode.Position;
    }

    private VisualElement CreateResourceFileRow(LessonAssetView asset)
    {
        VisualElement row = new();
        row.AddToClassList("resource-file-row");

        VisualElement info = new();
        info.AddToClassList("resource-file-info");

        Label name = new(string.IsNullOrWhiteSpace(asset.file_name) ? "PDF file" : asset.file_name);
        name.AddToClassList("resource-file-name");

        Label size = new(FormatFileSize(asset.file_size_bytes));
        size.AddToClassList("resource-file-size");

        Button download = new();
        download.text = "Download";
        download.AddToClassList("resource-download-button");
        download.clicked += () => StartCoroutine(DownloadAssetRoutine(asset, download));

        info.Add(name);
        info.Add(size);
        row.Add(info);
        row.Add(download);
        return row;
    }

    private IEnumerator DownloadAssetRoutine(LessonAssetView asset, Button button)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.storage_bucket) || string.IsNullOrWhiteSpace(asset.storage_path))
        {
            SetDownloadStatus("This file has no valid storage path.");
            yield break;
        }

        button?.SetEnabled(false);
        SetDownloadStatus($"Downloading {asset.file_name}...");

        string encodedBucket = UnityWebRequest.EscapeURL(asset.storage_bucket);
        string encodedPath = EncodeStoragePath(asset.storage_path);
        string url = $"{restService.ProjectUrl}/storage/v1/object/authenticated/{encodedBucket}/{encodedPath}";

        using UnityWebRequest request = UnityWebRequest.Get(url);
        restService.ApplyAuthHeaders(request);
        yield return request.SendWebRequest();

        button?.SetEnabled(true);

        if (request.result != UnityWebRequest.Result.Success)
        {
            SetDownloadStatus($"Download failed: {request.error}");
            yield break;
        }

        string safeName = MakeSafeFileName(
            string.IsNullOrWhiteSpace(asset.file_name)
                ? $"lesson-file-{asset.id}.pdf"
                : asset.file_name
        );

        string folder = Path.Combine(Application.persistentDataPath, "LessonDownloads");
        string destination = Path.Combine(folder, safeName);

        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(destination, request.downloadHandler.data);
            SetDownloadStatus($"Saved to: {destination}");
            Debug.Log($"Downloaded lesson file to {destination}");
        }
        catch (Exception exception)
        {
            SetDownloadStatus($"Cannot save file: {exception.Message}");
        }
    }

    private void OpenQuiz(LessonAssetView asset, int index)
    {
        PlayerPrefs.SetString("selected_quiz_id", asset.id ?? string.Empty);
        PlayerPrefs.SetString("selected_quiz_asset_id", asset.id ?? string.Empty);
        PlayerPrefs.SetString("selected_quiz_file_name", asset.file_name ?? string.Empty);
        PlayerPrefs.SetString("selected_quiz_bucket", asset.storage_bucket ?? string.Empty);
        PlayerPrefs.SetString("selected_quiz_storage_path", asset.storage_path ?? string.Empty);
        PlayerPrefs.SetInt("selected_quiz_order", index + 1);
        PlayerPrefs.Save();
        LoadSceneSafely(quizSceneName);
    }

    private void HandleLectureSlidesClicked()
    {
        OpenResourceModal("Lecture Slides", documentAssets);
    }

    private void HandleExerciseFilesClicked()
    {
        OpenResourceModal("Exercises", quizAssets);
    }

    private void HandleLaunchModelClicked()
    {
        BeginOpenModel("3d");
    }

    private void HandleVrModeClicked()
    {
        BeginOpenModel("vr");
    }

    private void HandleArModeClicked()
    {
        BeginOpenModel("ar");
    }

    private void HandleAiAssistantClicked()
    {
        // The AI chat scene in this project is named "ChatAIScene".
        // Use the real scene name directly so an old serialized Inspector value
        // such as "AIScene" cannot prevent navigation.
        const string chatAiSceneName = "ChatAIScene";

        PlayerPrefs.SetString("previous_scene", "ShowLessonScene");
        PlayerPrefs.Save();

        Debug.Log($"[ShowLessonPageController] Opening AI chat scene: {chatAiSceneName}");
        LoadSceneSafely(chatAiSceneName);
    }

    private void BeginOpenModel(string mode)
    {
        if (isOpeningModelScene)
            return;

        StartCoroutine(OpenModelRoutine(mode));
    }

    private IEnumerator OpenModelRoutine(string mode)
    {
        if (modelAssets.Count == 0)
        {
            ShowError("No 3D model is attached to this lesson.");
            yield break;
        }

        LessonAssetView model = modelAssets[0];

        if (model == null)
        {
            ShowError("The selected 3D model record is invalid.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(model.storage_bucket) ||
            string.IsNullOrWhiteSpace(model.storage_path))
        {
            ShowError(
                "The 3D model does not have a valid Storage bucket or path.");
            yield break;
        }

        string normalizedMode =
            string.IsNullOrWhiteSpace(mode)
                ? "3d"
                : mode.Trim().ToLowerInvariant();

        string destinationScene =
            normalizedMode == "ar"
                ? arModelsSceneName
                : interactiveModelSceneName;

        if (!CanLoadScene(destinationScene))
            yield break;

        isOpeningModelScene = true;
        SetModelButtonsEnabled(false);
        ClearError();

        string signedUrl = null;
        string signedUrlError = null;

        yield return CreateSignedModelUrlRoutine(
            model.storage_bucket,
            model.storage_path,
            Mathf.Max(60, modelSignedUrlLifetimeSeconds),
            value => signedUrl = value,
            message => signedUrlError = message
        );

        if (!string.IsNullOrWhiteSpace(signedUrlError))
        {
            isOpeningModelScene = false;
            SetModelButtonsEnabled(true);

            ShowError(
                "Cannot open the 3D model: " +
                signedUrlError);

            yield break;
        }

        if (string.IsNullOrWhiteSpace(signedUrl))
        {
            isOpeningModelScene = false;
            SetModelButtonsEnabled(true);

            ShowError(
                "Supabase returned an empty URL for the 3D model.");

            yield break;
        }

        string modelName =
            string.IsNullOrWhiteSpace(model.file_name)
                ? "3D Model"
                : Path.GetFileNameWithoutExtension(
                    model.file_name);

        PlayerPrefs.SetString(
            "interactive_mode",
            normalizedMode);

        PlayerPrefs.SetString(
            "selected_model_asset_id",
            model.id ?? string.Empty);

        PlayerPrefs.SetString(
            "selected_model_bucket",
            model.storage_bucket ?? string.Empty);

        PlayerPrefs.SetString(
            "selected_model_storage_path",
            model.storage_path ?? string.Empty);

        PlayerPrefs.SetString(
            "selected_model_file_name",
            model.file_name ?? string.Empty);

        // RuntimeGlbLoader/ARModelSceneController read these keys.
        PlayerPrefs.SetString(
            "selected_model_url",
            signedUrl);

        PlayerPrefs.SetString(
            "selected_model_name",
            modelName);

        PlayerPrefs.SetString(
            "selected_model_lesson_id",
            selectedLessonId ?? string.Empty);

        string currentRole = PlayerPrefs.GetString("current_role", showTeacherControls ? "teacher" : "student");
        PlayerPrefs.SetString("current_role", currentRole);

        // Giữ nguyên các thông tin user hiện có (nếu có)
        if (PlayerPrefs.HasKey("user_id")) PlayerPrefs.SetString("user_id", PlayerPrefs.GetString("user_id"));
        if (PlayerPrefs.HasKey("user_email")) PlayerPrefs.SetString("user_email", PlayerPrefs.GetString("user_email"));
        if (PlayerPrefs.HasKey("user_name")) PlayerPrefs.SetString("user_name", PlayerPrefs.GetString("user_name"));

        PlayerPrefs.SetString(
            "previous_scene",
            "ShowLessonScene");

        PlayerPrefs.Save();

        Debug.Log(
            "[ShowLessonPageController] Opening model scene." +
            $"\nMode: {normalizedMode}" +
            $"\nScene: {destinationScene}" +
            $"\nAsset ID: {model.id}" +
            $"\nBucket: {model.storage_bucket}" +
            $"\nPath: {model.storage_path}" +
            $"\nFile: {model.file_name}");

        SceneManager.LoadScene(destinationScene);
    }

    private IEnumerator CreateSignedModelUrlRoutine(
        string bucket,
        string storagePath,
        int expiresInSeconds,
        Action<string> onSuccess,
        Action<string> onError)
    {
        if (restService == null)
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        if (!restService.IsConfigured(out string configError))
        {
            onError?.Invoke(configError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(bucket) ||
            string.IsNullOrWhiteSpace(storagePath))
        {
            onError?.Invoke("The model Storage bucket or path is empty.");
            yield break;
        }

        string encodedBucket =
            UnityWebRequest.EscapeURL(bucket.Trim());

        string encodedPath =
            EncodeStoragePath(storagePath.Trim());

        string url =
            $"{restService.ProjectUrl.TrimEnd('/')}/storage/v1/object/sign/" +
            $"{encodedBucket}/{encodedPath}";

        SignedStorageUrlRequest payload = new()
        {
            expiresIn = Mathf.Max(60, expiresInSeconds)
        };

        byte[] body =
            Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));

        using UnityWebRequest request =
            new(url, UnityWebRequest.kHttpVerbPOST);

        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = SupabaseConfig.RequestTimeoutSeconds;

        restService.ApplyAuthHeaders(request);
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");

        yield return request.SendWebRequest();

        string responseText =
            request.downloadHandler?.text ?? string.Empty;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string message =
                !string.IsNullOrWhiteSpace(responseText)
                    ? responseText
                    : request.error;

            onError?.Invoke(
                $"Cannot create signed URL ({request.responseCode}): " +
                message);

            yield break;
        }

        SignedStorageUrlResponse response = null;

        try
        {
            response =
                JsonUtility.FromJson<SignedStorageUrlResponse>(responseText);
        }
        catch (Exception exception)
        {
            onError?.Invoke(
                "Cannot parse Supabase signed URL response: " +
                exception.Message);

            yield break;
        }

        string signedPath = response?.signedURL;

        if (string.IsNullOrWhiteSpace(signedPath))
            signedPath = response?.signed_url;

        if (string.IsNullOrWhiteSpace(signedPath))
        {
            onError?.Invoke(
                "Supabase returned an empty signed URL. Response: " +
                responseText);

            yield break;
        }

        string absoluteUrl;

        if (Uri.TryCreate(signedPath, UriKind.Absolute, out Uri parsedUrl))
        {
            absoluteUrl = parsedUrl.ToString();
        }
        else
        {
            string normalizedPath =
                signedPath.StartsWith("/")
                    ? signedPath
                    : "/" + signedPath;

            absoluteUrl =
                restService.ProjectUrl.TrimEnd('/') + normalizedPath;
        }

        onSuccess?.Invoke(absoluteUrl);
    }

    private void SetModelButtonsEnabled(bool enabled)
    {
        launchModelButton?.SetEnabled(enabled);
        vrModeButton?.SetEnabled(enabled);
        arModeButton?.SetEnabled(enabled);
    }

    private static bool CanLoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError(
                "[ShowLessonPageController] Destination scene name is empty.");
            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"Scene '{sceneName}' is not included in Build Profiles.");
            return false;
        }

        return true;
    }

    private void HandleBackClicked()
    {
        string previous = PlayerPrefs.GetString("previous_scene", previousSceneName);
        LoadSceneSafely(string.IsNullOrWhiteSpace(previous) ? previousSceneName : previous);
    }

    private void HandleEditClicked()
    {
        PlayerPrefs.SetString("lesson_editor_mode", "update");
        PlayerPrefs.SetString("selected_lesson_id", selectedLessonId);
        if (currentLesson != null)
            PlayerPrefs.SetString("selected_chapter_id", currentLesson.chapter_id ?? string.Empty);
        PlayerPrefs.SetString("previous_scene", "ShowLessonScene");
        PlayerPrefs.Save();
        LoadSceneSafely(editLessonSceneName);
    }

    private void CloseResourceModal()
    {
        SetVisible(resourceModalOverlay, false);
        SetNativeWebViewVisible(true);

        if (resumeVideoAfterModal && youtubeBridge != null && youtubeBridge.IsReady)
            youtubeBridge.Play();

        resumeVideoAfterModal = false;
    }

    private void ShowError(string message)
    {
        if (loadErrorLabel == null) return;
        loadErrorLabel.text = message;
        SetVisible(loadErrorLabel, true);
        Debug.LogError("[ShowLessonPageController] " + message);
    }

    private void ClearError()
    {
        if (loadErrorLabel == null) return;
        loadErrorLabel.text = string.Empty;
        SetVisible(loadErrorLabel, false);
    }

    private void SetDownloadStatus(string message)
    {
        if (downloadStatusLabel != null)
            downloadStatusLabel.text = message;
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        if (element == null) return;
        element.EnableInClassList(HiddenClass, !visible);
    }

    private static T ParseList<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonUtility.FromJson<T>($"{{\"items\":{json}}}");
        }
        catch (Exception exception)
        {
            Debug.LogError("Cannot parse Supabase response: " + exception.Message);
            return null;
        }
    }

    private static string ToDisplayStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "Published";
        string value = status.Replace("_", " ").Trim();
        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    private static string FormatTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int remainingSeconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours:00}:{minutes:00}:{remainingSeconds:00}"
            : $"{minutes:00}:{remainingSeconds:00}";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "PDF document";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024f:0.0} KB";
        return $"{bytes / (1024f * 1024f):0.0} MB";
    }

    private static string EncodeStoragePath(string path)
    {
        string[] parts = path.Split('/');
        for (int i = 0; i < parts.Length; i++)
            parts[i] = UnityWebRequest.EscapeURL(parts[i]);
        return string.Join("/", parts);
    }

    private static string MakeSafeFileName(string fileName)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');
        return fileName;
    }

    private static void LoadSceneSafely(string sceneName)
    {
        if (!CanLoadScene(sceneName))
            return;

        SceneManager.LoadScene(sceneName);
    }
    [Serializable]
    private class SignedStorageUrlRequest
    {
        public int expiresIn;
    }

    [Serializable]
    private class SignedStorageUrlResponse
    {
        public string signedURL;
        public string signed_url;
    }
}

[Serializable]
public class LessonView
{
    public string id;
    public string chapter_id;
    public string teacher_id;
    public string title;
    public string description;
    public string youtube_url;
    public bool has_video;
    public string status;
    public string created_at;
    public string updated_at;
}

[Serializable]
public class LessonViewList
{
    public LessonView[] items;
}

[Serializable]
public class LessonObjectiveView
{
    public string id;
    public string lesson_id;
    public string objective_text;
    public int objective_order;
}

[Serializable]
public class LessonObjectiveViewList
{
    public LessonObjectiveView[] items;
}

[Serializable]
public class LessonAssetView
{
    public string id;
    public string lesson_id;
    public string asset_type;
    public string file_name;
    public string storage_bucket;
    public string storage_path;
    public string mime_type;
    public string file_extension;
    public long file_size_bytes;
    public int display_order;
}

[Serializable]
public class LessonAssetViewList
{
    public LessonAssetView[] items;
}