using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

/// <summary>
/// Runtime 3D-model catalog for VRClassroomScene.
///
/// Data source:
/// ShowLessonPageController stores a JSON manifest in PlayerPrefs before
/// loading VRClassroomScene. The manifest contains every model belonging to
/// every lesson of the current class.
///
/// Responsibilities:
/// - deserialize the class model manifest;
/// - automatically load the current lesson's first model;
/// - load another model when the user selects it from the lesson/model browser;
/// - place and normalize the model in front of the player;
/// - toggle model visibility;
/// - toggle automatic Y-axis rotation.
/// </summary>
public class VRRuntimeModelCatalog : MonoBehaviour
{
    public const string ClassModelsJsonKey = "selected_class_models_json";
    public const string LegacyModelsJsonKey = "selected_lesson_models_json";
    public const string SelectedLessonIdKey = "selected_lesson_id";
    public const string SelectedModelIndexKey = "selected_lesson_model_index";

    [Header("Placement")]
    [Tooltip("Optional runtime anchor. If empty, one is created automatically.")]
    [SerializeField] private Transform modelAnchor;

    [Tooltip("Place lesson models on the teacher desk/front teaching table instead of directly in front of the player.")]
    [SerializeField] private bool placeOnTeacherDesk = true;

    [Tooltip("Distance from the front edge/wall toward the classroom center.")]
    [SerializeField, Range(0.5f, 4f)]
    private float teacherDeskInsetFromFront = 1.65f;

    [Tooltip("Fallback desk-top height when no desk collider can be detected.")]
    [SerializeField, Range(0.45f, 1.5f)]
    private float fallbackTeacherDeskHeight = 0.82f;

    [Tooltip("Small gap between the model bottom and the teacher desk.")]
    [SerializeField, Range(0f, 0.25f)]
    private float modelDeskClearance = 0.06f;

    [Tooltip("Horizontal search radius used to find the top surface of the teacher desk.")]
    [SerializeField, Range(0.2f, 2f)]
    private float teacherDeskSearchRadius = 0.75f;

    [SerializeField, Min(0.1f)]
    private float normalizedLargestDimension = 0.85f;

    [Tooltip("Legacy fallback distance used only if teacher-desk placement cannot be resolved.")]
    [SerializeField, Min(0.5f)]
    private float distanceInFrontOfPlayer = 2.4f;

    [Tooltip("Legacy floor clearance for fallback placement.")]
    [SerializeField] private float verticalOffset = 0.02f;

    [Header("Rotation")]
    [SerializeField, Min(1f)]
    private float autoRotateSpeed = 35f;

    [Tooltip("One tap on the Rotate button rotates the model by this many degrees.")]
    [SerializeField, Range(5f, 90f)]
    private float manualRotateStep = 30f;

    [Header("Debug")]
    [SerializeField] private bool logModelLoading = true;

    private VRModelLaunchManifest manifest;
    private readonly List<VRModelLaunchItem> models = new();

    private GameObject currentModel;
    private GltfImport currentImport;
    private int currentModelIndex = -1;
    private bool modelVisible = true;
    private bool autoRotateEnabled;
    private bool loading;

    public IReadOnlyList<VRModelLaunchItem> Models => models;
    public VRModelLaunchManifest Manifest => manifest;
    public GameObject CurrentModel => currentModel;
    public int CurrentModelIndex => currentModelIndex;
    public bool ModelVisible => modelVisible;
    public bool AutoRotateEnabled => autoRotateEnabled;
    public bool IsLoading => loading;
    public bool HasModels => models.Count > 0;

    public event Action CatalogReady;
    public event Action<int, VRModelLaunchItem, GameObject> ModelChanged;
    public event Action<bool> VisibilityChanged;
    public event Action<bool> AutoRotateChanged;
    public event Action<bool, string> LoadingStateChanged;

    private void Awake()
    {
        ReadManifestFromPlayerPrefs();
        EnsureAnchor();
    }

    private async void Start()
    {
        if (models.Count == 0)
        {
            Debug.LogWarning(
                "[VRRuntimeModelCatalog] The class model manifest is empty. " +
                "Open VRClassroomScene from ShowLessonScene > VR Mode.");
            CatalogReady?.Invoke();
            return;
        }

        CatalogReady?.Invoke();

        int initialIndex = FindInitialCurrentLessonModelIndex();
        await LoadModelAsync(initialIndex);
    }

    private void Update()
    {
        if (!autoRotateEnabled ||
            currentModel == null ||
            !currentModel.activeInHierarchy)
        {
            return;
        }

        currentModel.transform.Rotate(
            Vector3.up,
            autoRotateSpeed * Time.deltaTime,
            Space.World);
    }

    private void OnDestroy()
    {
        DisposeCurrentImport();
    }

    public void ReadManifestFromPlayerPrefs()
    {
        models.Clear();
        manifest = null;

        string json = PlayerPrefs.GetString(ClassModelsJsonKey, string.Empty);

        if (string.IsNullOrWhiteSpace(json))
            json = PlayerPrefs.GetString(LegacyModelsJsonKey, string.Empty);

        if (string.IsNullOrWhiteSpace(json))
        {
            // Backward-compatible one-model fallback.
            string singleUrl =
                PlayerPrefs.GetString("selected_model_url", string.Empty);

            string fallbackUrl =
                PlayerPrefs.GetString("selected_model_fallback_url", string.Empty);

            if (string.IsNullOrWhiteSpace(singleUrl) &&
                string.IsNullOrWhiteSpace(fallbackUrl))
            {
                return;
            }

            VRModelLaunchItem fallback = new VRModelLaunchItem
            {
                asset_id =
                    PlayerPrefs.GetString("selected_model_asset_id", string.Empty),
                lesson_id =
                    PlayerPrefs.GetString(
                        "selected_model_lesson_id",
                        PlayerPrefs.GetString(SelectedLessonIdKey, string.Empty)),
                lesson_title =
                    PlayerPrefs.GetString(
                        "selected_model_lesson_title",
                        PlayerPrefs.GetString("selected_lesson_title", "Current Lesson")),
                chapter_order =
                    PlayerPrefs.GetInt(
                        "selected_model_chapter_order",
                        PlayerPrefs.GetInt("selected_chapter_order", 0)),
                name =
                    PlayerPrefs.GetString("selected_model_name", "3D Model"),
                file_name =
                    PlayerPrefs.GetString("selected_model_file_name", string.Empty),
                bucket =
                    PlayerPrefs.GetString("selected_model_bucket", string.Empty),
                storage_path =
                    PlayerPrefs.GetString("selected_model_storage_path", string.Empty),
                url = singleUrl,
                fallback_url = fallbackUrl,
                display_order = 0
            };

            manifest = new VRModelLaunchManifest
            {
                class_id =
                    PlayerPrefs.GetString("selected_class_id", string.Empty),
                lesson_id =
                    PlayerPrefs.GetString(SelectedLessonIdKey, fallback.lesson_id),
                mode = "vr",
                models = new[] { fallback }
            };

            models.Add(fallback);
            return;
        }

        try
        {
            manifest = JsonUtility.FromJson<VRModelLaunchManifest>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[VRRuntimeModelCatalog] Cannot parse model manifest:\n" +
                exception);
            return;
        }

        if (manifest?.models == null)
            return;

        foreach (VRModelLaunchItem item in manifest.models)
        {
            if (item == null)
                continue;

            if (string.IsNullOrWhiteSpace(item.url) &&
                string.IsNullOrWhiteSpace(item.fallback_url))
            {
                Debug.LogWarning(
                    "[VRRuntimeModelCatalog] Skipping model with no usable URL: " +
                    (item.name ?? item.file_name ?? "Unnamed model"));
                continue;
            }

            models.Add(item);
        }
    }

    public int FindInitialCurrentLessonModelIndex()
    {
        if (models.Count == 0)
            return -1;

        string currentLessonId =
            manifest != null && !string.IsNullOrWhiteSpace(manifest.lesson_id)
                ? manifest.lesson_id
                : PlayerPrefs.GetString(SelectedLessonIdKey, string.Empty);

        if (!string.IsNullOrWhiteSpace(currentLessonId))
        {
            for (int i = 0; i < models.Count; i++)
            {
                if (string.Equals(
                        models[i].lesson_id,
                        currentLessonId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        int savedIndex =
            PlayerPrefs.GetInt(SelectedModelIndexKey, 0);

        return Mathf.Clamp(savedIndex, 0, models.Count - 1);
    }

    public async void SelectModel(int index)
    {
        await LoadModelAsync(index);
    }

    public async Task<bool> LoadModelAsync(int index)
    {
        if (loading)
            return false;

        if (index < 0 || index >= models.Count)
        {
            Debug.LogWarning(
                $"[VRRuntimeModelCatalog] Invalid model index: {index}");
            return false;
        }

        VRModelLaunchItem record = models[index];
        string modelUrl = GetBestUrl(record);

        if (string.IsNullOrWhiteSpace(modelUrl))
        {
            SetLoading(
                false,
                $"No URL available for {GetDisplayName(record)}");
            return false;
        }

        loading = true;
        SetLoading(true, $"Loading {GetDisplayName(record)}...");

        ClearCurrentModel();
        EnsureAnchor();

        if (placeOnTeacherDesk)
            RepositionAnchorAtTeacherDesk();
        else
            RepositionAnchorInFrontOfPlayer();

        if (logModelLoading)
        {
            Debug.Log(
                "[VRRuntimeModelCatalog] Loading VR lesson model." +
                $"\nIndex: {index}" +
                $"\nLesson: {record.lesson_title}" +
                $"\nModel: {GetDisplayName(record)}" +
                $"\nURL: {modelUrl}");
        }

        GltfImport importer = null;
        GameObject container = null;

        try
        {
            if (!Uri.TryCreate(
                    modelUrl.Trim(),
                    UriKind.Absolute,
                    out Uri modelUri))
            {
                throw new UriFormatException(
                    "The model URL is not a valid absolute HTTP/HTTPS URL.");
            }

            importer = new GltfImport();

            bool loaded =
                await importer.Load(modelUri);

            if (!loaded)
            {
                throw new InvalidOperationException(
                    "glTFast could not load the GLB/GLTF file.");
            }

            container =
                new GameObject(
                    "VRLessonModel_" +
                    MakeSafeObjectName(GetDisplayName(record)));

            container.transform.SetParent(modelAnchor, false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localRotation = Quaternion.identity;
            container.transform.localScale = Vector3.one;

            bool instantiated =
                await importer.InstantiateMainSceneAsync(
                    container.transform);

            if (!instantiated)
            {
                throw new InvalidOperationException(
                    "glTFast loaded the file but could not instantiate its main scene.");
            }

            DisableImportedCamerasAndLights(container);
            ForceRenderersEnabled(container);

            // Let renderer bounds settle after the async import.
            await Task.Yield();
            await Task.Yield();

            NormalizeAndGroundModel(container);
            PrepareRuntimeInteraction(container);

            currentImport = importer;
            importer = null;

            currentModel = container;
            currentModelIndex = index;
            modelVisible = true;

            PlayerPrefs.SetInt(
                SelectedModelIndexKey,
                index);

            PlayerPrefs.SetString(
                "selected_model_asset_id",
                record.asset_id ?? string.Empty);

            PlayerPrefs.SetString(
                "selected_model_lesson_id",
                record.lesson_id ?? string.Empty);

            PlayerPrefs.SetString(
                "selected_model_lesson_title",
                record.lesson_title ?? "Lesson");

            PlayerPrefs.SetString(
                "selected_model_name",
                GetDisplayName(record));

            PlayerPrefs.SetString(
                "selected_model_url",
                modelUrl);

            PlayerPrefs.Save();

            SetLoading(false, string.Empty);
            ModelChanged?.Invoke(index, record, currentModel);
            VisibilityChanged?.Invoke(modelVisible);

            if (logModelLoading)
            {
                Debug.Log(
                    "[VRRuntimeModelCatalog] Model displayed successfully: " +
                    GetDisplayName(record));
            }

            return true;
        }
        catch (Exception exception)
        {
            if (container != null)
                Destroy(container);

            try
            {
                importer?.Dispose();
            }
            catch
            {
                // Ignore importer cleanup exceptions.
            }

            Debug.LogError(
                "[VRRuntimeModelCatalog] Cannot display VR model:\n" +
                exception);

            SetLoading(
                false,
                "Cannot load " + GetDisplayName(record));

            return false;
        }
        finally
        {
            loading = false;
        }
    }

    public void RotateCurrentModel()
    {
        RotateCurrentModel(manualRotateStep);
    }

    public void RotateCurrentModel(float degrees)
    {
        if (currentModel == null)
        {
            Debug.LogWarning(
                "[VRRuntimeModelCatalog] No model is loaded to rotate.");
            return;
        }

        currentModel.transform.Rotate(
            Vector3.up,
            degrees,
            Space.World);
    }

    /// <summary>
    /// Compatibility method used by VRPageController after the Player has been
    /// moved to the back of the classroom. In VR classroom mode the model stays
    /// on the teacher desk, not in front of the Player.
    /// </summary>
    public void RefreshAnchorFromPlayer()
    {
        EnsureAnchor();

        if (placeOnTeacherDesk)
            RepositionCurrentModelAtTeacherDesk();
        else
            RepositionAnchorInFrontOfPlayer();
    }

    public void RefreshPlacementAtTeacherDesk()
    {
        EnsureAnchor();
        RepositionCurrentModelAtTeacherDesk();
    }

    public void ToggleVisibility()
    {
        SetVisible(!modelVisible);
    }

    public void SetVisible(bool visible)
    {
        modelVisible = visible;

        if (currentModel != null)
        {
            Renderer[] renderers =
                currentModel.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        VisibilityChanged?.Invoke(modelVisible);
    }

    public void ToggleAutoRotate()
    {
        autoRotateEnabled = !autoRotateEnabled;
        AutoRotateChanged?.Invoke(autoRotateEnabled);
    }

    public void SetAutoRotate(bool enabled)
    {
        autoRotateEnabled = enabled;
        AutoRotateChanged?.Invoke(autoRotateEnabled);
    }

    public List<VRLessonGroup> BuildLessonGroups()
    {
        List<VRLessonGroup> groups = new();
        Dictionary<string, VRLessonGroup> byLesson =
            new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < models.Count; i++)
        {
            VRModelLaunchItem model = models[i];

            string lessonKey =
                !string.IsNullOrWhiteSpace(model.lesson_id)
                    ? model.lesson_id
                    : (model.lesson_title ?? "Lesson");

            if (!byLesson.TryGetValue(
                    lessonKey,
                    out VRLessonGroup group))
            {
                group = new VRLessonGroup
                {
                    lesson_id = model.lesson_id ?? string.Empty,
                    lesson_title =
                        string.IsNullOrWhiteSpace(model.lesson_title)
                            ? "Lesson"
                            : model.lesson_title,
                    chapter_order = model.chapter_order
                };

                byLesson.Add(lessonKey, group);
                groups.Add(group);
            }

            group.model_indices.Add(i);
        }

        groups.Sort((a, b) =>
        {
            int chapterCompare =
                a.chapter_order.CompareTo(b.chapter_order);

            if (chapterCompare != 0)
                return chapterCompare;

            return string.Compare(
                a.lesson_title,
                b.lesson_title,
                StringComparison.OrdinalIgnoreCase);
        });

        return groups;
    }

    private void EnsureAnchor()
    {
        if (modelAnchor != null)
            return;

        GameObject anchorObject =
            new GameObject("VRRuntimeModelAnchor");

        modelAnchor = anchorObject.transform;
    }

    private void RepositionCurrentModelAtTeacherDesk()
    {
        RepositionAnchorAtTeacherDesk();

        if (currentModel == null)
            return;

        // Re-ground the already loaded model onto the newly resolved desk top.
        NormalizePositionOnly(currentModel);

        VRModelInteractionController interaction =
            currentModel.GetComponent<VRModelInteractionController>();

        interaction?.SaveResetPose();
    }

    private void RepositionAnchorAtTeacherDesk()
    {
        if (modelAnchor == null)
            EnsureAnchor();

        Camera cam = Camera.main;

        if (cam == null)
        {
#if UNITY_2023_1_OR_NEWER
            cam = FindFirstObjectByType<Camera>();
#else
            cam = FindObjectOfType<Camera>();
#endif
        }

        if (!TryGetFloorBounds(out Bounds floorBounds))
        {
            Debug.LogWarning(
                "[VRRuntimeModelCatalog] Classroom Floor bounds were not found. " +
                "Falling back to camera-relative model placement.");

            RepositionAnchorInFrontOfPlayer();
            return;
        }

        Vector3 frontDirection =
            ResolveClassroomFrontDirection(
                floorBounds,
                cam);

        float halfLength =
            Mathf.Abs(frontDirection.x) > 0.5f
                ? floorBounds.extents.x
                : floorBounds.extents.z;

        Vector3 deskCenter =
            floorBounds.center +
            frontDirection *
            Mathf.Max(
                0.25f,
                halfLength -
                teacherDeskInsetFromFront);

        float floorY =
            floorBounds.min.y;

        // If FurnitureBuilder gives the teacher table/desk a meaningful name,
        // use its REAL bounds directly. Otherwise use the front-center fallback.
        if (TryFindNamedTeacherDeskBounds(
                deskCenter,
                out Bounds teacherDeskBounds))
        {
            deskCenter.x =
                teacherDeskBounds.center.x;

            deskCenter.z =
                teacherDeskBounds.center.z;

            deskCenter.y =
                teacherDeskBounds.max.y +
                modelDeskClearance;
        }
        else
        {
            float deskTopY =
                FindTeacherDeskTopY(
                    deskCenter,
                    floorY);

            deskCenter.y =
                deskTopY +
                modelDeskClearance;
        }

        modelAnchor.position =
            deskCenter;

        // Face the model toward the student/back of the classroom.
        Vector3 towardStudents =
            -frontDirection;

        if (towardStudents.sqrMagnitude < 0.001f)
            towardStudents = Vector3.back;

        modelAnchor.rotation =
            Quaternion.LookRotation(
                towardStudents.normalized,
                Vector3.up);

        Debug.Log(
            "[VRRuntimeModelCatalog] Teacher-desk model anchor resolved. " +
            $"Position={modelAnchor.position}, Front={frontDirection}");
    }

    private Vector3 ResolveClassroomFrontDirection(
        Bounds floorBounds,
        Camera cam)
    {
        Transform frontReference =
            FindFrontReference();

        if (frontReference != null)
        {
            Vector3 direction =
                frontReference.position -
                floorBounds.center;

            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
                return SnapToDominantAxis(direction.normalized);
        }

        // VRPageController positions the Player at the back facing the board.
        // After startup the camera forward therefore points toward the class front.
        if (cam != null)
        {
            Vector3 direction = cam.transform.forward;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
                return SnapToDominantAxis(direction.normalized);
        }

        return Vector3.forward;
    }

    private static Vector3 SnapToDominantAxis(
        Vector3 direction)
    {
        if (Mathf.Abs(direction.x) >
            Mathf.Abs(direction.z))
        {
            return new Vector3(
                Mathf.Sign(direction.x),
                0f,
                0f);
        }

        return new Vector3(
            0f,
            0f,
            Mathf.Sign(
                Mathf.Approximately(direction.z, 0f)
                    ? 1f
                    : direction.z));
    }

    private Transform FindFrontReference()
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
            "teacher desk",
            "teacherdesk",
            "teacher table",
            "teachertable",
            "podium",
            "lectern",
            "whiteboard",
            "blackboard",
            "board",
            "front"
        };

        foreach (string token in preferredTokens)
        {
            foreach (Transform item in transforms)
            {
                if (item == null)
                    continue;

                string normalized =
                    NormalizeObjectName(
                        item.name);

                if (normalized.Contains(
                        NormalizeObjectName(token)))
                {
                    return item;
                }
            }
        }

        return null;
    }

    private bool TryGetFloorBounds(
        out Bounds bounds)
    {
        bounds = default;
        bool found = false;

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

            string objectName =
                NormalizeObjectName(
                    renderer.gameObject.name);

            string parentName =
                renderer.transform.parent != null
                    ? NormalizeObjectName(
                        renderer.transform.parent.name)
                    : string.Empty;

            bool looksLikeFloor =
                objectName.Contains("floor") ||
                parentName.Contains("floor");

            if (!looksLikeFloor)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(
                    renderer.bounds);
            }
        }

        return found;
    }

    private bool TryFindNamedTeacherDeskBounds(
        Vector3 expectedDeskCenter,
        out Bounds deskBounds)
    {
        deskBounds = default;

#if UNITY_2023_1_OR_NEWER
        Renderer[] renderers =
            FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
        Renderer[] renderers =
            FindObjectsOfType<Renderer>();
#endif

        Renderer best = null;
        float bestDistance = float.MaxValue;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            string normalized =
                NormalizeObjectName(
                    renderer.gameObject.name + " " +
                    (renderer.transform.parent != null
                        ? renderer.transform.parent.name
                        : string.Empty));

            bool teacherDeskName =
                normalized.Contains("teacher desk") ||
                normalized.Contains("teacherdesk") ||
                normalized.Contains("teacher table") ||
                normalized.Contains("teachertable") ||
                normalized.Contains("podium") ||
                normalized.Contains("lectern");

            if (!teacherDeskName)
                continue;

            Vector3 delta =
                renderer.bounds.center -
                expectedDeskCenter;

            delta.y = 0f;

            float distance =
                delta.sqrMagnitude;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = renderer;
            }
        }

        if (best == null)
            return false;

        deskBounds = best.bounds;
        return true;
    }

    private float FindTeacherDeskTopY(
        Vector3 expectedDeskCenter,
        float floorY)
    {
        // Prefer explicitly named teacher-desk geometry.
#if UNITY_2023_1_OR_NEWER
        Renderer[] renderers =
            FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
        Renderer[] renderers =
            FindObjectsOfType<Renderer>();
#endif

        Renderer bestNamedDesk = null;
        float bestNamedDistance = float.MaxValue;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            string normalized =
                NormalizeObjectName(
                    renderer.gameObject.name + " " +
                    (renderer.transform.parent != null
                        ? renderer.transform.parent.name
                        : string.Empty));

            bool teacherDeskName =
                normalized.Contains("teacher desk") ||
                normalized.Contains("teacherdesk") ||
                normalized.Contains("teacher table") ||
                normalized.Contains("teachertable") ||
                normalized.Contains("podium") ||
                normalized.Contains("lectern");

            if (!teacherDeskName)
                continue;

            Vector3 delta =
                renderer.bounds.center -
                expectedDeskCenter;

            delta.y = 0f;

            float distance =
                delta.sqrMagnitude;

            if (distance <
                bestNamedDistance)
            {
                bestNamedDistance = distance;
                bestNamedDesk = renderer;
            }
        }

        if (bestNamedDesk != null)
            return bestNamedDesk.bounds.max.y;

        // Generic fallback: sample downward rays around the expected front-center
        // position and select a tabletop-like hit above the floor.
        float highestReasonableY =
            floorY +
            1.45f;

        float bestY =
            float.NegativeInfinity;

        Vector2[] samples =
        {
            Vector2.zero,
            new Vector2(0.35f, 0f),
            new Vector2(-0.35f, 0f),
            new Vector2(0f, 0.35f),
            new Vector2(0f, -0.35f),
            new Vector2(0.55f, 0.35f),
            new Vector2(-0.55f, 0.35f),
            new Vector2(0.55f, -0.35f),
            new Vector2(-0.55f, -0.35f)
        };

        foreach (Vector2 sample in samples)
        {
            Vector3 origin =
                expectedDeskCenter +
                new Vector3(
                    sample.x *
                    teacherDeskSearchRadius,
                    3f,
                    sample.y *
                    teacherDeskSearchRadius);

            RaycastHit[] hits =
                Physics.RaycastAll(
                    origin,
                    Vector3.down,
                    6f,
                    ~0,
                    QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null)
                    continue;

                if (currentModel != null &&
                    hit.collider.transform.IsChildOf(
                        currentModel.transform))
                {
                    continue;
                }

                float y =
                    hit.point.y;

                bool tabletopHeight =
                    y > floorY + 0.35f &&
                    y < highestReasonableY;

                if (tabletopHeight)
                    bestY = Mathf.Max(bestY, y);
            }
        }

        if (!float.IsNegativeInfinity(bestY))
            return bestY;

        return floorY +
               fallbackTeacherDeskHeight;
    }

    private static string NormalizeObjectName(
        string value)
    {
        return (value ?? string.Empty)
            .Replace("_", " ")
            .Replace("-", " ")
            .ToLowerInvariant()
            .Trim();
    }

    private void NormalizePositionOnly(
        GameObject target)
    {
        if (target == null ||
            modelAnchor == null)
        {
            return;
        }

        if (!TryCalculateBounds(
                target,
                out Bounds bounds))
        {
            target.transform.position =
                modelAnchor.position;

            return;
        }

        Vector3 desiredBottomCenter =
            modelAnchor.position;

        Vector3 currentBottomCenter =
            new Vector3(
                bounds.center.x,
                bounds.min.y,
                bounds.center.z);

        target.transform.position +=
            desiredBottomCenter -
            currentBottomCenter;
    }

    private void PrepareRuntimeInteraction(
        GameObject target)
    {
        if (target == null)
            return;

        VRModelInteractionController interaction =
            target.GetComponent<VRModelInteractionController>();

        if (interaction == null)
        {
            interaction =
                target.AddComponent<VRModelInteractionController>();
        }

        interaction.Initialize(
            target,
            modelAnchor);
    }

    private void RepositionAnchorInFrontOfPlayer()
    {
        Camera cam = Camera.main;

        if (cam == null)
        {
#if UNITY_2023_1_OR_NEWER
            cam = FindFirstObjectByType<Camera>();
#else
            cam = FindObjectOfType<Camera>();
#endif
        }

        if (cam == null)
        {
            modelAnchor.position =
                new Vector3(0f, verticalOffset, 2.2f);

            modelAnchor.rotation =
                Quaternion.identity;

            return;
        }

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        forward.Normalize();

        Vector3 target =
            cam.transform.position +
            forward * distanceInFrontOfPlayer;

        float floorY = FindFloorY(target, cam.transform.position.y);
        target.y = floorY + verticalOffset;

        modelAnchor.position = target;

        modelAnchor.rotation =
            Quaternion.LookRotation(forward, Vector3.up);
    }

    private static float FindFloorY(
        Vector3 target,
        float cameraY)
    {
        Vector3 rayOrigin =
            new Vector3(
                target.x,
                Mathf.Max(cameraY + 2f, target.y + 4f),
                target.z);

        RaycastHit[] hits =
            Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                20f,
                ~0,
                QueryTriggerInteraction.Ignore);

        Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance));

        // Prefer an object explicitly named Floor.
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
                continue;

            string objectName =
                hit.collider.gameObject.name ?? string.Empty;

            string parentName =
                hit.collider.transform.parent != null
                    ? hit.collider.transform.parent.name
                    : string.Empty;

            if (objectName.IndexOf(
                    "floor",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                parentName.IndexOf(
                    "floor",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return hit.point.y;
            }
        }

        // Generated classroom floor is normally y = 0.
        return 0f;
    }

    private void NormalizeAndGroundModel(
        GameObject target)
    {
        if (!TryCalculateBounds(
                target,
                out Bounds bounds))
        {
            Debug.LogWarning(
                "[VRRuntimeModelCatalog] Loaded model has no Renderer. " +
                "Size normalization was skipped.");
            return;
        }

        float largestDimension =
            Mathf.Max(
                bounds.size.x,
                bounds.size.y,
                bounds.size.z);

        if (largestDimension > 0.0001f)
        {
            float multiplier =
                normalizedLargestDimension /
                largestDimension;

            target.transform.localScale *= multiplier;
        }

        if (!TryCalculateBounds(
                target,
                out bounds))
        {
            return;
        }

        Vector3 anchorPosition =
            modelAnchor.position;

        Vector3 desiredBottomCenter =
            new Vector3(
                anchorPosition.x,
                anchorPosition.y,
                anchorPosition.z);

        Vector3 currentBottomCenter =
            new Vector3(
                bounds.center.x,
                bounds.min.y,
                bounds.center.z);

        target.transform.position +=
            desiredBottomCenter -
            currentBottomCenter;
    }

    private static bool TryCalculateBounds(
        GameObject target,
        out Bounds bounds)
    {
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);

        Renderer first = null;

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                first = renderer;
                break;
            }
        }

        if (first == null)
        {
            bounds = default;
            return false;
        }

        bounds = first.bounds;

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
                bounds.Encapsulate(renderer.bounds);
        }

        return true;
    }

    private static void DisableImportedCamerasAndLights(
        GameObject target)
    {
        foreach (Camera importedCamera
                 in target.GetComponentsInChildren<Camera>(true))
        {
            importedCamera.enabled = false;
        }

        foreach (Light importedLight
                 in target.GetComponentsInChildren<Light>(true))
        {
            importedLight.enabled = false;
        }
    }

    private static void ForceRenderersEnabled(
        GameObject target)
    {
        foreach (Renderer renderer
                 in target.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
        }

        target.SetActive(true);
    }

    private void ClearCurrentModel()
    {
        if (currentModel != null)
        {
            Destroy(currentModel);
            currentModel = null;
        }

        currentModelIndex = -1;
        DisposeCurrentImport();
    }

    private void DisposeCurrentImport()
    {
        if (currentImport == null)
            return;

        try
        {
            currentImport.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[VRRuntimeModelCatalog] Importer cleanup warning: " +
                exception.Message);
        }

        currentImport = null;
    }

    private void SetLoading(
        bool value,
        string message)
    {
        loading = value;
        LoadingStateChanged?.Invoke(value, message ?? string.Empty);
    }

    private static string GetBestUrl(
        VRModelLaunchItem record)
    {
        if (record == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(record.url))
            return record.url.Trim();

        return record.fallback_url?.Trim() ??
               string.Empty;
    }

    public static string GetDisplayName(
        VRModelLaunchItem record)
    {
        if (record == null)
            return "3D Model";

        if (!string.IsNullOrWhiteSpace(record.name))
            return record.name;

        if (!string.IsNullOrWhiteSpace(record.file_name))
        {
            string file = record.file_name;

            int dot = file.LastIndexOf('.');
            if (dot > 0)
                file = file.Substring(0, dot);

            return file;
        }

        return "3D Model";
    }

    private static string MakeSafeObjectName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Model";

        foreach (char invalid in
                 new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
        {
            value = value.Replace(invalid, '_');
        }

        return value.Trim();
    }
}

[Serializable]
public class VRModelLaunchManifest
{
    public string class_id;
    public string lesson_id;
    public string mode;
    public VRModelLaunchItem[] models;
}

[Serializable]
public class VRModelLaunchItem
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

public class VRLessonGroup
{
    public string lesson_id;
    public string lesson_title;
    public int chapter_order;
    public readonly List<int> model_indices = new();
}
