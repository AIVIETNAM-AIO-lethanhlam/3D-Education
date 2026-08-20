using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class MainHomePageController : MonoBehaviour
{
    private const int CollapsedRecentLimit = 3;

    private VisualElement root;

    private Button myClassesBannerButton;
    private Button emptyStateActionButton;
    private Button seeAllRecentButton;

    private Button categoryAllButton;
    private Button categoryPhysicsButton;
    private Button categoryChemistryButton;
    private Button categoryMathButton;
    private Button categoryProgrammingButton;

    private Label activeClassCountLabel;
    private Label publicClassCountLabel;
    private Label privateClassCountLabel;

    private Label recentSectionTitle;
    private Label recentFilterLabel;
    private Label emptyStateTitle;
    private Label emptyStateDescription;
    private Label seeAllRecentText;

    private VisualElement recentCourseList;
    private VisualElement recentEmptyState;

    private string currentRole = "student";
    private string selectedCategoryName = "All";
    private bool showAllRecentClasses;
    private bool isLoading;

    private Button selectedCategoryButton;

    private GeneralHeaderController headerController;
    private BottomNavigationController bottomNavigationController;

    private readonly List<HomeClassItem> allClasses = new();
    private readonly Dictionary<string, string> categoryNamesById =
        new(StringComparer.OrdinalIgnoreCase);

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
        {
            Debug.LogError("Không tìm thấy UIDocument trong MainHomeScene.");
            return;
        }

        root = document.rootVisualElement;

        if (root == null)
        {
            Debug.LogError("rootVisualElement của MainHomeScene đang null.");
            return;
        }

        QueryPageElements();
        InitializeRole();
        InitializeHeader();
        InitializeBottomNavigation();
        RegisterPageEvents();

        SelectCategory("All", categoryAllButton, reload: false);
        StartCoroutine(LoadHomeClassData());
    }

    private void OnDisable()
    {
        UnregisterPageEvents();
        DisposeHeader();
        DisposeBottomNavigation();
    }

    private void QueryPageElements()
    {
        myClassesBannerButton =
            root.Q<Button>("my-classes-banner-button");

        emptyStateActionButton =
            root.Q<Button>("empty-state-action-button");

        seeAllRecentButton =
            root.Q<Button>("see-all-recent-button");

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

        activeClassCountLabel =
            root.Q<Label>("active-class-count-label");

        publicClassCountLabel =
            root.Q<Label>("public-class-count-label");

        privateClassCountLabel =
            root.Q<Label>("private-class-count-label");

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

        seeAllRecentText =
            seeAllRecentButton?.Q<Label>(className: "see-all-text");
    }

    private void InitializeRole()
    {
        string sessionRole = SupabaseSession.Role;

        currentRole = !string.IsNullOrWhiteSpace(sessionRole)
            ? sessionRole.Trim().ToLowerInvariant()
            : PlayerPrefs.GetString("current_role", "student")
                .Trim()
                .ToLowerInvariant();

        PlayerPrefs.SetString("current_role", currentRole);
        PlayerPrefs.Save();

        Debug.Log(
            $"[MainHome] Current role: {currentRole}, " +
            $"UserId: {SupabaseSession.UserId}, " +
            $"LoggedIn: {SupabaseSession.IsLoggedIn}"
        );
    }

    private void InitializeHeader()
    {
        headerController = new GeneralHeaderController(root);

        string fullName =
            PlayerPrefs.GetString(
                "current_full_name",
                PlayerPrefs.GetString("full_name", "User")
            );

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

    private void InitializeBottomNavigation()
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

    private void RegisterPageEvents()
    {
        if (myClassesBannerButton != null)
            myClassesBannerButton.clicked += OpenMyClasses;

        if (emptyStateActionButton != null)
            emptyStateActionButton.clicked += HandleEmptyStateAction;

        if (seeAllRecentButton != null)
            seeAllRecentButton.clicked += ToggleSeeAllRecent;

        if (categoryAllButton != null)
            categoryAllButton.clicked += SelectAllCategory;

        if (categoryPhysicsButton != null)
            categoryPhysicsButton.clicked += SelectPhysicsCategory;

        if (categoryChemistryButton != null)
            categoryChemistryButton.clicked += SelectChemistryCategory;

        if (categoryMathButton != null)
            categoryMathButton.clicked += SelectMathCategory;

        if (categoryProgrammingButton != null)
            categoryProgrammingButton.clicked += SelectProgrammingCategory;
    }

    private void UnregisterPageEvents()
    {
        if (myClassesBannerButton != null)
            myClassesBannerButton.clicked -= OpenMyClasses;

        if (emptyStateActionButton != null)
            emptyStateActionButton.clicked -= HandleEmptyStateAction;

        if (seeAllRecentButton != null)
            seeAllRecentButton.clicked -= ToggleSeeAllRecent;

        if (categoryAllButton != null)
            categoryAllButton.clicked -= SelectAllCategory;

        if (categoryPhysicsButton != null)
            categoryPhysicsButton.clicked -= SelectPhysicsCategory;

        if (categoryChemistryButton != null)
            categoryChemistryButton.clicked -= SelectChemistryCategory;

        if (categoryMathButton != null)
            categoryMathButton.clicked -= SelectMathCategory;

        if (categoryProgrammingButton != null)
            categoryProgrammingButton.clicked -= SelectProgrammingCategory;
    }

    private IEnumerator LoadHomeClassData()
    {
        if (isLoading)
            yield break;

        isLoading = true;
        ShowLoadingState();

        yield return LoadCategoryNames();

        if (IsTeacher())
        {
            SupabaseClass[] records = null;
            string error = null;

            yield return SupabaseClassService.GetTeacherClasses(
                result => records = result,
                message => error = message
            );

            if (!string.IsNullOrWhiteSpace(error))
            {
                FinishLoadWithError(error);
                yield break;
            }

            BuildTeacherItems(records);

            Debug.Log(
                $"[MainHome] Teacher classes loaded: {allClasses.Count}"
            );
        }
        else
        {
            yield return LoadStudentItemsDirectly();

            if (!isLoading)
                yield break;

            Debug.Log(
                $"[MainHome] Student enrolled classes loaded: {allClasses.Count}"
            );
        }

        isLoading = false;

        SortByRecentInteraction();
        UpdateBannerCounts();
        RenderRecentClasses();
    }

    private IEnumerator LoadStudentItemsDirectly()
    {
        allClasses.Clear();

        if (!SupabaseSession.IsLoggedIn ||
            !Guid.TryParse(SupabaseSession.UserId, out _))
        {
            FinishLoadWithError(
                "Student session is missing or invalid."
            );
            yield break;
        }

        string escapedUserId =
            Uri.EscapeDataString(
                SupabaseSession.UserId.Trim()
            );

        string membershipJson = null;
        string membershipError = null;

        yield return SupabaseRestService.Get(
            "class_members" +
            $"?user_id=eq.{escapedUserId}" +
            "&member_role=eq.student" +
            "&status=eq.enrolled" +
            "&select=id,class_id,user_id,status,joined_at" +
            "&order=joined_at.desc",
            json => membershipJson = json,
            message => membershipError = message
        );

        if (!string.IsNullOrWhiteSpace(membershipError))
        {
            FinishLoadWithError(membershipError);
            yield break;
        }

        if (!TryParseArray(
                membershipJson,
                out StudentMembershipRow[] memberships,
                out string membershipParseError))
        {
            FinishLoadWithError(membershipParseError);
            yield break;
        }

        foreach (StudentMembershipRow membership in memberships)
        {
            if (membership == null ||
                !Guid.TryParse(membership.class_id, out _))
            {
                continue;
            }

            SupabaseClass classRecord = null;
            string classError = null;

            yield return SupabaseClassService.GetClassById(
                membership.class_id,
                result => classRecord = result,
                message => classError = message
            );

            if (!string.IsNullOrWhiteSpace(classError))
            {
                Debug.LogWarning(
                    $"[MainHome] Không tải được class " +
                    $"{membership.class_id}: {classError}"
                );
                continue;
            }

            if (classRecord == null)
                continue;

            string teacherName = "Unknown Teacher";

            if (Guid.TryParse(classRecord.teacher_id, out _))
            {
                yield return LoadTeacherName(
                    classRecord.teacher_id,
                    name => teacherName = name
                );
            }

            allClasses.Add(
                new HomeClassItem
                {
                    ClassId =
                        classRecord.id ??
                        membership.class_id,

                    ClassName =
                        classRecord.class_name ??
                        "Untitled Class",

                    ClassCode =
                        classRecord.class_code ??
                        string.Empty,

                    CategoryId =
                        classRecord.category_id ??
                        string.Empty,

                    CategoryName =
                        ResolveCategoryName(
                            classRecord.category_id
                        ),

                    Visibility =
                        NormalizeVisibility(
                            classRecord.visibility
                        ),

                    TeacherName =
                        teacherName,

                    ProgressPercent = 0f,

                    DatabaseInteractionTime =
                        ParseDate(membership.joined_at),

                    LocalInteractionTime =
                        ClassInteractionHistory
                            .GetLastInteraction(
                                classRecord.id ??
                                membership.class_id
                            )
                }
            );
        }
    }

    private IEnumerator LoadTeacherName(
        string teacherId,
        Action<string> onLoaded)
    {
        string escapedTeacherId =
            Uri.EscapeDataString(
                teacherId.Trim()
            );

        string profileJson = null;
        string profileError = null;

        yield return SupabaseRestService.Get(
            "profiles" +
            $"?id=eq.{escapedTeacherId}" +
            "&select=full_name" +
            "&limit=1",
            json => profileJson = json,
            message => profileError = message
        );

        if (!string.IsNullOrWhiteSpace(profileError))
        {
            onLoaded?.Invoke("Unknown Teacher");
            yield break;
        }

        if (!TryParseArray(
                profileJson,
                out TeacherProfileRow[] profiles,
                out _)
            || profiles.Length == 0)
        {
            onLoaded?.Invoke("Unknown Teacher");
            yield break;
        }

        onLoaded?.Invoke(
            string.IsNullOrWhiteSpace(
                profiles[0].full_name)
                ? "Unknown Teacher"
                : profiles[0].full_name.Trim()
        );
    }

    private IEnumerator LoadCategoryNames()
    {
        categoryNamesById.Clear();

        string response = null;
        string error = null;

        yield return SupabaseRestService.Get(
            "categories?select=id,name",
            json => response = json,
            message => error = message
        );

        // Một số phiên bản schema cũ dùng category_id/category_name.
        // Nếu query id/name thất bại thì thử lại bằng alias PostgREST.
        if (!string.IsNullOrWhiteSpace(error))
        {
            response = null;
            error = null;

            yield return SupabaseRestService.Get(
                "categories?select=id:category_id,name:category_name",
                json => response = json,
                message => error = message
            );
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogWarning(
                "Không tải được tên categories. " +
                "MainHome vẫn hiển thị lớp bằng category_id. " +
                error
            );
            yield break;
        }

        if (string.IsNullOrWhiteSpace(response))
            yield break;

        try
        {
            CategoryRecordArray wrapper =
                JsonUtility.FromJson<CategoryRecordArray>(
                    "{\"items\":" + response + "}"
                );

            if (wrapper?.items == null)
                yield break;

            foreach (CategoryRecord category in wrapper.items)
            {
                if (category == null ||
                    string.IsNullOrWhiteSpace(category.id))
                {
                    continue;
                }

                categoryNamesById[category.id] =
                    string.IsNullOrWhiteSpace(category.name)
                        ? "Others"
                        : category.name.Trim();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Không parse được categories: " +
                exception.Message
            );
        }
    }

    private void BuildTeacherItems(SupabaseClass[] records)
    {
        allClasses.Clear();

        if (records == null)
            return;

        foreach (SupabaseClass record in records)
        {
            if (record == null)
                continue;

            allClasses.Add(
                new HomeClassItem
                {
                    ClassId = record.id,
                    ClassName = record.class_name,
                    ClassCode = record.class_code,
                    CategoryId = record.category_id,
                    CategoryName = ResolveCategoryName(record.category_id),
                    Visibility = NormalizeVisibility(record.visibility),
                    TeacherName = string.Empty,
                    ProgressPercent = 0f,
                    DatabaseInteractionTime = ParseDate(record.updated_at),
                    LocalInteractionTime =
                        ClassInteractionHistory.GetLastInteraction(record.id)
                }
            );
        }
    }

    private void BuildStudentItems(StudentEnrolledClass[] records)
    {
        allClasses.Clear();

        if (records == null)
            return;

        foreach (StudentEnrolledClass record in records)
        {
            if (record == null)
                continue;

            allClasses.Add(
                new HomeClassItem
                {
                    ClassId = record.class_id,
                    ClassName = record.class_name,
                    ClassCode = record.class_code,
                    CategoryId = record.category_id,
                    CategoryName = ResolveCategoryName(record.category_id),
                    Visibility = NormalizeVisibility(record.visibility),
                    ProgressPercent =
                        Mathf.Clamp(record.progress_percent, 0f, 100f),
                    DatabaseInteractionTime = ParseDate(record.joined_at),
                    LocalInteractionTime =
                        ClassInteractionHistory.GetLastInteraction(record.class_id)
                }
            );
        }
    }

    private void SortByRecentInteraction()
    {
        allClasses.Sort((left, right) =>
            right.EffectiveInteractionTime.CompareTo(
                left.EffectiveInteractionTime
            )
        );
    }

    private void UpdateBannerCounts()
    {
        int total = allClasses.Count;

        int publicCount = allClasses.Count(item =>
            string.Equals(
                item.Visibility,
                "public",
                StringComparison.OrdinalIgnoreCase
            )
        );

        int privateCount = allClasses.Count(item =>
            string.Equals(
                item.Visibility,
                "private",
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (activeClassCountLabel != null)
        {
            activeClassCountLabel.text =
                total == 1
                    ? "1 Active Class"
                    : $"{total} Active Classes";
        }

        if (publicClassCountLabel != null)
            publicClassCountLabel.text = $"{publicCount} Public";

        if (privateClassCountLabel != null)
            privateClassCountLabel.text = $"{privateCount} Private";
    }

    private void RenderRecentClasses()
    {
        if (recentCourseList == null)
            return;

        recentCourseList.Clear();

        List<HomeClassItem> filtered = allClasses
            .Where(MatchesSelectedCategory)
            .ToList();

        int visibleCount = showAllRecentClasses
            ? filtered.Count
            : Mathf.Min(CollapsedRecentLimit, filtered.Count);

        for (int i = 0; i < visibleCount; i++)
        {
            recentCourseList.Add(
                CreateRecentClassCard(filtered[i])
            );
        }

        bool hasClasses = filtered.Count > 0;

        SetVisible(recentCourseList, hasClasses);
        SetVisible(recentEmptyState, !hasClasses);

        if (!hasClasses)
        {
            if (emptyStateTitle != null)
                emptyStateTitle.text = "No classes yet";

            if (emptyStateDescription != null)
            {
                emptyStateDescription.text = IsTeacher()
                    ? "You have not created or updated any classes in this category yet."
                    : "You have not enrolled in or viewed any classes in this category yet.";
            }
        }

        bool canExpand = filtered.Count > CollapsedRecentLimit;

        if (seeAllRecentButton != null)
            seeAllRecentButton.style.display =
                canExpand ? DisplayStyle.Flex : DisplayStyle.None;

        if (seeAllRecentText != null)
            seeAllRecentText.text =
                showAllRecentClasses ? "Show less" : "See all";

        if (recentFilterLabel != null)
        {
            bool isAll = string.Equals(
                selectedCategoryName,
                "All",
                StringComparison.OrdinalIgnoreCase
            );

            recentFilterLabel.style.display =
                isAll ? DisplayStyle.None : DisplayStyle.Flex;

            recentFilterLabel.text =
                isAll ? string.Empty : $"Filtered: {selectedCategoryName}";
        }
    }

    private VisualElement CreateRecentClassCard(
        HomeClassItem item)
    {
        Button card = new(() => OpenClass(item));
        card.AddToClassList("recent-class-card");

        VisualElement content = new();
        content.AddToClassList("recent-class-card-content");

        VisualElement iconShell = new();
        iconShell.AddToClassList("recent-class-icon-shell");

        VisualElement icon = new();
        icon.AddToClassList("recent-class-icon");

        iconShell.Add(icon);

        VisualElement information = new();
        information.AddToClassList("recent-class-information");

        string displayName =
            string.IsNullOrWhiteSpace(item.ClassName)
                ? "Untitled Class"
                : item.ClassName.Trim();

        if (!string.IsNullOrWhiteSpace(item.ClassCode))
        {
            displayName =
                $"{item.ClassCode.Trim()} – {displayName}";
        }

        Label title = new(displayName);
        title.AddToClassList("recent-class-title");

        VisualElement metaRow = new();
        metaRow.AddToClassList("recent-class-meta-row");

        Label category = new(
            string.IsNullOrWhiteSpace(item.CategoryName)
                ? "Others"
                : item.CategoryName
        );
        category.AddToClassList("recent-class-category");

        Label separatorOne = new("·");
        separatorOne.AddToClassList("recent-class-separator");

        Label visibility = new(
            string.Equals(
                item.Visibility,
                "private",
                StringComparison.OrdinalIgnoreCase)
                ? "Private"
                : "Public"
        );
        visibility.AddToClassList("recent-class-visibility");

        Label separatorTwo = new("·");
        separatorTwo.AddToClassList("recent-class-separator");

        Label interactionTime =
            new(GetRelativeInteractionText(
                item.EffectiveInteractionTime
            ));
        interactionTime.AddToClassList("recent-class-time");

        metaRow.Add(category);
        metaRow.Add(separatorOne);
        metaRow.Add(visibility);

        if (!IsTeacher() &&
            !string.IsNullOrWhiteSpace(item.TeacherName))
        {
            Label teacherSeparator = new("·");
            teacherSeparator.AddToClassList(
                "recent-class-separator"
            );

            Label teacherName = new(item.TeacherName);
            teacherName.AddToClassList(
                "recent-class-teacher"
            );

            metaRow.Add(teacherSeparator);
            metaRow.Add(teacherName);
        }

        metaRow.Add(separatorTwo);
        metaRow.Add(interactionTime);

        information.Add(title);
        information.Add(metaRow);

        Label arrow = new("›");
        arrow.AddToClassList("recent-class-arrow");

        content.Add(iconShell);
        content.Add(information);
        content.Add(arrow);

        card.Add(content);
        return card;
    }

    private bool MatchesSelectedCategory(HomeClassItem item)
    {
        if (string.Equals(
                selectedCategoryName,
                "All",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string category =
            item.CategoryName?.Trim() ?? string.Empty;

        if (string.Equals(
                selectedCategoryName,
                "Programming",
                StringComparison.OrdinalIgnoreCase))
        {
            return category.Equals(
                       "Programming",
                       StringComparison.OrdinalIgnoreCase) ||
                   category.Equals(
                       "Technology",
                       StringComparison.OrdinalIgnoreCase) ||
                   category.Equals(
                       "Computer Science",
                       StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
            category,
            selectedCategoryName,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private void ToggleSeeAllRecent()
    {
        showAllRecentClasses = !showAllRecentClasses;
        RenderRecentClasses();
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
        Button categoryButton,
        bool reload = true)
    {
        selectedCategoryName =
            string.IsNullOrWhiteSpace(categoryName)
                ? "All"
                : categoryName.Trim();

        showAllRecentClasses = false;
        UpdateSelectedCategoryStyle(categoryButton);

        if (reload)
            RenderRecentClasses();
    }

    private void UpdateSelectedCategoryStyle(
        Button newSelectedButton)
    {
        selectedCategoryButton?.RemoveFromClassList(
            "category-card-active"
        );

        selectedCategoryButton = newSelectedButton;

        selectedCategoryButton?.AddToClassList(
            "category-card-active"
        );
    }

    private void UpdateRecentSectionForRole()
    {
        if (recentSectionTitle != null)
        {
            recentSectionTitle.text = IsTeacher()
                ? "Recently Updated"
                : "Recently Viewed";
        }

        if (emptyStateActionButton != null)
        {
            emptyStateActionButton.text = IsTeacher()
                ? "Create Class"
                : "Enroll Class";
        }
    }

    private void ShowLoadingState()
    {
        allClasses.Clear();

        if (activeClassCountLabel != null)
            activeClassCountLabel.text = "Loading classes...";

        if (publicClassCountLabel != null)
            publicClassCountLabel.text = "0 Public";

        if (privateClassCountLabel != null)
            privateClassCountLabel.text = "0 Private";

        if (recentCourseList != null)
        {
            recentCourseList.Clear();

            Label loading = new("Loading recent classes...");
            loading.AddToClassList("recent-loading-label");
            recentCourseList.Add(loading);
        }

        SetVisible(recentCourseList, true);
        SetVisible(recentEmptyState, false);
    }

    private void FinishLoadWithError(string error)
    {
        isLoading = false;

        Debug.LogError(
            "Không tải được dữ liệu MainHomeScene: " +
            error
        );

        if (activeClassCountLabel != null)
            activeClassCountLabel.text = "0 Active Classes";

        if (recentCourseList != null)
        {
            recentCourseList.Clear();

            Label message = new(
                "Unable to load classes. Please try again."
            );
            message.AddToClassList("recent-empty-label");
            recentCourseList.Add(message);
        }

        SetVisible(recentCourseList, true);
        SetVisible(recentEmptyState, false);
    }

    private string ResolveCategoryName(string categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
            return "Others";

        return categoryNamesById.TryGetValue(
            categoryId,
            out string categoryName)
            ? categoryName
            : "Others";
    }

    private static string NormalizeVisibility(
        string visibility)
    {
        return string.Equals(
            visibility,
            "private",
            StringComparison.OrdinalIgnoreCase)
            ? "private"
            : "public";
    }

    private static DateTime ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTime.MinValue;

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal,
            out DateTime parsed)
            ? parsed
            : DateTime.MinValue;
    }

    private static string GetRelativeInteractionText(
        DateTime timestamp)
    {
        if (timestamp == DateTime.MinValue)
            return "No activity";

        TimeSpan difference =
            DateTime.UtcNow - timestamp.ToUniversalTime();

        if (difference.TotalMinutes < 1)
            return "Just now";

        if (difference.TotalHours < 1)
            return $"{Mathf.Max(1, (int)difference.TotalMinutes)}m ago";

        if (difference.TotalDays < 1)
            return $"{Mathf.Max(1, (int)difference.TotalHours)}h ago";

        if (difference.TotalDays < 7)
            return $"{Mathf.Max(1, (int)difference.TotalDays)}d ago";

        return timestamp.ToLocalTime().ToString("dd/MM/yyyy");
    }

    private void OpenClass(HomeClassItem item)
    {
        if (item == null ||
            string.IsNullOrWhiteSpace(item.ClassId))
        {
            return;
        }

        ClassInteractionHistory.Record(item.ClassId);

        PlayerPrefs.SetString(
            "selected_class_id",
            item.ClassId
        );

        PlayerPrefs.SetString(
            "selected_class_name",
            item.ClassName ?? string.Empty
        );

        PlayerPrefs.Save();

        SceneHistory.LoadScene("ClassDetailScene");
    }

    private void HandleEmptyStateAction()
    {
        SceneHistory.LoadScene(
            IsTeacher()
                ? "CreateClassScene"
                : "EnrollClassScene"
        );
    }

    private void OpenNotifications()
    {
        Debug.Log("Mở trang thông báo.");
    }

    private void OpenProfile()
    {
        OpenSettingScene();
    }

    private void OpenHome()
    {
        Debug.Log("Đang ở MainHomeScene.");
    }

    private void OpenMyClasses()
    {
        PlayerPrefs.SetString(
            "current_role",
            currentRole
        );
        PlayerPrefs.Save();

        SceneHistory.LoadScene("MyClassesScene");
    }

    private void OpenAI()
    {
        Debug.Log("Mở chức năng AI.");
    }

    private void OpenSettings()
    {
        OpenSettingScene();
    }

    private void OpenSettingScene()
    {
        const string sceneName = "SettingScene";

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"{sceneName} chưa được thêm vào Build Profiles / Scene List."
            );
            return;
        }

        PlayerPrefs.SetString(
            "previous_scene",
            SceneManager.GetActiveScene().name
        );

        PlayerPrefs.Save();
        SceneHistory.LoadScene(sceneName);
    }

    private bool IsTeacher()
    {
        return string.Equals(
            currentRole,
            "teacher",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static void SetVisible(
        VisualElement element,
        bool visible)
    {
        if (element == null)
            return;

        element.style.display =
            visible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
    }

    private void DisposeHeader()
    {
        if (headerController == null)
            return;

        headerController.NotificationClicked -= OpenNotifications;
        headerController.ProfileClicked -= OpenProfile;
        headerController.Dispose();
        headerController = null;
    }

    private void DisposeBottomNavigation()
    {
        if (bottomNavigationController == null)
            return;

        bottomNavigationController.HomeClicked -= OpenHome;
        bottomNavigationController.MyClassesClicked -= OpenMyClasses;
        bottomNavigationController.AIClicked -= OpenAI;
        bottomNavigationController.SettingsClicked -= OpenSettings;

        bottomNavigationController.Dispose();
        bottomNavigationController = null;
    }

    private static bool TryParseArray<T>(
        string json,
        out T[] items,
        out string error)
    {
        items = Array.Empty<T>();

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Supabase returned an empty response.";
            return false;
        }

        try
        {
            string wrapped =
                "{\"items\":" + json.Trim() + "}";

            ArrayWrapper<T> wrapper =
                JsonUtility.FromJson<ArrayWrapper<T>>(
                    wrapped
                );

            items =
                wrapper?.items ??
                Array.Empty<T>();

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error =
                "Could not parse Supabase response: " +
                exception.Message;
            return false;
        }
    }

    [Serializable]
    private class ArrayWrapper<T>
    {
        public T[] items;
    }

    [Serializable]
    private class StudentMembershipRow
    {
        public string id;
        public string class_id;
        public string user_id;
        public string status;
        public string joined_at;
    }

    [Serializable]
    private class TeacherProfileRow
    {
        public string full_name;
    }

    [Serializable]
    private class CategoryRecord
    {
        public string id;
        public string name;
    }

    [Serializable]
    private class CategoryRecordArray
    {
        public CategoryRecord[] items;
    }

    private class HomeClassItem
    {
        public string ClassId;
        public string ClassName;
        public string ClassCode;
        public string CategoryId;
        public string CategoryName;
        public string Visibility;
        public string TeacherName;
        public float ProgressPercent;
        public DateTime DatabaseInteractionTime;
        public DateTime LocalInteractionTime;

        public DateTime EffectiveInteractionTime =>
            LocalInteractionTime > DatabaseInteractionTime
                ? LocalInteractionTime
                : DatabaseInteractionTime;
    }
}
