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
    // The same top/side spacing contract is used by ChatAIPageController.
    // CSS supplies a 54px/24px fallback while root geometry is unavailable.
    private const float HeaderTopFallback = 54f;
    private const float HeaderTopAfterSafeArea = 32f;
    private const float HeaderSidePadding = 24f;

    private readonly VisualElement pageRoot;
    private readonly VisualElement headerRoot;
    private bool largeSafeAreaEnabled;
    private float lastTopPadding = float.NaN;
    private float lastLeftPadding = float.NaN;
    private float lastRightPadding = float.NaN;

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
        this.pageRoot = pageRoot;

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

        // Root geometry changes when the panel settles or Android rotates.
        pageRoot.RegisterCallback<GeometryChangedEvent>(OnPageGeometryChanged);
        ApplyResponsiveInsets();
    }

    private void OnPageGeometryChanged(GeometryChangedEvent evt)
    {
        ApplyResponsiveInsets();
    }

    private void ApplyResponsiveInsets()
    {
        if (pageRoot == null || headerRoot == null)
            return;

        float panelWidth = pageRoot.resolvedStyle.width;
        float panelHeight = pageRoot.resolvedStyle.height;

        // The USS values are the fallback until UI Toolkit has laid out the panel.
        if (panelWidth <= 0f || panelHeight <= 0f ||
            float.IsNaN(panelWidth) || float.IsNaN(panelHeight))
            return;

        float screenWidth = Mathf.Max(1f, Screen.width);
        float screenHeight = Mathf.Max(1f, Screen.height);
        Rect safe = Screen.safeArea;

        // Screen.safeArea is in screen pixels; USS padding uses panel pixels.
        float topInset = Mathf.Max(0f,
            (screenHeight - safe.yMax) / screenHeight * panelHeight);
        float leftInset = Mathf.Max(0f,
            safe.xMin / screenWidth * panelWidth);
        float rightInset = Mathf.Max(0f,
            (screenWidth - safe.xMax) / screenWidth * panelWidth);

        float topPadding = Mathf.Max(
            largeSafeAreaEnabled ? 62f : HeaderTopFallback,
            topInset + HeaderTopAfterSafeArea +
                (largeSafeAreaEnabled ? 8f : 0f));
        float leftPadding = HeaderSidePadding + leftInset;
        float rightPadding = HeaderSidePadding + rightInset;

        // Avoid a geometry callback loop from reassigning identical styles.
        if (float.IsNaN(lastTopPadding) ||
            Mathf.Abs(lastTopPadding - topPadding) > 0.25f)
        {
            headerRoot.style.paddingTop = topPadding;
            lastTopPadding = topPadding;
        }

        if (float.IsNaN(lastLeftPadding) ||
            Mathf.Abs(lastLeftPadding - leftPadding) > 0.25f)
        {
            headerRoot.style.paddingLeft = leftPadding;
            lastLeftPadding = leftPadding;
        }

        if (float.IsNaN(lastRightPadding) ||
            Mathf.Abs(lastRightPadding - rightPadding) > 0.25f)
        {
            headerRoot.style.paddingRight = rightPadding;
            lastRightPadding = rightPadding;
        }
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
        largeSafeAreaEnabled = enabled;
        headerRoot.EnableInClassList(
            "header-large-safe-area",
            enabled);
        ApplyResponsiveInsets();
    }

    /// <summary>Shows/hides the red dot on the notification bell (unread notifications).</summary>
    public void SetNotificationDotVisible(bool visible)
    {
        SetVisible(notificationDot, visible);
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
        if (pageRoot != null)
            pageRoot.UnregisterCallback<GeometryChangedEvent>(OnPageGeometryChanged);

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