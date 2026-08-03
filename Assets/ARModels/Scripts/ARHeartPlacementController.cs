using System.Collections.Generic;
using UnityEngine;

namespace ARHeartTest
{
    /// <summary>
    /// Interactive Camera-Locked AR Controller:
    /// - Quản lý di chuyển, thu phóng, xoay bằng cảm ứng.
    /// - Quản lý Tự động xoay (Auto Rotate).
    /// - Quản lý Ẩn/Hiện mô hình (Visibility).
    /// - Quản lý Danh sách nhiều Mô hình 3D (Model Switching).
    /// </summary>
    public sealed class ARHeartPlacementController : MonoBehaviour
    {
        [Header("Camera Reference")]
        [SerializeField] private Camera arCamera;

        [Header("Model List (Danh sách mô hình 3D)")]
        [Tooltip("Kéo tất cả các Prefab 3D bạn muốn dùng vào đây (Ví dụ: Tim, Não, Xương...)")]
        [SerializeField] private List<GameObject> modelPrefabs = new List<GameObject>();
        private int currentModelIndex = 0;

        [Header("Scale Settings")]
        [SerializeField] [Min(0.0001f)] private float initialScale = 0.002f;
        [SerializeField] [Min(0.0001f)] private float minScale = 0.0005f;
        [SerializeField] [Min(0.001f)]  private float maxScale = 0.02f;
        [SerializeField] private float scaleSensitivity = 0.005f;

        [Header("Position Settings")]
        [SerializeField] private float defaultDistance = 0.4f;
        [SerializeField] private float dragSensitivity = 0.5f;

        [Header("Rotation Settings")]
        [SerializeField] private float rotationSensitivity = 1.0f;

        [Header("Auto Rotate Settings")]
        [SerializeField] private float autoRotateSpeed = 45.0f; // Độ/giây
        private bool isAutoRotating = false;

        private GameObject spawnedHeart;
        private Vector3 localOffset;
        private float currentScale;
        private Vector3 localEulerAngles = Vector3.zero;
        private bool isModelVisible = true;

        public bool IsAutoRotating => isAutoRotating;
        public bool IsModelVisible => isModelVisible;
        public List<GameObject> ModelPrefabs => modelPrefabs;
        public int CurrentModelIndex => currentModelIndex;

        private void Awake()
        {
            if (arCamera == null) arCamera = GetComponentInChildren<Camera>();
            if (arCamera == null) arCamera = Camera.main;

            var planeManager = GetComponent<UnityEngine.XR.ARFoundation.ARPlaneManager>();
            if (planeManager == null) planeManager = GetComponentInParent<UnityEngine.XR.ARFoundation.ARPlaneManager>();
            if (planeManager != null) planeManager.enabled = false;

            localOffset = new Vector3(0f, 0f, defaultDistance);
            currentScale = initialScale;
            localEulerAngles = Vector3.zero;
        }

        private void Start()
        {
            SpawnModelIndex(0);
        }

        private void Update()
        {
            if (spawnedHeart == null) return;

            // Xử lý tự động xoay mô hình
            if (isAutoRotating)
            {
                localEulerAngles.y += autoRotateSpeed * Time.deltaTime;
            }

            HandleTouchInput();
            UpdateHeartTransform();
        }

        private void HandleTouchInput()
        {
            if (!isModelVisible) return;

            // --- 1 NGÓN TAY: Di chuyển vị trí ---
            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Moved)
                {
                    float deltaX = (touch.deltaPosition.x / Screen.width) * dragSensitivity;
                    float deltaY = (touch.deltaPosition.y / Screen.height) * dragSensitivity;

                    localOffset.x += deltaX;
                    localOffset.y += deltaY;
                }
            }
            // --- 2 NGÓN TAY: Phóng to/Thu nhỏ & Xoay ---
            else if (Input.touchCount == 2)
            {
                Touch touch0 = Input.GetTouch(0);
                Touch touch1 = Input.GetTouch(1);

                if (touch0.phase == TouchPhase.Moved || touch1.phase == TouchPhase.Moved)
                {
                    Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
                    Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;

                    // Phóng to / Thu nhỏ
                    float prevTouchDeltaMag = (touch0PrevPos - touch1PrevPos).magnitude;
                    float touchDeltaMag = (touch0.position - touch1.position).magnitude;
                    float deltaMagnitudeDiff = touchDeltaMag - prevTouchDeltaMag;

                    float normalizedScaleDelta = deltaMagnitudeDiff / Screen.width;
                    currentScale += normalizedScaleDelta * scaleSensitivity;
                    currentScale = Mathf.Clamp(currentScale, minScale, maxScale);

                    // Xoay tay
                    Vector2 prevVector = touch1PrevPos - touch0PrevPos;
                    Vector2 currVector = touch1.position - touch0.position;
                    float angleDiff = Vector2.SignedAngle(prevVector, currVector);

                    localEulerAngles.y -= angleDiff * rotationSensitivity;
                }
            }
        }

        private void UpdateHeartTransform()
        {
            Camera cam = (arCamera != null) ? arCamera : Camera.main;
            if (cam == null || spawnedHeart == null) return;

            Transform camTransform = cam.transform;

            Vector3 worldPosition = camTransform.TransformPoint(localOffset);
            spawnedHeart.transform.position = worldPosition;

            Quaternion baseCameraRotation = Quaternion.LookRotation(camTransform.forward);
            Quaternion customRotation = Quaternion.Euler(localEulerAngles);
            spawnedHeart.transform.rotation = baseCameraRotation * customRotation;

            spawnedHeart.transform.localScale = Vector3.one * currentScale;
        }

        // --- PUBLIC API DÙNG CHO UI BUTTONS ---

        /// <summary>
        /// Bật / Tắt chế độ tự động xoay
        /// </summary>
        public bool ToggleAutoRotate()
        {
            isAutoRotating = !isAutoRotating;
            return isAutoRotating;
        }

        /// <summary>
        /// Ẩn / Hiện mô hình 3D trên màn hình
        /// </summary>
        public bool ToggleVisibility()
        {
            isModelVisible = !isModelVisible;
            if (spawnedHeart != null)
            {
                spawnedHeart.SetActive(isModelVisible);
            }
            return isModelVisible;
        }

        /// <summary>
        /// Đổi mô hình 3D hiển thị theo chỉ mục Index
        /// </summary>
        public void SpawnModelIndex(int index)
        {
            if (modelPrefabs == null || modelPrefabs.Count == 0) return;

            if (index < 0 || index >= modelPrefabs.Count) index = 0;
            currentModelIndex = index;

            if (spawnedHeart != null)
            {
                Destroy(spawnedHeart);
            }

            GameObject prefabToSpawn = modelPrefabs[currentModelIndex];
            if (prefabToSpawn == null) return;

            spawnedHeart = Instantiate(prefabToSpawn);
            spawnedHeart.name = prefabToSpawn.name;
            spawnedHeart.SetActive(isModelVisible);

            UpdateHeartTransform();
        }
    }
}