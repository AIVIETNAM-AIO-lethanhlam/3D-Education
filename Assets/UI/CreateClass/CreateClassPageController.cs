using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class CreateClassPageController : MonoBehaviour
{
    private const int TotalSteps = 3;

    private VisualElement root;

    // Header
    private Button backButton;
    private Button cancelButton;
    private Label stepLabel;
    private Label pageTitleLabel;

    // Progress
    private VisualElement progressStep1;
    private VisualElement progressStep2;
    private VisualElement progressStep3;

    // Panels
    private VisualElement basicInfoStep;
    private VisualElement brandingStep;
    private VisualElement reviewStep;

    // Step 1
    private TextField courseCodeField;
    private TextField courseNameField;
    private VisualElement visibilityDropdownRoot;
    private Button visibilityDropdownButton;
    private Label visibilitySelectedLabel;
    private VisualElement visibilityArrow;
    private VisualElement visibilityOptionsPanel;
    private Button visibilityPublicButton;
    private Button visibilityPrivateButton;
    private string selectedVisibility = string.Empty;

    private VisualElement categoryDropdownRoot;
    private Button categoryDropdownButton;
    private Label categorySelectedLabel;
    private VisualElement categoryArrow;
    private VisualElement categoryOptionsPanel;
    private ScrollView categoryOptionsScroll;
    private VisualElement categoryOptionsContainer;
    private Label categoryLoadingLabel;

    private string selectedCategoryId = string.Empty;
    private string selectedCategoryName = string.Empty;
    private readonly List<Button> categoryButtons = new();

    private Label basicInfoErrorLabel;

    // Step 2
    private Button uploadCoverButton;
    private VisualElement selectedCoverPreview;
    private VisualElement selectedCoverImage;
    private Button removeCoverButton;
    private Label brandingErrorLabel;
    private VisualElement brandingPreviewSection;

    private Button templateBlueButton;
    private Button templateDarkButton;
    private Button templatePurpleButton;
    private Button templateRedButton;

    private ScrollView templateScrollView;
    private Button templateScrollLeftButton;
    private Button templateScrollRightButton;
    private VisualElement templateScrollTrack;
    private VisualElement templateScrollThumb;

    private Button selectedTemplateButton;
    private bool isDraggingTemplateThumb;
    private bool isCreatingClass;
    private float templateThumbPointerOffset;

    // Step 3
    private VisualElement reviewCoverImage;
    private Label reviewCourseCode;
    private Label reviewCourseName;
    private Label reviewVisibility;

    private TextField courseDescriptionField;
    private Button voiceInputButton;
    private Label summaryCourseCodeLabel;
    private Label summaryCourseNameLabel;
    private Label summaryVisibilityLabel;

    // Bottom
    private Button nextButton;

    private int currentStep = 1;

    private string selectedTemplateClass = string.Empty;
    private Texture2D selectedCoverTexture;

    private readonly List<Button> templateButtons = new();

    private readonly string[] templateClasses =
    {
        "template-blue",
        "template-dark",
        "template-purple",
        "template-red"
    };

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
        {
            Debug.LogError(
                "CreateClassScene không tìm thấy UIDocument."
            );

            return;
        }

        root = document.rootVisualElement;

        if (root == null)
        {
            Debug.LogError(
                "rootVisualElement của CreateClassScene đang null."
            );

            return;
        }

        QueryElements();
        ConfigureVisibilityDropdown();
        ConfigureCategoryDropdown();
        ConfigureCategoryScrollView();
        RegisterEvents();
        LoadCategories();

        ShowStep(1);
        SetBrandingPreviewVisible(false);

        root.schedule.Execute(UpdateTemplateScrollbar)
            .StartingIn(100);
    }

    private void OnDisable()
    {
        UnregisterEvents();
    }

    private void QueryElements()
    {
        backButton = root.Q<Button>("back-button");
        cancelButton = root.Q<Button>("cancel-button");

        stepLabel = root.Q<Label>("step-label");
        pageTitleLabel = root.Q<Label>("page-title-label");

        progressStep1 =
            root.Q<VisualElement>("progress-step-1");

        progressStep2 =
            root.Q<VisualElement>("progress-step-2");

        progressStep3 =
            root.Q<VisualElement>("progress-step-3");

        basicInfoStep =
            root.Q<VisualElement>("basic-info-step");

        brandingStep =
            root.Q<VisualElement>("branding-step");

        reviewStep =
            root.Q<VisualElement>("review-step");

        courseCodeField =
            root.Q<TextField>("course-code-field");

        courseNameField =
            root.Q<TextField>("course-name-field");

        visibilityDropdownRoot =
            root.Q<VisualElement>("visibility-dropdown-root");

        visibilityDropdownButton =
            root.Q<Button>("visibility-dropdown-button");

        visibilitySelectedLabel =
            root.Q<Label>("visibility-selected-label");

        visibilityArrow =
            root.Q<VisualElement>("visibility-arrow");

        visibilityOptionsPanel =
            root.Q<VisualElement>("visibility-options-panel");

        visibilityPublicButton =
            root.Q<Button>("visibility-public-button");

        visibilityPrivateButton =
            root.Q<Button>("visibility-private-button");

        categoryDropdownRoot =
            root.Q<VisualElement>("category-dropdown-root");

        categoryDropdownButton =
            root.Q<Button>("category-dropdown-button");

        categorySelectedLabel =
            root.Q<Label>("category-selected-label");

        categoryArrow =
            root.Q<VisualElement>("category-arrow");

        categoryOptionsPanel =
            root.Q<VisualElement>("category-options-panel");

        categoryOptionsScroll =
            root.Q<ScrollView>("category-options-scroll");

        categoryOptionsContainer =
            root.Q<VisualElement>("category-options-container");

        categoryLoadingLabel =
            root.Q<Label>("category-loading-label");

        basicInfoErrorLabel =
            root.Q<Label>("basic-info-error-label");

        uploadCoverButton =
            root.Q<Button>("upload-cover-button");

        selectedCoverPreview =
            root.Q<VisualElement>("selected-cover-preview");

        selectedCoverImage =
            root.Q<VisualElement>("selected-cover-image");

        removeCoverButton =
            root.Q<Button>("remove-cover-button");

        brandingErrorLabel =
            root.Q<Label>("branding-error-label");

        brandingPreviewSection =
            root.Q<VisualElement>("branding-preview-section");

        templateBlueButton =
            root.Q<Button>("template-blue-button");

        templateDarkButton =
            root.Q<Button>("template-dark-button");

        templatePurpleButton =
            root.Q<Button>("template-purple-button");

        templateRedButton =
            root.Q<Button>("template-red-button");

        templateScrollView =
            root.Q<ScrollView>("template-scroll-view");

        templateScrollLeftButton =
            root.Q<Button>("template-scroll-left-button");

        templateScrollRightButton =
            root.Q<Button>("template-scroll-right-button");

        templateScrollTrack =
            root.Q<VisualElement>("template-scroll-track");

        templateScrollThumb =
            root.Q<VisualElement>("template-scroll-thumb");

        reviewCoverImage =
            root.Q<VisualElement>("review-cover-image");

        reviewCourseCode =
            root.Q<Label>("review-course-code");

        reviewCourseName =
            root.Q<Label>("review-course-name");

        reviewVisibility =
            root.Q<Label>("review-visibility");

        courseDescriptionField =
            root.Q<TextField>("course-description-field");

        voiceInputButton =
            root.Q<Button>("voice-input-button");

        summaryCourseCodeLabel =
            root.Q<Label>("summary-course-code-label");

        summaryCourseNameLabel =
            root.Q<Label>("summary-course-name-label");

        summaryVisibilityLabel =
            root.Q<Label>("summary-visibility-label");

        nextButton = root.Q<Button>("next-button");

        templateButtons.Clear();

        templateButtons.Add(templateBlueButton);
        templateButtons.Add(templateDarkButton);
        templateButtons.Add(templatePurpleButton);
        templateButtons.Add(templateRedButton);
    }

    private void ConfigureVisibilityDropdown()
    {
        selectedVisibility = string.Empty;

        if (visibilitySelectedLabel != null)
        {
            visibilitySelectedLabel.text = "Select visibility...";
            visibilitySelectedLabel.RemoveFromClassList(
                "custom-visibility-selected-label-active"
            );
        }

        SetVisibilityDropdownOpen(false);
        UpdateVisibilityOptionSelection();
    }

    private void ConfigureCategoryDropdown()
    {
        selectedCategoryId = string.Empty;
        selectedCategoryName = string.Empty;

        if (categorySelectedLabel != null)
        {
            categorySelectedLabel.text = "Select category...";
            categorySelectedLabel.RemoveFromClassList(
                "custom-visibility-selected-label-active"
            );
        }

        SetCategoryDropdownOpen(false);
        SetCategoryLoadingState("Loading categories...");
    }

    private void ConfigureCategoryScrollView()
    {
        if (categoryOptionsScroll == null)
        {
            return;
        }

        categoryOptionsScroll.verticalScrollerVisibility =
            ScrollerVisibility.Auto;

        categoryOptionsScroll.horizontalScrollerVisibility =
            ScrollerVisibility.Hidden;
    }

    private void RegisterEvents()
    {
        if (backButton != null)
        {
            backButton.clicked += HandleBackClicked;
        }

        if (cancelButton != null)
        {
            cancelButton.clicked += HandleCancelClicked;
        }

        if (nextButton != null)
        {
            nextButton.clicked += HandleNextClicked;
        }

        if (visibilityDropdownButton != null)
        {
            visibilityDropdownButton.clicked +=
                HandleVisibilityDropdownClicked;
        }

        if (visibilityPublicButton != null)
        {
            visibilityPublicButton.clicked += SelectPublicVisibility;
        }

        if (visibilityPrivateButton != null)
        {
            visibilityPrivateButton.clicked += SelectPrivateVisibility;
        }

        if (categoryDropdownButton != null)
        {
            categoryDropdownButton.clicked +=
                HandleCategoryDropdownClicked;
        }

        if (uploadCoverButton != null)
        {
            uploadCoverButton.clicked += HandleUploadCoverClicked;
        }

        if (removeCoverButton != null)
        {
            removeCoverButton.clicked += HandleRemoveCoverClicked;
        }

        if (templateBlueButton != null)
        {
            templateBlueButton.clicked += SelectBlueTemplate;
        }

        if (templateDarkButton != null)
        {
            templateDarkButton.clicked += SelectDarkTemplate;
        }

        if (templatePurpleButton != null)
        {
            templatePurpleButton.clicked += SelectPurpleTemplate;
        }

        if (templateRedButton != null)
        {
            templateRedButton.clicked += SelectRedTemplate;
        }

        if (templateScrollLeftButton != null)
        {
            templateScrollLeftButton.clicked += ScrollTemplatesLeft;
        }

        if (templateScrollRightButton != null)
        {
            templateScrollRightButton.clicked += ScrollTemplatesRight;
        }

        if (templateScrollThumb != null)
        {
            templateScrollThumb.RegisterCallback<PointerDownEvent>(
                OnTemplateThumbPointerDown
            );

            templateScrollThumb.RegisterCallback<PointerMoveEvent>(
                OnTemplateThumbPointerMove
            );

            templateScrollThumb.RegisterCallback<PointerUpEvent>(
                OnTemplateThumbPointerUp
            );
        }

        if (templateScrollTrack != null)
        {
            templateScrollTrack.RegisterCallback<GeometryChangedEvent>(
                OnTemplateScrollbarGeometryChanged
            );
        }

        if (templateScrollView != null)
        {
            templateScrollView.RegisterCallback<WheelEvent>(
                OnTemplateScrollWheel
            );
        }

        if (courseCodeField != null)
        {
            courseCodeField.RegisterValueChangedCallback(
                OnCourseCodeChanged
            );
        }

        if (courseNameField != null)
        {
            courseNameField.RegisterValueChangedCallback(
                OnCourseNameChanged
            );
        }

        if (voiceInputButton != null)
        {
            voiceInputButton.clicked += HandleVoiceInputClicked;
        }
    }

    private void UnregisterEvents()
    {
        if (backButton != null)
        {
            backButton.clicked -= HandleBackClicked;
        }

        if (cancelButton != null)
        {
            cancelButton.clicked -= HandleCancelClicked;
        }

        if (nextButton != null)
        {
            nextButton.clicked -= HandleNextClicked;
        }

        if (visibilityDropdownButton != null)
        {
            visibilityDropdownButton.clicked -=
                HandleVisibilityDropdownClicked;
        }

        if (visibilityPublicButton != null)
        {
            visibilityPublicButton.clicked -= SelectPublicVisibility;
        }

        if (visibilityPrivateButton != null)
        {
            visibilityPrivateButton.clicked -= SelectPrivateVisibility;
        }

        if (categoryDropdownButton != null)
        {
            categoryDropdownButton.clicked -=
                HandleCategoryDropdownClicked;
        }

        if (uploadCoverButton != null)
        {
            uploadCoverButton.clicked -= HandleUploadCoverClicked;
        }

        if (removeCoverButton != null)
        {
            removeCoverButton.clicked -= HandleRemoveCoverClicked;
        }

        if (templateBlueButton != null)
        {
            templateBlueButton.clicked -= SelectBlueTemplate;
        }

        if (templateDarkButton != null)
        {
            templateDarkButton.clicked -= SelectDarkTemplate;
        }

        if (templatePurpleButton != null)
        {
            templatePurpleButton.clicked -= SelectPurpleTemplate;
        }

        if (templateRedButton != null)
        {
            templateRedButton.clicked -= SelectRedTemplate;
        }

        if (templateScrollLeftButton != null)
        {
            templateScrollLeftButton.clicked -= ScrollTemplatesLeft;
        }

        if (templateScrollRightButton != null)
        {
            templateScrollRightButton.clicked -= ScrollTemplatesRight;
        }

        if (templateScrollThumb != null)
        {
            templateScrollThumb.UnregisterCallback<PointerDownEvent>(
                OnTemplateThumbPointerDown
            );

            templateScrollThumb.UnregisterCallback<PointerMoveEvent>(
                OnTemplateThumbPointerMove
            );

            templateScrollThumb.UnregisterCallback<PointerUpEvent>(
                OnTemplateThumbPointerUp
            );
        }

        if (templateScrollTrack != null)
        {
            templateScrollTrack.UnregisterCallback<GeometryChangedEvent>(
                OnTemplateScrollbarGeometryChanged
            );
        }

        if (templateScrollView != null)
        {
            templateScrollView.UnregisterCallback<WheelEvent>(
                OnTemplateScrollWheel
            );
        }

        if (courseCodeField != null)
        {
            courseCodeField.UnregisterValueChangedCallback(
                OnCourseCodeChanged
            );
        }

        if (courseNameField != null)
        {
            courseNameField.UnregisterValueChangedCallback(
                OnCourseNameChanged
            );
        }

        if (voiceInputButton != null)
        {
            voiceInputButton.clicked -= HandleVoiceInputClicked;
        }
    }

    private void HandleVisibilityDropdownClicked()
    {
        bool isOpen =
            visibilityOptionsPanel != null &&
            !visibilityOptionsPanel.ClassListContains("hidden");

        if (!isOpen)
        {
            SetCategoryDropdownOpen(false);
        }

        SetVisibilityDropdownOpen(!isOpen);
    }

    private void SelectPublicVisibility()
    {
        SelectVisibility("public", "Public");
    }

    private void SelectPrivateVisibility()
    {
        SelectVisibility("private", "Private");
    }

    private void SelectVisibility(string value, string displayText)
    {
        selectedVisibility = value;

        if (visibilitySelectedLabel != null)
        {
            visibilitySelectedLabel.text = displayText;
            visibilitySelectedLabel.AddToClassList(
                "custom-visibility-selected-label-active"
            );
        }

        if (basicInfoErrorLabel != null)
        {
            basicInfoErrorLabel.text = string.Empty;
        }

        UpdateVisibilityOptionSelection();
        SetVisibilityDropdownOpen(false);
    }

    private void SetVisibilityDropdownOpen(bool isOpen)
    {
        SetElementVisible(visibilityOptionsPanel, isOpen);

        visibilityDropdownRoot?.EnableInClassList(
            "custom-visibility-dropdown-open",
            isOpen
        );

        visibilityDropdownButton?.EnableInClassList(
            "custom-visibility-button-open",
            isOpen
        );

        visibilityArrow?.EnableInClassList(
            "custom-visibility-arrow-open",
            isOpen
        );
    }

    private void UpdateVisibilityOptionSelection()
    {
        bool isPublic = string.Equals(
            selectedVisibility,
            "public",
            StringComparison.OrdinalIgnoreCase
        );

        bool isPrivate = string.Equals(
            selectedVisibility,
            "private",
            StringComparison.OrdinalIgnoreCase
        );

        visibilityPublicButton?.EnableInClassList(
            "custom-visibility-option-selected",
            isPublic
        );

        visibilityPrivateButton?.EnableInClassList(
            "custom-visibility-option-selected",
            isPrivate
        );
    }

    private void HandleCategoryDropdownClicked()
    {
        bool isOpen =
            categoryOptionsPanel != null &&
            !categoryOptionsPanel.ClassListContains("hidden");

        if (!isOpen)
        {
            SetVisibilityDropdownOpen(false);
        }

        SetCategoryDropdownOpen(!isOpen);
    }

    private void SetCategoryDropdownOpen(bool isOpen)
    {
        SetElementVisible(categoryOptionsPanel, isOpen);

        categoryDropdownRoot?.EnableInClassList(
            "custom-visibility-dropdown-open",
            isOpen
        );

        categoryDropdownButton?.EnableInClassList(
            "custom-visibility-button-open",
            isOpen
        );

        categoryArrow?.EnableInClassList(
            "custom-visibility-arrow-open",
            isOpen
        );
    }

    private void LoadCategories()
    {
        StartCoroutine(LoadCategoriesCoroutine(false));
    }

    private IEnumerator LoadCategoriesCoroutine(bool isRetry)
    {
        SetCategoryLoadingState(
            isRetry
                ? "Retrying categories..."
                : "Loading categories..."
        );

        bool requestCompleted = false;
        string responseJson = null;
        string requestError = null;

        yield return SupabaseRestService.Get(
            "categories?select=id,name&order=name.asc",
            json =>
            {
                responseJson = json;
                requestCompleted = true;
            },
            error =>
            {
                requestError = error;
                requestCompleted = true;
            }
        );

        if (!requestCompleted)
        {
            SetCategoryLoadingState(
                "Unable to load categories."
            );

            Debug.LogError(
                "Supabase categories request did not complete."
            );

            yield break;
        }

        if (!string.IsNullOrWhiteSpace(requestError))
        {
            SetCategoryLoadingState(
                "Unable to load categories."
            );

            Debug.LogError(
                "Không tải được categories từ Supabase: " +
                requestError
            );

            yield break;
        }

        Debug.Log(
            "Categories response từ Supabase:\n" +
            (responseJson ?? "<null>")
        );

        if (!TryParseCategories(
                responseJson,
                out SupabaseCategory[] categories,
                out string parseError))
        {
            SetCategoryLoadingState(
                "Unable to load categories."
            );

            Debug.LogError(parseError);
            yield break;
        }

        if ((categories == null || categories.Length == 0) &&
            !isRetry)
        {
            yield return new WaitForSecondsRealtime(0.35f);
            yield return LoadCategoriesCoroutine(true);
            yield break;
        }

        if (categories == null || categories.Length == 0)
        {
            SetCategoryLoadingState(
                "No categories returned. Check the categories SELECT policy."
            );

            Debug.LogWarning(
                "Supabase returned [] for categories. " +
                "The table contains data, so the likely cause is an RLS " +
                "SELECT policy that does not allow the current user to read it."
            );

            yield break;
        }

        BuildCategoryOptions(categories);
    }

    private void BuildCategoryOptions(SupabaseCategory[] categories)
    {
        categoryButtons.Clear();
        categoryOptionsContainer?.Clear();

        if (categories == null || categories.Length == 0)
        {
            SetCategoryLoadingState("No categories available.");
            return;
        }

        SetElementVisible(categoryLoadingLabel, false);
        SetElementVisible(categoryOptionsScroll, true);

        if (categoryOptionsScroll != null)
        {
            categoryOptionsScroll.scrollOffset = Vector2.zero;
        }

        for (int index = 0; index < categories.Length; index++)
        {
            SupabaseCategory category = categories[index];

            if (category == null ||
                string.IsNullOrWhiteSpace(category.id) ||
                string.IsNullOrWhiteSpace(category.name))
            {
                continue;
            }

            Button optionButton = new Button();
            optionButton.text = category.name.Trim();
            optionButton.AddToClassList("custom-category-option");

            string categoryId = category.id.Trim();
            string categoryName = category.name.Trim();

            optionButton.clicked += () =>
                SelectCategory(
                    categoryId,
                    categoryName,
                    optionButton
                );

            categoryButtons.Add(optionButton);
            categoryOptionsContainer?.Add(optionButton);

            if (index < categories.Length - 1)
            {
                VisualElement divider = new VisualElement();
                divider.AddToClassList("custom-category-divider");
                categoryOptionsContainer?.Add(divider);
            }
        }

        if (categoryButtons.Count == 0)
        {
            SetCategoryLoadingState("No categories available.");
        }
    }

    private void SelectCategory(
        string categoryId,
        string categoryName,
        Button selectedButton)
    {
        selectedCategoryId = categoryId;
        selectedCategoryName = categoryName;

        if (categorySelectedLabel != null)
        {
            categorySelectedLabel.text = categoryName;
            categorySelectedLabel.AddToClassList(
                "custom-visibility-selected-label-active"
            );
        }

        foreach (Button button in categoryButtons)
        {
            button?.EnableInClassList(
                "custom-category-option-selected",
                button == selectedButton
            );
        }

        if (basicInfoErrorLabel != null)
        {
            basicInfoErrorLabel.text = string.Empty;
        }

        SetCategoryDropdownOpen(false);
    }

    private void SetCategoryLoadingState(string message)
    {
        if (categoryLoadingLabel != null)
        {
            categoryLoadingLabel.text = message;
            SetElementVisible(categoryLoadingLabel, true);
        }

        categoryOptionsContainer?.Clear();
        categoryButtons.Clear();
        SetElementVisible(categoryOptionsScroll, false);
    }

    private static bool TryParseCategories(
        string json,
        out SupabaseCategory[] categories,
        out string error)
    {
        categories = Array.Empty<SupabaseCategory>();

        string trimmed = json?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Supabase trả về danh sách categories rỗng.";
            return false;
        }

        try
        {
            string jsonToParse = trimmed.StartsWith("[")
                ? "{\"items\":" + trimmed + "}"
                : trimmed;

            SupabaseCategoryArrayWrapper wrapper =
                JsonUtility.FromJson<SupabaseCategoryArrayWrapper>(
                    jsonToParse
                );

            categories =
                wrapper?.items ?? Array.Empty<SupabaseCategory>();

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error =
                "Không parse được categories: " +
                exception.Message;

            return false;
        }
    }

    private void HandleNextClicked()
    {
        switch (currentStep)
        {
            case 1:
                if (!ValidateBasicInfo())
                {
                    return;
                }

                ShowStep(2);
                break;

            case 2:
                if (!ValidateBranding())
                {
                    return;
                }

                UpdateReviewInformation();
                ShowStep(3);
                break;

            case 3:
                CreateClass();
                break;
        }
    }

    private void HandleBackClicked()
    {
        if (currentStep > 1)
        {
            ShowStep(currentStep - 1);
            return;
        }

        ReturnToMyClasses();
    }

    private void HandleCancelClicked()
    {
        ClearUnsavedCreateClassData();
        ReturnToMyClasses();
    }

    private void ClearUnsavedCreateClassData()
    {
        // Step 1
        courseCodeField?.SetValueWithoutNotify(string.Empty);
        courseNameField?.SetValueWithoutNotify(string.Empty);

        selectedVisibility = string.Empty;

        if (visibilitySelectedLabel != null)
        {
            visibilitySelectedLabel.text = "Select visibility...";
            visibilitySelectedLabel.RemoveFromClassList(
                "custom-visibility-selected-label-active"
            );
        }

        SetVisibilityDropdownOpen(false);
        UpdateVisibilityOptionSelection();

        selectedCategoryId = string.Empty;
        selectedCategoryName = string.Empty;

        if (categorySelectedLabel != null)
        {
            categorySelectedLabel.text = "Select category...";
            categorySelectedLabel.RemoveFromClassList(
                "custom-visibility-selected-label-active"
            );
        }

        foreach (Button button in categoryButtons)
        {
            button?.RemoveFromClassList(
                "custom-category-option-selected"
            );
        }

        SetCategoryDropdownOpen(false);

        // Step 2
        if (selectedCoverTexture != null)
        {
            Destroy(selectedCoverTexture);
            selectedCoverTexture = null;
        }

        if (selectedCoverImage != null)
        {
            selectedCoverImage.style.backgroundImage =
                StyleKeyword.None;
        }

        SetElementVisible(uploadCoverButton, true);
        SetElementVisible(selectedCoverPreview, false);

        ClearTemplateSelection();
        ClearReviewCover();
        SetBrandingPreviewVisible(false);

        if (brandingErrorLabel != null)
        {
            brandingErrorLabel.text = string.Empty;
        }

        // Step 3
        courseDescriptionField?.SetValueWithoutNotify(
            string.Empty
        );

        if (summaryCourseCodeLabel != null)
        {
            summaryCourseCodeLabel.text = "No code set";
        }

        if (summaryCourseNameLabel != null)
        {
            summaryCourseNameLabel.text = "No name set";
        }

        if (summaryVisibilityLabel != null)
        {
            summaryVisibilityLabel.text =
                "No visibility set";
        }

        if (basicInfoErrorLabel != null)
        {
            basicInfoErrorLabel.text = string.Empty;
        }

        currentStep = 1;

        Debug.Log(
            "Đã hủy tạo lớp và xóa toàn bộ dữ liệu chưa lưu."
        );
    }

    private void ShowStep(int step)
    {
        currentStep = Mathf.Clamp(step, 1, TotalSteps);

        SetElementVisible(basicInfoStep, currentStep == 1);
        SetElementVisible(brandingStep, currentStep == 2);
        SetElementVisible(reviewStep, currentStep == 3);

        UpdateHeader();
        UpdateProgress();
        UpdateBottomButton();

        if (currentStep == 2)
        {
            bool hasBrandingSelection =
                selectedCoverTexture != null ||
                !string.IsNullOrWhiteSpace(selectedTemplateClass);

            SetBrandingPreviewVisible(hasBrandingSelection);

            if (hasBrandingSelection)
            {
                UpdateReviewInformation();
            }

            root.schedule.Execute(UpdateTemplateScrollbar)
                .StartingIn(50);
        }

        if (currentStep == 3)
        {
            UpdateOverviewSummary();
        }
    }

    private void UpdateHeader()
    {
        if (stepLabel != null)
        {
            stepLabel.text =
                $"Step {currentStep} of {TotalSteps}";
        }

        if (pageTitleLabel == null)
        {
            return;
        }

        pageTitleLabel.text = currentStep switch
        {
            1 => "Basic Info",
            2 => "Branding",
            3 => "Overview",
            _ => "Create Class"
        };
    }

    private void UpdateProgress()
    {
        SetProgressActive(
            progressStep1,
            currentStep >= 1
        );

        SetProgressActive(
            progressStep2,
            currentStep >= 2
        );

        SetProgressActive(
            progressStep3,
            currentStep >= 3
        );
    }

    private static void SetProgressActive(
        VisualElement progressElement,
        bool isActive
    )
    {
        if (progressElement == null)
        {
            return;
        }

        progressElement.EnableInClassList(
            "progress-segment-active",
            isActive
        );
    }

    private void UpdateBottomButton()
    {
        if (nextButton == null)
        {
            return;
        }

        nextButton.text =
            currentStep == TotalSteps
                ? "Create Class"
                : "Next";
    }

    private bool ValidateBasicInfo()
    {
        if (basicInfoErrorLabel != null)
        {
            basicInfoErrorLabel.text = string.Empty;
        }

        string courseCode =
            courseCodeField?.value?.Trim() ?? string.Empty;

        string courseName =
            courseNameField?.value?.Trim() ?? string.Empty;

        string visibility =
            selectedVisibility?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(courseCode))
        {
            ShowBasicInfoError(
                "Please enter the course code."
            );

            courseCodeField?.Focus();

            return false;
        }

        if (string.IsNullOrWhiteSpace(courseName))
        {
            ShowBasicInfoError(
                "Please enter the course name."
            );

            courseNameField?.Focus();

            return false;
        }

        if (string.IsNullOrWhiteSpace(visibility))
        {
            ShowBasicInfoError(
                "Please select class visibility."
            );

            return false;
        }

        if (string.IsNullOrWhiteSpace(selectedCategoryId))
        {
            ShowBasicInfoError(
                "Please select a category."
            );

            return false;
        }

        return true;
    }

    private bool ValidateBranding()
    {
        if (brandingErrorLabel != null)
        {
            brandingErrorLabel.text = string.Empty;
        }

        /*
         * Một lớp hợp lệ khi người dùng:
         * - Chọn ảnh riêng; hoặc
         * - Chọn một template.
         */

        bool hasUploadedCover =
            selectedCoverTexture != null;

        bool hasTemplate =
            !string.IsNullOrWhiteSpace(selectedTemplateClass);

        if (!hasUploadedCover && !hasTemplate)
        {
            if (brandingErrorLabel != null)
            {
                brandingErrorLabel.text =
                    "Please upload a cover image or choose a template.";
            }

            return false;
        }

        return true;
    }

    private void ShowBasicInfoError(string message)
    {
        if (basicInfoErrorLabel != null)
        {
            basicInfoErrorLabel.text = message;
        }
    }

    private void HandleUploadCoverClicked()
    {
#if UNITY_EDITOR
        OpenImagePickerInEditor();
#else
        /*
         * Trên Android, Unity UI Toolkit không tự mở thư viện ảnh.
         * Sau này có thể tích hợp NativeGallery.
         */
        Debug.Log(
            "Cần tích hợp NativeGallery để chọn ảnh trên Android."
        );
#endif
    }

#if UNITY_EDITOR
    private void OpenImagePickerInEditor()
    {
        string path = UnityEditor.EditorUtility.OpenFilePanel(
            "Choose Cover Image",
            string.Empty,
            "png,jpg,jpeg"
        );

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            byte[] imageBytes =
                System.IO.File.ReadAllBytes(path);

            Texture2D texture = new Texture2D(2, 2);

            bool loaded =
                texture.LoadImage(imageBytes);

            if (!loaded)
            {
                Destroy(texture);

                Debug.LogError(
                    "Không thể đọc ảnh cover đã chọn."
                );

                return;
            }

            SetUploadedCover(texture);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Lỗi khi tải cover image: {exception.Message}"
            );
        }
    }
#endif

    private void SetUploadedCover(Texture2D texture)
    {
        if (selectedCoverTexture != null)
        {
            Destroy(selectedCoverTexture);
        }

        selectedCoverTexture = texture;

        if (selectedCoverImage != null)
        {
            selectedCoverImage.style.backgroundImage =
                new StyleBackground(selectedCoverTexture);
        }

        SetElementVisible(uploadCoverButton, false);
        SetElementVisible(selectedCoverPreview, true);

        ClearTemplateSelection();
        UpdateReviewInformation();
        SetBrandingPreviewVisible(true);
    }

    private void HandleRemoveCoverClicked()
    {
        if (selectedCoverTexture != null)
        {
            Destroy(selectedCoverTexture);
            selectedCoverTexture = null;
        }

        if (selectedCoverImage != null)
        {
            selectedCoverImage.style.backgroundImage =
                StyleKeyword.None;
        }

        SetElementVisible(uploadCoverButton, true);
        SetElementVisible(selectedCoverPreview, false);

        ClearTemplateSelection();
        ClearReviewCover();
        SetBrandingPreviewVisible(false);
    }

    private void SelectBlueTemplate()
    {
        SelectTemplate(
            templateBlueButton,
            "template-blue"
        );
    }

    private void SelectDarkTemplate()
    {
        SelectTemplate(
            templateDarkButton,
            "template-dark"
        );
    }

    private void SelectPurpleTemplate()
    {
        SelectTemplate(
            templatePurpleButton,
            "template-purple"
        );
    }

    private void SelectRedTemplate()
    {
        SelectTemplate(
            templateRedButton,
            "template-red"
        );
    }

    private void SelectTemplate(
        Button selectedButton,
        string templateClass
    )
    {
        if (selectedCoverTexture != null)
        {
            Destroy(selectedCoverTexture);
            selectedCoverTexture = null;
        }

        SetElementVisible(uploadCoverButton, true);
        SetElementVisible(selectedCoverPreview, false);

        ClearTemplateSelection();

        selectedTemplateClass = templateClass;
        selectedTemplateButton = selectedButton;

        selectedButton?.AddToClassList(
            "template-selected"
        );

        UpdateReviewInformation();
        SetBrandingPreviewVisible(true);
    }

    private void ClearTemplateSelection()
    {
        foreach (Button button in templateButtons)
        {
            button?.RemoveFromClassList(
                "template-selected"
            );
        }

        selectedTemplateClass = string.Empty;
        selectedTemplateButton = null;
    }

    private void SetBrandingPreviewVisible(bool visible)
    {
        SetElementVisible(brandingPreviewSection, visible);
    }

    private void ClearReviewCover()
    {
        if (reviewCoverImage == null)
        {
            return;
        }

        foreach (string templateClass in templateClasses)
        {
            reviewCoverImage.RemoveFromClassList(templateClass);
        }

        reviewCoverImage.style.backgroundImage = StyleKeyword.None;
    }

    private void UpdateReviewInformation()
    {
        string courseCode =
            courseCodeField?.value?.Trim().ToUpper() ?? string.Empty;

        string courseName =
            courseNameField?.value?.Trim() ?? string.Empty;

        string visibility =
            selectedVisibility?.Trim() ?? string.Empty;

        if (visibility == "Select visibility...")
        {
            visibility = string.Empty;
        }

        if (reviewCourseCode != null)
        {
            reviewCourseCode.text = courseCode;
        }

        if (reviewCourseName != null)
        {
            reviewCourseName.text = courseName;
        }

        if (reviewVisibility != null)
        {
            reviewVisibility.text = visibility;
        }

        UpdateReviewCover();
    }

    private void UpdateReviewCover()
    {
        if (reviewCoverImage == null)
        {
            return;
        }

        foreach (string templateClass in templateClasses)
        {
            reviewCoverImage.RemoveFromClassList(templateClass);
        }

        if (selectedCoverTexture != null)
        {
            reviewCoverImage.style.backgroundImage =
                new StyleBackground(selectedCoverTexture);

            return;
        }

        if (selectedTemplateButton != null)
        {
            reviewCoverImage.style.backgroundImage =
                selectedTemplateButton.resolvedStyle.backgroundImage;

            return;
        }

        reviewCoverImage.style.backgroundImage =
            StyleKeyword.None;
    }

    private void ScrollTemplatesLeft()
    {
        ScrollTemplatesBy(-90f);
    }

    private void ScrollTemplatesRight()
    {
        ScrollTemplatesBy(90f);
    }

    private void ScrollTemplatesBy(float amount)
    {
        if (templateScrollView == null)
        {
            return;
        }

        float maxScroll = GetTemplateMaxScroll();
        float newX = Mathf.Clamp(
            templateScrollView.scrollOffset.x + amount,
            0f,
            maxScroll
        );

        templateScrollView.scrollOffset = new Vector2(newX, 0f);
        UpdateTemplateScrollbar();
    }

    private float GetTemplateMaxScroll()
    {
        if (templateScrollView == null)
        {
            return 0f;
        }

        float contentWidth =
            templateScrollView.contentContainer.resolvedStyle.width;

        float viewportWidth =
            templateScrollView.contentViewport.resolvedStyle.width;

        return Mathf.Max(0f, contentWidth - viewportWidth);
    }

    private void UpdateTemplateScrollbar()
    {
        if (templateScrollView == null ||
            templateScrollTrack == null ||
            templateScrollThumb == null)
        {
            return;
        }

        float trackWidth =
            templateScrollTrack.resolvedStyle.width;

        if (trackWidth <= 0f)
        {
            return;
        }

        float contentWidth =
            templateScrollView.contentContainer.resolvedStyle.width;

        float viewportWidth =
            templateScrollView.contentViewport.resolvedStyle.width;

        if (contentWidth <= 0f || viewportWidth <= 0f)
        {
            return;
        }

        float visibleRatio = Mathf.Clamp01(
            viewportWidth / contentWidth
        );

        float thumbWidth = Mathf.Clamp(
            trackWidth * visibleRatio,
            54f,
            trackWidth
        );

        templateScrollThumb.style.width = thumbWidth;

        float maxScroll = Mathf.Max(
            0f,
            contentWidth - viewportWidth
        );

        float maxThumbX = Mathf.Max(
            0f,
            trackWidth - thumbWidth
        );

        float normalized =
            maxScroll <= 0f
                ? 0f
                : templateScrollView.scrollOffset.x / maxScroll;

        templateScrollThumb.style.left =
            Mathf.Clamp01(normalized) * maxThumbX;
    }

    private void OnTemplateThumbPointerDown(
        PointerDownEvent evt
    )
    {
        if (templateScrollThumb == null)
        {
            return;
        }

        isDraggingTemplateThumb = true;
        templateThumbPointerOffset =
            evt.position.x -
            templateScrollThumb.worldBound.x;

        templateScrollThumb.CapturePointer(
            evt.pointerId
        );

        evt.StopPropagation();
    }

    private void OnTemplateThumbPointerMove(
        PointerMoveEvent evt
    )
    {
        if (!isDraggingTemplateThumb ||
            templateScrollView == null ||
            templateScrollTrack == null ||
            templateScrollThumb == null)
        {
            return;
        }

        float trackWidth =
            templateScrollTrack.resolvedStyle.width;

        float thumbWidth =
            templateScrollThumb.resolvedStyle.width;

        float maxThumbX = Mathf.Max(
            0f,
            trackWidth - thumbWidth
        );

        float localPointerX =
            evt.position.x -
            templateScrollTrack.worldBound.x -
            templateThumbPointerOffset;

        float thumbX = Mathf.Clamp(
            localPointerX,
            0f,
            maxThumbX
        );

        float normalized =
            maxThumbX <= 0f
                ? 0f
                : thumbX / maxThumbX;

        float maxScroll = GetTemplateMaxScroll();

        templateScrollView.scrollOffset =
            new Vector2(normalized * maxScroll, 0f);

        UpdateTemplateScrollbar();
        evt.StopPropagation();
    }

    private void OnTemplateThumbPointerUp(
        PointerUpEvent evt
    )
    {
        if (templateScrollThumb != null &&
            templateScrollThumb.HasPointerCapture(evt.pointerId))
        {
            templateScrollThumb.ReleasePointer(
                evt.pointerId
            );
        }

        isDraggingTemplateThumb = false;
        evt.StopPropagation();
    }

    private void OnTemplateScrollbarGeometryChanged(
        GeometryChangedEvent evt
    )
    {
        UpdateTemplateScrollbar();
    }

    private void OnTemplateScrollWheel(
        WheelEvent evt
    )
    {
        ScrollTemplatesBy(evt.delta.y * 25f);
        evt.StopPropagation();
    }

    private void UpdateOverviewSummary()
    {
        string courseCode =
            courseCodeField?.value?.Trim().ToUpper()
            ?? string.Empty;

        string courseName =
            courseNameField?.value?.Trim()
            ?? string.Empty;

        string visibility =
            selectedVisibility?.Trim()
            ?? string.Empty;

        if (summaryCourseCodeLabel != null)
        {
            summaryCourseCodeLabel.text =
                string.IsNullOrWhiteSpace(courseCode)
                    ? "No code set"
                    : courseCode;
        }

        if (summaryCourseNameLabel != null)
        {
            summaryCourseNameLabel.text =
                string.IsNullOrWhiteSpace(courseName)
                    ? "No name set"
                    : courseName;
        }

        if (summaryVisibilityLabel != null)
        {
            bool hasVisibility =
                !string.IsNullOrWhiteSpace(visibility);

            summaryVisibilityLabel.text =
                hasVisibility
                    ? visibility
                    : "No visibility set";
        }
    }

    private void HandleVoiceInputClicked()
    {
        Debug.Log(
            "Voice input chưa được tích hợp. " +
            "Sau này có thể kết nối Android Speech Recognizer."
        );
    }

    private void CreateClass()
    {
        if (isCreatingClass)
        {
            return;
        }

        if (!SupabaseSession.IsLoggedIn)
        {
            ShowCreateClassError(
                "Your login session has expired. Please sign in again."
            );

            return;
        }

        if (!SupabaseSession.IsTeacher)
        {
            ShowCreateClassError(
                "Only teacher accounts can create classes."
            );

            return;
        }

        string courseCode =
            courseCodeField?.value?.Trim().ToUpperInvariant()
            ?? string.Empty;

        string courseName =
            courseNameField?.value?.Trim()
            ?? string.Empty;

        string visibility =
            selectedVisibility?.Trim().ToLowerInvariant()
            ?? string.Empty;

        string courseDescription =
            courseDescriptionField?.value?.Trim()
            ?? string.Empty;

        SetCreateClassLoading(true);

        StartCoroutine(
            SupabaseClassService.CreateClass(
                className: courseName,
                description: courseDescription,
                classCode: courseCode,
                visibility: visibility,
                coverTemplate: selectedTemplateClass,
                coverImageUrl: string.Empty,
                categoryId: selectedCategoryId,
                onSuccess: createdClass =>
                {
                    SetCreateClassLoading(false);

                    PlayerPrefs.SetString(
                        "selected_class_id",
                        createdClass.id ?? string.Empty
                    );

                    PlayerPrefs.SetString(
                        "selected_class_name",
                        createdClass.class_name ?? courseName
                    );

                    PlayerPrefs.Save();

                    Debug.Log(
                        "Tạo lớp thành công trên Supabase:\n" +
                        $"ID: {createdClass.id}\n" +
                        $"Code: {createdClass.class_code}\n" +
                        $"Name: {createdClass.class_name}"
                    );

                    ReturnToMyClasses();
                },
                onError: error =>
                {
                    SetCreateClassLoading(false);

                    Debug.LogError(
                        "Không thể tạo lớp trên Supabase: " +
                        error
                    );

                    ShowCreateClassError(
                        TranslateCreateClassError(error)
                    );
                }
            )
        );
    }

    private void SetCreateClassLoading(bool loading)
    {
        isCreatingClass = loading;

        nextButton?.SetEnabled(!loading);
        backButton?.SetEnabled(!loading);
        cancelButton?.SetEnabled(!loading);

        if (nextButton != null)
        {
            nextButton.text =
                loading
                    ? "Creating Class..."
                    : "Create Class";
        }
    }

    private void ShowCreateClassError(string message)
    {
        if (brandingErrorLabel != null)
        {
            brandingErrorLabel.text = message;
            SetElementVisible(brandingErrorLabel, true);
        }

        Debug.LogError(message);
    }

    private static string TranslateCreateClassError(
        string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "Unable to create the class.";
        }

        string lower =
            error.ToLowerInvariant();

        if (lower.Contains("row-level security") ||
            lower.Contains("permission denied") ||
            lower.Contains("403"))
        {
            return "You do not have permission to create this class. Check the classes RLS policy.";
        }

        if (lower.Contains("duplicate") ||
            lower.Contains("unique") ||
            lower.Contains("409"))
        {
            return "This course code already exists.";
        }

        if (lower.Contains("access token") ||
            lower.Contains("jwt") ||
            lower.Contains("401"))
        {
            return "Your login session has expired. Please sign in again.";
        }

        if (lower.Contains("network") ||
            lower.Contains("connection") ||
            lower.Contains("resolve host"))
        {
            return "Cannot connect to Supabase. Please check your Internet connection.";
        }

        return error;
    }

    private void ReturnToMyClasses()
    {
        if (Application.CanStreamedLevelBeLoaded(
            "MyClassesScene"
        ))
        {
            SceneManager.LoadScene("MyClassesScene");
        }
        else
        {
            Debug.LogError(
                "Không tìm thấy MyClassesScene trong Build Settings."
            );
        }
    }

    private static void SetElementVisible(
        VisualElement element,
        bool visible
    )
    {
        if (element == null)
        {
            return;
        }

        element.EnableInClassList(
            "hidden",
            !visible
        );
    }

    private void OnCourseCodeChanged(
        ChangeEvent<string> changeEvent
    )
    {
        if (basicInfoErrorLabel != null)
        {
            basicInfoErrorLabel.text = string.Empty;
        }

        string upperCaseValue =
            changeEvent.newValue.ToUpper();

        if (upperCaseValue == changeEvent.newValue)
        {
            return;
        }

        courseCodeField.SetValueWithoutNotify(
            upperCaseValue
        );
    }

    private void OnCourseNameChanged(
        ChangeEvent<string> changeEvent
    )
    {
        if (basicInfoErrorLabel != null)
        {
            basicInfoErrorLabel.text = string.Empty;
        }
    }

    [Serializable]
    private class SupabaseCategory
    {
        public string id;
        public string name;
    }

    [Serializable]
    private class SupabaseCategoryArrayWrapper
    {
        public SupabaseCategory[] items;
    }
}