using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Management;
using TMPro;

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
    private Text annotationButtonText;
    private Button previousButton;
    private Button nextButton;

    private GameObject currentModel;
    private bool menuFollowing;
    private Vector3 menuTargetPosition;
    private Quaternion menuTargetRotation;
    private bool menuPlaced;
    private Font uiFont;

    private Transform leftHandTransform;
    private Transform rightHandTransform;
    private float cameraPitch = 0f;
    private Vector3 initialOriginPosition;
    private bool wasFocusedLastFrame = false;
    private bool isLaptopGripActive = false;
    private Vector3 laptopGripOffset = Vector3.forward;

    [Header("Model Detail & Information Panel")]
    private VRModelDetailService detailService;
    private VRModelDetailAnchorController detailAnchorController;
    private VRAnnotationController annotationController;
    private Transform infoPanelRoot;
    private TextMeshProUGUI infoTitleText;
    private TextMeshProUGUI infoConfidenceText;
    private TextMeshProUGUI infoDescHeader;
    private TextMeshProUGUI infoDescText;
    private TextMeshProUGUI infoStructHeader;
    private TextMeshProUGUI infoStructText;
    private TextMeshProUGUI infoFuncHeader;
    private TextMeshProUGUI infoFuncText;
    private VRModelDetailService.ModelPartData activePartData;
    private readonly List<GameObject> activeMarkerObjects = new List<GameObject>();

    // =========================================================
    // LIFECYCLE
    // =========================================================

    private void Awake()
    {
        // Active catalogs only: a catalog on a disabled phone-only object never loads models.
        catalog = FindAnyObjectByType<VRRuntimeModelCatalog>();
        if (catalog == null)
            catalog = gameObject.AddComponent<VRRuntimeModelCatalog>();

        catalog.CatalogReady += RefreshMenu;
        catalog.ModelChanged += HandleModelChanged;
        catalog.LoadingStateChanged += HandleLoadingChanged;
        catalog.VisibilityChanged += HandleVisibilityChanged;
        catalog.AutoRotateChanged += HandleAutoRotateChanged;

        detailService = FindAnyObjectByType<VRModelDetailService>();
        if (detailService == null)
            detailService = gameObject.AddComponent<VRModelDetailService>();

        detailAnchorController = FindAnyObjectByType<VRModelDetailAnchorController>();
        if (detailAnchorController == null)
            detailAnchorController = gameObject.AddComponent<VRModelDetailAnchorController>();

        annotationController = FindAnyObjectByType<VRAnnotationController>();
        if (annotationController == null)
            annotationController = gameObject.AddComponent<VRAnnotationController>();

        annotationController.Initialize(this, detailAnchorController);

        if (detailService != null)
            detailService.OnModelPartsLoaded += HandleModelPartsLoaded;

        AppLanguageManager.LanguageChanged += HandleLanguageChanged;
    }

    private void Start()
    {
        origin = FindAnyObjectByType<XROrigin>();
        if (origin != null)
        {
            initialOriginPosition = origin.transform.position;
            if (origin.Camera != null)
                head = origin.Camera.transform;
        }
        
        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        EnsureXRUIEventSystem();
        EnsureControllerModels();
        BuildMenu();
        BuildInfoPanel();
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

    private void EnsureControllerModels()
    {
        if (origin == null)
            origin = FindAnyObjectByType<XROrigin>();

        if (origin == null)
            return;

        leftHandTransform = origin.transform.Find("Camera Offset/Left Controller");
        if (leftHandTransform == null) leftHandTransform = origin.transform.Find("Left Controller");

        rightHandTransform = origin.transform.Find("Camera Offset/Right Controller");
        if (rightHandTransform == null) rightHandTransform = origin.transform.Find("Right Controller");

        if (leftHandTransform != null)
        {
            leftHandTransform.gameObject.SetActive(true);
#if UNITY_EDITOR
            MeshRenderer mr = leftHandTransform.GetComponentInChildren<MeshRenderer>(true);
            if (mr == null)
            {
                GameObject leftPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Controllers/XR Controller Left.prefab");
                if (leftPrefab != null)
                {
                    GameObject leftModel = Instantiate(leftPrefab, leftHandTransform);
                    leftModel.name = "XR Controller Left";
                    leftModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
                    SetLayerRecursively(leftModel, 0);
                }
            }
            else
            {
                SetLayerRecursively(leftHandTransform.gameObject, 0);
            }
#endif
            EnsureRayLaserVisual(leftHandTransform.gameObject);
            ConfigureLaserConvergence(leftHandTransform, 4.0f);
        }

        if (rightHandTransform != null)
        {
            rightHandTransform.gameObject.SetActive(true);
#if UNITY_EDITOR
            MeshRenderer mr = rightHandTransform.GetComponentInChildren<MeshRenderer>(true);
            if (mr == null)
            {
                GameObject rightPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab");
                if (rightPrefab != null)
                {
                    GameObject rightModel = Instantiate(rightPrefab, rightHandTransform);
                    rightModel.name = "XR Controller Right";
                    rightModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
                    SetLayerRecursively(rightModel, 0);
                }
            }
            else
            {
                SetLayerRecursively(rightHandTransform.gameObject, 0);
            }
#endif
            EnsureRayLaserVisual(rightHandTransform.gameObject);
            ConfigureLaserConvergence(rightHandTransform, -4.0f);
        }
    }

    private static void ConfigureLaserConvergence(Transform handTransform, float yAngle)
    {
        if (handTransform == null) return;

        Transform attach = handTransform.Find("Ray Attach");
        if (attach == null)
        {
            GameObject attachGo = new GameObject("Ray Attach");
            attachGo.transform.SetParent(handTransform, false);
            attach = attachGo.transform;
        }

        attach.localRotation = Quaternion.Euler(0f, yAngle, 0f);

        XRRayInteractor rayInteractor = handTransform.GetComponentInChildren<XRRayInteractor>(true);
        if (rayInteractor != null)
        {
            rayInteractor.attachTransform = attach;
            rayInteractor.rayOriginTransform = attach;
        }
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static void EnsureRayLaserVisual(GameObject handObject)
    {
        if (handObject == null) return;
        LineRenderer line = handObject.GetComponent<LineRenderer>();
        if (line == null)
            line = handObject.AddComponent<LineRenderer>();

        line.useWorldSpace = false;
        line.positionCount = 2;
        line.SetPosition(0, new Vector3(0f, 0f, 0.05f));
        line.SetPosition(1, new Vector3(0f, 0f, 4.5f));
        line.startWidth = 0.006f;
        line.endWidth = 0.002f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = new Color(1f, 1f, 1f, 0.95f);
        line.endColor = new Color(1f, 1f, 1f, 0.2f);

        // Feature 4: Laser Aiming Reticle Dot
        Transform reticle = handObject.transform.Find("LaserReticle");
        if (reticle == null)
        {
            GameObject reticleObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticleObj.name = "LaserReticle";
            Collider c = reticleObj.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.Destroy(c);

            reticleObj.transform.SetParent(handObject.transform, false);
            reticleObj.transform.localPosition = new Vector3(0f, 0f, 4.5f);
            reticleObj.transform.localScale = Vector3.one * 0.022f;

            Renderer r = reticleObj.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(Shader.Find("Sprites/Default"));
                r.material.color = new Color(0f, 0.92f, 1f, 0.95f); // Bright cyan reticle dot
            }
        }
    }

    private void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        UpdateMouseAndKeyboardControls();
#endif
    }

    private void UpdateMouseAndKeyboardControls()
    {
        if (XRSettings.isDeviceActive || head == null)
            return;

        // Focus lock protection: first click on Game Window only focuses window, no UI click trigger
        if (!Application.isFocused)
        {
            wasFocusedLastFrame = false;
            return;
        }

        if (!wasFocusedLastFrame)
        {
            wasFocusedLastFrame = true;
            return;
        }

        // HARD RESET (Phím R Khẩn Cấp): Reset cả camera, người chơi và mô hình về ban đầu
        if (Input.GetKeyDown(KeyCode.R))
        {
            HardResetAll();
            return;
        }

        // 1. WASD Movement (FPS style)
        float h = 0f;
        float v = 0f;

        if (Input.GetKey(KeyCode.A)) h -= 1f;
        if (Input.GetKey(KeyCode.D)) h += 1f;
        if (Input.GetKey(KeyCode.W)) v += 1f;
        if (Input.GetKey(KeyCode.S)) v -= 1f;

        if (h != 0f || v != 0f)
        {
            Vector3 forward = head.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 right = head.right;
            right.y = 0f;
            right.Normalize();

            Vector3 move = Vector3.ClampMagnitude(right * h + forward * v, 1f) * (2.2f * Time.deltaTime);

            Transform targetTransform = origin != null ? origin.transform : (head.parent != null ? head.parent : head);

            CharacterController cc = targetTransform.GetComponent<CharacterController>();
            if (cc != null && cc.enabled)
            {
                cc.Move(move);
            }
            else
            {
                targetTransform.position += move;
            }
        }

        // 2. Camera Rotation (Right-Click Drag or Arrow Keys)
        float yawDelta = 0f;
        float pitchDelta = 0f;

        if (Input.GetKey(KeyCode.LeftArrow)) yawDelta -= 70f * Time.deltaTime;
        if (Input.GetKey(KeyCode.RightArrow)) yawDelta += 70f * Time.deltaTime;
        if (Input.GetKey(KeyCode.UpArrow)) pitchDelta -= 70f * Time.deltaTime;
        if (Input.GetKey(KeyCode.DownArrow)) pitchDelta += 70f * Time.deltaTime;

        if (Input.GetMouseButton(1))
        {
            yawDelta += Input.GetAxis("Mouse X") * 2.5f;
            pitchDelta -= Input.GetAxis("Mouse Y") * 2.5f;
        }

        if (Mathf.Abs(yawDelta) > 0.001f || Mathf.Abs(pitchDelta) > 0.001f)
        {
            Transform targetBody = origin != null ? origin.transform : (head.parent != null ? head.parent : head);
            targetBody.Rotate(Vector3.up * yawDelta, UnityEngine.Space.World);

            cameraPitch = Mathf.Clamp(cameraPitch + pitchDelta, -80f, 80f);
            Vector3 currentEuler = head.localEulerAngles;
            head.localEulerAngles = new Vector3(cameraPitch, currentEuler.y, currentEuler.z);
        }

        // 3. Model Rotation via Keyboard (Q = Left, E = Right)
        if (Input.GetKey(KeyCode.Q)) RotateModel(-60f * Time.deltaTime);
        if (Input.GetKey(KeyCode.E)) RotateModel(60f * Time.deltaTime);

        // 4. Laptop Grip Fallback (G Key): Press & Hold G to grab and hold 3D model
        if (Input.GetKey(KeyCode.G) && currentModel != null)
        {
            if (!isLaptopGripActive)
            {
                isLaptopGripActive = true;
                laptopGripOffset = head.InverseTransformPoint(currentModel.transform.position);
            }
            currentModel.transform.position = head.TransformPoint(laptopGripOffset);
        }
        else if (isLaptopGripActive && !Input.GetKey(KeyCode.G))
        {
            isLaptopGripActive = false;
        }

        // 5. Direct Mouse Left-Click Raycast on Menu Buttons, Info Panel & 3D Markers
        if (Input.GetMouseButtonDown(0))
        {
            TryClickWorldUI();
        }
    }

    private void RotateModel(float degreesPerSec)
    {
        if (currentModel == null) return;
        XRGrabInteractable grab = currentModel.GetComponent<XRGrabInteractable>();
        if (grab != null && grab.isSelected)
        {
            // CRITICAL PROTECTION: Do NOT rotate if model is currently held by user's hand!
            return;
        }
        currentModel.transform.Rotate(Vector3.up, degreesPerSec, UnityEngine.Space.World);
    }

    private void HardResetAll()
    {
        // 1. Reset 3D Model
        ResetModel();

        // 2. Reset Player Position & Camera Rotation
        if (origin != null)
        {
            origin.transform.position = initialOriginPosition;
        }

        cameraPitch = 0f;
        if (head != null)
        {
            head.localRotation = Quaternion.identity;
        }

        // 3. Place World-Space Menu right in front of camera
        PlaceMenuInFront(true);
        CloseInfoPanel();
        Debug.Log("[VRXRSceneController] Hard Reset performed (R Key): Model, Camera, and Menu reset to origin.");
    }

    private void TryClickWorldUI()
    {
        Camera cam = head != null ? head.GetComponent<Camera>() : Camera.main;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        if (menuRoot != null)
        {
            Canvas canvas = menuRoot.GetComponent<Canvas>();
            if (canvas != null && canvas.worldCamera == null)
                canvas.worldCamera = cam;
        }

        if (infoPanelRoot != null)
        {
            Canvas canvas = infoPanelRoot.GetComponent<Canvas>();
            if (canvas != null && canvas.worldCamera == null)
                canvas.worldCamera = cam;
        }

        // 1. First attempt: Standard UI EventSystem Raycast
        if (EventSystem.current != null)
        {
            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };

            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            foreach (RaycastResult result in results)
            {
                if (result.gameObject != null)
                {
                    Button btn = result.gameObject.GetComponentInParent<Button>();
                    if (btn != null && btn.interactable)
                    {
                        btn.onClick.Invoke();
                        return;
                    }
                }
            }
        }

        // 2. Fallback attempt: Direct 3D Raycast to World-Space UI Buttons
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (menuRoot != null)
        {
            Button[] buttons = menuRoot.GetComponentsInChildren<Button>(true);
            foreach (Button btn in buttons)
            {
                if (btn == null || !btn.interactable || !btn.gameObject.activeInHierarchy)
                    continue;

                RectTransform rectTransform = btn.transform as RectTransform;
                if (rectTransform == null) continue;

                Plane plane = new Plane(rectTransform.forward, rectTransform.position);
                if (plane.Raycast(ray, out float enter) && enter > 0f)
                {
                    Vector3 hitPoint = ray.GetPoint(enter);
                    Vector3 localPoint = rectTransform.InverseTransformPoint(hitPoint);

                    if (rectTransform.rect.Contains(localPoint))
                    {
                        btn.onClick.Invoke();
                        return;
                    }
                }
            }
        }

        if (infoPanelRoot != null && infoPanelRoot.gameObject.activeInHierarchy)
        {
            Button[] buttons = infoPanelRoot.GetComponentsInChildren<Button>(true);
            foreach (Button btn in buttons)
            {
                if (btn == null || !btn.interactable || !btn.gameObject.activeInHierarchy)
                    continue;

                RectTransform rectTransform = btn.transform as RectTransform;
                if (rectTransform == null) continue;

                Plane plane = new Plane(rectTransform.forward, rectTransform.position);
                if (plane.Raycast(ray, out float enter) && enter > 0f)
                {
                    Vector3 hitPoint = ray.GetPoint(enter);
                    Vector3 localPoint = rectTransform.InverseTransformPoint(hitPoint);

                    if (rectTransform.rect.Contains(localPoint))
                    {
                        btn.onClick.Invoke();
                        return;
                    }
                }
            }
        }

        // 3. Fallback attempt: Direct 3D Raycast on 3D Information Markers
        if (Physics.Raycast(ray, out RaycastHit markerHit, 20f))
        {
            VRXRMarkerTrigger trigger = markerHit.collider.GetComponentInParent<VRXRMarkerTrigger>();
            if (trigger != null && trigger.OnMarkerClicked != null)
            {
                trigger.OnMarkerClicked.Invoke();
                return;
            }
        }
    }

    private void LateUpdate()
    {
        UpdateMenuFollow();
        UpdateControllerPositionsInEditor();
        UpdateInfoPanelBillboard();
    }

    private void UpdateInfoPanelBillboard()
    {
        if (infoPanelRoot != null && infoPanelRoot.gameObject.activeSelf && head != null)
        {
            Vector3 lookDir = infoPanelRoot.position - head.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                infoPanelRoot.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }
    }

    private void UpdateControllerPositionsInEditor()
    {
#if UNITY_EDITOR
        bool isRealHeadsetConnected = XRSettings.isDeviceActive &&
                                      !string.IsNullOrEmpty(XRSettings.loadedDeviceName) &&
                                      !XRSettings.loadedDeviceName.Contains("Mock");

        if (isRealHeadsetConnected || head == null)
            return;

        if (leftHandTransform == null || rightHandTransform == null)
        {
            EnsureControllerModels();
        }

        DisableTrackedPoseDrivers(leftHandTransform);
        DisableTrackedPoseDrivers(rightHandTransform);

        if (leftHandTransform != null)
        {
            leftHandTransform.position = head.TransformPoint(new Vector3(-0.24f, -0.20f, 0.48f));
            leftHandTransform.rotation = head.rotation * Quaternion.Euler(15f, -10f, 0f);
        }

        if (rightHandTransform != null)
        {
            rightHandTransform.position = head.TransformPoint(new Vector3(0.24f, -0.20f, 0.48f));
            rightHandTransform.rotation = head.rotation * Quaternion.Euler(15f, 10f, 0f);
        }
#endif
    }

    private static void DisableTrackedPoseDrivers(Transform target)
    {
        if (target == null) return;
        MonoBehaviour[] scripts = target.GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour script in scripts)
        {
            if (script == null) continue;
            string typeName = script.GetType().Name;
            if (typeName.Contains("TrackedPoseDriver") || typeName.Contains("ActionBasedController"))
            {
                script.enabled = false;
            }
        }
    }

    private void OnDestroy()
    {
        if (catalog != null)
        {
            catalog.CatalogReady -= RefreshMenu;
            catalog.ModelChanged -= HandleModelChanged;
            catalog.LoadingStateChanged -= HandleLoadingChanged;
            catalog.VisibilityChanged -= HandleVisibilityChanged;
            catalog.AutoRotateChanged -= HandleAutoRotateChanged;
        }

        if (detailService != null)
        {
            detailService.OnModelPartsLoaded -= HandleModelPartsLoaded;
        }

        AppLanguageManager.LanguageChanged -= HandleLanguageChanged;
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
            FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);

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

        if (FindAnyObjectByType<XRInteractionSimulator>(FindObjectsInactive.Include) != null)
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
        if (detailAnchorController != null && model != null)
            detailAnchorController.SetModelRoot(model.transform);

        // Load the structure (model_parts) of the model that is now shown.
        // The record passed by VRRuntimeModelCatalog.ModelChanged already holds
        // the asset id, lesson id and file name of this model.
        if (detailService != null && record != null)
        {
            if (System.Guid.TryParse(record.asset_id, out _))
                detailService.LoadModelParts(record.asset_id);
            else if (!string.IsNullOrWhiteSpace(record.lesson_id))
                detailService.ResolveModelAssetForLessonAndFile(record.lesson_id, record.file_name);
        }

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

        RectTransform rotateRow = CreateRow(canvasRect);
        CreateButton(rotateRow, T("↺ Rotate Left", "↺ Xoay trái"), () => RotateModel(-45f), out _);
        CreateButton(rotateRow, T("↻ Rotate Right", "↻ Xoay phải"), () => RotateModel(45f), out _);

        CreateButton(canvasRect, string.Empty, () => catalog?.ToggleVisibility(), out visibilityButtonText);
        CreateButton(canvasRect, string.Empty, () => catalog?.ToggleAutoRotate(), out rotateButtonText);
        CreateButton(canvasRect, string.Empty, ToggleAnnotations, out annotationButtonText);
        CreateButton(canvasRect, T("Reset model", "Đặt lại mô hình"), ResetModel, out _);
        CreateButton(canvasRect, T("Back to lesson", "Quay lại bài học"), GoBack, out _,
            new Color32(214, 60, 60, 255));

        CreateText(
            canvasRect,
            T("Grip: grab model. Q/E / Buttons: rotate.",
              "Grip: cầm mô hình. Q/E / Nút bấm: xoay."),
            20, FontStyle.Italic, new Color32(110, 124, 150, 255), 30f);

        PlaceMenuInFront(true);
    }

    private void BuildInfoPanel()
    {
        if (infoPanelRoot != null) return;

        GameObject canvasObject = new GameObject("XR Info Panel", typeof(RectTransform));
        infoPanelRoot = canvasObject.transform;

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = head != null ? head.GetComponent<Camera>() : Camera.main;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 5f;
        scaler.referencePixelsPerUnit = 100f;

        canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        RectTransform canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = new Vector2(760f, 840f);
        canvasObject.transform.localScale = Vector3.one * 0.0007f;

        Image background = canvasObject.AddComponent<Image>();
        background.color = new Color32(255, 255, 255, 255);

        VerticalLayoutGroup layout = canvasObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(30, 30, 26, 26);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Header Row (Title + Small Red 44x44 Close Button at Top Right)
        GameObject headerGo = new GameObject("HeaderRow", typeof(RectTransform));
        headerGo.transform.SetParent(canvasRect, false);
        HorizontalLayoutGroup headerLayout = headerGo.AddComponent<HorizontalLayoutGroup>();
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;

        GameObject titleGo = new GameObject("TitleText", typeof(RectTransform));
        titleGo.transform.SetParent(headerGo.transform, false);
        infoTitleText = titleGo.AddComponent<TextMeshProUGUI>();
        infoTitleText.text = "Detail Info";
        infoTitleText.fontSize = 28f;
        infoTitleText.fontStyle = FontStyles.Bold;
        infoTitleText.color = new Color32(15, 23, 42, 255);
        infoTitleText.alignment = TextAlignmentOptions.MidlineLeft;
        LayoutElement titleLayout = titleGo.AddComponent<LayoutElement>();
        titleLayout.flexibleWidth = 1f;
        titleLayout.preferredHeight = 44f;

        GameObject closeGo = new GameObject("CloseButton", typeof(RectTransform));
        closeGo.transform.SetParent(headerGo.transform, false);
        Image closeImg = closeGo.AddComponent<Image>();
        closeImg.color = new Color32(239, 68, 68, 255);
        Button closeBtn = closeGo.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(CloseInfoPanel);

        LayoutElement closeLayout = closeGo.AddComponent<LayoutElement>();
        closeLayout.preferredWidth = 44f;
        closeLayout.preferredHeight = 44f;
        closeLayout.flexibleWidth = 0f;
        closeLayout.flexibleHeight = 0f;

        GameObject closeLabelGo = new GameObject("Label", typeof(RectTransform));
        closeLabelGo.transform.SetParent(closeGo.transform, false);
        RectTransform closeLabelRect = (RectTransform)closeLabelGo.transform;
        closeLabelRect.anchorMin = Vector2.zero;
        closeLabelRect.anchorMax = Vector2.one;
        closeLabelRect.sizeDelta = Vector2.zero;

        TextMeshProUGUI closeLabelText = closeLabelGo.AddComponent<TextMeshProUGUI>();
        closeLabelText.text = "×";
        closeLabelText.fontSize = 32f;
        closeLabelText.fontStyle = FontStyles.Bold;
        closeLabelText.color = Color.white;
        closeLabelText.alignment = TextAlignmentOptions.Center;
        closeLabelText.raycastTarget = false;

        // AI Confidence Tag
        infoConfidenceText = CreateTMPText(canvasRect, string.Empty, 18f, FontStyles.Italic, new Color32(2, 132, 199, 255), 24f);

        // Description Section
        infoDescHeader = CreateTMPText(canvasRect, T("Description:", "Mô tả:"), 22f, FontStyles.Bold, new Color32(15, 23, 42, 255), 28f);
        infoDescText = CreateTMPText(canvasRect, string.Empty, 19f, FontStyles.Normal, new Color32(51, 65, 85, 255), 110f);

        // Structure Section
        infoStructHeader = CreateTMPText(canvasRect, T("Structure:", "Cấu tạo:"), 22f, FontStyles.Bold, new Color32(15, 23, 42, 255), 28f);
        infoStructText = CreateTMPText(canvasRect, string.Empty, 19f, FontStyles.Normal, new Color32(51, 65, 85, 255), 90f);

        // Function Section
        infoFuncHeader = CreateTMPText(canvasRect, T("Function:", "Chức năng:"), 22f, FontStyles.Bold, new Color32(15, 23, 42, 255), 28f);
        infoFuncText = CreateTMPText(canvasRect, string.Empty, 19f, FontStyles.Normal, new Color32(51, 65, 85, 255), 90f);

        infoPanelRoot.gameObject.SetActive(false);
    }

    private TextMeshProUGUI CreateTMPText(RectTransform parent, string text, float fontSize, FontStyles style, Color color, float preferredHeight)
    {
        GameObject go = new GameObject("TMPText", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Truncate;
        tmp.raycastTarget = false;

        LayoutElement element = go.AddComponent<LayoutElement>();
        element.preferredHeight = preferredHeight;
        return tmp;
    }

    public void OpenInfoPanelForPart(VRModelDetailService.ModelPartData part)
    {
        ShowInfoPanel(part);
    }

    private void ShowInfoPanel(VRModelDetailService.ModelPartData part)
    {
        if (part == null || infoPanelRoot == null) return;
        activePartData = part;

        bool isVi = AppLanguageManager.IsVietnamese;

        string name = isVi ? (!string.IsNullOrWhiteSpace(part.part_name_vi) ? part.part_name_vi : part.part_name)
                           : (!string.IsNullOrWhiteSpace(part.part_name) ? part.part_name : part.part_name_vi);

        if (string.IsNullOrWhiteSpace(name))
            name = isVi ? "Bộ phận mô hình" : "Model Part";

        if (infoTitleText != null) infoTitleText.text = name;

        if (infoConfidenceText != null)
        {
            float? conf = part.ai_confidence.HasValue ? part.ai_confidence : (part.anchor_confidence > 0f ? part.anchor_confidence : (float?)null);
            if (conf.HasValue)
                infoConfidenceText.text = (isVi ? "Độ tin cậy AI: " : "AI confidence: ") +
                                          Mathf.RoundToInt(Mathf.Clamp01(conf.Value) * 100f) + "%";
            else
                infoConfidenceText.text = string.Empty;
        }

        string desc = GetLocalizedField(part.description, part.description_vi);
        string structDesc = GetLocalizedField(part.structure_description, part.structure_description_vi);
        string funcDesc = GetLocalizedField(part.function_description, part.function_description_vi);

        if (infoDescText != null)
            infoDescText.text = !string.IsNullOrWhiteSpace(desc) ? desc : (isVi ? "Chưa có mô tả cho cấu trúc này." : "No description available.");

        bool hasStruct = !string.IsNullOrWhiteSpace(structDesc);
        if (infoStructHeader != null) infoStructHeader.gameObject.SetActive(hasStruct);
        if (infoStructText != null)
        {
            infoStructText.gameObject.SetActive(hasStruct);
            if (hasStruct) infoStructText.text = structDesc.Trim();
        }

        bool hasFunc = !string.IsNullOrWhiteSpace(funcDesc);
        if (infoFuncHeader != null) infoFuncHeader.gameObject.SetActive(hasFunc);
        if (infoFuncText != null)
        {
            infoFuncText.gameObject.SetActive(hasFunc);
            if (hasFunc) infoFuncText.text = funcDesc.Trim();
        }

        if (head != null)
        {
            Vector3 targetPos = head.position + head.forward * 0.9f + head.right * 0.35f + Vector3.up * 0.05f;
            infoPanelRoot.position = targetPos;
            infoPanelRoot.rotation = Quaternion.LookRotation(infoPanelRoot.position - head.position, Vector3.up);
        }

        infoPanelRoot.gameObject.SetActive(true);
    }

    private static string GetLocalizedField(string en, string vi)
    {
        bool isVi = AppLanguageManager.IsVietnamese;
        if (isVi)
            return !string.IsNullOrWhiteSpace(vi) ? vi : (!string.IsNullOrWhiteSpace(en) ? en : string.Empty);
        else
            return !string.IsNullOrWhiteSpace(en) ? en : (!string.IsNullOrWhiteSpace(vi) ? vi : string.Empty);
    }

    private void CloseInfoPanel()
    {
        if (infoPanelRoot != null)
            infoPanelRoot.gameObject.SetActive(false);
    }

    private void HandleModelPartsLoaded(List<VRModelDetailService.ModelPartData> parts)
    {
        ClearMarkers();
        if (parts == null || parts.Count == 0 || detailAnchorController == null || currentModel == null)
            return;

        foreach (var part in parts)
        {
            if (part == null || !part.is_active) continue;
            Vector3 worldPos = detailAnchorController.GetPartWorldPosition(part);
            if (worldPos == Vector3.zero)
                worldPos = currentModel.transform.position;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "VRInfoMarker_" + part.part_key;
            marker.transform.position = worldPos;

            marker.transform.SetParent(currentModel.transform, true);
            marker.transform.localScale = Vector3.one * 0.04f;

            Renderer r = marker.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(Shader.Find("Sprites/Default"));
                r.material.color = new Color(1f, 0.84f, 0f, 0.95f);
            }

            SphereCollider col = marker.GetComponent<SphereCollider>();
            if (col != null)
            {
                col.isTrigger = true;
                col.radius = 1.35f; // Generous 2.7x trigger hitbox for easy editor mouse clicking
            }

            VRXRMarkerTrigger trigger = marker.AddComponent<VRXRMarkerTrigger>();
            VRModelDetailService.ModelPartData partRef = part;
            trigger.OnMarkerClicked = () => ShowInfoPanel(partRef);

            XRSimpleInteractable interactable = marker.AddComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(_ => ShowInfoPanel(partRef));

            interactable.hoverEntered.AddListener(_ => OnMarkerHoverEnter(marker, r));
            interactable.hoverExited.AddListener(_ => OnMarkerHoverExit(marker, r));

            SetLayerRecursively(marker, 0);
            activeMarkerObjects.Add(marker);
        }

        if (annotationController != null)
        {
            annotationController.SetModelAndParts(currentModel.transform, parts, activeMarkerObjects);
        }
    }

    private static void OnMarkerHoverEnter(GameObject marker, Renderer renderer)
    {
        if (marker == null) return;
        marker.transform.localScale = Vector3.one * 0.055f;
        if (renderer != null && renderer.material != null)
            renderer.material.color = new Color(0f, 0.92f, 1f, 1f);
    }

    private static void OnMarkerHoverExit(GameObject marker, Renderer renderer)
    {
        if (marker == null) return;
        marker.transform.localScale = Vector3.one * 0.04f;
        if (renderer != null && renderer.material != null)
            renderer.material.color = new Color(1f, 0.84f, 0f, 0.95f);
    }

    private void ClearMarkers()
    {
        if (annotationController != null)
        {
            annotationController.Clear();
        }

        foreach (var marker in activeMarkerObjects)
        {
            if (marker != null)
                Destroy(marker);
        }
        activeMarkerObjects.Clear();
    }

    private void HandleLanguageChanged(string lang)
    {
        RefreshMenu();
        if (activePartData != null && infoPanelRoot != null && infoPanelRoot.gameObject.activeSelf)
        {
            ShowInfoPanel(activePartData);
        }
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

        if (annotationButtonText != null && annotationController != null)
        {
            annotationButtonText.text = annotationController.IsVisible
                ? T("⚛️ Hide Labels", "⚛️ Ẩn nhãn cấu tạo")
                : T("⚛️ Structural Labels", "⚛️ Nhãn Cấu Tạo");
        }
    }

    private void ToggleAnnotations()
    {
        if (annotationController != null)
        {
            annotationController.ToggleAnnotations();
            RefreshMenu();
        }
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

public class VRXRMarkerTrigger : MonoBehaviour
{
    public System.Action OnMarkerClicked;

    private void OnMouseDown()
    {
        OnMarkerClicked?.Invoke();
    }
}
