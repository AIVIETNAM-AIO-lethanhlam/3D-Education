using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(UIDocument))]
public class MainHomePageController : MonoBehaviour
{
    private Button myClassesBannerButton;
    private Button emptyStateActionButton;

    private Button categoryAllButton;
    private Button categoryPhysicsButton;
    private Button categoryChemistryButton;
    private Button categoryMathButton;
    private Button categoryProgrammingButton;

    private Label recentSectionTitle;
    private Label recentFilterLabel;
    private Label emptyStateTitle;
    private Label emptyStateDescription;

    private VisualElement recentCourseList;
    private VisualElement recentEmptyState;

    private string currentRole = "student";
    private Button selectedCategoryButton;

    private GeneralHeaderController headerController;
    private BottomNavigationController bottomNavigationController;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
        {
            Debug.LogError("Không tìm thấy UIDocument trong MainHomeScene.");
            return;
        }

        VisualElement root = document.rootVisualElement;

        if (root == null)
        {
            Debug.LogError("rootVisualElement của MainHomeScene đang null.");
            return;
        }

        QueryPageElements(root);
        InitializeHeader(root);
        InitializeBottomNavigation(root);
        RegisterPageEvents();

        // Mặc định mở category All:
        // vẫn hiển thị các course mẫu cùng phần trăm như giao diện cũ.
        SelectCategory("All", categoryAllButton);
    }

    private void OnDisable()
    {
        UnregisterPageEvents();
        DisposeHeader();
        DisposeBottomNavigation();
    }

    private void QueryPageElements(VisualElement root)
    {
        myClassesBannerButton =
            root.Q<Button>("my-classes-banner-button");

        emptyStateActionButton =
            root.Q<Button>("empty-state-action-button");

        categoryAllButton =
            root.Q<Button>("category-all-button");

        categoryPhysicsButton =
            root.Q<Button>("category-physics-button");

        categoryChemistryButton =
            root.Q<Button>("category-chemistry-button");

        categoryMathButton =
            root.Q<Button>("category-math-button");

        categoryProgrammingButton =
            root.Q<Button>("category-programming-button");

        recentSectionTitle =
            root.Q<Label>("recent-section-title");

        recentFilterLabel =
            root.Q<Label>("recent-filter-label");

        emptyStateTitle =
            root.Q<Label>("empty-state-title");

        emptyStateDescription =
            root.Q<Label>("empty-state-description");

        recentCourseList =
            root.Q<VisualElement>("recent-course-list");

        recentEmptyState =
            root.Q<VisualElement>("recent-empty-state");
    }

    private void InitializeHeader(VisualElement root)
    {
        headerController = new GeneralHeaderController(root);

        currentRole =
            PlayerPrefs.GetString("current_role", "student")
                .Trim()
                .ToLowerInvariant();

        string fullName =
            PlayerPrefs.GetString("current_full_name", "User");

        headerController.ConfigureHome(
            role: currentRole,
            userName: fullName,
            showNotification: true,
            showProfile: true,
            showNotificationDot: true);

        headerController.SetBottomBorderVisible(false);

        headerController.NotificationClicked += OpenNotifications;
        headerController.ProfileClicked += OpenProfile;

        UpdateRecentSectionForRole();
    }

    private void UpdateRecentSectionForRole()
    {
        bool isTeacher =
            string.Equals(
                currentRole,
                "teacher",
                StringComparison.OrdinalIgnoreCase);

        if (recentSectionTitle != null)
        {
            recentSectionTitle.text =
                isTeacher
                    ? "Recently Updated"
                    : "Recently Viewed";
        }

        if (emptyStateDescription != null)
        {
            emptyStateDescription.text =
                isTeacher
                    ? "You haven't created or updated any classes in this category yet."
                    : "You haven't enrolled in any classes in this category yet.";
        }

        if (emptyStateActionButton != null)
        {
            emptyStateActionButton.text =
                isTeacher
                    ? "Create Class"
                    : "Enroll Class";
        }
    }

    private void InitializeBottomNavigation(VisualElement root)
    {
        bottomNavigationController =
            new BottomNavigationController(
                root,
                BottomNavigationTab.Home);

        bottomNavigationController.HomeClicked += OpenHome;
        bottomNavigationController.MyClassesClicked += OpenMyClasses;
        bottomNavigationController.AIClicked += OpenAI;
        bottomNavigationController.SettingsClicked += OpenSettings;
    }

    private void DisposeHeader()
    {
        if (headerController == null)
        {
            return;
        }

        headerController.NotificationClicked -= OpenNotifications;
        headerController.ProfileClicked -= OpenProfile;

        headerController.Dispose();
        headerController = null;
    }

    private void DisposeBottomNavigation()
    {
        if (bottomNavigationController == null)
        {
            return;
        }

        bottomNavigationController.HomeClicked -= OpenHome;
        bottomNavigationController.MyClassesClicked -= OpenMyClasses;
        bottomNavigationController.AIClicked -= OpenAI;
        bottomNavigationController.SettingsClicked -= OpenSettings;

        bottomNavigationController.Dispose();
        bottomNavigationController = null;
    }

    private void RegisterPageEvents()
    {
        if (myClassesBannerButton != null)
        {
            myClassesBannerButton.clicked += OpenMyClasses;
        }

        if (emptyStateActionButton != null)
        {
            emptyStateActionButton.clicked += HandleEmptyStateAction;
        }

        if (categoryAllButton != null)
        {
            categoryAllButton.clicked += SelectAllCategory;
        }

        if (categoryPhysicsButton != null)
        {
            categoryPhysicsButton.clicked += SelectPhysicsCategory;
        }

        if (categoryChemistryButton != null)
        {
            categoryChemistryButton.clicked += SelectChemistryCategory;
        }

        if (categoryMathButton != null)
        {
            categoryMathButton.clicked += SelectMathCategory;
        }

        if (categoryProgrammingButton != null)
        {
            categoryProgrammingButton.clicked += SelectProgrammingCategory;
        }
    }

    private void UnregisterPageEvents()
    {
        if (myClassesBannerButton != null)
        {
            myClassesBannerButton.clicked -= OpenMyClasses;
        }

        if (emptyStateActionButton != null)
        {
            emptyStateActionButton.clicked -= HandleEmptyStateAction;
        }

        if (categoryAllButton != null)
        {
            categoryAllButton.clicked -= SelectAllCategory;
        }

        if (categoryPhysicsButton != null)
        {
            categoryPhysicsButton.clicked -= SelectPhysicsCategory;
        }

        if (categoryChemistryButton != null)
        {
            categoryChemistryButton.clicked -= SelectChemistryCategory;
        }

        if (categoryMathButton != null)
        {
            categoryMathButton.clicked -= SelectMathCategory;
        }

        if (categoryProgrammingButton != null)
        {
            categoryProgrammingButton.clicked -= SelectProgrammingCategory;
        }
    }

    private void SelectAllCategory()
    {
        SelectCategory("All", categoryAllButton);
    }

    private void SelectPhysicsCategory()
    {
        SelectCategory("Physics", categoryPhysicsButton);
    }

    private void SelectChemistryCategory()
    {
        SelectCategory("Chemistry", categoryChemistryButton);
    }

    private void SelectMathCategory()
    {
        SelectCategory("Math", categoryMathButton);
    }

    private void SelectProgrammingCategory()
    {
        SelectCategory("Programming", categoryProgrammingButton);
    }

    private void SelectCategory(
        string categoryName,
        Button categoryButton)
    {
        UpdateSelectedCategoryStyle(categoryButton);

        bool isAllCategory =
            string.Equals(
                categoryName,
                "All",
                StringComparison.OrdinalIgnoreCase);

        if (recentCourseList != null)
        {
            recentCourseList.style.display =
                isAllCategory
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }

        if (recentEmptyState != null)
        {
            recentEmptyState.style.display =
                isAllCategory
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
        }

        if (recentFilterLabel != null)
        {
            recentFilterLabel.style.display =
                isAllCategory
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;

            recentFilterLabel.text =
                isAllCategory
                    ? string.Empty
                    : "Filtered: " + categoryName;
        }

        if (emptyStateTitle != null && !isAllCategory)
        {
            emptyStateTitle.text = "No classes yet";
        }
    }

    private void UpdateSelectedCategoryStyle(Button newSelectedButton)
    {
        if (selectedCategoryButton != null)
        {
            selectedCategoryButton.RemoveFromClassList(
                "category-card-active");
        }

        selectedCategoryButton = newSelectedButton;

        if (selectedCategoryButton != null)
        {
            selectedCategoryButton.AddToClassList(
                "category-card-active");
        }
    }

    private void HandleEmptyStateAction()
    {
        bool isTeacher =
            string.Equals(
                currentRole,
                "teacher",
                StringComparison.OrdinalIgnoreCase);

        if (isTeacher)
        {
            Debug.Log("Teacher chọn tạo lớp học mới.");

            // Khi CreateClassScene đã có trong Build Profiles:
            // SceneNavigation.OpenScene("CreateClassScene");
        }
        else
        {
            Debug.Log("Student chọn đăng ký/tham gia lớp học.");

            // Khi EnrollClassScene đã có trong Build Profiles:
            // SceneNavigation.OpenScene("EnrollClassScene");
        }
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
        // Lưu lại role hiện tại trước khi chuyển Scene để MyClassesScene
        // có thể hiển thị đúng giao diện Teacher hoặc Student.
        string roleToSave = string.IsNullOrWhiteSpace(currentRole)
            ? PlayerPrefs.GetString("current_role", "student")
            : currentRole;

        roleToSave = roleToSave.Trim().ToLowerInvariant();

        PlayerPrefs.SetString("current_role", roleToSave);
        PlayerPrefs.Save();

        Debug.Log($"Mở MyClassesScene với role: {roleToSave}");

        SceneManager.LoadScene("MyClassesScene");
    }

    private void OpenAI()
    {
        Debug.Log("Mở chức năng AI.");

        // SceneNavigation.OpenScene("AIAssistantScene");
    }

    private void OpenSettings()
    {
        Debug.Log("Mở trang cài đặt.");

        // SceneNavigation.OpenScene("SettingsScene");
    }
}