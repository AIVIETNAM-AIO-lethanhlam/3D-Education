using UnityEngine;

namespace ARHeartTest
{
    /// <summary>
    /// Handles rotation and pinch scaling for the placed heart.
    /// One finger drag: rotate.
    /// Two finger pinch: scale.
    /// </summary>
    public sealed class ARHeartGestureController : MonoBehaviour
    {
        [Header("Rotation")]
        [SerializeField]
        [Min(0f)]
        private float rotationSpeed = 0.2f;

        [Header("Scaling")]
        [SerializeField]
        [Min(0.001f)]
        private float minimumScale = 0.03f;

        [SerializeField]
        [Min(0.001f)]
        private float maximumScale = 0.5f;

        [SerializeField]
        [Min(0f)]
        private float pinchScaleSpeed = 0.001f;

        private void Update()
        {
            if (Input.touchCount == 1)
            {
                HandleRotation(Input.GetTouch(0));
                return;
            }

            if (Input.touchCount >= 2)
            {
                HandlePinch(
                    Input.GetTouch(0),
                    Input.GetTouch(1)
                );
            }
        }

        private void HandleRotation(Touch touch)
        {
            if (touch.phase != TouchPhase.Moved)
            {
                return;
            }

            float yaw = -touch.deltaPosition.x * rotationSpeed;
            float pitch = touch.deltaPosition.y * rotationSpeed;

            transform.Rotate(
                Vector3.up,
                yaw,
                Space.World
            );

            transform.Rotate(
                Vector3.right,
                pitch,
                Space.Self
            );
        }

        private void HandlePinch(Touch firstTouch, Touch secondTouch)
        {
            Vector2 firstPreviousPosition =
                firstTouch.position - firstTouch.deltaPosition;

            Vector2 secondPreviousPosition =
                secondTouch.position - secondTouch.deltaPosition;

            float previousDistance = Vector2.Distance(
                firstPreviousPosition,
                secondPreviousPosition
            );

            float currentDistance = Vector2.Distance(
                firstTouch.position,
                secondTouch.position
            );

            float distanceDelta = currentDistance - previousDistance;

            float currentUniformScale = transform.localScale.x;
            float targetScale =
                currentUniformScale + distanceDelta * pinchScaleSpeed;

            targetScale = Mathf.Clamp(
                targetScale,
                minimumScale,
                maximumScale
            );

            transform.localScale = Vector3.one * targetScale;
        }
    }
}