using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class EnrollClassPageController : MonoBehaviour
{
    private VisualElement root;
    private VisualElement classListContainer;
    private VisualElement categoryRow;

    private TextField searchField;
    private GeneralHeaderController headerController;

    private ScrollView categoryScroll;
    private Button categoryScrollLeftButton;
    private Button categoryScrollRightButton;
    private VisualElement categoryScrollTrack;
    private VisualElement categoryScrollThumb;

    private bool isDraggingCategoryThumb;
    private float categoryDragPointerOffset;
    private const float CategoryScrollStep = 110f;

    private readonly List<Button> categoryButtons = new();
    private readonly List<CourseData> allCourses = new();

    // class_id -> membership row
    private readonly Dictionary<string, MembershipRecord>
        membershipByClassId = new(
            StringComparer.OrdinalIgnoreCase);

    // class_id -> visibility from public.classes
    private readonly Dictionary<string, string>
        visibilityByClassId = new(
            StringComparer.OrdinalIgnoreCase);

    private string selectedCategory = "All";
    private bool isLoading;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
        {
            Debug.LogError(
                "EnrollClassPageController: Không tìm thấy UIDocument.");
            return;
        }

        root = document.rootVisualElement;

        QueryElements();
        ConfigureHeader();
        RegisterEvents();
        LoadDiscoverClasses();
    }

    private void OnDisable()
    {
        UnregisterEvents();

        if (headerController != null)
        {
            headerController.BackClicked -= OnBackClicked;
            headerController.Dispose();
            headerController = null;
        }
    }

    private void QueryElements()
    {
        searchField = root.Q<TextField>("search-field");
        classListContainer =
            root.Q<VisualElement>("class-list-container");
        categoryRow =
            root.Q<VisualElement>("category-row");

        categoryScroll =
            root.Q<ScrollView>("category-scroll");
        categoryScrollLeftButton =
            root.Q<Button>("category-scroll-left-button");
        categoryScrollRightButton =
            root.Q<Button>("category-scroll-right-button");
        categoryScrollTrack =
            root.Q<VisualElement>("category-scroll-track");
        categoryScrollThumb =
            root.Q<VisualElement>("category-scroll-thumb");

        if (categoryScroll != null)
        {
            categoryScroll.horizontalScrollerVisibility =
                ScrollerVisibility.Hidden;
            categoryScroll.verticalScrollerVisibility =
                ScrollerVisibility.Hidden;
        }
    }

    private void ConfigureHeader()
    {
        headerController =
            new GeneralHeaderController(root);

        headerController.ConfigurePageWithIconAction(
            "Discover Classes",
            "Loading available classes...",
            "icon-header-graduation-cap",
            showBackButton: true,
            showSubtitleIcon: false
        );

        headerController.SetBottomBorderVisible(false);
        headerController.SetCustomClass(
            "enroll-class-header",
            true
        );
        headerController.BackClicked += OnBackClicked;
    }

    private void RegisterEvents()
    {
        searchField?.RegisterValueChangedCallback(
            OnSearchValueChanged);

        if (categoryScrollLeftButton != null)
        {
            categoryScrollLeftButton.clicked +=
                ScrollCategoriesLeft;
        }

        if (categoryScrollRightButton != null)
        {
            categoryScrollRightButton.clicked +=
                ScrollCategoriesRight;
        }

        categoryScrollTrack?.RegisterCallback<PointerDownEvent>(
            OnCategoryTrackPointerDown);

        if (categoryScrollThumb != null)
        {
            categoryScrollThumb.RegisterCallback<PointerDownEvent>(
                OnCategoryThumbPointerDown);

            categoryScrollThumb.RegisterCallback<PointerMoveEvent>(
                OnCategoryThumbPointerMove);

            categoryScrollThumb.RegisterCallback<PointerUpEvent>(
                OnCategoryThumbPointerUp);
        }

        if (categoryScroll != null)
        {
            categoryScroll.horizontalScroller.slider
                .RegisterValueChangedCallback(
                    OnCategoryScrollValueChanged);

            categoryScroll.RegisterCallback<GeometryChangedEvent>(
                OnCategoryGeometryChanged);

            categoryScroll.schedule.Execute(
                UpdateCategoryScrollbar).StartingIn(50);
        }
    }

    private void UnregisterEvents()
    {
        searchField?.UnregisterValueChangedCallback(
            OnSearchValueChanged);

        if (categoryScrollLeftButton != null)
        {
            categoryScrollLeftButton.clicked -=
                ScrollCategoriesLeft;
        }

        if (categoryScrollRightButton != null)
        {
            categoryScrollRightButton.clicked -=
                ScrollCategoriesRight;
        }

        categoryScrollTrack?.UnregisterCallback<PointerDownEvent>(
            OnCategoryTrackPointerDown);

        if (categoryScrollThumb != null)
        {
            categoryScrollThumb.UnregisterCallback<PointerDownEvent>(
                OnCategoryThumbPointerDown);

            categoryScrollThumb.UnregisterCallback<PointerMoveEvent>(
                OnCategoryThumbPointerMove);

            categoryScrollThumb.UnregisterCallback<PointerUpEvent>(
                OnCategoryThumbPointerUp);
        }

        if (categoryScroll != null)
        {
            categoryScroll.horizontalScroller.slider
                .UnregisterValueChangedCallback(
                    OnCategoryScrollValueChanged);

            categoryScroll.UnregisterCallback<GeometryChangedEvent>(
                OnCategoryGeometryChanged);
        }
    }

    private void LoadDiscoverClasses()
    {
        if (isLoading)
        {
            return;
        }

        if (!SupabaseSession.IsLoggedIn ||
            !Guid.TryParse(SupabaseSession.UserId, out _))
        {
            ShowState(
                "Session expired",
                "Please sign in again to discover available classes.",
                "error-state"
            );

            UpdateHeaderCount(0);
            return;
        }

        isLoading = true;

        ShowState(
            "Loading classes...",
            "Please wait while we load available classes.",
            "loading-state"
        );

        StartCoroutine(
            SupabaseRestService.Get(
                "discover_classes_view?select=*&order=created_at.desc",
                classJson =>
                {
                    if (!TryParseArray(
                            classJson,
                            out DiscoverClassRecord[] records,
                            out string classError))
                    {
                        FinishWithError(
                            "Unable to load classes",
                            classError);
                        return;
                    }

                    LoadClassVisibility(records);
                },
                error =>
                {
                    FinishWithError(
                        "Unable to load classes",
                        error);
                }
            )
        );
    }

    private void LoadClassVisibility(
        DiscoverClassRecord[] records)
    {
        StartCoroutine(
            SupabaseRestService.Get(
                "classes?select=id,visibility",
                json =>
                {
                    if (!TryParseArray(
                            json,
                            out ClassVisibilityRecord[] visibilities,
                            out string parseError))
                    {
                        FinishWithError(
                            "Unable to load visibility",
                            parseError);
                        return;
                    }

                    visibilityByClassId.Clear();

                    foreach (ClassVisibilityRecord item in visibilities)
                    {
                        if (item == null ||
                            string.IsNullOrWhiteSpace(item.id))
                        {
                            continue;
                        }

                        visibilityByClassId[item.id] =
                            NormalizeVisibility(item.visibility);
                    }

                    LoadStudentMemberships(records);
                },
                error =>
                {
                    FinishWithError(
                        "Unable to load visibility",
                        error);
                }
            )
        );
    }

    private void LoadStudentMemberships(
        DiscoverClassRecord[] records)
    {
        string userId =
            Uri.EscapeDataString(SupabaseSession.UserId);

        string query =
            "class_members" +
            $"?user_id=eq.{userId}" +
            "&member_role=eq.student" +
            "&select=id,class_id,status";

        StartCoroutine(
            SupabaseRestService.Get(
                query,
                membershipJson =>
                {
                    isLoading = false;

                    if (!TryParseArray(
                            membershipJson,
                            out MembershipRecord[] memberships,
                            out string membershipError))
                    {
                        ShowState(
                            "Unable to load enrollments",
                            membershipError,
                            "error-state"
                        );

                        UpdateHeaderCount(0);
                        return;
                    }

                    membershipByClassId.Clear();

                    foreach (MembershipRecord membership in memberships)
                    {
                        if (membership == null ||
                            string.IsNullOrWhiteSpace(
                                membership.class_id))
                        {
                            continue;
                        }

                        membership.status =
                            NormalizeMembershipStatus(
                                membership.status);

                        membershipByClassId[
                            membership.class_id] = membership;
                    }

                    int enrolledCount = memberships.Count(item =>
                        NormalizeMembershipStatus(item?.status) == "enrolled"
                    );

                    int pendingCount = memberships.Count(item =>
                        NormalizeMembershipStatus(item?.status) == "pending"
                    );

                    Debug.Log(
                        $"[EnrollClass] Memberships loaded: " +
                        $"{memberships.Length} total, " +
                        $"{enrolledCount} enrolled, " +
                        $"{pendingCount} pending."
                    );

                    BuildCourseData(records);
                    BuildCategoryButtons();
                    RefreshCourseList();
                },
                error =>
                {
                    FinishWithError(
                        "Unable to load enrollments",
                        error);
                }
            )
        );
    }

    private void FinishWithError(
        string title,
        string technicalError)
    {
        isLoading = false;

        Debug.LogError(
            $"{title}: {technicalError}");

        ShowState(
            title,
            "Please check your connection and Supabase policies.",
            "error-state"
        );

        UpdateHeaderCount(0);
    }

    private void BuildCourseData(
        DiscoverClassRecord[] records)
    {
        allCourses.Clear();

        if (records == null)
        {
            return;
        }

        foreach (DiscoverClassRecord record in records)
        {
            if (record == null ||
                string.IsNullOrWhiteSpace(record.id))
            {
                continue;
            }

            string category =
                string.IsNullOrWhiteSpace(record.category_name)
                    ? "Others"
                    : record.category_name.Trim();

            string visibility =
                visibilityByClassId.TryGetValue(
                    record.id,
                    out string savedVisibility)
                    ? savedVisibility
                    : NormalizeVisibility(record.visibility);

            string membershipStatus = string.Empty;
            string membershipId = string.Empty;

            if (membershipByClassId.TryGetValue(
                    record.id,
                    out MembershipRecord membership))
            {
                membershipStatus =
                    NormalizeMembershipStatus(
                        membership.status);

                membershipId =
                    membership.id ?? string.Empty;
            }

            allCourses.Add(
                new CourseData
                {
                    ClassId = record.id,
                    Code =
                        string.IsNullOrWhiteSpace(record.class_code)
                            ? "CLASS"
                            : record.class_code.Trim(),
                    ClassName =
                        string.IsNullOrWhiteSpace(record.class_name)
                            ? "Untitled Class"
                            : record.class_name.Trim(),
                    TeacherName =
                        string.IsNullOrWhiteSpace(record.teacher_name)
                            ? "Unknown Teacher"
                            : record.teacher_name.Trim(),
                    EnrolledCount =
                        Mathf.Max(0, record.enrolled_count),
                    Category = category,
                    Visibility = visibility,
                    MembershipId = membershipId,
                    EnrollmentStatus = membershipStatus,
                    IconName = GetIconName(category),
                    ThemeClass = GetThemeClass(
                        record.cover_template,
                        record.id)
                }
            );
        }
    }

    private void BuildCategoryButtons()
    {
        if (categoryRow == null)
        {
            return;
        }

        categoryRow.Clear();
        categoryButtons.Clear();
        selectedCategory = "All";

        CreateCategoryButton("All", true);

        IEnumerable<string> categories =
            allCourses
                .Select(course => course.Category)
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value);

        foreach (string category in categories)
        {
            CreateCategoryButton(category, false);
        }

        categoryScroll?.schedule.Execute(
            UpdateCategoryScrollbar).StartingIn(50);
    }

    private void CreateCategoryButton(
        string category,
        bool active)
    {
        Button button = new Button
        {
            text = category,
            userData = category
        };

        button.AddToClassList("category-button");

        if (active)
        {
            button.AddToClassList(
                "category-button-active");
        }

        button.clicked += () =>
            OnCategoryClicked(button);

        categoryButtons.Add(button);
        categoryRow.Add(button);
    }

    private void RefreshCourseList()
    {
        if (classListContainer == null)
        {
            return;
        }

        classListContainer.Clear();

        if (allCourses.Count == 0)
        {
            ShowState(
                "No classes available yet",
                "There are currently no classes in the system. Please check again later.",
                "database-empty-state"
            );

            UpdateHeaderCount(0);
            return;
        }

        string searchText =
            searchField?.value?.Trim().ToLowerInvariant()
            ?? string.Empty;

        List<CourseData> filteredCourses =
            allCourses
                .Where(course =>
                    selectedCategory == "All" ||
                    string.Equals(
                        course.Category,
                        selectedCategory,
                        StringComparison.OrdinalIgnoreCase))
                .Where(course =>
                    string.IsNullOrEmpty(searchText) ||
                    course.Code.ToLowerInvariant()
                        .Contains(searchText) ||
                    course.ClassName.ToLowerInvariant()
                        .Contains(searchText) ||
                    course.TeacherName.ToLowerInvariant()
                        .Contains(searchText) ||
                    course.Category.ToLowerInvariant()
                        .Contains(searchText) ||
                    course.Visibility.ToLowerInvariant()
                        .Contains(searchText))
                .ToList();

        UpdateHeaderCount(filteredCourses.Count);

        if (filteredCourses.Count == 0)
        {
            ShowState(
                "No classes found",
                "Try another keyword or category.",
                "database-empty-state"
            );

            return;
        }

        foreach (CourseData course in filteredCourses)
        {
            classListContainer.Add(
                CreateCourseCard(course));
        }
    }

    private VisualElement CreateCourseCard(
        CourseData course)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("class-card");
        card.AddToClassList(course.ThemeClass);

        VisualElement topLine = new VisualElement();
        topLine.AddToClassList("class-card-top-line");
        card.Add(topLine);

        VisualElement content = new VisualElement();
        content.AddToClassList("class-card-content");
        card.Add(content);

        VisualElement mainRow = new VisualElement();
        mainRow.AddToClassList("class-main-row");
        content.Add(mainRow);

        VisualElement iconWrapper = new VisualElement();
        iconWrapper.AddToClassList("class-icon-wrapper");
        mainRow.Add(iconWrapper);

        VisualElement courseIcon = new VisualElement();
        courseIcon.AddToClassList("class-icon");
        courseIcon.AddToClassList(
            $"course-icon-{course.IconName}");
        iconWrapper.Add(courseIcon);

        VisualElement classInfo = new VisualElement();
        classInfo.AddToClassList("class-info");
        mainRow.Add(classInfo);

        VisualElement codeRow = new VisualElement();
        codeRow.AddToClassList("class-code-row");
        classInfo.Add(codeRow);

        Label codeBadge = new Label(course.Code);
        codeBadge.AddToClassList("class-code-badge");
        codeRow.Add(codeBadge);

        Label visibilityBadge =
            new Label(ToTitleCase(course.Visibility));

        visibilityBadge.AddToClassList(
            "visibility-badge");

        visibilityBadge.AddToClassList(
            string.Equals(
                course.Visibility,
                "private",
                StringComparison.OrdinalIgnoreCase
            )
                ? "visibility-private"
                : "visibility-public");

        codeRow.Add(visibilityBadge);

        Label classNameLabel =
            new Label(course.ClassName);
        classNameLabel.AddToClassList("class-name");
        classInfo.Add(classNameLabel);

        VisualElement teacherRow = new VisualElement();
        teacherRow.AddToClassList("teacher-row");
        classInfo.Add(teacherRow);

        VisualElement teacherIcon = new VisualElement();
        teacherIcon.AddToClassList("teacher-icon");
        teacherRow.Add(teacherIcon);

        Label teacherLabel =
            new Label(course.TeacherName);
        teacherLabel.AddToClassList("teacher-name");
        teacherRow.Add(teacherLabel);

        VisualElement divider = new VisualElement();
        divider.AddToClassList("card-divider");
        content.Add(divider);

        VisualElement bottomRow = new VisualElement();
        bottomRow.AddToClassList("card-bottom-row");
        content.Add(bottomRow);

        VisualElement metaRow = new VisualElement();
        metaRow.AddToClassList("card-meta-row");
        bottomRow.Add(metaRow);

        metaRow.Add(
            CreateMetaItem(
                "students-meta-icon",
                $"{course.EnrolledCount} enrolled"));

        metaRow.Add(
            CreateMetaItem(
                "category-meta-icon",
                course.Category));

        VisualElement spacer = new VisualElement();
        spacer.AddToClassList("card-bottom-spacer");
        bottomRow.Add(spacer);

        Button enrollButton = new Button();
        enrollButton.AddToClassList("enroll-button");

        ApplyEnrollButtonState(
            enrollButton,
            course.EnrollmentStatus,
            false);

        enrollButton.clicked += () =>
            OnEnrollClicked(course, enrollButton);

        bottomRow.Add(enrollButton);
        return card;
    }

    private static VisualElement CreateMetaItem(
        string iconClass,
        string text)
    {
        VisualElement metaItem = new VisualElement();
        metaItem.AddToClassList("meta-item");

        VisualElement icon = new VisualElement();
        icon.AddToClassList("meta-icon");
        icon.AddToClassList(iconClass);
        metaItem.Add(icon);

        Label label = new Label(text);
        label.AddToClassList("meta-label");
        metaItem.Add(label);

        return metaItem;
    }

    private void OnEnrollClicked(
        CourseData course,
        Button enrollButton)
    {
        if (course == null ||
            enrollButton == null ||
            !enrollButton.enabledSelf)
        {
            return;
        }

        if (!Guid.TryParse(course.ClassId, out _) ||
            !Guid.TryParse(SupabaseSession.UserId, out _))
        {
            Debug.LogError(
                "class_id hoặc user_id không đúng UUID.");
            return;
        }

        string targetStatus =
            string.Equals(
                course.Visibility,
                "private",
                StringComparison.OrdinalIgnoreCase
            )
                ? "pending"
                : "enrolled";

        ApplyEnrollButtonState(
            enrollButton,
            course.EnrollmentStatus,
            true);

        // Rejected membership already exists because of the
        // unique (class_id, user_id) constraint, so update it.
        if (!string.IsNullOrWhiteSpace(course.MembershipId))
        {
            UpdateExistingMembership(
                course,
                enrollButton,
                targetStatus);
            return;
        }

        InsertNewMembership(
            course,
            enrollButton,
            targetStatus);
    }

    private void InsertNewMembership(
        CourseData course,
        Button enrollButton,
        string targetStatus)
    {
        ClassMemberInsert payload =
            new ClassMemberInsert
            {
                class_id = course.ClassId,
                user_id = SupabaseSession.UserId,
                member_role = "student",
                status = targetStatus
            };

        StartCoroutine(
            SupabaseRestService.Post(
                "class_members?select=*",
                JsonUtility.ToJson(payload),
                responseJson =>
                {
                    if (!TryParseArray(
                            responseJson,
                            out MembershipRecord[] inserted,
                            out string parseError) ||
                        inserted.Length == 0)
                    {
                        Debug.LogWarning(
                            "Membership inserted but response could not be parsed: " +
                            parseError);
                    }
                    else
                    {
                        course.MembershipId =
                            inserted[0].id ?? string.Empty;
                    }

                    CompleteEnrollmentChange(
                        course,
                        enrollButton,
                        targetStatus);
                },
                error =>
                {
                    HandleEnrollmentError(
                        course,
                        enrollButton,
                        error);
                },
                true
            )
        );
    }

    private void UpdateExistingMembership(
        CourseData course,
        Button enrollButton,
        string targetStatus)
    {
        ClassMemberStatusUpdate payload =
            new ClassMemberStatusUpdate
            {
                status = targetStatus
            };

        string membershipId =
            Uri.EscapeDataString(course.MembershipId);

        StartCoroutine(
            SupabaseRestService.Patch(
                "class_members" +
                $"?id=eq.{membershipId}" +
                "&select=*",
                JsonUtility.ToJson(payload),
                _ =>
                {
                    CompleteEnrollmentChange(
                        course,
                        enrollButton,
                        targetStatus);
                },
                error =>
                {
                    HandleEnrollmentError(
                        course,
                        enrollButton,
                        error);
                },
                true
            )
        );
    }

    private void CompleteEnrollmentChange(
        CourseData course,
        Button enrollButton,
        string targetStatus)
    {
        string previousStatus =
            course.EnrollmentStatus;

        course.EnrollmentStatus = targetStatus;

        if (targetStatus == "enrolled" &&
            previousStatus != "enrolled")
        {
            course.EnrolledCount++;
        }

        ApplyEnrollButtonState(
            enrollButton,
            course.EnrollmentStatus,
            false);

        Debug.Log(
            targetStatus == "enrolled"
                ? $"Enrolled successfully: {course.Code}"
                : $"Enrollment request pending: {course.Code}"
        );
    }

    private void HandleEnrollmentError(
        CourseData course,
        Button enrollButton,
        string error)
    {
        Debug.LogError(
            "Enroll class failed: " + error);

        ApplyEnrollButtonState(
            enrollButton,
            course.EnrollmentStatus,
            false);
    }

    private static void ApplyEnrollButtonState(
        Button button,
        string status,
        bool isLoading)
    {
        button.RemoveFromClassList("enrolled-button");
        button.RemoveFromClassList("pending-button");
        button.RemoveFromClassList("enrolling-button");

        if (isLoading)
        {
            button.text = "Processing...";
            button.AddToClassList("enrolling-button");
            button.SetEnabled(false);
            return;
        }

        string normalized =
            NormalizeMembershipStatus(status);

        if (normalized == "enrolled")
        {
            button.text = "Enrolled";
            button.AddToClassList("enrolled-button");
            button.SetEnabled(false);
            return;
        }

        if (normalized == "pending")
        {
            button.text = "Pending";
            button.AddToClassList("pending-button");
            button.SetEnabled(false);
            return;
        }

        // rejected or no membership
        button.text = "Enroll";
        button.SetEnabled(true);
    }

    private void ShowState(
        string title,
        string description,
        string stateClass)
    {
        classListContainer?.Clear();

        VisualElement state = new VisualElement();
        state.AddToClassList(stateClass);

        VisualElement icon = new VisualElement();
        icon.AddToClassList(
            stateClass.Replace("-state", "-icon"));
        state.Add(icon);

        Label titleLabel = new Label(title);
        titleLabel.AddToClassList(
            stateClass.Replace("-state", "-title"));
        state.Add(titleLabel);

        Label descriptionLabel =
            new Label(description);
        descriptionLabel.AddToClassList(
            stateClass.Replace("-state", "-description"));
        state.Add(descriptionLabel);

        classListContainer?.Add(state);
    }

    private void UpdateHeaderCount(int count)
    {
        string subtitle = count == 1
            ? "1 course available this semester"
            : $"{count} courses available this semester";

        headerController?.ConfigurePageWithIconAction(
            "Discover Classes",
            subtitle,
            "icon-header-graduation-cap",
            showBackButton: true,
            showSubtitleIcon: false
        );

        headerController?.SetBottomBorderVisible(false);
        headerController?.SetCustomClass(
            "enroll-class-header",
            true
        );
    }

    private void OnBackClicked()
    {
        PlayerPrefs.SetString(
            "current_role",
            "student"
        );
        PlayerPrefs.Save();

        SceneHistory.GoBack("MyClassesScene");
    }

    private void OnSearchValueChanged(
        ChangeEvent<string> changeEvent)
    {
        RefreshCourseList();
    }

    private void OnCategoryClicked(
        Button selectedButton)
    {
        selectedCategory =
            selectedButton.userData?.ToString() ?? "All";

        foreach (Button button in categoryButtons)
        {
            button.RemoveFromClassList(
                "category-button-active");
        }

        selectedButton.AddToClassList(
            "category-button-active");

        RefreshCourseList();
    }

    private static string NormalizeVisibility(
        string visibility)
    {
        return string.Equals(
                visibility?.Trim(),
                "private",
                StringComparison.OrdinalIgnoreCase)
            ? "private"
            : "public";
    }

    private static string NormalizeMembershipStatus(
        string status)
    {
        string value =
            status?.Trim().ToLowerInvariant()
            ?? string.Empty;

        return value switch
        {
            // Legacy value from the old database flow.
            // Treat it as enrolled so old rows still render correctly.
            "active" => "enrolled",
            "enrolled" => "enrolled",
            "pending" => "pending",
            "rejected" => "rejected",
            _ => string.Empty
        };
    }

    private static string ToTitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = value.Trim().ToLowerInvariant();

        return char.ToUpperInvariant(value[0]) +
               value.Substring(1);
    }

    private static string GetIconName(
        string category)
    {
        string value =
            category?.ToLowerInvariant() ?? string.Empty;

        if (value.Contains("computer") ||
            value.Contains("informatics"))
        {
            return "ai-sparkle";
        }

        if (value.Contains("chem"))
        {
            return "chemistry";
        }

        if (value.Contains("bio"))
        {
            return "student";
        }

        if (value.Contains("physics") ||
            value.Contains("electric"))
        {
            return "cube";
        }

        return "document";
    }

    private static string GetThemeClass(
        string coverTemplate,
        string classId)
    {
        string value =
            coverTemplate?.ToLowerInvariant() ?? string.Empty;

        if (value.Contains("purple") ||
            value.Contains("pink"))
        {
            return "theme-purple";
        }

        if (value.Contains("green"))
        {
            return "theme-green";
        }

        if (value.Contains("orange") ||
            value.Contains("sand"))
        {
            return "theme-orange";
        }

        if (value.Contains("blue"))
        {
            return "theme-blue";
        }

        string[] themes =
        {
            "theme-blue",
            "theme-purple",
            "theme-green",
            "theme-orange"
        };

        int hash = 17;

        foreach (char character in classId ?? string.Empty)
        {
            hash =
                (hash * 31 + character) & 0x7fffffff;
        }

        return themes[hash % themes.Length];
    }

    private void ScrollCategoriesLeft()
    {
        SetCategoryScrollValue(
            GetCategoryScrollValue() -
            CategoryScrollStep);
    }

    private void ScrollCategoriesRight()
    {
        SetCategoryScrollValue(
            GetCategoryScrollValue() +
            CategoryScrollStep);
    }

    private float GetCategoryScrollValue()
    {
        return categoryScroll == null
            ? 0f
            : categoryScroll.horizontalScroller.value;
    }

    private void SetCategoryScrollValue(float value)
    {
        if (categoryScroll == null)
        {
            return;
        }

        Scroller scroller =
            categoryScroll.horizontalScroller;

        scroller.value = Mathf.Clamp(
            value,
            scroller.lowValue,
            scroller.highValue);

        UpdateCategoryScrollbar();
    }

    private void OnCategoryScrollValueChanged(
        ChangeEvent<float> changeEvent)
    {
        UpdateCategoryScrollbar();
    }

    private void OnCategoryGeometryChanged(
        GeometryChangedEvent geometryEvent)
    {
        UpdateCategoryScrollbar();
    }

    private void UpdateCategoryScrollbar()
    {
        if (categoryScroll == null ||
            categoryScrollTrack == null ||
            categoryScrollThumb == null)
        {
            return;
        }

        Scroller scroller =
            categoryScroll.horizontalScroller;

        float trackWidth =
            categoryScrollTrack.contentRect.width -
            categoryScrollTrack.resolvedStyle.paddingLeft -
            categoryScrollTrack.resolvedStyle.paddingRight;

        float viewportWidth =
            categoryScroll.contentViewport.contentRect.width;

        float contentWidth =
            categoryScroll.contentContainer.layout.width;

        if (trackWidth <= 0f ||
            viewportWidth <= 0f ||
            contentWidth <= 0f)
        {
            return;
        }

        float visibleRatio =
            Mathf.Clamp01(viewportWidth / contentWidth);

        float thumbWidth = Mathf.Clamp(
            trackWidth * visibleRatio,
            44f,
            trackWidth);

        categoryScrollThumb.style.width = thumbWidth;

        float scrollRange =
            Mathf.Max(
                0f,
                scroller.highValue - scroller.lowValue);

        float normalizedValue =
            scrollRange <= 0.01f
                ? 0f
                : Mathf.InverseLerp(
                    scroller.lowValue,
                    scroller.highValue,
                    scroller.value);

        float travelWidth =
            Mathf.Max(0f, trackWidth - thumbWidth);

        categoryScrollThumb.style.marginLeft =
            travelWidth * normalizedValue;

        bool canMoveLeft =
            scrollRange > 0.01f &&
            scroller.value > scroller.lowValue + 0.5f;

        bool canMoveRight =
            scrollRange > 0.01f &&
            scroller.value < scroller.highValue - 0.5f;

        categoryScrollLeftButton?.EnableInClassList(
            "category-arrow-unavailable",
            !canMoveLeft);

        categoryScrollRightButton?.EnableInClassList(
            "category-arrow-unavailable",
            !canMoveRight);
    }

    private void OnCategoryTrackPointerDown(
        PointerDownEvent pointerEvent)
    {
        if (categoryScrollTrack == null ||
            categoryScrollThumb == null)
        {
            return;
        }

        float thumbWidth =
            categoryScrollThumb.resolvedStyle.width;

        float localX =
            categoryScrollTrack.WorldToLocal(
                pointerEvent.position).x;

        SetCategoryScrollFromThumbPosition(
            localX - thumbWidth * 0.5f);

        pointerEvent.StopPropagation();
    }

    private void OnCategoryThumbPointerDown(
        PointerDownEvent pointerEvent)
    {
        if (categoryScrollThumb == null)
        {
            return;
        }

        isDraggingCategoryThumb = true;

        categoryDragPointerOffset =
            categoryScrollThumb.WorldToLocal(
                pointerEvent.position).x;

        categoryScrollThumb.CapturePointer(
            pointerEvent.pointerId);

        pointerEvent.StopPropagation();
    }

    private void OnCategoryThumbPointerMove(
        PointerMoveEvent pointerEvent)
    {
        if (!isDraggingCategoryThumb ||
            categoryScrollTrack == null)
        {
            return;
        }

        float localX =
            categoryScrollTrack.WorldToLocal(
                pointerEvent.position).x;

        SetCategoryScrollFromThumbPosition(
            localX - categoryDragPointerOffset);

        pointerEvent.StopPropagation();
    }

    private void OnCategoryThumbPointerUp(
        PointerUpEvent pointerEvent)
    {
        if (!isDraggingCategoryThumb ||
            categoryScrollThumb == null)
        {
            return;
        }

        isDraggingCategoryThumb = false;

        if (categoryScrollThumb.HasPointerCapture(
                pointerEvent.pointerId))
        {
            categoryScrollThumb.ReleasePointer(
                pointerEvent.pointerId);
        }

        pointerEvent.StopPropagation();
    }

    private void SetCategoryScrollFromThumbPosition(
        float requestedThumbLeft)
    {
        if (categoryScroll == null ||
            categoryScrollTrack == null ||
            categoryScrollThumb == null)
        {
            return;
        }

        float trackWidth =
            categoryScrollTrack.contentRect.width -
            categoryScrollTrack.resolvedStyle.paddingLeft -
            categoryScrollTrack.resolvedStyle.paddingRight;

        float thumbWidth =
            categoryScrollThumb.resolvedStyle.width;

        float travelWidth =
            Mathf.Max(0f, trackWidth - thumbWidth);

        if (travelWidth <= 0.01f)
        {
            SetCategoryScrollValue(0f);
            return;
        }

        float normalizedValue =
            Mathf.Clamp01(
                requestedThumbLeft / travelWidth);

        Scroller scroller =
            categoryScroll.horizontalScroller;

        SetCategoryScrollValue(
            Mathf.Lerp(
                scroller.lowValue,
                scroller.highValue,
                normalizedValue));
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
                    wrapped);

            items =
                wrapper?.items ??
                Array.Empty<T>();

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error =
                "Could not parse Supabase data: " +
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
    private class DiscoverClassRecord
    {
        public string id;
        public string class_name;
        public string class_code;
        public string teacher_name;
        public string category_name;
        public string cover_template;
        public string created_at;
        public string visibility;
        public int enrolled_count;
    }

    [Serializable]
    private class ClassVisibilityRecord
    {
        public string id;
        public string visibility;
    }

    [Serializable]
    private class MembershipRecord
    {
        public string id;
        public string class_id;
        public string status;
    }

    [Serializable]
    private class ClassMemberInsert
    {
        public string class_id;
        public string user_id;
        public string member_role;
        public string status;
    }

    [Serializable]
    private class ClassMemberStatusUpdate
    {
        public string status;
    }

    [Serializable]
    private class CourseData
    {
        public string ClassId;
        public string Code;
        public string ClassName;
        public string TeacherName;
        public int EnrolledCount;
        public string Category;
        public string Visibility;
        public string MembershipId;
        public string EnrollmentStatus;
        public string IconName;
        public string ThemeClass;
    }
}
