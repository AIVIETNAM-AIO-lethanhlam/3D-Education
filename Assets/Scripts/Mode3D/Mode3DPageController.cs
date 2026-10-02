using System;
using System.Collections;
using System.Collections.Generic;
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

    [Tooltip("Off: the model stays in place and users can only zoom (pinch / mouse wheel). " +
             "Rotation is done by the Auto-rotate button around the model's own center.")]
    [SerializeField] private bool allowDragRotation = false;

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
    [SerializeField] private string vrSceneName = "VRClassroomScene";

    // Headset version of the VR classroom (same rule as ShowLessonScene).
    private const string HeadsetVrSceneName = "VRClassroomXRScene";

    // Folder inside the phone's Pictures (Gallery) where screenshots are saved.
    private const string GalleryFolder = "Pictures/Virtual Education";

    private bool isCapturing;

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
    private VisualElement modelListOverlay;
    private VisualElement modelListContainer;
    private Button closeModelListButton;

    // Pose of ModelRoot before any lesson model was fitted (used when switching models).
    private Vector3 initialRootPosition;
    private Quaternion initialRootRotation = Quaternion.identity;
    private Vector3 initialRootScale = Vector3.one;
    private bool initialRootPoseSaved;

    private Coroutine resetFlashRoutine;
    private Coroutine focusFlashRoutine;

    private Button structureButton;
    private VisualElement structureFrame;
    private ModelStructureOverlay structureOverlay;

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

    // BUG-013: runtime lesson models are imported by glTFast, whose shaders use
    // "baseColorFactor" (not URP "_BaseColor" / legacy "_Color").
    private static readonly int GltfBaseColorId = Shader.PropertyToID("baseColorFactor");

    // BUG-015: exploded view state (parts are collected after the model loads).
    [SerializeField, Range(0.05f, 1f)] private float explodeDistanceFactor = 0.35f;
    [SerializeField, Range(0.05f, 2f)] private float explodeDuration = 0.4f;
    private readonly List<Transform> explodeParts = new List<Transform>();
    private readonly Dictionary<Transform, Vector3> explodeRestPositions = new Dictionary<Transform, Vector3>();
    private Coroutine explodeRoutine;
    private readonly Dictionary<Transform, Vector3> explodeTargetPositions = new Dictionary<Transform, Vector3>();

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
        SaveInitialRootPose();

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
                // The lesson GLB replaced the placeholder: rebind structure labels.
                structureOverlay?.NotifyModelChanged();

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

    private Button modelReportButton;

    private void AddModelReportButton()
    {
        bool isTeacher = string.Equals(
            PlayerPrefs.GetString("current_role", "student"), "teacher", StringComparison.OrdinalIgnoreCase);

        string assetId = PlayerPrefs.GetString("selected_model_asset_id", string.Empty);
        string lessonId = PlayerPrefs.GetString("selected_model_lesson_id", string.Empty);
        VisualElement infoCard = root?.Q<VisualElement>("model-list-card");

        if (isTeacher || SupabaseSession.IsAdmin || modelReportButton != null || infoCard == null ||
            !Guid.TryParse(assetId, out _) || !Guid.TryParse(lessonId, out _))
            return;

        ModerationReportSheet.EnsureStyles(root);
        modelReportButton = ModerationReportSheet.CreateWideReportButton(
            AppLanguageManager.T("Report inappropriate model", "Báo cáo model không phù hợp"),
            () =>
            {
                HideModelList();
                ModerationReportSheet.Show(root, this, new[]
                {
                    new ModerationReportSheet.Target
                    {
                        TargetType = "model_3d",
                        LessonId = lessonId,
                        AssetId = assetId,
                        Label = AppLanguageManager.T("3D Model", "Model 3D")
                    }
                });
            });
        infoCard.Add(modelReportButton);
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
        modelListOverlay = root.Q<VisualElement>("model-list-overlay");
        modelListContainer = root.Q<VisualElement>("model-list-container");
        closeModelListButton = root.Q<Button>("close-model-list-button");
        titleLabel = root.Q<Label>("model-title-label");
        toastLabel = root.Q<Label>("toast-label");

        backButton = root.Q<Button>("back-button");
        infoButton = root.Q<Button>("info-button");
        closeInfoButton = root.Q<Button>("close-info-button");

        // Students can report this 3D model to the admin from the info panel (2026-09).
        AddModelReportButton();
        captureButton = root.Q<Button>("capture-button");
        vrButton = root.Q<Button>("vr-button");
        resetButton = root.Q<Button>("reset-button");
        zoomButton = root.Q<Button>("zoom-button");
        focusButton = root.Q<Button>("focus-button");
        layersButton = root.Q<Button>("layers-button");
        autoRotateButton = root.Q<Button>("auto-rotate-button");
        structureButton = root.Q<Button>("structure-button");
        structureFrame = root.Q<VisualElement>("structure-frame");

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
        SetupStructureOverlay();
    }

    // ---------------------------------------------------------------
    // Model structure (model_parts): labels + detail sheet, toggled by
    // the "structure" button in the bottom toolbar.
    // ---------------------------------------------------------------
    private void SetupStructureOverlay()
    {
        if (root == null)
            return;

        // Add the overlay inside "screen" so the info popup / toast (same parent)
        // can be kept above it.
        structureOverlay = ModelStructureOverlay.Attach(
            this,
            root.Q<VisualElement>("screen") ?? root,
            () => modelRoot,
            () => modelCamera != null ? modelCamera : Camera.main);

        if (structureOverlay == null)
            return;

        // Keep labels between the header and the bottom toolbar.
        structureOverlay.TopInset = 150f;
        structureOverlay.BottomInset = 230f;

        structureOverlay.SetModel(
            PlayerPrefs.GetString("selected_model_asset_id", string.Empty),
            PlayerPrefs.GetString("selected_model_lesson_id",
                PlayerPrefs.GetString("selected_lesson_id", string.Empty)),
            PlayerPrefs.GetString("selected_model_file_name", string.Empty));

        // Popups must stay above the structure labels.
        infoOverlay?.BringToFront();
        modelListOverlay?.BringToFront();
        toastLabel?.BringToFront();

        structureOverlay.EnabledChanged -= OnStructureOverlayChanged;
        structureOverlay.EnabledChanged += OnStructureOverlayChanged;
        OnStructureOverlayChanged(structureOverlay.IsEnabled);
    }

    private void ToggleStructureOverlay()
    {
        if (structureOverlay == null)
            SetupStructureOverlay();

        structureOverlay?.Toggle();

        if (structureOverlay != null)
        {
            ShowToast(structureOverlay.IsEnabled
                ? AppLanguageManager.T("Structure labels on", "Đã bật cấu trúc mô hình")
                : AppLanguageManager.T("Structure labels off", "Đã tắt cấu trúc mô hình"));
        }
    }

    private void OnStructureOverlayChanged(bool enabled)
    {
        structureFrame?.EnableInClassList("selected-tool-frame", enabled);
        SetButtonActive(structureButton, enabled);
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
        // A new model is loading: forget the parts of the previous one.
        ClearExplodeState();

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

        // GLB files often have their origin far from the mesh, so rotating
        // ModelRoot made the model orbit around an invisible point. Move the
        // pivot to the visual center so rotation and zoom happen in place.
        CenterPivotOnModelBounds();

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

    /// <summary>
    /// Moves ModelRoot to the center of the model's bounds and shifts its
    /// children back by the same amount, so nothing moves on screen but
    /// Rotate()/localScale now act around the model's own center.
    /// </summary>
    private void CenterPivotOnModelBounds()
    {
        if (modelRoot == null || !TryGetCombinedBounds(out Bounds bounds))
            return;

        Vector3 offset = bounds.center - modelRoot.position;
        if (offset.sqrMagnitude < 1e-10f)
            return;

        for (int i = 0; i < modelRoot.childCount; i++)
        {
            Transform child = modelRoot.GetChild(i);
            if (child != null)
                child.position -= offset;
        }

        modelRoot.position += offset;
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
        if (infoButton != null) infoButton.clicked += ShowModelList;
        if (closeModelListButton != null) closeModelListButton.clicked += HideModelList;
        modelListOverlay?.RegisterCallback<PointerUpEvent>(OnModelListOverlayPointerUp);
        if (closeInfoButton != null) closeInfoButton.clicked += HideInfo;
        if (captureButton != null) captureButton.clicked += CaptureScreenshot;
        if (vrButton != null) vrButton.clicked += OpenVRScene;
        if (resetButton != null) resetButton.clicked += ResetModel;
        if (zoomButton != null) zoomButton.clicked += ZoomIn;
        if (focusButton != null) focusButton.clicked += FocusModel;
        if (layersButton != null) layersButton.clicked += ToggleExplodedView;
        if (autoRotateButton != null) autoRotateButton.clicked += ToggleAutoRotate;
        if (structureButton != null) structureButton.clicked += ToggleStructureOverlay;

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
        if (infoButton != null) infoButton.clicked -= ShowModelList;
        if (closeModelListButton != null) closeModelListButton.clicked -= HideModelList;
        modelListOverlay?.UnregisterCallback<PointerUpEvent>(OnModelListOverlayPointerUp);
        if (closeInfoButton != null) closeInfoButton.clicked -= HideInfo;
        if (captureButton != null) captureButton.clicked -= CaptureScreenshot;
        if (vrButton != null) vrButton.clicked -= OpenVRScene;
        if (resetButton != null) resetButton.clicked -= ResetModel;
        if (zoomButton != null) zoomButton.clicked -= ZoomIn;
        if (focusButton != null) focusButton.clicked -= FocusModel;
        if (layersButton != null) layersButton.clicked -= ToggleExplodedView;
        if (autoRotateButton != null) autoRotateButton.clicked -= ToggleAutoRotate;
        if (structureButton != null) structureButton.clicked -= ToggleStructureOverlay;

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

        // Do not rotate/zoom while the user taps a structure label or reads
        // the structure detail sheet.
        if (ModelStructureOverlay.ShouldBlockWorldTouch())
        {
            dragging = false;
            return;
        }

        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            if (!allowDragRotation)
            {
                // Model stays in place: one finger does nothing (pinch still zooms).
                dragging = false;
                return;
            }

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
        if (Input.touchCount == 0 && allowDragRotation)
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
        RestoreExplodedParts();
        explodedView = false;

        SetButtonActive(autoRotateButton, false);
        SetButtonActive(layersButton, false);
        SetToolFrameActive(autoRotateFrame, false);
        SetToolFrameActive(layersFrame, false);

        // Reset is an action, not a mode: highlight it briefly.
        FlashToolFrame(resetFrame, ref resetFlashRoutine);

        ShowToast("Model reset");
    }

    private void FocusModel()
    {
        FitModelToPresentationBoard();

        // Centering is an action, not a mode: highlight it briefly.
        FlashToolFrame(focusFrame, ref focusFlashRoutine);
        ShowToast("Model centered");
    }

    private void ToggleAutoRotate()
    {
        autoRotate = !autoRotate;
        SetButtonActive(autoRotateButton, autoRotate);
        SetToolFrameActive(autoRotateFrame, autoRotate);
        ShowToast(autoRotate ? "Auto rotation on" : "Auto rotation off");
    }

    private void ToggleExplodedView()
    {

        // BUG-015: nothing implemented ShowExplodedView/HideExplodedView, so the
        // button only toggled its highlight. Move the model's parts directly.
        if (explodeParts.Count == 0)
            CollectExplodeParts();

        if (explodeParts.Count < 2)
        {
            explodedView = false;
            SetButtonActive(layersButton, false);
            SetToolFrameActive(layersFrame, false);
            ShowToast("This model has no separate parts");
            return;
        }

        explodedView = !explodedView;
        SetButtonActive(layersButton, explodedView);
        SetToolFrameActive(layersFrame, explodedView);

        if (explodeRoutine != null)
            StopCoroutine(explodeRoutine);

        explodeRoutine = StartCoroutine(AnimateExplode(explodedView));

        ShowToast(explodedView ? "Exploded view" : "Assembly view");
    }

    private void ClearExplodeState()
    {
        if (explodeRoutine != null)
        {
            StopCoroutine(explodeRoutine);
            explodeRoutine = null;
        }

        explodeParts.Clear();
        explodeRestPositions.Clear();
        explodeTargetPositions.Clear();
        explodedView = false;
        SetButtonActive(layersButton, false);
        SetToolFrameActive(layersFrame, false);
    }

    private void CollectExplodeParts()
    {
        explodeParts.Clear();
        explodeRestPositions.Clear();
        explodeTargetPositions.Clear();

        if (modelRoot == null)
            return;

        RefreshRenderers();

        // glTF files usually wrap the parts in single-child nodes
        // ("Scene", "RootNode", "Sketchfab_model"...). Walk down to the
        // first level that really has several children.
        Transform level = modelRoot;
        while (level.childCount == 1 && level.GetComponent<Renderer>() == null)
            level = level.GetChild(0);

        if (level.childCount > 1)
        {
            foreach (Transform child in level)
            {
                if (child.GetComponentInChildren<Renderer>(true) != null)
                    explodeParts.Add(child);
            }
        }

        if (explodeParts.Count < 2)
        {
            explodeParts.Clear();
            foreach (Renderer rendererItem in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (rendererItem == null || rendererItem.transform == modelRoot)
                    continue;

                // Skip a renderer whose parent part is already listed
                // (it would be moved twice).
                bool ancestorListed = false;
                for (Transform p = rendererItem.transform.parent; p != null && p != modelRoot; p = p.parent)
                {
                    if (explodeParts.Contains(p))
                    {
                        ancestorListed = true;
                        break;
                    }
                }

                if (!ancestorListed)
                    explodeParts.Add(rendererItem.transform);
            }
        }

        foreach (Transform part in explodeParts)
            explodeRestPositions[part] = part.localPosition;

        // Compute every part's exploded position ONCE, while the model is
        // assembled. Parts and their parents live under modelRoot, so these
        // local positions stay correct when the model is rotated or zoomed.
        if (!TryGetCombinedBounds(out Bounds bounds))
            return;

        float distance = bounds.size.magnitude * explodeDistanceFactor;

        foreach (Transform part in explodeParts)
        {
            Renderer partRenderer = part.GetComponentInChildren<Renderer>();
            Vector3 partCenter = partRenderer != null ? partRenderer.bounds.center : part.position;

            Vector3 direction = partCenter - bounds.center;
            if (direction.sqrMagnitude < 1e-8f)
                direction = Vector3.up;

            Vector3 worldOffset = direction.normalized * distance;
            Vector3 localOffset = part.parent != null
                ? part.parent.InverseTransformVector(worldOffset)
                : worldOffset;

            explodeTargetPositions[part] = part.localPosition + localOffset;
        }
    }

    private void RestoreExplodedParts()
    {
        if (explodeRoutine != null)
        {
            StopCoroutine(explodeRoutine);
            explodeRoutine = null;
        }

        foreach (KeyValuePair<Transform, Vector3> pair in explodeRestPositions)
        {
            if (pair.Key != null)
                pair.Key.localPosition = pair.Value;
        }
    }

    private IEnumerator AnimateExplode(bool explode)
    {
        var start = new Dictionary<Transform, Vector3>();
        var target = new Dictionary<Transform, Vector3>();

        foreach (Transform part in explodeParts)
        {
            if (part == null ||
                !explodeRestPositions.TryGetValue(part, out Vector3 rest) ||
                !explodeTargetPositions.TryGetValue(part, out Vector3 exploded))
            {
                continue;
            }

            start[part] = part.localPosition;
            target[part] = explode ? exploded : rest;
        }

        for (float elapsed = 0f; elapsed < explodeDuration; elapsed += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, elapsed / explodeDuration);
            foreach (KeyValuePair<Transform, Vector3> pair in target)
            {
                if (pair.Key != null)
                    pair.Key.localPosition = Vector3.Lerp(start[pair.Key], pair.Value, k);
            }
            yield return null;
        }

        foreach (KeyValuePair<Transform, Vector3> pair in target)
        {
            if (pair.Key != null)
                pair.Key.localPosition = pair.Value;
        }

        explodeRoutine = null;
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

            // BUG-013: set the colour per material slot and on every colour
            // property the shader actually has (glTFast / URP Lit / legacy).
            Material[] materials = rendererItem.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material material = materials[slot];
                if (material == null)
                    continue;

                rendererItem.GetPropertyBlock(propertyBlock, slot);

                if (material.HasProperty(GltfBaseColorId))
                    propertyBlock.SetColor(GltfBaseColorId, color);
                if (material.HasProperty(BaseColorId))
                    propertyBlock.SetColor(BaseColorId, color);
                if (material.HasProperty(ColorId))
                    propertyBlock.SetColor(ColorId, color);

                rendererItem.SetPropertyBlock(propertyBlock, slot);
            }
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

    // ---------------------------------------------------------------
    // Bottom toolbar: each toggle shows its own blue circle while ON, so
    // several tools can be active at the same time.
    // ---------------------------------------------------------------
    private static void SetToolFrameActive(VisualElement frame, bool active)
    {
        frame?.EnableInClassList("selected-tool-frame", active);
    }

    private void FlashToolFrame(VisualElement frame, ref Coroutine routine)
    {
        if (frame == null)
            return;

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(FlashToolFrameRoutine(frame));
    }

    private IEnumerator FlashToolFrameRoutine(VisualElement frame)
    {
        SetToolFrameActive(frame, true);
        yield return new WaitForSecondsRealtime(0.35f);
        SetToolFrameActive(frame, false);
    }

    // ---------------------------------------------------------------
    // Menu: list of all 3D models in the class (manifest written by
    // ShowLessonScene). Selecting one loads it and closes the list.
    // ---------------------------------------------------------------
    [Serializable]
    private class Mode3DModelManifest
    {
        public string class_id;
        public string lesson_id;
        public Mode3DModelEntry[] models;
    }

    [Serializable]
    private class Mode3DModelEntry
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

    private List<Mode3DModelEntry> ReadClassModels()
    {
        List<Mode3DModelEntry> result = new List<Mode3DModelEntry>();

        string json = PlayerPrefs.GetString("selected_class_models_json", string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            json = PlayerPrefs.GetString("selected_lesson_models_json", string.Empty);

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                Mode3DModelManifest manifest = JsonUtility.FromJson<Mode3DModelManifest>(json);
                if (manifest?.models != null)
                {
                    foreach (Mode3DModelEntry entry in manifest.models)
                    {
                        if (entry != null &&
                            (!string.IsNullOrWhiteSpace(entry.url) ||
                             !string.IsNullOrWhiteSpace(entry.fallback_url) ||
                             IsHttpUrl(entry.storage_path)))
                        {
                            result.Add(entry);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Mode3D] Cannot read class model list: " + exception.Message);
            }
        }

        if (result.Count == 0)
        {
            // Opened without a manifest: show at least the current model.
            result.Add(new Mode3DModelEntry
            {
                asset_id = PlayerPrefs.GetString("selected_model_asset_id", string.Empty),
                lesson_id = PlayerPrefs.GetString("selected_model_lesson_id", string.Empty),
                lesson_title = PlayerPrefs.GetString("selected_model_lesson_title", string.Empty),
                name = PlayerPrefs.GetString("selected_model_name", "3D Model"),
                file_name = PlayerPrefs.GetString("selected_model_file_name", string.Empty),
                storage_path = PlayerPrefs.GetString("selected_model_storage_path", string.Empty),
                url = PlayerPrefs.GetString("selected_model_url", string.Empty)
            });
        }

        return result;
    }

    private void ShowModelList()
    {
        BuildModelList();
        modelListOverlay?.RemoveFromClassList("hidden");
        modelListOverlay?.BringToFront();
    }

    private void HideModelList()
    {
        modelListOverlay?.AddToClassList("hidden");
    }

    private void OnModelListOverlayPointerUp(PointerUpEvent evt)
    {
        // Tap on the dark area outside the card closes the list.
        if (evt.target == modelListOverlay)
            HideModelList();
    }

    private void BuildModelList()
    {
        if (modelListContainer == null)
            return;

        modelListContainer.Clear();

        Label title = root?.Q<Label>("model-list-title");
        if (title != null)
            title.text = AppLanguageManager.T("3D models in this class", "Mô hình 3D trong lớp");

        Label hint = root?.Q<Label>("model-list-hint");
        if (hint != null)
            hint.text = AppLanguageManager.T(
                "Tap a model to view it. Drag to rotate, pinch with two fingers to zoom.",
                "Chọn một mô hình để xem. Kéo để xoay, dùng hai ngón tay để phóng to / thu nhỏ.");

        List<Mode3DModelEntry> models = ReadClassModels();
        models.Sort((a, b) =>
        {
            int chapter = a.chapter_order.CompareTo(b.chapter_order);
            if (chapter != 0) return chapter;
            int lesson = string.Compare(a.lesson_title, b.lesson_title, StringComparison.CurrentCultureIgnoreCase);
            if (lesson != 0) return lesson;
            return a.display_order.CompareTo(b.display_order);
        });

        string currentAssetId = PlayerPrefs.GetString("selected_model_asset_id", string.Empty);
        string currentUrl = PlayerPrefs.GetString("selected_model_url", string.Empty);
        string lastLesson = null;

        foreach (Mode3DModelEntry entry in models)
        {
            string lessonTitle = string.IsNullOrWhiteSpace(entry.lesson_title)
                ? AppLanguageManager.T("Lesson", "Bài học")
                : entry.lesson_title.Trim();

            if (!string.Equals(lessonTitle, lastLesson, StringComparison.Ordinal))
            {
                Label section = new Label(lessonTitle);
                section.AddToClassList("model-list-section");
                modelListContainer.Add(section);
                lastLesson = lessonTitle;
            }

            Mode3DModelEntry captured = entry;
            Button item = new Button(() => SelectModelFromList(captured));
            item.AddToClassList("model-list-item");

            string displayName = !string.IsNullOrWhiteSpace(entry.name)
                ? entry.name
                : (!string.IsNullOrWhiteSpace(entry.file_name)
                    ? Path.GetFileNameWithoutExtension(entry.file_name)
                    : "3D Model");

            Label name = new Label(displayName);
            name.AddToClassList("model-list-item-name");
            item.Add(name);

            if (!string.IsNullOrWhiteSpace(entry.file_name))
            {
                Label sub = new Label(entry.file_name);
                sub.AddToClassList("model-list-item-sub");
                item.Add(sub);
            }

            bool isCurrent =
                (!string.IsNullOrWhiteSpace(entry.asset_id) &&
                 string.Equals(entry.asset_id, currentAssetId, StringComparison.OrdinalIgnoreCase)) ||
                (string.IsNullOrWhiteSpace(entry.asset_id) &&
                 string.Equals(entry.url, currentUrl, StringComparison.Ordinal));

            if (isCurrent)
                item.AddToClassList("model-list-item-current");

            modelListContainer.Add(item);
        }

        if (models.Count == 0)
        {
            Label empty = new Label(AppLanguageManager.T(
                "This class has no 3D models yet.",
                "Lớp học này chưa có mô hình 3D."));
            empty.AddToClassList("model-list-empty");
            modelListContainer.Add(empty);
        }
    }

    private async void SelectModelFromList(Mode3DModelEntry entry)
    {
        if (entry == null)
            return;

        HideModelList();

        string currentAssetId = PlayerPrefs.GetString("selected_model_asset_id", string.Empty);
        if (runtimeModelLoaded &&
            !string.IsNullOrWhiteSpace(entry.asset_id) &&
            string.Equals(entry.asset_id, currentAssetId, StringComparison.OrdinalIgnoreCase))
        {
            return; // already showing this model
        }

        if (runtimeModelLoading)
        {
            ShowToast(AppLanguageManager.T("A model is still loading...", "Mô hình đang được tải..."));
            return;
        }

        string primaryUrl = !string.IsNullOrWhiteSpace(entry.url)
            ? entry.url
            : (!string.IsNullOrWhiteSpace(entry.fallback_url) ? entry.fallback_url : entry.storage_path);

        string displayName = !string.IsNullOrWhiteSpace(entry.name)
            ? entry.name
            : Path.GetFileNameWithoutExtension(entry.file_name ?? "3D Model");

        PlayerPrefs.SetString("selected_model_asset_id", entry.asset_id ?? string.Empty);
        PlayerPrefs.SetString("selected_model_lesson_id", entry.lesson_id ?? string.Empty);
        PlayerPrefs.SetString("selected_model_lesson_title", entry.lesson_title ?? string.Empty);
        PlayerPrefs.SetInt("selected_model_chapter_order", entry.chapter_order);
        PlayerPrefs.SetString("selected_model_name", displayName);
        PlayerPrefs.SetString("selected_model_file_name", entry.file_name ?? string.Empty);
        PlayerPrefs.SetString("selected_model_bucket", entry.bucket ?? string.Empty);
        PlayerPrefs.SetString("selected_model_storage_path", entry.storage_path ?? string.Empty);
        PlayerPrefs.SetString("selected_model_url", primaryUrl ?? string.Empty);
        PlayerPrefs.Save();

        // Stop modes tied to the old model and return the root to its original pose.
        autoRotate = false;
        SetButtonActive(autoRotateButton, false);
        SetToolFrameActive(autoRotateFrame, false);
        ClearExplodeState();
        structureOverlay?.CloseDetail();
        RestoreInitialRootPose();

        if (titleLabel != null)
            titleLabel.text = displayName;

        ShowToast(AppLanguageManager.T("Loading model...", "Đang tải mô hình..."));

        bool loaded = await LoadCurrentLessonModelAsync();
        if (!this)
            return;

        // Signed URLs expire after a while: retry with the public URL.
        if (!loaded &&
            !string.IsNullOrWhiteSpace(entry.fallback_url) &&
            !string.Equals(entry.fallback_url, primaryUrl, StringComparison.Ordinal))
        {
            PlayerPrefs.SetString("selected_model_url", entry.fallback_url);
            PlayerPrefs.Save();
            loaded = await LoadCurrentLessonModelAsync();
            if (!this)
                return;
        }

        if (!loaded)
        {
            ShowToast(AppLanguageManager.T("Could not load this model", "Không tải được mô hình này"));
            return;
        }

        structureOverlay?.SetModel(entry.asset_id, entry.lesson_id, entry.file_name);
        structureOverlay?.NotifyModelChanged();

        StartCoroutine(FitRuntimeModelAfterAsyncLoad());
    }

    private void SaveInitialRootPose()
    {
        if (initialRootPoseSaved || modelRoot == null)
            return;

        initialRootPosition = modelRoot.localPosition;
        initialRootRotation = modelRoot.localRotation;
        initialRootScale = modelRoot.localScale;
        initialRootPoseSaved = true;
    }

    private void RestoreInitialRootPose()
    {
        if (!initialRootPoseSaved || modelRoot == null)
            return;

        modelRoot.localPosition = initialRootPosition;
        modelRoot.localRotation = initialRootRotation;
        modelRoot.localScale = initialRootScale;
        hasFittedPose = false;
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

    // ---------------------------------------------------------------
    // VR: open the VR classroom with the model currently shown here.
    // ---------------------------------------------------------------
    private void OpenVRScene()
    {
        string targetScene = ResolveVrSceneName();

        if (string.IsNullOrWhiteSpace(targetScene) ||
            !Application.CanStreamedLevelBeLoaded(targetScene))
        {
            ShowToast(AppLanguageManager.T(
                "VR scene is not in Build Profiles",
                "Chưa thêm scene VR vào Build Profiles"));
            Debug.LogError("[Mode3D] VR scene '" + targetScene + "' cannot be loaded.");
            return;
        }

        // VRClassroomScene reads the class model list written by ShowLessonScene
        // (selected_class_models_json). Ask it to start with the model that is
        // on screen now instead of the first model of the lesson.
        string currentAssetId = PlayerPrefs.GetString("selected_model_asset_id", string.Empty);
        PlayerPrefs.SetString("vr_initial_asset_id", currentAssetId);

        List<Mode3DModelEntry> models = ReadClassModels();
        for (int i = 0; i < models.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(currentAssetId) &&
                string.Equals(models[i].asset_id, currentAssetId, StringComparison.OrdinalIgnoreCase))
            {
                PlayerPrefs.SetInt("selected_lesson_model_index", i);
                break;
            }
        }

        // If the scene was opened without a manifest, give VR a one-model manifest.
        if (string.IsNullOrWhiteSpace(PlayerPrefs.GetString("selected_class_models_json", string.Empty)) &&
            string.IsNullOrWhiteSpace(PlayerPrefs.GetString("selected_lesson_models_json", string.Empty)) &&
            models.Count > 0)
        {
            Mode3DModelManifest manifest = new Mode3DModelManifest
            {
                class_id = PlayerPrefs.GetString("selected_class_id", string.Empty),
                lesson_id = models[0].lesson_id,
                models = models.ToArray()
            };
            string json = JsonUtility.ToJson(manifest);
            PlayerPrefs.SetString("selected_class_models_json", json);
            PlayerPrefs.SetString("selected_lesson_models_json", json);
        }

        PlayerPrefs.SetString("interactive_mode", "vr");

        // Back from VR returns here (Mode3D's own Back still goes to ShowLessonScene).
        PlayerPrefs.SetString("previous_scene", "Mode3DScene");
        PlayerPrefs.Save();

        SceneManager.LoadScene(targetScene);
    }

    private string ResolveVrSceneName()
    {
        bool useHeadsetScene = UnityEngine.XR.XRSettings.isDeviceActive;

#if UNITY_EDITOR
        useHeadsetScene |= UnityEditor.EditorPrefs.GetBool("3DEducation.UseXRSceneInEditor", false);
#endif

        if (useHeadsetScene && Application.CanStreamedLevelBeLoaded(HeadsetVrSceneName))
            return HeadsetVrSceneName;

        return vrSceneName;
    }

    // ---------------------------------------------------------------
    // Screenshot -> phone Gallery (Pictures/Virtual Education).
    // ---------------------------------------------------------------
    private void CaptureScreenshot()
    {
        if (isCapturing)
            return;

        StartCoroutine(CaptureScreenshotRoutine());
    }

    private IEnumerator CaptureScreenshotRoutine()
    {
        isCapturing = true;

        // Hide the toast so it is not part of the picture.
        toastLabel?.AddToClassList("hidden");

        yield return new WaitForEndOfFrame();

        Texture2D texture = null;
        byte[] png = null;

        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            png = texture != null ? texture.EncodeToPNG() : null;
        }
        catch (Exception exception)
        {
            Debug.LogError("[Mode3D] Screenshot capture failed: " + exception);
        }
        finally
        {
            if (texture != null)
                Destroy(texture);
        }

        if (png == null || png.Length == 0)
        {
            isCapturing = false;
            ShowToast(AppLanguageManager.T("Could not take a screenshot", "Không chụp được màn hình"));
            yield break;
        }

        string modelName = PlayerPrefs.GetString("selected_model_name", "Model");
        string safeName = MakeScreenshotNamePart(string.IsNullOrWhiteSpace(modelName) ? "Model" : modelName);
        string fileName = $"VirtualEducation_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.png";

        bool saved = false;
        string savedLocation = null;

        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            saved = SaveImageToAndroidGallery(png, fileName);
            savedLocation = GalleryFolder;
#else
            string folder = Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots")
                : Path.Combine(Application.persistentDataPath, "Screenshots");

            Directory.CreateDirectory(folder);
            string fullPath = Path.Combine(folder, fileName);
            File.WriteAllBytes(fullPath, png);
            saved = true;
            savedLocation = fullPath;
#endif
        }
        catch (Exception exception)
        {
            Debug.LogError("[Mode3D] Saving screenshot failed: " + exception);
        }

        isCapturing = false;

        if (saved)
        {
            Debug.Log("[Mode3D] Screenshot saved: " + savedLocation + "/" + fileName);
            ShowToast(AppLanguageManager.T(
                "Saved to Gallery (Virtual Education)",
                "Đã lưu ảnh vào Thư viện (Virtual Education)"));
        }
        else
        {
            ShowToast(AppLanguageManager.T("Could not save the screenshot", "Không lưu được ảnh chụp"));
        }
    }

    private static string MakeScreenshotNamePart(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);

        foreach (char c in value.Trim())
        {
            if (Array.IndexOf(invalid, c) >= 0 || char.IsWhiteSpace(c))
                builder.Append('_');
            else
                builder.Append(c);
        }

        string result = builder.ToString();
        return result.Length > 40 ? result.Substring(0, 40) : result;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Inserts the PNG into MediaStore so it appears in the phone's Gallery /
    /// Photos app. Uses scoped storage (Android 10+, minSdk 29), so no storage
    /// permission is required.
    /// </summary>
    private static bool SaveImageToAndroidGallery(byte[] png, string fileName)
    {
        using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        using AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver");
        using AndroidJavaObject values = new AndroidJavaObject("android.content.ContentValues");

        values.Call("put", "_display_name", fileName);
        values.Call("put", "mime_type", "image/png");
        values.Call("put", "relative_path", GalleryFolder);

        using AndroidJavaClass media = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
        using AndroidJavaObject collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
        using AndroidJavaObject itemUri = resolver.Call<AndroidJavaObject>("insert", collection, values);

        if (itemUri == null)
            return false;

        using AndroidJavaObject stream = resolver.Call<AndroidJavaObject>("openOutputStream", itemUri);
        if (stream == null)
            return false;

        // Java byte[] is signed: reinterpret the managed byte[] without copying.
        sbyte[] data = (sbyte[])(object)png;
        stream.Call("write", data);
        stream.Call("flush");
        stream.Call("close");
        return true;
    }
#endif

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

        // Stop only the previous toast. StopAllCoroutines() also killed the
        // exploded-view animation (and any other running coroutine).
        if (toastRoutine != null)
            StopCoroutine(toastRoutine);

        toastRoutine = StartCoroutine(ToastRoutine(message));
    }

    private Coroutine toastRoutine;

    private IEnumerator ToastRoutine(string message)
    {
        toastLabel.text = message;
        toastLabel.RemoveFromClassList("hidden");

        yield return new WaitForSeconds(1.35f);

        toastLabel.AddToClassList("hidden");
    }
}
