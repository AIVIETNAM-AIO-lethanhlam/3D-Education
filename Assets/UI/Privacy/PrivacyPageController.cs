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

    private readonly Label[] sectionArrows =
        new Label[SectionCount];

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

        QueryElements(root);
        InitializeHeader(root);
        InitializeAccordion();
        RegisterEvents();

        // Section 1 mở mặc định và được xem là đã đọc.
        sectionVisited[0] = true;

        UpdateAcceptButtonState();
    }

    private void OnDisable()
    {
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
                root.Q<Label>(
                    $"privacy-section-{sectionNumber}-arrow");
        }
    }

    private void InitializeHeader(VisualElement root)
    {
        headerController =
            new GeneralHeaderController(root);

        headerController.ConfigurePageWithIconAction(
            title: "Privacy & Terms",
            subtitle: "Last updated: June 15, 2025",
            iconClass: "icon-header-document",
            showBackButton: true,
            showSubtitleIcon: true);

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

        Label arrow =
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
            arrow.text =
                expanded ? "⌃" : "⌄";
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
