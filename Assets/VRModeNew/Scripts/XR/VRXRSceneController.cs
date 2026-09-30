using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Management;

/// <summary>
/// Headset version of the VR classroom (VRClassroomXRScene).
///
/// The phone version (VRClassroomScene) is left untouched. This scene reuses the
/// same classroom, model catalog and AI detail markers, but:
///  - the player is an XR Origin (head tracking + two controllers),
///  - the lesson model can be grabbed with the controllers (Grip),
///    and scaled with two hands,
///  - the menu is a world-space panel that stays near the user's view,
///  - in the Unity Editor without a headset, the XR Interaction Simulator is
///    spawned automatically so the scene can be tested with mouse + keyboard.
/// </summary>
public class VRXRSceneController : MonoBehaviour
{
    private const string PreviousSceneKey = "previous_scene";

    [Header("Navigation")]
    [SerializeField] private string fallbackPreviousScene = "ShowLessonScene";

    [Header("Editor testing")]
    [Tooltip("Spawn the XR Interaction Simulator in Play Mode when no headset is active.")]
    [SerializeField] private bool spawnSimulatorInEditor = true;

    [Header("Menu placement")]
    [SerializeField, Range(0.5f, 2f)] private float menuDistance = 0.95f;
    [SerializeField, Range(-80f, 80f)] private float menuYawOffset = -38f;
    [SerializeField, Range(-0.8f, 0.4f)] private float menuHeightOffset = -0.22f;
    [SerializeField, Range(20f, 120f)] private float menuFollowAngle = 70f;
    [SerializeField, Range(1f, 12f)] private float menuFollowSpeed = 5f;

    private VRRuntimeModelCatalog catalog;
    private XROrigin origin;
    private Transform head;

    private Transform menuRoot;
    private Text titleText;
    private Text statusText;
    private Text visibilityButtonText;
    private Text rotateButtonText;
    private Button previousButton;
    private Button nextButton;

    private GameObject currentModel;
    private bool menuFollowing;
    private Vector3 menuTargetPosition;
    private Quaternion menuTargetRotation;
    private bool menuPlaced;
    private Font uiFont;

    // =========================================================
    // LIFECYCLE
    // =========================================================

    private void Awake()
    {
        // Active catalogs only: a catalog on a disabled phone-only object never loads models.
        catalog = FindFirstObjectByType<VRRuntimeModelCatalog>();
        if (catalog == null)
            catalog = gameObject.AddComponent<VRRuntimeModelCatalog>();

        catalog.CatalogReady += RefreshMenu;
        catalog.ModelChanged += HandleModelChanged;
        catalog.LoadingStateChanged += HandleLoadingChanged;
        catalog.VisibilityChanged += HandleVisibilityChanged;
        catalog.AutoRotateChanged += HandleAutoRotateChanged;
    }

    private void Start()
    {
        origin = FindFirstObjectByType<XROrigin>();
        if (origin != null && origin.Camera != null)
            head = origin.Camera.transform;
        else if (Camera.main != null)
            head = Camera.main.transform;

        EnsureXRUIEventSystem();
        BuildMenu();
        RefreshMenu();

        // Check again after the XR Interaction Simulator / interactors finish
        // their own setup (they can add UI modules a few frames later).
        StartCoroutine(RecheckUIEventSystemRoutine());

        StartCoroutine(PlaceOriginOnFloorRoutine());

#if UNITY_EDITOR
        if (spawnSimulatorInEditor)
            StartCoroutine(SpawnSimulatorIfNoHeadsetRoutine());
#endif
    }

    private void OnDestroy()
    {
        if (catalog == null)
            return;

        catalog.CatalogReady -= RefreshMenu;
        catalog.ModelChanged -= HandleModelChanged;
        catalog.LoadingStateChanged -= HandleLoadingChanged;
        catalog.VisibilityChanged -= HandleVisibilityChanged;
        catalog.AutoRotateChanged -= HandleAutoRotateChanged;
    }

    private void LateUpdate()
    {
        UpdateMenuFollow();
    }

    // =========================================================
    // XR RIG / INPUT
    // =========================================================

    private IEnumerator RecheckUIEventSystemRoutine()
    {
        yield return null;
        EnsureXRUIEventSystem();
        yield return new WaitForSecondsRealtime(2f);
        EnsureXRUIEventSystem();
    }

    private IEnumerator PlaceOriginOnFloorRoutine()
    {
        // The classroom (floor, desks) is generated at runtime; wait for it.
        yield return null;
        yield return null;

        if (origin != null)
        {
            Vector3 p = origin.transform.position;
            RaycastHit[] hits = Physics.RaycastAll(
                p + Vector3.up * 1.5f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);

            float bestY = float.NegativeInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null ||
                    hit.collider.GetComponentInParent<XROrigin>() != null)
                    continue;

                // Highest surface that is not above the start height (ignore ceilings).
                if (hit.point.y <= p.y + 0.6f && hit.point.y > bestY)
                    bestY = hit.point.y;
            }

            if (!float.IsNegativeInfinity(bestY))
                origin.transform.position = new Vector3(p.x, bestY, p.z);
        }

        yield return null;
        PlaceMenuInFront(true);
    }

    private static void EnsureXRUIEventSystem()
    {
        EventSystem eventSystem =
            FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);

        if (eventSystem == null)
            eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();

        eventSystem.gameObject.SetActive(true);

        // The phone scene uses the Input System UI module (touch/mouse).
        // Controllers need the XR UI Input Module to click world-space UI.
        // The XR interactors may already have added an XRUIInputModule in
        // OnEnable, but they do NOT remove the Input System UI module; while
        // both are enabled the old module wins and the controllers cannot
        // press any button. So always disable every non-XR module.
        XRUIInputModule xrModule = eventSystem.GetComponent<XRUIInputModule>();
        if (xrModule == null)
            xrModule = eventSystem.gameObject.AddComponent<XRUIInputModule>();

        foreach (BaseInputModule module in eventSystem.GetComponents<BaseInputModule>())
        {
            if (module != xrModule)
                module.enabled = false;
        }

        xrModule.enabled = true;
    }

#if UNITY_EDITOR
    private IEnumerator SpawnSimulatorIfNoHeadsetRoutine()
    {
        // VRSessionBootstrap may still be starting a real headset (Quest Link).
        yield return new WaitForSecondsRealtime(1.5f);

        if (XRSettings.isDeviceActive ||
            (XRGeneralSettings.Instance != null &&
             XRGeneralSettings.Instance.Manager != null &&
             XRGeneralSettings.Instance.Manager.activeLoader != null))
        {
            Debug.Log("[VRXRSceneController] Headset detected; XR Interaction Simulator not spawned.");
            yield break;
        }

        if (FindFirstObjectByType<XRInteractionSimulator>(FindObjectsInactive.Include) != null)
            yield break; // Already added by the XRI project setting.

        GameObject prefab = null;
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("\"XR Interaction Simulator\" t:Prefab"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("/XR Interaction Simulator.prefab"))
            {
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                break;
            }
        }

        if (prefab == null)
        {
            Debug.LogWarning(
                "[VRXRSceneController] XR Interaction Simulator sample is not imported. " +
                "Run menu '3D Education > VR > Build Headset VR Scene' or import it from " +
                "Package Manager > XR Interaction Toolkit > Samples.");
            yield break;
        }

        GameObject simulator = Instantiate(prefab);
        simulator.name = "XR Interaction Simulator";
        Debug.Log("[VRXRSceneController] XR Interaction Simulator spawned for Editor testing.");
    }
#endif

    // =========================================================
    // MODEL
    // =========================================================

    private void HandleModelChanged(int index, VRModelLaunchItem record, GameObject model)
    {
        currentModel = model;
        MakeGrabbable(model);
        RefreshMenu();
    }

    private void MakeGrabbable(GameObject model)
    {
        if (model == null)
            return;

        // The phone scene's mouse/touch interaction must not fight the XR grab.
        // Keep the component (its saved reset pose is used by "Reset model").
        VRModelInteractionController legacy = model.GetComponent<VRModelInteractionController>();
        if (legacy != null)
            legacy.enabled = false;

        Rigidbody body = model.GetComponent<Rigidbody>();
        if (body == null)
            body = model.AddComponent<Rigidbody>();

        body.isKinematic = true;
        body.useGravity = false;

        if (model.GetComponentInChildren<Collider>() == null)
            FitBoxCollider(model);

        if (model.GetComponent<XRGrabInteractable>() != null)
            return;

        XRGrabInteractable grab = model.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.throwOnDetach = false;
        grab.useDynamicAttach = true;
        grab.selectMode = InteractableSelectMode.Multiple;

        XRGeneralGrabTransformer transformer = model.AddComponent<XRGeneralGrabTransformer>();
        transformer.allowTwoHandedScaling = true;

        // Auto-rotation would fight the hand that is holding the model.
        grab.selectEntered.AddListener(_ =>
        {
            if (catalog != null && catalog.AutoRotateEnabled)
                catalog.SetAutoRotate(false);
        });
    }

    private static void FitBoxCollider(GameObject model)
    {
        BoxCollider box = model.AddComponent<BoxCollider>();
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        box.center = model.transform.InverseTransformPoint(bounds.center);
        Vector3 lossy = model.transform.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
    }

    private void ResetModel()
    {
        if (currentModel == null)
            return;

        XRGrabInteractable grab = currentModel.GetComponent<XRGrabInteractable>();
        if (grab != null && grab.isSelected)
            return;

        VRModelInteractionController legacy = currentModel.GetComponent<VRModelInteractionController>();
        if (legacy != null)
            legacy.ResetToTeacherDesk();
        else
            catalog?.RefreshPlacementAtTeacherDesk();
    }

    private void ShowModel(int step)
    {
        if (catalog == null || !catalog.HasModels || catalog.IsLoading)
            return;

        int count = catalog.Models.Count;
        int next = ((catalog.CurrentModelIndex + step) % count + count) % count;
        catalog.SelectModel(next);
    }

    private void GoBack()
    {
        string previousScene = PlayerPrefs.GetString(PreviousSceneKey, fallbackPreviousScene);

        if (string.IsNullOrWhiteSpace(previousScene) ||
            previousScene == gameObject.scene.name ||
            previousScene == "VRClassroomScene")
        {
            previousScene = fallbackPreviousScene;
        }

        if (!Application.CanStreamedLevelBeLoaded(previousScene))
        {
            Debug.LogError($"[VRXRSceneController] Scene '{previousScene}' is not in Build Profiles.");
            return;
        }

        SceneManager.LoadScene(previousScene);
    }

    // =========================================================
    // CATALOG EVENTS
    // =========================================================

    private void HandleLoadingChanged(bool loading, string message) => RefreshMenu();
    private void HandleVisibilityChanged(bool visible) => RefreshMenu();
    private void HandleAutoRotateChanged(bool enabled) => RefreshMenu();

    // =========================================================
    // WORLD-SPACE MENU
    // =========================================================

    private static string T(string english, string vietnamese) =>
        AppLanguageManager.IsVietnamese ? vietnamese : english;

    private void BuildMenu()
    {
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasObject = new GameObject("XR Menu", typeof(RectTransform));
        menuRoot = canvasObject.transform;

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = head != null ? head.GetComponent<Camera>() : Camera.main;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 3f;

        canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        RectTransform canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = new Vector2(520f, 600f);
        canvasObject.transform.localScale = Vector3.one * 0.001f; // 0.52 m wide

        Image background = canvasObject.AddComponent<Image>();
        background.color = new Color32(247, 249, 253, 240);

        VerticalLayoutGroup layout = canvasObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 26, 26);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        titleText = CreateText(canvasRect, "3D Model", 34, FontStyle.Bold, new Color32(8, 28, 70, 255), 88f);
        statusText = CreateText(canvasRect, string.Empty, 22, FontStyle.Normal, new Color32(90, 106, 138, 255), 34f);

        RectTransform row = CreateRow(canvasRect);
        previousButton = CreateButton(row, T("< Previous", "< Mô hình trước"), () => ShowModel(-1), out _);
        nextButton = CreateButton(row, T("Next >", "Mô hình sau >"), () => ShowModel(1), out _);

        CreateButton(canvasRect, string.Empty, () => catalog?.ToggleVisibility(), out visibilityButtonText);
        CreateButton(canvasRect, string.Empty, () => catalog?.ToggleAutoRotate(), out rotateButtonText);
        CreateButton(canvasRect, T("Reset model", "Đặt lại mô hình"), ResetModel, out _);
        CreateButton(canvasRect, T("Back to lesson", "Quay lại bài học"), GoBack, out _,
            new Color32(214, 60, 60, 255));

        CreateText(
            canvasRect,
            T("Grip: grab the model. Two hands: resize.",
              "Grip: cầm mô hình. Hai tay: phóng to/thu nhỏ."),
            20, FontStyle.Italic, new Color32(110, 124, 150, 255), 30f);

        PlaceMenuInFront(true);
    }

    private void RefreshMenu()
    {
        if (titleText == null || catalog == null)
            return;

        bool hasModels = catalog.HasModels;
        int count = hasModels ? catalog.Models.Count : 0;
        int index = catalog.CurrentModelIndex;

        if (hasModels && index >= 0 && index < count)
            titleText.text = VRRuntimeModelCatalog.GetDisplayName(catalog.Models[index]);
        else
            titleText.text = T("No 3D model", "Chưa có mô hình 3D");

        if (catalog.IsLoading)
            statusText.text = T("Loading 3D model...", "Đang tải mô hình 3D...");
        else if (!hasModels)
            statusText.text = T("Open VR Mode from a lesson.", "Hãy mở VR Mode từ một bài học.");
        else
            statusText.text = T($"Model {index + 1} / {count}", $"Mô hình {index + 1} / {count}");

        bool canSwitch = hasModels && count > 1 && !catalog.IsLoading;
        if (previousButton != null) previousButton.interactable = canSwitch;
        if (nextButton != null) nextButton.interactable = canSwitch;

        if (visibilityButtonText != null)
            visibilityButtonText.text = catalog.ModelVisible
                ? T("Hide model", "Ẩn mô hình")
                : T("Show model", "Hiện mô hình");

        if (rotateButtonText != null)
            rotateButtonText.text = catalog.AutoRotateEnabled
                ? T("Stop auto-rotate", "Tắt tự xoay")
                : T("Auto-rotate", "Bật tự xoay");
    }

    private Text CreateText(RectTransform parent, string text, int size, FontStyle style, Color color, float height)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Text label = go.AddComponent<Text>();
        label.font = uiFont;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.raycastTarget = false;

        LayoutElement element = go.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        return label;
    }

    private static RectTransform CreateRow(RectTransform parent)
    {
        GameObject go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        HorizontalLayoutGroup row = go.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 12f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = true;
        row.childForceExpandHeight = true;

        go.AddComponent<LayoutElement>().preferredHeight = 64f;
        return (RectTransform)go.transform;
    }

    private Button CreateButton(
        RectTransform parent,
        string label,
        UnityEngine.Events.UnityAction onClick,
        out Text labelText,
        Color32? color = null)
    {
        GameObject go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = color ?? new Color32(25, 91, 207, 255);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.85f, 0.9f, 1f, 1f);
        colors.pressedColor = new Color(0.7f, 0.78f, 0.95f, 1f);
        colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.6f);
        button.colors = colors;
        button.onClick.AddListener(onClick);

        go.AddComponent<LayoutElement>().preferredHeight = 64f;

        GameObject textObject = new GameObject("Label", typeof(RectTransform));
        textObject.transform.SetParent(go.transform, false);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 0f);
        textRect.offsetMax = new Vector2(-10f, 0f);

        labelText = textObject.AddComponent<Text>();
        labelText.font = uiFont;
        labelText.text = label;
        labelText.fontSize = 24;
        labelText.fontStyle = FontStyle.Bold;
        labelText.color = Color.white;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.raycastTarget = false;

        return button;
    }

    private bool TryGetMenuPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = default;

        if (head == null)
            return false;

        Vector3 forward = head.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = head.up; // looking straight down/up
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 direction = Quaternion.Euler(0f, menuYawOffset, 0f) * forward;
        position = head.position + direction * menuDistance + Vector3.up * menuHeightOffset;

        // A world-space canvas is read from the side its +Z points away from.
        Vector3 look = position - head.position;
        look.y = 0f;
        rotation = Quaternion.LookRotation(look.sqrMagnitude > 0.0001f ? look : forward, Vector3.up);
        return true;
    }

    private void PlaceMenuInFront(bool instant)
    {
        if (menuRoot == null || !TryGetMenuPose(out Vector3 position, out Quaternion rotation))
            return;

        menuTargetPosition = position;
        menuTargetRotation = rotation;

        if (instant || !menuPlaced)
        {
            menuRoot.SetPositionAndRotation(position, rotation);
            menuPlaced = true;
            menuFollowing = false;
        }
        else
        {
            menuFollowing = true;
        }
    }

    private void UpdateMenuFollow()
    {
        if (menuRoot == null || head == null)
            return;

        // Lazy follow: only move the menu when it drifts far out of view,
        // so it does not chase every small head movement.
        Vector3 toMenu = menuRoot.position - head.position;
        Vector3 flatToMenu = new Vector3(toMenu.x, 0f, toMenu.z);
        Vector3 flatForward = new Vector3(head.forward.x, 0f, head.forward.z);

        bool outOfView =
            flatForward.sqrMagnitude > 0.0001f &&
            flatToMenu.sqrMagnitude > 0.0001f &&
            Vector3.Angle(flatForward, flatToMenu) > menuFollowAngle;

        bool tooFar = flatToMenu.magnitude > menuDistance * 2.2f;

        if (!menuFollowing && (outOfView || tooFar))
            PlaceMenuInFront(false);

        if (!menuFollowing)
            return;

        float t = 1f - Mathf.Exp(-menuFollowSpeed * Time.unscaledDeltaTime);
        menuRoot.SetPositionAndRotation(
            Vector3.Lerp(menuRoot.position, menuTargetPosition, t),
            Quaternion.Slerp(menuRoot.rotation, menuTargetRotation, t));

        if ((menuRoot.position - menuTargetPosition).sqrMagnitude < 0.0004f)
            menuFollowing = false;
    }
}
