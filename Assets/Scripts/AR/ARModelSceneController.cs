using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Legacy AR plane-placement controller.
///
/// IMPORTANT:
/// Project hiện có ARHeartPlacementController mới dùng selected_lesson_models_json.
/// Nếu controller mới cùng tồn tại trong ARScene, class này tự dừng để tránh:
/// - download cùng model 2 lần;
/// - hai loader cùng đọc selected_model_url;
/// - RuntimeGlbLoader cũ làm thay đổi R2 presigned URL;
/// - hai hệ thống cùng điều khiển model/gesture.
///
/// Nếu scene chỉ dùng pipeline legacy này thì controller vẫn hoạt động bình thường.
/// </summary>
public class ARModelSceneController : MonoBehaviour
{
    private static readonly List<ARRaycastHit> RaycastHits = new();

    [Header("AR Components")]
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private Camera arCamera;

    [Header("Model")]
    [SerializeField] private RuntimeGlbLoader modelLoader;
    [SerializeField] private Transform modelRoot;

    [Header("Placement indicator")]
    [SerializeField] private GameObject placementIndicator;

    [Header("Settings")]
    [SerializeField] private bool placeOnHorizontalPlanes = true;
    [SerializeField] private bool placeOnVerticalPlanes = true;
    [SerializeField] private bool hidePlanesAfterPlacement = true;

    [Header("Compatibility")]
    [Tooltip(
        "Bật để tự tắt controller legacy này nếu ARHeartPlacementController mới " +
        "đang tồn tại trong ARScene.")]
    [SerializeField] private bool disableWhenRuntimeLessonControllerExists = true;

    private Pose currentPlacementPose;
    private bool placementPoseIsValid;
    private bool modelPlaced;
    private bool modelLoaded;
    private bool compatibilityDisabled;

    public bool IsModelPlaced => modelPlaced;
    public Transform ModelRoot => modelRoot;
    public bool IsCompatibilityDisabled => compatibilityDisabled;

    private void Awake()
    {
        if (disableWhenRuntimeLessonControllerExists &&
            HasNewRuntimeLessonController())
        {
            compatibilityDisabled = true;

            Debug.LogWarning(
                "[ARModelSceneController] ARHeartPlacementController was found. " +
                "Legacy ARModelSceneController is disabled to prevent duplicate model loading.");

            // Không gọi enabled=false vì gesture/UI cũ có thể query public state.
            // Các lifecycle/update bên dưới sẽ return ngay.
        }
    }

    private async void Start()
    {
        if (compatibilityDisabled)
            return;

        Debug.Log("========== LEGACY AR START ==========");

        bool referencesValid = ValidateReferences();
        if (!referencesValid)
        {
            Debug.LogError(
                "[ARModelSceneController] Missing required references. " +
                "Legacy AR startup aborted.");
            return;
        }

        ConfigurePlaneDetection();

        string modelUrl =
            PlayerPrefs.GetString(
                "selected_model_url",
                string.Empty);

        string modelName =
            PlayerPrefs.GetString(
                "selected_model_name",
                "Unknown");

        Debug.Log("[AR] Model Name = " + modelName);

        // Không log toàn bộ signed URL theo mặc định vì nó chứa temporary credentials.
        Debug.Log(
            "[AR] Model URL exists = " +
            (!string.IsNullOrWhiteSpace(modelUrl)));

        if (string.IsNullOrWhiteSpace(modelUrl))
        {
            Debug.LogError(
                "[AR] selected_model_url is EMPTY.\n" +
                "Open ARScene from ShowLessonScene so the selected model URL is prepared first.");
            return;
        }

        if (modelLoader == null)
        {
            Debug.LogError("[AR] RuntimeGlbLoader is missing.");
            return;
        }

        Debug.Log("[AR] Loading model through RuntimeGlbLoader...");

        GameObject model =
            await modelLoader.LoadModelAsync(modelUrl);

        if (this == null || compatibilityDisabled)
            return;

        if (model == null)
        {
            Debug.LogError(
                "[AR] modelLoader returned NULL. " +
                "Check RuntimeGlbLoader logs above for the exact cause.");
            return;
        }

        modelLoaded = true;

        Debug.Log(
            "[AR] Model loaded. Waiting for a valid AR plane before placement.");
    }

    private void Update()
    {
        if (compatibilityDisabled)
            return;

        if (!ReferencesReadyForPlacement())
            return;

        UpdatePlacementPose();
        UpdatePlacementIndicator();

        if (!modelLoaded)
            return;

        if (modelPlaced)
            return;

        if (Input.touchCount == 0)
            return;

        Touch touch = Input.GetTouch(0);

        if (touch.phase != TouchPhase.Began)
            return;

        if (IsTouchOverUI(touch.fingerId))
            return;

        if (placementPoseIsValid)
            PlaceModel();
    }

    private void ConfigurePlaneDetection()
    {
        if (planeManager == null)
            return;

        PlaneDetectionMode mode = PlaneDetectionMode.None;

        if (placeOnHorizontalPlanes)
            mode |= PlaneDetectionMode.Horizontal;

        if (placeOnVerticalPlanes)
            mode |= PlaneDetectionMode.Vertical;

        planeManager.requestedDetectionMode = mode;
        planeManager.enabled = true;
    }

    private void UpdatePlacementPose()
    {
        placementPoseIsValid = false;

        if (raycastManager == null ||
            planeManager == null ||
            arCamera == null)
        {
            return;
        }

        Vector2 screenCenter = new Vector2(
            Screen.width * 0.5f,
            Screen.height * 0.5f);

        placementPoseIsValid =
            raycastManager.Raycast(
                screenCenter,
                RaycastHits,
                TrackableType.PlaneWithinPolygon);

        if (!placementPoseIsValid ||
            RaycastHits.Count == 0)
        {
            return;
        }

        ARRaycastHit hit = RaycastHits[0];

        currentPlacementPose = hit.pose;

        ARPlane hitPlane =
            planeManager.GetPlane(hit.trackableId);

        if (hitPlane == null)
        {
            placementPoseIsValid = false;
            return;
        }

        bool horizontal =
            hitPlane.alignment == PlaneAlignment.HorizontalUp ||
            hitPlane.alignment == PlaneAlignment.HorizontalDown;

        bool vertical =
            hitPlane.alignment == PlaneAlignment.Vertical;

        placementPoseIsValid =
            (horizontal && placeOnHorizontalPlanes) ||
            (vertical && placeOnVerticalPlanes);

        if (!placementPoseIsValid)
            return;

        // Với mặt phẳng ngang, xoay model để hướng về camera.
        if (horizontal)
        {
            Vector3 cameraForward =
                arCamera.transform.forward;

            Vector3 cameraBearing = new Vector3(
                cameraForward.x,
                0f,
                cameraForward.z);

            if (cameraBearing.sqrMagnitude > 0.001f)
            {
                cameraBearing.Normalize();

                currentPlacementPose.rotation =
                    Quaternion.LookRotation(cameraBearing);
            }
        }
    }

    private void UpdatePlacementIndicator()
    {
        if (placementIndicator == null)
            return;

        bool showIndicator =
            !compatibilityDisabled &&
            !modelPlaced &&
            placementPoseIsValid &&
            modelLoaded;

        placementIndicator.SetActive(showIndicator);

        if (!showIndicator)
            return;

        placementIndicator.transform.SetPositionAndRotation(
            currentPlacementPose.position,
            currentPlacementPose.rotation);
    }

    private void PlaceModel()
    {
        if (modelRoot == null ||
            modelLoader == null ||
            modelLoader.LoadedModel == null)
        {
            return;
        }

        modelRoot.SetPositionAndRotation(
            currentPlacementPose.position,
            currentPlacementPose.rotation);

        modelLoader.LoadedModel.SetActive(true);

        modelPlaced = true;

        if (placementIndicator != null)
            placementIndicator.SetActive(false);

        if (hidePlanesAfterPlacement)
            SetPlaneVisualization(false);

        Debug.Log(
            "[ARModelSceneController] Legacy model placed.");
    }

    public void ResetPlacement()
    {
        if (compatibilityDisabled)
            return;

        modelPlaced = false;

        if (modelLoader != null &&
            modelLoader.LoadedModel != null)
        {
            modelLoader.LoadedModel.SetActive(false);
        }

        SetPlaneVisualization(true);
    }

    public void SetPlaneVisualization(bool visible)
    {
        if (planeManager == null)
            return;

        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane != null)
                plane.gameObject.SetActive(visible);
        }

        planeManager.enabled = visible;

        if (visible)
            ConfigurePlaneDetection();
    }

    public bool TryMoveModel(Vector2 screenPosition)
    {
        if (compatibilityDisabled ||
            !modelPlaced ||
            raycastManager == null ||
            modelRoot == null)
        {
            return false;
        }

        bool hitFound =
            raycastManager.Raycast(
                screenPosition,
                RaycastHits,
                TrackableType.PlaneWithinPolygon);

        if (!hitFound || RaycastHits.Count == 0)
            return false;

        Pose pose = RaycastHits[0].pose;

        modelRoot.position = pose.position;

        return true;
    }

    private bool IsTouchOverUI(int fingerId)
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject(
                   fingerId);
    }

    private bool ValidateReferences()
    {
        bool valid = true;

        if (raycastManager == null)
        {
            Debug.LogError(
                "[ARModelSceneController] ARRaycastManager is missing.");
            valid = false;
        }

        if (planeManager == null)
        {
            Debug.LogError(
                "[ARModelSceneController] ARPlaneManager is missing.");
            valid = false;
        }

        if (arCamera == null)
        {
            Debug.LogError(
                "[ARModelSceneController] AR Camera is missing.");
            valid = false;
        }

        if (modelLoader == null)
        {
            Debug.LogError(
                "[ARModelSceneController] RuntimeGlbLoader is missing.");
            valid = false;
        }

        if (modelRoot == null)
        {
            Debug.LogError(
                "[ARModelSceneController] ModelRoot is missing.");
            valid = false;
        }

        return valid;
    }

    private bool ReferencesReadyForPlacement()
    {
        return raycastManager != null &&
               planeManager != null &&
               arCamera != null &&
               modelLoader != null &&
               modelRoot != null;
    }

    /// <summary>
    /// Tránh compile dependency trực tiếp vào namespace/class của pipeline mới.
    /// Chỉ cần tìm MonoBehaviour có type name ARHeartPlacementController.
    /// </summary>
    private static bool HasNewRuntimeLessonController()
    {
        MonoBehaviour[] behaviours;

#if UNITY_2023_1_OR_NEWER
        behaviours =
            FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
        behaviours =
            FindObjectsOfType<MonoBehaviour>(true);
#endif

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
                continue;

            Type type = behaviour.GetType();

            if (string.Equals(
                    type.Name,
                    "ARHeartPlacementController",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
