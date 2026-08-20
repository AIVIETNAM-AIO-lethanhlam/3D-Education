using UnityEngine;

namespace VRMode.Script
{
    [RequireComponent(typeof(Collider))]
    public class VRTouchManipulator : MonoBehaviour
    {
        [Header("Tốc độ & Giới hạn di chuyển")]
        [SerializeField] private float moveSpeed = 0.002f;

        [Header("Tốc độ & Giới hạn Scale (Phóng to / Thu nhỏ)")]
        [SerializeField] private float minScale = 0.0005f;
        [SerializeField] private float maxScale = 0.02f;
        [SerializeField] private float scaleSpeed = 0.0005f;

        private Camera mainCamera;
        private bool isSelected = false;

        private void Start()
        {
            mainCamera = Camera.main;
        }

        private void Update()
        {
            if (Input.touchCount == 0)
            {
                isSelected = false;
                return;
            }

            // 1. Nhận diện thao tác vuốt 1 ngón (Di chuyển)
            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);

                if (touch.phase == TouchPhase.Began)
                {
                    Ray ray = mainCamera.ScreenPointToRay(touch.position);
                    if (Physics.Raycast(ray, out RaycastHit hit))
                    {
                        // Kiểm tra nếu ngón tay chạm đúng vào Collider của vật thể này
                        if (hit.transform == transform || hit.transform.IsChildOf(transform))
                        {
                            isSelected = true;
                        }
                    }
                }

                if (isSelected && touch.phase == TouchPhase.Moved)
                {
                    Vector3 touchDelta = new Vector3(touch.deltaPosition.x, touch.deltaPosition.y, 0);
                    transform.Translate(touchDelta * moveSpeed, Space.World);
                }
            }
            // 2. Nhận diện thao tác chụm 2 ngón (Pinch to Zoom)
            else if (Input.touchCount == 2 && isSelected)
            {
                Touch touch0 = Input.GetTouch(0);
                Touch touch1 = Input.GetTouch(1);

                Vector2 touch0Prev = touch0.position - touch0.deltaPosition;
                Vector2 touch1Prev = touch1.position - touch1.deltaPosition;

                float prevMag = (touch0Prev - touch1Prev).magnitude;
                float currentMag = (touch0.position - touch1.position).magnitude;

                float diff = currentMag - prevMag;

                Zoom(diff * scaleSpeed);
            }
        }

        public void Zoom(float amount)
        {
            Vector3 newScale = transform.localScale + Vector3.one * amount;

            // Giới hạn không cho quá nhỏ hoặc quá to
            newScale.x = Mathf.Clamp(newScale.x, minScale, maxScale);
            newScale.y = Mathf.Clamp(newScale.y, minScale, maxScale);
            newScale.z = Mathf.Clamp(newScale.z, minScale, maxScale);

            transform.localScale = newScale;
        }

        // Các hàm hỗ trợ nút bấm Gaze (+ / -) khi lắp vào kính VR
        public void ZoomIn() => Zoom(scaleSpeed * 20f);
        public void ZoomOut() => Zoom(-scaleSpeed * 20f);
    }
}