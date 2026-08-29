using UnityEngine;
using UnityEngine.EventSystems; // Cần thêm dòng này

public class CameraLook : MonoBehaviour
{
    [Header("References")]
    public Transform playerBody;
    public Transform cameraPivot;

    [Header("Look Settings")]
    public float sensitivity = 0.15f;

    private float xRotation = 0f;

    private int lookFingerId = -1;
    private Vector2 lastTouchPosition;

    void Update()
    {
        HandleLook();
    }

    private void HandleLook()
    {
        foreach (Touch touch in Input.touches)
        {
            if (touch.phase == TouchPhase.Began)
            {
                // Kiểm tra: Nằm ở nửa phải màn hình VÀ không chạm vào UI
                if (touch.position.x > Screen.width * 0.5f && 
                    !EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                {
                    lookFingerId = touch.fingerId;
                    lastTouchPosition = touch.position;
                }
            }

            if (touch.fingerId != lookFingerId)
                continue;

            if (touch.phase == TouchPhase.Moved)
            {
                Vector2 delta = touch.position - lastTouchPosition;

                float mouseX = delta.x * sensitivity;
                float mouseY = delta.y * sensitivity;

                // Xoay Player trái phải
                playerBody.Rotate(Vector3.up * mouseX);

                // Camera nhìn lên xuống
                xRotation -= mouseY;
                xRotation = Mathf.Clamp(xRotation, -80f, 80f);

                cameraPivot.localRotation =
                    Quaternion.Euler(xRotation, 0, 0);

                lastTouchPosition = touch.position;
            }

            if (touch.phase == TouchPhase.Ended ||
                touch.phase == TouchPhase.Canceled)
            {
                if (touch.fingerId == lookFingerId)
                    lookFingerId = -1;
            }
        }
    }
}