using System;
using UnityEngine;
using UnityEngine.UIElements;

public enum GeneralHeaderType
{
    Home,
    Page
}

public enum HeaderRightActionType
{
    None,
    Icon,
    Text
}

public class GeneralHeaderController : IDisposable
{
    private readonly VisualElement headerRoot;

    private readonly VisualElement homeHeaderLayout;
    private readonly VisualElement pageHeaderLayout;

    private readonly Label welcomeLabel;
    private readonly Label userNameLabel;

    private readonly Button notificationButton;
    private readonly Button profileButton;
    private readonly VisualElement notificationDot;

    private readonly Button backButton;
    private readonly Label pageTitleLabel;

    private readonly VisualElement subtitleContainer;
    private readonly VisualElement subtitleIcon;
    private readonly Label pageSubtitleLabel;

    private readonly Button rightIconButton;
    private readonly VisualElement rightIcon;

    private readonly Button rightTextButton;
    private readonly Label rightTextPrefix;
    private readonly Label rightTextLabel;

    private GeneralHeaderType currentHeaderType;

    public event Action NotificationClicked;
    public event Action ProfileClicked;
    public event Action BackClicked;
    public event Action RightActionClicked;

    public GeneralHeaderController(VisualElement pageRoot)
    {
        if (pageRoot == null)
        {
            Debug.LogError(
                "GeneralHeaderController: pageRoot đang null.");

            return;
        }

        headerRoot =
            pageRoot.Q<VisualElement>("general-header");

        if (headerRoot == null)
        {
            Debug.LogError(
                "Không tìm thấy general-header trong UXML.");

            return;
        }

        homeHeaderLayout =
            headerRoot.Q<VisualElement>(
                "home-header-layout");

        pageHeaderLayout =
            headerRoot.Q<VisualElement>(
                "page-header-layout");

        welcomeLabel =
            headerRoot.Q<Label>(
                "header-welcome-label");

        userNameLabel =
            headerRoot.Q<Label>(
                "header-user-name-label");

        notificationButton =
            headerRoot.Q<Button>(
                "header-notification-button");

        profileButton =
            headerRoot.Q<Button>(
                "header-profile-button");

        notificationDot =
            headerRoot.Q<VisualElement>(
                "header-notification-dot");

        backButton =
            headerRoot.Q<Button>(
                "header-back-button");

        pageTitleLabel =
            headerRoot.Q<Label>(
                "header-page-title-label");

        subtitleContainer =
            headerRoot.Q<VisualElement>(
                "header-subtitle-container");

        subtitleIcon =
            headerRoot.Q<VisualElement>(
                "header-subtitle-icon");

        pageSubtitleLabel =
            headerRoot.Q<Label>(
                "header-page-subtitle-label");

        rightIconButton =
            headerRoot.Q<Button>(
                "header-right-icon-button");

        rightIcon =
            headerRoot.Q<VisualElement>(
                "header-right-icon");

        rightTextButton =
            headerRoot.Q<Button>(
                "header-right-text-button");

        rightTextPrefix =
            headerRoot.Q<Label>(
                "header-right-text-prefix");

        rightTextLabel =
            headerRoot.Q<Label>(
                "header-right-text-label");

        RegisterCallbacks();

        SetHeaderType(GeneralHeaderType.Home);
        SetRightActionType(HeaderRightActionType.None);
    }

    private void RegisterCallbacks()
    {
        if (notificationButton != null)
        {
            notificationButton.clicked +=
                HandleNotificationClicked;
        }

        if (profileButton != null)
        {
            profileButton.clicked +=
                HandleProfileClicked;
        }

        if (backButton != null)
        {
            backButton.clicked +=
                HandleBackClicked;
        }

        if (rightIconButton != null)
        {
            rightIconButton.clicked +=
                HandleRightActionClicked;
        }

        if (rightTextButton != null)
        {
            rightTextButton.clicked +=
                HandleRightActionClicked;
        }
    }

    public void ConfigureHome(
        string role,
        string userName,
        bool showNotification = true,
        bool showProfile = true,
        bool showNotificationDot = true)
    {
        SetHeaderType(GeneralHeaderType.Home);
        SetRightActionType(HeaderRightActionType.None);

        string formattedRole =
            string.Equals(
                role,
                "teacher",
                StringComparison.OrdinalIgnoreCase)
                ? "Teacher"
                : "Student";

        if (welcomeLabel != null)
        {
            welcomeLabel.text =
                $"Hello, {formattedRole}";
        }

        if (userNameLabel != null)
        {
            userNameLabel.text =
                string.IsNullOrWhiteSpace(userName)
                    ? "User"
                    : userName;
        }

        SetVisible(
            notificationButton,
            showNotification);

        SetVisible(
            profileButton,
            showProfile);

        SetVisible(
            notificationDot,
            showNotification &&
            showNotificationDot);
    }

    public void ConfigurePage(
        string title,
        string subtitle = null,
        bool showBackButton = true,
        bool showSubtitleIcon = false)
    {
        SetHeaderType(GeneralHeaderType.Page);
        SetRightActionType(HeaderRightActionType.None);

        if (pageTitleLabel != null)
        {
            pageTitleLabel.text =
                string.IsNullOrWhiteSpace(title)
                    ? "Page"
                    : title;
        }

        SetVisible(
            backButton,
            showBackButton);

        bool hasSubtitle =
            !string.IsNullOrWhiteSpace(subtitle);

        SetVisible(
            subtitleContainer,
            hasSubtitle);

        if (pageSubtitleLabel != null)
        {
            pageSubtitleLabel.text =
                hasSubtitle
                    ? subtitle
                    : string.Empty;
        }

        SetVisible(
            subtitleIcon,
            hasSubtitle &&
            showSubtitleIcon);
    }

    public void ConfigurePageWithIconAction(
        string title,
        string subtitle,
        string iconClass,
        bool showBackButton = true,
        bool showSubtitleIcon = false)
    {
        ConfigurePage(
            title,
            subtitle,
            showBackButton,
            showSubtitleIcon);

        SetRightActionType(
            HeaderRightActionType.Icon);

        if (rightIcon == null)
        {
            return;
        }

        rightIcon.ClearClassList();
        rightIcon.AddToClassList(
            "header-right-icon");

        if (!string.IsNullOrWhiteSpace(iconClass))
        {
            rightIcon.AddToClassList(
                iconClass);
        }
    }

    public void ConfigurePageWithTextAction(
        string title,
        string subtitle,
        string actionText,
        string actionPrefix = "+",
        string actionStyleClass = null,
        bool showBackButton = true)
    {
        ConfigurePage(
            title,
            subtitle,
            showBackButton,
            showSubtitleIcon: false);

        SetRightActionType(
            HeaderRightActionType.Text);

        if (rightTextLabel != null)
        {
            rightTextLabel.text =
                string.IsNullOrWhiteSpace(actionText)
                    ? "Action"
                    : actionText;
        }

        if (rightTextPrefix != null)
        {
            rightTextPrefix.text =
                actionPrefix ?? string.Empty;

            SetVisible(
                rightTextPrefix,
                !string.IsNullOrWhiteSpace(
                    actionPrefix));
        }

        if (rightTextButton == null)
        {
            return;
        }

        rightTextButton.RemoveFromClassList(
            "header-action-create-class");

        rightTextButton.RemoveFromClassList(
            "header-action-enroll-class");

        if (!string.IsNullOrWhiteSpace(
                actionStyleClass))
        {
            rightTextButton.AddToClassList(
                actionStyleClass);
        }
    }

    public void SetHeaderType(
        GeneralHeaderType headerType)
    {
        currentHeaderType = headerType;

        bool isHome =
            headerType == GeneralHeaderType.Home;

        SetVisible(
            homeHeaderLayout,
            isHome);

        SetVisible(
            pageHeaderLayout,
            !isHome);

        if (headerRoot == null)
        {
            return;
        }

        headerRoot.RemoveFromClassList(
            "general-header-home");

        headerRoot.RemoveFromClassList(
            "general-header-page");

        headerRoot.AddToClassList(
            isHome
                ? "general-header-home"
                : "general-header-page");
    }

    public void SetRightActionType(
        HeaderRightActionType actionType)
    {
        SetVisible(
            rightIconButton,
            actionType ==
            HeaderRightActionType.Icon);

        SetVisible(
            rightTextButton,
            actionType ==
            HeaderRightActionType.Text);
    }

    public void SetBottomBorderVisible(
        bool visible)
    {
        if (headerRoot == null)
        {
            return;
        }

        headerRoot.EnableInClassList(
            "header-no-bottom-border",
            !visible);
    }

    public void SetCompact(bool compact)
    {
        if (headerRoot == null)
        {
            return;
        }

        /*
         USS sẽ tự quyết định khoảng cách compact riêng
         cho Home và Page dựa vào general-header-home/page.
        */
        headerRoot.EnableInClassList(
            "header-compact",
            compact);
    }

    public void SetLargeSafeArea(bool enabled)
    {
        if (headerRoot == null)
        {
            return;
        }

        /*
         Chỉ nên dùng cho page header trên thiết bị
         có notch hoặc status bar lớn.
        */
        headerRoot.EnableInClassList(
            "header-large-safe-area",
            enabled);
    }

    public void SetCustomClass(
        string className,
        bool enabled = true)
    {
        if (headerRoot == null ||
            string.IsNullOrWhiteSpace(className))
        {
            return;
        }

        headerRoot.EnableInClassList(
            className,
            enabled);
    }

    public GeneralHeaderType GetHeaderType()
    {
        return currentHeaderType;
    }

    private static void SetVisible(
        VisualElement element,
        bool visible)
    {
        if (element == null)
        {
            return;
        }

        element.style.display =
            visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
    }

    private void HandleNotificationClicked()
    {
        NotificationClicked?.Invoke();
    }

    private void HandleProfileClicked()
    {
        ProfileClicked?.Invoke();
    }

    private void HandleBackClicked()
    {
        /*
         * Nếu Scene hiện tại có đăng ký xử lý Back riêng,
         * GeneralHeader sẽ ưu tiên callback đó.
         *
         * Nếu không có callback riêng, nút Back tự động quay
         * về Scene trước đó thông qua SceneHistory.
         */
        if (BackClicked != null)
        {
            BackClicked.Invoke();
            return;
        }

        SceneHistory.GoBack("MainHomeScene");
    }

    private void HandleRightActionClicked()
    {
        RightActionClicked?.Invoke();
    }

    public void Dispose()
    {
        if (notificationButton != null)
        {
            notificationButton.clicked -=
                HandleNotificationClicked;
        }

        if (profileButton != null)
        {
            profileButton.clicked -=
                HandleProfileClicked;
        }

        if (backButton != null)
        {
            backButton.clicked -=
                HandleBackClicked;
        }

        if (rightIconButton != null)
        {
            rightIconButton.clicked -=
                HandleRightActionClicked;
        }

        if (rightTextButton != null)
        {
            rightTextButton.clicked -=
                HandleRightActionClicked;
        }

        NotificationClicked = null;
        ProfileClicked = null;
        BackClicked = null;
        RightActionClicked = null;
    }
}