using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
[RequireComponent(typeof(SupabaseRuntimeRestService))]
public class CreateLessonPageController : MonoBehaviour
{
    private const int TotalSteps = 3;
    private const int MinimumObjectives = 2;
    private const long MaxPdfBytes = 25L * 1024L * 1024L;
    private const long MaxModelBytes = 100L * 1024L * 1024L;

    [Header("Services")]
    [SerializeField] private SupabaseLessonService lessonService;
    [SerializeField] private CloudflareR2StorageService r2StorageService;
    [SerializeField] private SupabaseQuizService quizService; // <-- Đã thêm QuizService

    private SupabaseRuntimeRestService runtimeRestService;

    private VisualElement root;
    private int currentStep = 1;
    private bool isSaving;
    private bool isUpdateMode;
    private string editingLessonId = string.Empty;

    private readonly List<ExistingAssetData> existingAssets = new();
    private readonly HashSet<string> removedExistingAssetIds = new();

    private Button backButton;
    private Button cancelButton;
    private Button nextButton;
    private Label stepLabel;
    private Label pageTitleLabel;
    private Label nextButtonLabel;
    private VisualElement nextButtonIcon;

    private VisualElement progressStep1;
    private VisualElement progressStep2;
    private VisualElement progressStep3;

    private ScrollView formatStep;
    private ScrollView assetStep;
    private ScrollView detailsStep;

    private DropdownField chapterDropdown;
    private Button videoFormatButton;
    private Button modelFormatButton;
    private Button documentFormatButton;
    private Button selectAllFormatsButton;
    private VisualElement videoRadio;
    private VisualElement modelRadio;
    private VisualElement documentRadio;
    private Label formatErrorLabel;

    private VisualElement videoUploadCard;
    private VisualElement documentUploadCard;
    private VisualElement exerciseUploadCard;
    private VisualElement modelUploadCard;
    private TextField youtubeUrlField;
    private Button confirmVideoLinkButton;
    private Label videoLinkStatusLabel;
    private Button uploadDocumentsButton;
    private Button uploadExerciseButton;
    private Button uploadModelButton;
    private VisualElement exerciseFileRow;
    private VisualElement modelFileRow;
    private Label exerciseFileLabel;
    private Label modelFileLabel;
    private Button quizDeadlineDateButton;
    private Button quizDeadlineTimeButton;
    private Label quizDeadlineDateLabel;
    private Label quizDeadlineTimeLabel;
    private VisualElement quizCalendarPopup;
    private Label quizCalendarMonthLabel;
    private VisualElement quizCalendarWeekdays;
    private VisualElement quizCalendarDays;
    private Button quizCalendarPreviousButton;
    private Button quizCalendarNextButton;
    private VisualElement quizTimePopup;
    private ScrollView quizHourScroll;
    private ScrollView quizMinuteScroll;
    private Button quizTimeDoneButton;
    private string quizDeadlineDateValue = string.Empty;
    private string quizDeadlineTimeValue = string.Empty;
    private DateTime calendarDisplayMonth;
    private int selectedDeadlineHour = 23;
    private int selectedDeadlineMinute = 59;
    private readonly List<Button> deadlineHourButtons = new();
    private readonly List<Button> deadlineMinuteButtons = new();
    private Button removeExerciseButton;
    private Button removeModelButton;
    private VisualElement documentChipContainer;
    private Label documentPickerStatusLabel;
    private Label assetErrorLabel;

    private TextField lessonTitleField;
    private TextField lessonDescriptionField;
    private Button generateAiButton;
    private VisualElement objectivesContainer;
    private Button addObjectiveButton;
    private Label detailsErrorLabel;

    private VisualElement saveProgressContainer;
    private ProgressBar saveProgressBar;
    private Label saveProgressLabel;

    private bool videoSelected;
    private bool modelSelected;
    private bool documentSelected;

    private string selectedYoutubeUrl = string.Empty;
    private string selectedExercisePath = string.Empty;
    private string selectedModelPath = string.Empty;
    private string selectedChapterId = string.Empty;
    private string existingQuizId = string.Empty;

    private readonly List<string> selectedDocumentPaths = new();
    private readonly List<TextField> objectiveFields = new();
    private readonly List<ChapterRecord> loadedChapters = new();

    // Keep original UI text so switching languages is reversible, without touching user input.
    private readonly Dictionary<VisualElement, string> staticTexts = new();
    private static readonly Dictionary<string, string> VietnameseText = new(StringComparer.Ordinal)
    {
            { "Cancel", "Hủy" },
            { "CHAPTER", "CHƯƠNG" },
            { "CLASS NAME", "TÊN BÀI HỌC" },
            { "LESSON FORMAT", "ĐỊNH DẠNG BÀI HỌC" },
            { "Select all that apply", "Chọn tất cả" },
            { "A lesson can combine multiple formats.", "Một bài học có thể kết hợp nhiều định dạng." },
            { "Video Lecture", "Bài giảng video" },
            { "Upload or link a recorded video lesson", "Tải lên hoặc liên kết video bài giảng" },
            { "3D Interactive", "Tương tác 3D" },
            { "Attach a 3D model or simulation", "Đính kèm mô hình 3D hoặc mô phỏng" },
            { "Document", "Tài liệu" },
            { "PDF reading, slides, or study notes", "Tài liệu PDF, slide hoặc ghi chú học tập" },
            { "Upload assets for each format selected. You can skip blocks that don't apply.", "Tải tài nguyên cho từng định dạng đã chọn. Có thể bỏ qua mục không áp dụng." },
            { "Paste one YouTube video URL", "Dán một liên kết video YouTube" },
            { "Confirm YouTube Link", "Xác nhận liên kết YouTube" },
            { "Documents", "Tài liệu" },
            { "Upload one or more PDF documents", "Tải lên một hoặc nhiều tài liệu PDF" },
            { "Select PDF Documents", "Chọn tài liệu PDF" },
            { "Exercise PDF", "PDF bài tập" },
            { "Upload one exercise PDF file (Max 1 file)", "Tải lên một PDF bài tập (tối đa 1 tệp)" },
            { "No exercise PDF selected", "Chưa chọn PDF bài tập" },
            { "Select Exercise PDF", "Chọn PDF bài tập" },
            { "QUIZ DEADLINE", "HẠN NỘP QUIZ" },
            { "Use local time. Example: 2026-10-30 at 23:59", "Dùng giờ địa phương. Ví dụ: 2026-10-30 lúc 23:59" },
            { "Please enter the quiz deadline date and time.", "Vui lòng nhập ngày và giờ hết hạn của quiz." },
            { "Quiz deadline must use YYYY-MM-DD and HH:mm.", "Hạn nộp quiz phải có định dạng YYYY-MM-DD và HH:mm." },
            { "Quiz deadline must be in the future.", "Hạn nộp quiz phải ở thời điểm tương lai." },
            { "Choose time", "Chọn giờ" },
            { "Done", "Xong" },
            { "3D Interactive Model", "Mô hình 3D tương tác" },
            { "Upload one GLB model (Max 1 file)", "Tải lên một mô hình GLB (tối đa 1 tệp)" },
            { "No 3D asset selected", "Chưa chọn mô hình 3D" },
            { "Select GLB Model", "Chọn mô hình GLB" },
            { "Generate Description & Objectives with AI", "Tạo mô tả và mục tiêu bằng AI" },
            { "LESSON DESCRIPTION", "MÔ TẢ BÀI HỌC" },
            { "LEARNING OBJECTIVES", "MỤC TIÊU HỌC TẬP" },
            { "Add New Objective", "Thêm mục tiêu mới" },
            { "Save Draft", "Lưu bản nháp" },
            { "Next", "Tiếp theo" },
            { "Finish", "Hoàn tất" },
            { "Save & Publish", "Lưu và xuất bản" },
            { "Step 1 of 3", "Bước 1/3" },
            { "Location & Format", "Vị trí và định dạng" },
            { "Asset Upload", "Tải tài nguyên lên" },
            { "Lesson Details", "Chi tiết bài học" },
            { "Create Lesson", "Tạo bài học" },
            { "Loading chapters...", "Đang tải chương..." },
            { "No chapter available", "Chưa có chương nào" },
            { "Please select a valid chapter.", "Vui lòng chọn chương hợp lệ." },
            { "Please enter the lesson name.", "Vui lòng nhập tên bài học." },
            { "Please select at least one lesson format.", "Vui lòng chọn ít nhất một định dạng bài học." },
            { "Please enter a valid YouTube URL.", "Vui lòng nhập đường dẫn YouTube hợp lệ." },
            { "Invalid YouTube URL.", "Đường dẫn YouTube không hợp lệ." },
            { "YouTube link confirmed.", "Đã xác nhận liên kết YouTube." },
            { "Please select at least one PDF document.", "Vui lòng chọn ít nhất một tài liệu PDF." },
            { "Please select one GLB model.", "Vui lòng chọn một mô hình GLB." },
            { "Please return to Step 1 and enter the lesson name.", "Vui lòng quay lại bước 1 và nhập tên bài học." },
            { "Please enter at least one learning objective.", "Vui lòng nhập ít nhất một mục tiêu học tập." },
            { "Enter a lesson title before generating content.", "Nhập tên bài học trước khi tạo nội dung." },
            { "Preparing lesson...", "Đang chuẩn bị bài học..." },
            { "Updating lesson...", "Đang cập nhật bài học..." },
            { "Lesson updated.", "Đã cập nhật bài học." },
            { "Creating lesson record...", "Đang tạo bài học..." },
            { "Saving learning objectives...", "Đang lưu mục tiêu học tập..." },
            { "Draft saved.", "Đã lưu bản nháp." },
            { "Lesson published.", "Đã xuất bản bài học." },
            { "The lesson could not be found.", "Không tìm thấy bài học." },
            { "The lesson selected for editing is invalid.", "Bài học được chọn để chỉnh sửa không hợp lệ." },
            { "No class selected. Please reopen this page from Class Detail.", "Chưa chọn lớp học. Hãy mở lại từ trang chi tiết lớp học." },
            { "This class has no chapter. Create a chapter first.", "Lớp chưa có chương. Hãy tạo chương trước." },
            { "Android file picker is not installed yet.", "Chưa cài đặt bộ chọn tệp Android." },
            { "The selected file does not exist.", "Không tìm thấy tệp đã chọn." },
            { "Missing class, teacher, or chapter information.", "Thiếu thông tin lớp, giáo viên hoặc chương." },
            { "Cannot create lesson.", "Không thể tạo bài học." },
            { "Students will be able to...", "Học sinh có thể..." },
            { "SupabaseLessonService is missing.", "Thiếu SupabaseLessonService." },
            { "SupabaseRuntimeRestService is missing.", "Thiếu SupabaseRuntimeRestService." },
            { "CloudflareR2StorageService is missing.", "Thiếu CloudflareR2StorageService." },
            { "selected_class_id is not a valid UUID.", "Mã lớp học không hợp lệ." },
            { "teacher_id is not a valid UUID.", "Mã giáo viên không hợp lệ." },
            { "selected_chapter_id is not a valid UUID.", "Mã chương không hợp lệ." },
            { "Describe what this lesson is about, its structure, and any prerequisites students should know before starting...", "Mô tả nội dung, cấu trúc và kiến thức cần có trước khi học..." },
    };

    private static string T(string english, string vietnamese) =>
        AppLanguageManager.IsVietnamese ? vietnamese : english;

    private static string L(string english)
    {
        if (!AppLanguageManager.IsVietnamese || string.IsNullOrEmpty(english)) return english;
        if (VietnameseText.TryGetValue(english, out string translated)) return translated;
        if (english.StartsWith("Uploading PDF ") && english.EndsWith(" to R2..."))
            return english.Replace("Uploading PDF ", "Đang tải PDF ").Replace(" of ", "/").Replace(" to R2...", " lên R2...");
        return english;
    }

    private void CaptureStaticTexts()
    {
        staticTexts.Clear();
        CaptureStaticTextsRecursive(root);
    }

    private void CaptureStaticTextsRecursive(VisualElement element)
    {
        if (element is Label label && !string.IsNullOrEmpty(label.text))
            staticTexts[element] = label.text;
        else if (element is Button button && !string.IsNullOrEmpty(button.text))
            staticTexts[element] = button.text;
        foreach (VisualElement child in element.Children()) CaptureStaticTextsRecursive(child);
    }

    private void OnLanguageChanged(string language) => ApplyCurrentLanguage();

    private void ApplyCurrentLanguage()
    {
        if (root == null) return;
        foreach (var entry in staticTexts)
        {
            if (entry.Key is Label label) label.text = L(entry.Value);
            else if (entry.Key is Button button) button.text = L(entry.Value);
        }
        if (lessonTitleField != null) lessonTitleField.textEdition.placeholder = T("Enter lesson name", "Nhập tên bài học");
        if (lessonDescriptionField != null) lessonDescriptionField.textEdition.placeholder = T("Describe what this lesson is about, its structure, and any prerequisites students should know before starting...", "Mô tả nội dung, cấu trúc và kiến thức cần có trước khi học...");
        RefreshDeadlineLabels();
        BuildCalendarWeekdays();
        BuildCalendarDays();
        if (cancelButton != null) cancelButton.text = L("Cancel");
        foreach (TextField field in objectiveFields) field.tooltip = L("Students will be able to...");
        TranslateVisibleMessage(formatErrorLabel);
        TranslateVisibleMessage(assetErrorLabel);
        TranslateVisibleMessage(detailsErrorLabel);
        TranslateVisibleMessage(saveProgressLabel);
        if (exerciseFileLabel != null && (exerciseFileLabel.text == "No exercise PDF selected" || exerciseFileLabel.text == "Chưa chọn PDF bài tập"))
            exerciseFileLabel.text = L("No exercise PDF selected");
        if (modelFileLabel != null && (modelFileLabel.text == "No 3D asset selected" || modelFileLabel.text == "Chưa chọn mô hình 3D"))
            modelFileLabel.text = L("No 3D asset selected");
        if (videoLinkStatusLabel != null && !string.IsNullOrEmpty(videoLinkStatusLabel.text))
            videoLinkStatusLabel.text = AppLanguageManager.IsVietnamese ? L(videoLinkStatusLabel.text) : EnglishForVietnamese(videoLinkStatusLabel.text);
        UpdateChapterDropdownLabels();
        UpdateHeader();
        UpdateBottomActions();
    }

    private static void TranslateVisibleMessage(Label label)
    {
        if (label == null || string.IsNullOrEmpty(label.text)) return;
        string english = EnglishForVietnamese(label.text);
        label.text = L(english);
    }

    private static string EnglishForVietnamese(string value)
    {
        foreach (var pair in VietnameseText) if (pair.Value == value) return pair.Key;
        return value;
    }

    private string ChapterDisplay(ChapterRecord chapter)
    {
        int order = chapter.chapter_order > 0 ? chapter.chapter_order : loadedChapters.IndexOf(chapter) + 1;
        string name = chapter.title?.Trim() ?? string.Empty;
        // Default chapter titles already include their number: do not print Chapter 1 – Chương 1.
        if (name == $"Chapter {order}" || name == $"Chương {order}" || name == $"New Chapter {order}")
            return T($"Chapter {order}", $"Chương {order}");
        return string.IsNullOrEmpty(name) ? T($"Chapter {order}", $"Chương {order}") : name;
    }

    private void UpdateChapterDropdownLabels()
    {
        if (chapterDropdown == null || loadedChapters.Count == 0) return;
        int selectedIndex = loadedChapters.FindIndex(ch => ch.id == selectedChapterId);
        if (selectedIndex < 0) selectedIndex = 0;
        List<string> choices = new();
        foreach (ChapterRecord chapter in loadedChapters) choices.Add(ChapterDisplay(chapter));
        chapterDropdown.choices = choices;
        chapterDropdown.SetValueWithoutNotify(choices[selectedIndex]);
    }

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();
        if (document == null)
        {
            Debug.LogError("CreateLessonScene không tìm thấy UIDocument.");
            return;
        }

        root = document.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("rootVisualElement của CreateLessonScene đang null.");
            return;
        }

        ResolveServices();
        QueryElements();
        InitializeDeadlinePickers();
        RegisterEvents();
        AppLanguageManager.LanguageChanged += OnLanguageChanged;
        CaptureStaticTexts();
        isUpdateMode = string.Equals(
            PlayerPrefs.GetString("lesson_editor_mode", "create"),
            "update",
            StringComparison.OrdinalIgnoreCase
        );

        editingLessonId = PlayerPrefs.GetString(
            "selected_lesson_id",
            string.Empty
        );

        BuildInitialObjectives();
        ApplyCurrentLanguage();
        ShowStep(1);
        StartCoroutine(InitializeEditorRoutine());
    }

    private void OnDisable()
    {
        AppLanguageManager.LanguageChanged -= OnLanguageChanged;
        UnregisterEvents();
        staticTexts.Clear();
    }

    private void ResolveServices()
    {
        if (lessonService == null)
            lessonService = GetComponent<SupabaseLessonService>();

        if (r2StorageService == null)
            r2StorageService = GetComponent<CloudflareR2StorageService>();

        if (quizService == null)
            quizService = GetComponent<SupabaseQuizService>(); // Tự động tìm SupabaseQuizService

        runtimeRestService = GetComponent<SupabaseRuntimeRestService>();

        if (lessonService == null)
            Debug.LogError("Thiếu SupabaseLessonService trên CreateLessonUIDocument.");

        if (r2StorageService == null)
            Debug.LogError("Thiếu CloudflareR2StorageService trên CreateLessonUIDocument.");

        if (quizService == null)
            Debug.LogWarning("Thiếu SupabaseQuizService trên CreateLessonUIDocument.");
    }

    private void QueryElements()
    {
        backButton = root.Q<Button>("back-button");
        cancelButton = root.Q<Button>("cancel-button");
        nextButton = root.Q<Button>("next-button");
        stepLabel = root.Q<Label>("step-label");
        pageTitleLabel = root.Q<Label>("page-title-label");
        nextButtonLabel = root.Q<Label>("next-button-label");
        nextButtonIcon = root.Q<VisualElement>("next-button-icon");

        progressStep1 = root.Q<VisualElement>("progress-step-1");
        progressStep2 = root.Q<VisualElement>("progress-step-2");
        progressStep3 = root.Q<VisualElement>("progress-step-3");

        formatStep = root.Q<ScrollView>("format-step");
        assetStep = root.Q<ScrollView>("asset-step");
        detailsStep = root.Q<ScrollView>("details-step");

        chapterDropdown = root.Q<DropdownField>("chapter-dropdown");
        videoFormatButton = root.Q<Button>("video-format-button");
        modelFormatButton = root.Q<Button>("model-format-button");
        documentFormatButton = root.Q<Button>("document-format-button");
        selectAllFormatsButton = root.Q<Button>("select-all-formats-button");
        videoRadio = root.Q<VisualElement>("video-radio");
        modelRadio = root.Q<VisualElement>("model-radio");
        documentRadio = root.Q<VisualElement>("document-radio");
        formatErrorLabel = root.Q<Label>("format-error-label");

        videoUploadCard = root.Q<VisualElement>("video-upload-card");
        documentUploadCard = root.Q<VisualElement>("document-upload-card");
        exerciseUploadCard = root.Q<VisualElement>("exercise-upload-card");
        modelUploadCard = root.Q<VisualElement>("model-upload-card");
        youtubeUrlField = root.Q<TextField>("youtube-url-field");
        confirmVideoLinkButton = root.Q<Button>("confirm-video-link-button");
        videoLinkStatusLabel = root.Q<Label>("video-link-status-label");
        uploadDocumentsButton = root.Q<Button>("upload-documents-button");
        uploadExerciseButton = root.Q<Button>("upload-exercise-button");
        uploadModelButton = root.Q<Button>("upload-model-button");
        exerciseFileRow = root.Q<VisualElement>("exercise-file-row");
        modelFileRow = root.Q<VisualElement>("model-file-row");
        exerciseFileLabel = root.Q<Label>("exercise-file-label");
        modelFileLabel = root.Q<Label>("model-file-label");
        quizDeadlineDateButton = root.Q<Button>("quiz-deadline-date-button");
        quizDeadlineTimeButton = root.Q<Button>("quiz-deadline-time-button");
        quizDeadlineDateLabel = root.Q<Label>("quiz-deadline-date-label");
        quizDeadlineTimeLabel = root.Q<Label>("quiz-deadline-time-label");
        quizCalendarPopup = root.Q<VisualElement>("quiz-calendar-popup");
        quizCalendarMonthLabel = root.Q<Label>("quiz-calendar-month-label");
        quizCalendarWeekdays = root.Q<VisualElement>("quiz-calendar-weekdays");
        quizCalendarDays = root.Q<VisualElement>("quiz-calendar-days");
        quizCalendarPreviousButton = root.Q<Button>("quiz-calendar-previous-button");
        quizCalendarNextButton = root.Q<Button>("quiz-calendar-next-button");
        quizTimePopup = root.Q<VisualElement>("quiz-time-popup");
        quizHourScroll = root.Q<ScrollView>("quiz-hour-scroll");
        quizMinuteScroll = root.Q<ScrollView>("quiz-minute-scroll");
        quizTimeDoneButton = root.Q<Button>("quiz-time-done-button");
        removeExerciseButton = root.Q<Button>("remove-exercise-button");
        removeModelButton = root.Q<Button>("remove-model-button");
        documentChipContainer = root.Q<VisualElement>("document-chip-container");
        documentPickerStatusLabel = root.Q<Label>("document-picker-status-label");
        assetErrorLabel = root.Q<Label>("asset-error-label");

        lessonTitleField = root.Q<TextField>("lesson-title-field");
        lessonDescriptionField = root.Q<TextField>("lesson-description-field");
        generateAiButton = root.Q<Button>("generate-ai-button");
        objectivesContainer = root.Q<VisualElement>("objectives-container");
        addObjectiveButton = root.Q<Button>("add-objective-button");
        detailsErrorLabel = root.Q<Label>("details-error-label");

        saveProgressContainer = root.Q<VisualElement>("save-progress-container");
        saveProgressBar = root.Q<ProgressBar>("save-progress-bar");
        saveProgressLabel = root.Q<Label>("save-progress-label");
    }

    private void RegisterEvents()
    {
        if (backButton != null) backButton.clicked += HandleBack;
        if (cancelButton != null) cancelButton.clicked += HandleCancel;
        if (nextButton != null) nextButton.clicked += HandleNext;

        if (videoFormatButton != null) videoFormatButton.clicked += ToggleVideoFormat;
        if (modelFormatButton != null) modelFormatButton.clicked += ToggleModelFormat;
        if (documentFormatButton != null) documentFormatButton.clicked += ToggleDocumentFormat;
        if (selectAllFormatsButton != null) selectAllFormatsButton.clicked += SelectAllFormats;

        if (confirmVideoLinkButton != null) confirmVideoLinkButton.clicked += ConfirmYoutubeLink;
        if (uploadDocumentsButton != null) uploadDocumentsButton.clicked += PickDocuments;
        if (uploadExerciseButton != null) uploadExerciseButton.clicked += PickExercisePdf;
        if (uploadModelButton != null) uploadModelButton.clicked += PickModel;
        if (removeExerciseButton != null) removeExerciseButton.clicked += RemoveExercisePdf;
        if (removeModelButton != null) removeModelButton.clicked += RemoveModel;
        if (quizDeadlineDateButton != null) quizDeadlineDateButton.clicked += ToggleCalendarPicker;
        if (quizDeadlineTimeButton != null) quizDeadlineTimeButton.clicked += ToggleTimePicker;
        if (quizCalendarPreviousButton != null) quizCalendarPreviousButton.clicked += ShowPreviousCalendarMonth;
        if (quizCalendarNextButton != null) quizCalendarNextButton.clicked += ShowNextCalendarMonth;
        if (quizTimeDoneButton != null) quizTimeDoneButton.clicked += ConfirmTimeSelection;

        if (generateAiButton != null) generateAiButton.clicked += HandleGenerateWithAi;
        if (addObjectiveButton != null) addObjectiveButton.clicked += AddObjective;

        if (chapterDropdown != null)
            chapterDropdown.RegisterValueChangedCallback(HandleChapterChanged);

        root?.RegisterCallback<PointerDownEvent>(HandleRootPointerDown, TrickleDown.TrickleDown);
    }

    private void UnregisterEvents()
    {
        if (backButton != null) backButton.clicked -= HandleBack;
        if (cancelButton != null) cancelButton.clicked -= HandleCancel;
        if (nextButton != null) nextButton.clicked -= HandleNext;

        if (videoFormatButton != null) videoFormatButton.clicked -= ToggleVideoFormat;
        if (modelFormatButton != null) modelFormatButton.clicked -= ToggleModelFormat;
        if (documentFormatButton != null) documentFormatButton.clicked -= ToggleDocumentFormat;
        if (selectAllFormatsButton != null) selectAllFormatsButton.clicked -= SelectAllFormats;

        if (confirmVideoLinkButton != null) confirmVideoLinkButton.clicked -= ConfirmYoutubeLink;
        if (uploadDocumentsButton != null) uploadDocumentsButton.clicked -= PickDocuments;
        if (uploadExerciseButton != null) uploadExerciseButton.clicked -= PickExercisePdf;
        if (uploadModelButton != null) uploadModelButton.clicked -= PickModel;
        if (removeExerciseButton != null) removeExerciseButton.clicked -= RemoveExercisePdf;
        if (removeModelButton != null) removeModelButton.clicked -= RemoveModel;
        if (quizDeadlineDateButton != null) quizDeadlineDateButton.clicked -= ToggleCalendarPicker;
        if (quizDeadlineTimeButton != null) quizDeadlineTimeButton.clicked -= ToggleTimePicker;
        if (quizCalendarPreviousButton != null) quizCalendarPreviousButton.clicked -= ShowPreviousCalendarMonth;
        if (quizCalendarNextButton != null) quizCalendarNextButton.clicked -= ShowNextCalendarMonth;
        if (quizTimeDoneButton != null) quizTimeDoneButton.clicked -= ConfirmTimeSelection;

        if (generateAiButton != null) generateAiButton.clicked -= HandleGenerateWithAi;
        if (addObjectiveButton != null) addObjectiveButton.clicked -= AddObjective;

        if (chapterDropdown != null)
            chapterDropdown.UnregisterValueChangedCallback(HandleChapterChanged);

        root?.UnregisterCallback<PointerDownEvent>(HandleRootPointerDown, TrickleDown.TrickleDown);
    }

    private IEnumerator InitializeEditorRoutine()
    {
        yield return LoadChaptersRoutine();

        if (isUpdateMode)
        {
            if (!Guid.TryParse(editingLessonId, out _))
            {
                SetLabel(formatErrorLabel, "The lesson selected for editing is invalid.");
                yield break;
            }

            yield return LoadLessonForUpdateRoutine();
        }

        UpdateBottomActions();
    }

    private IEnumerator LoadChaptersRoutine()
    {
        string classId = PlayerPrefs.GetString("selected_class_id", string.Empty);

        if (string.IsNullOrWhiteSpace(classId))
        {
            SetLabel(formatErrorLabel, "No class selected. Please reopen this page from Class Detail.");
            yield break;
        }

        string passedChapterId = PlayerPrefs.GetString("selected_chapter_id", string.Empty);
        string passedChapterTitle = PlayerPrefs.GetString("selected_chapter_title", string.Empty);
        int passedChapterOrder = PlayerPrefs.GetInt("selected_chapter_order", 0);

        if (!string.IsNullOrWhiteSpace(passedChapterId))
        {
            selectedChapterId = passedChapterId;

            loadedChapters.Clear();
            loadedChapters.Add(new ChapterRecord
            {
                id = passedChapterId,
                class_id = classId,
                title = string.IsNullOrWhiteSpace(passedChapterTitle)
                    ? T($"Chapter {Mathf.Max(1, passedChapterOrder)}", $"Chương {Mathf.Max(1, passedChapterOrder)}")
                    : passedChapterTitle,
                chapter_order = Mathf.Max(1, passedChapterOrder)
            });

            if (chapterDropdown != null)
            {
                string label = ChapterDisplay(loadedChapters[0]);
                chapterDropdown.choices = new List<string> { label };
                chapterDropdown.index = 0;
                chapterDropdown.SetEnabled(false);
            }

            ClearLabel(formatErrorLabel);
            yield break;
        }

        if (lessonService == null)
        {
            SetLabel(formatErrorLabel, "SupabaseLessonService is missing.");
            yield break;
        }

        if (chapterDropdown != null)
        {
            chapterDropdown.choices = new List<string> { L("Loading chapters...") };
            chapterDropdown.index = 0;
            chapterDropdown.SetEnabled(false);
        }

        List<ChapterRecord> result = null;
        string error = null;

        yield return lessonService.GetChaptersByClass(
            classId,
            chapters => result = chapters,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            SetLabel(formatErrorLabel, error);
            yield break;
        }

        loadedChapters.Clear();
        if (result != null) loadedChapters.AddRange(result);

        if (loadedChapters.Count == 0)
        {
            if (chapterDropdown != null)
            {
                chapterDropdown.choices = new List<string> { L("No chapter available") };
                chapterDropdown.index = 0;
                chapterDropdown.SetEnabled(false);
            }

            SetLabel(formatErrorLabel, "This class has no chapter. Create a chapter first.");
            yield break;
        }

        List<string> labels = new();
        foreach (ChapterRecord chapter in loadedChapters)
        {
            int order = chapter.chapter_order <= 0 ? labels.Count + 1 : chapter.chapter_order;
            labels.Add(ChapterDisplay(chapter));
        }

        if (chapterDropdown != null)
        {
            chapterDropdown.choices = labels;
            chapterDropdown.index = 0;
            chapterDropdown.SetEnabled(true);
        }

        selectedChapterId = loadedChapters[0].id;
        ClearLabel(formatErrorLabel);
    }

    private void HandleChapterChanged(ChangeEvent<string> evt)
    {
        if (chapterDropdown == null) return;
        int index = chapterDropdown.index;
        if (index >= 0 && index < loadedChapters.Count)
            selectedChapterId = loadedChapters[index].id;
    }

    private void ToggleVideoFormat()
    {
        videoSelected = !videoSelected;
        UpdateFormatVisual(videoFormatButton, videoRadio, videoSelected);
        ClearLabel(formatErrorLabel);
    }

    private void ToggleModelFormat()
    {
        modelSelected = !modelSelected;
        UpdateFormatVisual(modelFormatButton, modelRadio, modelSelected);
        ClearLabel(formatErrorLabel);
    }

    private void ToggleDocumentFormat()
    {
        documentSelected = !documentSelected;
        UpdateFormatVisual(documentFormatButton, documentRadio, documentSelected);
        ClearLabel(formatErrorLabel);
    }

    private void SelectAllFormats()
    {
        videoSelected = true;
        modelSelected = true;
        documentSelected = true;

        UpdateFormatVisual(videoFormatButton, videoRadio, true);
        UpdateFormatVisual(modelFormatButton, modelRadio, true);
        UpdateFormatVisual(documentFormatButton, documentRadio, true);
        ClearLabel(formatErrorLabel);
    }

    private static void UpdateFormatVisual(Button card, VisualElement radio, bool selected)
    {
        card?.EnableInClassList("format-card-selected", selected);
        radio?.EnableInClassList("format-radio-selected", selected);
    }

    private void HandleNext()
    {
        if (isSaving) return;

        if (currentStep == 1)
        {
            if (!ValidateFormats()) return;
            UpdateUploadCards();
            ShowStep(2);
            return;
        }

        if (currentStep == 2)
        {
            if (!ValidateAssets()) return;
            ShowStep(3);
            return;
        }

        if (!ValidateDetails()) return;

        if (isUpdateMode)
            StartCoroutine(UpdateLessonRoutine());
        else
            SaveLesson();
    }

    private void HandleBack()
    {
        if (isSaving) return;

        if (currentStep > 1)
        {
            ShowStep(currentStep - 1);
            return;
        }

        ReturnToClassDetail();
    }

    private void HandleCancel()
    {
        if (isSaving) return;
        ClearUnsavedData();
        ReturnToClassDetail();
    }

    private bool ValidateFormats()
    {
        if (string.IsNullOrWhiteSpace(selectedChapterId))
        {
            SetLabel(formatErrorLabel, "Please select a valid chapter.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(lessonTitleField?.value))
        {
            SetLabel(formatErrorLabel, "Please enter the lesson name.");
            lessonTitleField?.Focus();
            return false;
        }

        if (videoSelected || modelSelected || documentSelected)
        {
            ClearLabel(formatErrorLabel);
            return true;
        }

        SetLabel(formatErrorLabel, "Please select at least one lesson format.");
        return false;
    }

    private bool ValidateAssets()
    {
        ClearLabel(assetErrorLabel);

        if (videoSelected)
        {
            string url = youtubeUrlField?.value?.Trim() ?? string.Empty;
            if (!IsValidYoutubeUrl(url))
            {
                SetLabel(assetErrorLabel, "Please enter a valid YouTube URL.");
                SetVideoStatus("Invalid YouTube URL.", true);
                youtubeUrlField?.Focus();
                return false;
            }
            selectedYoutubeUrl = url;
        }

        bool hasExistingDocument = existingAssets.Exists(asset => asset.asset_type == "document" && !removedExistingAssetIds.Contains(asset.id));

        if (documentSelected && selectedDocumentPaths.Count == 0 && !hasExistingDocument)
        {
            SetLabel(assetErrorLabel, "Please select at least one PDF document.");
            return false;
        }

        bool hasExistingModel = existingAssets.Exists(asset => asset.asset_type == "model_3d" && !removedExistingAssetIds.Contains(asset.id));

        if (modelSelected && string.IsNullOrWhiteSpace(selectedModelPath) && !hasExistingModel)
        {
            SetLabel(assetErrorLabel, "Please select one GLB model.");
            return false;
        }

        bool hasExistingQuiz = existingAssets.Exists(
            asset => asset.asset_type == "quiz_pdf" &&
                     !removedExistingAssetIds.Contains(asset.id)
        );

        if ((!string.IsNullOrWhiteSpace(selectedExercisePath) || hasExistingQuiz) &&
            !TryGetQuizDeadlineUtc(out _, out string deadlineError))
        {
            SetLabel(assetErrorLabel, deadlineError);
            quizDeadlineDateButton?.Focus();
            return false;
        }

        return true;
    }

    private bool TryGetQuizDeadlineUtc(out string utcIso, out string error)
    {
        utcIso = string.Empty;
        error = string.Empty;

        string date = quizDeadlineDateValue;
        string time = quizDeadlineTimeValue;

        if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time))
        {
            error = "Please enter the quiz deadline date and time.";
            return false;
        }

        if (!DateTime.TryParseExact(
                date + " " + time,
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime localDeadline))
        {
            error = "Quiz deadline must use YYYY-MM-DD and HH:mm.";
            return false;
        }

        localDeadline = DateTime.SpecifyKind(localDeadline, DateTimeKind.Local);

        if (localDeadline <= DateTime.Now)
        {
            error = "Quiz deadline must be in the future.";
            return false;
        }

        utcIso = localDeadline.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        return true;
    }

    private void InitializeDeadlinePickers()
    {
        DateTime today = DateTime.Today;
        calendarDisplayMonth = new DateTime(today.Year, today.Month, 1);

        BuildCalendarWeekdays();
        BuildCalendarDays();
        BuildTimeOptions();
        RefreshDeadlineLabels();

        quizCalendarPopup?.AddToClassList("hidden");
        quizTimePopup?.AddToClassList("hidden");
    }

    private void ToggleCalendarPicker()
    {
        bool willOpen = quizCalendarPopup != null &&
                        quizCalendarPopup.ClassListContains("hidden");

        quizTimePopup?.AddToClassList("hidden");

        if (quizCalendarPopup == null) return;
        if (willOpen)
        {
            BuildCalendarDays();
            quizCalendarPopup.RemoveFromClassList("hidden");
        }
        else
        {
            quizCalendarPopup.AddToClassList("hidden");
        }
    }

    private void ToggleTimePicker()
    {
        bool willOpen = quizTimePopup != null &&
                        quizTimePopup.ClassListContains("hidden");

        quizCalendarPopup?.AddToClassList("hidden");

        if (quizTimePopup == null) return;
        if (willOpen)
        {
            RefreshTimeOptionStyles();
            quizTimePopup.RemoveFromClassList("hidden");
            ScrollToSelectedTime();
        }
        else
        {
            quizTimePopup.AddToClassList("hidden");
        }
    }

    private void ShowPreviousCalendarMonth()
    {
        calendarDisplayMonth = calendarDisplayMonth.AddMonths(-1);
        BuildCalendarDays();
    }

    private void ShowNextCalendarMonth()
    {
        calendarDisplayMonth = calendarDisplayMonth.AddMonths(1);
        BuildCalendarDays();
    }

    private void BuildCalendarWeekdays()
    {
        if (quizCalendarWeekdays == null) return;
        quizCalendarWeekdays.Clear();

        string[] english = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        string[] vietnamese = { "CN", "T2", "T3", "T4", "T5", "T6", "T7" };
        string[] values = AppLanguageManager.IsVietnamese ? vietnamese : english;

        foreach (string value in values)
        {
            VisualElement cell = new VisualElement();
            cell.AddToClassList("quiz-calendar-cell");
            Label label = new Label(value);
            label.AddToClassList("quiz-calendar-weekday");
            cell.Add(label);
            quizCalendarWeekdays.Add(cell);
        }
    }

    private void BuildCalendarDays()
    {
        if (quizCalendarDays == null) return;
        quizCalendarDays.Clear();

        if (quizCalendarMonthLabel != null)
        {
            quizCalendarMonthLabel.text = AppLanguageManager.IsVietnamese
                ? $"Tháng {calendarDisplayMonth.Month}, {calendarDisplayMonth.Year}"
                : calendarDisplayMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        }

        int emptyCells = (int)calendarDisplayMonth.DayOfWeek;
        for (int i = 0; i < emptyCells; i++)
        {
            VisualElement empty = new VisualElement();
            empty.AddToClassList("quiz-calendar-cell");
            quizCalendarDays.Add(empty);
        }

        int daysInMonth = DateTime.DaysInMonth(
            calendarDisplayMonth.Year,
            calendarDisplayMonth.Month
        );

        DateTime selectedDate;
        bool hasSelectedDate = DateTime.TryParseExact(
            quizDeadlineDateValue,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out selectedDate
        );

        for (int day = 1; day <= daysInMonth; day++)
        {
            DateTime date = new DateTime(
                calendarDisplayMonth.Year,
                calendarDisplayMonth.Month,
                day
            );

            Button dayButton = new Button(() => SelectDeadlineDate(date))
            {
                text = day.ToString(CultureInfo.InvariantCulture)
            };
            dayButton.AddToClassList("quiz-calendar-day");

            if (date.Date == DateTime.Today)
                dayButton.AddToClassList("quiz-calendar-day-today");

            if (hasSelectedDate && date.Date == selectedDate.Date)
                dayButton.AddToClassList("quiz-calendar-day-selected");

            VisualElement cell = new VisualElement();
            cell.AddToClassList("quiz-calendar-cell");
            cell.Add(dayButton);
            quizCalendarDays.Add(cell);
        }

        int renderedCells = emptyCells + daysInMonth;
        int trailingCells = (7 - (renderedCells % 7)) % 7;
        for (int i = 0; i < trailingCells; i++)
        {
            VisualElement empty = new VisualElement();
            empty.AddToClassList("quiz-calendar-cell");
            quizCalendarDays.Add(empty);
        }
    }

    private void SelectDeadlineDate(DateTime date)
    {
        quizDeadlineDateValue = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        calendarDisplayMonth = new DateTime(date.Year, date.Month, 1);
        RefreshDeadlineLabels();
        BuildCalendarDays();
        quizCalendarPopup?.AddToClassList("hidden");
        ClearLabel(assetErrorLabel);
    }

    private void BuildTimeOptions()
    {
        deadlineHourButtons.Clear();
        deadlineMinuteButtons.Clear();
        quizHourScroll?.Clear();
        quizMinuteScroll?.Clear();

        for (int hour = 0; hour < 24; hour++)
        {
            int value = hour;
            Button button = CreateTimeOption(value, () =>
            {
                selectedDeadlineHour = value;
                RefreshTimeOptionStyles();
            });
            deadlineHourButtons.Add(button);
            quizHourScroll?.Add(button);
        }

        for (int minute = 0; minute < 60; minute++)
        {
            int value = minute;
            Button button = CreateTimeOption(value, () =>
            {
                selectedDeadlineMinute = value;
                RefreshTimeOptionStyles();
            });
            deadlineMinuteButtons.Add(button);
            quizMinuteScroll?.Add(button);
        }

        RefreshTimeOptionStyles();
    }

    private static Button CreateTimeOption(int value, Action clicked)
    {
        Button button = new Button(clicked)
        {
            text = value.ToString("00", CultureInfo.InvariantCulture)
        };
        button.AddToClassList("quiz-time-option");
        return button;
    }

    private void RefreshTimeOptionStyles()
    {
        for (int i = 0; i < deadlineHourButtons.Count; i++)
            deadlineHourButtons[i].EnableInClassList("quiz-time-option-selected", i == selectedDeadlineHour);

        for (int i = 0; i < deadlineMinuteButtons.Count; i++)
            deadlineMinuteButtons[i].EnableInClassList("quiz-time-option-selected", i == selectedDeadlineMinute);
    }

    private void ScrollToSelectedTime()
    {
        quizTimePopup?.schedule.Execute(() =>
        {
            if (selectedDeadlineHour >= 0 && selectedDeadlineHour < deadlineHourButtons.Count)
                quizHourScroll?.ScrollTo(deadlineHourButtons[selectedDeadlineHour]);

            if (selectedDeadlineMinute >= 0 && selectedDeadlineMinute < deadlineMinuteButtons.Count)
                quizMinuteScroll?.ScrollTo(deadlineMinuteButtons[selectedDeadlineMinute]);
        });
    }

    private void ConfirmTimeSelection()
    {
        quizDeadlineTimeValue =
            $"{selectedDeadlineHour:00}:{selectedDeadlineMinute:00}";
        RefreshDeadlineLabels();
        quizTimePopup?.AddToClassList("hidden");
        ClearLabel(assetErrorLabel);
    }

    private void HandleRootPointerDown(PointerDownEvent evt)
    {
        VisualElement target = evt.target as VisualElement;
        if (target == null) return;

        bool insideCalendar =
            (quizCalendarPopup != null && quizCalendarPopup.Contains(target)) ||
            (quizDeadlineDateButton != null && quizDeadlineDateButton.Contains(target));

        bool insideTimePicker =
            (quizTimePopup != null && quizTimePopup.Contains(target)) ||
            (quizDeadlineTimeButton != null && quizDeadlineTimeButton.Contains(target));

        if (!insideCalendar && !insideTimePicker)
        {
            quizCalendarPopup?.AddToClassList("hidden");
            quizTimePopup?.AddToClassList("hidden");
        }
    }

    private void RefreshDeadlineLabels()
    {
        SetPickerLabel(
            quizDeadlineDateLabel,
            quizDeadlineDateValue,
            "YYYY-MM-DD"
        );
        SetPickerLabel(
            quizDeadlineTimeLabel,
            quizDeadlineTimeValue,
            "HH:mm"
        );
    }

    private static void SetPickerLabel(Label label, string value, string placeholder)
    {
        if (label == null) return;
        bool isEmpty = string.IsNullOrWhiteSpace(value);
        label.text = isEmpty ? placeholder : value;
        label.EnableInClassList("quiz-picker-placeholder", isEmpty);
    }

    private void ClearQuizDeadline()
    {
        quizDeadlineDateValue = string.Empty;
        quizDeadlineTimeValue = string.Empty;
        selectedDeadlineHour = 23;
        selectedDeadlineMinute = 59;
        calendarDisplayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        RefreshDeadlineLabels();
        RefreshTimeOptionStyles();
        quizCalendarPopup?.AddToClassList("hidden");
        quizTimePopup?.AddToClassList("hidden");
    }

    private void SetQuizDeadlineFields(string utcIso)
    {
        if (string.IsNullOrWhiteSpace(utcIso) ||
            !DateTimeOffset.TryParse(
                utcIso,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed))
        {
            return;
        }

        DateTime local = parsed.ToLocalTime().DateTime;
        quizDeadlineDateValue = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        quizDeadlineTimeValue = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        calendarDisplayMonth = new DateTime(local.Year, local.Month, 1);
        selectedDeadlineHour = local.Hour;
        selectedDeadlineMinute = local.Minute;
        RefreshDeadlineLabels();
    }

    private IEnumerator UpdateQuizDeadlineRoutine(string quizId, Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(quizId))
        {
            onError?.Invoke("Quiz ID is missing. Cannot save deadline.");
            yield break;
        }

        if (!TryGetQuizDeadlineUtc(out string utcIso, out string validationError))
        {
            onError?.Invoke(validationError);
            yield break;
        }

        if (quizService == null)
        {
            onError?.Invoke("SupabaseQuizService is missing. Cannot save deadline.");
            yield break;
        }

        string requestError = null;
        string savedDeadline = null;

        yield return quizService.UpdateQuizDeadline(
            quizId,
            utcIso,
            value => savedDeadline = value,
            message => requestError = message
        );

        if (string.IsNullOrWhiteSpace(requestError) &&
            string.IsNullOrWhiteSpace(savedDeadline))
        {
            requestError = "Supabase did not confirm that the quiz deadline was saved.";
        }

        onError?.Invoke(requestError);
    }

    private bool ValidateDetails()
    {
        ClearLabel(detailsErrorLabel);

        if (string.IsNullOrWhiteSpace(lessonTitleField?.value))
        {
            SetLabel(detailsErrorLabel, "Please return to Step 1 and enter the lesson name.");
            lessonTitleField?.Focus();
            return false;
        }

        foreach (TextField field in objectiveFields)
        {
            if (!string.IsNullOrWhiteSpace(field.value))
                return true;
        }

        SetLabel(detailsErrorLabel, "Please enter at least one learning objective.");
        return false;
    }

    private void ConfirmYoutubeLink()
    {
        string url = youtubeUrlField?.value?.Trim() ?? string.Empty;
        if (!IsValidYoutubeUrl(url))
        {
            selectedYoutubeUrl = string.Empty;
            SetVideoStatus("Invalid YouTube URL.", true);
            return;
        }

        selectedYoutubeUrl = url;
        SetVideoStatus("YouTube link confirmed.", false);
        ClearLabel(assetErrorLabel);
    }

    private static bool IsValidYoutubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        return Regex.IsMatch(
            url,
            @"^https?://(www\.)?(youtube\.com/(watch\?v=|shorts/)|youtu\.be/)[A-Za-z0-9_-]+",
            RegexOptions.IgnoreCase
        );
    }

    private void SetVideoStatus(string text, bool isError)
    {
        if (videoLinkStatusLabel == null) return;
        videoLinkStatusLabel.text = L(text);
        videoLinkStatusLabel.EnableInClassList("video-link-status-error", isError);
    }

    private void ShowStep(int step)
    {
        currentStep = Mathf.Clamp(step, 1, TotalSteps);

        SetVisible(formatStep, currentStep == 1);
        SetVisible(assetStep, currentStep == 2);
        SetVisible(detailsStep, currentStep == 3);

        UpdateHeader();
        UpdateProgress();
        UpdateBottomActions();
    }

    private void UpdateHeader()
    {
        if (stepLabel != null) stepLabel.text = T($"Step {currentStep} of {TotalSteps}", $"Bước {currentStep}/{TotalSteps}");

        if (pageTitleLabel != null)
        {
            pageTitleLabel.text = L(currentStep switch
            {
                1 => "Location & Format",
                2 => "Asset Upload",
                3 => "Lesson Details",
                _ => "Create Lesson"
            });
        }
    }

    private void UpdateProgress()
    {
        UpdateProgressSegment(progressStep1, 1);
        UpdateProgressSegment(progressStep2, 2);
        UpdateProgressSegment(progressStep3, 3);
    }

    private void UpdateProgressSegment(VisualElement segment, int segmentStep)
    {
        if (segment == null) return;
        segment.EnableInClassList("progress-complete", segmentStep < currentStep);
        segment.EnableInClassList("progress-current", segmentStep == currentStep);
    }

    private void UpdateBottomActions()
    {
        bool isLastStep = currentStep == TotalSteps;

        if (nextButtonLabel != null)
        {
            nextButtonLabel.text = isLastStep
                ? (isUpdateMode ? T("Save Changes", "Lưu thay đổi") : L("Create Lesson"))
                : L("Next");
        }

        nextButton?.EnableInClassList("update-mode-finish", isUpdateMode && isLastStep);
        SetVisible(nextButtonIcon, !isLastStep);
    }

    private void UpdateUploadCards()
    {
        SetVisible(videoUploadCard, videoSelected);
        SetVisible(documentUploadCard, documentSelected);
        SetVisible(exerciseUploadCard, documentSelected);
        SetVisible(modelUploadCard, modelSelected);
    }

    private void BuildInitialObjectives()
    {
        objectivesContainer?.Clear();
        objectiveFields.Clear();

        for (int i = 0; i < MinimumObjectives; i++)
            AddObjective();
    }

    private void AddObjective()
    {
        if (objectivesContainer == null) return;

        VisualElement row = new();
        row.AddToClassList("objective-row");

        Label number = new();
        number.AddToClassList("objective-number");

        TextField field = new();
        field.AddToClassList("objective-field");
        field.multiline = true;
        field.tooltip = L("Students will be able to...");

        Button remove = new();
        remove.text = "×";
        remove.AddToClassList("remove-objective-button");

        row.Add(number);
        row.Add(field);
        row.Add(remove);
        objectivesContainer.Add(row);
        objectiveFields.Add(field);

        remove.clicked += () =>
        {
            if (objectiveFields.Count <= 1) return;
            objectiveFields.Remove(field);
            row.RemoveFromHierarchy();
            RefreshObjectiveNumbers();
        };

        RefreshObjectiveNumbers();
    }

    private void RefreshObjectiveNumbers()
    {
        if (objectivesContainer == null) return;

        for (int i = 0; i < objectivesContainer.childCount; i++)
        {
            Label number = objectivesContainer[i].Q<Label>(className: "objective-number");
            if (number != null) number.text = $"{i + 1}.";
        }
    }

    private bool isGeneratingWithAi;

    private void HandleGenerateWithAi()
    {
        string title = lessonTitleField?.value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            SetLabel(detailsErrorLabel, T("Enter a lesson title before generating content.", "Nhập tên bài học trước khi tạo nội dung."));
            return;
        }

        if (isGeneratingWithAi)
            return;

        StartCoroutine(GenerateLessonContentRoutine(title));
    }

    /// <summary>
    /// Calls the generate-lesson-content Edge Function (Gemini) to draft the
    /// description and learning objectives. Falls back to the local template
    /// when the AI service is unavailable.
    /// </summary>
    private IEnumerator GenerateLessonContentRoutine(string title)
    {
        // Read the current SettingScene language at the exact moment the user
        // generates content.
        bool generateInVietnamese = AppLanguageManager.IsVietnamese;

        isGeneratingWithAi = true;
        generateAiButton?.SetEnabled(false);
        SetLabel(detailsErrorLabel, T("Generating with AI...", "Đang tạo nội dung bằng AI..."));

        yield return SupabaseTokenRefresher.EnsureFreshToken();

        string chapterTitle = PlayerPrefs.GetString("selected_chapter_title", string.Empty);
        string body =
            "{\"title\":\"" + EscapeJsonForAi(title) + "\"," +
            "\"chapter_title\":\"" + EscapeJsonForAi(chapterTitle) + "\"," +
            "\"language\":\"" + (generateInVietnamese ? "vi" : "en") + "\"," +
            "\"objective_count\":3}";

        string description = null;
        List<string> objectives = null;
        string error = null;

        using (UnityWebRequest request = new UnityWebRequest(
                   SupabaseConfig.FunctionsUrl + "/generate-lesson-content",
                   UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 60;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", SupabaseConfig.PublishableKey);
            request.SetRequestHeader("Authorization", "Bearer " + SupabaseSession.AccessToken);

            yield return request.SendWebRequest();

            string text = request.downloadHandler?.text ?? string.Empty;
            GeneratedLessonContent response = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(text))
                    response = JsonUtility.FromJson<GeneratedLessonContent>(text);
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            if (request.result == UnityWebRequest.Result.Success &&
                response != null && response.success &&
                !string.IsNullOrWhiteSpace(response.description))
            {
                description = response.description.Trim();
                objectives = new List<string>();
                if (response.objectives != null)
                {
                    foreach (string objective in response.objectives)
                        if (!string.IsNullOrWhiteSpace(objective))
                            objectives.Add(objective.Trim());
                }
            }
            else
            {
                error = response?.error ?? error ?? request.error ?? ("HTTP " + request.responseCode);
            }
        }

        if (string.IsNullOrWhiteSpace(description) || objectives == null || objectives.Count == 0)
        {
            Debug.LogWarning("[CreateLesson] AI generation failed, using template: " + error);
            BuildTemplateLessonContent(title, generateInVietnamese, out description, out objectives);
            SetLabel(detailsErrorLabel, T(
                "AI is busy right now, a template was filled in. You can edit it or try again.",
                "AI đang bận, đã điền nội dung mẫu. Bạn có thể sửa hoặc thử lại."));
        }
        else
        {
            ClearLabel(detailsErrorLabel);
        }

        // Pressing the AI button means regenerate: always replace the previous content.
        if (lessonDescriptionField != null)
            lessonDescriptionField.value = description;

        objectivesContainer?.Clear();
        objectiveFields.Clear();

        foreach (string generatedObjective in objectives)
        {
            AddObjective();
            objectiveFields[^1].SetValueWithoutNotify(generatedObjective);
        }

        isGeneratingWithAi = false;
        generateAiButton?.SetEnabled(true);
    }

    private static void BuildTemplateLessonContent(
        string title,
        bool vietnamese,
        out string description,
        out List<string> objectives)
    {
        description = vietnamese
            ? $"Bài học giới thiệu {title}, giải thích các khái niệm cốt lõi và hướng dẫn học sinh thực hành trước khi tự vận dụng."
            : $"This lesson introduces {title}, explains the core concepts, and gives students guided practice before applying the topic independently.";

        objectives = vietnamese
            ? new List<string>
            {
                $"Giải thích các khái niệm cơ bản của {title}",
                $"Vận dụng các nguyên lý chính của {title} trong hoạt động thực hành"
            }
            : new List<string>
            {
                $"Explain the fundamental concepts of {title}",
                $"Apply the main principles of {title} in a practical activity"
            };
    }

    private static string EscapeJsonForAi(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\t", " ");
    }

    [Serializable]
    private class GeneratedLessonContent
    {
        public bool success;
        public string description;
        public string[] objectives;
        public string error;
    }

    private void PickDocuments()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel(T("Choose PDF Document", "Chọn tài liệu PDF"), string.Empty, "pdf");
        if (string.IsNullOrWhiteSpace(path)) return;

        if (!ValidateLocalFile(path, ".pdf", MaxPdfBytes, "PDF", out string error))
        {
            SetLabel(assetErrorLabel, error);
            return;
        }

        if (!selectedDocumentPaths.Contains(path))
        {
            selectedDocumentPaths.Add(path);
            RebuildDocumentChips();
        }

        ClearLabel(assetErrorLabel);
#elif UNITY_ANDROID || UNITY_IOS
        if (NativeFilePicker.IsFilePickerBusy())
        {
            SetLabel(assetErrorLabel, T("The file picker is already open.", "Trình chọn tệp đang mở."));
            return;
        }

        ClearLabel(assetErrorLabel);
        SetDocumentPickerStatus(T("Opening file picker...", "Đang mở trình chọn tệp..."));

        string pdfFileType = NativeFilePicker.ConvertExtensionToFileType("pdf");
        NativeFilePicker.PickMultipleFiles(paths =>
        {
            if (paths == null || paths.Length == 0)
            {
                SetDocumentPickerStatus(T("No new PDF selected.", "Chưa chọn thêm PDF."));
                return;
            }

            int addedCount = 0;
            List<string> errors = new();
            foreach (string path in paths)
            {
                string normalizedPath = NormalizeLocalFilePath(path);
                if (!ValidateLocalFile(normalizedPath, ".pdf", MaxPdfBytes, "PDF", out string error))
                {
                    errors.Add($"{Path.GetFileName(path)}: {error}");
                    continue;
                }

                if (!selectedDocumentPaths.Contains(normalizedPath))
                {
                    selectedDocumentPaths.Add(normalizedPath);
                    addedCount++;
                }
            }

            RebuildDocumentChips();
            SetDocumentPickerStatus(T(
                $"{selectedDocumentPaths.Count} PDF document(s) selected.",
                $"Đã chọn {selectedDocumentPaths.Count} tài liệu PDF."
            ));

            if (errors.Count > 0)
                SetLabel(assetErrorLabel, string.Join("\n", errors));
            else if (addedCount == 0)
                SetLabel(assetErrorLabel, T("The selected PDFs were already added.", "Các PDF đã chọn đã có trong danh sách."));
            else
                ClearLabel(assetErrorLabel);
        }, new[] { pdfFileType });

#else
        SetLabel(assetErrorLabel, T("File selection is not supported on this platform.", "Nền tảng này không hỗ trợ chọn tệp."));
#endif
    }

    private void PickExercisePdf()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel(T("Choose Exercise PDF", "Chọn PDF bài tập"), string.Empty, "pdf");
        if (string.IsNullOrWhiteSpace(path)) return;

        if (!ValidateLocalFile(path, ".pdf", MaxPdfBytes, "Exercise PDF", out string error))
        {
            SetLabel(assetErrorLabel, error);
            return;
        }

        selectedExercisePath = path;

        if (exerciseFileLabel != null)
            exerciseFileLabel.text = Path.GetFileName(path);

        SetVisible(exerciseFileRow, true);
        ClearLabel(assetErrorLabel);
#elif UNITY_ANDROID || UNITY_IOS
        if (NativeFilePicker.IsFilePickerBusy())
        {
            SetLabel(assetErrorLabel, T("The file picker is already open.", "Trình chọn tệp đang mở."));
            return;
        }

        ClearLabel(assetErrorLabel);
        string pdfFileType = NativeFilePicker.ConvertExtensionToFileType("pdf");
        NativeFilePicker.PickFile(path =>
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            path = NormalizeLocalFilePath(path);

            if (!ValidateLocalFile(path, ".pdf", MaxPdfBytes, "Exercise PDF", out string error))
            {
                SetLabel(assetErrorLabel, error);
                return;
            }

            selectedExercisePath = path;
            if (exerciseFileLabel != null)
                exerciseFileLabel.text = Path.GetFileName(path);

            SetVisible(exerciseFileRow, true);
            ClearLabel(assetErrorLabel);
        }, new[] { pdfFileType });

#else
        SetLabel(assetErrorLabel, T("File selection is not supported on this platform.", "Nền tảng này không hỗ trợ chọn tệp."));
#endif
    }

    private void SetDocumentPickerStatus(string message)
    {
        if (documentPickerStatusLabel == null) return;
        documentPickerStatusLabel.text = message ?? string.Empty;
        SetVisible(documentPickerStatusLabel, !string.IsNullOrWhiteSpace(message));
    }

    private void PickModel()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel(T("Choose GLB 3D Model", "Chọn mô hình GLB 3D"), string.Empty, "glb");
        if (string.IsNullOrWhiteSpace(path)) return;

        if (!ValidateLocalFile(path, ".glb", MaxModelBytes, "GLB", out string error))
        {
            SetLabel(assetErrorLabel, error);
            return;
        }

        selectedModelPath = path;

        if (modelFileLabel != null)
            modelFileLabel.text = Path.GetFileName(path);

        SetVisible(modelFileRow, true);
        ClearLabel(assetErrorLabel);
#elif UNITY_ANDROID || UNITY_IOS
        if (NativeFilePicker.IsFilePickerBusy())
        {
            SetLabel(assetErrorLabel, T("The file picker is already open.", "Trình chọn tệp đang mở."));
            return;
        }

        ClearLabel(assetErrorLabel);

#if UNITY_ANDROID
        // Some Android document providers don't register the .glb MIME type.
        // Allow the common GLB MIME types (and */* as a compatibility fallback),
        // then strictly validate the selected file extension below.
        string[] glbFileTypes =
        {
            "model/gltf-binary",
            "application/octet-stream",
            "*/*"
        };
#else
        string[] glbFileTypes =
        {
            NativeFilePicker.ConvertExtensionToFileType("glb")
        };
#endif

        NativeFilePicker.PickFile(path =>
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string normalizedPath = NormalizeLocalFilePath(path);
            if (!ValidateLocalFile(normalizedPath, ".glb", MaxModelBytes, "GLB", out string error))
            {
                SetLabel(assetErrorLabel, error);
                return;
            }

            selectedModelPath = normalizedPath;

            if (modelFileLabel != null)
                modelFileLabel.text = Path.GetFileName(normalizedPath);

            SetVisible(modelFileRow, true);
            ClearLabel(assetErrorLabel);
        }, glbFileTypes);
#else
        SetLabel(assetErrorLabel, T("File selection is not supported on this platform.", "Nền tảng này không hỗ trợ chọn tệp."));
#endif
    }

    private void RemoveExercisePdf()
    {
        ExistingAssetData existing = existingAssets.Find(asset => asset.asset_type == "quiz_pdf" && !removedExistingAssetIds.Contains(asset.id));
        if (existing != null) removedExistingAssetIds.Add(existing.id);

        selectedExercisePath = string.Empty;
        existingQuizId = string.Empty;
        ClearQuizDeadline();

        if (exerciseFileLabel != null)
            exerciseFileLabel.text = L("No exercise PDF selected");

        SetVisible(exerciseFileRow, false);
        ClearLabel(assetErrorLabel);
    }

    private void RemoveModel()
    {
        ExistingAssetData existing = existingAssets.Find(asset => asset.asset_type == "model_3d" && !removedExistingAssetIds.Contains(asset.id));
        if (existing != null) removedExistingAssetIds.Add(existing.id);

        selectedModelPath = string.Empty;

        if (modelFileLabel != null)
            modelFileLabel.text = L("No 3D asset selected");

        SetVisible(modelFileRow, false);
        ClearLabel(assetErrorLabel);
    }

    private static bool ValidateLocalFile(string path, string extension, long maxBytes, string displayType, out string error)
    {
        error = string.Empty;

        if (!File.Exists(path))
        {
            error = "The selected file does not exist.";
            return false;
        }

        if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Only {displayType} files are allowed.";
            return false;
        }

        if (new FileInfo(path).Length > maxBytes)
        {
            error = $"{displayType} file is too large.";
            return false;
        }

        return true;
    }

    private static string NormalizeLocalFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        string trimmed = path.Trim();
        if (!trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        try
        {
            return new Uri(trimmed).LocalPath;
        }
        catch
        {
            return trimmed.Replace("file://", string.Empty);
        }
    }

    private void RebuildDocumentChips()
    {
        if (documentChipContainer == null) return;
        documentChipContainer.Clear();

        if (isUpdateMode)
        {
            foreach (ExistingAssetData asset in existingAssets)
            {
                if (asset.asset_type != "document" || removedExistingAssetIds.Contains(asset.id))
                    continue;

                AddExistingDocumentChip(asset);
            }
        }

        for (int i = 0; i < selectedDocumentPaths.Count; i++)
        {
            string path = selectedDocumentPaths[i];

            VisualElement chip = new();
            chip.AddToClassList("document-chip-row");

            VisualElement icon = new();
            icon.AddToClassList("document-chip-icon");

            Label fileName = new(Path.GetFileName(path));
            fileName.AddToClassList("document-chip-text");

            Button remove = new(() =>
            {
                selectedDocumentPaths.Remove(path);
                RebuildDocumentChips();
                SetDocumentPickerStatus(selectedDocumentPaths.Count == 0
                    ? string.Empty
                    : T($"{selectedDocumentPaths.Count} PDF document(s) selected.", $"Đã chọn {selectedDocumentPaths.Count} tài liệu PDF."));
            })
            { text = "×" };
            remove.AddToClassList("remove-file-button");

            chip.Add(icon);
            chip.Add(fileName);
            chip.Add(remove);
            documentChipContainer.Add(chip);
        }
    }

    private IEnumerator LoadLessonForUpdateRoutine()
    {
        if (runtimeRestService == null)
        {
            SetLabel(formatErrorLabel, "SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string lessonId = UnityWebRequest.EscapeURL(editingLessonId);
        string lessonResponse = null;
        string error = null;

        yield return runtimeRestService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/lessons?select=id,chapter_id,title,description,youtube_url,has_video,status&id=eq." + lessonId,
            null, null,
            value => lessonResponse = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            SetLabel(formatErrorLabel, error);
            yield break;
        }

        LessonEditorRecordList lessonWrapper = JsonUtility.FromJson<LessonEditorRecordList>($"{{\"items\":{lessonResponse}}}");
        if (lessonWrapper?.items == null || lessonWrapper.items.Length == 0)
        {
            SetLabel(formatErrorLabel, "The lesson could not be found.");
            yield break;
        }

        LessonEditorRecord lesson = lessonWrapper.items[0];
        selectedChapterId = lesson.chapter_id;
        lessonTitleField?.SetValueWithoutNotify(lesson.title ?? string.Empty);
        lessonDescriptionField?.SetValueWithoutNotify(lesson.description ?? string.Empty);
        youtubeUrlField?.SetValueWithoutNotify(lesson.youtube_url ?? string.Empty);
        selectedYoutubeUrl = lesson.youtube_url ?? string.Empty;
        videoSelected = !string.IsNullOrWhiteSpace(lesson.youtube_url);

        string assetResponse = null;
        error = null;

        yield return runtimeRestService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/lesson_assets?select=id,asset_type,file_name,storage_bucket,storage_path,mime_type,file_extension,file_size_bytes,display_order&lesson_id=eq." + lessonId + "&order=display_order.asc",
            null, null,
            value => assetResponse = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            SetLabel(assetErrorLabel, error);
            yield break;
        }

        ExistingAssetDataList assetWrapper = JsonUtility.FromJson<ExistingAssetDataList>($"{{\"items\":{assetResponse}}}");
        existingAssets.Clear();
        removedExistingAssetIds.Clear();

        if (assetWrapper?.items != null)
            existingAssets.AddRange(assetWrapper.items);

        documentSelected = existingAssets.Exists(asset => asset.asset_type == "document" || asset.asset_type == "quiz_pdf");
        modelSelected = existingAssets.Exists(asset => asset.asset_type == "model_3d");

        UpdateFormatVisual(videoFormatButton, videoRadio, videoSelected);
        UpdateFormatVisual(documentFormatButton, documentRadio, documentSelected);
        UpdateFormatVisual(modelFormatButton, modelRadio, modelSelected);

        RebuildExistingAssetRows();

        string quizResponse = null;
        error = null;

        yield return runtimeRestService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/quizzes?select=id,closes_at&lesson_id=eq." + lessonId + "&order=created_at.desc&limit=1",
            null, null,
            value => quizResponse = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            SetLabel(assetErrorLabel, error);
            yield break;
        }

        QuizDeadlineRecordList quizWrapper = JsonUtility.FromJson<QuizDeadlineRecordList>(
            $"{{\"items\":{quizResponse}}}"
        );

        existingQuizId = string.Empty;
        if (quizWrapper?.items != null && quizWrapper.items.Length > 0)
        {
            existingQuizId = quizWrapper.items[0].id ?? string.Empty;
            SetQuizDeadlineFields(quizWrapper.items[0].closes_at);
        }

        string objectiveResponse = null;
        error = null;

        yield return runtimeRestService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            "rest/v1/lesson_objectives?select=id,objective_text,objective_order&lesson_id=eq." + lessonId + "&order=objective_order.asc",
            null, null,
            value => objectiveResponse = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            SetLabel(detailsErrorLabel, error);
            yield break;
        }

        LessonObjectiveEditorList objectiveWrapper = JsonUtility.FromJson<LessonObjectiveEditorList>($"{{\"items\":{objectiveResponse}}}");

        objectivesContainer?.Clear();
        objectiveFields.Clear();

        if (objectiveWrapper?.items != null)
        {
            foreach (LessonObjectiveEditor objective in objectiveWrapper.items)
            {
                AddObjective();
                objectiveFields[^1].SetValueWithoutNotify(objective.objective_text ?? string.Empty);
            }
        }

        while (objectiveFields.Count < MinimumObjectives)
            AddObjective();

        UpdateUploadCards();
        ClearLabel(formatErrorLabel);
    }

    private void RebuildExistingAssetRows()
    {
        RebuildDocumentChips();

        ExistingAssetData exercise = existingAssets.Find(asset => asset.asset_type == "quiz_pdf" && !removedExistingAssetIds.Contains(asset.id));
        if (exercise != null)
        {
            if (exerciseFileLabel != null) exerciseFileLabel.text = exercise.file_name;
            SetVisible(exerciseFileRow, true);
        }
        else if (string.IsNullOrWhiteSpace(selectedExercisePath))
        {
            SetVisible(exerciseFileRow, false);
        }

        ExistingAssetData model = existingAssets.Find(asset => asset.asset_type == "model_3d" && !removedExistingAssetIds.Contains(asset.id));
        if (model != null)
        {
            if (modelFileLabel != null) modelFileLabel.text = model.file_name;
            SetVisible(modelFileRow, true);
        }
        else if (string.IsNullOrWhiteSpace(selectedModelPath))
        {
            SetVisible(modelFileRow, false);
        }
    }

    private void AddExistingDocumentChip(ExistingAssetData asset)
    {
        VisualElement chip = new();
        chip.AddToClassList("document-chip-row");

        VisualElement icon = new();
        icon.AddToClassList("document-chip-icon");

        Label fileName = new(asset.file_name);
        fileName.AddToClassList("document-chip-text");

        Button remove = new() { text = "×" };
        remove.AddToClassList("remove-file-button");
        remove.clicked += () =>
        {
            removedExistingAssetIds.Add(asset.id);
            RebuildExistingAssetRows();
        };

        chip.Add(icon);
        chip.Add(fileName);
        chip.Add(remove);
        documentChipContainer.Add(chip);
    }

    private IEnumerator UpdateLessonRoutine()
    {
        if (isSaving) yield break;

        isSaving = true;
        SetSavingUi(true);
        SetSaveProgress(10f, "Updating lesson...");

        string finalYoutubeUrl = videoSelected ? youtubeUrlField?.value?.Trim() : null;

        LessonUpdatePayload payload = new()
        {
            title = lessonTitleField?.value?.Trim() ?? string.Empty,
            description = lessonDescriptionField?.value?.Trim() ?? string.Empty,
            youtube_url = string.IsNullOrWhiteSpace(finalYoutubeUrl) ? string.Empty : finalYoutubeUrl,
            has_video = !string.IsNullOrWhiteSpace(finalYoutubeUrl),
            status = "published"
        };

        string error = null;
        string lessonId = UnityWebRequest.EscapeURL(editingLessonId);

        yield return runtimeRestService.SendJson(
            "PATCH",
            $"rest/v1/lessons?id=eq.{lessonId}",
            JsonUtility.ToJson(payload),
            "return=minimal",
            _ => { },
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            FailSaving(error);
            yield break;
        }

        foreach (string assetId in removedExistingAssetIds)
        {
            error = null;
            yield return runtimeRestService.SendJson(
                "DELETE",
                $"rest/v1/lesson_assets?id=eq.{UnityWebRequest.EscapeURL(assetId)}",
                null, "return=minimal",
                _ => { },
                message => error = message
            );

            if (!string.IsNullOrWhiteSpace(error))
            {
                FailSaving(error);
                yield break;
            }
        }

        error = null;
        yield return runtimeRestService.SendJson(
            "DELETE",
            $"rest/v1/lesson_objectives?lesson_id=eq.{lessonId}",
            null, "return=minimal",
            _ => { },
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            FailSaving(error);
            yield break;
        }

        List<string> objectives = CollectObjectives();
        for (int i = 0; i < objectives.Count; i++)
        {
            error = null;
            LessonObjectiveInsert objective = new()
            {
                lesson_id = editingLessonId,
                objective_text = objectives[i],
                objective_order = i + 1
            };

            yield return lessonService.CreateLessonObjective(objective, () => { }, message => error = message);
            if (!string.IsNullOrWhiteSpace(error))
            {
                FailSaving(error);
                yield break;
            }
        }

        yield return UploadNewAssetsForExistingLesson(editingLessonId);

        if (!isSaving) yield break;

        SetSaveProgress(100f, "Lesson updated.");
        yield return new WaitForSecondsRealtime(0.35f);

        isSaving = false;
        PlayerPrefs.SetString("lesson_editor_mode", "create");
        PlayerPrefs.Save();
        ReturnToClassDetail();
    }

    private IEnumerator UploadNewAssetsForExistingLesson(string lessonId)
    {
        string classId = PlayerPrefs.GetString("selected_class_id", string.Empty);
        string teacherId = PlayerPrefs.GetString("user_id", string.Empty);
        string error = null;

        if (string.IsNullOrWhiteSpace(selectedExercisePath) &&
            !string.IsNullOrWhiteSpace(existingQuizId))
        {
            yield return UpdateQuizDeadlineRoutine(existingQuizId, message => error = message);
            if (!string.IsNullOrWhiteSpace(error))
            {
                FailSaving(error);
                yield break;
            }
        }

        // 1. Upload new Document PDFs lên Cloudflare R2
        for (int i = 0; i < selectedDocumentPaths.Count; i++)
        {
            string localPath = selectedDocumentPaths[i];
            string storagePath = $"{teacherId}/{classId}/{lessonId}/documents/{Guid.NewGuid():N}.pdf";
            string uploadedPath = null;

            yield return r2StorageService.UploadFile(
                "lesson-documents", // R2 Bucket Name
                storagePath,
                localPath,
                "application/pdf",
                path => uploadedPath = path,
                message => error = message
            );

            if (!string.IsNullOrWhiteSpace(error))
            {
                FailSaving(error);
                yield break;
            }

            LessonAssetInsert asset = new()
            {
                lesson_id = lessonId,
                uploaded_by = teacherId,
                asset_type = "document",
                file_name = Path.GetFileName(localPath),
                storage_bucket = "lesson-documents",
                storage_path = uploadedPath,
                mime_type = "application/pdf",
                file_extension = ".pdf",
                file_size_bytes = new FileInfo(localPath).Length,
                display_order = i
            };

            yield return lessonService.CreateLessonAsset(asset, () => { }, message => error = message);
            if (!string.IsNullOrWhiteSpace(error))
            {
                FailSaving(error);
                yield break;
            }
        }

        // 2. Quiz PDF is handled ENTIRELY by the hosted Edge Function.
        //
        // Unity sends the LOCAL PDF once:
        // local PDF -> parse-quiz-pdf
        // -> Gemini parse
        // -> Cloudflare R2 upload
        // -> lesson_assets
        // -> quizzes / quiz_questions / quiz_options.
        //
        // IMPORTANT: Do NOT upload the quiz PDF to R2 again from Unity.
        if (!string.IsNullOrWhiteSpace(selectedExercisePath))
        {
            if (quizService == null)
            {
                FailSaving(
                    "Thiếu SupabaseQuizService. Không thể xử lý Quiz PDF."
                );
                yield break;
            }

            SetSaveProgress(
                72f,
                T("Analyzing Quiz PDF and saving data...", "Đang phân tích Quiz PDF và lưu dữ liệu...")
            );

            ParseQuizPdfResponse quizResult = null;
            string quizError = null;

            string quizTitle =
                lessonTitleField?.value?.Trim();

            if (string.IsNullOrWhiteSpace(quizTitle))
            {
                quizTitle = T("Exercise", "Bài tập");
            }

            if (!TryGetQuizDeadlineUtc(out string closesAtUtcIso, out string deadlineError))
            {
                FailSaving(deadlineError);
                yield break;
            }

            yield return quizService.CallParseQuizFunctionDetailed(
                lessonId,
                quizTitle,
                selectedExercisePath,
                closesAtUtcIso,
                response => quizResult = response,
                message => quizError = message
            );

            if (!string.IsNullOrWhiteSpace(quizError) ||
                quizResult == null ||
                !quizResult.success ||
                string.IsNullOrWhiteSpace(quizResult.quiz_id))
            {
                FailSaving(
                    string.IsNullOrWhiteSpace(quizError)
                        ? "Backend không tạo được Quiz từ PDF."
                        : quizError
                );
                yield break;
            }

            existingQuizId = quizResult.quiz_id;

            yield return RestoreQuizPdfDisplayNameRoutine(
                quizResult.lesson_asset_id,
                selectedExercisePath);

            Debug.Log(
                "[CreateLessonPageController] Quiz update pipeline completed. " +
                $"Quiz ID: {quizResult.quiz_id}, " +
                $"Asset ID: {quizResult.lesson_asset_id}, " +
                $"R2: {quizResult.storage?.bucket}/{quizResult.storage?.path}"
            );

            SetSaveProgress(
                82f,
                T("Quiz saved successfully.", "Quiz đã được lưu thành công.")
            );
        }

        // 3. Upload 3D GLB Model lên R2, sau đó INSERT lesson_assets.
        // Chính INSERT này sẽ kích hoạt database trigger auto_process_model_detail.
        // Trigger gọi process-model-detail -> Cloud Run -> Gemini -> model_parts.
        if (!string.IsNullOrWhiteSpace(selectedModelPath))
        {
            yield return UploadModelAssetAndQueueAutomaticDetailGeneration(
                lessonId,
                teacherId,
                classId,
                selectedModelPath,
                88f
            );

            if (!isSaving)
                yield break;
        }
    }

    /// <summary>
    /// Upload model GLB lên Cloudflare R2 trước. Chỉ khi upload thành công mới tạo
    /// row lesson_assets(asset_type = model_3d). Database trigger
    /// auto_process_model_detail sẽ tự động gọi process-model-detail ở backend.
    /// Unity tuyệt đối không cần tạo signed URL hoặc gọi Cloud Run thủ công.
    /// </summary>
    private IEnumerator UploadModelAssetAndQueueAutomaticDetailGeneration(
        string lessonId,
        string teacherId,
        string classId,
        string localModelPath,
        float progressValue
    )
    {
        if (string.IsNullOrWhiteSpace(localModelPath) || !File.Exists(localModelPath))
        {
            FailSaving("Không tìm thấy file GLB đã chọn.");
            yield break;
        }

        if (r2StorageService == null)
        {
            FailSaving("CloudflareR2StorageService is missing.");
            yield break;
        }

        if (lessonService == null)
        {
            FailSaving("SupabaseLessonService is missing.");
            yield break;
        }

        SetSaveProgress(progressValue, T("Uploading 3D model to R2...", "Đang tải model 3D lên R2..."));

        string storagePath =
            $"{teacherId}/{classId}/{lessonId}/models/{Guid.NewGuid():N}.glb";
        string uploadedPath = null;
        string uploadError = null;

        yield return r2StorageService.UploadFile(
            "lesson-models",
            storagePath,
            localModelPath,
            "model/gltf-binary",
            path => uploadedPath = path,
            error => uploadError = error
        );

        if (!string.IsNullOrWhiteSpace(uploadError))
        {
            FailSaving(uploadError);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(uploadedPath))
        {
            FailSaving("R2 upload thành công nhưng storage_path trả về rỗng.");
            yield break;
        }

        // QUAN TRỌNG: chỉ INSERT sau khi R2 upload thành công.
        // AFTER INSERT trigger trên lesson_assets sẽ nhận row hoàn chỉnh và bắt đầu
        // pipeline AI tạo label + description + structure + function + anchor.
        LessonAssetInsert modelAsset = new()
        {
            lesson_id = lessonId,
            uploaded_by = teacherId,
            asset_type = "model_3d",
            file_name = Path.GetFileName(localModelPath),
            storage_bucket = "lesson-models",
            storage_path = uploadedPath,
            mime_type = "model/gltf-binary",
            file_extension = ".glb",
            file_size_bytes = new FileInfo(localModelPath).Length,
            display_order = 0
        };

        string insertError = null;
        LessonAssetRecord createdModelAsset = null;
        yield return lessonService.CreateLessonAsset(
            modelAsset,
            created => createdModelAsset = created,
            error => insertError = error
        );

        if (!string.IsNullOrWhiteSpace(insertError))
        {
            FailSaving(insertError);
            yield break;
        }

        if (createdModelAsset == null ||
            string.IsNullOrWhiteSpace(createdModelAsset.id))
        {
            FailSaving("Model đã được upload nhưng không lấy được asset_id để phân tích GLB.");
            yield break;
        }

        // Do not rely only on a database webhook. Mobile uploads explicitly
        // queue analysis using the exact row id returned by Supabase.
        string analysisError = null;
        yield return lessonService.GenerateModelDetails(
            createdModelAsset.id,
            () => { },
            error => analysisError = error
        );

        if (!string.IsNullOrWhiteSpace(analysisError))
        {
            // Uploading the GLB and generating AI metadata are two separate
            // operations. A temporary Gemini/worker outage must not discard
            // an otherwise valid lesson or force the user to upload the model
            // again. The asset remains in lesson_assets and can be processed
            // again by the webhook/manual retry pipeline.
            Debug.LogWarning(
                "[CreateLessonPageController] Model upload succeeded, but " +
                "automatic detail generation is pending/failed temporarily. " +
                $"Asset ID: {createdModelAsset.id}. Error: {analysisError}"
            );

            SetSaveProgress(
                Mathf.Min(progressValue + 4f, 96f),
                T(
                    "Model uploaded. Detail analysis can be retried later.",
                    "Mô hình đã tải lên. Có thể thử phân tích chi tiết lại sau."
                )
            );

            yield break;
        }

        SetSaveProgress(
            Mathf.Min(progressValue + 4f, 96f),
            T("Model uploaded and analyzed successfully.", "Model đã tải lên và phân tích cấu trúc thành công.")
        );

        Debug.Log(
            "[CreateLessonPageController] Model asset inserted successfully. " +
            "GLB structure analysis completed. " +
            $"Asset ID: {createdModelAsset.id}, " +
            $"Lesson ID: {lessonId}, File: {Path.GetFileName(localModelPath)}, " +
            $"R2: lesson-models/{uploadedPath}"
        );
    }

    private void SaveLesson()
    {
        if (isSaving) return;

        if (lessonService == null)
        {
            SetLabel(detailsErrorLabel, "SupabaseLessonService is missing.");
            return;
        }

        // Lecture documents and GLB models are still uploaded by Unity.
        // Quiz PDFs are now uploaded by the hosted parse-quiz-pdf Edge Function.
        bool requiresStorage =
            selectedDocumentPaths.Count > 0 ||
            modelSelected;

        if (requiresStorage && r2StorageService == null)
        {
            SetLabel(detailsErrorLabel, "CloudflareR2StorageService is missing.");
            return;
        }

        StartCoroutine(CreateLessonRoutine());
    }

    private IEnumerator CreateLessonRoutine()
    {
        isSaving = true;
        SetSavingUi(true);
        SetSaveProgress(5f, "Preparing lesson...");

        string classId = PlayerPrefs.GetString("selected_class_id", string.Empty);
        string teacherId = PlayerPrefs.GetString("user_id", string.Empty);

        if (string.IsNullOrWhiteSpace(classId) || string.IsNullOrWhiteSpace(teacherId) || string.IsNullOrWhiteSpace(selectedChapterId))
        {
            FailSaving("Missing class, teacher, or chapter information.");
            yield break;
        }

        if (!Guid.TryParse(classId, out _))
        {
            FailSaving("selected_class_id is not a valid UUID.");
            yield break;
        }

        if (!Guid.TryParse(teacherId, out _))
        {
            FailSaving("teacher_id is not a valid UUID.");
            yield break;
        }

        if (!Guid.TryParse(selectedChapterId, out _))
        {
            FailSaving("selected_chapter_id is not a valid UUID.");
            yield break;
        }

        List<string> objectives = CollectObjectives();
        string finalStatus = "published";
        string finalYoutubeUrl = videoSelected && !string.IsNullOrWhiteSpace(selectedYoutubeUrl)
            ? selectedYoutubeUrl.Trim()
            : null;

        CreateLessonRequest request = new()
        {
            chapter_id = selectedChapterId,
            teacher_id = teacherId,
            title = lessonTitleField.value.Trim(),
            description = lessonDescriptionField?.value?.Trim() ?? string.Empty,
            youtube_url = finalYoutubeUrl,
            has_video = !string.IsNullOrWhiteSpace(finalYoutubeUrl),
            status = finalStatus
        };

        LessonRecord createdLesson = null;
        string operationError = null;

        SetSaveProgress(10f, "Creating lesson record...");

        yield return lessonService.CreateLesson(
            request,
            lesson => createdLesson = lesson,
            error => operationError = error
        );

        if (createdLesson == null)
        {
            FailSaving(operationError ?? "Cannot create lesson.");
            yield break;
        }

        int totalUploads = selectedDocumentPaths.Count +
            (!string.IsNullOrWhiteSpace(selectedExercisePath) ? 1 : 0) +
            (modelSelected ? 1 : 0);

        int completedUploads = 0;

        // 1. Upload Lesson Document PDFs lên Cloudflare R2
        for (int i = 0; i < selectedDocumentPaths.Count; i++)
        {
            string localPath = selectedDocumentPaths[i];
            SetSaveProgress(
                CalculateUploadProgress(completedUploads, totalUploads),
                $"Uploading PDF {i + 1} of {selectedDocumentPaths.Count} to R2..."
            );

            string storagePath = $"{teacherId}/{classId}/{createdLesson.id}/documents/{Guid.NewGuid():N}.pdf";
            string uploadedPath = null;
            operationError = null;

            yield return r2StorageService.UploadFile(
                "lesson-documents", // R2 Bucket Name
                storagePath,
                localPath,
                "application/pdf",
                path => uploadedPath = path,
                error => operationError = error
            );

            if (!string.IsNullOrWhiteSpace(operationError))
            {
                FailSaving(operationError);
                yield break;
            }

            LessonAssetInsert asset = new()
            {
                lesson_id = createdLesson.id,
                uploaded_by = teacherId,
                asset_type = "document",
                file_name = Path.GetFileName(localPath),
                storage_bucket = "lesson-documents",
                storage_path = uploadedPath,
                mime_type = "application/pdf",
                file_extension = ".pdf",
                file_size_bytes = new FileInfo(localPath).Length,
                display_order = i
            };

            yield return lessonService.CreateLessonAsset(
                asset,
                () => { },
                error => operationError = error
            );

            if (!string.IsNullOrWhiteSpace(operationError))
            {
                FailSaving(operationError);
                yield break;
            }

            completedUploads++;
        }

        // 2. Quiz PDF is handled ENTIRELY by parse-quiz-pdf.
        //
        // Edge Function performs:
        // Gemini parse -> R2 upload -> lesson_assets -> quizzes
        // -> quiz_questions -> quiz_options.
        //
        // Unity must NOT upload the same quiz PDF to R2 again.
        if (!string.IsNullOrWhiteSpace(selectedExercisePath))
        {
            if (quizService == null)
            {
                FailSaving(
                    "Thiếu SupabaseQuizService. Không thể xử lý Quiz PDF."
                );
                yield break;
            }

            SetSaveProgress(
                CalculateUploadProgress(completedUploads, totalUploads),
                T("Analyzing Quiz PDF and saving data...", "Đang phân tích Quiz PDF và lưu dữ liệu...")
            );

            ParseQuizPdfResponse quizResult = null;
            string quizError = null;

            if (!TryGetQuizDeadlineUtc(out string closesAtUtcIso, out string deadlineError))
            {
                FailSaving(deadlineError);
                yield break;
            }

            yield return quizService.CallParseQuizFunctionDetailed(
                createdLesson.id,
                createdLesson.title,
                selectedExercisePath,
                closesAtUtcIso,
                response => quizResult = response,
                message => quizError = message
            );

            if (!string.IsNullOrWhiteSpace(quizError) ||
                quizResult == null ||
                !quizResult.success ||
                string.IsNullOrWhiteSpace(quizResult.quiz_id))
            {
                FailSaving(
                    string.IsNullOrWhiteSpace(quizError)
                        ? "Backend không tạo được Quiz từ PDF."
                        : quizError
                );
                yield break;
            }

            completedUploads++;

            yield return RestoreQuizPdfDisplayNameRoutine(
                quizResult.lesson_asset_id,
                selectedExercisePath);

            Debug.Log(
                "[CreateLessonPageController] Quiz create pipeline completed. " +
                $"Quiz ID: {quizResult.quiz_id}, " +
                $"Asset ID: {quizResult.lesson_asset_id}, " +
                $"R2: {quizResult.storage?.bucket}/{quizResult.storage?.path}"
            );
        }

        // 3. Upload 3D GLB Model lên R2 rồi INSERT lesson_assets.
        // Không gọi process-model-detail từ Unity: backend trigger sẽ tự làm việc đó.
        if (modelSelected)
        {
            yield return UploadModelAssetAndQueueAutomaticDetailGeneration(
                createdLesson.id,
                teacherId,
                classId,
                selectedModelPath,
                CalculateUploadProgress(completedUploads, totalUploads)
            );

            if (!isSaving)
                yield break;

            completedUploads++;
        }

        SetSaveProgress(85f, "Saving learning objectives...");

        for (int i = 0; i < objectives.Count; i++)
        {
            operationError = null;

            LessonObjectiveInsert objective = new()
            {
                lesson_id = createdLesson.id,
                objective_text = objectives[i],
                objective_order = i + 1
            };

            yield return lessonService.CreateLessonObjective(
                objective,
                () => { },
                error => operationError = error
            );

            if (!string.IsNullOrWhiteSpace(operationError))
            {
                FailSaving(operationError);
                yield break;
            }
        }

        PlayerPrefs.SetString("selected_lesson_id", createdLesson.id);
        PlayerPrefs.Save();

        SetSaveProgress(100f, "Lesson published.");
        yield return new WaitForSecondsRealtime(0.35f);

        isSaving = false;
        ReturnToClassDetail();
    }

    private static float CalculateUploadProgress(int completed, int total)
    {
        if (total <= 0) return 70f;
        return 20f + (50f * completed / total);
    }

    private List<string> CollectObjectives()
    {
        List<string> objectives = new();
        foreach (TextField field in objectiveFields)
        {
            string value = field.value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                objectives.Add(value);
        }
        return objectives;
    }

    private void SetSavingUi(bool saving)
    {
        nextButton?.SetEnabled(!saving);
        cancelButton?.SetEnabled(!saving);
        backButton?.SetEnabled(!saving);

        SetVisible(saveProgressContainer, saving);
    }

    private void SetSaveProgress(float value, string text)
    {
        if (saveProgressBar != null)
            saveProgressBar.value = Mathf.Clamp(value, 0f, 100f);

        if (saveProgressLabel != null)
            saveProgressLabel.text = L(text);
    }

    // parse-quiz-pdf stores an ASCII-only file_name (Vietnamese letters become
    // "_"). Restore the original, human-readable name for display; the R2
    // storage_path is left untouched.
    private IEnumerator RestoreQuizPdfDisplayNameRoutine(string assetId, string localPdfPath)
    {
        if (runtimeRestService == null ||
            string.IsNullOrWhiteSpace(assetId) ||
            string.IsNullOrWhiteSpace(localPdfPath))
            yield break;

        string originalName = Path.GetFileName(localPdfPath);
        if (string.IsNullOrWhiteSpace(originalName))
            yield break;

        originalName = originalName.Normalize(System.Text.NormalizationForm.FormC);

        string error = null;
        yield return runtimeRestService.SendJson(
            "PATCH",
            $"rest/v1/lesson_assets?id=eq.{UnityWebRequest.EscapeURL(assetId)}",
            JsonUtility.ToJson(new QuizPdfFileNamePatch { file_name = originalName }),
            "return=minimal",
            _ => { },
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
            Debug.LogWarning("[CreateLessonPageController] Could not restore quiz PDF file name: " + error);
    }

    private void FailSaving(string message)
    {
        Debug.LogError(message);
        SetLabel(detailsErrorLabel, message);
        SetSavingUi(false);
        isSaving = false;
    }

    private void ClearUnsavedData()
    {
        videoSelected = false;
        modelSelected = false;
        documentSelected = false;

        selectedYoutubeUrl = string.Empty;
        selectedExercisePath = string.Empty;
        selectedModelPath = string.Empty;
        existingQuizId = string.Empty;
        selectedDocumentPaths.Clear();

        youtubeUrlField?.SetValueWithoutNotify(string.Empty);
        lessonTitleField?.SetValueWithoutNotify(string.Empty);
        lessonDescriptionField?.SetValueWithoutNotify(string.Empty);
        ClearQuizDeadline();

        UpdateFormatVisual(videoFormatButton, videoRadio, false);
        UpdateFormatVisual(modelFormatButton, modelRadio, false);
        UpdateFormatVisual(documentFormatButton, documentRadio, false);

        documentChipContainer?.Clear();
        SetVideoStatus(string.Empty, false);

        if (exerciseFileLabel != null)
            exerciseFileLabel.text = L("No exercise PDF selected");

        if (modelFileLabel != null)
            modelFileLabel.text = L("No 3D asset selected");

        SetVisible(exerciseFileRow, false);
        SetVisible(modelFileRow, false);

        ClearLabel(formatErrorLabel);
        ClearLabel(assetErrorLabel);
        ClearLabel(detailsErrorLabel);
        BuildInitialObjectives();
    }

    private void ReturnToClassDetail()
    {
        const string sceneName = "ClassDetailScene";

        if (Application.CanStreamedLevelBeLoaded(sceneName))
            SceneManager.LoadScene(sceneName);
        else
            Debug.LogError($"Không tìm thấy {sceneName} trong Build Settings.");
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        if (element == null) return;
        element.EnableInClassList("hidden", !visible);
    }

    private static void SetLabel(Label label, string message)
    {
        if (label != null) label.text = L(message);
    }

    private static void ClearLabel(Label label)
    {
        if (label != null) label.text = string.Empty;
    }
}

#region Data Models for Editor & Supabase REST API
[Serializable]
public class ExistingAssetData
{
    public string id;
    public string asset_type;
    public string file_name;
    public string storage_bucket;
    public string storage_path;
    public string mime_type;
    public string file_extension;
    public long file_size_bytes;
    public int display_order;
}

[Serializable]
public class ExistingAssetDataList
{
    public ExistingAssetData[] items;
}

[Serializable]
public class LessonEditorRecord
{
    public string id;
    public string chapter_id;
    public string title;
    public string description;
    public string youtube_url;
    public bool has_video;
    public string status;
}

[Serializable]
public class LessonEditorRecordList
{
    public LessonEditorRecord[] items;
}

[Serializable]
public class LessonObjectiveEditor
{
    public string id;
    public string objective_text;
    public int objective_order;
}

[Serializable]
public class LessonObjectiveEditorList
{
    public LessonObjectiveEditor[] items;
}

[Serializable]
public class LessonUpdatePayload
{
    public string title;
    public string description;
    public string youtube_url;
    public bool has_video;
    public string status;
}

[Serializable]
public class QuizDeadlineRecord
{
    public string id;
    public string closes_at;
}

[Serializable]
public class QuizDeadlineRecordList
{
    public QuizDeadlineRecord[] items;
}

[Serializable]
public class QuizDeadlineUpdatePayload
{
    public string closes_at;
}
#endregion

[Serializable]
public class QuizPdfFileNamePatch
{
    public string file_name;
}
