using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UIToolkitButton = UnityEngine.UIElements.Button;

/// <summary>
/// UI Toolkit controller for VRClassroomScene.
///
/// Keeps the old uGUI FixedJoystick usable by making full-screen UI Toolkit
/// containers non-pickable. Only actual controls and the open model browser
/// receive pointer input.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class VRPageController : MonoBehaviour
{
    [Header("Navigation")]
    [SerializeField] private string fallbackPreviousScene = "ShowLessonScene";

    [Header("Runtime model catalog")]
    [SerializeField] private VRRuntimeModelCatalog modelCatalog;

    [Header("AI model detail mode")]
    [SerializeField] private VRModelDetailService detailService;
    [SerializeField] private VRModelDetailAnchorController detailAnchorController;

    [Tooltip("Horizontal UI distance between the projected 3D anchor and its label.")]
    [SerializeField, Range(45f, 180f)]
    private float detailLabelHorizontalOffset = 92f;

    [Tooltip("Minimum vertical separation between labels placed on the same side.")]
    [SerializeField, Range(22f, 80f)]
    private float detailLabelVerticalSpacing = 38f;

    [Tooltip("Screen padding used when clamping labels.")]
    [SerializeField, Range(4f, 40f)]
    private float detailLabelScreenPadding = 14f;

    [Header("Player start pose")]
    [SerializeField]
    private bool spawnAtBackFacingFront = true;

    [Tooltip("Distance kept from the back edge of the classroom floor.")]
    [SerializeField, Range(0.3f, 3f)]
    private float backWallInset = 1.1f;

    [Tooltip("Used only if no Teacher/Board/Front object can be detected. Usually +Z or -Z.")]
    [SerializeField]
    private Vector3 fallbackFrontDirection = Vector3.forward;

    [Header("Joystick")]
    [Tooltip("Fallback scale if the AI button geometry is not available.")]
    [SerializeField, Range(0.45f, 1f)]
    private float joystickFallbackScale = 0.70f;

    [Tooltip("Mirror the AI button's bottom/right spacing onto the joystick at bottom-left.")]
    [SerializeField]
    private bool alignJoystickWithAIButton = true;

    [Header("Startup stabilization")]
    [Tooltip("Minimum time to keep the loading screen visible when entering VR.")]
    [SerializeField, Min(0.1f)]
    private float minimumStartupLoadingSeconds = 0.75f;

    [Tooltip("Extra settle time after the first GLB model has finished loading.")]
    [SerializeField, Min(0f)]
    private float settleAfterModelLoadedSeconds = 0.30f;

    [Tooltip("Snap the Player/CharacterController onto the classroom floor before movement is enabled.")]
    [SerializeField]
    private bool snapPlayerToFloorOnStartup = true;

    [Tooltip("Small clearance between CharacterController bottom and the floor.")]
    [SerializeField, Range(0.001f, 0.15f)]
    private float floorClearance = 0.03f;

    private const string PreviousSceneKey = "previous_scene";
    private const string HiddenClass = "hidden";
    private const string MenuHiddenClass = "menu-hidden";

    private UIDocument uiDocument;
    private VisualElement root;
    private UIManager uiManager;

    private UIToolkitButton backButton;
    private UIToolkitButton menuButton;
    private UIToolkitButton modelsButton;
    private UIToolkitButton visibilityButton;
    private UIToolkitButton rotateButton;
    private UIToolkitButton detailsButton;

    private VisualElement visibilityIcon;
    private VisualElement rotateIcon;

    private VisualElement browserOverlay;
    private VisualElement browserPanel;
    private UIToolkitButton browserBackButton;
    private UIToolkitButton closeBrowserButton;
    private Label browserTitle;
    private Label browserSubtitle;
    private ScrollView lessonList;
    private ScrollView modelList;
    private Label loadingLabel;

    private VisualElement startupLoadingOverlay;
    private VisualElement startupSpinner;
    private Label startupLoadingText;

    // AI chat UI
    private UIToolkitButton aiChatButton;
    private UIToolkitButton closeAIChatButton;
    private UIToolkitButton sendAIChatButton;
    private VisualElement aiChatOverlay;
    private VisualElement aiChatPanel;
    private ScrollView aiChatMessages;
    private VisualElement aiChatMessageContainer;
    private TextField aiChatInput;
    private Label aiChatTyping;
    private Label aiChatContext;

    // AI model detail UI
    private VisualElement detailConnectorLayer;
    private VisualElement detailLabelsLayer;
    private VisualElement detailPopupOverlay;
    private VisualElement detailPopupPanel;
    private UIToolkitButton closeDetailPopupButton;
    private ScrollView detailPopupScroll;
    private Label detailPopupTitle;
    private Label detailPopupConfidence;
    private Label detailPopupDescription;
    private Label detailPopupStructure;
    private Label detailPopupFunction;
    private Label detailStructureHeading;
    private Label detailFunctionHeading;

    private bool detailModeEnabled;

    // True while a modal UI surface should own pointer/drag input.
    // Runtime 3D interaction scripts can read this to avoid reacting
    // to mouse/touch input that belongs to the popup.
    public static bool IsWorldInputBlocked { get; private set; }

    private bool detailInputStateCaptured;
    private bool detailPlayerMovementWasEnabled;
    private bool detailCameraLookWasEnabled;
    private bool detailJoystickWasActive;

    private readonly Dictionary<string, UIToolkitButton>
        detailPartLabels =
            new Dictionary<string, UIToolkitButton>();

    private readonly Dictionary<string, Vector2>
        detailLabelCenters =
            new Dictionary<string, Vector2>();

    private readonly Dictionary<string, Vector2>
        detailAnchorPanelPositions =
            new Dictionary<string, Vector2>();

    // Legacy uGUI joystick
    private GameObject fixedJoystickObject;
    private RectTransform fixedJoystickRect;
    private Vector3 fixedJoystickOriginalScale = Vector3.one;

    private CharacterController playerCharacterController;
    private MonoBehaviour playerMovementBehaviour;
    private MonoBehaviour cameraLookBehaviour;

    // Old VR scene still contains a world-space PlacementIndicator
    // (GameObject "Cylinder"). It is the grey object that can appear
    // around the center crosshair. The new runtime model workflow does
    // not use this object, so keep it disabled.
    private GameObject legacyPlacementIndicator;

    private bool startupStabilizing;
    private bool firstModelReady;
    private float startupStartedAt;
    private float spinnerAngle;

    private bool menuOpen;
    private VRLessonGroup openLessonGroup;

    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogError(
                "[VRPageController] UIDocument is missing.");
            return;
        }

        root = uiDocument.rootVisualElement;

        startupStartedAt = Time.unscaledTime;
        firstModelReady = false;
        startupStabilizing = true;

        ResolvePlayerStartupComponents();
        ResolveJoystick();
        FreezePlayerForStartup();

        ResolveAndHideLegacyPlacementIndicator();

#if UNITY_2023_1_OR_NEWER
        uiManager =
            FindFirstObjectByType<UIManager>(
                FindObjectsInactive.Include);
#else
        uiManager =
            FindObjectOfType<UIManager>(true);
#endif

        uiManager?.SetLegacyToolbarVisible(false);
        uiManager?.HideAllLegacyToolbarButtons();

        ResolveCatalog();
        ResolveDetailControllers();
        QueryUI();
        ShowStartupLoading("Loading VR classroom...");
        ConfigurePickingModes();
        RegisterEvents();
        RegisterCatalogEvents();
        RegisterDetailEvents();

        SetMenuOpen(false);
        CloseBrowser();
        CloseAIChat();
        CloseDetailPopup();
        SetDetailMode(false);
        StartCoroutine(AlignJoystickToAIButtonWhenReady());

        RefreshVisibilityIcon(
            modelCatalog == null ||
            modelCatalog.ModelVisible);

        RefreshRotateIcon(
            modelCatalog != null &&
            modelCatalog.AutoRotateEnabled);

        RefreshLoadingLabel(
            modelCatalog != null &&
            modelCatalog.IsLoading,
            modelCatalog != null &&
            modelCatalog.IsLoading
                ? "Loading 3D model..."
                : string.Empty);
    }

    private void Update()
    {
        if (startupStabilizing &&
            startupSpinner != null)
        {
            spinnerAngle -=
                260f * Time.unscaledDeltaTime;

            startupSpinner.transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    spinnerAngle);
        }

        // Keep the detail popup ScrollView inside its real scroll range.
        // In Device Simulator, dragging short content can otherwise pull
        // the content downward even when there is nothing to scroll.
        ClampDetailPopupScrollOffset();
    }

    private void LateUpdate()
    {
        // Legacy scene scripts can re-enable old Canvas controls.
        uiManager?.HideAllLegacyToolbarButtons();

        // The old ModelSpawner still references a PlacementIndicator
        // ("Cylinder"). Suppress it every frame so the grey platform/cylinder
        // never appears below the pink crosshair.
        HideLegacyPlacementIndicator();

        if (detailModeEnabled)
            UpdateDetailLabelPositions();

        // During startup, movement stays frozen even if another script
        // tries to re-enable the Player controller.
        if (startupStabilizing)
        {
            if (playerMovementBehaviour != null)
                playerMovementBehaviour.enabled = false;

            if (cameraLookBehaviour != null)
                cameraLookBehaviour.enabled = false;
        }
    }

    private void OnDisable()
    {
        IsWorldInputBlocked = false;
        detailInputStateCaptured = false;

        startupStabilizing = false;
        UnfreezePlayerAfterStartup();

        UnregisterEvents();
        UnregisterCatalogEvents();
        UnregisterDetailEvents();

        if (detailConnectorLayer != null)
        {
            detailConnectorLayer.generateVisualContent -=
                DrawDetailConnectors;
        }

        ClearDetailLabels();
    }

    private void ResolveCatalog()
    {
#if UNITY_2023_1_OR_NEWER
        if (modelCatalog == null)
        {
            modelCatalog =
                FindFirstObjectByType<VRRuntimeModelCatalog>(
                    FindObjectsInactive.Include);
        }
#else
        if (modelCatalog == null)
            modelCatalog = FindObjectOfType<VRRuntimeModelCatalog>(true);
#endif

        if (modelCatalog == null)
        {
            // No manual Inspector setup is required.
            modelCatalog =
                gameObject.AddComponent<VRRuntimeModelCatalog>();
        }
    }

    private void QueryUI()
    {
        backButton =
            root.Q<UIToolkitButton>("BtnBack");

        menuButton =
            root.Q<UIToolkitButton>("BtnMenu");

        modelsButton =
            root.Q<UIToolkitButton>("BtnModels");

        visibilityButton =
            root.Q<UIToolkitButton>("BtnVisibility");

        rotateButton =
            root.Q<UIToolkitButton>("BtnRotate");

        detailsButton =
            root.Q<UIToolkitButton>("BtnDetails");

        visibilityIcon =
            root.Q<VisualElement>("VisibilityIcon");

        rotateIcon =
            root.Q<VisualElement>("RotateIcon");

        browserOverlay =
            root.Q<VisualElement>("ModelBrowserOverlay");

        browserPanel =
            root.Q<VisualElement>("ModelBrowserPanel");

        browserBackButton =
            root.Q<UIToolkitButton>("BtnBrowserBack");

        closeBrowserButton =
            root.Q<UIToolkitButton>("BtnCloseBrowser");

        browserTitle =
            root.Q<Label>("BrowserTitle");

        browserSubtitle =
            root.Q<Label>("BrowserSubtitle");

        lessonList =
            root.Q<ScrollView>("LessonList");

        modelList =
            root.Q<ScrollView>("ModelList");

        loadingLabel =
            root.Q<Label>("ModelLoadingLabel");

        startupLoadingOverlay =
            root.Q<VisualElement>("StartupLoadingOverlay");

        startupSpinner =
            root.Q<VisualElement>("StartupSpinner");

        startupLoadingText =
            root.Q<Label>("StartupLoadingText");

        aiChatButton =
            root.Q<UIToolkitButton>("BtnAIChat");

        closeAIChatButton =
            root.Q<UIToolkitButton>("BtnCloseAIChat");

        sendAIChatButton =
            root.Q<UIToolkitButton>("BtnSendAIChat");

        aiChatOverlay =
            root.Q<VisualElement>("AIChatOverlay");

        aiChatPanel =
            root.Q<VisualElement>("AIChatPanel");

        aiChatMessages =
            root.Q<ScrollView>("AIChatMessages");

        aiChatMessageContainer =
            root.Q<VisualElement>("AIChatMessageContainer");

        aiChatInput =
            root.Q<TextField>("AIChatInput");

        aiChatTyping =
            root.Q<Label>("AIChatTyping");

        aiChatContext =
            root.Q<Label>("AIChatContext");

        detailConnectorLayer =
            root.Q<VisualElement>("DetailConnectorLayer");

        detailLabelsLayer =
            root.Q<VisualElement>("DetailLabelsLayer");

        detailPopupOverlay =
            root.Q<VisualElement>("DetailPopupOverlay");

        detailPopupPanel =
            root.Q<VisualElement>("DetailPopupPanel");

        closeDetailPopupButton =
            root.Q<UIToolkitButton>("BtnCloseDetailPopup");

        detailPopupScroll =
            root.Q<ScrollView>("DetailPopupScroll");

        if (detailPopupScroll != null)
        {
            // Prevent elastic overscroll, which was causing the content
            // to be dragged far down and leave a large blank area.
            detailPopupScroll.touchScrollBehavior =
                ScrollView.TouchScrollBehavior.Clamped;

            detailPopupScroll.verticalScrollerVisibility =
                ScrollerVisibility.Auto;

            detailPopupScroll.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;

            detailPopupScroll.mouseWheelScrollSize = 38f;
        }

        detailPopupTitle =
            root.Q<Label>("DetailPopupTitle");

        detailPopupConfidence =
            root.Q<Label>("DetailPopupConfidence");

        detailPopupDescription =
            root.Q<Label>("DetailPopupDescription");

        detailPopupStructure =
            root.Q<Label>("DetailPopupStructure");

        detailPopupFunction =
            root.Q<Label>("DetailPopupFunction");

        detailStructureHeading =
            root.Q<Label>("DetailStructureHeading");

        detailFunctionHeading =
            root.Q<Label>("DetailFunctionHeading");

        if (detailConnectorLayer != null)
        {
            detailConnectorLayer.generateVisualContent -=
                DrawDetailConnectors;

            detailConnectorLayer.generateVisualContent +=
                DrawDetailConnectors;
        }

        if (lessonList != null)
            lessonList.verticalScrollerVisibility =
                ScrollerVisibility.Hidden;

        if (modelList != null)
            modelList.verticalScrollerVisibility =
                ScrollerVisibility.Hidden;
    }

    private void ConfigurePickingModes()
    {
        // Critical: these full-screen containers must not block the old
        // uGUI FixedJoystick underneath the UIDocument.
        root.pickingMode = PickingMode.Ignore;

        VisualElement vrRoot =
            root.Q<VisualElement>("VRRoot");

        if (vrRoot != null)
            vrRoot.pickingMode = PickingMode.Ignore;

        VisualElement menuRoot =
            root.Q<VisualElement>("VerticalMenuRoot");

        if (menuRoot != null)
            menuRoot.pickingMode = PickingMode.Ignore;

        if (visibilityIcon != null)
            visibilityIcon.pickingMode = PickingMode.Ignore;

        if (rotateIcon != null)
            rotateIcon.pickingMode = PickingMode.Ignore;

        if (loadingLabel != null)
            loadingLabel.pickingMode = PickingMode.Ignore;

        if (startupSpinner != null)
            startupSpinner.pickingMode = PickingMode.Ignore;

        if (startupLoadingText != null)
            startupLoadingText.pickingMode = PickingMode.Ignore;

        if (startupLoadingOverlay != null)
            startupLoadingOverlay.pickingMode = PickingMode.Position;

        SetPickable(backButton, true);
        SetPickable(menuButton, true);
        SetPickable(modelsButton, true);
        SetPickable(visibilityButton, true);
        SetPickable(rotateButton, true);
        SetPickable(detailsButton, true);
        SetPickable(aiChatButton, true);

        if (detailConnectorLayer != null)
            detailConnectorLayer.pickingMode = PickingMode.Ignore;

        if (detailLabelsLayer != null)
            detailLabelsLayer.pickingMode = PickingMode.Ignore;

        SetBrowserPicking(false);
        SetAIChatPicking(false);
        SetDetailPopupPicking(false);
    }

    private static void SetPickable(
        VisualElement element,
        bool value)
    {
        if (element == null)
            return;

        element.pickingMode =
            value
                ? PickingMode.Position
                : PickingMode.Ignore;
    }

    private void SetBrowserPicking(bool open)
    {
        SetPickable(browserOverlay, open);
        SetPickable(browserPanel, open);
        SetPickable(browserBackButton, open);
        SetPickable(closeBrowserButton, open);
        SetPickable(lessonList, open);
        SetPickable(modelList, open);
    }

    private void SetAIChatPicking(bool open)
    {
        SetPickable(aiChatOverlay, open);
        SetPickable(aiChatPanel, open);
        SetPickable(closeAIChatButton, open);
        SetPickable(sendAIChatButton, open);
        SetPickable(aiChatMessages, open);
        SetPickable(aiChatInput, open);
    }

    private void SetDetailPopupPicking(bool open)
    {
        SetPickable(detailPopupOverlay, open);
        SetPickable(detailPopupPanel, open);
        SetPickable(closeDetailPopupButton, open);
        SetPickable(detailPopupScroll, open);
    }


    private void RegisterEvents()
    {
        if (backButton != null)
            backButton.clicked += HandleBack;

        if (menuButton != null)
            menuButton.clicked += ToggleMenu;

        if (modelsButton != null)
            modelsButton.clicked += OpenLessonBrowser;

        if (visibilityButton != null)
            visibilityButton.clicked += ToggleModelVisibility;

        if (rotateButton != null)
            rotateButton.clicked += ToggleAutoRotate;

        if (detailsButton != null)
            detailsButton.clicked += ToggleDetailMode;

        if (closeDetailPopupButton != null)
            closeDetailPopupButton.clicked += CloseDetailPopup;

        if (detailPopupOverlay != null)
        {
            detailPopupOverlay.RegisterCallback<PointerDownEvent>(
                StopDetailPopupPointerEvent);
            detailPopupOverlay.RegisterCallback<PointerMoveEvent>(
                StopDetailPopupPointerEvent);
            detailPopupOverlay.RegisterCallback<PointerUpEvent>(
                StopDetailPopupPointerEvent);
            detailPopupOverlay.RegisterCallback<WheelEvent>(
                StopDetailPopupWheelEvent);
        }

        // Intercept drag/wheel input BEFORE ScrollView's default drag handling.
        // If the popup content fits completely inside the viewport, there is
        // nothing to scroll, so the gesture must be consumed immediately.
        // This removes the one-frame "bounce/jump" that happened when Update()
        // corrected the offset only after ScrollView had already moved it.
        if (detailPopupScroll != null)
        {
            detailPopupScroll.RegisterCallback<PointerDownEvent>(
                GuardDetailPopupScrollPointerDown,
                TrickleDown.TrickleDown);

            detailPopupScroll.RegisterCallback<PointerMoveEvent>(
                GuardDetailPopupScrollPointerMove,
                TrickleDown.TrickleDown);

            detailPopupScroll.RegisterCallback<PointerUpEvent>(
                GuardDetailPopupScrollPointerUp,
                TrickleDown.TrickleDown);

            detailPopupScroll.RegisterCallback<WheelEvent>(
                GuardDetailPopupScrollWheel,
                TrickleDown.TrickleDown);
        }

        if (browserBackButton != null)
            browserBackButton.clicked += ShowLessonBrowser;

        if (closeBrowserButton != null)
            closeBrowserButton.clicked += CloseBrowser;

        if (aiChatButton != null)
            aiChatButton.clicked += OpenAIChat;

        if (closeAIChatButton != null)
            closeAIChatButton.clicked += CloseAIChat;

        if (sendAIChatButton != null)
            sendAIChatButton.clicked += SendAIChatMessage;

        if (aiChatInput != null)
            aiChatInput.RegisterCallback<KeyDownEvent>(HandleAIChatKeyDown);
    }

    private void UnregisterEvents()
    {
        if (backButton != null)
            backButton.clicked -= HandleBack;

        if (menuButton != null)
            menuButton.clicked -= ToggleMenu;

        if (modelsButton != null)
            modelsButton.clicked -= OpenLessonBrowser;

        if (visibilityButton != null)
            visibilityButton.clicked -= ToggleModelVisibility;

        if (rotateButton != null)
            rotateButton.clicked -= ToggleAutoRotate;

        if (detailsButton != null)
            detailsButton.clicked -= ToggleDetailMode;

        if (closeDetailPopupButton != null)
            closeDetailPopupButton.clicked -= CloseDetailPopup;

        if (detailPopupOverlay != null)
        {
            detailPopupOverlay.UnregisterCallback<PointerDownEvent>(
                StopDetailPopupPointerEvent);
            detailPopupOverlay.UnregisterCallback<PointerMoveEvent>(
                StopDetailPopupPointerEvent);
            detailPopupOverlay.UnregisterCallback<PointerUpEvent>(
                StopDetailPopupPointerEvent);
            detailPopupOverlay.UnregisterCallback<WheelEvent>(
                StopDetailPopupWheelEvent);
        }

        if (detailPopupScroll != null)
        {
            detailPopupScroll.UnregisterCallback<PointerDownEvent>(
                GuardDetailPopupScrollPointerDown,
                TrickleDown.TrickleDown);

            detailPopupScroll.UnregisterCallback<PointerMoveEvent>(
                GuardDetailPopupScrollPointerMove,
                TrickleDown.TrickleDown);

            detailPopupScroll.UnregisterCallback<PointerUpEvent>(
                GuardDetailPopupScrollPointerUp,
                TrickleDown.TrickleDown);

            detailPopupScroll.UnregisterCallback<WheelEvent>(
                GuardDetailPopupScrollWheel,
                TrickleDown.TrickleDown);
        }

        if (browserBackButton != null)
            browserBackButton.clicked -= ShowLessonBrowser;

        if (closeBrowserButton != null)
            closeBrowserButton.clicked -= CloseBrowser;

        if (aiChatButton != null)
            aiChatButton.clicked -= OpenAIChat;

        if (closeAIChatButton != null)
            closeAIChatButton.clicked -= CloseAIChat;

        if (sendAIChatButton != null)
            sendAIChatButton.clicked -= SendAIChatMessage;

        if (aiChatInput != null)
            aiChatInput.UnregisterCallback<KeyDownEvent>(HandleAIChatKeyDown);
    }

    private void RegisterCatalogEvents()
    {
        if (modelCatalog == null)
            return;

        modelCatalog.CatalogReady += HandleCatalogReady;
        modelCatalog.ModelChanged += HandleModelChanged;
        modelCatalog.VisibilityChanged += RefreshVisibilityIcon;
        modelCatalog.AutoRotateChanged += RefreshRotateIcon;
        modelCatalog.LoadingStateChanged += RefreshLoadingLabel;
    }

    private void UnregisterCatalogEvents()
    {
        if (modelCatalog == null)
            return;

        modelCatalog.CatalogReady -= HandleCatalogReady;
        modelCatalog.ModelChanged -= HandleModelChanged;
        modelCatalog.VisibilityChanged -= RefreshVisibilityIcon;
        modelCatalog.AutoRotateChanged -= RefreshRotateIcon;
        modelCatalog.LoadingStateChanged -= RefreshLoadingLabel;
    }

    // =========================================================
    // Main right-side menu
    // =========================================================

    private void ToggleMenu()
    {
        SetMenuOpen(!menuOpen);
    }

    private void SetMenuOpen(bool open)
    {
        menuOpen = open;

        SetActionVisible(modelsButton, open);
        SetActionVisible(visibilityButton, open);
        SetActionVisible(rotateButton, open);
        SetActionVisible(detailsButton, open);
    }

    private static void SetActionVisible(
        VisualElement element,
        bool visible)
    {
        if (element == null)
            return;

        if (visible)
        {
            element.RemoveFromClassList(
                MenuHiddenClass);
        }
        else if (!element.ClassListContains(
                     MenuHiddenClass))
        {
            element.AddToClassList(
                MenuHiddenClass);
        }
    }

    // =========================================================
    // Navigation
    // =========================================================

    private void HandleBack()
    {
        string previousScene =
            PlayerPrefs.GetString(
                PreviousSceneKey,
                fallbackPreviousScene);

        if (string.IsNullOrWhiteSpace(previousScene) ||
            string.Equals(
                previousScene,
                gameObject.scene.name,
                StringComparison.OrdinalIgnoreCase))
        {
            previousScene =
                fallbackPreviousScene;
        }

        if (!Application.CanStreamedLevelBeLoaded(
                previousScene))
        {
            Debug.LogError(
                $"[VRPageController] Scene '{previousScene}' " +
                "is not enabled in Build Profiles.");
            return;
        }

        SceneManager.LoadScene(previousScene);
    }

    // =========================================================
    // Model visibility / auto rotation
    // =========================================================

    private void ToggleModelVisibility()
    {
        if (modelCatalog == null ||
            modelCatalog.CurrentModel == null)
        {
            Debug.LogWarning(
                "[VRPageController] No loaded lesson model to hide/show.");
            return;
        }

        modelCatalog.ToggleVisibility();

        // Do NOT close the three action buttons here.
    }

    private void ToggleAutoRotate()
    {
        if (modelCatalog == null ||
            modelCatalog.CurrentModel == null)
        {
            Debug.LogWarning(
                "[VRPageController] No loaded lesson model to rotate.");
            return;
        }

        // Tap once: start automatic rotation.
        // Tap again: stop rotation.
        modelCatalog.ToggleAutoRotate();

        // Keep the action menu open.
    }

    private void RefreshVisibilityIcon(bool visible)
    {
        if (visibilityIcon == null)
            return;

        visibilityIcon.RemoveFromClassList("eye-icon");
        visibilityIcon.RemoveFromClassList("eye-off-icon");

        visibilityIcon.AddToClassList(
            visible
                ? "eye-icon"
                : "eye-off-icon");
    }

    private void RefreshRotateIcon(bool enabled)
    {
        if (rotateIcon == null)
            return;

        rotateIcon.RemoveFromClassList("rotate-icon");
        rotateIcon.RemoveFromClassList("no-rotate-icon");

        rotateIcon.AddToClassList(
            enabled
                ? "rotate-icon"
                : "no-rotate-icon");
    }

    // =========================================================
    // Lesson -> model browser
    // =========================================================

    private void OpenLessonBrowser()
    {
        if (browserOverlay == null)
            return;

        browserOverlay.RemoveFromClassList(
            HiddenClass);

        browserOverlay.BringToFront();
        SetBrowserPicking(true);

        ShowLessonBrowser();

        // Keep the top-right menu open. The browser is a separate overlay.
    }

    private void CloseBrowser()
    {
        openLessonGroup = null;

        if (browserOverlay != null &&
            !browserOverlay.ClassListContains(
                HiddenClass))
        {
            browserOverlay.AddToClassList(
                HiddenClass);
        }

        SetBrowserPicking(false);
    }

    private void ShowLessonBrowser()
    {
        openLessonGroup = null;

        if (lessonList == null ||
            modelList == null)
        {
            return;
        }

        lessonList.Clear();
        modelList.Clear();

        lessonList.RemoveFromClassList(
            HiddenClass);

        if (!modelList.ClassListContains(
                HiddenClass))
        {
            modelList.AddToClassList(
                HiddenClass);
        }

        SetVisible(
            browserBackButton,
            false);

        if (browserTitle != null)
            browserTitle.text = "3D Models";

        if (browserSubtitle != null)
            browserSubtitle.text =
                "Choose a lesson to see its models";

        if (modelCatalog == null ||
            !modelCatalog.HasModels)
        {
            AddEmptyRow(
                lessonList,
                "No 3D models are available for this class.");
            return;
        }

        List<VRLessonGroup> groups =
            modelCatalog.BuildLessonGroups();

        string currentLessonId =
            modelCatalog.Manifest?.lesson_id ??
            PlayerPrefs.GetString(
                "selected_lesson_id",
                string.Empty);

        foreach (VRLessonGroup group in groups)
        {
            if (group == null)
                continue;

            UIToolkitButton row =
                new UIToolkitButton();

            row.AddToClassList(
                "browser-row");

            bool isCurrent =
                !string.IsNullOrWhiteSpace(
                    currentLessonId) &&
                string.Equals(
                    group.lesson_id,
                    currentLessonId,
                    StringComparison.OrdinalIgnoreCase);

            if (isCurrent)
            {
                row.AddToClassList(
                    "browser-row-current");
            }

            VisualElement info =
                new VisualElement();

            info.AddToClassList(
                "browser-row-info");

            string chapterText =
                group.chapter_order > 0
                    ? $"Chapter {group.chapter_order}"
                    : "Lesson";

            Label eyebrow =
                new Label(
                    isCurrent
                        ? chapterText + " • Current lesson"
                        : chapterText);

            eyebrow.AddToClassList(
                "browser-row-eyebrow");

            Label title =
                new Label(
                    string.IsNullOrWhiteSpace(
                        group.lesson_title)
                        ? "Lesson"
                        : group.lesson_title);

            title.AddToClassList(
                "browser-row-title");

            Label count =
                new Label(
                    $"{group.model_indices.Count} model" +
                    (group.model_indices.Count == 1
                        ? string.Empty
                        : "s"));

            count.AddToClassList(
                "browser-row-count");

            info.Add(eyebrow);
            info.Add(title);

            row.Add(info);
            row.Add(count);

            VRLessonGroup captured = group;

            row.clicked += () =>
                ShowModelsForLesson(
                    captured);

            lessonList.Add(row);
        }
    }

    private void ShowModelsForLesson(
        VRLessonGroup group)
    {
        if (group == null ||
            lessonList == null ||
            modelList == null ||
            modelCatalog == null)
        {
            return;
        }

        openLessonGroup = group;

        lessonList.AddToClassList(
            HiddenClass);

        modelList.RemoveFromClassList(
            HiddenClass);

        modelList.Clear();

        SetVisible(
            browserBackButton,
            true);

        if (browserTitle != null)
        {
            browserTitle.text =
                string.IsNullOrWhiteSpace(
                    group.lesson_title)
                    ? "Lesson Models"
                    : group.lesson_title;
        }

        if (browserSubtitle != null)
        {
            browserSubtitle.text =
                group.chapter_order > 0
                    ? $"Chapter {group.chapter_order} • Select a model"
                    : "Select a model";
        }

        if (group.model_indices.Count == 0)
        {
            AddEmptyRow(
                modelList,
                "This lesson has no 3D models.");
            return;
        }

        foreach (int modelIndex
                 in group.model_indices)
        {
            if (modelIndex < 0 ||
                modelIndex >= modelCatalog.Models.Count)
            {
                continue;
            }

            VRModelLaunchItem record =
                modelCatalog.Models[modelIndex];

            UIToolkitButton row =
                new UIToolkitButton();

            row.AddToClassList(
                "model-row");

            if (modelIndex ==
                modelCatalog.CurrentModelIndex)
            {
                row.AddToClassList(
                    "model-row-selected");
            }

            VisualElement cube =
                new VisualElement();

            cube.AddToClassList(
                "model-row-icon");

            VisualElement info =
                new VisualElement();

            info.AddToClassList(
                "browser-row-info");

            Label title =
                new Label(
                    VRRuntimeModelCatalog
                        .GetDisplayName(record));

            title.AddToClassList(
                "browser-row-title");

            Label subtitle =
                new Label(
                    string.IsNullOrWhiteSpace(
                        record.file_name)
                        ? "3D model"
                        : record.file_name);

            subtitle.AddToClassList(
                "model-row-subtitle");

            info.Add(title);
            info.Add(subtitle);

            Label state =
                new Label(
                    modelIndex ==
                    modelCatalog.CurrentModelIndex
                        ? "Showing"
                        : "Open");

            state.AddToClassList(
                "model-row-action");

            row.Add(cube);
            row.Add(info);
            row.Add(state);

            int capturedIndex =
                modelIndex;

            row.clicked += () =>
            {
                if (modelCatalog.IsLoading)
                    return;

                modelCatalog.SelectModel(
                    capturedIndex);

                // Close browser after a model is chosen so the
                // user can immediately see it in the classroom.
                CloseBrowser();
            };

            modelList.Add(row);
        }
    }

    private static void AddEmptyRow(
        VisualElement parent,
        string text)
    {
        if (parent == null)
            return;

        Label empty =
            new Label(text);

        empty.AddToClassList(
            "browser-empty");

        empty.pickingMode =
            PickingMode.Ignore;

        parent.Add(empty);
    }

    private void HandleCatalogReady()
    {
        if (browserOverlay != null &&
            !browserOverlay.ClassListContains(
                HiddenClass))
        {
            ShowLessonBrowser();
        }

        if (modelCatalog != null &&
            !modelCatalog.HasModels &&
            startupStabilizing)
        {
            firstModelReady = true;
            ShowStartupLoading("Preparing classroom...");
            StartCoroutine(FinishStartupWhenStable());
        }
    }

    private void HandleModelChanged(
        int index,
        VRModelLaunchItem record,
        GameObject model)
    {
        RefreshVisibilityIcon(true);

        firstModelReady = true;

        if (startupStabilizing)
        {
            ShowStartupLoading(
                "Preparing classroom...");

            StopCoroutine(
                nameof(FinishStartupWhenStable));

            StartCoroutine(
                FinishStartupWhenStable());
        }

        Debug.Log(
            "[VRPageController] Showing lesson model in VR: " +
            VRRuntimeModelCatalog.GetDisplayName(record));

        ClearDetailLabels();

        if (detailModeEnabled)
        {
            StartCoroutine(
                RefreshDetailLabelsWhenAnchorsReady());
        }
    }

    private void RefreshLoadingLabel(
        bool isLoading,
        string message)
    {
        if (loadingLabel == null)
            return;

        loadingLabel.text =
            string.IsNullOrWhiteSpace(message)
                ? "Loading 3D model..."
                : message;

        if (isLoading)
            loadingLabel.RemoveFromClassList(
                HiddenClass);
        else if (!loadingLabel.ClassListContains(
                     HiddenClass))
            loadingLabel.AddToClassList(
                HiddenClass);
    }

    private static void SetVisible(
        VisualElement element,
        bool visible)
    {
        if (element == null)
            return;

        if (visible)
            element.RemoveFromClassList(
                HiddenClass);
        else if (!element.ClassListContains(
                     HiddenClass))
            element.AddToClassList(
                HiddenClass);
    }


    // =========================================================
    // Legacy placement indicator cleanup
    // =========================================================

    private void ResolveAndHideLegacyPlacementIndicator()
    {
        legacyPlacementIndicator = null;

#if UNITY_2023_1_OR_NEWER
        MonoBehaviour[] behaviours =
            FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
        MonoBehaviour[] behaviours =
            FindObjectsOfType<MonoBehaviour>(true);
#endif

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
                continue;

            if (!string.Equals(
                    behaviour.GetType().Name,
                    "PlacementIndicator",
                    StringComparison.Ordinal))
            {
                continue;
            }

            legacyPlacementIndicator =
                behaviour.gameObject;

            // Disable the behaviour first so it cannot move/show itself again.
            behaviour.enabled = false;

            break;
        }

        // Fallback for the exact legacy scene object.
        if (legacyPlacementIndicator == null)
        {
            GameObject cylinder =
                GameObject.Find("Cylinder");

            if (cylinder != null &&
                cylinder.GetComponent<Renderer>() != null)
            {
                legacyPlacementIndicator =
                    cylinder;
            }
        }

        HideLegacyPlacementIndicator();

        if (legacyPlacementIndicator != null)
        {
            Debug.Log(
                "[VRPageController] Legacy PlacementIndicator hidden: " +
                legacyPlacementIndicator.name);
        }
    }

    private void HideLegacyPlacementIndicator()
    {
        if (legacyPlacementIndicator == null)
            return;

        // Disable all renderers/colliders first. This also prevents the
        // invisible legacy indicator from interfering with raycasts.
        Renderer[] renderers =
            legacyPlacementIndicator
                .GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
                renderer.enabled = false;
        }

        Collider[] colliders =
            legacyPlacementIndicator
                .GetComponentsInChildren<Collider>(true);

        foreach (Collider collider in colliders)
        {
            if (collider != null)
                collider.enabled = false;
        }

        MonoBehaviour[] behaviours =
            legacyPlacementIndicator
                .GetComponents<MonoBehaviour>();

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
                continue;

            if (string.Equals(
                    behaviour.GetType().Name,
                    "PlacementIndicator",
                    StringComparison.Ordinal))
            {
                behaviour.enabled = false;
            }
        }

        if (legacyPlacementIndicator.activeSelf)
            legacyPlacementIndicator.SetActive(false);
    }


    // =========================================================
    // Joystick sizing + alignment
    // =========================================================

    private void ResolveJoystick()
    {
#if UNITY_2023_1_OR_NEWER
        MonoBehaviour[] behaviours =
            FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
        MonoBehaviour[] behaviours =
            FindObjectsOfType<MonoBehaviour>(true);
#endif

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
                continue;

            if (!string.Equals(
                    behaviour.GetType().Name,
                    "FixedJoystick",
                    StringComparison.Ordinal))
            {
                continue;
            }

            fixedJoystickObject = behaviour.gameObject;
            fixedJoystickRect =
                fixedJoystickObject.transform as RectTransform;

            fixedJoystickOriginalScale =
                fixedJoystickObject.transform.localScale;

            // Apply a safe fallback immediately. A later coroutine will match
            // the exact visual size/vertical center of BtnAIChat.
            fixedJoystickObject.transform.localScale =
                fixedJoystickOriginalScale *
                Mathf.Clamp(
                    joystickFallbackScale,
                    0.45f,
                    1f);

            break;
        }
    }

    private IEnumerator AlignJoystickToAIButtonWhenReady()
    {
        if (!alignJoystickWithAIButton ||
            fixedJoystickRect == null ||
            aiChatButton == null ||
            root == null)
        {
            yield break;
        }

        // Wait for UI Toolkit and Canvas layout to settle.
        yield return null;
        yield return new WaitForEndOfFrame();
        yield return null;

        // Retry for a few frames on slower Android devices.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (TryAlignJoystickToAIButton())
                yield break;

            yield return null;
        }

        Debug.LogWarning(
            "[VRPageController] Could not align joystick to AI button. " +
            "Fallback joystick scale is being used.");
    }

    private bool TryAlignJoystickToAIButton()
    {
        if (fixedJoystickRect == null ||
            aiChatButton == null ||
            root == null)
        {
            return false;
        }

        float rootWidth =
            root.resolvedStyle.width;

        float rootHeight =
            root.resolvedStyle.height;

        Rect aiBounds =
            aiChatButton.worldBound;

        if (rootWidth <= 1f ||
            rootHeight <= 1f ||
            aiBounds.width <= 1f ||
            aiBounds.height <= 1f ||
            Screen.width <= 1 ||
            Screen.height <= 1)
        {
            return false;
        }

        Canvas canvas =
            fixedJoystickRect.GetComponentInParent<Canvas>();

        if (canvas == null)
            return false;

        RectTransform canvasRect =
            canvas.transform as RectTransform;

        if (canvasRect == null)
            return false;

        Camera eventCamera =
            canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;

        // UI Toolkit uses a top-left origin. Convert its button bounds to
        // physical screen pixels, whose Y origin is bottom-left.
        float scaleX =
            Screen.width / rootWidth;

        float scaleY =
            Screen.height / rootHeight;

        float aiPhysicalWidth =
            aiBounds.width * scaleX;

        float aiPhysicalHeight =
            aiBounds.height * scaleY;

        float aiRightMargin =
            Mathf.Max(
                0f,
                Screen.width -
                aiBounds.xMax * scaleX);

        float aiBottomMargin =
            Mathf.Max(
                0f,
                Screen.height -
                aiBounds.yMax * scaleY);

        // Mirror BtnAIChat:
        // same width/height, same bottom offset, but on the LEFT side.
        Vector2 targetScreenCenter =
            new Vector2(
                aiRightMargin +
                aiPhysicalWidth * 0.5f,
                aiBottomMargin +
                aiPhysicalHeight * 0.5f);

        // First make joystick's rendered diameter equal to the AI button.
        Vector3[] corners =
            new Vector3[4];

        fixedJoystickRect.GetWorldCorners(corners);

        Vector2 bottomLeft =
            RectTransformUtility.WorldToScreenPoint(
                eventCamera,
                corners[0]);

        Vector2 topRight =
            RectTransformUtility.WorldToScreenPoint(
                eventCamera,
                corners[2]);

        float currentScreenWidth =
            Mathf.Abs(
                topRight.x -
                bottomLeft.x);

        float currentScreenHeight =
            Mathf.Abs(
                topRight.y -
                bottomLeft.y);

        float currentDiameter =
            Mathf.Max(
                currentScreenWidth,
                currentScreenHeight);

        float targetDiameter =
            Mathf.Min(
                aiPhysicalWidth,
                aiPhysicalHeight);

        if (currentDiameter > 0.5f &&
            targetDiameter > 0.5f)
        {
            float correction =
                targetDiameter /
                currentDiameter;

            fixedJoystickRect.localScale *=
                correction;
        }

        // Move its visual center to exactly the same vertical center as AI.
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                canvasRect,
                targetScreenCenter,
                eventCamera,
                out Vector3 targetWorldPoint))
        {
            return false;
        }

        fixedJoystickRect.position =
            targetWorldPoint;

        Physics.SyncTransforms();

        Debug.Log(
            "[VRPageController] Joystick aligned with AI button. " +
            $"Target diameter={targetDiameter:F1}px, " +
            $"bottom margin={aiBottomMargin:F1}px.");

        return true;
    }

    // =========================================================
    // Classroom start pose
    // =========================================================

    private void PositionPlayerAtBackFacingFront()
    {
        if (playerCharacterController == null)
            return;

        if (!TryGetClassroomFloorBounds(out Bounds floorBounds))
        {
            Debug.LogWarning(
                "[VRPageController] Could not detect classroom floor bounds. " +
                "Keeping current player position.");
            return;
        }

        Vector3 floorCenter = floorBounds.center;
        floorCenter.y = playerCharacterController.transform.position.y;

        Transform frontReference =
            FindFrontOfClassReference();

        Vector3 frontDirection;

        if (frontReference != null)
        {
            frontDirection =
                frontReference.position -
                floorBounds.center;

            frontDirection.y = 0f;
        }
        else
        {
            frontDirection = fallbackFrontDirection;
            frontDirection.y = 0f;
        }

        if (frontDirection.sqrMagnitude < 0.001f)
            frontDirection = Vector3.forward;

        frontDirection.Normalize();

        // Use the dominant floor axis so the player is not placed diagonally.
        if (Mathf.Abs(frontDirection.x) > Mathf.Abs(frontDirection.z))
        {
            frontDirection =
                new Vector3(
                    Mathf.Sign(frontDirection.x),
                    0f,
                    0f);
        }
        else
        {
            frontDirection =
                new Vector3(
                    0f,
                    0f,
                    Mathf.Sign(frontDirection.z));
        }

        float halfLength =
            Mathf.Abs(frontDirection.x) > 0.5f
                ? floorBounds.extents.x
                : floorBounds.extents.z;

        // Front is +frontDirection, so back is the opposite edge.
        Vector3 backPosition =
            floorBounds.center -
            frontDirection *
            Mathf.Max(
                0.3f,
                halfLength - backWallInset);

        backPosition.y =
            playerCharacterController.transform.position.y;

        bool controllerWasEnabled =
            playerCharacterController.enabled;

        playerCharacterController.enabled = false;

        Transform player =
            playerCharacterController.transform;

        player.position = backPosition;

        Vector3 lookPoint =
            frontReference != null
                ? frontReference.position
                : floorBounds.center +
                  frontDirection * halfLength;

        lookPoint.y = player.position.y;

        Vector3 lookDirection =
            lookPoint - player.position;

        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude > 0.001f)
        {
            player.rotation =
                Quaternion.LookRotation(
                    lookDirection.normalized,
                    Vector3.up);
        }

        playerCharacterController.enabled =
            controllerWasEnabled;

        Physics.SyncTransforms();

        Debug.Log(
            "[VRPageController] Spawned at back of classroom. " +
            $"Position={player.position}, Facing={player.forward}, " +
            $"FrontReference={(frontReference != null ? frontReference.name : "fallback")}");
    }

    private bool TryGetClassroomFloorBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

#if UNITY_2023_1_OR_NEWER
        Renderer[] renderers =
            FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
        Renderer[] renderers =
            FindObjectsOfType<Renderer>();
#endif

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            string n =
                renderer.gameObject.name ?? string.Empty;

            string parent =
                renderer.transform.parent != null
                    ? renderer.transform.parent.name
                    : string.Empty;

            bool looksLikeFloor =
                n.IndexOf(
                    "floor",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                parent.IndexOf(
                    "floor",
                    StringComparison.OrdinalIgnoreCase) >= 0;

            if (!looksLikeFloor)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private Transform FindFrontOfClassReference()
    {
#if UNITY_2023_1_OR_NEWER
        Transform[] transforms =
            FindObjectsByType<Transform>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
        Transform[] transforms =
            FindObjectsOfType<Transform>();
#endif

        string[] preferredTokens =
        {
            "teacherdesk",
            "teacher desk",
            "teacher",
            "whiteboard",
            "blackboard",
            "board",
            "podium",
            "front"
        };

        foreach (string token in preferredTokens)
        {
            foreach (Transform item in transforms)
            {
                if (item == null)
                    continue;

                string normalized =
                    (item.name ?? string.Empty)
                    .Replace("_", " ")
                    .Replace("-", " ")
                    .ToLowerInvariant();

                if (normalized.Contains(
                        token.Replace("_", " ").ToLowerInvariant()))
                {
                    return item;
                }
            }
        }

        return null;
    }

    // =========================================================
    // In-scene AI learning chat

    // =========================================================
    // AI MODEL DETAIL MODE
    // =========================================================

    private void ResolveDetailControllers()
    {
#if UNITY_2023_1_OR_NEWER
        if (detailService == null)
        {
            detailService =
                FindFirstObjectByType<VRModelDetailService>(
                    FindObjectsInactive.Include);
        }

        if (detailAnchorController == null)
        {
            detailAnchorController =
                FindFirstObjectByType<VRModelDetailAnchorController>(
                    FindObjectsInactive.Include);
        }
#else
        if (detailService == null)
            detailService = FindObjectOfType<VRModelDetailService>(true);

        if (detailAnchorController == null)
            detailAnchorController = FindObjectOfType<VRModelDetailAnchorController>(true);
#endif
    }


    private void RegisterDetailEvents()
    {
        if (detailService == null)
            return;

        detailService.OnModelPartsLoaded -=
            HandleDetailPartsLoaded;

        detailService.OnModelPartsLoaded +=
            HandleDetailPartsLoaded;
    }


    private void UnregisterDetailEvents()
    {
        if (detailService == null)
            return;

        detailService.OnModelPartsLoaded -=
            HandleDetailPartsLoaded;
    }


    private void HandleDetailPartsLoaded(
        List<VRModelDetailService.ModelPartData> parts)
    {
        if (!detailModeEnabled)
            return;

        StartCoroutine(
            RefreshDetailLabelsWhenAnchorsReady());
    }


    private void ToggleDetailMode()
    {
        SetDetailMode(
            !detailModeEnabled);
    }


    private void SetDetailMode(bool enabled)
    {
        detailModeEnabled = enabled;

        if (detailsButton != null)
        {
            if (enabled)
            {
                if (!detailsButton.ClassListContains("detail-menu-active"))
                    detailsButton.AddToClassList("detail-menu-active");
            }
            else
            {
                detailsButton.RemoveFromClassList("detail-menu-active");
            }
        }

        if (detailConnectorLayer != null)
        {
            SetVisible(
                detailConnectorLayer,
                enabled);
        }

        if (detailLabelsLayer != null)
        {
            SetVisible(
                detailLabelsLayer,
                enabled);
        }

        if (!enabled)
        {
            CloseDetailPopup();
            ClearDetailLabels();
            return;
        }

        ResolveDetailControllers();

        if (detailService == null ||
            detailAnchorController == null)
        {
            Debug.LogWarning(
                "[VRPageController] Detail Mode cannot start because " +
                "VRModelDetailService or VRModelDetailAnchorController is missing.");
            return;
        }

        if (detailService.CurrentParts == null ||
            detailService.CurrentParts.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(
                    detailService.CurrentAssetId))
            {
                detailService.LoadCurrentModelParts();
            }
            else
            {
                detailService.ResolveCurrentModelAsset();
            }

            return;
        }

        StartCoroutine(
            RefreshDetailLabelsWhenAnchorsReady());
    }


    private IEnumerator RefreshDetailLabelsWhenAnchorsReady()
    {
        const float timeout = 4f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (!detailModeEnabled)
                yield break;

            int availableAnchors =
                CountAvailableDetailAnchors();

            if (availableAnchors > 0)
            {
                BuildDetailLabels();
                yield break;
            }

            elapsed += 0.08f;
            yield return new WaitForSecondsRealtime(0.08f);
        }

        Debug.LogWarning(
            "[VRPageController] Detail labels timed out waiting for automatic AI anchors.");
    }


    private int CountAvailableDetailAnchors()
    {
        if (detailService == null ||
            detailAnchorController == null ||
            detailService.CurrentParts == null)
        {
            return 0;
        }

        int count = 0;

        foreach (VRModelDetailService.ModelPartData part
                 in detailService.CurrentParts)
        {
            if (part == null ||
                !part.is_active ||
                string.IsNullOrWhiteSpace(part.part_key))
            {
                continue;
            }

            if (detailAnchorController.TryGetAutomaticAnchor(
                    part.part_key,
                    out Transform anchor) &&
                anchor != null)
            {
                count++;
            }
        }

        return count;
    }


    private void BuildDetailLabels()
    {
        ClearDetailLabels();

        if (!detailModeEnabled ||
            detailLabelsLayer == null ||
            detailService == null ||
            detailAnchorController == null ||
            detailService.CurrentParts == null)
        {
            return;
        }

        foreach (VRModelDetailService.ModelPartData part
                 in detailService.CurrentParts)
        {
            if (part == null ||
                !part.is_active ||
                string.IsNullOrWhiteSpace(part.part_key))
            {
                continue;
            }

            if (!detailAnchorController.TryGetAutomaticAnchor(
                    part.part_key,
                    out Transform anchor) ||
                anchor == null)
            {
                continue;
            }

            string key =
                part.part_key.Trim();

            UIToolkitButton label =
                new UIToolkitButton();

            label.text =
                string.IsNullOrWhiteSpace(part.part_name)
                    ? key
                    : part.part_name.Trim();

            label.name =
                "DetailLabel_" + key;

            label.AddToClassList(
                "detail-part-label");

            label.pickingMode =
                PickingMode.Position;

            VRModelDetailService.ModelPartData capturedPart =
                part;

            label.clicked += () =>
                OpenDetailPopup(
                    capturedPart);

            detailLabelsLayer.Add(label);

            detailPartLabels[key] =
                label;
        }

        detailLabelsLayer.BringToFront();

        UpdateDetailLabelPositions();

        Debug.Log(
            "[VRPageController] Detail Mode labels ready. Count = " +
            detailPartLabels.Count);
    }


    private void ClearDetailLabels()
    {
        detailPartLabels.Clear();
        detailLabelCenters.Clear();
        detailAnchorPanelPositions.Clear();

        if (detailLabelsLayer != null)
            detailLabelsLayer.Clear();

        detailConnectorLayer?.MarkDirtyRepaint();
    }


    private void UpdateDetailLabelPositions()
    {
        if (!detailModeEnabled ||
            detailLabelsLayer == null ||
            detailService == null ||
            detailAnchorController == null ||
            root == null)
        {
            return;
        }

        Camera cam =
            Camera.main;

        if (cam == null)
            return;

        float rootWidth =
            root.resolvedStyle.width;

        float rootHeight =
            root.resolvedStyle.height;

        if (rootWidth <= 1f ||
            rootHeight <= 1f ||
            Screen.width <= 1 ||
            Screen.height <= 1)
        {
            return;
        }

        detailLabelCenters.Clear();
        detailAnchorPanelPositions.Clear();

        List<DetailPlacementEntry> left =
            new List<DetailPlacementEntry>();

        List<DetailPlacementEntry> right =
            new List<DetailPlacementEntry>();

        foreach (VRModelDetailService.ModelPartData part
                 in detailService.CurrentParts)
        {
            if (part == null ||
                string.IsNullOrWhiteSpace(part.part_key))
            {
                continue;
            }

            string key =
                part.part_key.Trim();

            if (!detailPartLabels.TryGetValue(
                    key,
                    out UIToolkitButton label) ||
                label == null)
            {
                continue;
            }

            if (!detailAnchorController.TryGetAutomaticAnchor(
                    key,
                    out Transform anchor) ||
                anchor == null)
            {
                label.style.display =
                    DisplayStyle.None;
                continue;
            }

            Vector3 screen =
                cam.WorldToScreenPoint(
                    anchor.position);

            if (screen.z <= 0f)
            {
                label.style.display =
                    DisplayStyle.None;
                continue;
            }

            float panelX =
                screen.x /
                Screen.width *
                rootWidth;

            float panelY =
                (1f -
                 screen.y /
                 Screen.height) *
                rootHeight;

            Vector2 anchorPanel =
                new Vector2(
                    panelX,
                    panelY);

            detailAnchorPanelPositions[key] =
                anchorPanel;

            DetailPlacementEntry entry =
                new DetailPlacementEntry
                {
                    key = key,
                    label = label,
                    anchorPanel = anchorPanel
                };

            if (panelX < rootWidth * 0.5f)
                left.Add(entry);
            else
                right.Add(entry);
        }

        left.Sort(
            (a, b) =>
                a.anchorPanel.y.CompareTo(
                    b.anchorPanel.y));

        right.Sort(
            (a, b) =>
                a.anchorPanel.y.CompareTo(
                    b.anchorPanel.y));

        LayoutDetailSide(
            left,
            false,
            rootWidth,
            rootHeight);

        LayoutDetailSide(
            right,
            true,
            rootWidth,
            rootHeight);

        detailConnectorLayer?.MarkDirtyRepaint();
    }


    private void DrawDetailConnectors(
        MeshGenerationContext context)
    {
        if (!detailModeEnabled ||
            detailConnectorLayer == null ||
            detailAnchorPanelPositions.Count == 0 ||
            detailLabelCenters.Count == 0)
        {
            return;
        }

        Painter2D painter =
            context.painter2D;

        painter.lineWidth =
            1.6f;

        painter.strokeColor =
            new Color(
                0.23f,
                0.48f,
                0.78f,
                0.90f);

        foreach (
            KeyValuePair<string, Vector2> pair
            in detailAnchorPanelPositions)
        {
            if (!detailLabelCenters.TryGetValue(
                    pair.Key,
                    out Vector2 labelCenter))
            {
                continue;
            }

            Vector2 anchorPoint =
                pair.Value;

            // Stop the connector slightly before the label center
            // so the line does not visually cross the label text.
            Vector2 direction =
                labelCenter -
                anchorPoint;

            float distance =
                direction.magnitude;

            if (distance < 2f)
                continue;

            direction /=
                distance;

            Vector2 lineEnd =
                labelCenter -
                direction * 28f;

            painter.BeginPath();
            painter.MoveTo(anchorPoint);
            painter.LineTo(lineEnd);
            painter.Stroke();
        }
    }


    private void LayoutDetailSide(
        List<DetailPlacementEntry> entries,
        bool rightSide,
        float rootWidth,
        float rootHeight)
    {
        if (entries == null ||
            entries.Count == 0)
        {
            return;
        }

        float lastY =
            float.NegativeInfinity;

        for (int i = 0;
             i < entries.Count;
             i++)
        {
            DetailPlacementEntry entry =
                entries[i];

            UIToolkitButton label =
                entry.label;

            if (label == null)
                continue;

            label.style.display =
                DisplayStyle.Flex;

            float width =
                label.resolvedStyle.width;

            if (width <= 1f)
                width = 128f;

            float height =
                label.resolvedStyle.height;

            if (height <= 1f)
                height = 36f;

            float centerX =
                rightSide
                    ? entry.anchorPanel.x +
                      detailLabelHorizontalOffset
                    : entry.anchorPanel.x -
                      detailLabelHorizontalOffset;

            centerX =
                Mathf.Clamp(
                    centerX,
                    detailLabelScreenPadding +
                    width * 0.5f,
                    rootWidth -
                    detailLabelScreenPadding -
                    width * 0.5f);

            float centerY =
                Mathf.Clamp(
                    entry.anchorPanel.y,
                    detailLabelScreenPadding +
                    height * 0.5f,
                    rootHeight -
                    detailLabelScreenPadding -
                    height * 0.5f);

            if (!float.IsNegativeInfinity(lastY))
            {
                centerY =
                    Mathf.Max(
                        centerY,
                        lastY +
                        detailLabelVerticalSpacing);
            }

            float maxCenterY =
                rootHeight -
                detailLabelScreenPadding -
                height * 0.5f;

            centerY =
                Mathf.Min(
                    centerY,
                    maxCenterY);

            label.style.left =
                centerX -
                width * 0.5f;

            label.style.top =
                centerY -
                height * 0.5f;

            detailLabelCenters[entry.key] =
                new Vector2(
                    centerX,
                    centerY);

            lastY = centerY;
        }
    }


    private void OpenDetailPopup(
        VRModelDetailService.ModelPartData part)
    {
        if (part == null ||
            detailPopupOverlay == null)
        {
            return;
        }

        if (detailPopupTitle != null)
        {
            detailPopupTitle.text =
                string.IsNullOrWhiteSpace(part.part_name)
                    ? "Model part"
                    : part.part_name.Trim();
        }

        if (detailPopupConfidence != null)
        {
            if (part.ai_confidence.HasValue)
            {
                detailPopupConfidence.text =
                    "AI confidence: " +
                    Mathf.RoundToInt(
                        Mathf.Clamp01(
                            part.ai_confidence.Value) *
                        100f) +
                    "%";
            }
            else
            {
                detailPopupConfidence.text =
                    string.Empty;
            }
        }

        SetDetailText(
            detailPopupDescription,
            part.description,
            "No description is available yet.");

        bool hasStructure =
            !string.IsNullOrWhiteSpace(
                part.structure_description);

        SetVisible(
            detailStructureHeading,
            hasStructure);

        SetVisible(
            detailPopupStructure,
            hasStructure);

        if (hasStructure)
        {
            detailPopupStructure.text =
                part.structure_description.Trim();
        }

        bool hasFunction =
            !string.IsNullOrWhiteSpace(
                part.function_description);

        SetVisible(
            detailFunctionHeading,
            hasFunction);

        SetVisible(
            detailPopupFunction,
            hasFunction);

        if (hasFunction)
        {
            detailPopupFunction.text =
                part.function_description.Trim();
        }

        detailPopupOverlay.RemoveFromClassList(
            HiddenClass);

        detailPopupOverlay.BringToFront();

        SetDetailPopupPicking(true);
        PauseWorldInputForDetailPopup();

        if (detailPopupScroll != null)
        {
            detailPopupScroll.scrollOffset = Vector2.zero;

            // Wait until UI Toolkit has completed layout before resetting
            // one more time. This avoids the content appearing halfway down
            // after reopening the popup.
            detailPopupScroll.schedule.Execute(
                () =>
                {
                    if (detailPopupScroll != null)
                        detailPopupScroll.scrollOffset = Vector2.zero;
                }
            ).ExecuteLater(1);
        }
    }


    private static void SetDetailText(
        Label label,
        string value,
        string fallback)
    {
        if (label == null)
            return;

        label.text =
            string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
    }


    private void CloseDetailPopup()
    {
        if (detailPopupOverlay != null &&
            !detailPopupOverlay.ClassListContains(
                HiddenClass))
        {
            detailPopupOverlay.AddToClassList(
                HiddenClass);
        }

        SetDetailPopupPicking(false);
        ResumeWorldInputAfterDetailPopup();
    }


    private void PauseWorldInputForDetailPopup()
    {
        IsWorldInputBlocked = true;

        if (!detailInputStateCaptured)
        {
            detailPlayerMovementWasEnabled =
                playerMovementBehaviour != null &&
                playerMovementBehaviour.enabled;

            detailCameraLookWasEnabled =
                cameraLookBehaviour != null &&
                cameraLookBehaviour.enabled;

            detailJoystickWasActive =
                fixedJoystickObject != null &&
                fixedJoystickObject.activeSelf;

            detailInputStateCaptured = true;
        }

        if (playerMovementBehaviour != null)
            playerMovementBehaviour.enabled = false;

        if (cameraLookBehaviour != null)
            cameraLookBehaviour.enabled = false;

        if (fixedJoystickObject != null)
            fixedJoystickObject.SetActive(false);
    }


    private void ResumeWorldInputAfterDetailPopup()
    {
        IsWorldInputBlocked = false;

        if (!detailInputStateCaptured)
            return;

        if (!startupStabilizing)
        {
            if (playerMovementBehaviour != null)
                playerMovementBehaviour.enabled =
                    detailPlayerMovementWasEnabled;

            if (cameraLookBehaviour != null)
                cameraLookBehaviour.enabled =
                    detailCameraLookWasEnabled;
        }

        if (fixedJoystickObject != null)
            fixedJoystickObject.SetActive(
                detailJoystickWasActive);

        detailInputStateCaptured = false;
    }


    private void StopDetailPopupPointerEvent(
        PointerDownEvent evt)
    {
        evt?.StopPropagation();
    }


    private void StopDetailPopupPointerEvent(
        PointerMoveEvent evt)
    {
        evt?.StopPropagation();
    }


    private void StopDetailPopupPointerEvent(
        PointerUpEvent evt)
    {
        evt?.StopPropagation();
    }


    private void StopDetailPopupWheelEvent(
        WheelEvent evt)
    {
        evt?.StopPropagation();
    }


    private bool DetailPopupHasScrollableOverflow()
    {
        if (detailPopupScroll == null)
            return false;

        VisualElement viewport =
            detailPopupScroll.contentViewport;

        VisualElement content =
            detailPopupScroll.contentContainer;

        if (viewport == null ||
            content == null)
        {
            return false;
        }

        float viewportHeight =
            viewport.resolvedStyle.height;

        float contentHeight =
            content.resolvedStyle.height;

        if (float.IsNaN(viewportHeight) ||
            float.IsNaN(contentHeight) ||
            viewportHeight <= 0f ||
            contentHeight <= 0f)
        {
            return false;
        }

        // A small tolerance prevents tiny fractional layout differences
        // from turning scrolling on/off between frames.
        return contentHeight > viewportHeight + 1f;
    }


    private void ConsumeNonScrollableDetailGesture(
        EventBase evt)
    {
        if (evt == null ||
            DetailPopupHasScrollableOverflow())
        {
            return;
        }

        // Lock first, before UI Toolkit gets a chance to visually move
        // the ScrollView content.
        if (detailPopupScroll != null)
        {
            detailPopupScroll.scrollOffset =
                Vector2.zero;
        }

        // Stop ScrollView's built-in drag/wheel default action in the same
        // input event. This is the key difference from only clamping in Update().
        evt.PreventDefault();
        evt.StopImmediatePropagation();
    }


    private void GuardDetailPopupScrollPointerDown(
        PointerDownEvent evt)
    {
        ConsumeNonScrollableDetailGesture(evt);
    }


    private void GuardDetailPopupScrollPointerMove(
        PointerMoveEvent evt)
    {
        ConsumeNonScrollableDetailGesture(evt);
    }


    private void GuardDetailPopupScrollPointerUp(
        PointerUpEvent evt)
    {
        ConsumeNonScrollableDetailGesture(evt);
    }


    private void GuardDetailPopupScrollWheel(
        WheelEvent evt)
    {
        ConsumeNonScrollableDetailGesture(evt);
    }


    private void ClampDetailPopupScrollOffset()
    {
        if (detailPopupScroll == null ||
            detailPopupOverlay == null ||
            detailPopupOverlay.ClassListContains(HiddenClass))
        {
            return;
        }

        VisualElement viewport =
            detailPopupScroll.contentViewport;

        VisualElement content =
            detailPopupScroll.contentContainer;

        if (viewport == null ||
            content == null)
        {
            return;
        }

        float viewportHeight =
            viewport.resolvedStyle.height;

        float contentHeight =
            content.resolvedStyle.height;

        if (float.IsNaN(viewportHeight) ||
            float.IsNaN(contentHeight) ||
            viewportHeight <= 0f ||
            contentHeight <= 0f)
        {
            return;
        }

        float maxScrollY =
            Mathf.Max(
                0f,
                contentHeight - viewportHeight);

        Vector2 current =
            detailPopupScroll.scrollOffset;

        float clampedY =
            Mathf.Clamp(
                current.y,
                0f,
                maxScrollY);

        // Short content: lock it to the top and hide the scrollbar.
        if (maxScrollY <= 0.5f)
        {
            clampedY = 0f;

            detailPopupScroll.verticalScrollerVisibility =
                ScrollerVisibility.Hidden;
        }
        else
        {
            detailPopupScroll.verticalScrollerVisibility =
                ScrollerVisibility.Auto;
        }

        if (Mathf.Abs(current.y - clampedY) > 0.01f)
        {
            detailPopupScroll.scrollOffset =
                new Vector2(
                    0f,
                    clampedY);
        }
    }


    [Serializable]
    private class DetailPlacementEntry
    {
        public string key;
        public UIToolkitButton label;
        public Vector2 anchorPanel;
    }


    // =========================================================

    private void OpenAIChat()
    {
        if (aiChatOverlay == null)
            return;

        UpdateAIChatContextLabel();

        aiChatOverlay.RemoveFromClassList(HiddenClass);
        aiChatOverlay.BringToFront();
        SetAIChatPicking(true);

        if (playerMovementBehaviour != null)
            playerMovementBehaviour.enabled = false;

        if (cameraLookBehaviour != null)
            cameraLookBehaviour.enabled = false;

        if (fixedJoystickObject != null)
            fixedJoystickObject.SetActive(false);

        aiChatInput?.Focus();
    }

    private void CloseAIChat()
    {
        if (aiChatOverlay != null &&
            !aiChatOverlay.ClassListContains(HiddenClass))
        {
            aiChatOverlay.AddToClassList(HiddenClass);
        }

        SetAIChatPicking(false);

        if (!startupStabilizing)
        {
            if (playerMovementBehaviour != null)
                playerMovementBehaviour.enabled = true;

            if (cameraLookBehaviour != null)
                cameraLookBehaviour.enabled = true;
        }

        if (fixedJoystickObject != null)
            fixedJoystickObject.SetActive(true);
    }

    private void HandleAIChatKeyDown(KeyDownEvent evt)
    {
        if (evt == null)
            return;

        if (evt.keyCode != KeyCode.Return &&
            evt.keyCode != KeyCode.KeypadEnter)
        {
            return;
        }

        evt.StopPropagation();
        SendAIChatMessage();
    }

    private void SendAIChatMessage()
    {
        if (aiChatInput == null)
            return;

        string userText =
            aiChatInput.value?.Trim();

        if (string.IsNullOrWhiteSpace(userText))
            return;

        StartCoroutine(
            SendAIChatMessageRoutine(userText));
    }

    private IEnumerator SendAIChatMessageRoutine(
        string userText)
    {
        if (sendAIChatButton != null)
            sendAIChatButton.SetEnabled(false);

        if (aiChatInput != null)
        {
            aiChatInput.SetEnabled(false);
            aiChatInput.value = string.Empty;
        }

        AddAIChatBubble(
            userText,
            true);

        SetAIChatTyping(true);

        string contextualPrompt =
            BuildVRContextPrompt(userText);

        bool requestFinished = false;
        string answer = string.Empty;
        string requestError = string.Empty;

        // Reuse the SAME AIService already used by ChatAIScene.
        yield return AIService.SendMessage(
            contextualPrompt,
            value =>
            {
                answer = value;
                requestFinished = true;
            },
            error =>
            {
                requestError = error;
                requestFinished = true;
            });

        SetAIChatTyping(false);

        if (!requestFinished ||
            string.IsNullOrWhiteSpace(answer))
        {
            Debug.LogError(
                "[VRPageController] VR AI chat failed: " +
                requestError);

            answer =
                "Mình chưa nhận được phản hồi từ AI. " +
                "Bạn thử gửi lại câu hỏi nhé.";
        }

        AddAIChatBubble(
            answer.Trim(),
            false);

        if (aiChatInput != null)
        {
            aiChatInput.SetEnabled(true);
            aiChatInput.Focus();
        }

        if (sendAIChatButton != null)
            sendAIChatButton.SetEnabled(true);
    }

    private string BuildVRContextPrompt(
        string userQuestion)
    {
        string classId =
            PlayerPrefs.GetString(
                "selected_class_id",
                string.Empty);

        string lessonId =
            PlayerPrefs.GetString(
                "selected_lesson_id",
                string.Empty);

        string lessonTitle =
            PlayerPrefs.GetString(
                "selected_lesson_title",
                PlayerPrefs.GetString(
                    "selected_model_lesson_title",
                    "Current lesson"));

        string modelName =
            PlayerPrefs.GetString(
                "selected_model_name",
                "Current 3D model");

        string modelAssetId =
            PlayerPrefs.GetString(
                "selected_model_asset_id",
                string.Empty);

        return
            "You are answering a student inside VR mode of a 3D education app.\n" +
            $"Class ID: {classId}\n" +
            $"Lesson ID: {lessonId}\n" +
            $"Lesson title: {lessonTitle}\n" +
            $"Current 3D model: {modelName}\n" +
            $"Model asset ID: {modelAssetId}\n" +
            "The student may ask about the lesson, the model's anatomy/structure, " +
            "the function of its parts, relationships between parts, or how the model works. " +
            "Use the context above when the student says 'this model', 'this part', " +
            "'mô hình này', 'bộ phận này', or similar wording. " +
            "If a specific model part was not selected, do not invent which part they mean; " +
            "ask a short clarifying question when necessary.\n\n" +
            "Student question:\n" +
            userQuestion;
    }

    private void AddAIChatBubble(
        string text,
        bool fromUser)
    {
        if (aiChatMessageContainer == null)
            return;

        VisualElement row =
            new VisualElement();

        row.AddToClassList("ai-chat-row");
        row.AddToClassList(
            fromUser
                ? "ai-chat-row-user"
                : "ai-chat-row-assistant");

        Label bubble =
            new Label(text ?? string.Empty);

        bubble.AddToClassList("ai-chat-bubble");
        bubble.AddToClassList(
            fromUser
                ? "ai-chat-bubble-user"
                : "ai-chat-bubble-assistant");

        row.Add(bubble);
        aiChatMessageContainer.Add(row);

        aiChatMessages?.schedule.Execute(
            () => aiChatMessages.ScrollTo(row));
    }

    private void SetAIChatTyping(bool visible)
    {
        if (aiChatTyping == null)
            return;

        if (visible)
            aiChatTyping.RemoveFromClassList(HiddenClass);
        else if (!aiChatTyping.ClassListContains(HiddenClass))
            aiChatTyping.AddToClassList(HiddenClass);
    }

    private void UpdateAIChatContextLabel()
    {
        if (aiChatContext == null)
            return;

        string lesson =
            PlayerPrefs.GetString(
                "selected_lesson_title",
                "Current lesson");

        string model =
            PlayerPrefs.GetString(
                "selected_model_name",
                "3D model");

        aiChatContext.text =
            lesson + " • " + model;
    }

    // =========================================================
    // Startup stabilization
    // =========================================================

    private void ResolvePlayerStartupComponents()
    {
#if UNITY_2023_1_OR_NEWER
        playerCharacterController =
            FindFirstObjectByType<CharacterController>(
                FindObjectsInactive.Include);
#else
        playerCharacterController =
            FindObjectOfType<CharacterController>(true);
#endif

        if (playerCharacterController == null)
            return;

        MonoBehaviour[] behaviours =
            playerCharacterController
                .GetComponents<MonoBehaviour>();

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
                continue;

            string typeName =
                behaviour.GetType().Name;

            if (string.Equals(
                    typeName,
                    "PlayerController",
                    StringComparison.Ordinal))
            {
                playerMovementBehaviour =
                    behaviour;
            }
        }

        // CameraLook normally lives on Player/CameraPivot or its child.
        MonoBehaviour[] childBehaviours =
            playerCharacterController
                .GetComponentsInChildren<MonoBehaviour>(true);

        foreach (MonoBehaviour behaviour in childBehaviours)
        {
            if (behaviour == null)
                continue;

            if (string.Equals(
                    behaviour.GetType().Name,
                    "CameraLook",
                    StringComparison.Ordinal))
            {
                cameraLookBehaviour =
                    behaviour;
                break;
            }
        }
    }

    private void FreezePlayerForStartup()
    {
        if (playerMovementBehaviour != null)
            playerMovementBehaviour.enabled = false;

        if (cameraLookBehaviour != null)
            cameraLookBehaviour.enabled = false;

        if (snapPlayerToFloorOnStartup)
            SnapPlayerCharacterControllerToFloor();
    }

    private void UnfreezePlayerAfterStartup()
    {
        if (playerMovementBehaviour != null)
            playerMovementBehaviour.enabled = true;

        if (cameraLookBehaviour != null)
            cameraLookBehaviour.enabled = true;
    }

    private void SnapPlayerCharacterControllerToFloor()
    {
        if (playerCharacterController == null)
            return;

        Transform player =
            playerCharacterController.transform;

        Vector3 rayOrigin =
            player.position +
            Vector3.up * 6f;

        RaycastHit[] hits =
            Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                20f,
                ~0,
                QueryTriggerInteraction.Ignore);

        Array.Sort(
            hits,
            (a, b) =>
                a.distance.CompareTo(
                    b.distance));

        RaycastHit? selectedFloorHit = null;

        // Prefer the generated classroom Floor instead of desks/chairs.
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
                continue;

            // Ignore the Player's own CharacterController.
            if (hit.collider ==
                playerCharacterController)
            {
                continue;
            }

            string objectName =
                hit.collider.gameObject.name ??
                string.Empty;

            string parentName =
                hit.collider.transform.parent != null
                    ? hit.collider.transform.parent.name
                    : string.Empty;

            bool looksLikeFloor =
                objectName.IndexOf(
                    "floor",
                    StringComparison.OrdinalIgnoreCase) >= 0
                ||
                parentName.IndexOf(
                    "floor",
                    StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksLikeFloor)
            {
                selectedFloorHit = hit;
                break;
            }
        }

        float floorY =
            selectedFloorHit.HasValue
                ? selectedFloorHit.Value.point.y
                : 0f;

        // CharacterController bottom =
        // transform.y + center.y - height/2.
        float bottomOffset =
            playerCharacterController.center.y -
            playerCharacterController.height * 0.5f;

        bool wasEnabled =
            playerCharacterController.enabled;

        playerCharacterController.enabled =
            false;

        Vector3 position =
            player.position;

        position.y =
            floorY -
            bottomOffset +
            floorClearance;

        player.position =
            position;

        playerCharacterController.enabled =
            wasEnabled;

        Physics.SyncTransforms();

        Debug.Log(
            $"[VRPageController] Player snapped to stable floor position Y={position.y:F3}");
    }

    private IEnumerator FinishStartupWhenStable()
    {
        while (!firstModelReady)
            yield return null;

        float elapsed =
            Time.unscaledTime -
            startupStartedAt;

        float remainingMinimum =
            Mathf.Max(
                0f,
                minimumStartupLoadingSeconds -
                elapsed);

        if (remainingMinimum > 0f)
        {
            yield return new WaitForSecondsRealtime(
                remainingMinimum);
        }

        if (settleAfterModelLoadedSeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(
                settleAfterModelLoadedSeconds);
        }

        // Put the user at the BACK of the classroom and face the teaching/front area.
        // This is done only after generated classroom geometry is available.
        if (spawnAtBackFacingFront)
            PositionPlayerAtBackFacingFront();

        if (snapPlayerToFloorOnStartup)
            SnapPlayerCharacterControllerToFloor();

        // The runtime GLB anchor is camera-relative. Refresh it after moving/rotating Player.
        modelCatalog?.RefreshAnchorFromPlayer();

        startupStabilizing = false;
        UnfreezePlayerAfterStartup();
        HideStartupLoading();
    }

    private void ShowStartupLoading(
        string message)
    {
        startupStabilizing = true;

        if (startupLoadingOverlay != null)
        {
            startupLoadingOverlay
                .RemoveFromClassList(
                    "startup-hidden");

            startupLoadingOverlay
                .BringToFront();

            startupLoadingOverlay.pickingMode =
                PickingMode.Position;
        }

        if (startupLoadingText != null &&
            !string.IsNullOrWhiteSpace(message))
        {
            startupLoadingText.text =
                message;
        }
    }

    private void HideStartupLoading()
    {
        if (startupLoadingOverlay == null)
            return;

        if (!startupLoadingOverlay
                .ClassListContains(
                    "startup-hidden"))
        {
            startupLoadingOverlay
                .AddToClassList(
                    "startup-hidden");
        }

        startupLoadingOverlay.pickingMode =
            PickingMode.Ignore;
    }

}
