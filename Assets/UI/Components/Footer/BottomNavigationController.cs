using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public enum BottomNavigationTab
{
    Home,
    MyClasses,
    AI,
    Settings
}

public class BottomNavigationController : IDisposable
{
    /*
     * Đổi các tên này nếu tên Scene thực tế trong project khác.
     */
    private const string HomeSceneName = "MainHomeScene";
    private const string MyClassesSceneName = "MyClassesScene";
    private const string ChatAISceneName = "ChatAIScene";
    private const string SettingsSceneName = "SettingScene";

    // Dedicated key so ChatAIScene always returns to the exact page
    // from which the AI footer button was opened.
    private const string ChatAIPreviousSceneKey = "chat_ai_previous_scene";

    private readonly VisualElement footerRoot;

    private readonly Button homeButton;
    private readonly Button classesButton;
    private readonly Button aiButton;
    private readonly Button settingsButton;

    private BottomNavigationTab activeTab;

    public event Action HomeClicked;
    public event Action MyClassesClicked;
    public event Action AIClicked;
    public event Action SettingsClicked;

    /*
     * Constructor này tự xác định tab active dựa theo Scene hiện tại.
     */
    public BottomNavigationController(
        VisualElement pageRoot)
        : this(
            pageRoot,
            DetectTabFromCurrentScene())
    {
    }

    /*
     * Constructor này cho phép truyền tab active thủ công.
     */
    public BottomNavigationController(
        VisualElement pageRoot,
        BottomNavigationTab activeTab)
    {
        if (pageRoot == null)
        {
            Debug.LogError(
                "BottomNavigationController: pageRoot đang null.");
            return;
        }

        footerRoot =
            pageRoot.Q<VisualElement>(
                "bottom-navigation");

        if (footerRoot == null)
        {
            Debug.LogError(
                "Không tìm thấy bottom-navigation trong UXML.");
            return;
        }

        homeButton =
            footerRoot.Q<Button>(
                "home-nav-button");

        classesButton =
            footerRoot.Q<Button>(
                "classes-nav-button");

        aiButton =
            footerRoot.Q<Button>(
                "ai-nav-button");

        settingsButton =
            footerRoot.Q<Button>(
                "settings-nav-button");

        ValidateElements();
        RegisterCallbacks();
        SetActiveTab(activeTab);
    }

    /* =========================================================
       DETECT CURRENT SCENE
       ========================================================= */

    private static BottomNavigationTab
        DetectTabFromCurrentScene()
    {
        string currentSceneName =
            SceneManager.GetActiveScene().name;

        switch (currentSceneName)
        {
            case HomeSceneName:
                return BottomNavigationTab.Home;

            case "MyClassesScene":
                return BottomNavigationTab.MyClasses;

            case ChatAISceneName:
                return BottomNavigationTab.AI;

            case SettingsSceneName:
                return BottomNavigationTab.Settings;

            default:
                Debug.LogWarning(
                    $"BottomNavigationController: " +
                    $"Không xác định được tab cho Scene " +
                    $"'{currentSceneName}'. Mặc định chọn Home.");

                return BottomNavigationTab.Home;
        }
    }

    private void ValidateElements()
    {
        if (homeButton == null)
        {
            Debug.LogError(
                "Không tìm thấy home-nav-button.");
        }

        if (classesButton == null)
        {
            Debug.LogError(
                "Không tìm thấy classes-nav-button.");
        }

        if (aiButton == null)
        {
            Debug.LogError(
                "Không tìm thấy ai-nav-button.");
        }

        if (settingsButton == null)
        {
            Debug.LogError(
                "Không tìm thấy settings-nav-button.");
        }
    }

    /* =========================================================
       REGISTER EVENTS
       ========================================================= */

    private void RegisterCallbacks()
    {
        if (homeButton != null)
        {
            homeButton.clicked +=
                HandleHomeClicked;
        }

        if (classesButton != null)
        {
            classesButton.clicked +=
                HandleMyClassesClicked;
        }

        if (aiButton != null)
        {
            aiButton.clicked +=
                HandleAiClicked;
        }

        if (settingsButton != null)
        {
            settingsButton.clicked +=
                HandleSettingsClicked;
        }
    }

    /* =========================================================
       BUTTON HANDLERS
       ========================================================= */

    private void HandleHomeClicked()
    {
        SetActiveTab(
            BottomNavigationTab.Home);

        HomeClicked?.Invoke();

        LoadSceneIfNeeded(
            HomeSceneName);
    }

    private void HandleMyClassesClicked()
    {
        SetActiveTab(
            BottomNavigationTab.MyClasses);

        MyClassesClicked?.Invoke();

        LoadSceneIfNeeded(
            MyClassesSceneName);
    }

    private void HandleAiClicked()
    {
        string currentSceneName =
            SceneManager.GetActiveScene().name;

        // Save the exact scene that opened ChatAIScene.
        // Examples:
        // MainHomeScene -> ChatAIScene -> Back -> MainHomeScene
        // MyClassesScene -> ChatAIScene -> Back -> MyClassesScene
        if (!string.Equals(
                currentSceneName,
                ChatAISceneName,
                StringComparison.Ordinal))
        {
            PlayerPrefs.SetString(
                ChatAIPreviousSceneKey,
                currentSceneName
            );

            PlayerPrefs.Save();
        }

        SetActiveTab(
            BottomNavigationTab.AI
        );

        AIClicked?.Invoke();

        LoadSceneIfNeeded(
            ChatAISceneName
        );
    }

    private void HandleSettingsClicked()
    {
        SetActiveTab(
            BottomNavigationTab.Settings);

        SettingsClicked?.Invoke();

        LoadSceneIfNeeded(
            SettingsSceneName);
    }

    /* =========================================================
       SCENE NAVIGATION
       ========================================================= */

    private void LoadSceneIfNeeded(
        string sceneName)
    {
        string currentSceneName =
            SceneManager.GetActiveScene().name;

        /*
         * Không load lại nếu đang ở đúng Scene.
         */
        if (string.Equals(
                currentSceneName,
                sceneName,
                StringComparison.Ordinal))
        {
            return;
        }

        /*
         * Kiểm tra Scene đã được thêm vào Build Profiles hay chưa.
         */
        if (!Application.CanStreamedLevelBeLoaded(
                sceneName))
        {
            Debug.LogError(
                $"Không thể mở Scene '{sceneName}'. " +
                "Hãy kiểm tra tên Scene và thêm Scene vào " +
                "File > Build Profiles > Scene List.");

            return;
        }

        Debug.Log(
            $"Bottom Navigation: {currentSceneName} -> {sceneName}");

        SceneManager.LoadScene(
            sceneName);
    }

    /* =========================================================
       ACTIVE TAB
       ========================================================= */

    public void SetActiveTab(
        BottomNavigationTab newActiveTab)
    {
        activeTab = newActiveTab;

        SetButtonActive(
            homeButton,
            activeTab ==
            BottomNavigationTab.Home);

        SetButtonActive(
            classesButton,
            activeTab ==
            BottomNavigationTab.MyClasses);

        SetButtonActive(
            aiButton,
            activeTab ==
            BottomNavigationTab.AI);

        SetButtonActive(
            settingsButton,
            activeTab ==
            BottomNavigationTab.Settings);
    }

    private static void SetButtonActive(
        Button button,
        bool isActive)
    {
        if (button == null)
        {
            return;
        }

        button.EnableInClassList(
            "nav-button-active",
            isActive);
    }

    public BottomNavigationTab GetActiveTab()
    {
        return activeTab;
    }

    /* =========================================================
       DISPOSE
       ========================================================= */

    public void Dispose()
    {
        if (homeButton != null)
        {
            homeButton.clicked -=
                HandleHomeClicked;
        }

        if (classesButton != null)
        {
            classesButton.clicked -=
                HandleMyClassesClicked;
        }

        if (aiButton != null)
        {
            aiButton.clicked -=
                HandleAiClicked;
        }

        if (settingsButton != null)
        {
            settingsButton.clicked -=
                HandleSettingsClicked;
        }

        HomeClicked = null;
        MyClassesClicked = null;
        AIClicked = null;
        SettingsClicked = null;
    }
}