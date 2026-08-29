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
    // Quiz scene is fixed in code so an old serialized Inspector value
    // (for example "DoExerciseScene") cannot override it.
    private const string QuizSceneName = "StartQuizScene";

    [SerializeField] private string Mode3DSceneName = "Mode3DScene";
    [SerializeField] private string vrSceneName = "VRClassroomScene";
    [SerializeField] private string arModelsSceneName = "ARScene";

    [SerializeField] private string aiSceneName = "ChatAIScene";

    [Header("3D model storage")]
    [SerializeField, Min(60)] private int modelSignedUrlLifetimeSeconds = 3600;

    [Header("PDF / external file storage")]
    [Tooltip("Public/custom-domain base URL for Cloudflare R2, e.g. https://files.example.com or https://pub-xxxx.r2.dev. If lesson_assets.storage_path only stores an R2 object key, this field is required for a public R2 bucket.")]
    [SerializeField] private string r2PublicBaseUrl = string.Empty;

    [Header("Lesson Models Public R2")]
    [Tooltip("Public Development URL/custom domain of the lesson-models bucket. " +
             "This is intentionally separate from old serialized R2/CDN fields so newly uploaded models " +
             "do not keep using a stale r2.dev domain.")]
    [SerializeField] private string lessonModelsPublicBaseUrl =
        "https://pub-d18240b07b8944fabf89fcb8663dcf5f.r2.dev";

    [Tooltip("Optional Supabase Edge Function that returns a fresh signed R2 download URL for private objects. Leave empty when your R2 bucket is public. Expected JSON response contains url or signed_url.")]
    [SerializeField] private string r2SignedUrlFunctionName = string.Empty;

    [Header("Current user")]
    [SerializeField] private bool showTeacherControls = true;

    private const string HiddenClass = "hidden";

    // Navigation keys:
    // previous_scene is still used by child scenes (AR/3D/AI) to return to ShowLessonScene.
    // show_lesson_parent_scene preserves the real parent of ShowLessonScene
    // (normally ClassDetailScene) while a child scene is temporarily open.
    private const string PreviousSceneKey = "previous_scene";
    private const string ShowLessonParentSceneKey = "show_lesson_parent_scene";

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
    private Button pdfViewerClose;
    private Button pdfViewerDownload;

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
    private Label pdfViewerFileName;
    private Label pdfViewerStatus;

    private VisualElement videoWrapper;
    private VisualElement videoSection;
    private ScrollView lessonScrollView;
    private VisualElement videoProgressFill;
    private VisualElement objectivesContainer;
    private VisualElement quizContainer;
    private VisualElement interactiveModelCard;
    private VisualElement resourceModalOverlay;
    private ScrollView resourceFileList;
    private VisualElement pdfViewerOverlay;
    private VisualElement pdfViewerHeader;
    private VisualElement pdfViewerContent;

    private readonly List<LessonAssetView> documentAssets = new();
    private readonly List<LessonAssetView> quizAssets = new();
    private readonly List<LessonAssetView> modelAssets = new();
    private readonly List<ClassModelSource> classModelSources = new();

    private LessonView currentLesson;
    private string selectedLessonId;
    private bool isMuted;
    private bool fallbackPlaying;
    private float fallbackCurrentTime;
    private bool isOpeningModelScene;
    private MonoBehaviour nativeWebViewComponent;
    private bool resumeVideoAfterModal;
    private LessonAssetView currentPdfAsset;
    private bool pdfViewerOpen;
    private bool resumeVideoAfterPdf;
    private bool nativeVideoWebViewVisible;
    private bool nativeVideoWebViewVisibilityKnown;

    private void OnEnable()
    {
        // If we just came back from AR/3D, previous_scene is still "ShowLessonScene"
        // because the child scene needed that value for its own Back button.
        // Restore the real parent (for example ClassDetailScene) before this page's
        // header Back button can be used.
        RestoreParentSceneAfterChildReturn();

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
        // unity-webview is a native view, so keep it synchronized with UI Toolkit.
        // While the PDF viewer is open, the WebView must fill the entire PDF body.
        if (pdfViewerOpen)
            UpdateNativePdfWebViewRect();
        else
            UpdateNativeVideoWebViewRect();

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
        pdfViewerClose = root.Q<Button>("pdf-viewer-close");
        pdfViewerDownload = root.Q<Button>("pdf-viewer-download");

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
        pdfViewerFileName = root.Q<Label>("pdf-viewer-file-name");
        pdfViewerStatus = root.Q<Label>("pdf-viewer-status");

        videoWrapper = root.Q<VisualElement>("video-wrapper");
        videoSection = root.Q<VisualElement>("video-section");
        lessonScrollView = root.Q<ScrollView>("lesson-scroll-view");
        videoProgressFill = root.Q<VisualElement>("video-progress-fill");
        objectivesContainer = root.Q<VisualElement>("objectives-container");
        quizContainer = root.Q<VisualElement>("quiz-container");
        interactiveModelCard = root.Q<VisualElement>("interactive-model-card");
        resourceModalOverlay = root.Q<VisualElement>("resource-modal-overlay");
        resourceFileList = root.Q<ScrollView>("resource-file-list");
        pdfViewerOverlay = root.Q<VisualElement>("pdf-viewer-overlay");
        pdfViewerHeader = root.Q<VisualElement>("pdf-viewer-header");
        pdfViewerContent = root.Q<VisualElement>("pdf-viewer-content");
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

        // First check this page hierarchy.
        MonoBehaviour[] localComponents = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour component in localComponents)
        {
            if (component == null) continue;
            if (string.Equals(component.GetType().Name, "WebViewObject", StringComparison.Ordinal))
            {
                nativeWebViewComponent = component;
                return;
            }
        }

        // unity-webview / the YouTube bridge may create WebViewObject on a separate
        // runtime GameObject. Search the whole scene as a fallback.
#if UNITY_2023_1_OR_NEWER
        MonoBehaviour[] allComponents = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        MonoBehaviour[] allComponents = FindObjectsOfType<MonoBehaviour>(true);
#endif
        foreach (MonoBehaviour component in allComponents)
        {
            if (component == null) continue;
            if (string.Equals(component.GetType().Name, "WebViewObject", StringComparison.Ordinal))
            {
                nativeWebViewComponent = component;
                return;
            }
        }

        Debug.LogWarning(
            "[ShowLessonPageController] WebViewObject was not found yet. " +
            "It may be created later by UnityWebViewYouTubeBridge; the controller will search again when a PDF is opened.");
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


    /// <summary>
    /// unity-webview is rendered as a native Android view above Unity. Because it is
    /// not a child of UI Toolkit's ScrollView, it would otherwise stay fixed on the
    /// screen while the lesson content scrolls. This method recalculates the WebView
    /// margins from the current worldBound of the video-section every frame.
    /// </summary>
    private void UpdateNativeVideoWebViewRect()
    {
        if (nativeWebViewComponent == null || videoSection == null || root == null)
            return;

        // PDF viewer temporarily reuses the same native WebView and manages its own margins.
        if (pdfViewerOpen)
            return;

        // Do not let the native WebView cover the resource modal.
        bool resourceModalOpen =
            resourceModalOverlay != null &&
            !resourceModalOverlay.ClassListContains(HiddenClass);

        bool hasVideo =
            currentLesson != null &&
            !string.IsNullOrWhiteSpace(currentLesson.youtube_url) &&
            videoWrapper != null &&
            !videoWrapper.ClassListContains(HiddenClass);

        if (resourceModalOpen || !hasVideo)
        {
            SetNativeVideoWebViewVisibility(false);
            return;
        }

        Rect bounds = videoSection.worldBound;
        if (bounds.width <= 1f || bounds.height <= 1f)
        {
            SetNativeVideoWebViewVisibility(false);
            return;
        }

        float panelScale = 1f;
        if (root.panel != null)
            panelScale = Mathf.Max(0.01f, root.panel.scaledPixelsPerPoint);

        int left = Mathf.RoundToInt(bounds.xMin * panelScale);
        int top = Mathf.RoundToInt(bounds.yMin * panelScale);
        int right = Mathf.RoundToInt(Screen.width - bounds.xMax * panelScale);
        int bottom = Mathf.RoundToInt(Screen.height - bounds.yMax * panelScale);

        // If the video has completely left the visible screen, hide the native view.
        if (right >= Screen.width || left >= Screen.width ||
            bottom >= Screen.height || top >= Screen.height ||
            bounds.xMax <= 0f || bounds.yMax <= 0f)
        {
            SetNativeVideoWebViewVisibility(false);
            return;
        }

        // Clamp partially visible edges. This makes the native player move together
        // with the ScrollView instead of remaining pinned to its original position.
        left = Mathf.Clamp(left, 0, Screen.width);
        top = Mathf.Clamp(top, 0, Screen.height);
        right = Mathf.Clamp(right, 0, Screen.width);
        bottom = Mathf.Clamp(bottom, 0, Screen.height);

        if (!TrySetNativeWebViewMargins(left, top, right, bottom))
            return;

        SetNativeVideoWebViewVisibility(true);
    }

    private void SetNativeVideoWebViewVisibility(bool visible)
    {
        if (nativeVideoWebViewVisibilityKnown && nativeVideoWebViewVisible == visible)
            return;

        SetNativeWebViewVisible(visible);
        nativeVideoWebViewVisible = visible;
        nativeVideoWebViewVisibilityKnown = true;
    }

    private bool TrySetNativeWebViewMargins(int left, int top, int right, int bottom)
    {
        if (nativeWebViewComponent == null)
            return false;

        Type type = nativeWebViewComponent.GetType();

        var fourArgs = type.GetMethod(
            "SetMargins",
            new[] { typeof(int), typeof(int), typeof(int), typeof(int) });

        if (fourArgs != null)
        {
            try
            {
                fourArgs.Invoke(
                    nativeWebViewComponent,
                    new object[] { left, top, right, bottom });
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[ShowLessonPageController] Cannot update YouTube WebView margins: " +
                    exception.Message);
                return false;
            }
        }

        var fiveArgs = type.GetMethod(
            "SetMargins",
            new[]
            {
                typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool)
            });

        if (fiveArgs != null)
        {
            try
            {
                fiveArgs.Invoke(
                    nativeWebViewComponent,
                    new object[] { left, top, right, bottom, false });
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[ShowLessonPageController] Cannot update YouTube WebView margins: " +
                    exception.Message);
                return false;
            }
        }

        return false;
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
        Register(pdfViewerClose, ClosePdfViewer);
        Register(pdfViewerDownload, HandlePdfViewerDownloadClicked);
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
        Unregister(pdfViewerClose, ClosePdfViewer);
        Unregister(pdfViewerDownload, HandlePdfViewerDownloadClicked);
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
        ScrollView lessonScroll = lessonScrollView ?? root.Q<ScrollView>("lesson-scroll-view");
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
            "?select=*" +
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
            row.clicked += () => StartCoroutine(OpenQuizRoutine(captured, capturedIndex));
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
        nativeVideoWebViewVisible = false;
        nativeVideoWebViewVisibilityKnown = true;

        resourceFileList.Clear();
        if (resourceModalTitle != null) resourceModalTitle.text = title;
        if (resourceModalMessage != null)
            resourceModalMessage.text = assets.Count == 0
                ? "No files are available."
                : "Tap a file to view it, or tap Download to save it.";
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

        Button open = new();
        open.AddToClassList("resource-file-open-button");
        open.tooltip = "Open PDF";

        VisualElement info = new();
        info.AddToClassList("resource-file-info");

        Label name = new(string.IsNullOrWhiteSpace(asset.file_name) ? "PDF file" : asset.file_name);
        name.AddToClassList("resource-file-name");

        Label size = new(FormatFileSize(asset.file_size_bytes));
        size.AddToClassList("resource-file-size");

        info.Add(name);
        info.Add(size);
        open.Add(info);

        LessonAssetView captured = asset;
        open.clicked += () => BeginOpenPdf(captured);

        Button download = new();
        download.text = "Download";
        download.AddToClassList("resource-download-button");
        download.clicked += () => StartCoroutine(DownloadAssetRoutine(captured, download));

        row.Add(open);
        row.Add(download);
        return row;
    }

    private void BeginOpenPdf(LessonAssetView asset)
    {
        if (asset == null)
        {
            SetDownloadStatus("This PDF record is invalid.");
            return;
        }

        StartCoroutine(OpenPdfRoutine(asset));
    }

    private IEnumerator OpenPdfRoutine(LessonAssetView asset)
    {
        if (asset == null)
        {
            SetDownloadStatus("This PDF record is invalid.");
            yield break;
        }

        currentPdfAsset = asset;

        // Remember whether the embedded lesson video was playing.
        resumeVideoAfterPdf =
            youtubeBridge != null &&
            youtubeBridge.IsReady &&
            youtubeBridge.IsPlaying;

        if (youtubeBridge != null && youtubeBridge.IsReady)
            youtubeBridge.Pause();

        // IMPORTANT: UnityWebViewYouTubeBridge normally recalculates WebView margins
        // every frame for the lesson video. Put the bridge in fullscreen mode while
        // the PDF is open so it stops overriding the PDF reader margins.
        if (youtubeBridge != null)
            youtubeBridge.SetFullscreen(true);

        // Hide the file-list popup while the full-page PDF viewer is active.
        SetVisible(resourceModalOverlay, false);
        resumeVideoAfterModal = false;

        if (pdfViewerFileName != null)
            pdfViewerFileName.text =
                string.IsNullOrWhiteSpace(asset.file_name)
                    ? "PDF file"
                    : asset.file_name;

        if (pdfViewerStatus != null)
            pdfViewerStatus.text = "Loading PDF...";

        SetVisible(pdfViewerOverlay, true);
        pdfViewerOverlay?.BringToFront();

        // lesson_assets may point to:
        //  - an absolute R2/public/signed URL,
        //  - an R2 object key + r2PublicBaseUrl,
        //  - a Supabase Storage bucket/path.
        // Try the valid candidates in that order instead of assuming every PDF
        // lives in Supabase Storage. This also fixes old rows where storage_path
        // accidentally contains the bucket name a second time.
        byte[] pdfBytes = null;
        string pdfError = null;

        yield return FetchAssetBytesRoutine(
            asset,
            value => pdfBytes = value,
            message => pdfError = message);

        if (!string.IsNullOrWhiteSpace(pdfError))
        {
            if (pdfViewerStatus != null)
                pdfViewerStatus.text = pdfError;

            Debug.LogError("[ShowLessonPageController] " + pdfError);
            yield break;
        }

        if (pdfBytes == null || pdfBytes.Length == 0)
        {
            if (pdfViewerStatus != null)
                pdfViewerStatus.text = "The PDF file is empty.";

            yield break;
        }

        if (pdfViewerStatus != null)
            pdfViewerStatus.text = "Preparing pages...";

        string base64Pdf = Convert.ToBase64String(pdfBytes);
        string html = BuildPdfViewerHtml(base64Pdf);

        if (!TryLoadNativeWebViewHtml(html))
        {
            if (pdfViewerStatus != null)
                pdfViewerStatus.text =
                    "PDF WebView is not available. Make sure unity-webview is active in ShowLessonScene.";

            yield break;
        }

        pdfViewerOpen = true;

        // Keep the native WebView below our own toolbar so the X, filename and
        // Download button remain visible.
        ConfigureNativeWebViewForPdf();
        SetNativeWebViewVisible(true);

        if (pdfViewerStatus != null)
            pdfViewerStatus.text = string.Empty;
    }

    private static string BuildPdfViewerHtml(string base64Pdf)
    {
        // Full-body touch-first PDF.js viewer. There are no on-screen zoom controls;
        // Android/iOS users zoom directly with a two-finger pinch gesture.
        return @"<!doctype html>
<html>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1, minimum-scale=1, maximum-scale=6, user-scalable=yes, viewport-fit=cover'>
<style>
*{box-sizing:border-box;}
html,body{
    margin:0;
    padding:0;
    width:100%;
    min-height:100%;
    background:#e7ebf2;
    font-family:Arial,sans-serif;
    overflow-x:auto;
    overflow-y:auto;
    -webkit-overflow-scrolling:touch;
    touch-action:pan-x pan-y pinch-zoom;
    -webkit-user-select:none;
    user-select:none;
    cursor:grab;

    /* Keep scrolling enabled, but hide the visual scrollbar. */
    scrollbar-width:none;
    -ms-overflow-style:none;
}
html::-webkit-scrollbar,
body::-webkit-scrollbar,
#pages::-webkit-scrollbar{
    width:0 !important;
    height:0 !important;
    display:none !important;
    background:transparent !important;
}
#pages{
    width:100%;
    min-height:100vh;
    margin:0;
    padding:0 0 8px 0;
    scrollbar-width:none;
    -ms-overflow-style:none;
}
.page-wrap{
    width:100%;
    margin:0 0 6px 0;
    padding:0;
    display:flex;
    justify-content:center;
    align-items:flex-start;
    background:#dfe4ec;
}
canvas{
    display:block;
    margin:0;
    padding:0;
    width:100%;
    height:auto;
    background:#fff;
}
#status{
    position:fixed;
    left:50%;
    top:18px;
    transform:translateX(-50%);
    z-index:999;
    max-width:86%;
    background:rgba(255,255,255,.96);
    color:#42536d;
    padding:10px 14px;
    border-radius:10px;
    box-shadow:0 2px 12px rgba(0,0,0,.14);
    font-size:14px;
    text-align:center;
}
</style>
</head>
<body>
<div id='pages'></div>
<div id='status'>Loading PDF...</div>
<script src='https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js'></script>
<script>
(function(){
    const status = document.getElementById('status');
    const pages = document.getElementById('pages');

    // Unity Editor does not always translate click-and-drag into native WebView
    // touch scrolling. Add explicit mouse-drag scrolling for Editor/desktop while
    // keeping normal touch/pinch scrolling on Android.
    let mouseDragging = false;
    let lastMouseX = 0;
    let lastMouseY = 0;

    document.addEventListener('mousedown', function(event){
        if(event.button !== 0) return;
        mouseDragging = true;
        lastMouseX = event.clientX;
        lastMouseY = event.clientY;
        document.body.style.cursor = 'grabbing';
        event.preventDefault();
    }, {passive:false});

    window.addEventListener('mousemove', function(event){
        if(!mouseDragging) return;
        const dx = event.clientX - lastMouseX;
        const dy = event.clientY - lastMouseY;
        lastMouseX = event.clientX;
        lastMouseY = event.clientY;
        window.scrollBy(-dx, -dy);
        event.preventDefault();
    }, {passive:false});

    function stopMouseDrag(){
        mouseDragging = false;
        document.body.style.cursor = 'grab';
    }

    window.addEventListener('mouseup', stopMouseDrag);
    window.addEventListener('mouseleave', stopMouseDrag);

    // Desktop mouse wheel support in the Unity Editor. Android still uses
    // the WebView's native finger scrolling.
    window.addEventListener('wheel', function(event){
        window.scrollBy(event.deltaX, event.deltaY);
        event.preventDefault();
    }, {passive:false});

    function fail(message){
        status.textContent = message;
        status.style.display = 'block';
    }

    async function renderAll(pdf){
        pages.innerHTML = '';
        status.textContent = 'Rendering ' + pdf.numPages + ' page(s)...';
        status.style.display = 'block';

        // Exact WebView CSS width: no artificial min-width and no side padding.
        const availableWidth = Math.max(1, document.documentElement.clientWidth || window.innerWidth || 1);

        for(let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber++){
            const page = await pdf.getPage(pageNumber);
            const baseViewport = page.getViewport({scale:1});
            const scale = availableWidth / baseViewport.width;
            const viewport = page.getViewport({scale:scale});

            const wrap = document.createElement('div');
            wrap.className = 'page-wrap';

            const canvas = document.createElement('canvas');
            const ratio = Math.min(window.devicePixelRatio || 1, 2);
            canvas.width = Math.max(1, Math.floor(viewport.width * ratio));
            canvas.height = Math.max(1, Math.floor(viewport.height * ratio));
            canvas.style.width = '100%';
            canvas.style.height = Math.floor(viewport.height) + 'px';

            wrap.appendChild(canvas);
            pages.appendChild(wrap);

            const ctx = canvas.getContext('2d');
            await page.render({
                canvasContext: ctx,
                viewport: viewport,
                transform: ratio === 1 ? null : [ratio,0,0,ratio,0,0]
            }).promise;
        }

        status.style.display = 'none';
        window.scrollTo(0,0);
    }

    try{
        if(!window.pdfjsLib){
            fail('PDF renderer could not be loaded. Check the internet connection.');
            return;
        }

        pdfjsLib.GlobalWorkerOptions.workerSrc =
            'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';

        const raw = atob('" + base64Pdf + @"');
        const bytes = new Uint8Array(raw.length);
        for(let i=0;i<raw.length;i++) bytes[i] = raw.charCodeAt(i);

        pdfjsLib.getDocument({data:bytes}).promise.then(renderAll).catch(function(error){
            fail('Cannot render PDF: ' + error.message);
        });
    }
    catch(error){
        fail('Cannot open PDF: ' + error.message);
    }
})();
</script>
</body>
</html>";
    }

    private bool TryLoadNativeWebViewHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;

        if (nativeWebViewComponent == null)
            FindNativeWebViewComponent();

        if (nativeWebViewComponent == null)
            return false;

        Type type = nativeWebViewComponent.GetType();

        // Most versions of net.gree.unity-webview expose:
        // LoadHTML(string html, string baseUrl)
        var twoArgs = type.GetMethod(
            "LoadHTML",
            new[] { typeof(string), typeof(string) });

        if (twoArgs != null)
        {
            try
            {
                twoArgs.Invoke(
                    nativeWebViewComponent,
                    new object[] { html, "https://localhost/" });
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[ShowLessonPageController] LoadHTML(html, baseUrl) failed: " +
                    exception.Message);
            }
        }

        // Compatibility fallback for versions that expose LoadHTML(string).
        var oneArg = type.GetMethod(
            "LoadHTML",
            new[] { typeof(string) });

        if (oneArg != null)
        {
            try
            {
                oneArg.Invoke(
                    nativeWebViewComponent,
                    new object[] { html });
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[ShowLessonPageController] LoadHTML(html) failed: " +
                    exception.Message);
            }
        }

        Debug.LogError(
            "[ShowLessonPageController] WebViewObject.LoadHTML was not found.");
        return false;
    }

    private void ClosePdfViewer()
    {
        if (!pdfViewerOpen && currentPdfAsset == null)
        {
            SetVisible(pdfViewerOverlay, false);
            return;
        }

        SetNativeWebViewVisible(false);
        // Restore the native WebView scrollbar setting before handing the WebView
        // back to the YouTube bridge. PDF scrolling remains available while open.
        SetNativeWebViewScrollbarsVisible(true);
        nativeVideoWebViewVisible = false;
        nativeVideoWebViewVisibilityKnown = true;
        SetVisible(pdfViewerOverlay, false);

        pdfViewerOpen = false;
        currentPdfAsset = null;

        // Allow UnityWebViewYouTubeBridge to control the native WebView again.
        if (youtubeBridge != null)
            youtubeBridge.SetFullscreen(false);

        // The PDF temporarily reused the same native WebView as the embedded YouTube player.
        // Reload the lesson video page when returning to ShowLessonScene.
        if (youtubeBridge != null &&
            currentLesson != null &&
            !string.IsNullOrWhiteSpace(currentLesson.youtube_url))
        {
            youtubeBridge.Load(currentLesson.youtube_url);

            if (resumeVideoAfterPdf)
                StartCoroutine(ResumeYoutubeAfterPdfRoutine());
        }

        resumeVideoAfterPdf = false;
    }

    private IEnumerator ResumeYoutubeAfterPdfRoutine()
    {
        // Give the embedded player a short moment to rebuild after Load().
        float timeout = 3f;
        while (timeout > 0f)
        {
            if (youtubeBridge != null && youtubeBridge.IsReady)
            {
                youtubeBridge.Play();
                yield break;
            }

            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void HandlePdfViewerDownloadClicked()
    {
        if (currentPdfAsset == null)
            return;

        StartCoroutine(DownloadAssetRoutine(currentPdfAsset, pdfViewerDownload));
    }

    private bool TryLoadNativeWebViewUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (nativeWebViewComponent == null)
            FindNativeWebViewComponent();

        if (nativeWebViewComponent == null)
            return false;

        Type type = nativeWebViewComponent.GetType();
        var loadUrl = type.GetMethod("LoadURL", new[] { typeof(string) });

        if (loadUrl == null)
        {
            Debug.LogError(
                "[ShowLessonPageController] WebViewObject.LoadURL(string) was not found.");
            return false;
        }

        try
        {
            loadUrl.Invoke(nativeWebViewComponent, new object[] { url });
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[ShowLessonPageController] Cannot load PDF URL in WebView: " +
                exception.Message);
            return false;
        }
    }

    private void ConfigureNativeWebViewForPdf()
    {
        // Hide the native Android WebView scrollbar while preserving touch scrolling.
        SetNativeWebViewScrollbarsVisible(false);

        // Apply immediately, then Update() keeps it correct while the UI settles.
        UpdateNativePdfWebViewRect();
    }

    private void SetNativeWebViewScrollbarsVisible(bool visible)
    {
        if (nativeWebViewComponent == null)
            FindNativeWebViewComponent();

        if (nativeWebViewComponent == null)
            return;

        Type type = nativeWebViewComponent.GetType();

        // net.gree.unity-webview versions that expose scrollbar control use this API.
        var method = type.GetMethod(
            "SetScrollbarsVisibility",
            new[] { typeof(bool) });

        if (method == null)
            return;

        try
        {
            method.Invoke(nativeWebViewComponent, new object[] { visible });
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[ShowLessonPageController] Cannot change WebView scrollbar visibility: " +
                exception.Message);
        }
    }

    private void UpdateNativePdfWebViewRect()
    {
        if (nativeWebViewComponent == null || root == null || !pdfViewerOpen)
            return;

        float panelScale = 1f;
        if (root.panel != null)
            panelScale = Mathf.Max(0.01f, root.panel.scaledPixelsPerPoint);

        // The native WebView must never cover the UI Toolkit header because native
        // Android views are rendered above Unity. Use the header's actual bottom edge
        // as the top margin, and force left/right/bottom margins to zero so the PDF
        // reader occupies the ENTIRE remaining phone body.
        float headerBottomPoints = 86f;
        if (pdfViewerHeader != null)
        {
            Rect headerBounds = pdfViewerHeader.worldBound;
            if (headerBounds.height > 1f && headerBounds.yMax > 1f)
                headerBottomPoints = headerBounds.yMax;
        }

        int top = Mathf.RoundToInt(headerBottomPoints * panelScale);
        top = Mathf.Clamp(top, 0, Mathf.Max(0, Screen.height - 1));

        if (!TrySetNativeWebViewMargins(0, top, 0, 0))
        {
            Debug.LogWarning(
                "[ShowLessonPageController] Could not size PDF WebView to full body.");
        }
    }

    private IEnumerator DownloadAssetRoutine(LessonAssetView asset, Button button)
    {
        if (asset == null)
        {
            SetDownloadStatus("This file record is invalid.");
            yield break;
        }

        button?.SetEnabled(false);
        SetDownloadStatus($"Downloading {asset.file_name}...");

        byte[] bytes = null;
        string error = null;

        yield return FetchAssetBytesRoutine(
            asset,
            value => bytes = value,
            message => error = message);

        button?.SetEnabled(true);

        if (!string.IsNullOrWhiteSpace(error))
        {
            SetDownloadStatus(error);
            yield break;
        }

        if (bytes == null || bytes.Length == 0)
        {
            SetDownloadStatus("Download failed: the file is empty.");
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
            File.WriteAllBytes(destination, bytes);
            SetDownloadStatus($"Saved to: {destination}");
            Debug.Log($"Downloaded lesson file to {destination}");
        }
        catch (Exception exception)
        {
            SetDownloadStatus($"Cannot save file: {exception.Message}");
        }
    }

    private IEnumerator FetchAssetBytesRoutine(
        LessonAssetView asset,
        Action<byte[]> onSuccess,
        Action<string> onError)
    {
        // Private R2 objects need a fresh signed URL. If a signer Edge Function is
        // configured, try it before any persisted/public URL. This avoids stale URLs.
        string freshR2Url = null;
        if (!string.IsNullOrWhiteSpace(r2SignedUrlFunctionName))
        {
            yield return ResolveFreshR2UrlRoutine(asset, value => freshR2Url = value);
            if (!string.IsNullOrWhiteSpace(freshR2Url))
            {
                byte[] signedBytes = null;
                string signedError = null;
                yield return DownloadBytesFromUrlRoutine(
                    freshR2Url,
                    false,
                    "fresh R2 signed URL",
                    value => signedBytes = value,
                    message => signedError = message);

                if (signedBytes != null && signedBytes.Length > 0)
                {
                    onSuccess?.Invoke(signedBytes);
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(signedError))
                    Debug.LogWarning("[ShowLessonPageController] " + signedError);
            }
        }

        List<AssetUrlCandidate> candidates = BuildAssetUrlCandidates(asset);

        if (candidates.Count == 0)
        {
            onError?.Invoke(
                "No usable file URL was found. Check lesson_assets.storage_path/storage_bucket " +
                "or configure the R2 Public Base URL on ShowLessonPageController.");
            yield break;
        }

        string lastError = string.Empty;

        foreach (AssetUrlCandidate candidate in candidates)
        {
            byte[] bytes = null;
            string error = null;

            yield return DownloadBytesFromUrlRoutine(
                candidate.url,
                candidate.applySupabaseAuth,
                candidate.description,
                value => bytes = value,
                message => error = message);

            if (bytes != null && bytes.Length > 0)
            {
                onSuccess?.Invoke(bytes);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                lastError = error;
                Debug.LogWarning("[ShowLessonPageController] File candidate failed: " + lastError);
            }
        }

        onError?.Invoke(
            "Cannot load this PDF from its saved storage location. " +
            "Please verify the file URL/object key in lesson_assets. " +
            lastError);
    }

    private IEnumerator DownloadBytesFromUrlRoutine(
        string url,
        bool applySupabaseAuth,
        string description,
        Action<byte[]> onSuccess,
        Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            onError?.Invoke(description + " -> empty URL");
            yield break;
        }

        string normalizedUrl = NormalizeHttpUrl(url);

        using UnityWebRequest request = UnityWebRequest.Get(normalizedUrl);
        request.timeout = SupabaseConfig.RequestTimeoutSeconds;
        request.SetRequestHeader("Accept", "application/pdf,application/octet-stream,*/*");

        if (applySupabaseAuth)
            restService.ApplyAuthHeaders(request);

        yield return request.SendWebRequest();

        byte[] data = request.downloadHandler?.data;
        string contentType = request.GetResponseHeader("Content-Type") ?? string.Empty;

        if (request.result == UnityWebRequest.Result.Success && data != null && data.Length > 0)
        {
            // Do not accept an HTML error/login page as a PDF just because the server returned 200.
            bool looksPdf = data.Length >= 5 &&
                            data[0] == (byte)'%' && data[1] == (byte)'P' &&
                            data[2] == (byte)'D' && data[3] == (byte)'F' && data[4] == (byte)'-';
            bool pdfContentType = contentType.IndexOf("application/pdf", StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksPdf || pdfContentType)
            {
                Debug.Log($"[ShowLessonPageController] PDF loaded successfully from {description}: {normalizedUrl}");
                onSuccess?.Invoke(data);
                yield break;
            }

            string preview = TryGetTextPreview(data);
            onError?.Invoke(
                $"{description} returned non-PDF content ({contentType}). URL: {normalizedUrl}. " + preview);
            yield break;
        }

        string bodyPreview = TryGetTextPreview(data);
        onError?.Invoke(
            $"{description} -> HTTP {request.responseCode}: {request.error}. URL: {normalizedUrl}. {bodyPreview}");
    }

    private IEnumerator ResolveFreshR2UrlRoutine(LessonAssetView asset, Action<string> onResolved)
    {
        if (asset == null || string.IsNullOrWhiteSpace(r2SignedUrlFunctionName) || restService == null)
            yield break;

        string endpoint =
            $"{restService.ProjectUrl.TrimEnd('/')}/functions/v1/{UnityWebRequest.EscapeURL(r2SignedUrlFunctionName.Trim())}";

        R2SignedUrlRequest payload = new()
        {
            asset_id = asset.id ?? string.Empty,
            bucket = asset.storage_bucket ?? string.Empty,
            key = ExtractObjectKey(asset.storage_path),
            file_name = asset.file_name ?? string.Empty
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        using UnityWebRequest request = new(endpoint, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = SupabaseConfig.RequestTimeoutSeconds;
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");
        restService.ApplyAuthHeaders(request);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning(
                $"[ShowLessonPageController] R2 signer function failed ({request.responseCode}): " +
                (request.downloadHandler?.text ?? request.error));
            yield break;
        }

        R2SignedUrlResponse response = null;
        try
        {
            response = JsonUtility.FromJson<R2SignedUrlResponse>(request.downloadHandler.text);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[ShowLessonPageController] Cannot parse R2 signed URL response: " + exception.Message);
            yield break;
        }

        string resolved = response?.url;
        if (string.IsNullOrWhiteSpace(resolved)) resolved = response?.signed_url;
        if (string.IsNullOrWhiteSpace(resolved)) resolved = response?.signedURL;

        if (!string.IsNullOrWhiteSpace(resolved))
            onResolved?.Invoke(resolved.Trim());
    }

    private static string ExtractObjectKey(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) return string.Empty;
        string value = storagePath.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
            return Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

        return Uri.UnescapeDataString(value.TrimStart('/'));
    }

    private static string NormalizeHttpUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        string value = url.Trim();

        // CRITICAL:
        // Do NOT rebuild/escape an AWS Signature V4 / Cloudflare R2 presigned URL.
        // The signature is calculated from the exact canonical path + query string.
        // Re-encoding the path after signing can make R2 return HTTP 403
        // SignatureDoesNotMatch even though the URL originally generated by the signer is valid.
        if (IsPresignedObjectUrl(value))
            return value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
            return value.Replace(" ", "%20");

        try
        {
            UriBuilder builder = new(uri);
            string[] parts = Uri.UnescapeDataString(builder.Path).Split('/');
            for (int i = 0; i < parts.Length; i++)
                parts[i] = UnityWebRequest.EscapeURL(parts[i]).Replace("+", "%20");
            builder.Path = string.Join("/", parts);
            return builder.Uri.AbsoluteUri;
        }
        catch
        {
            return value.Replace(" ", "%20");
        }
    }

    private static bool IsPresignedObjectUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return url.IndexOf("X-Amz-Signature=", StringComparison.OrdinalIgnoreCase) >= 0 ||
               url.IndexOf("X-Amz-Credential=", StringComparison.OrdinalIgnoreCase) >= 0 ||
               url.IndexOf("X-Amz-Algorithm=AWS4-HMAC-SHA256", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string TryGetTextPreview(byte[] data)
    {
        if (data == null || data.Length == 0) return string.Empty;
        int length = Mathf.Min(data.Length, 240);
        try
        {
            string text = Encoding.UTF8.GetString(data, 0, length)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            return string.IsNullOrWhiteSpace(text) ? string.Empty : "Response: " + text;
        }
        catch
        {
            return string.Empty;
        }
    }

    private List<AssetUrlCandidate> BuildAssetUrlCandidates(LessonAssetView asset)
    {
        List<AssetUrlCandidate> result = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        void Add(string url, bool applyAuth, string description)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            string normalized = url.Trim();
            if (!seen.Add(normalized)) return;

            result.Add(new AssetUrlCandidate
            {
                url = normalized,
                applySupabaseAuth = applyAuth,
                description = description
            });
        }

        // Prefer URLs already persisted by the backend/R2 upload flow.
        AddAbsoluteAssetUrl(asset.file_url, "lesson_assets.file_url");
        AddAbsoluteAssetUrl(asset.public_url, "lesson_assets.public_url");
        AddAbsoluteAssetUrl(asset.signed_url, "lesson_assets.signed_url");
        AddAbsoluteAssetUrl(asset.r2_url, "lesson_assets.r2_url");
        AddAbsoluteAssetUrl(asset.object_url, "lesson_assets.object_url");
        AddAbsoluteAssetUrl(asset.url, "lesson_assets.url");

        void AddAbsoluteAssetUrl(string url, string description)
        {
            if (!IsHttpUrl(url)) return;
            Add(url, IsSupabaseProjectUrl(url), description);
        }

        string rawPath = asset.storage_path?.Trim() ?? string.Empty;
        string rawBucket = asset.storage_bucket?.Trim() ?? string.Empty;

        // Some rows store the complete URL directly in storage_path.
        if (IsHttpUrl(rawPath))
            Add(rawPath, IsSupabaseProjectUrl(rawPath), "absolute storage_path");

        // Some R2 integrations store the public/custom-domain base in storage_bucket.
        if (IsHttpUrl(rawBucket) && !string.IsNullOrWhiteSpace(rawPath))
            Add(
                rawBucket.TrimEnd('/') + "/" + rawPath.TrimStart('/'),
                false,
                "R2/public storage_bucket + storage_path");

        // Public R2/custom domain. When storage_path is already an absolute URL,
        // use only its object path instead of concatenating the whole URL.
        if (!string.IsNullOrWhiteSpace(r2PublicBaseUrl) &&
            !string.IsNullOrWhiteSpace(rawPath))
        {
            string objectKey = ExtractObjectKey(rawPath);
            Add(
                r2PublicBaseUrl.TrimEnd('/') + "/" + objectKey.TrimStart('/'),
                false,
                "R2 Public Base URL + object key");

            // Compatibility candidates for older rows that stored only the filename
            // or prefixed the R2 bucket name into the key.
            if (!string.IsNullOrWhiteSpace(rawBucket))
            {
                string bucketPrefix = rawBucket.Trim('/') + "/";
                if (objectKey.StartsWith(bucketPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    Add(
                        r2PublicBaseUrl.TrimEnd('/') + "/" + objectKey.Substring(bucketPrefix.Length),
                        false,
                        "R2 Public Base URL + key without bucket prefix");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(rawBucket) &&
            !IsHttpUrl(rawBucket) &&
            !string.IsNullOrWhiteSpace(rawPath))
        {
            string pathForSupabase = rawPath.TrimStart('/');
            string bucketPrefix = rawBucket.Trim('/') + "/";

            // Fix legacy rows such as bucket=lesson-files and
            // storage_path=lesson-files/folder/file.pdf.
            if (pathForSupabase.StartsWith(bucketPrefix, StringComparison.OrdinalIgnoreCase))
                pathForSupabase = pathForSupabase.Substring(bucketPrefix.Length);

            string encodedBucket = UnityWebRequest.EscapeURL(rawBucket);
            string encodedPath = EncodeStoragePath(pathForSupabase);
            string projectUrl = restService.ProjectUrl.TrimEnd('/');

            Add(
                $"{projectUrl}/storage/v1/object/authenticated/{encodedBucket}/{encodedPath}",
                true,
                "Supabase authenticated storage");

            // Public buckets do not need the authenticated endpoint.
            Add(
                $"{projectUrl}/storage/v1/object/public/{encodedBucket}/{encodedPath}",
                false,
                "Supabase public storage");
        }

        return result;
    }

    private bool IsSupabaseProjectUrl(string url)
    {
        if (!IsHttpUrl(url) || restService == null || string.IsNullOrWhiteSpace(restService.ProjectUrl))
            return false;

        return url.StartsWith(
            restService.ProjectUrl.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttpUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private IEnumerator OpenQuizRoutine(LessonAssetView asset, int index)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.id))
        {
            ShowError("Quiz asset is missing or invalid.");
            yield break;
        }

        string response = null;
        string error = null;
        string encodedAssetId = UnityWebRequest.EscapeURL(asset.id);

        // IMPORTANT: lesson_assets.id is NOT quizzes.id.
        // Resolve the real quiz row through quizzes.source_asset_id.
        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/quizzes" +
            "?select=id,lesson_id,title,total_questions,max_score,opens_at,closes_at,is_published,source_asset_id" +
            $"&source_asset_id=eq.{encodedAssetId}&limit=1",
            null,
            null,
            value => response = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            ShowError("Cannot load quiz metadata: " + error);
            yield break;
        }

        QuizMetadataViewList wrapper = ParseList<QuizMetadataViewList>(response);
        if (wrapper?.items == null || wrapper.items.Length == 0)
        {
            ShowError(
                "No quiz database row is linked to this quiz PDF. " +
                "Expected quizzes.source_asset_id = " + asset.id
            );
            yield break;
        }

        QuizMetadataView quiz = wrapper.items[0];

        PlayerPrefs.SetString("selected_quiz_id", quiz.id ?? string.Empty);
        PlayerPrefs.SetString("selected_quiz_asset_id", asset.id ?? string.Empty);
        PlayerPrefs.SetString("selected_quiz_title", quiz.title ?? "Quiz");
        PlayerPrefs.SetString(
            "selected_quiz_subtitle",
            currentLesson != null ? currentLesson.title ?? string.Empty : string.Empty
        );
        PlayerPrefs.SetString("selected_quiz_file_name", asset.file_name ?? string.Empty);
        PlayerPrefs.SetString("selected_quiz_bucket", asset.storage_bucket ?? string.Empty);
        PlayerPrefs.SetString("selected_quiz_storage_path", asset.storage_path ?? string.Empty);
        PlayerPrefs.SetString("selected_quiz_open_at", quiz.opens_at ?? string.Empty);
        PlayerPrefs.SetString("selected_quiz_close_at", quiz.closes_at ?? string.Empty);
        PlayerPrefs.SetInt("selected_quiz_questions", quiz.total_questions);
        PlayerPrefs.SetFloat(
            "selected_quiz_max_score",
            quiz.max_score > 0f ? quiz.max_score : 10f
        );
        PlayerPrefs.SetInt("selected_quiz_order", index + 1);
        PlayerPrefs.Save();

        Debug.Log(
            "[ShowLessonPageController] Opening quiz. " +
            $"Quiz ID: {quiz.id}, Asset ID: {asset.id}, " +
            $"Questions: {quiz.total_questions}"
        );

        LoadSceneSafely(QuizSceneName);
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

    private IEnumerator LoadClassModelSourcesRoutine()
    {
        classModelSources.Clear();

        string classId = PlayerPrefs.GetString("selected_class_id", string.Empty);

        // Defensive fallback: if the class id is unavailable, keep the old behavior
        // and expose only models from the current lesson.
        if (!Guid.TryParse(classId, out _))
        {
            foreach (LessonAssetView asset in modelAssets)
            {
                if (asset == null) continue;
                classModelSources.Add(new ClassModelSource
                {
                    asset = asset,
                    lessonTitle = currentLesson != null && !string.IsNullOrWhiteSpace(currentLesson.title)
                        ? currentLesson.title
                        : "Current Lesson",
                    chapterOrder = PlayerPrefs.GetInt("selected_chapter_order", 0),
                    isCurrentLesson = true
                });
            }
            yield break;
        }

        string encodedClassId = UnityWebRequest.EscapeURL(classId);
        string chapterJson = null;
        string requestError = null;

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/chapters" +
            "?select=id,title,chapter_order" +
            $"&class_id=eq.{encodedClassId}" +
            "&order=chapter_order.asc,created_at.asc",
            null,
            null,
            value => chapterJson = value,
            message => requestError = message);

        if (!string.IsNullOrWhiteSpace(requestError))
        {
            Debug.LogWarning("[ShowLessonPageController] Cannot load class chapters for 3D catalog: " + requestError);
            yield return AddCurrentLessonModelsAsFallback();
            yield break;
        }

        ClassChapterViewList chapterWrapper = ParseList<ClassChapterViewList>(chapterJson);
        if (chapterWrapper?.items == null || chapterWrapper.items.Length == 0)
        {
            yield return AddCurrentLessonModelsAsFallback();
            yield break;
        }

        Dictionary<string, int> chapterOrderById = new Dictionary<string, int>();
        List<string> chapterIds = new List<string>();
        foreach (ClassChapterView chapter in chapterWrapper.items)
        {
            if (chapter == null || string.IsNullOrWhiteSpace(chapter.id)) continue;
            chapterIds.Add(chapter.id);
            chapterOrderById[chapter.id] = chapter.chapter_order;
        }

        if (chapterIds.Count == 0)
        {
            yield return AddCurrentLessonModelsAsFallback();
            yield break;
        }

        string lessonJson = null;
        requestError = null;
        string chapterFilter = string.Join(",", chapterIds);

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/lessons" +
            "?select=id,chapter_id,title,created_at" +
            $"&chapter_id=in.({chapterFilter})" +
            "&order=created_at.asc",
            null,
            null,
            value => lessonJson = value,
            message => requestError = message);

        if (!string.IsNullOrWhiteSpace(requestError))
        {
            Debug.LogWarning("[ShowLessonPageController] Cannot load class lessons for 3D catalog: " + requestError);
            yield return AddCurrentLessonModelsAsFallback();
            yield break;
        }

        ClassLessonViewList lessonWrapper = ParseList<ClassLessonViewList>(lessonJson);
        if (lessonWrapper?.items == null || lessonWrapper.items.Length == 0)
        {
            yield return AddCurrentLessonModelsAsFallback();
            yield break;
        }

        Dictionary<string, ClassLessonView> lessonById = new Dictionary<string, ClassLessonView>();
        List<string> lessonIds = new List<string>();
        foreach (ClassLessonView lesson in lessonWrapper.items)
        {
            if (lesson == null || string.IsNullOrWhiteSpace(lesson.id)) continue;
            lessonById[lesson.id] = lesson;
            lessonIds.Add(lesson.id);
        }

        if (lessonIds.Count == 0)
        {
            yield return AddCurrentLessonModelsAsFallback();
            yield break;
        }

        string assetJson = null;
        requestError = null;
        string lessonFilter = string.Join(",", lessonIds);

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/lesson_assets" +
            "?select=*" +
            "&asset_type=eq.model_3d" +
            $"&lesson_id=in.({lessonFilter})" +
            "&order=display_order.asc,created_at.asc",
            null,
            null,
            value => assetJson = value,
            message => requestError = message);

        if (!string.IsNullOrWhiteSpace(requestError))
        {
            Debug.LogWarning("[ShowLessonPageController] Cannot load class 3D assets for 3D catalog: " + requestError);
            yield return AddCurrentLessonModelsAsFallback();
            yield break;
        }

        LessonAssetViewList assetWrapper = ParseList<LessonAssetViewList>(assetJson);
        if (assetWrapper?.items != null)
        {
            foreach (LessonAssetView asset in assetWrapper.items)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.lesson_id)) continue;
                if (!lessonById.TryGetValue(asset.lesson_id, out ClassLessonView lesson)) continue;

                int chapterOrder = 0;
                if (!string.IsNullOrWhiteSpace(lesson.chapter_id))
                    chapterOrderById.TryGetValue(lesson.chapter_id, out chapterOrder);

                classModelSources.Add(new ClassModelSource
                {
                    asset = asset,
                    lessonTitle = string.IsNullOrWhiteSpace(lesson.title) ? "Lesson" : lesson.title,
                    chapterOrder = chapterOrder,
                    isCurrentLesson = string.Equals(asset.lesson_id, selectedLessonId, StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        classModelSources.Sort((a, b) =>
        {
            // Current lesson first so its first model remains initially visible.
            int currentCompare = b.isCurrentLesson.CompareTo(a.isCurrentLesson);
            if (currentCompare != 0) return currentCompare;

            int chapterCompare = a.chapterOrder.CompareTo(b.chapterOrder);
            if (chapterCompare != 0) return chapterCompare;

            int lessonCompare = string.Compare(a.lessonTitle, b.lessonTitle, StringComparison.OrdinalIgnoreCase);
            if (lessonCompare != 0) return lessonCompare;

            return (a.asset?.display_order ?? 0).CompareTo(b.asset?.display_order ?? 0);
        });

        Debug.Log(
            $"[ShowLessonPageController] Class 3D catalog loaded {classModelSources.Count} model(s) " +
            $"for class {classId} across {lessonById.Count} lesson(s).");
    }

    private IEnumerator AddCurrentLessonModelsAsFallback()
    {
        foreach (LessonAssetView asset in modelAssets)
        {
            if (asset == null) continue;
            classModelSources.Add(new ClassModelSource
            {
                asset = asset,
                lessonTitle = currentLesson != null && !string.IsNullOrWhiteSpace(currentLesson.title)
                    ? currentLesson.title
                    : "Current Lesson",
                chapterOrder = PlayerPrefs.GetInt("selected_chapter_order", 0),
                isCurrentLesson = true
            });
        }
        yield break;
    }

    private void BeginOpenModel(string mode)
    {
        if (isOpeningModelScene)
            return;

        StartCoroutine(OpenModelRoutine(mode));
    }

    private IEnumerator OpenModelRoutine(string mode)
    {
        // Build one model catalog for 3D / AR / VR from the ENTIRE current class.
        // Current-lesson models are sorted first, so VR opens with the model
        // belonging to the lesson the user is currently viewing.
        yield return LoadClassModelSourcesRoutine();

        if (classModelSources.Count == 0)
        {
            ShowError("This class does not have any 3D model yet.");
            yield break;
        }

        string normalizedMode =
            string.IsNullOrWhiteSpace(mode)
                ? "3d"
                : mode.Trim().ToLowerInvariant();

        string destinationScene;

        switch (normalizedMode)
        {
            case "ar":
                destinationScene = arModelsSceneName;
                break;

            case "vr":
                destinationScene = vrSceneName;
                break;

            case "3d":
            default:
                destinationScene = Mode3DSceneName;
                break;
        }

        if (!CanLoadScene(destinationScene))
        {
            ShowError($"Scene '{destinationScene}' is not included in Build Profiles.");
            yield break;
        }

        isOpeningModelScene = true;
        SetModelButtonsEnabled(false);
        ClearError();

        // IMPORTANT:
        // lesson_assets may point to Cloudflare R2, a persisted public/signed URL,
        // or Supabase Storage. Do NOT force every model through Supabase Storage signing.
        // That was the cause of the 400/404 NoSuchKey error shown in the Console.
        ModelLaunchManifest manifest = new()
        {
            lesson_id = selectedLessonId ?? string.Empty,
            mode = normalizedMode,
            class_id = PlayerPrefs.GetString("selected_class_id", string.Empty),
            models = new ModelLaunchItem[classModelSources.Count]
        };

        for (int i = 0; i < classModelSources.Count; i++)
        {
            ClassModelSource source = classModelSources[i];
            LessonAssetView model = source?.asset;
            if (model == null)
                continue;

            string resolvedUrl = null;
            yield return ResolveModelLaunchUrlRoutine(
                model,
                value => resolvedUrl = value);

            string modelName =
                string.IsNullOrWhiteSpace(model.file_name)
                    ? $"3D Model {i + 1}"
                    : Path.GetFileNameWithoutExtension(model.file_name);

            manifest.models[i] = new ModelLaunchItem
            {
                asset_id = model.id ?? string.Empty,
                lesson_id = model.lesson_id ?? selectedLessonId ?? string.Empty,
                lesson_title = source.lessonTitle ?? "Lesson",
                chapter_order = source.chapterOrder,
                name = modelName,
                file_name = model.file_name ?? string.Empty,
                bucket = model.storage_bucket ?? string.Empty,
                storage_path = model.storage_path ?? string.Empty,
                url = resolvedUrl ?? string.Empty,
                fallback_url = BuildPublicR2ModelUrl(model),
                display_order = model.display_order
            };
        }

        // Remove null slots, if the database happened to return an invalid row.
        List<ModelLaunchItem> validModels = new();
        foreach (ModelLaunchItem item in manifest.models)
        {
            if (item != null)
                validModels.Add(item);
        }
        manifest.models = validModels.ToArray();

        if (manifest.models.Length == 0)
        {
            isOpeningModelScene = false;
            SetModelButtonsEnabled(true);
            ShowError("No valid 3D model record was found for this lesson.");
            yield break;
        }

        // Find the first model that actually belongs to the lesson currently open
        // in ShowLessonScene. This guarantees that VRClassroomScene starts with the
        // CURRENT lesson model even though the manifest also contains the whole class.
        int initialModelIndex = 0;
        for (int i = 0; i < manifest.models.Length; i++)
        {
            ModelLaunchItem candidate = manifest.models[i];
            if (candidate != null &&
                string.Equals(
                    candidate.lesson_id,
                    selectedLessonId,
                    StringComparison.OrdinalIgnoreCase))
            {
                initialModelIndex = i;
                break;
            }
        }

        ModelLaunchItem first = manifest.models[initialModelIndex];

        PlayerPrefs.SetString("interactive_mode", normalizedMode);
        PlayerPrefs.SetString("selected_model_asset_id", first.asset_id ?? string.Empty);
        PlayerPrefs.SetString("selected_model_bucket", first.bucket ?? string.Empty);
        PlayerPrefs.SetString("selected_model_storage_path", first.storage_path ?? string.Empty);
        PlayerPrefs.SetString("selected_model_file_name", first.file_name ?? string.Empty);
        PlayerPrefs.SetString("selected_model_url", first.url ?? first.fallback_url ?? string.Empty);
        PlayerPrefs.SetString("selected_model_name", first.name ?? "3D Model");
        PlayerPrefs.SetString("selected_model_lesson_id", first.lesson_id ?? selectedLessonId ?? string.Empty);
        PlayerPrefs.SetString("selected_model_lesson_title", first.lesson_title ?? "Lesson");
        PlayerPrefs.SetInt("selected_model_chapter_order", first.chapter_order);

        // The manifest contains ALL 3D models from ALL lessons of the current class.
        // Keep the old key for AR compatibility, and also save a clearer class-level key
        // for VRClassroomScene.
        string manifestJson = JsonUtility.ToJson(manifest);
        PlayerPrefs.SetString("selected_lesson_models_json", manifestJson);
        PlayerPrefs.SetString("selected_class_models_json", manifestJson);
        PlayerPrefs.SetInt("selected_lesson_model_count", manifest.models.Length);
        PlayerPrefs.SetInt("selected_lesson_model_index", initialModelIndex);

        // Preserve the REAL page that opened ShowLessonScene before temporarily
        // changing previous_scene for the AR/3D child scene.
        PreserveShowLessonParentScene();

        // ARScene / Mode3DScene use previous_scene to return here.
        PlayerPrefs.SetString(PreviousSceneKey, "ShowLessonScene");
        PlayerPrefs.Save();

        // Native unity-webview is drawn above Unity. Hide it before changing scene so it
        // cannot cover the AR camera after ARScene has loaded.
        if (youtubeBridge != null && youtubeBridge.IsReady)
            youtubeBridge.Pause();
        SetNativeWebViewVisible(false);

        Debug.Log(
            "[ShowLessonPageController] Opening model scene." +
            $"\nMode: {normalizedMode}" +
            $"\nScene: {destinationScene}" +
            $"\nLesson models: {manifest.models.Length}" +
            $"\nFirst model URL present: {!string.IsNullOrWhiteSpace(first.url)}" +
            $"\nFirst public fallback present: {!string.IsNullOrWhiteSpace(first.fallback_url)}" +
            $"\nFirst model path: {first.storage_path}");

        SceneManager.LoadScene(destinationScene);
    }

    /// <summary>
    /// Builds a public R2/custom-domain fallback URL when r2PublicBaseUrl is configured.
    /// This does NOT use the private S3 API endpoint. Example:
    /// https://pub-xxxx.r2.dev/{objectKey}
    /// or https://models.example.com/{objectKey}
    ///
    /// Use this as a fallback when a private R2 signer returns AccessDenied.
    /// </summary>
    private string BuildPublicR2ModelUrl(LessonAssetView model)
    {
        if (model == null)
            return string.Empty;

        string rawPath = model.storage_path?.Trim() ?? string.Empty;
        string rawBucket = model.storage_bucket?.Trim() ?? string.Empty;

        // lesson-models has its own public domain. Always canonicalize old/stale
        // *.r2.dev URLs to the current lesson-models public domain while preserving
        // the object key.
        if (string.Equals(rawBucket, "lesson-models", StringComparison.OrdinalIgnoreCase))
        {
            string publicBase = GetLessonModelsPublicBaseUrl();

            if (!string.IsNullOrWhiteSpace(publicBase) &&
                !string.IsNullOrWhiteSpace(rawPath))
            {
                string key = ExtractObjectKey(rawPath);

                // Old data can occasionally contain "lesson-models/<key>".
                const string bucketPrefix = "lesson-models/";
                if (key.StartsWith(bucketPrefix, StringComparison.OrdinalIgnoreCase))
                    key = key.Substring(bucketPrefix.Length);

                if (!string.IsNullOrWhiteSpace(key))
                    return publicBase.TrimEnd('/') + "/" + key.TrimStart('/');
            }
        }

        // Other absolute public/custom-domain URLs remain untouched.
        if (IsHttpUrl(rawPath) && !IsR2S3ApiUrl(rawPath))
            return rawPath;

        // Some integrations store the public/custom-domain base in storage_bucket.
        if (IsHttpUrl(rawBucket) &&
            !IsR2S3ApiUrl(rawBucket) &&
            !string.IsNullOrWhiteSpace(rawPath))
        {
            return rawBucket.TrimEnd('/') + "/" +
                   ExtractObjectKey(rawPath).TrimStart('/');
        }

        if (string.IsNullOrWhiteSpace(r2PublicBaseUrl) ||
            string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        string objectKey = ExtractObjectKey(rawPath);

        if (!string.IsNullOrWhiteSpace(rawBucket) && !IsHttpUrl(rawBucket))
        {
            string bucketPrefix = rawBucket.Trim('/') + "/";
            if (objectKey.StartsWith(bucketPrefix, StringComparison.OrdinalIgnoreCase))
                objectKey = objectKey.Substring(bucketPrefix.Length);
        }

        return r2PublicBaseUrl.TrimEnd('/') + "/" + objectKey.TrimStart('/');
    }

    private string GetLessonModelsPublicBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(lessonModelsPublicBaseUrl))
            return lessonModelsPublicBaseUrl.Trim().TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(r2PublicBaseUrl))
            return r2PublicBaseUrl.Trim().TrimEnd('/');

        return string.Empty;
    }

    private static bool IsR2S3ApiUrl(string url)
    {
        if (!IsHttpUrl(url))
            return false;

        return url.IndexOf(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Resolve a model URL without assuming that the object is stored in Supabase Storage.
    ///
    /// Priority:
    /// 1) Absolute public/custom-domain storage_path (for example pub-xxxx.r2.dev)
    /// 2) R2 signer only when the saved path is not already public
    /// 3) Other saved/public candidates
    /// 4) Supabase compatibility fallback
    ///
    /// This avoids re-signing a public lesson-models URL and fixes the AccessDenied loop.
    /// </summary>
    private IEnumerator ResolveModelLaunchUrlRoutine(
        LessonAssetView model,
        Action<string> onResolved)
    {
        if (model == null)
            yield break;

        // 1) lesson-models: always canonicalize through the CURRENT public domain.
        // This repairs newly uploaded rows that were saved with an old r2.dev host
        // while keeping the same valid object key.
        if (string.Equals(
                model.storage_bucket?.Trim(),
                "lesson-models",
                StringComparison.OrdinalIgnoreCase))
        {
            string canonicalPublicUrl = BuildPublicR2ModelUrl(model);

            if (!string.IsNullOrWhiteSpace(canonicalPublicUrl))
            {
                string original = model.storage_path?.Trim() ?? string.Empty;

                if (!string.Equals(
                        original,
                        canonicalPublicUrl,
                        StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning(
                        "[ShowLessonPageController] Repaired lesson-models URL host/path for runtime load." +
                        "\nOriginal: " + original +
                        "\nCanonical: " + canonicalPublicUrl);
                }
                else
                {
                    Debug.Log(
                        "[ShowLessonPageController] Using canonical lesson-models public URL directly.");
                }

                onResolved?.Invoke(canonicalPublicUrl);
                yield break;
            }
        }

        // 2) Other absolute public/custom-domain storage paths can be used directly.
        string absoluteStoragePath = model.storage_path?.Trim() ?? string.Empty;
        if (IsHttpUrl(absoluteStoragePath) &&
            !IsR2S3ApiUrl(absoluteStoragePath))
        {
            Debug.Log(
                "[ShowLessonPageController] Using public model storage_path directly. " +
                "R2 signer skipped.");

            onResolved?.Invoke(absoluteStoragePath);
            yield break;
        }

        // 3) Private Cloudflare R2: ask the configured Edge Function for a fresh URL.
        // This runs only when storage_path is not already a public URL.
        if (!string.IsNullOrWhiteSpace(r2SignedUrlFunctionName))
        {
            string freshR2Url = null;
            yield return ResolveFreshR2UrlRoutine(model, value => freshR2Url = value);

            if (!string.IsNullOrWhiteSpace(freshR2Url))
            {
                // Preserve the signer output byte-for-byte except surrounding whitespace.
                // Never pass an R2/AWS presigned URL through UriBuilder/EscapeURL.
                string exactSignedUrl = freshR2Url.Trim();
                Debug.Log("[ShowLessonPageController] Fresh R2 model URL received. Presigned=" +
                          IsPresignedObjectUrl(exactSignedUrl));
                onResolved?.Invoke(exactSignedUrl);
                yield break;
            }
        }

        // 4) Persisted URL columns / public R2 base URL / other saved candidates.
        List<AssetUrlCandidate> candidates = BuildAssetUrlCandidates(model);
        foreach (AssetUrlCandidate candidate in candidates)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.url))
                continue;

            // Prefer external/R2 URLs over Supabase candidates. No download is performed
            // here; RuntimeGlbLoader in the destination scene will download the GLB.
            if (!candidate.applySupabaseAuth)
            {
                string candidateUrl = candidate.url.Trim();
                onResolved?.Invoke(
                    IsPresignedObjectUrl(candidateUrl)
                        ? candidateUrl
                        : NormalizeHttpUrl(candidateUrl));
                yield break;
            }
        }

        // 5) Supabase Storage compatibility fallback.
        // This is important for private buckets because /object/authenticated/... requires
        // an Authorization header, while glTFast normally downloads a URL by itself.
        if (!string.IsNullOrWhiteSpace(model.storage_bucket) &&
            !string.IsNullOrWhiteSpace(model.storage_path) &&
            !IsHttpUrl(model.storage_bucket))
        {
            string signedUrl = null;
            string signedError = null;

            yield return CreateSignedModelUrlRoutine(
                model.storage_bucket,
                model.storage_path,
                Mathf.Max(60, modelSignedUrlLifetimeSeconds),
                value => signedUrl = value,
                message => signedError = message);

            if (!string.IsNullOrWhiteSpace(signedUrl))
            {
                string exactSignedUrl = signedUrl.Trim();
                onResolved?.Invoke(
                    IsPresignedObjectUrl(exactSignedUrl)
                        ? exactSignedUrl
                        : NormalizeHttpUrl(exactSignedUrl));
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(signedError))
            {
                Debug.LogWarning(
                    "[ShowLessonPageController] Supabase signed model URL failed. " +
                    "Trying authenticated URL as a compatibility fallback. " +
                    signedError);
            }
        }

        // 4) Compatibility fallback: authenticated Supabase candidate.
        // ARScene also tries to apply SupabaseRuntimeRestService auth headers when possible.
        foreach (AssetUrlCandidate candidate in candidates)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.url))
                continue;

            if (candidate.applySupabaseAuth)
            {
                onResolved?.Invoke(NormalizeHttpUrl(candidate.url));
                yield break;
            }
        }

        onResolved?.Invoke(string.Empty);
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

    private void PreserveShowLessonParentScene()
    {
        string currentPrevious =
            PlayerPrefs.GetString(
                PreviousSceneKey,
                previousSceneName);

        // Only overwrite the preserved parent when previous_scene is a real parent.
        // This avoids turning ShowLessonScene into its own parent after repeated
        // AR -> ShowLesson -> AR cycles.
        if (string.IsNullOrWhiteSpace(currentPrevious) ||
            string.Equals(
                currentPrevious,
                "ShowLessonScene",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!PlayerPrefs.HasKey(ShowLessonParentSceneKey))
            {
                PlayerPrefs.SetString(
                    ShowLessonParentSceneKey,
                    previousSceneName);
            }

            return;
        }

        PlayerPrefs.SetString(
            ShowLessonParentSceneKey,
            currentPrevious);
    }

    private void RestoreParentSceneAfterChildReturn()
    {
        string currentPrevious =
            PlayerPrefs.GetString(
                PreviousSceneKey,
                previousSceneName);

        if (!string.Equals(
                currentPrevious,
                "ShowLessonScene",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!PlayerPrefs.HasKey(ShowLessonParentSceneKey))
        {
            // Fallback prevents a self-loop even for old PlayerPrefs data.
            PlayerPrefs.SetString(
                PreviousSceneKey,
                previousSceneName);
            PlayerPrefs.Save();
            return;
        }

        string parent =
            PlayerPrefs.GetString(
                ShowLessonParentSceneKey,
                previousSceneName);

        if (string.IsNullOrWhiteSpace(parent) ||
            string.Equals(
                parent,
                "ShowLessonScene",
                StringComparison.OrdinalIgnoreCase))
        {
            parent = previousSceneName;
        }

        PlayerPrefs.SetString(
            PreviousSceneKey,
            parent);

        PlayerPrefs.Save();

        Debug.Log(
            "[ShowLessonPageController] Restored parent scene after child return: " +
            parent);
    }

    private void HandleBackClicked()
    {
        if (pdfViewerOpen || currentPdfAsset != null)
        {
            ClosePdfViewer();
            return;
        }

        string previous =
            PlayerPrefs.GetString(
                PreviousSceneKey,
                previousSceneName);

        // Defensive recovery: an AR/3D child scene may have left previous_scene
        // as ShowLessonScene. Never let the header Back button reload the same scene.
        if (string.IsNullOrWhiteSpace(previous) ||
            string.Equals(
                previous,
                SceneManager.GetActiveScene().name,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                previous,
                "ShowLessonScene",
                StringComparison.OrdinalIgnoreCase))
        {
            string preservedParent =
                PlayerPrefs.GetString(
                    ShowLessonParentSceneKey,
                    previousSceneName);

            previous =
                string.IsNullOrWhiteSpace(preservedParent)
                    ? previousSceneName
                    : preservedParent;
        }

        // Do not keep stale child-return state once the user leaves ShowLessonScene.
        PlayerPrefs.DeleteKey(ShowLessonParentSceneKey);
        PlayerPrefs.SetString(PreviousSceneKey, previous);
        PlayerPrefs.Save();

        LoadSceneSafely(previous);
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
        // The next Update() recalculates the correct scrolled video rectangle
        // before showing the native YouTube WebView again.
        nativeVideoWebViewVisible = false;
        nativeVideoWebViewVisibilityKnown = false;

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
    private sealed class ClassModelSource
    {
        public LessonAssetView asset;
        public string lessonTitle;
        public int chapterOrder;
        public bool isCurrentLesson;
    }

    [Serializable]
    private class ModelLaunchManifest
    {
        public string class_id;
        public string lesson_id;
        public string mode;
        public ModelLaunchItem[] models;
    }

    [Serializable]
    private class ModelLaunchItem
    {
        public string asset_id;
        public string lesson_id;
        public string lesson_title;
        public int chapter_order;
        public string name;
        public string file_name;
        public string bucket;
        public string storage_path;
        public string url;
        public string fallback_url;
        public int display_order;
    }

    private class AssetUrlCandidate
    {
        public string url;
        public bool applySupabaseAuth;
        public string description;
    }

    [Serializable]
    private class R2SignedUrlRequest
    {
        public string asset_id;
        public string bucket;
        public string key;
        public string file_name;
    }

    [Serializable]
    private class R2SignedUrlResponse
    {
        public string url;
        public string signed_url;
        public string signedURL;
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
public class ClassChapterView
{
    public string id;
    public string title;
    public int chapter_order;
}

[Serializable]
public class ClassChapterViewList
{
    public ClassChapterView[] items;
}

[Serializable]
public class ClassLessonView
{
    public string id;
    public string chapter_id;
    public string title;
    public string created_at;
}

[Serializable]
public class ClassLessonViewList
{
    public ClassLessonView[] items;
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

    // Optional URL columns used by R2 / external-storage backends.
    // JsonUtility simply leaves them null when the columns are not present.
    public string file_url;
    public string public_url;
    public string signed_url;
    public string r2_url;
    public string object_url;
    public string url;

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

[Serializable]
public class QuizMetadataView
{
    public string id;
    public string lesson_id;
    public string title;
    public int total_questions;
    public float max_score;
    public string opens_at;
    public string closes_at;
    public bool is_published;
    public string source_asset_id;
}

[Serializable]
public class QuizMetadataViewList
{
    public QuizMetadataView[] items;
}

