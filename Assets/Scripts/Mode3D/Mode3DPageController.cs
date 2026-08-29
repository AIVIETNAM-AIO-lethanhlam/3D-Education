using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using GLTFast;

public class Mode3DPageController : MonoBehaviour
{
    private const string Mode3DSceneName = "Mode3DScene";

    // The current Mode3DScene screenshot shows that Mode3DUIDocument only has
    // a UIDocument component. This bootstrap makes the scene self-healing:
    // whenever Mode3DScene is loaded, attach this controller automatically
    // if it is missing.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterMode3DBootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedBootstrap;
        SceneManager.sceneLoaded += OnSceneLoadedBootstrap;
    }

    private static void OnSceneLoadedBootstrap(
        Scene scene,
        LoadSceneMode mode)
    {
        if (!string.Equals(
                scene.name,
                Mode3DSceneName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Mode3DPageController existing =
            FindAnyObjectByType<Mode3DPageController>();

        if (existing != null)
            return;

        UIDocument[] documents =
            FindObjectsByType<UIDocument>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        UIDocument target = null;

        foreach (UIDocument document in documents)
        {
            if (document == null)
                continue;

            if (string.Equals(
                    document.gameObject.name,
                    "Mode3DUIDocument",
                    StringComparison.OrdinalIgnoreCase))
            {
                target = document;
                break;
            }

            if (target == null)
                target = document;
        }

        if (target == null)
        {
            Debug.LogError(
                "[Mode3D] Bootstrap could not find a UIDocument in Mode3DScene.");
            return;
        }

        Mode3DPageController controller =
            target.gameObject.AddComponent<Mode3DPageController>();

        controller.uiDocument = target;

        Debug.Log(
            "[Mode3D] Mode3DPageController was missing and has been attached automatically to " +
            target.gameObject.name + ".");
    }
    [Header("UI")]
    [SerializeField] private UIDocument uiDocument;

    [Header("3D Model")]
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Renderer[] modelRenderers;
    [SerializeField] private Camera modelCamera;

    [Header("Mode3D camera composition")]
    [Tooltip("The UI root is transparent; this camera color preserves the original navy background.")]
    [SerializeField] private Color mode3DBackgroundColor =
        new Color(5f / 255f, 18f / 255f, 45f / 255f, 1f);

    [Tooltip("Layer forced onto runtime-loaded GLB objects so Main Camera can render them.")]
    [SerializeField] private int runtimeModelLayer = 0;

    [Header("Runtime model from ShowLessonScene")]
    [Tooltip("Load selected_model_url passed by ShowLessonScene when Mode3DScene opens.")]
    [SerializeField] private bool loadRuntimeModelFromPlayerPrefs = true;

    [Tooltip("Remove placeholder/model children already under ModelRoot before loading the lesson GLB.")]
    [SerializeField] private bool clearExistingModelChildren = true;

    private bool runtimeModelLoaded;
    private bool runtimeModelLoading;

    // Keep the glTFast importer alive for as long as this scene uses the
    // instantiated model. glTFast owns meshes/materials/textures imported from
    // the GLB; disposing it immediately after InstantiateMainSceneAsync can leave
    // Renderer components in the hierarchy but their assets invalid/invisible.
    private GltfImport activeGltfImport;

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
    [SerializeField] private float targetViewportCenterY = 0.545f;

    [Header("Touch gestures")]
    [Tooltip("Horizontal one-finger drag rotates the model left/right.")]
    [SerializeField] private float touchRotationSensitivity = 0.22f;

    [Tooltip("Two-finger pinch controls model zoom.")]
    [SerializeField] private float pinchZoomSensitivity = 1.0f;

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

    private async void Start()
    {
        Debug.Log(
            "[Mode3D] Controller started." +
            $"\nselected_model_name: {PlayerPrefs.GetString("selected_model_name", string.Empty)}" +
            $"\nselected_model_url present: {!string.IsNullOrWhiteSpace(PlayerPrefs.GetString("selected_model_url", string.Empty))}" +
            $"\nprevious_scene: {PlayerPrefs.GetString("previous_scene", previousSceneName)}");

        if (loadRuntimeModelFromPlayerPrefs)
        {
            bool loaded = await LoadCurrentLessonModelAsync();

            if (!this)
                return;

            if (loaded)
            {
                // Let Unity finish creating renderer bounds before fitting.
                StartCoroutine(FitRuntimeModelAfterAsyncLoad());
            }
            else if (fitModelOnStart)
            {
                StartCoroutine(FitModelAfterLoading());
            }
            else
            {
                SaveCurrentPoseAsDefault();
            }

            return;
        }

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

        ConfigureMode3DCamera();

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
        {
            string selectedModelName =
                PlayerPrefs.GetString("selected_model_name", string.Empty);

            titleLabel.text =
                !string.IsNullOrWhiteSpace(selectedModelName)
                    ? selectedModelName
                    : modelTitle;
        }

        RegisterCallbacks();
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
    }

    private void OnDestroy()
    {
        if (activeGltfImport != null)
        {
            try
            {
                activeGltfImport.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Mode3D] Failed to dispose glTFast importer during scene cleanup: " +
                    exception.Message);
            }

            activeGltfImport = null;
        }
    }

    private void Update()
    {
        HandleTouchGestures();

        if (autoRotate && modelRoot != null && !dragging && Input.touchCount == 0)
            modelRoot.Rotate(Vector3.up, autoRotateSpeed * Time.deltaTime, Space.World);
    }

    /// <summary>
    /// Loads the model selected by ShowLessonScene directly with glTFast.
    /// This intentionally uses glTFast's typed API instead of reflection because
    /// the installed package version already supports:
    ///     new GltfImport()
    ///     Load(Uri)
    ///     InstantiateMainSceneAsync(Transform)
    /// and this is the same API used successfully elsewhere in the project.
    /// </summary>
    private async Task<bool> LoadCurrentLessonModelAsync()
    {
        if (runtimeModelLoading)
            return false;

        runtimeModelLoading = true;

        string modelUrl =
            PlayerPrefs.GetString(
                "selected_model_url",
                string.Empty).Trim();

        string storagePath =
            PlayerPrefs.GetString(
                "selected_model_storage_path",
                string.Empty).Trim();

        string modelName =
            PlayerPrefs.GetString(
                "selected_model_name",
                string.Empty).Trim();

        string fileName =
            PlayerPrefs.GetString(
                "selected_model_file_name",
                string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(modelUrl) &&
            IsHttpUrl(storagePath))
        {
            modelUrl = storagePath;
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            modelName =
                !string.IsNullOrWhiteSpace(fileName)
                    ? Path.GetFileNameWithoutExtension(fileName)
                    : "3D Model";
        }

        if (titleLabel != null)
            titleLabel.text = modelName;

        if (string.IsNullOrWhiteSpace(modelUrl))
        {
            runtimeModelLoading = false;

            Debug.LogError(
                "[Mode3D] selected_model_url is empty. " +
                "Open Mode3DScene from ShowLessonScene.");

            ShowToast("Model URL is missing");
            return false;
        }

        if (!Uri.TryCreate(
                modelUrl,
                UriKind.Absolute,
                out Uri modelUri))
        {
            runtimeModelLoading = false;

            Debug.LogError(
                "[Mode3D] Invalid model URL:\n" +
                modelUrl);

            ShowToast("Invalid model URL");
            return false;
        }

        if (modelRoot == null)
        {
            GameObject rootObject =
                new GameObject("ModelRoot");

            modelRoot = rootObject.transform;
        }

        if (clearExistingModelChildren)
        {
            for (int i = modelRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = modelRoot.GetChild(i);

                if (child != null)
                    Destroy(child.gameObject);
            }

            // Destroy executes at end of frame. Since this method is async rather
            // than a coroutine, move old children away immediately as well.
            modelRoot.DetachChildren();
        }

        ShowToast("Loading 3D model...");

        Debug.Log(
            "[Mode3D] Loading lesson model with glTFast." +
            $"\nName: {modelName}" +
            $"\nURL: {modelUrl}");

        try
        {
            // Dispose only the PREVIOUS import when switching/reloading a model.
            // Do not dispose the importer that backs the currently visible model.
            if (activeGltfImport != null)
            {
                try
                {
                    activeGltfImport.Dispose();
                }
                catch
                {
                    // Ignore cleanup errors from an older import.
                }

                activeGltfImport = null;
            }

            activeGltfImport = new GltfImport();

            bool loaded =
                await activeGltfImport.Load(modelUri);

            if (!loaded)
            {
                runtimeModelLoading = false;

                Debug.LogError(
                    "[Mode3D] glTFast could not load the GLB." +
                    $"\nName: {modelName}" +
                    $"\nURL: {modelUrl}");

                ShowToast("Cannot load model");
                activeGltfImport?.Dispose();
                activeGltfImport = null;
                return false;
            }

            bool instantiated =
                await activeGltfImport.InstantiateMainSceneAsync(
                    modelRoot);

            if (!instantiated)
            {
                runtimeModelLoading = false;

                Debug.LogError(
                    "[Mode3D] glTFast loaded the GLB but could not instantiate its main scene.");

                ShowToast("Cannot display model");
                activeGltfImport?.Dispose();
                activeGltfImport = null;
                return false;
            }

            runtimeModelLoaded = true;
            runtimeModelLoading = false;

            DisableImportedCamerasAndLights(modelRoot);
            ForceRuntimeModelVisible(modelRoot);

            Renderer[] importedRenderers =
                modelRoot.GetComponentsInChildren<Renderer>(true);

            int rendererWithMaterialCount = 0;
            int rendererWithMeshCount = 0;

            foreach (Renderer importedRenderer in importedRenderers)
            {
                if (importedRenderer == null)
                    continue;

                if (importedRenderer.sharedMaterials != null &&
                    importedRenderer.sharedMaterials.Length > 0)
                {
                    rendererWithMaterialCount++;
                }

                if (importedRenderer is SkinnedMeshRenderer skinned &&
                    skinned.sharedMesh != null)
                {
                    rendererWithMeshCount++;
                }
                else
                {
                    MeshFilter filter =
                        importedRenderer.GetComponent<MeshFilter>();

                    if (filter != null && filter.sharedMesh != null)
                        rendererWithMeshCount++;
                }
            }

            Debug.Log(
                "[Mode3D] GLB instantiated successfully." +
                $"\nName: {modelName}" +
                $"\nChildren under ModelRoot: {modelRoot.childCount}" +
                $"\nRenderers: {importedRenderers.Length}" +
                $"\nRenderers with mesh: {rendererWithMeshCount}" +
                $"\nRenderers with materials: {rendererWithMaterialCount}");

            // IMPORTANT: keep activeGltfImport alive. It owns the imported
            // meshes/materials/textures used by the instantiated renderers.
            return true;
        }
        catch (Exception exception)
        {
            runtimeModelLoading = false;

            try
            {
                activeGltfImport?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors.
            }

            activeGltfImport = null;

            Debug.LogError(
                "[Mode3D] Runtime model load exception:\n" +
                exception);

            ShowToast("Cannot display model");
            return false;
        }
    }

    private IEnumerator FitRuntimeModelAfterAsyncLoad()
    {
        // Wait a few frames because renderer bounds can settle after async GLB import.
        for (int i = 0; i < 4; i++)
            yield return null;

        RefreshRenderers();

        Debug.Log(
            "[Mode3D] Preparing runtime model fit." +
            $"\nRenderer count: {(modelRenderers == null ? 0 : modelRenderers.Length)}");

        if (modelRenderers == null ||
            modelRenderers.Length == 0)
        {
            Debug.LogError(
                "[Mode3D] Model was instantiated but no Renderer was found under ModelRoot.");
            ShowToast("Model has no renderer");
            yield break;
        }

        if (fitModelOnStart)
            yield return FitModelAfterLoading();
        else
            SaveCurrentPoseAsDefault();

        ShowToast("Model loaded");

        Debug.Log(
            "[Mode3D] Runtime lesson model is ready." +
            $"\nRenderer count: {modelRenderers.Length}" +
            $"\nModelRoot position: {modelRoot.position}" +
            $"\nModelRoot scale: {modelRoot.localScale}" +
            BuildVisibilityDiagnostic());
    }

    private void ConfigureMode3DCamera()
    {
        if (modelCamera == null)
            modelCamera = Camera.main;

        if (modelCamera == null)
        {
            Debug.LogWarning("[Mode3D] Main Camera was not found.");
            return;
        }

        // UI Toolkit is rendered after the camera. The previous `.screen` USS rule
        // used an opaque navy background and therefore covered the successfully
        // loaded 3D model. The camera now supplies that same navy background.
        modelCamera.clearFlags = CameraClearFlags.SolidColor;
        modelCamera.backgroundColor = mode3DBackgroundColor;

        int safeLayer = Mathf.Clamp(runtimeModelLayer, 0, 31);
        modelCamera.cullingMask |= 1 << safeLayer;

        Debug.Log(
            "[Mode3D] Camera configured for 3D visibility." +
            $"\nCamera: {modelCamera.name}" +
            $"\nCullingMask: {modelCamera.cullingMask}");
    }

    private void ForceRuntimeModelVisible(Transform rootTransform)
    {
        if (rootTransform == null)
            return;

        int safeLayer = Mathf.Clamp(runtimeModelLayer, 0, 31);

        SetLayerRecursively(rootTransform.gameObject, safeLayer);

        Renderer[] renderers =
            rootTransform.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer rendererItem in renderers)
        {
            if (rendererItem == null)
                continue;

            rendererItem.enabled = true;

            if (!rendererItem.gameObject.activeSelf)
                rendererItem.gameObject.SetActive(true);
        }

        if (modelCamera == null)
            modelCamera = Camera.main;

        if (modelCamera != null)
            modelCamera.cullingMask |= 1 << safeLayer;

        Debug.Log(
            "[Mode3D] Runtime model visibility normalized." +
            $"\nLayer: {safeLayer}" +
            $"\nRenderer count: {renderers.Length}");
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null)
            return;

        target.layer = layer;

        Transform targetTransform = target.transform;

        for (int i = 0; i < targetTransform.childCount; i++)
        {
            Transform child = targetTransform.GetChild(i);

            if (child != null)
                SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static void DisableImportedCamerasAndLights(
        Transform rootTransform)
    {
        if (rootTransform == null)
            return;

        Camera[] importedCameras =
            rootTransform.GetComponentsInChildren<Camera>(true);

        foreach (Camera importedCamera in importedCameras)
        {
            if (importedCamera != null)
                importedCamera.enabled = false;
        }

        Light[] importedLights =
            rootTransform.GetComponentsInChildren<Light>(true);

        foreach (Light importedLight in importedLights)
        {
            if (importedLight != null)
                importedLight.enabled = false;
        }
    }

    private string BuildVisibilityDiagnostic()
    {
        if (modelCamera == null ||
            !TryGetCombinedBounds(out Bounds bounds))
        {
            return "\nVisibility diagnostic: unavailable";
        }

        Vector3 viewport =
            modelCamera.WorldToViewportPoint(bounds.center);

        return
            $"\nBounds center viewport: ({viewport.x:F3}, {viewport.y:F3}, {viewport.z:F3})" +
            $"\nCamera enabled: {modelCamera.enabled}" +
            $"\nCamera active: {modelCamera.gameObject.activeInHierarchy}";
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

    private void HandleTouchGestures()
    {
        if (modelRoot == null || !hasFittedPose)
            return;

        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Moved)
            {
                dragging = true;

                Vector3 upAxis =
                    modelCamera != null
                        ? modelCamera.transform.up
                        : Vector3.up;

                // Only the horizontal finger movement rotates the model.
                modelRoot.Rotate(
                    upAxis,
                    -touch.deltaPosition.x * touchRotationSensitivity,
                    Space.World
                );
            }
            else if (touch.phase == TouchPhase.Ended ||
                     touch.phase == TouchPhase.Canceled)
            {
                dragging = false;
            }

            return;
        }

        if (Input.touchCount >= 2)
        {
            dragging = false;

            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);

            Vector2 firstPrevious =
                first.position - first.deltaPosition;

            Vector2 secondPrevious =
                second.position - second.deltaPosition;

            float previousDistance =
                Vector2.Distance(firstPrevious, secondPrevious);

            float currentDistance =
                Vector2.Distance(first.position, second.position);

            if (previousDistance <= 0.001f)
                return;

            float ratio = currentDistance / previousDistance;

            // Blend the raw pinch ratio so scaling feels controlled on a phone.
            float adjustedRatio =
                Mathf.Lerp(
                    1f,
                    ratio,
                    Mathf.Max(0f, pinchZoomSensitivity));

            ChangeZoomByRatio(adjustedRatio);
        }
    }

    private void ChangeZoomByRatio(float ratio)
    {
        if (modelRoot == null || !hasFittedPose)
            return;

        float baseScale =
            Mathf.Max(fittedScale.x, 0.0001f);

        float currentMultiplier =
            modelRoot.localScale.x / baseScale;

        float targetMultiplier =
            Mathf.Clamp(
                currentMultiplier * ratio,
                minZoomMultiplier,
                maxZoomMultiplier);

        modelRoot.localScale =
            fittedScale * targetMultiplier;
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

        // Mouse/editor drag: rotate only left/right around the camera's up axis.
        // Vertical drag no longer tilts the model.
        if (Input.touchCount == 0)
        {
            modelRoot.Rotate(
                modelCamera != null ? modelCamera.transform.up : Vector3.up,
                -delta.x * rotationSpeed,
                Space.World
            );
        }

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
        string currentScene =
            SceneManager.GetActiveScene().name;

        string rememberedScene =
            PlayerPrefs.GetString(
                "previous_scene",
                previousSceneName);

        // ShowLessonScene sets previous_scene before opening Mode3DScene.
        // Never reload Mode3DScene itself if stale PlayerPrefs exists.
        if (string.IsNullOrWhiteSpace(rememberedScene) ||
            string.Equals(
                rememberedScene,
                currentScene,
                StringComparison.OrdinalIgnoreCase))
        {
            rememberedScene = previousSceneName;
        }

        if (!string.IsNullOrWhiteSpace(rememberedScene) &&
            Application.CanStreamedLevelBeLoaded(rememberedScene))
        {
            Debug.Log(
                "[Mode3D] Back -> " +
                rememberedScene);

            SceneManager.LoadScene(
                rememberedScene);

            return;
        }

        if (!string.IsNullOrWhiteSpace(previousSceneName) &&
            Application.CanStreamedLevelBeLoaded(previousSceneName))
        {
            SceneManager.LoadScene(
                previousSceneName);
        }
        else
        {
            Debug.LogError(
                "[Mode3D] Cannot go back. " +
                $"Scene '{rememberedScene}' / '{previousSceneName}' is not in Build Profiles.");
        }
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

    private static bool IsHttpUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Uri.TryCreate(
                   value.Trim(),
                   UriKind.Absolute,
                   out Uri uri) &&
               (uri.Scheme == Uri.UriSchemeHttp ||
                uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string MakeSafeFileName(
        string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "model.glb";

        foreach (char invalid in
                 Path.GetInvalidFileNameChars())
        {
            fileName =
                fileName.Replace(
                    invalid,
                    '_');
        }

        return fileName;
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
