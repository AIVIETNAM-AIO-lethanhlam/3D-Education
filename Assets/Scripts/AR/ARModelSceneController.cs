using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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

    private Pose currentPlacementPose;
    private bool placementPoseIsValid;
    private bool modelPlaced;
    private bool modelLoaded;

    public bool IsModelPlaced => modelPlaced;
    public Transform ModelRoot => modelRoot;

    private async void Start()
    {
        Debug.Log("========== AR START ==========");

        ValidateReferences();

        Debug.Log("[AR] ValidateReferences finished");

        ConfigurePlaneDetection();

        Debug.Log("[AR] ConfigurePlaneDetection finished");

        string modelUrl =
            PlayerPrefs.GetString(
                "selected_model_url",
                string.Empty);

        string modelName =
            PlayerPrefs.GetString(
                "selected_model_name",
                "Unknown");

        Debug.Log("[AR] Model Name = " + modelName);
        Debug.Log("[AR] Model URL = " + modelUrl);

        if (string.IsNullOrWhiteSpace(modelUrl))
        {
            Debug.LogError("[AR] selected_model_url is EMPTY");
            return;
        }

        Debug.Log("[AR] Loading model...");

        GameObject model =
            await modelLoader.LoadModelAsync(modelUrl);

        if (model == null)
        {
            Debug.LogError("[AR] modelLoader returned NULL");
            return;
        }

        Debug.Log("[AR] Model loaded");

        modelLoaded = true;

        Debug.Log("[AR] modelLoaded = TRUE");
    }

    private void Update()
    {
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

        Debug.Log("[AR] Touch detected");

        if (IsTouchOverUI(touch.fingerId))
        {
            Debug.Log("[AR] Touch on UI");
            return;
        }

        Debug.Log("[AR] placementPoseIsValid = " + placementPoseIsValid);

        if (placementPoseIsValid)
        {
            Debug.Log("[AR] Calling PlaceModel()");
            PlaceModel();
        }
    }

    private void ConfigurePlaneDetection()
    {
        PlaneDetectionMode mode = PlaneDetectionMode.None;

        if (placeOnHorizontalPlanes)
        {
            mode |= PlaneDetectionMode.Horizontal;
        }

        if (placeOnVerticalPlanes)
        {
            mode |= PlaneDetectionMode.Vertical;
        }

        planeManager.requestedDetectionMode = mode;
    }

    private void UpdatePlacementPose()
    {
        Vector2 screenCenter = new Vector2(
            Screen.width * 0.5f,
            Screen.height * 0.5f
        );

        TrackableType trackableTypes =
            TrackableType.PlaneWithinPolygon;

        placementPoseIsValid = raycastManager.Raycast(
            screenCenter,
            RaycastHits,
            trackableTypes
        );

        Debug.Log(
            "[AR] Raycast = " +
            placementPoseIsValid +
            " Hits = " +
            RaycastHits.Count);

        if (!placementPoseIsValid)
        {
            return;
        }

        ARRaycastHit hit = RaycastHits[0];

        currentPlacementPose = hit.pose;

        ARPlane hitPlane = planeManager.GetPlane(hit.trackableId);

        if (hitPlane != null)
        {
            Debug.Log(
                "[AR] Plane Alignment = " +
                hitPlane.alignment);
        }

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
        {
            return;
        }

        // On horizontal surfaces, make the model face the camera.
        if (horizontal)
        {
            Vector3 cameraForward = arCamera.transform.forward;
            Vector3 cameraBearing = new Vector3(
                cameraForward.x,
                0f,
                cameraForward.z
            ).normalized;

            if (cameraBearing.sqrMagnitude > 0.001f)
            {
                currentPlacementPose.rotation =
                    Quaternion.LookRotation(cameraBearing);
            }
        }
    }

    private void UpdatePlacementIndicator()
    {
        if (placementIndicator == null)
        {
            return;
        }

        bool showIndicator =
            !modelPlaced &&
            placementPoseIsValid &&
            modelLoaded;

        placementIndicator.SetActive(showIndicator);

        if (!showIndicator)
        {
            return;
        }

        placementIndicator.transform.SetPositionAndRotation(
            currentPlacementPose.position,
            currentPlacementPose.rotation
        );
    }

    private void PlaceModel()
    {
        modelRoot.SetPositionAndRotation(
            currentPlacementPose.position,
            currentPlacementPose.rotation
        );

        GameObject loadedModel = modelLoader.LoadedModel;

        if (loadedModel != null)
        {
            loadedModel.SetActive(true);
        }

        modelPlaced = true;

        if (placementIndicator != null)
        {
            placementIndicator.SetActive(false);
        }

        if (hidePlanesAfterPlacement)
        {
            SetPlaneVisualization(false);
        }

        Debug.Log("[ARModelSceneController] Model placed.");
    }

    public void ResetPlacement()
    {
        modelPlaced = false;

        if (modelLoader.LoadedModel != null)
        {
            modelLoader.LoadedModel.SetActive(false);
        }

        SetPlaneVisualization(true);
    }

    public void SetPlaneVisualization(bool visible)
    {
        foreach (ARPlane plane in planeManager.trackables)
        {
            plane.gameObject.SetActive(visible);
        }

        planeManager.enabled = visible;
    }

    public bool TryMoveModel(Vector2 screenPosition)
    {
        if (!modelPlaced)
        {
            return false;
        }

        bool hitFound = raycastManager.Raycast(
            screenPosition,
            RaycastHits,
            TrackableType.PlaneWithinPolygon
        );

        if (!hitFound)
        {
            return false;
        }

        Pose pose = RaycastHits[0].pose;

        modelRoot.position = pose.position;

        return true;
    }

    private bool IsTouchOverUI(int fingerId)
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject(fingerId);
    }

    private void ValidateReferences()
    {
        if (raycastManager == null)
        {
            Debug.LogError("ARRaycastManager is missing.");
        }

        if (planeManager == null)
        {
            Debug.LogError("ARPlaneManager is missing.");
        }

        if (arCamera == null)
        {
            Debug.LogError("AR Camera is missing.");
        }

        if (modelLoader == null)
        {
            Debug.LogError("RuntimeGlbLoader is missing.");
        }

        if (modelRoot == null)
        {
            Debug.LogError("ModelRoot is missing.");
        }

        Debug.Log(
            "[AR] References:" +
            "\nRaycastManager = " + (raycastManager != null) +
            "\nPlaneManager = " + (planeManager != null) +
            "\nCamera = " + (arCamera != null) +
            "\nModelLoader = " + (modelLoader != null) +
            "\nModelRoot = " + (modelRoot != null));
    }
}