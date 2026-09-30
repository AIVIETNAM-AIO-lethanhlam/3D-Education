using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class PrivacyPageController : MonoBehaviour
{
    private const int SectionCount = 7;

    private GeneralHeaderController headerController;

    private ScrollView privacyScroll;
    private Toggle consentToggle;
    private Button acceptContinueButton;

    private readonly Button[] sectionButtons =
        new Button[SectionCount];

    private readonly VisualElement[] sectionContents =
        new VisualElement[SectionCount];

    // Arrow containers: the icon (arrow-down.png / arrow-up.png) is chosen
    // by the USS class privacy-section-arrow--expanded.
    private readonly VisualElement[] sectionArrows =
        new VisualElement[SectionCount];

    private readonly Label[] sectionTitles =
        new Label[SectionCount];

    private readonly Label[] sectionBodies =
        new Label[SectionCount];

    private Label introLabel;
    private VisualElement consentPanel;
    private VisualElement pageRoot;

    // Base padding under the Accept button (matches .privacy-consent-panel in USS).
    private const float ConsentPanelBasePaddingBottom = 28f;

    private const string ExpandedArrowClass =
        "privacy-section-arrow--expanded";

    // =========================================================
    // LOCALIZED TEXT (EN / VI) - follows the language in SettingScene
    // =========================================================

    private static readonly string[] SectionTitlesEn =
    {
        "1. Data Collection",
        "2. How We Use Your Data",
        "3. Data Sharing & Third Parties",
        "4. Security & Encryption",
        "5. Your Rights",
        "6. Terms of Academic Use",
        "7. Cookie Policy"
    };

    private static readonly string[] SectionTitlesVi =
    {
        "1. Thu thập dữ liệu",
        "2. Cách chúng tôi sử dụng dữ liệu",
        "3. Chia sẻ dữ liệu & bên thứ ba",
        "4. Bảo mật & mã hóa",
        "5. Quyền của bạn",
        "6. Điều khoản sử dụng học thuật",
        "7. Chính sách cookie"
    };

    private static readonly string[] SectionBodiesEn =
    {
        "We collect information you provide directly to us, including name, email address, institution affiliation, and usage data. This data is used solely to provide and improve the Virtual Education service. We do not sell personal information to third parties under any circumstances.",
        "Your data is used to personalize your learning experience, deliver course content, track academic progress, generate AI-powered recommendations, and ensure platform security. Analytics are processed in aggregate and anonymized form.",
        "We share data with Bách Khoa University faculty and administration for academic purposes only. Third-party services such as authentication, cloud storage, and AI services are bound by data-processing agreements and applicable privacy standards.",
        "Data is protected in transit and at rest using appropriate encryption. Authentication sessions expire according to security policy. We conduct regular security reviews and address reported vulnerabilities.",
        "You may request access, correction, export, or deletion of your personal data where applicable. Requests can be submitted through the application settings or the university support channel.",
        "The platform is provided for educational purposes only. Sharing login credentials, submitting AI-generated work as your own without disclosure, or attempting unauthorized access may result in academic or account-related action.",
        "We use essential session cookies and optional analytics cookies. Analytics preferences may be managed in Settings. Essential cookies are required for authentication and core application functionality."
    };

    private static readonly string[] SectionBodiesVi =
    {
        "Chúng tôi thu thập thông tin bạn cung cấp trực tiếp, bao gồm họ tên, địa chỉ email, đơn vị trực thuộc và dữ liệu sử dụng. Dữ liệu này chỉ được dùng để cung cấp và cải thiện dịch vụ Virtual Education. Chúng tôi không bán thông tin cá nhân cho bên thứ ba trong bất kỳ trường hợp nào.",
        "Dữ liệu của bạn được dùng để cá nhân hóa trải nghiệm học tập, cung cấp nội dung bài học, theo dõi tiến độ học tập, tạo gợi ý bằng AI và bảo đảm an toàn cho hệ thống. Số liệu thống kê được xử lý ở dạng tổng hợp và ẩn danh.",
        "Chúng tôi chỉ chia sẻ dữ liệu với giảng viên và bộ phận quản lý của Trường Đại học Bách Khoa cho mục đích học tập. Các dịch vụ bên thứ ba như xác thực, lưu trữ đám mây và AI phải tuân thủ thỏa thuận xử lý dữ liệu và các tiêu chuẩn bảo mật liên quan.",
        "Dữ liệu được bảo vệ khi truyền và khi lưu trữ bằng các phương thức mã hóa phù hợp. Phiên đăng nhập sẽ hết hạn theo chính sách bảo mật. Chúng tôi thường xuyên rà soát bảo mật và xử lý các lỗ hổng được báo cáo.",
        "Bạn có thể yêu cầu truy cập, chỉnh sửa, xuất hoặc xóa dữ liệu cá nhân của mình khi phù hợp. Yêu cầu có thể được gửi qua phần cài đặt của ứng dụng hoặc kênh hỗ trợ của trường.",
        "Nền tảng chỉ được cung cấp cho mục đích giáo dục. Việc chia sẻ thông tin đăng nhập, nộp bài do AI tạo ra như bài của mình mà không ghi rõ, hoặc cố gắng truy cập trái phép có thể dẫn đến xử lý học vụ hoặc xử lý tài khoản.",
        "Chúng tôi sử dụng cookie phiên thiết yếu và cookie phân tích tùy chọn. Bạn có thể quản lý tùy chọn phân tích trong phần Cài đặt. Cookie thiết yếu là bắt buộc cho việc xác thực và các chức năng cốt lõi của ứng dụng."
    };

    private static string T(string english, string vietnamese) =>
        AppLanguageManager.IsVietnamese ? vietnamese : english;

    private readonly bool[] sectionVisited =
        new bool[SectionCount];

    private int expandedSectionIndex = 0;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
        {
            Debug.LogError(
                "Không tìm thấy UIDocument trong PrivacyScene.");
            return;
        }

        VisualElement root = document.rootVisualElement;

        if (root == null)
        {
            Debug.LogError(
                "rootVisualElement của PrivacyScene đang null.");
            return;
        }

        pageRoot = root;

        QueryElements(root);
        InitializeHeader(root);
        ApplyLanguage();
        InitializeAccordion();
        RegisterEvents();

        AppLanguageManager.LanguageChanged += OnLanguageChanged;

        // Keep the consent panel above the phone's gesture bar / rounded corners.
        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

        // Section 1 mở mặc định và được xem là đã đọc.
        sectionVisited[0] = true;

        UpdateAcceptButtonState();
    }

    private void OnDisable()
    {
        AppLanguageManager.LanguageChanged -= OnLanguageChanged;
        pageRoot?.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

        UnregisterEvents();
        DisposeHeader();
    }

    private void QueryElements(VisualElement root)
    {
        privacyScroll =
            root.Q<ScrollView>("privacy-scroll");

        consentToggle =
            root.Q<Toggle>("privacy-consent-toggle");

        acceptContinueButton =
            root.Q<Button>("accept-continue-button");

        introLabel =
            root.Q<Label>("privacy-intro-label");

        consentPanel =
            root.Q<VisualElement>("privacy-consent-panel");

        for (int i = 0; i < SectionCount; i++)
        {
            int sectionNumber = i + 1;

            sectionButtons[i] =
                root.Q<Button>(
                    $"privacy-section-{sectionNumber}-button");

            sectionContents[i] =
                root.Q<VisualElement>(
                    $"privacy-section-{sectionNumber}-content");

            sectionArrows[i] =
                root.Q<VisualElement>(
                    $"privacy-section-{sectionNumber}-arrow");

            sectionTitles[i] =
                root.Q<Label>(
                    $"privacy-section-{sectionNumber}-title");

            sectionBodies[i] =
                root.Q<Label>(
                    $"privacy-section-{sectionNumber}-body");
        }
    }

    private void InitializeHeader(VisualElement root)
    {
        headerController =
            new GeneralHeaderController(root);

        ConfigureHeaderText();

        /*
        * Không dùng compact vì trang Privacy có:
        * - title
        * - subtitle
        * - icon bên phải
        */
        headerController.SetCompact(false);

        /*
        * Bật vùng an toàn lớn để header nằm dưới notch.
        * USS sẽ sử dụng padding-top: 58px.
        */
        headerController.SetLargeSafeArea(true);

        headerController.SetBottomBorderVisible(true);

        headerController.BackClicked +=
            HandleBackClicked;

        headerController.RightActionClicked +=
            HandleDocumentClicked;
    }

    private void InitializeAccordion()
    {
        for (int i = 0; i < SectionCount; i++)
        {
            SetSectionExpanded(
                i,
                i == expandedSectionIndex);
        }
    }

    private void RegisterEvents()
    {
        for (int i = 0; i < SectionCount; i++)
        {
            int capturedIndex = i;

            if (sectionButtons[i] != null)
            {
                sectionButtons[i].clicked +=
                    () => ToggleSection(capturedIndex);
            }
        }

        if (consentToggle != null)
        {
            consentToggle.RegisterValueChangedCallback(
                OnConsentChanged);
        }

        if (acceptContinueButton != null)
        {
            acceptContinueButton.clicked +=
                HandleAcceptAndContinue;
        }
    }

    private void UnregisterEvents()
    {
        // clicked callbacks above use captured lambdas.
        // They are removed automatically when the UIDocument tree is destroyed.
        // OnEnable is normally called once per scene load.

        if (consentToggle != null)
        {
            consentToggle.UnregisterValueChangedCallback(
                OnConsentChanged);
        }

        if (acceptContinueButton != null)
        {
            acceptContinueButton.clicked -=
                HandleAcceptAndContinue;
        }
    }

    private void ToggleSection(int sectionIndex)
    {
        if (sectionIndex < 0 ||
            sectionIndex >= SectionCount)
        {
            return;
        }

        bool isCurrentlyExpanded =
            expandedSectionIndex == sectionIndex;

        if (isCurrentlyExpanded)
        {
            SetSectionExpanded(sectionIndex, false);
            expandedSectionIndex = -1;
        }
        else
        {
            if (expandedSectionIndex >= 0)
            {
                SetSectionExpanded(
                    expandedSectionIndex,
                    false);
            }

            expandedSectionIndex = sectionIndex;
            sectionVisited[sectionIndex] = true;

            SetSectionExpanded(sectionIndex, true);
            ScrollSectionIntoView(sectionIndex);
        }

        UpdateAcceptButtonState();
    }

    private void SetSectionExpanded(
        int sectionIndex,
        bool expanded)
    {
        VisualElement content =
            sectionContents[sectionIndex];

        VisualElement arrow =
            sectionArrows[sectionIndex];

        if (content != null)
        {
            content.style.display =
                expanded
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }

        if (arrow != null)
        {
            // arrow-up.png when open, arrow-down.png when closed (see USS).
            arrow.EnableInClassList(
                ExpandedArrowClass,
                expanded);
        }
    }

    private void ScrollSectionIntoView(
        int sectionIndex)
    {
        if (privacyScroll == null ||
            sectionButtons[sectionIndex] == null)
        {
            return;
        }

        privacyScroll.schedule.Execute(
            () =>
            {
                privacyScroll.ScrollTo(
                    sectionButtons[sectionIndex]);
            }).ExecuteLater(50);
    }

    // =========================================================
    // LANGUAGE
    // =========================================================

    private void OnLanguageChanged(string language)
    {
        ApplyLanguage();
    }

    private void ConfigureHeaderText()
    {
        headerController?.ConfigurePageWithIconAction(
            title: T("Privacy & Terms", "Quyền riêng tư & Điều khoản"),
            // No "Last updated" line under the title.
            subtitle: null,
            iconClass: "icon-header-document",
            showBackButton: true,
            showSubtitleIcon: false);

        // Privacy-only header style (smaller title, wraps to 2 lines,
        // vertically centred with the back button). See PrivacyPage.uss.
        headerController?.SetCustomClass("privacy-header", true);
    }

    private void ApplyLanguage()
    {
        bool vi = AppLanguageManager.IsVietnamese;

        ConfigureHeaderText();

        if (introLabel != null)
        {
            introLabel.text = vi
                ? "Chính sách quyền riêng tư này mô tả cách Bách Khoa University Virtual Education thu thập, sử dụng và bảo vệ thông tin cá nhân của bạn. Vui lòng đọc kỹ tất cả các mục trước khi tiếp tục."
                : "This Privacy Policy describes how Bách Khoa University Virtual Education collects, uses, and protects your personal information. Please read all sections carefully before proceeding.";
        }

        for (int i = 0; i < SectionCount; i++)
        {
            if (sectionTitles[i] != null)
                sectionTitles[i].text = vi ? SectionTitlesVi[i] : SectionTitlesEn[i];

            if (sectionBodies[i] != null)
                sectionBodies[i].text = vi ? SectionBodiesVi[i] : SectionBodiesEn[i];
        }

        if (consentToggle != null)
        {
            consentToggle.text = vi
                ? "Tôi đã đọc và hiểu chính sách"
                : "I have read and understood the policy";
        }

        if (acceptContinueButton != null)
        {
            acceptContinueButton.text = vi
                ? "Đồng ý & Tiếp tục"
                : "Accept & Continue";
        }
    }

    // =========================================================
    // SAFE AREA (bottom)
    // =========================================================

    private void OnRootGeometryChanged(GeometryChangedEvent evt)
    {
        ApplyBottomSafeArea();
    }

    /// <summary>
    /// Adds the device's bottom safe-area inset (gesture bar, rounded corners)
    /// to the consent panel so the Accept button is not glued to the screen edge.
    /// </summary>
    private void ApplyBottomSafeArea()
    {
        if (consentPanel == null || pageRoot == null)
            return;

        float rootHeight = pageRoot.layout.height;
        if (float.IsNaN(rootHeight) || rootHeight <= 0f || Screen.height <= 0)
            return;

        // Screen.safeArea is in pixels, measured from the bottom-left corner.
        float bottomInsetPixels = Mathf.Max(0f, Screen.safeArea.yMin);
        float pixelsToPanel = rootHeight / Screen.height;
        float bottomInset = bottomInsetPixels * pixelsToPanel;

        float targetPadding = ConsentPanelBasePaddingBottom + bottomInset;

        if (Mathf.Abs(consentPanel.resolvedStyle.paddingBottom - targetPadding) > 0.5f)
            consentPanel.style.paddingBottom = targetPadding;
    }

    private void OnConsentChanged(
        ChangeEvent<bool> evt)
    {
        UpdateAcceptButtonState();
    }

    private void UpdateAcceptButtonState()
    {
        if (acceptContinueButton == null)
        {
            return;
        }

        bool consentAccepted =
            consentToggle != null &&
            consentToggle.value;

        // Hiện tại chỉ yêu cầu người dùng đánh dấu đồng ý.
        // Muốn bắt buộc mở đủ 7 phần, đổi thành:
        // bool canContinue =
        //     consentAccepted && HaveVisitedAllSections();
        bool canContinue = consentAccepted;

        acceptContinueButton.SetEnabled(canContinue);
    }

    private bool HaveVisitedAllSections()
    {
        for (int i = 0; i < sectionVisited.Length; i++)
        {
            if (!sectionVisited[i])
            {
                return false;
            }
        }

        return true;
    }

    private void HandleAcceptAndContinue()
    {
        if (consentToggle == null ||
            !consentToggle.value)
        {
            return;
        }

        PlayerPrefs.SetInt(
            "privacy_policy_accepted",
            1);

        PlayerPrefs.SetString(
            "privacy_policy_version",
            "2025-06-15");

        PlayerPrefs.Save();

        string targetScene =
            IsUserLoggedIn()
                ? "MainHomeScene"
                : "AuthScene";

        SceneManager.LoadScene(targetScene);
    }

    private bool IsUserLoggedIn()
    {
        // Ưu tiên user ID nếu hệ thống đăng nhập có lưu key này.
        if (PlayerPrefs.HasKey("current_user_id") &&
            !string.IsNullOrWhiteSpace(
                PlayerPrefs.GetString(
                    "current_user_id",
                    string.Empty)))
        {
            return true;
        }

        // Tương thích với cấu trúc PlayerPrefs hiện tại của project.
        bool hasRole =
            PlayerPrefs.HasKey("current_role") &&
            !string.IsNullOrWhiteSpace(
                PlayerPrefs.GetString(
                    "current_role",
                    string.Empty));

        bool hasName =
            PlayerPrefs.HasKey("current_full_name") &&
            !string.IsNullOrWhiteSpace(
                PlayerPrefs.GetString(
                    "current_full_name",
                    string.Empty));

        return hasRole && hasName;
    }

    private void HandleBackClicked()
    {
        // Nếu bạn đã tạo SceneNavigation.cs:
        // SceneNavigation.GoBack("SettingsScene");

        // Fallback an toàn khi chưa có navigation history:
        if (IsUserLoggedIn())
        {
            SceneManager.LoadScene("MainHomeScene");
        }
        else
        {
            SceneManager.LoadScene("AuthScene");
        }
    }

    private void HandleDocumentClicked()
    {
        Debug.Log(
            "Nút tài liệu Privacy & Terms được nhấn.");
    }

    private void DisposeHeader()
    {
        if (headerController == null)
        {
            return;
        }

        headerController.BackClicked -=
            HandleBackClicked;

        headerController.RightActionClicked -=
            HandleDocumentClicked;

        headerController.Dispose();
        headerController = null;
    }
}
