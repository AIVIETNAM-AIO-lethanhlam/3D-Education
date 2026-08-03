using UnityEngine;
using UnityEngine.EventSystems;

public class ARModelGestureController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARModelSceneController sceneController;
    [SerializeField] private Transform modelRoot;

    [Header("Scale")]
    [SerializeField] private float scaleSensitivity = 0.008f;
    [SerializeField] private float minimumScale = 0.15f;
    [SerializeField] private float maximumScale = 2.5f;

    [Header("Rotation")]
    [SerializeField] private float rotationSensitivity = 1f;

    [Header("Move")]
    [SerializeField] private float moveThresholdPixels = 8f;

    private Vector2 firstTouchStartPosition;
    private bool firstTouchIsDragging;

    private void Update()
    {
        if (sceneController == null ||
            !sceneController.IsModelPlaced ||
            modelRoot == null)
        {
            return;
        }

        if (Input.touchCount == 1)
        {
            HandleSingleTouch();
        }
        else if (Input.touchCount >= 2)
        {
            HandleTwoTouches();
        }
    }

    private void HandleSingleTouch()
    {
        Touch touch = Input.GetTouch(0);

        if (IsTouchOverUI(touch.fingerId))
        {
            return;
        }

        switch (touch.phase)
        {
            case TouchPhase.Began:
                firstTouchStartPosition = touch.position;
                firstTouchIsDragging = false;
                break;

            case TouchPhase.Moved:
                float distance =
                    Vector2.Distance(
                        firstTouchStartPosition,
                        touch.position
                    );

                if (distance >= moveThresholdPixels)
                {
                    firstTouchIsDragging = true;
                }

                if (firstTouchIsDragging)
                {
                    sceneController.TryMoveModel(touch.position);
                }

                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                firstTouchIsDragging = false;
                break;
        }
    }

    private void HandleTwoTouches()
    {
        Touch touch0 = Input.GetTouch(0);
        Touch touch1 = Input.GetTouch(1);

        if (IsTouchOverUI(touch0.fingerId) ||
            IsTouchOverUI(touch1.fingerId))
        {
            return;
        }

        Vector2 previousPosition0 =
            touch0.position - touch0.deltaPosition;

        Vector2 previousPosition1 =
            touch1.position - touch1.deltaPosition;

        HandlePinchScale(
            previousPosition0,
            previousPosition1,
            touch0.position,
            touch1.position
        );

        HandleTwistRotation(
            previousPosition0,
            previousPosition1,
            touch0.position,
            touch1.position
        );
    }

    private void HandlePinchScale(
        Vector2 previousPosition0,
        Vector2 previousPosition1,
        Vector2 currentPosition0,
        Vector2 currentPosition1
    )
    {
        float previousDistance = Vector2.Distance(
            previousPosition0,
            previousPosition1
        );

        float currentDistance = Vector2.Distance(
            currentPosition0,
            currentPosition1
        );

        float distanceDifference =
            currentDistance - previousDistance;

        float currentScale = modelRoot.localScale.x;

        float targetScale = Mathf.Clamp(
            currentScale + distanceDifference * scaleSensitivity,
            minimumScale,
            maximumScale
        );

        modelRoot.localScale = Vector3.one * targetScale;
    }

    private void HandleTwistRotation(
        Vector2 previousPosition0,
        Vector2 previousPosition1,
        Vector2 currentPosition0,
        Vector2 currentPosition1
    )
    {
        Vector2 previousDirection =
            previousPosition1 - previousPosition0;

        Vector2 currentDirection =
            currentPosition1 - currentPosition0;

        float angle = Vector2.SignedAngle(
            previousDirection,
            currentDirection
        );

        modelRoot.Rotate(
            Vector3.up,
            -angle * rotationSensitivity,
            Space.Self
        );
    }

    public void RotateLeft()
    {
        modelRoot.Rotate(0f, 15f, 0f, Space.Self);
    }

    public void RotateRight()
    {
        modelRoot.Rotate(0f, -15f, 0f, Space.Self);
    }

    public void ZoomIn()
    {
        SetScale(modelRoot.localScale.x + 0.1f);
    }

    public void ZoomOut()
    {
        SetScale(modelRoot.localScale.x - 0.1f);
    }

    private void SetScale(float scale)
    {
        float clampedScale = Mathf.Clamp(
            scale,
            minimumScale,
            maximumScale
        );

        modelRoot.localScale = Vector3.one * clampedScale;
    }

    private bool IsTouchOverUI(int fingerId)
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject(fingerId);
    }
}