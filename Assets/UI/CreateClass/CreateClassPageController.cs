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
    private Label voiceStatusLabel;
    private AndroidSpeechRecognizer speechRecognizer;
    private string voiceBaseDescription = string.Empty;
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
        ConfigureSpeechRecognizer();

        AppLanguageManager.LanguageChanged += OnLanguageChanged;

        ConfigureVisibilityDropdown();
        ConfigureCategoryDropdown();
        ConfigureCategoryScrollView();
        RegisterEvents();
        LoadCategories();

        ShowStep(1);
        ApplyCurrentLanguage();
        SetBrandingPreviewVisible(false);

        root.schedule.Execute(UpdateTemplateScrollbar)
            .StartingIn(100);
    }

    private void OnDisable()
    {
        AppLanguageManager.LanguageChanged -= OnLanguageChanged;
        UnregisterEvents();

        if (speechRecognizer != null && speechRecognizer.IsListening)
            speechRecognizer.CancelListening();

        SetVoiceListeningUi(false);
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

        voiceStatusLabel =
            root.Q<Label>("voice-status-label");

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
            visibilitySelectedLabel.text = T("Select visibility...", "Chọn quyền truy cập...");
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
            categorySelectedLabel.text = T("Select category...", "Chọn danh mục...");
            categorySelectedLabel.RemoveFromClassList(
                "custom-visibility-selected-label-active"
            );
        }

        SetCategoryDropdownOpen(false);
        SetCategoryLoadingState(T("Loading categories...", "Đang tải danh mục..."));
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

    private void OnLanguageChanged(string language)
    {
        ApplyCurrentLanguage();
    }

    private void ApplyCurrentLanguage()
    {
        if (root == null)
            return;

        LocalizeElementTree(root);

        if (courseCodeField != null)
            courseCodeField.textEdition.placeholder =
                T("e.g. CS101, EE301...", "VD: CS101, EE301...");

        if (courseNameField != null)
            courseNameField.textEdition.placeholder =
                T("e.g. Introduction to Circuits", "VD: Nhập môn Mạch điện");

        if (courseDescriptionField != null)
            courseDescriptionField.textEdition.placeholder =
                T(
                    "This course introduces students to the fundamentals of...",
                    "Khóa học này giới thiệu cho sinh viên những kiến thức cơ bản về..."
                );

        // Keep current selections while changing only their display text.
        if (visibilitySelectedLabel != null)
        {
            if (string.Equals(selectedVisibility, "public", StringComparison.OrdinalIgnoreCase))
                visibilitySelectedLabel.text = T("Public", "Công khai");
            else if (string.Equals(selectedVisibility, "private", StringComparison.OrdinalIgnoreCase))
                visibilitySelectedLabel.text = T("Private", "Riêng tư");
            else
                visibilitySelectedLabel.text = T("Select visibility...", "Chọn quyền truy cập...");
        }

        if (categorySelectedLabel != null)
        {
            categorySelectedLabel.text =
                string.IsNullOrWhiteSpace(selectedCategoryName)
                    ? T("Select category...", "Chọn danh mục...")
                    : LocalizeCategoryName(selectedCategoryName);
        }

        if (voiceInputButton != null)
        {
            voiceInputButton.tooltip =
                speechRecognizer != null && speechRecognizer.IsListening
                    ? T("Stop voice input", "Dừng nhập bằng giọng nói")
                    : T("Voice input", "Nhập bằng giọng nói");
        }

        // Category buttons are generated dynamically.
        foreach (Button button in categoryButtons)
        {
            if (button == null)
                continue;

            string raw = GetCanonicalCategoryName(button.text);
            button.text = LocalizeCategoryName(raw);
        }

        UpdateHeader();
        UpdateBottomButton();
        UpdateOverviewSummary();
        UpdateReviewInformation();
    }

    private void LocalizeElementTree(VisualElement element)
    {
        if (element == null)
            return;

        if (element is Label label && !string.IsNullOrWhiteSpace(label.text))
            label.text = TranslateKnownUiText(label.text);

        if (element is Button button && !string.IsNullOrWhiteSpace(button.text))
            button.text = TranslateKnownUiText(button.text);

        foreach (VisualElement child in element.Children())
            LocalizeElementTree(child);
    }

    private static string TranslateKnownUiText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        bool vi = AppLanguageManager.IsVietnamese;

        switch (text.Trim())
        {
            case "Cancel":
            case "Hủy":
                return vi ? "Hủy" : "Cancel";
            case "COURSE CODE":
            case "MÃ LỚP":
                return vi ? "MÃ LỚP" : "COURSE CODE";
            case "COURSE NAME":
            case "TÊN LỚP":
                return vi ? "TÊN LỚP" : "COURSE NAME";
            case "VISIBILITY":
            case "QUYỀN TRUY CẬP":
                return vi ? "QUYỀN TRUY CẬP" : "VISIBILITY";
            case "Public":
            case "Công khai":
                return vi ? "Công khai" : "Public";
            case "Anyone can discover and join this class":
            case "Mọi người có thể tìm thấy và tham gia lớp":
                return vi ? "Mọi người có thể tìm thấy và tham gia lớp" : "Anyone can discover and join this class";
            case "Private":
            case "Riêng tư":
                return vi ? "Riêng tư" : "Private";
            case "Only invited students can access this class":
            case "Chỉ học sinh được mời mới có thể truy cập lớp":
                return vi ? "Chỉ học sinh được mời mới có thể truy cập lớp" : "Only invited students can access this class";
            case "CATEGORY":
            case "DANH MỤC":
                return vi ? "DANH MỤC" : "CATEGORY";
            case "COVER IMAGE":
            case "ẢNH BÌA":
                return vi ? "ẢNH BÌA" : "COVER IMAGE";
            case "Upload Cover Image":
            case "Tải ảnh bìa":
                return vi ? "Tải ảnh bìa" : "Upload Cover Image";
            case "Remove":
            case "Xóa":
                return vi ? "Xóa" : "Remove";
            case "OR CHOOSE A TEMPLATE":
            case "HOẶC CHỌN MỘT MẪU":
                return vi ? "HOẶC CHỌN MỘT MẪU" : "OR CHOOSE A TEMPLATE";
            case "PREVIEW":
            case "XEM TRƯỚC":
                return vi ? "XEM TRƯỚC" : "PREVIEW";
            case "COURSE DESCRIPTION":
            case "MÔ TẢ LỚP HỌC":
                return vi ? "MÔ TẢ LỚP HỌC" : "COURSE DESCRIPTION";
            case "Describe what students will learn, course structure, and any prerequisites.":
            case "Mô tả nội dung học, cấu trúc khóa học và các điều kiện tiên quyết.":
                return vi
                    ? "Mô tả nội dung học, cấu trúc khóa học và các điều kiện tiên quyết."
                    : "Describe what students will learn, course structure, and any prerequisites.";
            case "CLASS SUMMARY":
            case "TÓM TẮT LỚP HỌC":
                return vi ? "TÓM TẮT LỚP HỌC" : "CLASS SUMMARY";
            case "Course code":
            case "Mã lớp":
                return vi ? "Mã lớp" : "Course code";
            case "Course name":
            case "Tên lớp":
                return vi ? "Tên lớp" : "Course name";
            case "Visibility":
            case "Quyền truy cập":
                return vi ? "Quyền truy cập" : "Visibility";
            case "Next":
            case "Tiếp theo":
                return vi ? "Tiếp theo" : "Next";
            case "Create Class":
            case "Tạo lớp":
                return vi ? "Tạo lớp" : T("Create Class", "Tạo lớp");
        }

        return text;
    }

    private static string T(string english, string vietnamese)
    {
        return AppLanguageManager.IsVietnamese ? vietnamese : english;
    }

    private static string LocalizeVisibility(string value)
    {
        if (string.Equals(value, "public", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Public", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Công khai", StringComparison.OrdinalIgnoreCase))
            return T("Public", "Công khai");

        if (string.Equals(value, "private", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Private", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Riêng tư", StringComparison.OrdinalIgnoreCase))
            return T("Private", "Riêng tư");

        return value;
    }

    private static string LocalizeCategoryName(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return categoryName;

        string normalized = GetCanonicalCategoryName(categoryName);

        if (!AppLanguageManager.IsVietnamese)
            return normalized;

        switch (normalized.ToLowerInvariant())
        {
            case "math":
            case "mathematics": return "Toán";
            case "physics": return "Vật lý";
            case "chemistry": return "Hóa học";
            case "biology": return "Sinh học";
            case "english": return "Tiếng Anh";
            case "foreign language": return "Ngoại ngữ";
            case "history": return "Lịch sử";
            case "geography": return "Địa lý";
            case "technology": return "Công nghệ";
            case "computer science": return "Khoa học máy tính";
            case "programming": return "Lập trình";
            case "university": return "Đại học";
            case "others": return "Khác";
            default: return normalized;
        }
    }

    private static string GetCanonicalCategoryName(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return string.Empty;

        switch (categoryName.Trim().ToLowerInvariant())
        {
            case "toán": return "Math";
            case "vật lý": return "Physics";
            case "hóa học": return "Chemistry";
            case "sinh học": return "Biology";
            case "tiếng anh": return "English";
            case "ngoại ngữ": return "Foreign Language";
            case "lịch sử": return "History";
            case "địa lý": return "Geography";
            case "công nghệ": return "Technology";
            case "khoa học máy tính": return "Computer Science";
            case "lập trình": return "Programming";
            case "đại học": return "University";
            case "khác": return "Others";
            default: return categoryName.Trim();
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
        SelectVisibility("public", T("Public", "Công khai"));
    }

    private void SelectPrivateVisibility()
    {
        SelectVisibility("private", T("Private", "Riêng tư"));
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

        if (isOpen)
        {
            root?.schedule.Execute(() =>
                AlignDropdownPanel(
                    visibilityDropdownButton,
                    visibilityOptionsPanel
                )
            ).StartingIn(1);
        }

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

        if (isOpen)
        {
            root?.schedule.Execute(() =>
                AlignDropdownPanel(
                    categoryDropdownButton,
                    categoryOptionsPanel
                )
            ).StartingIn(1);
        }

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


    private static void AlignDropdownPanel(
        Button triggerButton,
        VisualElement optionsPanel)
    {
        if (triggerButton == null || optionsPanel == null)
            return;

        float triggerWidth =
            triggerButton.resolvedStyle.width;

        if (triggerWidth <= 0f)
            return;

        // Match the opened list exactly to the trigger box.
        optionsPanel.style.width = triggerWidth;
        optionsPanel.style.minWidth = triggerWidth;
        optionsPanel.style.maxWidth = triggerWidth;

        optionsPanel.style.marginLeft = 0;
        optionsPanel.style.marginRight = 0;
    }

    private void LoadCategories()
    {
        StartCoroutine(LoadCategoriesCoroutine(false));
    }

    private IEnumerator LoadCategoriesCoroutine(bool isRetry)
    {
        SetCategoryLoadingState(
            isRetry
                ? T("Retrying categories...", "Đang thử tải lại danh mục...")
                : T("Loading categories...", "Đang tải danh mục...")
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
                T("Unable to load categories.", "Không thể tải danh mục.")
            );

            Debug.LogError(
                "Supabase categories request did not complete."
            );

            yield break;
        }

        if (!string.IsNullOrWhiteSpace(requestError))
        {
            SetCategoryLoadingState(
                T("Unable to load categories.", "Không thể tải danh mục.")
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
                T("Unable to load categories.", "Không thể tải danh mục.")
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
                T("No categories returned. Check the categories SELECT policy.", "Không có danh mục nào được trả về.")
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
            SetCategoryLoadingState(T("No categories available.", "Không có danh mục nào."));
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
            optionButton.text = LocalizeCategoryName(category.name.Trim());
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
            SetCategoryLoadingState(T("No categories available.", "Không có danh mục nào."));
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
            categorySelectedLabel.text = LocalizeCategoryName(categoryName);
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
            visibilitySelectedLabel.text = T("Select visibility...", "Chọn quyền truy cập...");
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
            categorySelectedLabel.text = T("Select category...", "Chọn danh mục...");
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
            summaryCourseCodeLabel.text = T("No code set", "Chưa có mã lớp");
        }

        if (summaryCourseNameLabel != null)
        {
            summaryCourseNameLabel.text = T("No name set", "Chưa có tên lớp");
        }

        if (summaryVisibilityLabel != null)
        {
            summaryVisibilityLabel.text =
                T("No visibility set", "Chưa chọn quyền truy cập");
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
                T($"Step {currentStep} of {TotalSteps}", $"Bước {currentStep} / {TotalSteps}");
        }

        if (pageTitleLabel == null)
        {
            return;
        }

        pageTitleLabel.text = currentStep switch
        {
            1 => T("Basic Info", "Thông tin cơ bản"),
            2 => T("Branding", "Hình ảnh"),
            3 => T("Overview", "Tổng quan"),
            _ => T("Create Class", "Tạo lớp")
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
                ? T("Create Class", "Tạo lớp")
                : T("Next", "Tiếp theo");
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
                T("Please enter the course code.", "Vui lòng nhập mã lớp.")
            );

            courseCodeField?.Focus();

            return false;
        }

        if (string.IsNullOrWhiteSpace(courseName))
        {
            ShowBasicInfoError(
                T("Please enter the course name.", "Vui lòng nhập tên lớp.")
            );

            courseNameField?.Focus();

            return false;
        }

        if (string.IsNullOrWhiteSpace(visibility))
        {
            ShowBasicInfoError(
                T("Please select class visibility.", "Vui lòng chọn quyền truy cập của lớp.")
            );

            return false;
        }

        if (string.IsNullOrWhiteSpace(selectedCategoryId))
        {
            ShowBasicInfoError(
                T("Please select a category.", "Vui lòng chọn danh mục.")
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
                    T("Please upload a cover image or choose a template.", "Vui lòng tải ảnh bìa hoặc chọn một mẫu.");
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
            reviewVisibility.text = LocalizeVisibility(visibility);
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
                    ? LocalizeVisibility(visibility)
                    : T("No visibility set", "Chưa chọn quyền truy cập");
        }
    }

    private void ConfigureSpeechRecognizer()
    {
        speechRecognizer = GetComponent<AndroidSpeechRecognizer>();

        if (speechRecognizer == null)
            speechRecognizer = gameObject.AddComponent<AndroidSpeechRecognizer>();

        speechRecognizer.PartialResultReceived -= HandleVoicePartialResult;
        speechRecognizer.FinalResultReceived -= HandleVoiceFinalResult;
        speechRecognizer.ListeningStateChanged -= HandleVoiceListeningStateChanged;
        speechRecognizer.ErrorReceived -= HandleVoiceRecognitionError;

        speechRecognizer.PartialResultReceived += HandleVoicePartialResult;
        speechRecognizer.FinalResultReceived += HandleVoiceFinalResult;
        speechRecognizer.ListeningStateChanged += HandleVoiceListeningStateChanged;
        speechRecognizer.ErrorReceived += HandleVoiceRecognitionError;
    }

    private void HandleVoiceInputClicked()
    {
        if (speechRecognizer == null)
        {
            ShowVoiceStatus(
                T(
                    "Voice recognition is unavailable.",
                    "Không thể khởi tạo nhận diện giọng nói."
                ),
                true
            );

            return;
        }

        if (speechRecognizer.IsListening)
        {
            speechRecognizer.StopListening();
            ShowVoiceStatus(
                T("Processing speech...", "Đang xử lý giọng nói..."),
                false
            );
            return;
        }

        voiceBaseDescription =
            courseDescriptionField?.value?.TrimEnd() ?? string.Empty;

        string languageCode =
            AppLanguageManager.IsVietnamese
                ? "vi-VN"
                : "en-US";

        ShowVoiceStatus(
            T("Requesting microphone...", "Đang mở microphone..."),
            false
        );

        speechRecognizer.StartListening(languageCode);
    }

    private void HandleVoicePartialResult(string transcript)
    {
        if (courseDescriptionField == null ||
            string.IsNullOrWhiteSpace(transcript))
        {
            return;
        }

        courseDescriptionField.SetValueWithoutNotify(
            CombineVoiceDescription(
                voiceBaseDescription,
                transcript
            )
        );

        ShowVoiceStatus(
            T("Listening...", "Đang nghe..."),
            false,
            true
        );
    }

    private void HandleVoiceFinalResult(string transcript)
    {
        if (courseDescriptionField != null &&
            !string.IsNullOrWhiteSpace(transcript))
        {
            string finalText = CombineVoiceDescription(
                voiceBaseDescription,
                transcript
            );

            courseDescriptionField.value = finalText;
            voiceBaseDescription = finalText;
        }

        ShowVoiceStatus(
            T("Voice text added.", "Đã thêm nội dung từ giọng nói."),
            false
        );

        root?.schedule.Execute(() =>
        {
            if (voiceStatusLabel != null &&
                !(speechRecognizer?.IsListening ?? false))
            {
                SetElementVisible(voiceStatusLabel, false);
            }
        }).StartingIn(1800);
    }

    private void HandleVoiceListeningStateChanged(bool listening)
    {
        SetVoiceListeningUi(listening);

        if (listening)
        {
            ShowVoiceStatus(
                T(
                    "Listening... Tap the microphone again to stop.",
                    "Đang nghe... Nhấn lại nút micro để dừng."
                ),
                false,
                true
            );
        }
    }

    private void HandleVoiceRecognitionError(string error)
    {
        SetVoiceListeningUi(false);

        ShowVoiceStatus(
            string.IsNullOrWhiteSpace(error)
                ? T(
                    "Unable to recognize speech.",
                    "Không thể nhận diện giọng nói."
                )
                : error,
            true
        );
    }

    private void SetVoiceListeningUi(bool listening)
    {
        voiceInputButton?.EnableInClassList(
            "voice-recording",
            listening
        );

        if (voiceInputButton != null)
        {
            voiceInputButton.tooltip = listening
                ? T("Stop voice input", "Dừng nhập bằng giọng nói")
                : T("Voice input", "Nhập bằng giọng nói");
        }
    }

    private void ShowVoiceStatus(
        string message,
        bool isError,
        bool isListening = false)
    {
        if (voiceStatusLabel == null)
            return;

        voiceStatusLabel.text = message ?? string.Empty;

        voiceStatusLabel.EnableInClassList(
            "voice-status-error",
            isError
        );

        voiceStatusLabel.EnableInClassList(
            "voice-status-listening",
            isListening && !isError
        );

        SetElementVisible(
            voiceStatusLabel,
            !string.IsNullOrWhiteSpace(message)
        );
    }

    private static string CombineVoiceDescription(
        string original,
        string transcript)
    {
        string baseText = original?.TrimEnd() ?? string.Empty;
        string spokenText = transcript?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(baseText))
            return spokenText;

        if (string.IsNullOrWhiteSpace(spokenText))
            return baseText;

        char lastCharacter = baseText[baseText.Length - 1];
        string separator =
            lastCharacter == '.' ||
            lastCharacter == '!' ||
            lastCharacter == '?' ||
            lastCharacter == '\n'
                ? " "
                : ". ";

        return baseText + separator + spokenText;
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
                T("Your login session has expired. Please sign in again.", "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.")
            );

            return;
        }

        if (!SupabaseSession.IsTeacher)
        {
            ShowCreateClassError(
                T("Only teacher accounts can create classes.", "Chỉ tài khoản giáo viên mới có thể tạo lớp.")
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
                    ? T("Creating Class...", "Đang tạo lớp...")
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
            return T("Unable to create the class.", "Không thể tạo lớp.");
        }

        string lower =
            error.ToLowerInvariant();

        if (lower.Contains("row-level security") ||
            lower.Contains("permission denied") ||
            lower.Contains("403"))
        {
            return T("You do not have permission to create this class. Check the classes RLS policy.", "Bạn không có quyền tạo lớp này.");
        }

        if (lower.Contains("duplicate") ||
            lower.Contains("unique") ||
            lower.Contains("409"))
        {
            return T("This course code already exists.", "Mã lớp này đã tồn tại.");
        }

        if (lower.Contains("access token") ||
            lower.Contains("jwt") ||
            lower.Contains("401"))
        {
            return T("Your login session has expired. Please sign in again.", "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        }

        if (lower.Contains("network") ||
            lower.Contains("connection") ||
            lower.Contains("resolve host"))
        {
            return T("Cannot connect to Supabase. Please check your Internet connection.", "Không thể kết nối đến Supabase. Vui lòng kiểm tra Internet.");
        }

        return error;
    }

    private void ReturnToMyClasses()
    {
        SceneHistory.GoBack("MyClassesScene");
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