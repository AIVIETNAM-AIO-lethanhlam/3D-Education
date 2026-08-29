using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ARHeartTest
{
    public class ARUIManagerUIToolkit : MonoBehaviour
    {
        [Header("UI Document")]
        [SerializeField] private UIDocument uiDocument;

        [Header("AR Controller")]
        [SerializeField] private ARHeartPlacementController arController;

        [Header("Backend Services")]
        [Tooltip("Required for teacher model upload. You can assign the same configured component used by CreateLessonScene.")]
        [SerializeField] private CloudflareR2StorageService r2StorageService;

        [Tooltip("Optional. If empty, the script finds/creates it beside SupabaseRuntimeRestService.")]
        [SerializeField] private SupabaseLessonService lessonService;

        [Tooltip("Used to load ALL lessons in the current class for the 'Thêm vào' dropdown.")]
        [SerializeField] private SupabaseRuntimeRestService restService;

        [Header("Sprites for Visibility Toggle")]
        [SerializeField] private Sprite showWhiteSprite;
        [SerializeField] private Sprite hideWhiteSprite;

        [Header("Sprites for Auto-Rotate Toggle")]
        [SerializeField] private Sprite rotateWhiteSprite;
        [SerializeField] private Sprite noRotateWhiteSprite;

        [Header("Sprite for Model List Button")]
        [SerializeField] private Sprite modelListIconSprite;

        private Button backButton;
        private Button menuButton;
        private VisualElement menuPanel;

        private Button toggleVisibilityBtn;
        private Button toggleRotateBtn;
        private Button modelListBtn;

        private VisualElement visibilityIcon;
        private VisualElement rotateIcon;
        private VisualElement modelListIcon;

        private VisualElement modelPopup;
        private Button closePopupBtn;
        private VisualElement modelListContainer;

        private VisualElement loadingPanel;
        private Label loadingLabel;

        // Teacher controls in the model-list popup.
        private VisualElement teacherModelActions;
        private Button teacherEditModelsBtn;
        private Button teacherDoneModelsBtn;

        // Teacher add-model popup.
        private VisualElement addModelPopup;
        private Button closeAddModelBtn;
        private Button chooseModelFileBtn;
        private Label selectedModelFileLabel;
        private DropdownField targetLessonDropdown;
        private Button confirmAddModelBtn;
        private Label addModelErrorLabel;
        private VisualElement uploadProgressRow;
        private Label uploadSpinnerLabel;
        private Label uploadProgressLabel;

        private bool isMenuOpen;
        private bool isModelVisible = true;
        private bool isAutoRotating;
        private bool isTeacher;
        private bool teacherEditMode;
        private bool isUploadingModel;

        private string selectedUploadModelPath = string.Empty;
        private IVisualElementScheduledItem spinnerSchedule;
        private int spinnerFrame;
        private readonly string[] spinnerFrames = { "◐", "◓", "◑", "◒" };

        private readonly HashSet<string> expandedLessonIds = new HashSet<string>();
        private readonly List<ClassLessonOption> classLessons = new List<ClassLessonOption>();

        [Serializable]
        private sealed class ChapterListWrapper
        {
            public ChapterDto[] items;
        }

        [Serializable]
        private sealed class LessonListWrapper
        {
            public LessonDto[] items;
        }

        [Serializable]
        private sealed class ChapterDto
        {
            public string id;
            public string title;
            public int chapter_order;
        }

        [Serializable]
        private sealed class LessonDto
        {
            public string id;
            public string chapter_id;
            public string title;
            public string created_at;
        }

        private sealed class ClassLessonOption
        {
            public string lessonId;
            public string lessonTitle;
            public int chapterOrder;
        }

        private sealed class LessonModelGroup
        {
            public string lessonId;
            public string lessonTitle;
            public int chapterOrder;
            public readonly List<int> modelIndices = new List<int>();
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (uiDocument == null)
            {
                Debug.LogError("[AR UI] UIDocument is missing.");
                return;
            }

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null)
                return;

            QueryElements(root);
            RegisterEvents();

            isTeacher =
                string.Equals(
                    PlayerPrefs.GetString("current_role", "student"),
                    "teacher",
                    StringComparison.OrdinalIgnoreCase);

            ConfigureRoleUI();

            if (arController != null)
            {
                arController.ModelListChanged += OnModelListChanged;
                arController.ModelChanged += OnModelChanged;
                arController.LoadingStateChanged += OnLoadingStateChanged;

                isModelVisible = arController.IsModelVisible;
                isAutoRotating = arController.IsAutoRotating;
            }

            if (modelListIcon != null && modelListIconSprite != null)
                modelListIcon.style.backgroundImage = new StyleBackground(modelListIconSprite);

            UpdateVisibilityUI();
            UpdateRotateUI();
            UpdateModelListButtonState();
            HideLoadingPanel();
            SetUploadLoading(false);

            if (isTeacher)
                StartCoroutine(LoadClassLessonsRoutine());
        }

        private void Start()
        {
            PopulateModelList();
            UpdateModelListButtonState();
        }

        private void OnDisable()
        {
            UnregisterEvents();

            if (arController != null)
            {
                arController.ModelListChanged -= OnModelListChanged;
                arController.ModelChanged -= OnModelChanged;
                arController.LoadingStateChanged -= OnLoadingStateChanged;
            }

            spinnerSchedule?.Pause();
        }

        private void ResolveReferences()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();

            if (arController == null)
                arController = FindAnyObjectByType<ARHeartPlacementController>();

            if (restService == null)
                restService = FindAnyObjectByType<SupabaseRuntimeRestService>();

            if (lessonService == null)
                lessonService = FindAnyObjectByType<SupabaseLessonService>();

            if (lessonService == null && restService != null)
            {
                lessonService = restService.GetComponent<SupabaseLessonService>();
                if (lessonService == null)
                    lessonService = restService.gameObject.AddComponent<SupabaseLessonService>();
            }

            if (r2StorageService == null)
                r2StorageService = FindAnyObjectByType<CloudflareR2StorageService>();
        }

        private void QueryElements(VisualElement root)
        {
            backButton = root.Q<Button>("back-button");
            menuButton = root.Q<Button>("menu-button");
            menuPanel = root.Q<VisualElement>("menu-panel");

            toggleVisibilityBtn = root.Q<Button>("toggle-visibility-btn");
            toggleRotateBtn = root.Q<Button>("toggle-rotate-btn");
            modelListBtn = root.Q<Button>("model-list-btn");

            visibilityIcon = root.Q<VisualElement>("visibility-icon");
            rotateIcon = root.Q<VisualElement>("rotate-icon");
            modelListIcon = root.Q<VisualElement>("model-list-icon");

            modelPopup = root.Q<VisualElement>("model-popup");
            closePopupBtn = root.Q<Button>("close-popup-btn");
            modelListContainer = root.Q<VisualElement>("model-list-container");

            loadingPanel = root.Q<VisualElement>("loading-panel");
            loadingLabel = root.Q<Label>("loading-label");

            teacherModelActions = root.Q<VisualElement>("teacher-model-actions");
            teacherEditModelsBtn = root.Q<Button>("teacher-edit-models-btn");
            teacherDoneModelsBtn = root.Q<Button>("teacher-done-models-btn");

            addModelPopup = root.Q<VisualElement>("add-model-popup");
            closeAddModelBtn = root.Q<Button>("close-add-model-btn");
            chooseModelFileBtn = root.Q<Button>("choose-model-file-btn");
            selectedModelFileLabel = root.Q<Label>("selected-model-file-label");
            targetLessonDropdown = root.Q<DropdownField>("target-lesson-dropdown");
            confirmAddModelBtn = root.Q<Button>("confirm-add-model-btn");
            addModelErrorLabel = root.Q<Label>("add-model-error-label");
            uploadProgressRow = root.Q<VisualElement>("upload-progress-row");
            uploadSpinnerLabel = root.Q<Label>("upload-spinner-label");
            uploadProgressLabel = root.Q<Label>("upload-progress-label");
        }

        private void RegisterEvents()
        {
            if (backButton != null) backButton.clicked += OnBackClicked;
            if (menuButton != null) menuButton.clicked += ToggleMenuPanel;
            if (toggleVisibilityBtn != null) toggleVisibilityBtn.clicked += OnToggleVisibilityClicked;
            if (toggleRotateBtn != null) toggleRotateBtn.clicked += OnToggleRotateClicked;
            if (modelListBtn != null) modelListBtn.clicked += OpenModelPopup;
            if (closePopupBtn != null) closePopupBtn.clicked += CloseModelPopup;

            if (teacherEditModelsBtn != null) teacherEditModelsBtn.clicked += OnTeacherEditClicked;
            if (teacherDoneModelsBtn != null) teacherDoneModelsBtn.clicked += OnTeacherDoneClicked;

            if (closeAddModelBtn != null) closeAddModelBtn.clicked += CloseAddModelPopup;
            if (chooseModelFileBtn != null) chooseModelFileBtn.clicked += PickModelFile;
            if (confirmAddModelBtn != null) confirmAddModelBtn.clicked += BeginUploadSelectedModel;
        }

        private void UnregisterEvents()
        {
            if (backButton != null) backButton.clicked -= OnBackClicked;
            if (menuButton != null) menuButton.clicked -= ToggleMenuPanel;
            if (toggleVisibilityBtn != null) toggleVisibilityBtn.clicked -= OnToggleVisibilityClicked;
            if (toggleRotateBtn != null) toggleRotateBtn.clicked -= OnToggleRotateClicked;
            if (modelListBtn != null) modelListBtn.clicked -= OpenModelPopup;
            if (closePopupBtn != null) closePopupBtn.clicked -= CloseModelPopup;

            if (teacherEditModelsBtn != null) teacherEditModelsBtn.clicked -= OnTeacherEditClicked;
            if (teacherDoneModelsBtn != null) teacherDoneModelsBtn.clicked -= OnTeacherDoneClicked;

            if (closeAddModelBtn != null) closeAddModelBtn.clicked -= CloseAddModelPopup;
            if (chooseModelFileBtn != null) chooseModelFileBtn.clicked -= PickModelFile;
            if (confirmAddModelBtn != null) confirmAddModelBtn.clicked -= BeginUploadSelectedModel;
        }

        private void ConfigureRoleUI()
        {
            if (teacherModelActions != null)
            {
                teacherModelActions.style.display =
                    isTeacher
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
            }

            teacherEditMode = false;

            if (teacherEditModelsBtn != null)
                teacherEditModelsBtn.text = "Chỉnh sửa";

            if (addModelPopup != null)
                addModelPopup.AddToClassList("hidden");
        }

        private void OnBackClicked()
        {
            string targetScene = PlayerPrefs.GetString("previous_scene", "ShowLessonScene");
            if (string.IsNullOrWhiteSpace(targetScene))
                targetScene = "ShowLessonScene";

            PlayerPrefs.Save();

            if (Application.CanStreamedLevelBeLoaded(targetScene))
                SceneManager.LoadScene(targetScene);
            else
                Debug.LogError($"[AR UI] Scene '{targetScene}' is not in Build Profiles.");
        }

        private void ToggleMenuPanel()
        {
            isMenuOpen = !isMenuOpen;
            if (menuPanel == null) return;

            if (isMenuOpen)
                menuPanel.RemoveFromClassList("hidden");
            else
                menuPanel.AddToClassList("hidden");
        }

        private void OnToggleVisibilityClicked()
        {
            isModelVisible =
                arController != null
                    ? arController.ToggleVisibility()
                    : !isModelVisible;

            UpdateVisibilityUI();
        }

        private void UpdateVisibilityUI()
        {
            if (visibilityIcon == null) return;

            Sprite targetSprite =
                isModelVisible
                    ? showWhiteSprite
                    : hideWhiteSprite;

            if (targetSprite != null)
                visibilityIcon.style.backgroundImage =
                    new StyleBackground(targetSprite);
        }

        private void OnToggleRotateClicked()
        {
            isAutoRotating =
                arController != null
                    ? arController.ToggleAutoRotate()
                    : !isAutoRotating;

            UpdateRotateUI();
        }

        private void UpdateRotateUI()
        {
            if (rotateIcon == null) return;

            Sprite targetSprite =
                isAutoRotating
                    ? rotateWhiteSprite
                    : noRotateWhiteSprite;

            if (targetSprite != null)
                rotateIcon.style.backgroundImage =
                    new StyleBackground(targetSprite);
        }

        private void OpenModelPopup()
        {
            PopulateModelList();

            if (modelPopup != null)
                modelPopup.RemoveFromClassList("hidden");

            if (isMenuOpen)
                ToggleMenuPanel();
        }

        private void CloseModelPopup()
        {
            if (modelPopup != null)
                modelPopup.AddToClassList("hidden");
        }

        private void OnTeacherEditClicked()
        {
            if (!isTeacher || isUploadingModel)
                return;

            if (!teacherEditMode)
            {
                teacherEditMode = true;
                if (teacherEditModelsBtn != null)
                    teacherEditModelsBtn.text = "Thêm mô hình";
                return;
            }

            OpenAddModelPopup();
        }

        private void OnTeacherDoneClicked()
        {
            if (!isTeacher || isUploadingModel)
                return;

            teacherEditMode = false;

            if (teacherEditModelsBtn != null)
                teacherEditModelsBtn.text = "Chỉnh sửa";

            CloseAddModelPopup();
        }

        private void OpenAddModelPopup()
        {
            if (!isTeacher || addModelPopup == null)
                return;

            ClearAddModelError();

            if (classLessons.Count == 0)
                StartCoroutine(LoadClassLessonsRoutine());

            RefreshLessonDropdown();

            addModelPopup.RemoveFromClassList("hidden");
        }

        private void CloseAddModelPopup()
        {
            if (isUploadingModel)
                return;

            if (addModelPopup != null)
                addModelPopup.AddToClassList("hidden");
        }

        private void PickModelFile()
        {
            if (!isTeacher || isUploadingModel)
                return;

#if UNITY_EDITOR
            string path =
                UnityEditor.EditorUtility.OpenFilePanel(
                    "Chọn mô hình 3D",
                    string.Empty,
                    "glb");

            SetSelectedUploadFile(path);
#else
            if (!TryOpenNativeFilePicker())
            {
                ShowAddModelError(
                    "Không tìm thấy NativeFilePicker. Hãy cài plugin hoặc chọn file trên Unity Editor.");
            }
#endif
        }

        private bool TryOpenNativeFilePicker()
        {
            try
            {
                Type pickerType = FindTypeInLoadedAssemblies("NativeFilePicker");
                if (pickerType == null)
                    return false;

                MethodInfo[] methods =
                    pickerType.GetMethods(
                        BindingFlags.Public |
                        BindingFlags.Static);

                foreach (MethodInfo method in methods)
                {
                    if (!string.Equals(method.Name, "PickFile", StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 0 ||
                        !typeof(Delegate).IsAssignableFrom(parameters[0].ParameterType))
                        continue;

                    MethodInfo callbackMethod =
                        GetType().GetMethod(
                            nameof(OnNativeModelFilePicked),
                            BindingFlags.Instance |
                            BindingFlags.NonPublic);

                    Delegate callback =
                        Delegate.CreateDelegate(
                            parameters[0].ParameterType,
                            this,
                            callbackMethod,
                            false);

                    if (callback == null)
                        continue;

                    object[] args = new object[parameters.Length];
                    args[0] = callback;

                    for (int i = 1; i < parameters.Length; i++)
                    {
                        if (parameters[i].ParameterType == typeof(string[]))
                        {
                            args[i] = new[]
                            {
                                "model/gltf-binary",
                                "application/octet-stream"
                            };
                        }
                        else if (parameters[i].HasDefaultValue)
                        {
                            args[i] = parameters[i].DefaultValue;
                        }
                        else
                        {
                            args[i] =
                                parameters[i].ParameterType.IsValueType
                                    ? Activator.CreateInstance(parameters[i].ParameterType)
                                    : null;
                        }
                    }

                    method.Invoke(null, args);
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[AR UI] Native file picker error: " + exception);
            }

            return false;
        }

        // Compatible with NativeFilePicker callbacks shaped as Action<string>.
        private void OnNativeModelFilePicked(string path)
        {
            SetSelectedUploadFile(path);
        }

        private static Type FindTypeInLoadedAssemblies(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName, false);
                if (type != null) return type;
            }

            return null;
        }

        private void SetSelectedUploadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".glb", StringComparison.OrdinalIgnoreCase))
            {
                ShowAddModelError("Vui lòng chọn file .glb.");
                return;
            }

            selectedUploadModelPath = path;

            if (selectedModelFileLabel != null)
                selectedModelFileLabel.text = Path.GetFileName(path);

            ClearAddModelError();
        }

        private void BeginUploadSelectedModel()
        {
            if (!isTeacher || isUploadingModel)
                return;

            if (string.IsNullOrWhiteSpace(selectedUploadModelPath) ||
                !File.Exists(selectedUploadModelPath))
            {
                ShowAddModelError("Bạn chưa chọn file GLB hợp lệ.");
                return;
            }

            int selectedLessonIndex =
                targetLessonDropdown != null
                    ? targetLessonDropdown.index
                    : -1;

            if (selectedLessonIndex < 0 ||
                selectedLessonIndex >= classLessons.Count)
            {
                ShowAddModelError("Vui lòng chọn bài học ở mục 'Thêm vào'.");
                return;
            }

            ResolveReferences();

            if (r2StorageService == null)
            {
                ShowAddModelError(
                    "ARScene chưa có CloudflareR2StorageService. " +
                    "Hãy gắn component đã cấu hình R2 vào UIDocument của ARScene.");
                return;
            }

            if (lessonService == null)
            {
                ShowAddModelError(
                    "Không tìm thấy SupabaseLessonService/SupabaseRuntimeRestService.");
                return;
            }

            StartCoroutine(
                UploadSelectedModelRoutine(
                    classLessons[selectedLessonIndex]));
        }

        private IEnumerator UploadSelectedModelRoutine(
            ClassLessonOption targetLesson)
        {
            isUploadingModel = true;
            SetUploadLoading(true, "Đang tải mô hình lên Cloudflare R2...");
            ClearAddModelError();

            string teacherId =
                PlayerPrefs.GetString("user_id", string.Empty);

            string classId =
                PlayerPrefs.GetString("selected_class_id", string.Empty);

            if (string.IsNullOrWhiteSpace(teacherId) ||
                string.IsNullOrWhiteSpace(classId))
            {
                FinishUploadWithError(
                    "Thiếu user_id hoặc selected_class_id.");
                yield break;
            }

            string objectKey =
                $"{teacherId}/{classId}/{targetLesson.lessonId}/models/{Guid.NewGuid():N}.glb";

            string uploadedUrl = null;
            string uploadError = null;

            yield return r2StorageService.UploadFile(
                "lesson-models",
                objectKey,
                selectedUploadModelPath,
                "model/gltf-binary",
                url => uploadedUrl = url,
                error => uploadError = error);

            if (!string.IsNullOrWhiteSpace(uploadError) ||
                string.IsNullOrWhiteSpace(uploadedUrl))
            {
                FinishUploadWithError(
                    string.IsNullOrWhiteSpace(uploadError)
                        ? "Upload model thất bại."
                        : uploadError);
                yield break;
            }

            SetUploadLoading(true, "Đang lưu model vào database...");

            string databaseError = null;
            string fileName =
                Path.GetFileName(selectedUploadModelPath);

            LessonAssetInsert asset =
                new LessonAssetInsert
                {
                    lesson_id = targetLesson.lessonId,
                    uploaded_by = teacherId,
                    asset_type = "model_3d",
                    file_name = fileName,
                    storage_bucket = "lesson-models",
                    storage_path = uploadedUrl,
                    mime_type = "model/gltf-binary",
                    file_extension = ".glb",
                    file_size_bytes =
                        new FileInfo(selectedUploadModelPath).Length,
                    display_order =
                        GetNextDisplayOrderForLesson(
                            targetLesson.lessonId)
                };

            bool databaseSaved = false;

            yield return lessonService.CreateLessonAsset(
                asset,
                () => databaseSaved = true,
                error => databaseError = error);

            if (!databaseSaved ||
                !string.IsNullOrWhiteSpace(databaseError))
            {
                FinishUploadWithError(
                    string.IsNullOrWhiteSpace(databaseError)
                        ? "Không lưu được lesson_assets."
                        : databaseError);
                yield break;
            }

            string modelName =
                Path.GetFileNameWithoutExtension(fileName);

            int newIndex =
                arController != null
                    ? arController.AddRuntimeModel(
                        string.Empty,
                        targetLesson.lessonId,
                        targetLesson.lessonTitle,
                        targetLesson.chapterOrder,
                        modelName,
                        fileName,
                        "lesson-models",
                        uploadedUrl,
                        uploadedUrl,
                        asset.display_order)
                    : -1;

            expandedLessonIds.Add(targetLesson.lessonId);

            selectedUploadModelPath = string.Empty;

            if (selectedModelFileLabel != null)
                selectedModelFileLabel.text = "Chưa chọn file";

            SetUploadLoading(false);
            isUploadingModel = false;

            PopulateModelList();
            CloseAddModelPopup();

            // Keep edit mode enabled so teacher can add another model immediately.
            if (teacherEditModelsBtn != null)
                teacherEditModelsBtn.text = "Thêm mô hình";

            Debug.Log(
                $"[AR UI] Uploaded model '{modelName}' to lesson '{targetLesson.lessonTitle}'. " +
                $"Runtime index={newIndex}");
        }

        private int GetNextDisplayOrderForLesson(string lessonId)
        {
            if (arController == null)
                return 0;

            int count = 0;
            for (int i = 0; i < arController.ModelCount; i++)
            {
                if (string.Equals(
                        arController.GetModelLessonId(i),
                        lessonId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        private IEnumerator LoadClassLessonsRoutine()
        {
            ResolveReferences();

            if (restService == null)
            {
                Debug.LogWarning(
                    "[AR UI] Cannot load class lessons: SupabaseRuntimeRestService missing.");
                yield break;
            }

            string classId =
                PlayerPrefs.GetString(
                    "selected_class_id",
                    string.Empty);

            if (string.IsNullOrWhiteSpace(classId))
                yield break;

            string encodedClassId =
                UnityWebRequest.EscapeURL(classId);

            string chapterJson = null;
            string error = null;

            yield return restService.SendJson(
                UnityWebRequest.kHttpVerbGET,
                "rest/v1/chapters" +
                "?select=id,title,chapter_order" +
                $"&class_id=eq.{encodedClassId}" +
                "&order=chapter_order.asc,created_at.asc",
                null,
                null,
                value => chapterJson = value,
                message => error = message);

            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning(
                    "[AR UI] Cannot load chapters for upload dropdown: " + error);
                yield break;
            }

            ChapterListWrapper chapters =
                ParseArray<ChapterListWrapper>(chapterJson);

            if (chapters?.items == null ||
                chapters.items.Length == 0)
            {
                yield break;
            }

            Dictionary<string, int> chapterOrderById =
                new Dictionary<string, int>();

            List<string> chapterIds =
                new List<string>();

            foreach (ChapterDto chapter in chapters.items)
            {
                if (chapter == null ||
                    string.IsNullOrWhiteSpace(chapter.id))
                {
                    continue;
                }

                chapterIds.Add(chapter.id);
                chapterOrderById[chapter.id] =
                    chapter.chapter_order;
            }

            if (chapterIds.Count == 0)
                yield break;

            string lessonJson = null;
            error = null;

            yield return restService.SendJson(
                UnityWebRequest.kHttpVerbGET,
                "rest/v1/lessons" +
                "?select=id,chapter_id,title,created_at" +
                $"&chapter_id=in.({string.Join(",", chapterIds)})" +
                "&order=created_at.asc",
                null,
                null,
                value => lessonJson = value,
                message => error = message);

            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning(
                    "[AR UI] Cannot load lessons for upload dropdown: " + error);
                yield break;
            }

            LessonListWrapper lessons =
                ParseArray<LessonListWrapper>(lessonJson);

            classLessons.Clear();

            if (lessons?.items != null)
            {
                foreach (LessonDto lesson in lessons.items)
                {
                    if (lesson == null ||
                        string.IsNullOrWhiteSpace(lesson.id))
                    {
                        continue;
                    }

                    int chapterOrder = 0;

                    if (!string.IsNullOrWhiteSpace(lesson.chapter_id))
                    {
                        chapterOrderById.TryGetValue(
                            lesson.chapter_id,
                            out chapterOrder);
                    }

                    classLessons.Add(
                        new ClassLessonOption
                        {
                            lessonId = lesson.id,
                            lessonTitle =
                                string.IsNullOrWhiteSpace(lesson.title)
                                    ? "Bài học"
                                    : lesson.title,
                            chapterOrder = chapterOrder
                        });
                }
            }

            classLessons.Sort(
                (a, b) =>
                {
                    int chapterCompare =
                        a.chapterOrder.CompareTo(
                            b.chapterOrder);

                    if (chapterCompare != 0)
                        return chapterCompare;

                    return string.Compare(
                        a.lessonTitle,
                        b.lessonTitle,
                        StringComparison.OrdinalIgnoreCase);
                });

            RefreshLessonDropdown();
        }

        private static T ParseArray<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default;

            return JsonUtility.FromJson<T>(
                $"{{\"items\":{json}}}");
        }

        private void RefreshLessonDropdown()
        {
            if (targetLessonDropdown == null)
                return;

            List<string> choices =
                new List<string>();

            foreach (ClassLessonOption lesson in classLessons)
                choices.Add(lesson.lessonTitle);

            targetLessonDropdown.choices = choices;

            if (choices.Count > 0)
            {
                if (targetLessonDropdown.index < 0 ||
                    targetLessonDropdown.index >= choices.Count)
                {
                    targetLessonDropdown.index = 0;
                }
            }
            else
            {
                targetLessonDropdown.value = string.Empty;
            }
        }

        private void SetUploadLoading(
            bool loading,
            string text = "Đang tải mô hình...")
        {
            isUploadingModel = loading;

            if (uploadProgressRow != null)
            {
                uploadProgressRow.style.display =
                    loading
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
            }

            if (uploadProgressLabel != null &&
                !string.IsNullOrWhiteSpace(text))
            {
                uploadProgressLabel.text = text;
            }

            if (confirmAddModelBtn != null)
                confirmAddModelBtn.SetEnabled(!loading);

            if (chooseModelFileBtn != null)
                chooseModelFileBtn.SetEnabled(!loading);

            if (targetLessonDropdown != null)
                targetLessonDropdown.SetEnabled(!loading);

            if (loading)
            {
                spinnerFrame = 0;

                if (uploadSpinnerLabel != null)
                {
                    uploadSpinnerLabel.text =
                        spinnerFrames[spinnerFrame];

                    spinnerSchedule?.Pause();

                    spinnerSchedule =
                        uploadSpinnerLabel.schedule
                            .Execute(() =>
                            {
                                spinnerFrame =
                                    (spinnerFrame + 1) %
                                    spinnerFrames.Length;

                                uploadSpinnerLabel.text =
                                    spinnerFrames[spinnerFrame];
                            })
                            .Every(120);
                }
            }
            else
            {
                spinnerSchedule?.Pause();
            }
        }

        private void FinishUploadWithError(string message)
        {
            SetUploadLoading(false);
            isUploadingModel = false;
            ShowAddModelError(message);
            Debug.LogError("[AR UI] Model upload failed: " + message);
        }

        private void ShowAddModelError(string message)
        {
            if (addModelErrorLabel == null)
                return;

            addModelErrorLabel.text = message ?? string.Empty;

            if (string.IsNullOrWhiteSpace(message))
                addModelErrorLabel.AddToClassList("hidden");
            else
                addModelErrorLabel.RemoveFromClassList("hidden");
        }

        private void ClearAddModelError()
        {
            ShowAddModelError(string.Empty);
        }

        private void PopulateModelList()
        {
            if (modelListContainer == null)
                return;

            modelListContainer.Clear();

            if (arController == null ||
                arController.ModelCount <= 0)
            {
                Label empty =
                    new Label(
                        "Lớp học này chưa có mô hình 3D.");

                empty.AddToClassList(
                    "model-empty-label");

                modelListContainer.Add(empty);
                return;
            }

            int currentIndex =
                arController.CurrentModelIndex;

            string currentLessonId =
                arController.GetModelLessonId(
                    currentIndex);

            List<LessonModelGroup> groups =
                BuildLessonGroups();

            if (!string.IsNullOrWhiteSpace(
                    currentLessonId))
            {
                expandedLessonIds.Add(
                    currentLessonId);
            }

            foreach (LessonModelGroup group in groups)
            {
                VisualElement lessonBlock =
                    new VisualElement();

                lessonBlock.AddToClassList(
                    "lesson-model-group");

                Button lessonHeader =
                    new Button();

                lessonHeader.AddToClassList(
                    "lesson-dropdown-header");

                VisualElement headerText =
                    new VisualElement();

                headerText.AddToClassList(
                    "lesson-dropdown-text");

                Label lessonTitle =
                    new Label(group.lessonTitle);

                lessonTitle.AddToClassList(
                    "lesson-dropdown-title");

                Label lessonCount =
                    new Label(
                        group.modelIndices.Count == 1
                            ? "1 mô hình"
                            : $"{group.modelIndices.Count} mô hình");

                lessonCount.AddToClassList(
                    "lesson-dropdown-count");

                headerText.Add(lessonTitle);
                headerText.Add(lessonCount);

                VisualElement arrow =
                    new VisualElement();

                arrow.AddToClassList(
                    "lesson-dropdown-arrow");

                bool initiallyExpanded =
                    expandedLessonIds.Contains(
                        group.lessonId);

                arrow.AddToClassList(
                    initiallyExpanded
                        ? "lesson-dropdown-arrow-up"
                        : "lesson-dropdown-arrow-down");

                lessonHeader.Add(headerText);
                lessonHeader.Add(arrow);
                lessonBlock.Add(lessonHeader);

                VisualElement modelContainer =
                    new VisualElement();

                modelContainer.AddToClassList(
                    "lesson-model-items");

                modelContainer.style.display =
                    initiallyExpanded
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;

                foreach (int modelIndex in group.modelIndices)
                {
                    modelContainer.Add(
                        CreateModelButton(
                            modelIndex,
                            currentIndex));
                }

                lessonBlock.Add(modelContainer);

                string capturedLessonId =
                    group.lessonId;

                lessonHeader.clicked += () =>
                {
                    bool willExpand;

                    if (expandedLessonIds.Contains(
                            capturedLessonId))
                    {
                        expandedLessonIds.Remove(
                            capturedLessonId);

                        willExpand = false;
                    }
                    else
                    {
                        expandedLessonIds.Add(
                            capturedLessonId);

                        willExpand = true;
                    }

                    modelContainer.style.display =
                        willExpand
                            ? DisplayStyle.Flex
                            : DisplayStyle.None;

                    arrow.RemoveFromClassList(
                        "lesson-dropdown-arrow-up");

                    arrow.RemoveFromClassList(
                        "lesson-dropdown-arrow-down");

                    arrow.AddToClassList(
                        willExpand
                            ? "lesson-dropdown-arrow-up"
                            : "lesson-dropdown-arrow-down");
                };

                modelListContainer.Add(
                    lessonBlock);
            }
        }

        private List<LessonModelGroup> BuildLessonGroups()
        {
            List<LessonModelGroup> groups =
                new List<LessonModelGroup>();

            Dictionary<string, LessonModelGroup> byLesson =
                new Dictionary<string, LessonModelGroup>();

            if (arController == null)
                return groups;

            for (int i = 0;
                 i < arController.ModelCount;
                 i++)
            {
                string lessonId =
                    arController.GetModelLessonId(i);

                if (string.IsNullOrWhiteSpace(lessonId))
                    lessonId = "lesson-" + i;

                if (!byLesson.TryGetValue(
                        lessonId,
                        out LessonModelGroup group))
                {
                    group =
                        new LessonModelGroup
                        {
                            lessonId = lessonId,
                            lessonTitle =
                                arController.GetModelLessonTitle(i),
                            chapterOrder =
                                arController.GetModelChapterOrder(i)
                        };

                    byLesson[lessonId] = group;
                    groups.Add(group);
                }

                group.modelIndices.Add(i);
            }

            groups.Sort(
                (a, b) =>
                {
                    int chapterCompare =
                        a.chapterOrder.CompareTo(
                            b.chapterOrder);

                    if (chapterCompare != 0)
                        return chapterCompare;

                    return string.Compare(
                        a.lessonTitle,
                        b.lessonTitle,
                        StringComparison.OrdinalIgnoreCase);
                });

            return groups;
        }

        private Button CreateModelButton(
            int modelIndex,
            int currentIndex)
        {
            string modelName =
                arController.GetModelName(
                    modelIndex);

            Button itemButton = new Button();
            itemButton.AddToClassList("model-item-btn");

            VisualElement textArea =
                new VisualElement();

            textArea.AddToClassList(
                "model-item-text-area");

            Label nameLabel =
                new Label(modelName);

            nameLabel.AddToClassList(
                "model-item-name");

            Label stateLabel =
                new Label();

            stateLabel.AddToClassList(
                "model-item-state");

            bool selected =
                modelIndex == currentIndex;

            stateLabel.text =
                selected
                    ? "Đang hiển thị"
                    : (arController.IsModelCached(modelIndex)
                        ? "Đã tải"
                        : "Nhấn để mở");

            textArea.Add(nameLabel);
            textArea.Add(stateLabel);
            itemButton.Add(textArea);

            if (selected)
                itemButton.AddToClassList(
                    "model-item-btn-selected");

            itemButton.clicked += () =>
            {
                if (arController == null ||
                    arController.IsLoadingModel)
                {
                    return;
                }

                if (modelIndex ==
                    arController.CurrentModelIndex)
                {
                    return;
                }

                arController.SpawnModelIndex(
                    modelIndex);

                PopulateModelList();
            };

            return itemButton;
        }

        private void OnModelListChanged()
        {
            PopulateModelList();
            UpdateModelListButtonState();
        }

        private void OnModelChanged(int index)
        {
            PopulateModelList();
        }

        private void OnLoadingStateChanged(
            bool loading,
            string message)
        {
            if (loading)
            {
                if (loadingLabel != null)
                {
                    loadingLabel.text =
                        string.IsNullOrWhiteSpace(message)
                            ? "Đang tải mô hình 3D..."
                            : message;
                }

                if (loadingPanel != null)
                    loadingPanel.RemoveFromClassList("hidden");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    HideLoadingPanel();
                }
                else
                {
                    if (loadingLabel != null)
                        loadingLabel.text = message;

                    if (loadingPanel != null)
                        loadingPanel.RemoveFromClassList("hidden");
                }

                PopulateModelList();
            }

            UpdateModelListButtonState();
        }

        private void HideLoadingPanel()
        {
            if (loadingPanel != null)
                loadingPanel.AddToClassList("hidden");
        }

        private void UpdateModelListButtonState()
        {
            if (modelListBtn != null)
            {
                modelListBtn.SetEnabled(
                    arController != null &&
                    arController.ModelCount > 0 &&
                    !arController.IsLoadingModel);
            }
        }
    }
}
