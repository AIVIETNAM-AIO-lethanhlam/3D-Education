using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

/// <summary>
/// Makes a runtime-loaded lesson GLB interactive in VRClassroomScene.
///
/// Unity Editor / desktop test:
/// - Left mouse drag on model: grab/move.
/// - Right mouse drag on model: rotate.
/// - Mouse wheel while pointing at model: scale.
/// - R: reset to teacher-desk pose.
///
/// XR headset/controllers:
/// - Hold either controller Grip near the model: grab and move it.
/// - Hold both controller Grips near the model: two-hand move + scale.
///   The two-hand distance controls the model scale.
///
/// This uses UnityEngine.XR device tracking directly, so the basic grab/scale
/// path does not require XR Interaction Toolkit compile-time references.
/// </summary>
public class VRModelInteractionController : MonoBehaviour
{
    [Header("Desktop / Unity Editor test")]
    [SerializeField] private bool enableMouseTest = true;

    [SerializeField, Range(0.2f, 3f)]
    private float mouseGrabDistanceMultiplier = 1f;

    [SerializeField, Range(20f, 240f)]
    private float mouseRotateSpeed = 95f;

    [SerializeField, Range(0.01f, 0.30f)]
    private float mouseWheelScaleStep = 0.08f;

    [Header("XR controller interaction")]
    [SerializeField] private bool enableXRControllerInteraction = true;

    [Tooltip("Controller Grip can begin a grab when it is within this distance of the model bounds.")]
    [SerializeField, Range(0.1f, 1.5f)]
    private float xrGrabDistance = 0.55f;

    [Tooltip("Smooth movement while held. Higher values follow the controller more tightly.")]
    [SerializeField, Range(5f, 40f)]
    private float xrFollowSpeed = 22f;

    [Header("Scale limits")]
    [SerializeField, Range(0.1f, 1f)]
    private float minimumScaleMultiplier = 0.35f;

    [SerializeField, Range(1f, 6f)]
    private float maximumScaleMultiplier = 3f;

    private GameObject modelRoot;
    private Transform deskAnchor;

    private Camera mainCamera;
    private Collider interactionCollider;
    private Rigidbody body;

    private Vector3 defaultLocalScale;
    private Vector3 resetPosition;
    private Quaternion resetRotation;
    private Vector3 resetScale;

    // Desktop grab state
    private bool mouseGrabbed;
    private float mouseGrabDistance;
    private Vector3 mouseGrabOffset;

    // XR state
    private InputDevice leftDevice;
    private InputDevice rightDevice;

    private bool previousLeftGrip;
    private bool previousRightGrip;

    private XRNode? grabbedHand;
    private Vector3 oneHandPositionOffset;
    private Quaternion oneHandRotationOffset;

    private bool twoHandActive;
    private float twoHandStartDistance;
    private Vector3 twoHandStartScale;
    private Vector3 twoHandStartModelPosition;
    private Vector3 twoHandStartMidpoint;
    private Quaternion twoHandStartRotation;
    private Vector3 twoHandStartDirection;

    public void Initialize(
        GameObject target,
        Transform teacherDeskAnchor)
    {
        modelRoot =
            target != null
                ? target
                : gameObject;

        deskAnchor =
            teacherDeskAnchor;

        mainCamera =
            Camera.main;

        EnsurePhysicsAndCollider();

        defaultLocalScale =
            modelRoot.transform.localScale;

        SaveResetPose();
    }

    private void Awake()
    {
        if (modelRoot == null)
            modelRoot = gameObject;

        mainCamera =
            Camera.main;
    }

    private void Start()
    {
        EnsurePhysicsAndCollider();

        if (defaultLocalScale ==
            Vector3.zero)
        {
            defaultLocalScale =
                modelRoot.transform.localScale;
        }

        SaveResetPose();
    }

    private void Update()
    {
        if (modelRoot == null)
            return;

        if (enableMouseTest)
            UpdateDesktopInteraction();

        if (enableXRControllerInteraction)
            UpdateXRInteraction();
    }

    private void EnsurePhysicsAndCollider()
    {
        if (modelRoot == null)
            return;

        body =
            modelRoot.GetComponent<Rigidbody>();

        if (body == null)
            body = modelRoot.AddComponent<Rigidbody>();

        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation =
            RigidbodyInterpolation.Interpolate;

        interactionCollider =
            modelRoot.GetComponent<Collider>();

        if (interactionCollider == null)
        {
            BoxCollider box =
                modelRoot.AddComponent<BoxCollider>();

            FitBoxColliderToRenderers(
                modelRoot,
                box);

            interactionCollider = box;
        }
    }

    private static void FitBoxColliderToRenderers(
        GameObject target,
        BoxCollider box)
    {
        if (target == null ||
            box == null)
        {
            return;
        }

        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);

        if (renderers == null ||
            renderers.Length == 0)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one;
            return;
        }

        bool found = false;
        Bounds worldBounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (!found)
            {
                worldBounds = renderer.bounds;
                found = true;
            }
            else
            {
                worldBounds.Encapsulate(
                    renderer.bounds);
            }
        }

        if (!found)
        {
            box.center = Vector3.zero;
            box.size = Vector3.one;
            return;
        }

        Transform root =
            target.transform;

        Vector3 localCenter =
            root.InverseTransformPoint(
                worldBounds.center);

        Vector3 localSize =
            new Vector3(
                SafeDivide(
                    worldBounds.size.x,
                    Mathf.Abs(root.lossyScale.x)),
                SafeDivide(
                    worldBounds.size.y,
                    Mathf.Abs(root.lossyScale.y)),
                SafeDivide(
                    worldBounds.size.z,
                    Mathf.Abs(root.lossyScale.z)));

        box.center = localCenter;
        box.size =
            new Vector3(
                Mathf.Max(0.05f, localSize.x),
                Mathf.Max(0.05f, localSize.y),
                Mathf.Max(0.05f, localSize.z));
    }

    private static float SafeDivide(
        float value,
        float divisor)
    {
        if (divisor < 0.0001f)
            return value;

        return value / divisor;
    }

    // =========================================================
    // Desktop / Unity Editor test
    // =========================================================

    private void UpdateDesktopInteraction()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
            return;

        // Do not manipulate the 3D model while clicking UI Toolkit/uGUI controls.
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            if (Input.GetMouseButtonUp(0))
                mouseGrabbed = false;

            return;
        }

        // Device Simulator / Game view can temporarily report an invalid
        // mouse position such as (Infinity, -Infinity). Passing that value
        // directly to Camera.ScreenPointToRay() causes:
        // "Screen position out of view frustum" every frame.
        if (!TryGetValidMouseRay(out Ray mouseRay))
        {
            if (Input.GetMouseButtonUp(0))
                mouseGrabbed = false;

            return;
        }

        bool pointingAtModel =
            TryRaycastThisModel(
                mouseRay,
                out RaycastHit hit);

        if (Input.GetMouseButtonDown(0) &&
            pointingAtModel)
        {
            mouseGrabbed = true;

            mouseGrabDistance =
                Vector3.Distance(
                    mainCamera.transform.position,
                    hit.point) *
                mouseGrabDistanceMultiplier;

            Vector3 targetPoint =
                mouseRay.GetPoint(
                    mouseGrabDistance);

            mouseGrabOffset =
                modelRoot.transform.position -
                targetPoint;
        }

        if (mouseGrabbed &&
            Input.GetMouseButton(0))
        {
            Vector3 targetPoint =
                mouseRay.GetPoint(
                    mouseGrabDistance);

            modelRoot.transform.position =
                targetPoint +
                mouseGrabOffset;
        }

        if (Input.GetMouseButtonUp(0))
            mouseGrabbed = false;

        if (Input.GetMouseButton(1) &&
            pointingAtModel)
        {
            float yaw =
                Input.GetAxis("Mouse X") *
                mouseRotateSpeed *
                Time.unscaledDeltaTime;

            float pitch =
                -Input.GetAxis("Mouse Y") *
                mouseRotateSpeed *
                Time.unscaledDeltaTime;

            modelRoot.transform.Rotate(
                Vector3.up,
                yaw,
                Space.World);

            modelRoot.transform.Rotate(
                mainCamera.transform.right,
                pitch,
                Space.World);
        }

        float wheel =
            Input.mouseScrollDelta.y;

        if (pointingAtModel &&
            Mathf.Abs(wheel) > 0.001f)
        {
            float factor =
                1f +
                wheel *
                mouseWheelScaleStep;

            ApplyScaleFactor(
                factor);
        }

        if (Input.GetKeyDown(KeyCode.R))
            ResetToTeacherDesk();
    }

    /// <summary>
    /// Safely builds a mouse ray for desktop/Editor testing.
    /// Unity Device Simulator may temporarily return +/-Infinity for
    /// Input.mousePosition while the cursor is outside the simulated screen.
    /// </summary>
    private bool TryGetValidMouseRay(
        out Ray mouseRay)
    {
        mouseRay = default;

        if (mainCamera == null)
            return false;

        Vector3 mousePosition =
            Input.mousePosition;

        // Never pass NaN/Infinity to Camera.ScreenPointToRay().
        if (!IsFinite(mousePosition.x) ||
            !IsFinite(mousePosition.y) ||
            !IsFinite(mousePosition.z))
        {
            return false;
        }

        Rect pixelRect =
            mainCamera.pixelRect;

        if (pixelRect.width <= 0f ||
            pixelRect.height <= 0f)
        {
            return false;
        }

        // In the Device Simulator the cursor can be outside the simulated
        // camera area. Ignore interaction until it returns inside.
        if (!pixelRect.Contains(
                new Vector2(
                    mousePosition.x,
                    mousePosition.y)))
        {
            return false;
        }

        mouseRay =
            mainCamera.ScreenPointToRay(
                mousePosition);

        return
            IsFinite(mouseRay.origin.x) &&
            IsFinite(mouseRay.origin.y) &&
            IsFinite(mouseRay.origin.z) &&
            IsFinite(mouseRay.direction.x) &&
            IsFinite(mouseRay.direction.y) &&
            IsFinite(mouseRay.direction.z);
    }

    private static bool IsFinite(
        float value)
    {
        return
            !float.IsNaN(value) &&
            !float.IsInfinity(value);
    }

    private bool TryRaycastThisModel(
        Ray ray,
        out RaycastHit modelHit)
    {
        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                100f,
                ~0,
                QueryTriggerInteraction.Ignore);

        Array.Sort(
            hits,
            (a, b) =>
                a.distance.CompareTo(
                    b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
                continue;

            if (hit.collider.transform == modelRoot.transform ||
                hit.collider.transform.IsChildOf(modelRoot.transform))
            {
                modelHit = hit;
                return true;
            }
        }

        modelHit = default;
        return false;
    }

    // =========================================================
    // XR controller grip interaction
    // =========================================================

    private void UpdateXRInteraction()
    {
        RefreshXRDevices();

        bool leftGrip =
            ReadGrip(leftDevice);

        bool rightGrip =
            ReadGrip(rightDevice);

        bool leftTracked =
            TryGetControllerPose(
                leftDevice,
                out Vector3 leftPosition,
                out Quaternion leftRotation);

        bool rightTracked =
            TryGetControllerPose(
                rightDevice,
                out Vector3 rightPosition,
                out Quaternion rightRotation);

        bool leftNear =
            leftTracked &&
            IsPositionNearModel(
                leftPosition);

        bool rightNear =
            rightTracked &&
            IsPositionNearModel(
                rightPosition);

        // Start two-hand interaction when both grips are held and both hands
        // are close enough to the model (or one hand already owns the grab).
        bool wantsTwoHands =
            leftGrip &&
            rightGrip &&
            leftTracked &&
            rightTracked &&
            (twoHandActive ||
             grabbedHand.HasValue ||
             (leftNear && rightNear));

        if (wantsTwoHands)
        {
            if (!twoHandActive)
            {
                BeginTwoHand(
                    leftPosition,
                    rightPosition);
            }

            UpdateTwoHand(
                leftPosition,
                rightPosition);

            grabbedHand = null;
        }
        else
        {
            if (twoHandActive)
                EndTwoHand();

            // Rising-edge one-hand grab.
            if (!grabbedHand.HasValue)
            {
                if (leftGrip &&
                    !previousLeftGrip &&
                    leftNear)
                {
                    BeginOneHand(
                        XRNode.LeftHand,
                        leftPosition,
                        leftRotation);
                }
                else if (rightGrip &&
                         !previousRightGrip &&
                         rightNear)
                {
                    BeginOneHand(
                        XRNode.RightHand,
                        rightPosition,
                        rightRotation);
                }
            }

            if (grabbedHand.HasValue)
            {
                bool held =
                    grabbedHand.Value ==
                    XRNode.LeftHand
                        ? leftGrip
                        : rightGrip;

                bool tracked =
                    grabbedHand.Value ==
                    XRNode.LeftHand
                        ? leftTracked
                        : rightTracked;

                if (!held || !tracked)
                {
                    grabbedHand = null;
                }
                else
                {
                    Vector3 controllerPosition =
                        grabbedHand.Value ==
                        XRNode.LeftHand
                            ? leftPosition
                            : rightPosition;

                    Quaternion controllerRotation =
                        grabbedHand.Value ==
                        XRNode.LeftHand
                            ? leftRotation
                            : rightRotation;

                    UpdateOneHand(
                        controllerPosition,
                        controllerRotation);
                }
            }
        }

        previousLeftGrip = leftGrip;
        previousRightGrip = rightGrip;
    }

    private void RefreshXRDevices()
    {
        if (!leftDevice.isValid)
        {
            leftDevice =
                InputDevices.GetDeviceAtXRNode(
                    XRNode.LeftHand);
        }

        if (!rightDevice.isValid)
        {
            rightDevice =
                InputDevices.GetDeviceAtXRNode(
                    XRNode.RightHand);
        }
    }

    private static bool ReadGrip(
        InputDevice device)
    {
        if (!device.isValid)
            return false;

        return device.TryGetFeatureValue(
                   CommonUsages.gripButton,
                   out bool pressed) &&
               pressed;
    }

    private static bool TryGetControllerPose(
        InputDevice device,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!device.isValid)
            return false;

        bool hasPosition =
            device.TryGetFeatureValue(
                CommonUsages.devicePosition,
                out position);

        bool hasRotation =
            device.TryGetFeatureValue(
                CommonUsages.deviceRotation,
                out rotation);

        return hasPosition &&
               hasRotation;
    }

    private bool IsPositionNearModel(
        Vector3 position)
    {
        if (interactionCollider == null)
            return false;

        Vector3 closest =
            interactionCollider.ClosestPoint(
                position);

        return Vector3.Distance(
                   closest,
                   position) <=
               xrGrabDistance;
    }

    private void BeginOneHand(
        XRNode hand,
        Vector3 controllerPosition,
        Quaternion controllerRotation)
    {
        grabbedHand = hand;

        oneHandPositionOffset =
            Quaternion.Inverse(
                controllerRotation) *
            (modelRoot.transform.position -
             controllerPosition);

        oneHandRotationOffset =
            Quaternion.Inverse(
                controllerRotation) *
            modelRoot.transform.rotation;
    }

    private void UpdateOneHand(
        Vector3 controllerPosition,
        Quaternion controllerRotation)
    {
        Vector3 desiredPosition =
            controllerPosition +
            controllerRotation *
            oneHandPositionOffset;

        Quaternion desiredRotation =
            controllerRotation *
            oneHandRotationOffset;

        float t =
            1f -
            Mathf.Exp(
                -xrFollowSpeed *
                Time.unscaledDeltaTime);

        modelRoot.transform.position =
            Vector3.Lerp(
                modelRoot.transform.position,
                desiredPosition,
                t);

        modelRoot.transform.rotation =
            Quaternion.Slerp(
                modelRoot.transform.rotation,
                desiredRotation,
                t);
    }

    private void BeginTwoHand(
        Vector3 leftPosition,
        Vector3 rightPosition)
    {
        twoHandActive = true;

        twoHandStartDistance =
            Mathf.Max(
                0.05f,
                Vector3.Distance(
                    leftPosition,
                    rightPosition));

        twoHandStartScale =
            modelRoot.transform.localScale;

        twoHandStartModelPosition =
            modelRoot.transform.position;

        twoHandStartMidpoint =
            (leftPosition +
             rightPosition) *
            0.5f;

        twoHandStartRotation =
            modelRoot.transform.rotation;

        twoHandStartDirection =
            (rightPosition -
             leftPosition).normalized;
    }

    private void UpdateTwoHand(
        Vector3 leftPosition,
        Vector3 rightPosition)
    {
        Vector3 midpoint =
            (leftPosition +
             rightPosition) *
            0.5f;

        float distance =
            Mathf.Max(
                0.05f,
                Vector3.Distance(
                    leftPosition,
                    rightPosition));

        float scaleFactor =
            distance /
            twoHandStartDistance;

        Vector3 targetScale =
            ClampAbsoluteScale(
                twoHandStartScale *
                scaleFactor);

        Vector3 currentDirection =
            (rightPosition -
             leftPosition).normalized;

        Quaternion deltaRotation =
            currentDirection.sqrMagnitude > 0.001f &&
            twoHandStartDirection.sqrMagnitude > 0.001f
                ? Quaternion.FromToRotation(
                    twoHandStartDirection,
                    currentDirection)
                : Quaternion.identity;

        Vector3 modelOffsetFromStartMidpoint =
            twoHandStartModelPosition -
            twoHandStartMidpoint;

        modelRoot.transform.localScale =
            targetScale;

        modelRoot.transform.position =
            midpoint +
            deltaRotation *
            modelOffsetFromStartMidpoint;

        modelRoot.transform.rotation =
            deltaRotation *
            twoHandStartRotation;
    }

    private void EndTwoHand()
    {
        twoHandActive = false;
    }

    // =========================================================
    // Scale / reset
    // =========================================================

    private void ApplyScaleFactor(
        float factor)
    {
        factor =
            Mathf.Max(
                0.05f,
                factor);

        modelRoot.transform.localScale =
            ClampAbsoluteScale(
                modelRoot.transform.localScale *
                factor);
    }

    private Vector3 ClampAbsoluteScale(
        Vector3 candidate)
    {
        float baseMagnitude =
            Mathf.Max(
                0.0001f,
                defaultLocalScale.magnitude);

        float ratio =
            candidate.magnitude /
            baseMagnitude;

        float clampedRatio =
            Mathf.Clamp(
                ratio,
                minimumScaleMultiplier,
                maximumScaleMultiplier);

        return defaultLocalScale *
               clampedRatio;
    }

    public void SaveResetPose()
    {
        if (modelRoot == null)
            return;

        resetPosition =
            modelRoot.transform.position;

        resetRotation =
            modelRoot.transform.rotation;

        resetScale =
            modelRoot.transform.localScale;

        if (defaultLocalScale ==
            Vector3.zero)
        {
            defaultLocalScale =
                resetScale;
        }
    }

    public void ResetToTeacherDesk()
    {
        if (modelRoot == null)
            return;

        modelRoot.transform.position =
            resetPosition;

        modelRoot.transform.rotation =
            resetRotation;

        modelRoot.transform.localScale =
            resetScale;

        mouseGrabbed = false;
        grabbedHand = null;
        twoHandActive = false;
    }
}
