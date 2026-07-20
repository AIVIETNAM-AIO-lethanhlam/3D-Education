using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class MainHomePageController : MonoBehaviour
{
    private Label welcomeRoleLabel;
    private Label userNameLabel;

    private Button notificationButton;
    private Button profileButton;
    private Button myClassesBannerButton;

    private Button homeNavButton;
    private Button classesNavButton;
    private Button aiNavButton;
    private Button settingsNavButton;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
        {
            Debug.LogError("Không tìm thấy UIDocument trong MainHomeScene.");
            return;
        }

        VisualElement root = document.rootVisualElement;

        welcomeRoleLabel =
            root.Q<Label>("welcome-role-label");

        userNameLabel =
            root.Q<Label>("user-name-label");

        notificationButton =
            root.Q<Button>("notification-button");

        profileButton =
            root.Q<Button>("profile-button");

        myClassesBannerButton =
            root.Q<Button>("my-classes-banner-button");

        homeNavButton =
            root.Q<Button>("home-nav-button");

        classesNavButton =
            root.Q<Button>("classes-nav-button");

        aiNavButton =
            root.Q<Button>("ai-nav-button");

        settingsNavButton =
            root.Q<Button>("settings-nav-button");

        LoadCurrentUser();
        RegisterEvents();
    }

    private void OnDisable()
    {
        UnregisterEvents();
    }

    private void LoadCurrentUser()
    {
        string role =
            PlayerPrefs.GetString("current_role", "student");

        string fullName =
            PlayerPrefs.GetString("current_full_name", "User");

        string displayRole =
            role == "teacher"
                ? "Teacher"
                : "Student";

        if (welcomeRoleLabel != null)
        {
            welcomeRoleLabel.text =
                $"Hello, {displayRole}";
        }

        if (userNameLabel != null)
        {
            userNameLabel.text = fullName;
        }
    }

    private void RegisterEvents()
    {
        if (notificationButton != null)
            notificationButton.clicked += OpenNotifications;

        if (profileButton != null)
            profileButton.clicked += OpenProfile;

        if (myClassesBannerButton != null)
            myClassesBannerButton.clicked += OpenMyClasses;

        if (homeNavButton != null)
            homeNavButton.clicked += OpenHome;

        if (classesNavButton != null)
            classesNavButton.clicked += OpenMyClasses;

        if (aiNavButton != null)
            aiNavButton.clicked += OpenAI;

        if (settingsNavButton != null)
            settingsNavButton.clicked += OpenSettings;
    }

    private void UnregisterEvents()
    {
        if (notificationButton != null)
            notificationButton.clicked -= OpenNotifications;

        if (profileButton != null)
            profileButton.clicked -= OpenProfile;

        if (myClassesBannerButton != null)
            myClassesBannerButton.clicked -= OpenMyClasses;

        if (homeNavButton != null)
            homeNavButton.clicked -= OpenHome;

        if (classesNavButton != null)
            classesNavButton.clicked -= OpenMyClasses;

        if (aiNavButton != null)
            aiNavButton.clicked -= OpenAI;

        if (settingsNavButton != null)
            settingsNavButton.clicked -= OpenSettings;
    }

    private void OpenNotifications()
    {
        Debug.Log("Mở trang thông báo.");
    }

    private void OpenProfile()
    {
        Debug.Log("Mở trang hồ sơ cá nhân.");
    }

    private void OpenHome()
    {
        Debug.Log("Đang ở trang chủ.");
    }

    private void OpenMyClasses()
    {
        Debug.Log("Mở danh sách lớp học.");
    }

    private void OpenAI()
    {
        Debug.Log("Mở chức năng AI.");
    }

    private void OpenSettings()
    {
        Debug.Log("Mở trang cài đặt.");
    }
}