using System;
using System.Collections;
using System.Collections.Generic;
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
    private Button saveDraftButton;
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
    private Button removeExerciseButton;
    private Button removeModelButton;
    private VisualElement documentChipContainer;
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

    private readonly List<string> selectedDocumentPaths = new();
    private readonly List<TextField> objectiveFields = new();
    private readonly List<ChapterRecord> loadedChapters = new();

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
        RegisterEvents();
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
        ShowStep(1);
        StartCoroutine(InitializeEditorRoutine());
    }

    private void OnDisable()
    {
        UnregisterEvents();
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
        saveDraftButton = root.Q<Button>("save-draft-button");
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
        removeExerciseButton = root.Q<Button>("remove-exercise-button");
        removeModelButton = root.Q<Button>("remove-model-button");
        documentChipContainer = root.Q<VisualElement>("document-chip-container");
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
        if (saveDraftButton != null) saveDraftButton.clicked += HandleSaveDraft;

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

        if (generateAiButton != null) generateAiButton.clicked += HandleGenerateWithAi;
        if (addObjectiveButton != null) addObjectiveButton.clicked += AddObjective;

        if (chapterDropdown != null)
            chapterDropdown.RegisterValueChangedCallback(HandleChapterChanged);
    }

    private void UnregisterEvents()
    {
        if (backButton != null) backButton.clicked -= HandleBack;
        if (cancelButton != null) cancelButton.clicked -= HandleCancel;
        if (nextButton != null) nextButton.clicked -= HandleNext;
        if (saveDraftButton != null) saveDraftButton.clicked -= HandleSaveDraft;

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

        if (generateAiButton != null) generateAiButton.clicked -= HandleGenerateWithAi;
        if (addObjectiveButton != null) addObjectiveButton.clicked -= AddObjective;

        if (chapterDropdown != null)
            chapterDropdown.UnregisterValueChangedCallback(HandleChapterChanged);
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
                    ? $"Chapter {Mathf.Max(1, passedChapterOrder)}"
                    : passedChapterTitle,
                chapter_order = Mathf.Max(1, passedChapterOrder)
            });

            if (chapterDropdown != null)
            {
                string label = $"Chapter {loadedChapters[0].chapter_order} – {loadedChapters[0].title}";
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
            chapterDropdown.choices = new List<string> { "Loading chapters..." };
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
                chapterDropdown.choices = new List<string> { "No chapter available" };
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
            labels.Add($"Chapter {order} – {chapter.title}");
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
            SaveLesson(false);
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

        return true;
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
        videoLinkStatusLabel.text = text;
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
        if (stepLabel != null) stepLabel.text = $"Step {currentStep} of {TotalSteps}";

        if (pageTitleLabel != null)
        {
            pageTitleLabel.text = currentStep switch
            {
                1 => "Location & Format",
                2 => "Asset Upload",
                3 => "Lesson Details",
                _ => "Create Lesson"
            };
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
                ? (isUpdateMode ? "Finish" : "Save & Publish")
                : "Next";
        }

        nextButton?.EnableInClassList("update-mode-finish", isUpdateMode && isLastStep);
        SetVisible(nextButtonIcon, !isLastStep);
        SetVisible(saveDraftButton, isLastStep && !isUpdateMode);
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
        field.tooltip = "Students will be able to...";

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

    private void HandleGenerateWithAi()
    {
        string title = lessonTitleField?.value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            SetLabel(detailsErrorLabel, "Enter a lesson title before generating content.");
            return;
        }

        if (lessonDescriptionField != null && string.IsNullOrWhiteSpace(lessonDescriptionField.value))
        {
            lessonDescriptionField.value =
                $"This lesson introduces {title}, explains the core concepts, and gives students guided practice before applying the topic independently.";
        }

        string[] generatedObjectives =
        {
            $"Explain the fundamental concepts of {title}",
            $"Apply the main principles of {title} in a practical activity"
        };

        while (objectiveFields.Count < generatedObjectives.Length)
            AddObjective();

        for (int i = 0; i < generatedObjectives.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(objectiveFields[i].value))
                objectiveFields[i].value = generatedObjectives[i];
        }

        ClearLabel(detailsErrorLabel);
    }

    private void PickDocuments()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Choose PDF Document", string.Empty, "pdf");
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
#else
        SetLabel(assetErrorLabel, "Android file picker is not installed yet.");
#endif
    }

    private void PickExercisePdf()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Choose Exercise PDF", string.Empty, "pdf");
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
#else
        SetLabel(assetErrorLabel, "Android file picker is not installed yet.");
#endif
    }

    private void PickModel()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Choose GLB 3D Model", string.Empty, "glb");
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
#else
        SetLabel(assetErrorLabel, "Android file picker is not installed yet.");
#endif
    }

    private void RemoveExercisePdf()
    {
        ExistingAssetData existing = existingAssets.Find(asset => asset.asset_type == "quiz_pdf" && !removedExistingAssetIds.Contains(asset.id));
        if (existing != null) removedExistingAssetIds.Add(existing.id);

        selectedExercisePath = string.Empty;

        if (exerciseFileLabel != null)
            exerciseFileLabel.text = "No exercise PDF selected";

        SetVisible(exerciseFileRow, false);
        ClearLabel(assetErrorLabel);
    }

    private void RemoveModel()
    {
        ExistingAssetData existing = existingAssets.Find(asset => asset.asset_type == "model_3d" && !removedExistingAssetIds.Contains(asset.id));
        if (existing != null) removedExistingAssetIds.Add(existing.id);

        selectedModelPath = string.Empty;

        if (modelFileLabel != null)
            modelFileLabel.text = "No 3D asset selected";

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
                "Đang phân tích Quiz PDF và lưu dữ liệu..."
            );

            ParseQuizPdfResponse quizResult = null;
            string quizError = null;

            string quizTitle =
                lessonTitleField?.value?.Trim();

            if (string.IsNullOrWhiteSpace(quizTitle))
            {
                quizTitle = "Bài tập";
            }

            yield return quizService.CallParseQuizFunctionDetailed(
                lessonId,
                quizTitle,
                selectedExercisePath,
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

            Debug.Log(
                "[CreateLessonPageController] Quiz update pipeline completed. " +
                $"Quiz ID: {quizResult.quiz_id}, " +
                $"Asset ID: {quizResult.lesson_asset_id}, " +
                $"R2: {quizResult.storage?.bucket}/{quizResult.storage?.path}"
            );

            SetSaveProgress(
                82f,
                "Quiz đã được lưu thành công."
            );
        }

        // 3. Upload 3D GLB Model lên Cloudflare R2
        if (!string.IsNullOrWhiteSpace(selectedModelPath))
        {
            string uploadedPath = null;
            string storagePath = $"{teacherId}/{classId}/{lessonId}/models/{Guid.NewGuid():N}.glb";
            error = null;

            yield return r2StorageService.UploadFile(
                "lesson-models", // R2 Bucket Name
                storagePath,
                selectedModelPath,
                "model/gltf-binary",
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
                asset_type = "model_3d",
                file_name = Path.GetFileName(selectedModelPath),
                storage_bucket = "lesson-models",
                storage_path = uploadedPath,
                mime_type = "model/gltf-binary",
                file_extension = ".glb",
                file_size_bytes = new FileInfo(selectedModelPath).Length,
                display_order = 0
            };

            yield return lessonService.CreateLessonAsset(asset, () => { }, message => error = message);
            if (!string.IsNullOrWhiteSpace(error))
            {
                FailSaving(error);
                yield break;
            }
        }
    }

    private void HandleSaveDraft()
    {
        if (!ValidateDetails()) return;
        SaveLesson(true);
    }

    private void SaveLesson(bool asDraft)
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

        StartCoroutine(CreateLessonRoutine(asDraft));
    }

    private IEnumerator CreateLessonRoutine(bool asDraft)
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
        string finalStatus = asDraft ? "draft" : "published";
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
                "Đang phân tích Quiz PDF và lưu dữ liệu..."
            );

            ParseQuizPdfResponse quizResult = null;
            string quizError = null;

            yield return quizService.CallParseQuizFunctionDetailed(
                createdLesson.id,
                createdLesson.title,
                selectedExercisePath,
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

            Debug.Log(
                "[CreateLessonPageController] Quiz create pipeline completed. " +
                $"Quiz ID: {quizResult.quiz_id}, " +
                $"Asset ID: {quizResult.lesson_asset_id}, " +
                $"R2: {quizResult.storage?.bucket}/{quizResult.storage?.path}"
            );
        }

        // 3. Upload 3D GLB Model lên Cloudflare R2
        if (modelSelected)
        {
            SetSaveProgress(
                CalculateUploadProgress(completedUploads, totalUploads),
                "Uploading 3D model to R2..."
            );

            string storagePath = $"{teacherId}/{classId}/{createdLesson.id}/models/{Guid.NewGuid():N}.glb";
            string uploadedPath = null;
            operationError = null;

            yield return r2StorageService.UploadFile(
                "lesson-models", // R2 Bucket Name
                storagePath,
                selectedModelPath,
                "model/gltf-binary",
                path => uploadedPath = path,
                error => operationError = error
            );

            if (!string.IsNullOrWhiteSpace(operationError))
            {
                FailSaving(operationError);
                yield break;
            }

            LessonAssetInsert modelAsset = new()
            {
                lesson_id = createdLesson.id,
                uploaded_by = teacherId,
                asset_type = "model_3d",
                file_name = Path.GetFileName(selectedModelPath),
                storage_bucket = "lesson-models",
                storage_path = uploadedPath,
                mime_type = "model/gltf-binary",
                file_extension = ".glb",
                file_size_bytes = new FileInfo(selectedModelPath).Length,
                display_order = 0
            };

            yield return lessonService.CreateLessonAsset(
                modelAsset,
                () => { },
                error => operationError = error
            );

            if (!string.IsNullOrWhiteSpace(operationError))
            {
                FailSaving(operationError);
                yield break;
            }
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

        SetSaveProgress(100f, asDraft ? "Draft saved." : "Lesson published.");
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
        saveDraftButton?.SetEnabled(!saving);
        cancelButton?.SetEnabled(!saving);
        backButton?.SetEnabled(!saving);

        SetVisible(saveProgressContainer, saving);
    }

    private void SetSaveProgress(float value, string text)
    {
        if (saveProgressBar != null)
            saveProgressBar.value = Mathf.Clamp(value, 0f, 100f);

        if (saveProgressLabel != null)
            saveProgressLabel.text = text;
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
        selectedDocumentPaths.Clear();

        youtubeUrlField?.SetValueWithoutNotify(string.Empty);
        lessonTitleField?.SetValueWithoutNotify(string.Empty);
        lessonDescriptionField?.SetValueWithoutNotify(string.Empty);

        UpdateFormatVisual(videoFormatButton, videoRadio, false);
        UpdateFormatVisual(modelFormatButton, modelRadio, false);
        UpdateFormatVisual(documentFormatButton, documentRadio, false);

        documentChipContainer?.Clear();
        SetVideoStatus(string.Empty, false);

        if (exerciseFileLabel != null)
            exerciseFileLabel.text = "No exercise PDF selected";

        if (modelFileLabel != null)
            modelFileLabel.text = "No 3D asset selected";

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
        if (label != null) label.text = message;
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
#endregion