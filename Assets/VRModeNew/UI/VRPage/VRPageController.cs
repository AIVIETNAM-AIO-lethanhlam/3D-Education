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
    private float detailLabelVerticalSpacing = 52f;

    [Tooltip("Screen padding used when clamping labels.")]
    [SerializeField, Range(4f, 40f)]
    private float detailLabelScreenPadding = 14f;

    [Tooltip(
        "Top edge of the vertical zone reserved for Detail labels, as a fraction "
        + "of the phone UI height. Keeping this below the toolbar prevents labels "
        + "from covering the top controls."
    )]
    [SerializeField, Range(0.08f, 0.35f)]
    private float detailLabelSafeTop = 0.16f;

    [Tooltip(
        "Bottom edge of the Detail-label zone, as a fraction of the phone UI "
        + "height. Labels are never placed below this line, keeping the joystick "
        + "and AI button area clear."
    )]
    [SerializeField, Range(0.55f, 0.90f)]
    private float detailLabelSafeBottom = 0.76f;

    [Tooltip(
        "When many labels are visible, distribute the whole label column through "
        + "the safe vertical zone instead of allowing the group to accumulate "
        + "near the projected model position."
    )]
    [SerializeField]
    private bool distributeDetailLabelsInSafeZone = true;

    [Tooltip(
        "Minimum visible gap between two adjacent Detail labels. "
        + "The layout uses the actual resolved label height plus this gap."
    )]
    [SerializeField, Range(2f, 24f)]
    private float detailLabelMinimumGap = 12f;

    [Header("Detail label horizontal layout")]

    [Tooltip(
        "Optionally force a nearly even left/right split. Disabled by default so "
        + "labels follow their natural projected side while still using collision-safe spacing."
    )]
    [SerializeField]
    private bool balanceDetailLabelColumns = false;

    [Tooltip(
        "Horizontal center of the left label column, as a fraction of the phone UI width."
    )]
    [SerializeField, Range(0.12f, 0.45f)]
    private float detailLeftColumnX = 0.23f;

    [Tooltip(
        "Horizontal center of the right label column, as a fraction of the phone UI width."
    )]
    [SerializeField, Range(0.50f, 0.82f)]
    private float detailRightColumnX = 0.64f;

    [Tooltip(
        "Right-most boundary that Detail labels may use. Keeping this below the "
        + "menu column prevents labels from covering the menu buttons."
    )]
    [SerializeField, Range(0.65f, 0.90f)]
    private float detailRightSafeEdge = 0.76f;

    [Tooltip(
        "Top reservation for the RIGHT label column. Because the right column is "
        + "kept left of the menu stack, this can stay close to the normal safe top."
    )]
    [SerializeField, Range(0.18f, 0.42f)]
    private float detailRightSafeTop = 0.26f;

    [Tooltip(
        "Bottom edge used only by the RIGHT Detail-label column. A slightly lower "
        + "value than the global bottom still leaves the AI chat button area clear."
    )]
    [SerializeField, Range(0.65f, 0.85f)]
    private float detailRightSafeBottom = 0.78f;

    [Tooltip(
        "Extra vertical spacing used by labels in the RIGHT column so they are as "
        + "easy to scan as the labels on the left."
    )]
    [SerializeField, Range(0f, 24f)]
    private float detailRightExtraVerticalSpacing = 8f;

    [Tooltip(
        "Minimum visible gap between adjacent labels in the RIGHT column."
    )]
    [SerializeField, Range(4f, 28f)]
    private float detailRightMinimumGap = 16f;

    [Header("Centered model auto rotation")]

    [Tooltip(
        "Angular speed for the Rotate button. The model rotates around the "
        + "visual center of its rendered geometry, so it spins in place instead "
        + "of orbiting around an offset model pivot."
    )]
    [SerializeField, Range(5f, 120f)]
    private float centeredAutoRotateSpeed = 28f;

    [Tooltip(
        "Use world-up as the spin axis. Recommended for anatomical models so "
        + "they remain upright while rotating."
    )]
    [SerializeField]
    private bool centeredAutoRotateUseWorldUp = true;

    private bool centeredAutoRotateEnabled = false;
    private Transform centeredAutoRotateModel;
    private Vector3 centeredAutoRotatePivotWorld;
    private bool centeredAutoRotatePivotValid = false;

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

        // The Rotate button now controls centered in-place rotation.
        // Start in the OFF state even if the catalog had its own legacy flag.
        if (modelCatalog != null &&
            modelCatalog.AutoRotateEnabled)
        {
            modelCatalog.ToggleAutoRotate();
        }

        centeredAutoRotateEnabled = false;
        centeredAutoRotatePivotValid = false;

        RefreshRotateIcon(false);

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

        UpdateCenteredAutoRotation();

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

        // Some VRPage UXML versions only contain the orange BtnAIChat button
        // and do not yet contain the chat modal itself. Build a complete
        // runtime chat window when those elements are missing so the feature
        // works without requiring another UXML/USS migration.
        EnsureAIChatUI();

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

    private void EnsureAIChatUI()
    {
        if (root == null)
            return;

        // If the UXML already provides the complete AI chat window, keep it.
        if (aiChatOverlay != null &&
            aiChatPanel != null &&
            aiChatMessages != null &&
            aiChatInput != null &&
            sendAIChatButton != null &&
            closeAIChatButton != null)
        {
            return;
        }

        aiChatOverlay =
            new VisualElement
            {
                name = "AIChatOverlay"
            };

        aiChatOverlay.style.position = Position.Absolute;
        aiChatOverlay.style.left = 0;
        aiChatOverlay.style.right = 0;
        aiChatOverlay.style.top = 0;
        aiChatOverlay.style.bottom = 0;
        aiChatOverlay.style.backgroundColor =
            new Color(0f, 0f, 0f, 0.38f);
        aiChatOverlay.style.alignItems = Align.Center;
        aiChatOverlay.style.justifyContent = Justify.Center;
        aiChatOverlay.style.display = DisplayStyle.None;

        aiChatPanel =
            new VisualElement
            {
                name = "AIChatPanel"
            };

        aiChatPanel.style.width = Length.Percent(91f);
        aiChatPanel.style.height = Length.Percent(76f);
        aiChatPanel.style.maxWidth = 720f;
        aiChatPanel.style.backgroundColor =
            new Color(0.965f, 0.975f, 0.995f, 1f);
        aiChatPanel.style.borderTopLeftRadius = 22f;
        aiChatPanel.style.borderTopRightRadius = 22f;
        aiChatPanel.style.borderBottomLeftRadius = 22f;
        aiChatPanel.style.borderBottomRightRadius = 22f;
        aiChatPanel.style.paddingLeft = 16f;
        aiChatPanel.style.paddingRight = 16f;
        aiChatPanel.style.paddingTop = 14f;
        aiChatPanel.style.paddingBottom = 14f;
        aiChatPanel.style.flexDirection = FlexDirection.Column;

        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.marginBottom = 6f;

        VisualElement titleBox = new VisualElement();
        titleBox.style.flexGrow = 1f;

        Label title = new Label("AI học tập");
        title.style.fontSize = 19f;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new Color(0.08f, 0.16f, 0.30f, 1f);

        aiChatContext =
            new Label("Bài học hiện tại • Mô hình 3D")
            {
                name = "AIChatContext"
            };
        aiChatContext.style.fontSize = 11f;
        aiChatContext.style.color = new Color(0.35f, 0.42f, 0.54f, 1f);
        aiChatContext.style.marginTop = 2f;

        titleBox.Add(title);
        titleBox.Add(aiChatContext);

        closeAIChatButton =
            new UIToolkitButton
            {
                name = "BtnCloseAIChat",
                text = "×"
            };
        closeAIChatButton.style.width = 38f;
        closeAIChatButton.style.height = 38f;
        closeAIChatButton.style.fontSize = 24f;
        closeAIChatButton.style.backgroundColor =
            new Color(1f, 1f, 1f, 1f);
        closeAIChatButton.style.borderTopLeftRadius = 19f;
        closeAIChatButton.style.borderTopRightRadius = 19f;
        closeAIChatButton.style.borderBottomLeftRadius = 19f;
        closeAIChatButton.style.borderBottomRightRadius = 19f;

        header.Add(titleBox);
        header.Add(closeAIChatButton);
        aiChatPanel.Add(header);

        Label helper =
            new Label(
                "Hỏi AI về bài học, cấu tạo, chức năng hoặc các bộ phận của mô hình đang xem.");
        helper.style.whiteSpace = WhiteSpace.Normal;
        helper.style.fontSize = 12f;
        helper.style.color = new Color(0.30f, 0.36f, 0.46f, 1f);
        helper.style.marginBottom = 8f;
        aiChatPanel.Add(helper);

        aiChatMessages =
            new ScrollView(ScrollViewMode.Vertical)
            {
                name = "AIChatMessages"
            };
        // Keep the message area compact when the conversation is still short.
        // Previously flexGrow = 1 made the ScrollView occupy all remaining
        // vertical space, creating the large empty light-blue area below the
        // first message.
        aiChatMessages.style.flexGrow = 0f;
        aiChatMessages.style.flexShrink = 1f;
        aiChatMessages.style.height = Length.Percent(52f);
        aiChatMessages.style.minHeight = 180f;
        aiChatMessages.style.maxHeight = 390f;
        aiChatMessages.style.backgroundColor =
            new Color(1f, 1f, 1f, 0.72f);
        aiChatMessages.style.borderTopLeftRadius = 15f;
        aiChatMessages.style.borderTopRightRadius = 15f;
        aiChatMessages.style.borderBottomLeftRadius = 15f;
        aiChatMessages.style.borderBottomRightRadius = 15f;
        aiChatMessages.style.paddingLeft = 8f;
        aiChatMessages.style.paddingRight = 8f;
        aiChatMessages.style.paddingTop = 8f;
        aiChatMessages.style.paddingBottom = 8f;
        // Keep the chat vertically scrollable, but hide the visible scrollbar.
        // Users can still drag/swipe inside the chat history or use the mouse wheel.
        aiChatMessages.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        aiChatMessages.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        aiChatMessages.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;

        // Force-hide the internal UI Toolkit scrollers too.
        // On some Unity 6 versions, ScrollerVisibility.Hidden alone can still
        // leave the vertical track/arrows visible after layout.
        if (aiChatMessages.verticalScroller != null)
        {
            aiChatMessages.verticalScroller.style.display = DisplayStyle.None;
            aiChatMessages.verticalScroller.style.width = 0f;
            aiChatMessages.verticalScroller.style.minWidth = 0f;
            aiChatMessages.verticalScroller.style.maxWidth = 0f;
        }

        if (aiChatMessages.horizontalScroller != null)
        {
            aiChatMessages.horizontalScroller.style.display = DisplayStyle.None;
            aiChatMessages.horizontalScroller.style.height = 0f;
            aiChatMessages.horizontalScroller.style.minHeight = 0f;
            aiChatMessages.horizontalScroller.style.maxHeight = 0f;
        }

        // Re-apply after the first layout pass because Unity can rebuild the
        // internal ScrollView hierarchy during geometry resolution.
        aiChatMessages.schedule.Execute(() =>
        {
            aiChatMessages.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            aiChatMessages.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            if (aiChatMessages.verticalScroller != null)
            {
                aiChatMessages.verticalScroller.style.display = DisplayStyle.None;
                aiChatMessages.verticalScroller.style.width = 0f;
                aiChatMessages.verticalScroller.style.minWidth = 0f;
                aiChatMessages.verticalScroller.style.maxWidth = 0f;
            }

            if (aiChatMessages.horizontalScroller != null)
            {
                aiChatMessages.horizontalScroller.style.display = DisplayStyle.None;
                aiChatMessages.horizontalScroller.style.height = 0f;
                aiChatMessages.horizontalScroller.style.minHeight = 0f;
                aiChatMessages.horizontalScroller.style.maxHeight = 0f;
            }
        }).ExecuteLater(1);

        aiChatMessageContainer =
            new VisualElement
            {
                name = "AIChatMessageContainer"
            };
        aiChatMessageContainer.style.flexDirection = FlexDirection.Column;
        aiChatMessageContainer.style.flexGrow = 0f;
        aiChatMessageContainer.style.flexShrink = 0f;
        aiChatMessageContainer.style.height = StyleKeyword.Auto;
        aiChatMessages.Add(aiChatMessageContainer);
        aiChatPanel.Add(aiChatMessages);

        aiChatTyping =
            new Label("AI đang trả lời...")
            {
                name = "AIChatTyping"
            };
        aiChatTyping.style.fontSize = 11f;
        aiChatTyping.style.color = new Color(0.28f, 0.43f, 0.72f, 1f);
        aiChatTyping.style.marginTop = 6f;
        aiChatTyping.style.display = DisplayStyle.None;
        aiChatPanel.Add(aiChatTyping);

        VisualElement inputRow = new VisualElement();
        inputRow.style.flexDirection = FlexDirection.Row;
        inputRow.style.alignItems = Align.FlexEnd;
        inputRow.style.marginTop = 8f;

        aiChatInput =
            new TextField
            {
                name = "AIChatInput",
                multiline = true
            };
        aiChatInput.style.flexGrow = 1f;
        aiChatInput.style.minHeight = 44f;
        aiChatInput.style.maxHeight = 92f;
        aiChatInput.style.marginRight = 8f;
        aiChatInput.style.backgroundColor = Color.white;
        aiChatInput.style.borderTopLeftRadius = 13f;
        aiChatInput.style.borderTopRightRadius = 13f;
        aiChatInput.style.borderBottomLeftRadius = 13f;
        aiChatInput.style.borderBottomRightRadius = 13f;

        sendAIChatButton =
            new UIToolkitButton
            {
                name = "BtnSendAIChat",
                text = "Gửi"
            };
        sendAIChatButton.style.width = 62f;
        sendAIChatButton.style.height = 44f;
        sendAIChatButton.style.backgroundColor =
            new Color(0.10f, 0.38f, 0.92f, 1f);
        sendAIChatButton.style.color = Color.white;
        sendAIChatButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        sendAIChatButton.style.borderTopLeftRadius = 13f;
        sendAIChatButton.style.borderTopRightRadius = 13f;
        sendAIChatButton.style.borderBottomLeftRadius = 13f;
        sendAIChatButton.style.borderBottomRightRadius = 13f;

        inputRow.Add(aiChatInput);
        inputRow.Add(sendAIChatButton);
        aiChatPanel.Add(inputRow);

        aiChatOverlay.Add(aiChatPanel);
        root.Add(aiChatOverlay);

        Debug.Log(
            "[VRPageController] Runtime AI chat window created.");
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
        modelCatalog.AutoRotateChanged += HandleLegacyAutoRotateChanged;
        modelCatalog.LoadingStateChanged += RefreshLoadingLabel;
    }

    private void UnregisterCatalogEvents()
    {
        if (modelCatalog == null)
            return;

        modelCatalog.CatalogReady -= HandleCatalogReady;
        modelCatalog.ModelChanged -= HandleModelChanged;
        modelCatalog.VisibilityChanged -= RefreshVisibilityIcon;
        modelCatalog.AutoRotateChanged -= HandleLegacyAutoRotateChanged;
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

        // Disable the catalog's old auto-rotation first. That implementation
        // may rotate around the GLB/root pivot, which can be offset from the
        // visible center and makes the model appear to orbit.
        if (modelCatalog.AutoRotateEnabled)
        {
            modelCatalog.ToggleAutoRotate();
        }

        centeredAutoRotateEnabled =
            !centeredAutoRotateEnabled;

        centeredAutoRotateModel =
            modelCatalog.CurrentModel.transform;

        // Recalculate the VISUAL pivot whenever rotation starts. This uses
        // renderer bounds rather than transform.position, so models whose GLB
        // origin is off-center still spin in place.
        centeredAutoRotatePivotValid =
            TryCalculateCurrentModelVisualCenter(
                out centeredAutoRotatePivotWorld);

        if (!centeredAutoRotatePivotValid)
        {
            centeredAutoRotatePivotWorld =
                centeredAutoRotateModel.position;

            centeredAutoRotatePivotValid = true;
        }

        RefreshRotateIcon(
            centeredAutoRotateEnabled);

        Debug.Log(
            "[VRPageController] Centered auto rotation: "
            + (centeredAutoRotateEnabled
                ? "ON"
                : "OFF")
            + " pivot="
            + centeredAutoRotatePivotWorld);

        // Keep the action menu open.
    }

    private void UpdateCenteredAutoRotation()
    {
        if (!centeredAutoRotateEnabled)
            return;

        if (modelCatalog == null ||
            modelCatalog.CurrentModel == null)
        {
            StopCenteredAutoRotation();
            return;
        }

        Transform currentModel =
            modelCatalog.CurrentModel.transform;

        centeredAutoRotateModel =
            currentModel;

        // IMPORTANT:
        // The user can grab/move the model while auto-rotation is enabled.
        // Therefore the pivot must NOT remain a fixed world-space point from
        // the moment the Rotate button was pressed. Recalculate the model's
        // current visible center every frame so it continues to spin in place
        // wherever the user moves it.
        centeredAutoRotatePivotValid =
            TryCalculateCurrentModelVisualCenter(
                out centeredAutoRotatePivotWorld);

        if (!centeredAutoRotatePivotValid)
        {
            centeredAutoRotatePivotWorld =
                currentModel.position;

            centeredAutoRotatePivotValid = true;
        }

        float angle =
            centeredAutoRotateSpeed *
            Time.deltaTime;

        Vector3 axis =
            centeredAutoRotateUseWorldUp
                ? Vector3.up
                : currentModel.up;

        // Rotate around the CURRENT visible center. Because the center is
        // refreshed after any move/scale interaction, the model spins at its
        // present location instead of orbiting around an old fixed pivot.
        currentModel.RotateAround(
            centeredAutoRotatePivotWorld,
            axis,
            angle);
    }

    private bool TryCalculateCurrentModelVisualCenter(
        out Vector3 center)
    {
        center = Vector3.zero;

        if (modelCatalog == null ||
            modelCatalog.CurrentModel == null)
        {
            return false;
        }

        Renderer[] renderers =
            modelCatalog.CurrentModel
                .GetComponentsInChildren<Renderer>(
                    true);

        bool hasBounds = false;
        Bounds combinedBounds =
            new Bounds();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null ||
                !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                combinedBounds =
                    renderer.bounds;

                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(
                    renderer.bounds);
            }
        }

        if (!hasBounds)
            return false;

        center =
            combinedBounds.center;

        return true;
    }

    private void StopCenteredAutoRotation()
    {
        centeredAutoRotateEnabled = false;
        centeredAutoRotateModel = null;
        centeredAutoRotatePivotValid = false;

        RefreshRotateIcon(false);
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

    private void HandleLegacyAutoRotateChanged(
        bool enabled)
    {
        // The catalog may still emit this legacy event. Do not let it change
        // the UI state for our centered rotation. If legacy rotation somehow
        // becomes enabled, immediately turn it back off.
        if (enabled &&
            modelCatalog != null)
        {
            modelCatalog.ToggleAutoRotate();
        }

        RefreshRotateIcon(
            centeredAutoRotateEnabled);
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
        centeredAutoRotateModel =
            model != null
                ? model.transform
                : null;

        centeredAutoRotatePivotValid = false;

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

        ResolveDetailControllers();

        // Bind the anchor controller to the exact runtime GLB that was
        // just loaded by VRRuntimeModelCatalog.
        if (detailAnchorController != null)
        {
            detailAnchorController.SetModelRoot(
                model != null ? model.transform : null
            );
        }

        // Resolve labels for the exact GLB file, not merely the first
        // model_3d row belonging to the lesson.
        if (detailService != null && record != null)
        {
            string lessonId =
                PlayerPrefs.GetString(
                    "selected_lesson_id",
                    ""
                );

            if (!string.IsNullOrWhiteSpace(lessonId) &&
                !string.IsNullOrWhiteSpace(record.file_name))
            {
                Debug.Log(
                    "[VRPageController] Resolving Detail labels for runtime model:"
                    + "\nLesson ID = " + lessonId
                    + "\nFile = " + record.file_name
                );

                detailService.ResolveModelAssetForLessonAndFile(
                    lessonId,
                    record.file_name
                );
            }
            else if (!string.IsNullOrWhiteSpace(lessonId))
            {
                detailService.ResolveModelAssetForLesson(
                    lessonId
                );
            }
        }

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
            ResolveDetailControllers();

            if (detailAnchorController != null)
            {
                detailAnchorController.SetAutomaticAnchorMarkersVisible(false);
            }

            CloseDetailPopup();
            ClearDetailLabels();
            return;
        }

        ResolveDetailControllers();

        if (detailAnchorController != null)
        {
            // Always show the yellow 3D anchor dots while Detail Mode is ON.
            // This also recreates markers if the scene previously serialized
            // Show Automatic Anchor Markers as false.
            detailAnchorController.SetAutomaticAnchorMarkersVisible(true);
        }

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

            if (balanceDetailLabelColumns)
            {
                // First collect by natural projected side. We rebalance after
                // all anchors are known so one side cannot become overloaded.
                if (panelX < rootWidth * 0.5f)
                    left.Add(entry);
                else
                    right.Add(entry);
            }
            else
            {
                if (panelX < rootWidth * 0.5f)
                    left.Add(entry);
                else
                    right.Add(entry);
            }
        }

        if (balanceDetailLabelColumns)
        {
            List<DetailPlacementEntry> allEntries =
                new List<DetailPlacementEntry>(
                    left.Count + right.Count);

            allEntries.AddRange(left);
            allEntries.AddRange(right);

            // Sort top-to-bottom first, then alternate columns.
            // This gives a visually even two-column distribution instead
            // of putting almost every label on the same side.
            allEntries.Sort(
                (a, b) =>
                    a.anchorPanel.y.CompareTo(
                        b.anchorPanel.y));

            left.Clear();
            right.Clear();

            for (int i = 0; i < allEntries.Count; i++)
            {
                DetailPlacementEntry item =
                    allEntries[i];

                if ((i & 1) == 0)
                    left.Add(item);
                else
                    right.Add(item);
            }
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

        // Keep connector visibility consistent across light/dark model
        // backgrounds. The previous semi-transparent blue could look very pale
        // on some scenes/models (brain.glb in particular).
        painter.lineWidth =
            2.25f;

        painter.strokeColor =
            new Color(
                0.08f,
                0.42f,
                0.95f,
                1.00f);

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

        // ---------------------------------------------------------
        // Reserve a vertical Detail-label zone.
        //
        // The old layout started every label close to its projected
        // anchor and only pushed later labels DOWN. When 20+ parts
        // were present and the model was far away, most projected
        // anchors were low on screen, so the whole label column
        // accumulated above the joystick / AI controls.
        //
        // The new layout keeps all labels between safeTop and
        // safeBottom, then shifts/distributes the complete group
        // upward as needed.
        // ---------------------------------------------------------

        float requestedSafeTop =
            rightSide
                ? Mathf.Max(
                    detailLabelSafeTop,
                    detailRightSafeTop)
                : detailLabelSafeTop;

        float safeTop =
            Mathf.Clamp(
                rootHeight * requestedSafeTop,
                detailLabelScreenPadding,
                rootHeight - detailLabelScreenPadding);

        float requestedSafeBottom =
            rightSide
                ? Mathf.Max(
                    detailLabelSafeBottom,
                    detailRightSafeBottom)
                : detailLabelSafeBottom;

        float safeBottom =
            Mathf.Clamp(
                rootHeight * requestedSafeBottom,
                safeTop + 40f,
                rootHeight - detailLabelScreenPadding);

        int count =
            entries.Count;

        // UI Toolkit may not have resolved every label size on the
        // first frame, so use the same stable fallback as before.
        float representativeHeight = 36f;

        for (int i = 0; i < count; i++)
        {
            UIToolkitButton candidate =
                entries[i].label;

            if (candidate == null)
                continue;

            float resolvedHeight =
                candidate.resolvedStyle.height;

            if (resolvedHeight > 1f)
            {
                representativeHeight =
                    Mathf.Max(
                        representativeHeight,
                        resolvedHeight);
            }
        }

        float minCenterY =
            safeTop +
            representativeHeight * 0.5f;

        float maxCenterY =
            safeBottom -
            representativeHeight * 0.5f;

        if (maxCenterY < minCenterY)
        {
            float middle =
                (safeTop + safeBottom) * 0.5f;

            minCenterY =
                middle;

            maxCenterY =
                middle;
        }

        // Fit the requested spacing inside the available safe zone.
        // For very large part counts this automatically compresses
        // spacing, but never lets the column run into the joystick.
        float availableCenterSpan =
            Mathf.Max(
                0f,
                maxCenterY - minCenterY);

        // Center-to-center spacing should be at least the current label height
        // plus a visible gap. This prevents adjacent white label cards from
        // appearing stuck together when there are many parts.
        float columnVerticalSpacing =
            detailLabelVerticalSpacing +
            (rightSide
                ? detailRightExtraVerticalSpacing
                : 0f);

        float columnMinimumGap =
            rightSide
                ? Mathf.Max(
                    detailLabelMinimumGap,
                    detailRightMinimumGap)
                : detailLabelMinimumGap;

        float desiredSpacing =
            Mathf.Max(
                columnVerticalSpacing,
                representativeHeight +
                columnMinimumGap);

        float spacing =
            desiredSpacing;

        if (count > 1)
        {
            float maximumSpacingThatFits =
                availableCenterSpan /
                (count - 1);

            spacing =
                Mathf.Min(
                    desiredSpacing,
                    maximumSpacingThatFits);
        }

        // Never collapse labels to an unreadably tiny spacing unless the safe
        // zone physically cannot fit the current column.
        float readableMinimumSpacing =
            representativeHeight + 4f;

        if (count > 1 &&
            availableCenterSpan >=
            readableMinimumSpacing * (count - 1))
        {
            spacing =
                Mathf.Max(
                    spacing,
                    readableMinimumSpacing);
        }

        // Find the average projected Y. This lets the label group
        // still follow the model vertically, but as ONE group.
        float averageAnchorY = 0f;

        for (int i = 0; i < count; i++)
        {
            averageAnchorY +=
                entries[i].anchorPanel.y;
        }

        averageAnchorY /=
            count;

        float groupSpan =
            spacing *
            Mathf.Max(
                0,
                count - 1);

        float groupStartY;

        if (distributeDetailLabelsInSafeZone &&
            count > 1)
        {
            // Center the sorted label group near the projected model,
            // then clamp the whole group into the reserved safe zone.
            groupStartY =
                averageAnchorY -
                groupSpan * 0.5f;

            groupStartY =
                Mathf.Clamp(
                    groupStartY,
                    minCenterY,
                    Mathf.Max(
                        minCenterY,
                        maxCenterY - groupSpan));
        }
        else
        {
            groupStartY =
                Mathf.Clamp(
                    entries[0].anchorPanel.y,
                    minCenterY,
                    maxCenterY);
        }

        for (int i = 0;
             i < count;
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
                height = representativeHeight;

            // Always use two visually separated columns.
            // balanceDetailLabelColumns only decides HOW MANY labels go to
            // each side; it no longer controls the X position itself.
            float centerX =
                rootWidth *
                (rightSide
                    ? detailRightColumnX
                    : detailLeftColumnX);

            float leftBoundary =
                detailLabelScreenPadding +
                width * 0.5f;

            float rightBoundary =
                rootWidth *
                detailRightSafeEdge -
                width * 0.5f;

            // Keep an empty center corridor between the two label columns.
            // This makes the left/right groups easier to read and prevents
            // adjacent white cards from visually merging.
            float centerCorridorHalfWidth =
                Mathf.Max(
                    18f,
                    rootWidth * 0.045f);

            if (rightSide)
            {
                leftBoundary =
                    Mathf.Max(
                        leftBoundary,
                        rootWidth * 0.5f +
                        centerCorridorHalfWidth +
                        width * 0.5f);
            }
            else
            {
                rightBoundary =
                    Mathf.Min(
                        rightBoundary,
                        rootWidth * 0.5f -
                        centerCorridorHalfWidth -
                        width * 0.5f);
            }

            // Never let labels enter the right-side menu column.
            centerX =
                Mathf.Clamp(
                    centerX,
                    leftBoundary,
                    Mathf.Max(
                        leftBoundary,
                        rightBoundary));

            float centerY;

            if (distributeDetailLabelsInSafeZone &&
                count > 1)
            {
                centerY =
                    groupStartY +
                    spacing * i;
            }
            else
            {
                centerY =
                    Mathf.Clamp(
                        entry.anchorPanel.y,
                        minCenterY,
                        maxCenterY);
            }

            // Final per-label clamp accounts for an individual
            // label being taller than the representative height.
            centerY =
                Mathf.Clamp(
                    centerY,
                    safeTop +
                    height * 0.5f,
                    safeBottom -
                    height * 0.5f);

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
        {
            EnsureAIChatUI();
        }

        if (aiChatOverlay == null)
        {
            Debug.LogError(
                "[VRPageController] AI chat overlay could not be created.");
            return;
        }

        UpdateAIChatContextLabel();

        aiChatOverlay.RemoveFromClassList(HiddenClass);
        aiChatOverlay.style.display = DisplayStyle.Flex;
        aiChatOverlay.BringToFront();
        SetAIChatPicking(true);

        if (aiChatMessageContainer != null &&
            aiChatMessageContainer.childCount == 0)
        {
            AddAIChatBubble(
                "Xin chào! Bạn có thể hỏi mình về bài học hoặc mô hình 3D đang xem. " +
                "Ví dụ: ‘Thùy trán có chức năng gì?’ hoặc ‘Giải thích cấu tạo của mô hình này’." ,
                false);
        }

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
        if (aiChatOverlay != null)
        {
            if (!aiChatOverlay.ClassListContains(HiddenClass))
                aiChatOverlay.AddToClassList(HiddenClass);

            aiChatOverlay.style.display = DisplayStyle.None;
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

        // Gemini may temporarily return HTTP 503 when the selected model is
        // under high demand. Retry automatically instead of immediately
        // showing an error to the student.
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            requestFinished = false;
            answer = string.Empty;
            requestError = string.Empty;

            Debug.Log(
                $"[VRPageController] AI chat attempt {attempt}/{maxAttempts}");

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

            if (requestFinished &&
                !string.IsNullOrWhiteSpace(answer))
            {
                break;
            }

            bool retryable =
                IsRetryableAIChatError(requestError);

            if (!retryable || attempt >= maxAttempts)
            {
                break;
            }

            float retryDelay =
                attempt == 1 ? 1.5f : 3.0f;

            Debug.LogWarning(
                $"[VRPageController] Gemini temporarily unavailable. " +
                $"Retrying in {retryDelay:0.0}s...");

            if (aiChatTyping != null)
            {
                aiChatTyping.text =
                    $"AI đang bận, đang thử lại ({attempt + 1}/{maxAttempts})...";
            }

            yield return new WaitForSecondsRealtime(retryDelay);
        }

        if (aiChatTyping != null)
            aiChatTyping.text = "AI đang trả lời...";

        SetAIChatTyping(false);

        if (!requestFinished ||
            string.IsNullOrWhiteSpace(answer))
        {
            Debug.LogError(
                "[VRPageController] VR AI chat failed: " +
                requestError);

            if (!string.IsNullOrWhiteSpace(requestError) &&
                requestError.IndexOf(
                    "too long",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                answer =
                    "Nội dung gửi tới AI vẫn vượt giới hạn. " +
                    "Mình đã tự rút gọn context; bạn hãy thử gửi lại câu hỏi.";
            }
            else if (IsRetryableAIChatError(requestError))
            {
                answer =
                    "AI đang có lượng truy cập cao nên chưa phản hồi được. " +
                    "Bạn thử lại sau vài giây nhé.";
            }
            else
            {
                answer =
                    "Mình chưa nhận được phản hồi từ AI. " +
                    "Bạn thử gửi lại câu hỏi nhé.";
            }
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

    private static bool IsRetryableAIChatError(
        string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return true;

        string value = error.ToLowerInvariant();

        return
            value.Contains("503") ||
            value.Contains("service unavailable") ||
            value.Contains("high demand") ||
            value.Contains("temporarily") ||
            value.Contains("unavailable") ||
            value.Contains("gemini api request failed") ||
            value.Contains("429") ||
            value.Contains("rate limit");
    }


    private string BuildVRContextPrompt(
        string userQuestion)
    {
        string lessonTitle =
            PlayerPrefs.GetString(
                "selected_lesson_title",
                PlayerPrefs.GetString(
                    "selected_model_lesson_title",
                    "Bài học hiện tại"));

        string modelName =
            PlayerPrefs.GetString(
                "selected_model_name",
                string.Empty);

        if (string.IsNullOrWhiteSpace(modelName) &&
            modelCatalog != null &&
            modelCatalog.CurrentModel != null)
        {
            modelName = modelCatalog.CurrentModel.name;
        }

        if (string.IsNullOrWhiteSpace(modelName))
            modelName = "Mô hình 3D hiện tại";

        string partsContext =
            BuildCurrentModelPartsContext(userQuestion);

        string prompt =
            "Bạn là trợ lý AI học tập trong ứng dụng giáo dục 3D. " +
            "Trả lời bằng tiếng Việt, rõ ràng, chính xác và dễ hiểu. " +
            "Ưu tiên dữ liệu bộ phận của mô hình được cung cấp. " +
            "Không tự bịa thông tin nếu dữ liệu không đủ.\n" +
            $"Bài học: {lessonTitle}\n" +
            $"Mô hình: {modelName}\n" +
            "Dữ liệu mô hình:\n" +
            partsContext +
            "\nCâu hỏi: " +
            (userQuestion ?? string.Empty).Trim();

        // ai-chat Edge Function currently rejects oversized `message` payloads.
        // Keep the complete request comfortably below that validation limit.
        const int maxPromptCharacters = 3400;
        if (prompt.Length > maxPromptCharacters)
        {
            int questionReserve =
                Mathf.Min(
                    600,
                    (userQuestion ?? string.Empty).Length + 20);

            int contextLimit =
                Mathf.Max(
                    800,
                    maxPromptCharacters - questionReserve - 350);

            if (partsContext.Length > contextLimit)
            {
                partsContext =
                    partsContext.Substring(0, contextLimit) +
                    "\n...(đã rút gọn dữ liệu model để phù hợp giới hạn AI chat)";
            }

            prompt =
                "Bạn là trợ lý AI học tập trong ứng dụng giáo dục 3D. " +
                "Trả lời bằng tiếng Việt, chính xác, ngắn gọn và dễ hiểu.\n" +
                $"Bài học: {lessonTitle}\n" +
                $"Mô hình: {modelName}\n" +
                "Dữ liệu mô hình:\n" +
                partsContext +
                "\nCâu hỏi: " +
                (userQuestion ?? string.Empty).Trim();
        }

        if (prompt.Length > maxPromptCharacters)
        {
            prompt =
                prompt.Substring(0, maxPromptCharacters - 3) +
                "...";
        }

        Debug.Log(
            "[VRPageController] AI chat prompt length = " +
            prompt.Length);

        return prompt;
    }


    private string BuildCurrentModelPartsContext(
        string userQuestion)
    {
        if (detailService == null ||
            detailService.CurrentParts == null ||
            detailService.CurrentParts.Count == 0)
        {
            return "(Chưa có dữ liệu label của mô hình.)";
        }

        string normalizedQuestion =
            NormalizeChatSearchText(userQuestion);

        List<VRModelDetailService.ModelPartData> matched =
            new List<VRModelDetailService.ModelPartData>();

        List<VRModelDetailService.ModelPartData> fallback =
            new List<VRModelDetailService.ModelPartData>();

        foreach (VRModelDetailService.ModelPartData part
                 in detailService.CurrentParts)
        {
            if (part == null || !part.is_active)
                continue;

            fallback.Add(part);

            string partName =
                NormalizeChatSearchText(part.part_name);
            string partKey =
                NormalizeChatSearchText(part.part_key);

            if (!string.IsNullOrWhiteSpace(normalizedQuestion) &&
                ((!string.IsNullOrWhiteSpace(partName) &&
                  normalizedQuestion.Contains(partName)) ||
                 (!string.IsNullOrWhiteSpace(partKey) &&
                  normalizedQuestion.Contains(partKey))))
            {
                matched.Add(part);
            }
        }

        // If the student mentions a specific label, send rich details only for
        // matching labels. Otherwise send a compact overview of the model.
        List<VRModelDetailService.ModelPartData> source =
            matched.Count > 0
                ? matched
                : fallback;

        int maxParts =
            matched.Count > 0
                ? 4
                : 10;

        System.Text.StringBuilder builder =
            new System.Text.StringBuilder();

        int appended = 0;
        for (int i = 0;
             i < source.Count && appended < maxParts;
             i++)
        {
            VRModelDetailService.ModelPartData part = source[i];
            if (part == null)
                continue;

            string displayName =
                string.IsNullOrWhiteSpace(part.part_name)
                    ? part.part_key
                    : part.part_name;

            builder.Append("- ");
            builder.Append(displayName);

            if (matched.Count > 0)
            {
                AppendCompactChatField(
                    builder,
                    " | Mô tả: ",
                    part.description,
                    260);

                AppendCompactChatField(
                    builder,
                    " | Cấu tạo: ",
                    part.structure_description,
                    260);

                AppendCompactChatField(
                    builder,
                    " | Chức năng: ",
                    part.function_description,
                    260);
            }
            else
            {
                // Overview requests only need enough context to know which
                // labels exist. A short description helps without overflowing
                // the Edge Function's message-length validation.
                AppendCompactChatField(
                    builder,
                    " | ",
                    part.description,
                    110);
            }

            builder.AppendLine();
            appended++;
        }

        if (fallback.Count > appended && matched.Count == 0)
        {
            builder.Append("- ... và ");
            builder.Append(fallback.Count - appended);
            builder.Append(" bộ phận khác trên mô hình.");
        }

        return builder.Length > 0
            ? builder.ToString().TrimEnd()
            : "(Chưa có bộ phận active.)";
    }


    private static void AppendCompactChatField(
        System.Text.StringBuilder builder,
        string prefix,
        string value,
        int maxLength)
    {
        if (builder == null ||
            string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string compact =
            value.Trim()
                .Replace("\r", " ")
                .Replace("\n", " ");

        while (compact.Contains("  "))
            compact = compact.Replace("  ", " ");

        if (compact.Length > maxLength)
            compact = compact.Substring(0, maxLength - 3) + "...";

        builder.Append(prefix);
        builder.Append(compact);
    }


    private static string NormalizeChatSearchText(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim()
            .ToLowerInvariant()
            .Replace("_", " ")
            .Replace("-", " ");
    }


    private void AddAIChatBubble(
        string text,
        bool fromUser)
    {
        if (aiChatMessageContainer == null)
            return;

        aiChatMessageContainer.style.width =
            Length.Percent(100f);
        aiChatMessageContainer.style.alignItems =
            Align.Stretch;

        VisualElement row =
            new VisualElement();

        row.style.width = Length.Percent(100f);
        row.style.flexGrow = 0f;
        row.style.flexShrink = 0f;
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent =
            fromUser
                ? Justify.FlexEnd
                : Justify.FlexStart;
        row.style.alignItems = Align.FlexStart;
        row.style.marginTop = 4f;
        row.style.marginBottom = 4f;
        row.style.paddingLeft = 3f;
        row.style.paddingRight = 3f;

        string safeText =
            text ?? string.Empty;

        Label bubble =
            new Label(safeText);

        // Important:
        // Do not use the old USS bubble classes here. Previous rules can
        // force a fixed/min height and clip multi-line messages.
        bubble.style.width = StyleKeyword.Auto;
        bubble.style.height = StyleKeyword.Auto;
        bubble.style.minWidth = 0f;
        bubble.style.minHeight = 0f;
        bubble.style.maxWidth = Length.Percent(80f);
        bubble.style.flexGrow = 0f;
        bubble.style.flexShrink = 0f;
        bubble.style.whiteSpace = WhiteSpace.Normal;

        // Keep text visible while the first layout pass is being calculated.
        // The exact bubble width/height is set by FitAIChatBubbleToText below.
        bubble.style.overflow = Overflow.Visible;

        bubble.style.fontSize = 13f;
        bubble.style.unityTextAlign = TextAnchor.UpperLeft;
        bubble.style.paddingLeft = 11f;
        bubble.style.paddingRight = 11f;
        bubble.style.paddingTop = 8f;
        bubble.style.paddingBottom = 8f;

        bubble.style.borderTopLeftRadius = 12f;
        bubble.style.borderTopRightRadius = 12f;
        bubble.style.borderBottomLeftRadius = 12f;
        bubble.style.borderBottomRightRadius = 12f;

        bubble.style.backgroundColor =
            fromUser
                ? new Color(0.10f, 0.38f, 0.92f, 1f)
                : new Color(0.91f, 0.94f, 0.98f, 1f);

        bubble.style.color =
            fromUser
                ? Color.white
                : new Color(0.10f, 0.15f, 0.24f, 1f);

        row.Add(bubble);
        aiChatMessageContainer.Add(row);

        // UI Toolkit needs one layout pass before the ScrollView width is
        // reliable. Then measure the real wrapped text and explicitly resize
        // the background so no line is clipped.
        bubble.schedule.Execute(
            () =>
            {
                FitAIChatBubbleToText(
                    bubble,
                    safeText);

                aiChatMessages?.ScrollTo(row);
            });
    }


    private void FitAIChatBubbleToText(
        Label bubble,
        string text)
    {
        if (bubble == null)
            return;

        float viewportWidth = 0f;

        if (aiChatMessages != null &&
            aiChatMessages.contentViewport != null)
        {
            viewportWidth =
                aiChatMessages.contentViewport.resolvedStyle.width;
        }

        if (float.IsNaN(viewportWidth) ||
            viewportWidth <= 1f)
        {
            viewportWidth =
                aiChatPanel != null
                    ? aiChatPanel.resolvedStyle.width - 36f
                    : 260f;
        }

        if (float.IsNaN(viewportWidth) ||
            viewportWidth <= 1f)
        {
            viewportWidth = 260f;
        }

        float maxBubbleWidth =
            Mathf.Max(
                120f,
                viewportWidth * 0.80f);

        // Avoid MeasureTextSize/MeasureMode because the enum/API differs
        // between Unity UI Toolkit versions. Estimate a compact width for
        // short messages, and let UI Toolkit wrap + auto-size the height.
        int characterCount =
            string.IsNullOrEmpty(text)
                ? 0
                : text.Length;

        float estimatedWidth =
            30f + characterCount * 6.8f;

        float desiredWidth =
            Mathf.Clamp(
                estimatedWidth,
                54f,
                maxBubbleWidth);

        bubble.style.width = desiredWidth;
        bubble.style.maxWidth = maxBubbleWidth;

        // Critical: no fixed height. WhiteSpace.Normal + auto height lets
        // multi-line text expand the bubble instead of being clipped.
        bubble.style.height = StyleKeyword.Auto;
        bubble.style.minHeight = 0f;
        bubble.style.flexGrow = 0f;
        bubble.style.flexShrink = 0f;
        bubble.style.whiteSpace = WhiteSpace.Normal;

        // Keep all wrapped lines visible inside the message area.
        bubble.style.overflow = Overflow.Visible;
    }


    private void SetAIChatTyping(bool visible)
    {
        if (aiChatTyping == null)
            return;

        aiChatTyping.style.display =
            visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;

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
