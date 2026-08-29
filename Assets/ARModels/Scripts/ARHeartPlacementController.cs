using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ARHeartTest
{
    /// <summary>
    /// AR controller dùng chung cho 2 trường hợp:
    /// 1) Runtime models được ShowLessonScene truyền qua PlayerPrefs (Supabase/R2).
    /// 2) Fallback các prefab gán thủ công trong Inspector để test trong Editor.
    ///
    /// Khi mở ARScene từ ShowLessonScene:
    /// - đọc selected_lesson_models_json;
    /// - tự load model đầu tiên;
    /// - cache các model đã tải;
    /// - cho phép UI đổi model theo index.
    ///
    /// Runtime GLB được nạp bằng glTFast qua reflection, vì vậy file này không tạo
    /// compile dependency trực tiếp vào namespace GLTFast. Project vẫn cần cài glTFast
    /// để runtime có thể instantiate file .glb.
    /// </summary>
    public sealed class ARHeartPlacementController : MonoBehaviour
    {
        [Header("Camera Reference")]
        [SerializeField] private Camera arCamera;

        [Header("Fallback Model Prefabs (Editor only / optional)")]
        [Tooltip("Chỉ dùng khi ARScene được chạy trực tiếp và không có selected_lesson_models_json.")]
        [SerializeField] private List<GameObject> modelPrefabs = new List<GameObject>();

        [Header("Runtime GLB")]
        [Tooltip("Kích thước cạnh lớn nhất mà model runtime sẽ được auto-fit vào, tính bằng mét.")]
        [SerializeField] [Min(0.02f)] private float runtimeTargetSizeMeters = 0.25f;

        [Tooltip("Thư mục cache model nằm trong Application.persistentDataPath.")]
        [SerializeField] private string runtimeCacheFolder = "ARLessonModels";

        [Header("Scale Settings")]
        [SerializeField] [Min(0.05f)] private float initialRuntimeScale = 1.0f;
        [SerializeField] [Min(0.01f)] private float minRuntimeScale = 0.2f;
        [SerializeField] [Min(0.05f)] private float maxRuntimeScale = 4.0f;
        [SerializeField] private float scaleSensitivity = 2.0f;

        [Header("Position Settings")]
        [SerializeField] private float defaultDistance = 0.4f;
        [SerializeField] private float dragSensitivity = 0.5f;

        [Header("Rotation Settings")]
        [SerializeField] private float rotationSensitivity = 1.0f;

        [Header("Auto Rotate Settings")]
        [SerializeField] private float autoRotateSpeed = 45.0f;

        private readonly List<RuntimeModelRecord> runtimeModels = new List<RuntimeModelRecord>();
        private readonly Dictionary<int, GameObject> runtimeModelCache = new Dictionary<int, GameObject>();

        private int currentModelIndex;
        private GameObject spawnedHeart;
        private Vector3 localOffset;
        private float currentScale;
        private Vector3 localEulerAngles = Vector3.zero;
        private bool isModelVisible = true;
        private bool isAutoRotating;
        private bool isLoadingModel;
        private bool usesRuntimeManifest;

        public bool IsAutoRotating => isAutoRotating;
        public bool IsModelVisible => isModelVisible;
        public bool IsLoadingModel => isLoadingModel;
        public bool UsesRuntimeManifest => usesRuntimeManifest;
        public int CurrentModelIndex => currentModelIndex;

        // Giữ property cũ để không làm hỏng code khác trong project.
        public List<GameObject> ModelPrefabs => modelPrefabs;

        public int ModelCount =>
            usesRuntimeManifest ? runtimeModels.Count :
            (modelPrefabs != null ? modelPrefabs.Count : 0);

        public event Action ModelListChanged;
        public event Action<int> ModelChanged;
        public event Action<bool, string> LoadingStateChanged;

        [Serializable]
        private sealed class ModelLaunchManifest
        {
            public string class_id;
            public string lesson_id;
            public string mode;
            public ModelLaunchItem[] models;
        }

        [Serializable]
        private sealed class ModelLaunchItem
        {
            public string asset_id;
            public string lesson_id;
            public string lesson_title;
            public int chapter_order;
            public string name;
            public string file_name;
            public string bucket;
            public string storage_path;
            public string url;
            public string fallback_url;
            public int display_order;
        }

        private sealed class RuntimeModelRecord
        {
            public string assetId;
            public string lessonId;
            public string lessonTitle;
            public int chapterOrder;
            public string name;
            public string fileName;
            public string bucket;
            public string storagePath;
            public string url;
            public string fallbackUrl;
            public int displayOrder;
        }

        private void Awake()
        {
            if (arCamera == null) arCamera = GetComponentInChildren<Camera>();
            if (arCamera == null) arCamera = Camera.main;

            var planeManager = GetComponent<UnityEngine.XR.ARFoundation.ARPlaneManager>();
            if (planeManager == null)
                planeManager = GetComponentInParent<UnityEngine.XR.ARFoundation.ARPlaneManager>();

            // Scene hiện tại đang dùng camera-locked model, không đặt lên AR plane.
            if (planeManager != null)
                planeManager.enabled = false;

            localOffset = new Vector3(0f, 0f, defaultDistance);
            currentScale = initialRuntimeScale;
            localEulerAngles = Vector3.zero;

            ReadLessonModelsFromPlayerPrefs();
        }

        private void Start()
        {
            if (ModelCount > 0)
                SpawnModelIndex(0);
            else
                SetLoadingState(false, "Bài học này chưa có mô hình 3D.");
        }

        private void Update()
        {
            if (spawnedHeart == null) return;

            if (isAutoRotating)
                localEulerAngles.y += autoRotateSpeed * Time.deltaTime;

            HandleTouchInput();
            UpdateHeartTransform();
        }

        private void OnDestroy()
        {
            foreach (var pair in runtimeModelCache)
            {
                if (pair.Value != null)
                    Destroy(pair.Value);
            }
            runtimeModelCache.Clear();
        }

        private void ReadLessonModelsFromPlayerPrefs()
        {
            runtimeModels.Clear();

            string json = PlayerPrefs.GetString("selected_lesson_models_json", string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    ModelLaunchManifest manifest = JsonUtility.FromJson<ModelLaunchManifest>(json);
                    if (manifest?.models != null)
                    {
                        foreach (ModelLaunchItem item in manifest.models)
                        {
                            if (item == null) continue;

                            string displayName = !string.IsNullOrWhiteSpace(item.name)
                                ? item.name
                                : (!string.IsNullOrWhiteSpace(item.file_name)
                                    ? Path.GetFileNameWithoutExtension(item.file_name)
                                    : "3D Model");

                            runtimeModels.Add(new RuntimeModelRecord
                            {
                                assetId = item.asset_id ?? string.Empty,
                                lessonId = item.lesson_id ?? string.Empty,
                                lessonTitle = string.IsNullOrWhiteSpace(item.lesson_title) ? "Lesson" : item.lesson_title,
                                chapterOrder = item.chapter_order,
                                name = displayName,
                                fileName = item.file_name ?? string.Empty,
                                bucket = item.bucket ?? string.Empty,
                                storagePath = item.storage_path ?? string.Empty,
                                url = item.url ?? string.Empty,
                                fallbackUrl = item.fallback_url ?? string.Empty,
                                displayOrder = item.display_order
                            });
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError("[AR Runtime] Cannot parse selected_lesson_models_json: " + exception.Message);
                }
            }

            // Tương thích với flow cũ: nếu manifest chưa tồn tại nhưng ShowLessonScene
            // vẫn truyền selected_model_* thì tạo list 1 phần tử.
            if (runtimeModels.Count == 0)
            {
                string legacyUrl = PlayerPrefs.GetString("selected_model_url", string.Empty);
                string legacyPath = PlayerPrefs.GetString("selected_model_storage_path", string.Empty);
                string legacyName = PlayerPrefs.GetString("selected_model_name", string.Empty);

                if (!string.IsNullOrWhiteSpace(legacyUrl) || !string.IsNullOrWhiteSpace(legacyPath))
                {
                    runtimeModels.Add(new RuntimeModelRecord
                    {
                        assetId = PlayerPrefs.GetString("selected_model_asset_id", string.Empty),
                        lessonId = PlayerPrefs.GetString("selected_model_lesson_id", string.Empty),
                        lessonTitle = PlayerPrefs.GetString("selected_lesson_title", "Current Lesson"),
                        chapterOrder = PlayerPrefs.GetInt("selected_chapter_order", 0),
                        name = string.IsNullOrWhiteSpace(legacyName) ? "3D Model" : legacyName,
                        fileName = PlayerPrefs.GetString("selected_model_file_name", string.Empty),
                        bucket = PlayerPrefs.GetString("selected_model_bucket", string.Empty),
                        storagePath = legacyPath,
                        url = legacyUrl,
                        fallbackUrl = string.Empty,
                        displayOrder = 0
                    });
                }
            }

            usesRuntimeManifest = runtimeModels.Count > 0;

            Debug.Log(
                usesRuntimeManifest
                    ? $"[AR Runtime] Received {runtimeModels.Count} lesson model(s) from ShowLessonScene."
                    : "[AR Runtime] No runtime lesson model manifest. Using Inspector prefabs as fallback.");

            ModelListChanged?.Invoke();
        }

        public string GetModelName(int index)
        {
            if (usesRuntimeManifest)
            {
                if (index < 0 || index >= runtimeModels.Count) return "3D Model";
                return runtimeModels[index].name;
            }

            if (modelPrefabs == null || index < 0 || index >= modelPrefabs.Count || modelPrefabs[index] == null)
                return "3D Model";

            return CleanModelName(modelPrefabs[index].name);
        }

        public string GetModelLessonId(int index)
        {
            if (!usesRuntimeManifest || index < 0 || index >= runtimeModels.Count)
                return "fallback";

            return string.IsNullOrWhiteSpace(runtimeModels[index].lessonId)
                ? "unknown-lesson"
                : runtimeModels[index].lessonId;
        }

        public string GetModelLessonTitle(int index)
        {
            if (!usesRuntimeManifest || index < 0 || index >= runtimeModels.Count)
                return "Models";

            return string.IsNullOrWhiteSpace(runtimeModels[index].lessonTitle)
                ? "Lesson"
                : runtimeModels[index].lessonTitle;
        }

        public int GetModelChapterOrder(int index)
        {
            if (!usesRuntimeManifest || index < 0 || index >= runtimeModels.Count)
                return 0;

            return runtimeModels[index].chapterOrder;
        }

        /// <summary>
        /// Adds a model uploaded while ARScene is already open.
        /// The model is immediately available to the lesson/model list without reloading the scene.
        /// </summary>
        public int AddRuntimeModel(
            string assetId,
            string lessonId,
            string lessonTitle,
            int chapterOrder,
            string modelName,
            string fileName,
            string bucket,
            string storagePath,
            string url,
            int displayOrder = 0)
        {
            if (string.IsNullOrWhiteSpace(lessonId) ||
                string.IsNullOrWhiteSpace(url))
            {
                Debug.LogError(
                    "[AR Runtime] Cannot append uploaded model: lessonId or URL is empty.");
                return -1;
            }

            // Avoid duplicate rows if UI receives the same upload callback twice.
            for (int i = 0; i < runtimeModels.Count; i++)
            {
                RuntimeModelRecord existing = runtimeModels[i];

                if (!string.IsNullOrWhiteSpace(assetId) &&
                    string.Equals(
                        existing.assetId,
                        assetId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }

                if (string.Equals(
                        existing.lessonId,
                        lessonId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        existing.url,
                        url,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            string safeFileName =
                string.IsNullOrWhiteSpace(fileName)
                    ? "model.glb"
                    : fileName;

            string safeName =
                string.IsNullOrWhiteSpace(modelName)
                    ? Path.GetFileNameWithoutExtension(safeFileName)
                    : modelName;

            runtimeModels.Add(
                new RuntimeModelRecord
                {
                    assetId = assetId ?? string.Empty,
                    lessonId = lessonId,
                    lessonTitle =
                        string.IsNullOrWhiteSpace(lessonTitle)
                            ? "Lesson"
                            : lessonTitle,
                    chapterOrder = chapterOrder,
                    name = safeName,
                    fileName = safeFileName,
                    bucket =
                        string.IsNullOrWhiteSpace(bucket)
                            ? "lesson-models"
                            : bucket,
                    storagePath = storagePath ?? url,
                    url = url,
                    fallbackUrl = url,
                    displayOrder = displayOrder
                });

            usesRuntimeManifest = true;
            int newIndex = runtimeModels.Count - 1;

            Debug.Log(
                $"[AR Runtime] Added uploaded model '{safeName}' to lesson '{lessonTitle}'.");

            ModelListChanged?.Invoke();
            return newIndex;
        }

        public bool IsModelCached(int index)
        {
            return usesRuntimeManifest &&
                   runtimeModelCache.TryGetValue(index, out GameObject cached) &&
                   cached != null;
        }

        public bool ToggleAutoRotate()
        {
            isAutoRotating = !isAutoRotating;
            return isAutoRotating;
        }

        public bool ToggleVisibility()
        {
            isModelVisible = !isModelVisible;

            if (spawnedHeart != null)
                spawnedHeart.SetActive(isModelVisible);

            return isModelVisible;
        }

        /// <summary>
        /// API giữ tên cũ để UI hiện tại không phải đổi cách gọi.
        /// Runtime manifest -> tải/đổi GLB.
        /// Không có manifest -> fallback prefab Inspector.
        /// </summary>
        public void SpawnModelIndex(int index)
        {
            if (isLoadingModel) return;

            if (usesRuntimeManifest)
            {
                if (runtimeModels.Count == 0) return;
                index = Mathf.Clamp(index, 0, runtimeModels.Count - 1);
                StartCoroutine(LoadRuntimeModelRoutine(index));
                return;
            }

            SpawnFallbackPrefab(index);
        }

        private void SpawnFallbackPrefab(int index)
        {
            if (modelPrefabs == null || modelPrefabs.Count == 0)
                return;

            if (index < 0 || index >= modelPrefabs.Count)
                index = 0;

            currentModelIndex = index;

            if (spawnedHeart != null)
                Destroy(spawnedHeart);

            GameObject prefabToSpawn = modelPrefabs[currentModelIndex];
            if (prefabToSpawn == null) return;

            spawnedHeart = Instantiate(prefabToSpawn);
            spawnedHeart.name = prefabToSpawn.name;
            spawnedHeart.SetActive(isModelVisible);

            currentScale = initialRuntimeScale;
            localEulerAngles = Vector3.zero;

            UpdateHeartTransform();
            ModelChanged?.Invoke(currentModelIndex);
        }

        private IEnumerator LoadRuntimeModelRoutine(int index)
        {
            if (index < 0 || index >= runtimeModels.Count)
                yield break;

            RuntimeModelRecord record = runtimeModels[index];

            // Model đã tải rồi -> chỉ đổi active object, không download lại.
            if (runtimeModelCache.TryGetValue(index, out GameObject cached) && cached != null)
            {
                ActivateRuntimeModel(index, cached);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(record.url))
            {
                SetLoadingState(false,
                    $"Không có URL để tải {record.name}. Hãy kiểm tra signed URL từ ShowLessonScene.");
                Debug.LogError(
                    $"[AR Runtime] Empty URL. asset={record.assetId}, bucket={record.bucket}, path={record.storagePath}");
                yield break;
            }

            Debug.Log(
                $"[AR Runtime] Preparing model '{record.name}'." +
                $"\nRuntime source: {(record.url.IndexOf("X-Amz-Signature=", StringComparison.OrdinalIgnoreCase) >= 0 ? "R2 presigned URL" : "normal URL")}" +
                $"\nStorage path: {record.storagePath}");

            isLoadingModel = true;
            SetLoadingState(true, $"Đang tải {record.name}...");

            string safeFileName = MakeSafeFileName(
                !string.IsNullOrWhiteSpace(record.fileName)
                    ? record.fileName
                    : (!string.IsNullOrWhiteSpace(record.assetId)
                        ? record.assetId + ".glb"
                        : $"model_{index}.glb"));

            if (!safeFileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) &&
                !safeFileName.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
            {
                safeFileName += ".glb";
            }

            string cacheDir = Path.Combine(Application.persistentDataPath, runtimeCacheFolder);
            string localPath = Path.Combine(cacheDir, safeFileName);

            try
            {
                Directory.CreateDirectory(cacheDir);
            }
            catch (Exception exception)
            {
                isLoadingModel = false;
                SetLoadingState(false, "Không tạo được thư mục cache model: " + exception.Message);
                yield break;
            }

            // Download model if cache does not exist yet.
            // First try the private/signed URL. If Cloudflare returns AccessDenied,
            // automatically try the public/custom-domain fallback passed by ShowLessonScene.
            if (!File.Exists(localPath) || new FileInfo(localPath).Length == 0)
            {
                byte[] downloadedBytes = null;
                long responseCode = 0;
                string downloadError = null;
                string responseBody = null;

                yield return DownloadModelBytesRoutine(
                    record.url,
                    bytes => downloadedBytes = bytes,
                    code => responseCode = code,
                    error => downloadError = error,
                    body => responseBody = body);

                bool accessDenied =
                    responseCode == 403 &&
                    !string.IsNullOrWhiteSpace(responseBody) &&
                    responseBody.IndexOf("AccessDenied", StringComparison.OrdinalIgnoreCase) >= 0;

                if ((downloadedBytes == null || downloadedBytes.Length == 0) &&
                    accessDenied &&
                    !string.IsNullOrWhiteSpace(record.fallbackUrl))
                {
                    Debug.LogWarning(
                        "[AR Runtime] Private R2 URL returned AccessDenied. " +
                        "Trying configured public/custom-domain fallback for " + record.name + ".");

                    downloadedBytes = null;
                    responseCode = 0;
                    downloadError = null;
                    responseBody = null;

                    yield return DownloadModelBytesRoutine(
                        record.fallbackUrl,
                        bytes => downloadedBytes = bytes,
                        code => responseCode = code,
                        error => downloadError = error,
                        body => responseBody = body);
                }

                if (downloadedBytes == null || downloadedBytes.Length == 0)
                {
                    isLoadingModel = false;

                    string serverDetail = string.IsNullOrWhiteSpace(responseBody)
                        ? string.Empty
                        : " | Server: " + responseBody.Replace("\r", " ").Replace("\n", " ").Trim();

                    if (serverDetail.Length > 360)
                        serverDetail = serverDetail.Substring(0, 360) + "...";

                    string hint = accessDenied && string.IsNullOrWhiteSpace(record.fallbackUrl)
                        ? " R2 returned AccessDenied. The signer credential needs Object Read permission " +
                          "for the lesson-models bucket, or configure r2PublicBaseUrl on ShowLessonPageController " +
                          "using the PUBLIC r2.dev/custom-domain URL of the lesson-models bucket."
                        : string.Empty;

                    string message =
                        $"Không tải được {record.name} (HTTP {responseCode}): {downloadError}.{hint}{serverDetail}";

                    SetLoadingState(false, message);
                    Debug.LogError("[AR Runtime] " + message);
                    yield break;
                }

                try
                {
                    File.WriteAllBytes(localPath, downloadedBytes);
                }
                catch (Exception exception)
                {
                    isLoadingModel = false;
                    SetLoadingState(false, "Không lưu được model cache: " + exception.Message);
                    yield break;
                }
            }

            GameObject modelPivot = new GameObject("RuntimeModel_" + record.name);
            GameObject contentRoot = new GameObject("GLB_Content");
            contentRoot.transform.SetParent(modelPivot.transform, false);

            bool instantiateSuccess = false;
            string loadError = null;

            yield return LoadGlbWithGltfFastRoutine(
                localPath,
                contentRoot.transform,
                success => instantiateSuccess = success,
                error => loadError = error);

            if (!instantiateSuccess)
            {
                Destroy(modelPivot);
                isLoadingModel = false;

                string message =
                    string.IsNullOrWhiteSpace(loadError)
                        ? "Không thể load GLB."
                        : loadError;

                SetLoadingState(false, message);
                Debug.LogError("[AR Runtime] " + message);
                yield break;
            }

            AutoFitRuntimeModel(contentRoot.transform);

            modelPivot.SetActive(false);
            runtimeModelCache[index] = modelPivot;

            isLoadingModel = false;
            ActivateRuntimeModel(index, modelPivot);
            SetLoadingState(false, string.Empty);
        }

        private void ActivateRuntimeModel(int index, GameObject modelRoot)
        {
            foreach (var pair in runtimeModelCache)
            {
                if (pair.Value != null)
                    pair.Value.SetActive(false);
            }

            spawnedHeart = modelRoot;
            currentModelIndex = index;

            currentScale = initialRuntimeScale;
            localEulerAngles = Vector3.zero;
            localOffset = new Vector3(0f, 0f, defaultDistance);

            spawnedHeart.SetActive(isModelVisible);
            UpdateHeartTransform();

            PlayerPrefs.SetInt("selected_lesson_model_index", currentModelIndex);
            PlayerPrefs.SetString("selected_model_name", GetModelName(currentModelIndex));
            PlayerPrefs.Save();

            Debug.Log($"[AR Runtime] Showing model {currentModelIndex}: {GetModelName(currentModelIndex)}");
            ModelChanged?.Invoke(currentModelIndex);
        }

        private void AutoFitRuntimeModel(Transform contentRoot)
        {
            if (contentRoot == null) return;

            Renderer[] renderers = contentRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest <= 0.00001f)
                return;

            float fitScale = runtimeTargetSizeMeters / largest;

            // Center model geometry around the content root origin before camera-lock placement.
            Vector3 worldCenter = bounds.center;
            foreach (Transform child in contentRoot)
                child.position -= worldCenter;

            contentRoot.localScale = Vector3.one * fitScale;
        }

        private void HandleTouchInput()
        {
            if (!isModelVisible || isLoadingModel)
                return;

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
            else if (Input.touchCount == 2)
            {
                Touch touch0 = Input.GetTouch(0);
                Touch touch1 = Input.GetTouch(1);

                if (touch0.phase == TouchPhase.Moved || touch1.phase == TouchPhase.Moved)
                {
                    Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
                    Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;

                    float previousDistance = (touch0PrevPos - touch1PrevPos).magnitude;
                    float currentDistance = (touch0.position - touch1.position).magnitude;
                    float delta = currentDistance - previousDistance;

                    currentScale += (delta / Mathf.Max(1f, Screen.width)) * scaleSensitivity;
                    currentScale = Mathf.Clamp(currentScale, minRuntimeScale, maxRuntimeScale);

                    Vector2 previousVector = touch1PrevPos - touch0PrevPos;
                    Vector2 currentVector = touch1.position - touch0.position;
                    float angleDiff = Vector2.SignedAngle(previousVector, currentVector);
                    localEulerAngles.y -= angleDiff * rotationSensitivity;
                }
            }
        }

        private void UpdateHeartTransform()
        {
            Camera cam = arCamera != null ? arCamera : Camera.main;
            if (cam == null || spawnedHeart == null)
                return;

            Transform camTransform = cam.transform;
            spawnedHeart.transform.position = camTransform.TransformPoint(localOffset);

            Quaternion baseCameraRotation = Quaternion.LookRotation(camTransform.forward, camTransform.up);
            Quaternion customRotation = Quaternion.Euler(localEulerAngles);
            spawnedHeart.transform.rotation = baseCameraRotation * customRotation;
            spawnedHeart.transform.localScale = Vector3.one * currentScale;
        }

        private void SetLoadingState(bool loading, string message)
        {
            LoadingStateChanged?.Invoke(loading, message ?? string.Empty);
        }

        private IEnumerator DownloadModelBytesRoutine(
            string url,
            Action<byte[]> onBytes,
            Action<long> onResponseCode,
            Action<string> onError,
            Action<string> onResponseBody)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                onBytes?.Invoke(null);
                onResponseCode?.Invoke(0);
                onError?.Invoke("Empty URL");
                onResponseBody?.Invoke(string.Empty);
                yield break;
            }

            string exactUrl = url.Trim();

            bool isAwsPresigned =
                exactUrl.IndexOf("X-Amz-Signature=", StringComparison.OrdinalIgnoreCase) >= 0 ||
                exactUrl.IndexOf("X-Amz-Algorithm=AWS4-HMAC-SHA256", StringComparison.OrdinalIgnoreCase) >= 0;

            using UnityWebRequest request = UnityWebRequest.Get(exactUrl);
            request.timeout = 120;

            // AWS/R2 presigned URLs already contain authorization in the query string.
            // Do not modify them or attach unrelated auth headers.
            if (!isAwsPresigned)
                TryApplySupabaseAuthHeaders(request);

            yield return request.SendWebRequest();

            long code = request.responseCode;
            byte[] data = request.downloadHandler?.data;

            string body = string.Empty;
            if (request.result != UnityWebRequest.Result.Success)
            {
                try
                {
                    body = request.downloadHandler != null
                        ? request.downloadHandler.text
                        : string.Empty;
                }
                catch
                {
                    body = string.Empty;
                }
            }

            onResponseCode?.Invoke(code);
            onResponseBody?.Invoke(body);

            if (request.result == UnityWebRequest.Result.Success &&
                data != null &&
                data.Length > 0)
            {
                onBytes?.Invoke(data);
                onError?.Invoke(string.Empty);
                yield break;
            }

            onBytes?.Invoke(null);
            onError?.Invoke(request.error ?? "Download failed");
        }

        private void TryApplySupabaseAuthHeaders(UnityWebRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.url))
                return;

            if (request.url.IndexOf("/storage/v1/object/authenticated/", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            MonoBehaviour[] allBehaviours;
#if UNITY_2023_1_OR_NEWER
            allBehaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
            allBehaviours = FindObjectsOfType<MonoBehaviour>(true);
#endif
            foreach (MonoBehaviour behaviour in allBehaviours)
            {
                if (behaviour == null) continue;
                if (!string.Equals(
                        behaviour.GetType().Name,
                        "SupabaseRuntimeRestService",
                        StringComparison.Ordinal))
                    continue;

                MethodInfo method = behaviour.GetType().GetMethod(
                    "ApplyAuthHeaders",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(UnityWebRequest) },
                    null);

                if (method == null) continue;

                try
                {
                    method.Invoke(behaviour, new object[] { request });
                    Debug.Log("[AR Runtime] Applied Supabase auth headers to model download.");
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[AR Runtime] Cannot apply Supabase auth headers: " + exception.Message);
                }
            }
        }

        /// <summary>
        /// glTFast loader bằng reflection:
        /// - tránh compile error nếu namespace/package version thay đổi;
        /// - vẫn yêu cầu glTFast package tồn tại ở runtime.
        /// </summary>
        private IEnumerator LoadGlbWithGltfFastRoutine(
            string localPath,
            Transform parent,
            Action<bool> onCompleted,
            Action<string> onError)
        {
            Type gltfImportType = FindTypeInLoadedAssemblies("GLTFast.GltfImport");
            if (gltfImportType == null)
            {
                onError?.Invoke(
                    "Không tìm thấy GLTFast.GltfImport. Hãy cài package glTFast trong Package Manager trước khi build AR.");
                onCompleted?.Invoke(false);
                yield break;
            }

            object gltfImport;
            try
            {
                // glTFast versions used by Unity can expose a constructor whose
                // parameters are optional (for example logger/material/defer agent)
                // instead of a true parameterless CLR constructor.
                //
                // C# code such as `new GltfImport()` still compiles because those
                // arguments are optional, but Activator.CreateInstance(type) fails
                // with "Default constructor not found".
                //
                // Create the instance by invoking a compatible constructor and
                // explicitly supplying each optional/default argument.
                gltfImport = CreateInstanceWithOptionalDefaults(gltfImportType);

                if (gltfImport == null)
                    throw new MissingMethodException(
                        "No compatible GltfImport constructor was found.");
            }
            catch (Exception exception)
            {
                onError?.Invoke(
                    "Không tạo được GltfImport: " +
                    GetBaseExceptionMessage(exception));
                onCompleted?.Invoke(false);
                yield break;
            }

            string fileUrl = new Uri(localPath).AbsoluteUri;

            MethodInfo loadMethod = FindBestMethod(gltfImportType, "Load", typeof(string), typeof(Uri));
            if (loadMethod == null)
            {
                onError?.Invoke("glTFast không có hàm Load(string/Uri) tương thích.");
                onCompleted?.Invoke(false);
                yield break;
            }

            object loadReturn;
            try
            {
                loadReturn = InvokeWithDefaults(
                    gltfImport,
                    loadMethod,
                    loadMethod.GetParameters()[0].ParameterType == typeof(Uri)
                        ? (object)new Uri(fileUrl)
                        : fileUrl);
            }
            catch (Exception exception)
            {
                onError?.Invoke("glTFast Load() failed: " + GetBaseExceptionMessage(exception));
                onCompleted?.Invoke(false);
                yield break;
            }

            bool loadSuccess = false;
            string asyncError = null;
            yield return WaitForAsyncResult(
                loadReturn,
                result => loadSuccess = ConvertResultToBool(result),
                error => asyncError = error);

            if (!string.IsNullOrWhiteSpace(asyncError) || !loadSuccess)
            {
                onError?.Invoke(
                    string.IsNullOrWhiteSpace(asyncError)
                        ? "glTFast không đọc được file GLB."
                        : "glTFast Load error: " + asyncError);
                onCompleted?.Invoke(false);
                yield break;
            }

            MethodInfo instantiateMethod =
                FindMethodStartingWithParameter(gltfImportType, "InstantiateMainSceneAsync", typeof(Transform))
                ?? FindMethodStartingWithParameter(gltfImportType, "InstantiateMainScene", typeof(Transform));

            if (instantiateMethod == null)
            {
                onError?.Invoke("glTFast không có InstantiateMainSceneAsync(Transform).");
                onCompleted?.Invoke(false);
                yield break;
            }

            object instantiateReturn;
            try
            {
                instantiateReturn = InvokeWithDefaults(
                    gltfImport,
                    instantiateMethod,
                    parent);
            }
            catch (Exception exception)
            {
                onError?.Invoke("glTFast instantiate failed: " + GetBaseExceptionMessage(exception));
                onCompleted?.Invoke(false);
                yield break;
            }

            bool instantiateSuccess = false;
            asyncError = null;

            yield return WaitForAsyncResult(
                instantiateReturn,
                result => instantiateSuccess = ConvertResultToBool(result, true),
                error => asyncError = error);

            if (!string.IsNullOrWhiteSpace(asyncError))
            {
                onError?.Invoke("glTFast instantiate error: " + asyncError);
                onCompleted?.Invoke(false);
                yield break;
            }

            onCompleted?.Invoke(instantiateSuccess);
        }

        private static object CreateInstanceWithOptionalDefaults(Type type)
        {
            if (type == null)
                return null;

            ConstructorInfo[] constructors =
                type.GetConstructors(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            // Prefer the constructor with the fewest parameters.
            Array.Sort(
                constructors,
                (a, b) => a.GetParameters().Length.CompareTo(
                    b.GetParameters().Length));

            foreach (ConstructorInfo constructor in constructors)
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                object[] arguments = new object[parameters.Length];
                bool compatible = true;

                for (int i = 0; i < parameters.Length; i++)
                {
                    ParameterInfo parameter = parameters[i];

                    if (parameter.HasDefaultValue)
                    {
                        object defaultValue = parameter.DefaultValue;

                        // Some reflection runtimes expose Missing.Value for optional
                        // reference parameters. Passing null is what normal C# default
                        // invocation would effectively do for these glTFast services.
                        if (defaultValue == DBNull.Value ||
                            defaultValue == Type.Missing ||
                            defaultValue == Missing.Value)
                        {
                            defaultValue =
                                parameter.ParameterType.IsValueType
                                    ? Activator.CreateInstance(parameter.ParameterType)
                                    : null;
                        }

                        arguments[i] = defaultValue;
                        continue;
                    }

                    if (parameter.IsOptional)
                    {
                        arguments[i] =
                            parameter.ParameterType.IsValueType
                                ? Activator.CreateInstance(parameter.ParameterType)
                                : null;
                        continue;
                    }

                    // Many glTFast service/dependency parameters are reference types
                    // and accept null even when the metadata does not mark them optional.
                    if (!parameter.ParameterType.IsValueType ||
                        Nullable.GetUnderlyingType(parameter.ParameterType) != null)
                    {
                        arguments[i] = null;
                        continue;
                    }

                    compatible = false;
                    break;
                }

                if (!compatible)
                    continue;

                try
                {
                    return constructor.Invoke(arguments);
                }
                catch
                {
                    // Try the next constructor.
                }
            }

            return null;
        }

        private static Type FindTypeInLoadedAssemblies(string fullName)
        {
            Type direct = Type.GetType(fullName);
            if (direct != null) return direct;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }

            return null;
        }

        private static MethodInfo FindBestMethod(Type type, string methodName, params Type[] supportedFirstParameterTypes)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0)
                    continue;

                foreach (Type supported in supportedFirstParameterTypes)
                {
                    if (parameters[0].ParameterType == supported)
                        return method;
                }
            }

            return null;
        }

        private static MethodInfo FindMethodStartingWithParameter(Type type, string methodName, Type firstType)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length > 0 && parameters[0].ParameterType == firstType)
                    return method;
            }

            return null;
        }

        private static object InvokeWithDefaults(object target, MethodInfo method, object firstArgument)
        {
            ParameterInfo[] parameters = method.GetParameters();
            object[] arguments = new object[parameters.Length];
            arguments[0] = firstArgument;

            for (int i = 1; i < parameters.Length; i++)
            {
                if (parameters[i].HasDefaultValue)
                    arguments[i] = parameters[i].DefaultValue;
                else if (parameters[i].ParameterType.IsValueType)
                    arguments[i] = Activator.CreateInstance(parameters[i].ParameterType);
                else
                    arguments[i] = null;
            }

            return method.Invoke(target, arguments);
        }

        private static IEnumerator WaitForAsyncResult(
            object asyncObject,
            Action<object> onResult,
            Action<string> onError)
        {
            if (asyncObject == null)
            {
                // Một số API sync trả void; coi như thành công.
                onResult?.Invoke(true);
                yield break;
            }

            Task task = asyncObject as Task;

            // ValueTask / ValueTask<T>: lấy Task thông qua AsTask().
            if (task == null)
            {
                MethodInfo asTask = asyncObject.GetType().GetMethod(
                    "AsTask",
                    BindingFlags.Instance | BindingFlags.Public);

                if (asTask != null)
                {
                    try
                    {
                        task = asTask.Invoke(asyncObject, null) as Task;
                    }
                    catch (Exception exception)
                    {
                        onError?.Invoke(GetBaseExceptionMessage(exception));
                        yield break;
                    }
                }
            }

            if (task == null)
            {
                onResult?.Invoke(asyncObject);
                yield break;
            }

            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
            {
                onError?.Invoke(
                    task.Exception != null
                        ? GetBaseExceptionMessage(task.Exception)
                        : "Unknown async task error.");
                yield break;
            }

            if (task.IsCanceled)
            {
                onError?.Invoke("Operation was cancelled.");
                yield break;
            }

            PropertyInfo resultProperty = task.GetType().GetProperty("Result");
            object result = resultProperty != null
                ? resultProperty.GetValue(task)
                : (object)true;

            onResult?.Invoke(result);
        }

        private static bool ConvertResultToBool(object result, bool defaultForVoid = false)
        {
            if (result == null)
                return defaultForVoid;

            if (result is bool boolResult)
                return boolResult;

            return true;
        }

        private static string GetBaseExceptionMessage(Exception exception)
        {
            if (exception == null) return "Unknown error.";

            Exception current = exception;
            while (current.InnerException != null)
                current = current.InnerException;

            return current.Message;
        }

        private static string MakeSafeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "model.glb";

            foreach (char invalid in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalid, '_');

            return fileName;
        }

        private static string CleanModelName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "3D Model";

            return raw
                .Replace("Root", string.Empty)
                .Replace("Prefab", string.Empty)
                .Trim();
        }
    }
}
