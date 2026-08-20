using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class Mode3DPageController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private UIDocument uiDocument;

    [Header("3D Model")]
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Renderer[] modelRenderers;
    [SerializeField] private Camera modelCamera;

    [Header("Automatic model fitting")]
    [SerializeField] private bool fitModelOnStart = true;

    [Tooltip("Maximum fraction of screen height occupied by the model.")]
    [Range(0.08f, 0.35f)]
    [SerializeField] private float targetViewportHeight = 0.13f;

    [Tooltip("Maximum fraction of screen width occupied by the model.")]
    [Range(0.12f, 0.60f)]
    [SerializeField] private float targetViewportWidth = 0.28f;

    [Tooltip("Vertical position matching the center of the Figma glass board.")]
    [Range(0.25f, 0.75f)]
    [SerializeField] private float targetViewportCenterY = 0.475f;

    [SerializeField] private float fitDelaySeconds = 0.15f;

    [Header("Model controls")]
    [SerializeField] private string modelTitle = "Engine Assembly · V6";
    [SerializeField] private float rotationSpeed = 0.22f;
    [SerializeField] private float autoRotateSpeed = 22f;
    [SerializeField] private float zoomStepPercent = 0.12f;
    [SerializeField] private float minZoomMultiplier = 0.55f;
    [SerializeField] private float maxZoomMultiplier = 2.20f;

    [Header("Navigation")]
    [SerializeField] private string previousSceneName = "ShowLessonScene";
    [SerializeField] private string vrSceneName = "VRModeScene";

    private VisualElement root;
    private VisualElement interactionArea;
    private VisualElement infoOverlay;
    private Label titleLabel;
    private Label toastLabel;

    private Button backButton;
    private Button infoButton;
    private Button closeInfoButton;
    private Button captureButton;
    private Button vrButton;
    private Button resetButton;
    private Button zoomButton;
    private Button focusButton;
    private Button layersButton;
    private Button autoRotateButton;

    private VisualElement resetFrame;
    private VisualElement zoomFrame;
    private VisualElement focusFrame;
    private VisualElement layersFrame;
    private VisualElement autoRotateFrame;

    private Button colorBlue;
    private Button colorDark;
    private Button colorGreen;
    private Button colorPurple;

    private Vector3 fittedPosition;
    private Quaternion fittedRotation;
    private Vector3 fittedScale;

    private bool hasFittedPose;
    private bool dragging;
    private bool autoRotate;
    private bool explodedView;

    private Vector2 lastPointerPosition;

    private MaterialPropertyBlock propertyBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (modelCamera == null)
            modelCamera = Camera.main;

        if (modelRoot == null)
        {
            GameObject foundRoot = GameObject.Find("ModelRoot");
            if (foundRoot != null)
                modelRoot = foundRoot.transform;
        }

        propertyBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        if (fitModelOnStart)
            StartCoroutine(FitModelAfterLoading());
        else
            SaveCurrentPoseAsDefault();
    }

    private void OnEnable()
    {
        if (uiDocument == null)
        {
            Debug.LogError("[Mode3D] UIDocument is missing.");
            return;
        }

        root = uiDocument.rootVisualElement;

        interactionArea = root.Q<VisualElement>("model-interaction-area");
        infoOverlay = root.Q<VisualElement>("info-overlay");
        titleLabel = root.Q<Label>("model-title-label");
        toastLabel = root.Q<Label>("toast-label");

        backButton = root.Q<Button>("back-button");
        infoButton = root.Q<Button>("info-button");
        closeInfoButton = root.Q<Button>("close-info-button");
        captureButton = root.Q<Button>("capture-button");
        vrButton = root.Q<Button>("vr-button");
        resetButton = root.Q<Button>("reset-button");
        zoomButton = root.Q<Button>("zoom-button");
        focusButton = root.Q<Button>("focus-button");
        layersButton = root.Q<Button>("layers-button");
        autoRotateButton = root.Q<Button>("auto-rotate-button");

        resetFrame = root.Q<VisualElement>("reset-frame");
        zoomFrame = root.Q<VisualElement>("zoom-frame");
        focusFrame = root.Q<VisualElement>("focus-frame");
        layersFrame = root.Q<VisualElement>("layers-frame");
        autoRotateFrame = root.Q<VisualElement>("auto-rotate-frame");

        colorBlue = root.Q<Button>("color-blue");
        colorDark = root.Q<Button>("color-dark");
        colorGreen = root.Q<Button>("color-green");
        colorPurple = root.Q<Button>("color-purple");

        if (titleLabel != null)
            titleLabel.text = modelTitle;

        RegisterCallbacks();
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
    }

    private void Update()
    {
        if (autoRotate && modelRoot != null && !dragging)
            modelRoot.Rotate(Vector3.up, autoRotateSpeed * Time.deltaTime, Space.World);
    }

    private IEnumerator FitModelAfterLoading()
    {
        if (fitDelaySeconds > 0f)
            yield return new WaitForSeconds(fitDelaySeconds);
        else
            yield return null;

        // A runtime GLB loader may create renderers one or more frames later.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            RefreshRenderers();

            if (modelRenderers != null && modelRenderers.Length > 0)
                break;

            yield return null;
        }

        FitModelToPresentationBoard();
    }

    [ContextMenu("Fit Model To Presentation Board")]
    public void FitModelToPresentationBoard()
    {
        if (modelRoot == null)
        {
            Debug.LogWarning("[Mode3D] Model Root is not assigned.");
            return;
        }

        if (modelCamera == null)
            modelCamera = Camera.main;

        if (modelCamera == null)
        {
            Debug.LogWarning("[Mode3D] Model Camera is not assigned.");
            return;
        }

        RefreshRenderers();

        if (!TryGetCombinedBounds(out Bounds bounds))
        {
            Debug.LogWarning("[Mode3D] No Renderer was found under Model Root.");
            SaveCurrentPoseAsDefault();
            return;
        }

        // First center the model bounds on the root position.
        Vector3 targetWorldCenter = GetTargetWorldCenter(bounds.center);
        modelRoot.position += targetWorldCenter - bounds.center;

        // Bounds must be recalculated after moving the root.
        if (!TryGetCombinedBounds(out bounds))
            return;

        float distance = Mathf.Abs(
            Vector3.Dot(bounds.center - modelCamera.transform.position, modelCamera.transform.forward)
        );

        distance = Mathf.Max(distance, modelCamera.nearClipPlane + 0.5f);

        float availableHeight;

        if (modelCamera.orthographic)
        {
            availableHeight = modelCamera.orthographicSize * 2f;
        }
        else
        {
            float verticalFovRadians = modelCamera.fieldOfView * Mathf.Deg2Rad;
            availableHeight = 2f * distance * Mathf.Tan(verticalFovRadians * 0.5f);
        }

        float availableWidth = availableHeight * modelCamera.aspect;

        float desiredWorldHeight = availableHeight * targetViewportHeight;
        float desiredWorldWidth = availableWidth * targetViewportWidth;

        float currentHeight = Mathf.Max(bounds.size.y, 0.0001f);
        float currentWidth = Mathf.Max(bounds.size.x, 0.0001f);

        float heightScaleFactor = desiredWorldHeight / currentHeight;
        float widthScaleFactor = desiredWorldWidth / currentWidth;

        // Use the smaller factor so the model always fits inside the Figma board.
        float scaleFactor = Mathf.Min(heightScaleFactor, widthScaleFactor);
        scaleFactor = Mathf.Clamp(scaleFactor, 0.0001f, 1000f);

        modelRoot.localScale *= scaleFactor;

        // Recenter once more because scale changes the bounds center.
        if (TryGetCombinedBounds(out bounds))
        {
            targetWorldCenter = GetTargetWorldCenter(bounds.center);
            modelRoot.position += targetWorldCenter - bounds.center;
        }

        SaveCurrentPoseAsDefault();

        Debug.Log(
            $"[Mode3D] Model fitted. Renderer count: {modelRenderers.Length}, " +
            $"scale: {modelRoot.localScale}, viewport: {targetViewportWidth} x {targetViewportHeight}"
        );
    }

    private Vector3 GetTargetWorldCenter(Vector3 currentBoundsCenter)
    {
        float distance = Mathf.Abs(
            Vector3.Dot(currentBoundsCenter - modelCamera.transform.position, modelCamera.transform.forward)
        );

        distance = Mathf.Max(distance, modelCamera.nearClipPlane + 0.5f);

        Vector3 viewportPoint = new Vector3(
            0.5f,
            targetViewportCenterY,
            distance
        );

        return modelCamera.ViewportToWorldPoint(viewportPoint);
    }

    private void RefreshRenderers()
    {
        if (modelRoot == null)
            return;

        modelRenderers = modelRoot.GetComponentsInChildren<Renderer>(true);
    }

    private bool TryGetCombinedBounds(out Bounds combinedBounds)
    {
        combinedBounds = default;

        if (modelRenderers == null || modelRenderers.Length == 0)
            return false;

        bool foundRenderer = false;

        foreach (Renderer rendererItem in modelRenderers)
        {
            if (rendererItem == null || !rendererItem.enabled)
                continue;

            if (!rendererItem.gameObject.activeInHierarchy)
                continue;

            if (!foundRenderer)
            {
                combinedBounds = rendererItem.bounds;
                foundRenderer = true;
            }
            else
            {
                combinedBounds.Encapsulate(rendererItem.bounds);
            }
        }

        return foundRenderer;
    }

    private void SaveCurrentPoseAsDefault()
    {
        if (modelRoot == null)
            return;

        fittedPosition = modelRoot.localPosition;
        fittedRotation = modelRoot.localRotation;
        fittedScale = modelRoot.localScale;
        hasFittedPose = true;
    }

    private void RegisterCallbacks()
    {
        if (backButton != null) backButton.clicked += GoBack;
        if (infoButton != null) infoButton.clicked += ShowInfo;
        if (closeInfoButton != null) closeInfoButton.clicked += HideInfo;
        if (captureButton != null) captureButton.clicked += CaptureScreenshot;
        if (vrButton != null) vrButton.clicked += OpenVRScene;
        if (resetButton != null) resetButton.clicked += ResetModel;
        if (zoomButton != null) zoomButton.clicked += ZoomIn;
        if (focusButton != null) focusButton.clicked += FocusModel;
        if (layersButton != null) layersButton.clicked += ToggleExplodedView;
        if (autoRotateButton != null) autoRotateButton.clicked += ToggleAutoRotate;

        if (colorBlue != null)
            colorBlue.clicked += OnBlueSelected;

        if (colorDark != null)
            colorDark.clicked += OnDarkSelected;

        if (colorGreen != null)
            colorGreen.clicked += OnGreenSelected;

        if (colorPurple != null)
            colorPurple.clicked += OnPurpleSelected;

        if (interactionArea != null)
        {
            interactionArea.RegisterCallback<PointerDownEvent>(OnPointerDown);
            interactionArea.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            interactionArea.RegisterCallback<PointerUpEvent>(OnPointerUp);
            interactionArea.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            interactionArea.RegisterCallback<WheelEvent>(OnWheel);
        }
    }

    private void UnregisterCallbacks()
    {
        if (backButton != null) backButton.clicked -= GoBack;
        if (infoButton != null) infoButton.clicked -= ShowInfo;
        if (closeInfoButton != null) closeInfoButton.clicked -= HideInfo;
        if (captureButton != null) captureButton.clicked -= CaptureScreenshot;
        if (vrButton != null) vrButton.clicked -= OpenVRScene;
        if (resetButton != null) resetButton.clicked -= ResetModel;
        if (zoomButton != null) zoomButton.clicked -= ZoomIn;
        if (focusButton != null) focusButton.clicked -= FocusModel;
        if (layersButton != null) layersButton.clicked -= ToggleExplodedView;
        if (autoRotateButton != null) autoRotateButton.clicked -= ToggleAutoRotate;

        if (colorBlue != null)
            colorBlue.clicked -= OnBlueSelected;

        if (colorDark != null)
            colorDark.clicked -= OnDarkSelected;

        if (colorGreen != null)
            colorGreen.clicked -= OnGreenSelected;

        if (colorPurple != null)
            colorPurple.clicked -= OnPurpleSelected;

        if (interactionArea != null)
        {
            interactionArea.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            interactionArea.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            interactionArea.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            interactionArea.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            interactionArea.UnregisterCallback<WheelEvent>(OnWheel);
        }
    }

    private void OnBlueSelected()
    {
        SetModelColor(new Color32(49, 103, 228, 255), colorBlue);
    }

    private void OnDarkSelected()
    {
        SetModelColor(new Color32(31, 37, 69, 255), colorDark);
    }

    private void OnGreenSelected()
    {
        SetModelColor(new Color32(34, 163, 82, 255), colorGreen);
    }

    private void OnPurpleSelected()
    {
        SetModelColor(new Color32(126, 55, 218, 255), colorPurple);
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (modelRoot == null || interactionArea == null)
            return;

        dragging = true;
        lastPointerPosition = new Vector2(evt.position.x, evt.position.y);

        interactionArea.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!dragging ||
            modelRoot == null ||
            interactionArea == null ||
            !interactionArea.HasPointerCapture(evt.pointerId))
        {
            return;
        }

        Vector2 currentPointerPosition = new Vector2(
            evt.position.x,
            evt.position.y
        );

        Vector2 delta = currentPointerPosition - lastPointerPosition;
        lastPointerPosition = currentPointerPosition;

        modelRoot.Rotate(
            modelCamera != null ? modelCamera.transform.up : Vector3.up,
            -delta.x * rotationSpeed,
            Space.World
        );

        modelRoot.Rotate(
            modelCamera != null ? modelCamera.transform.right : Vector3.right,
            delta.y * rotationSpeed,
            Space.World
        );

        evt.StopPropagation();
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        dragging = false;

        if (interactionArea != null &&
            interactionArea.HasPointerCapture(evt.pointerId))
        {
            interactionArea.ReleasePointer(evt.pointerId);
        }

        evt.StopPropagation();
    }

    private void OnPointerCancel(PointerCancelEvent evt)
    {
        dragging = false;

        if (interactionArea != null &&
            interactionArea.HasPointerCapture(evt.pointerId))
        {
            interactionArea.ReleasePointer(evt.pointerId);
        }
    }

    private void OnWheel(WheelEvent evt)
    {
        if (evt.delta.y > 0f)
            ChangeZoom(-zoomStepPercent);
        else if (evt.delta.y < 0f)
            ChangeZoom(zoomStepPercent);

        evt.StopPropagation();
    }

    private void ChangeZoom(float percent)
    {
        if (modelRoot == null || !hasFittedPose)
            return;

        float currentMultiplier = modelRoot.localScale.x / Mathf.Max(fittedScale.x, 0.0001f);
        float targetMultiplier = Mathf.Clamp(
            currentMultiplier + percent,
            minZoomMultiplier,
            maxZoomMultiplier
        );

        modelRoot.localScale = fittedScale * targetMultiplier;
    }

    private void ZoomIn()
    {
        SelectBottomTool(zoomFrame);
        ChangeZoom(zoomStepPercent);
        ShowToast("Zoom");
    }

    private void ResetModel()
    {
        if (modelRoot == null)
            return;

        if (!hasFittedPose)
            SaveCurrentPoseAsDefault();

        modelRoot.localPosition = fittedPosition;
        modelRoot.localRotation = fittedRotation;
        modelRoot.localScale = fittedScale;

        autoRotate = false;
        explodedView = false;

        SetButtonActive(autoRotateButton, false);
        SetButtonActive(layersButton, false);
        SetButtonActive(resetButton, true);
        SelectBottomTool(resetFrame);

        ShowToast("Model reset");
    }

    private void FocusModel()
    {
        SelectBottomTool(focusFrame);
        FitModelToPresentationBoard();
        SetButtonActive(focusButton, true);
        ShowToast("Model centered");
    }

    private void ToggleAutoRotate()
    {
        SelectBottomTool(autoRotateFrame);
        autoRotate = !autoRotate;
        SetButtonActive(autoRotateButton, autoRotate);
        ShowToast(autoRotate ? "Auto rotation on" : "Auto rotation off");
    }

    private void ToggleExplodedView()
    {
        SelectBottomTool(layersFrame);
        explodedView = !explodedView;
        SetButtonActive(layersButton, explodedView);

        modelRoot?.BroadcastMessage(
            explodedView ? "ShowExplodedView" : "HideExplodedView",
            SendMessageOptions.DontRequireReceiver
        );

        ShowToast(explodedView ? "Exploded view" : "Assembly view");
    }

    private void SetModelColor(Color color, Button selectedButton)
    {
        RefreshRenderers();

        if (modelRenderers == null || modelRenderers.Length == 0)
        {
            Debug.LogWarning("[Mode3D] No model renderers were found.");
            return;
        }

        foreach (Renderer rendererItem in modelRenderers)
        {
            if (rendererItem == null)
                continue;

            rendererItem.GetPropertyBlock(propertyBlock);

            Material sharedMaterial = rendererItem.sharedMaterial;

            if (sharedMaterial != null && sharedMaterial.HasProperty(BaseColorId))
                propertyBlock.SetColor(BaseColorId, color);
            else
                propertyBlock.SetColor(ColorId, color);

            rendererItem.SetPropertyBlock(propertyBlock);
        }

        SetSelectedSwatch(selectedButton);
        ShowToast("Color changed");
    }

    private void SetSelectedSwatch(Button selected)
    {
        Button[] swatches =
        {
            colorBlue,
            colorDark,
            colorGreen,
            colorPurple
        };

        foreach (Button swatch in swatches)
        {
            if (swatch == null)
                continue;

            swatch.EnableInClassList("selected", swatch == selected);
        }
    }

    private static void SetButtonActive(Button button, bool active)
    {
        if (button != null)
            button.EnableInClassList("active", active);
    }

    private void SelectBottomTool(VisualElement selectedFrame)
    {
        VisualElement[] frames =
        {
            resetFrame,
            zoomFrame,
            focusFrame,
            layersFrame,
            autoRotateFrame
        };

        foreach (VisualElement frame in frames)
        {
            if (frame == null)
                continue;

            frame.EnableInClassList(
                "selected-tool-frame",
                frame == selectedFrame
            );
        }
    }

    private void ShowInfo()
    {
        infoOverlay?.RemoveFromClassList("hidden");
    }

    private void HideInfo()
    {
        infoOverlay?.AddToClassList("hidden");
    }

    private void GoBack()
    {
        string rememberedScene = PlayerPrefs.GetString(
            "previous_scene",
            previousSceneName
        );

        if (!string.IsNullOrWhiteSpace(rememberedScene) &&
            Application.CanStreamedLevelBeLoaded(rememberedScene))
        {
            SceneManager.LoadScene(rememberedScene);
            return;
        }

        if (!string.IsNullOrWhiteSpace(previousSceneName))
            SceneManager.LoadScene(previousSceneName);
    }

    private void OpenVRScene()
    {
        if (string.IsNullOrWhiteSpace(vrSceneName))
        {
            ShowToast("VR scene is not configured");
            return;
        }

        if (Application.CanStreamedLevelBeLoaded(vrSceneName))
            SceneManager.LoadScene(vrSceneName);
        else
            ShowToast("Add VR scene to Build Profiles");
    }

    private void CaptureScreenshot()
    {
        string fileName =
            $"Mode3D_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";

        ScreenCapture.CaptureScreenshot(fileName);
        ShowToast("Screenshot saved");

        Debug.Log($"[Mode3D] Screenshot saved as {fileName}");
    }

    private void ShowToast(string message)
    {
        if (toastLabel == null)
            return;

        StopAllCoroutines();
        StartCoroutine(ToastRoutine(message));
    }

    private IEnumerator ToastRoutine(string message)
    {
        toastLabel.text = message;
        toastLabel.RemoveFromClassList("hidden");

        yield return new WaitForSeconds(1.35f);

        toastLabel.AddToClassList("hidden");
    }
}
