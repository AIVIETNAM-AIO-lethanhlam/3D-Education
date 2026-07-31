using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class MyClassesPageController : MonoBehaviour
{
    private VisualElement root;
    private VisualElement teacherContent;
    private VisualElement studentContent;
    private VisualElement studentStateContainer;
    private VisualElement studentClassesContainer;
    private VisualElement studentQuizzesSection;
    private VisualElement studentQuizzesContainer;
    private ScrollView myClassesScrollView;

    private GeneralHeaderController headerController;
    private BottomNavigationController bottomNavigationController;

    private string currentRole;
    private int loadedClassCount;

    private readonly string[] cardThemes =
    {
        "blue", "pink", "green", "sand"
    };

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
        {
            Debug.LogError("MyClassesScene không tìm thấy UIDocument.");
            return;
        }

        root = document.rootVisualElement;

        if (root == null)
        {
            Debug.LogError("rootVisualElement của MyClassesScene đang null.");
            return;
        }

        QueryElements();
        InitializeRole();
        InitializeHeader();
        InitializeBottomNavigation();
        UpdateRoleLayout();

        if (IsTeacher())
        {
            LoadTeacherClasses();
        }
        else
        {
            LoadStudentClasses();
        }
    }

    private void OnDisable()
    {
        DisposeHeader();
        DisposeBottomNavigation();
    }

    private void QueryElements()
    {
        teacherContent = root.Q<VisualElement>("teacher-content");
        studentContent = root.Q<VisualElement>("student-content");
        studentStateContainer =
            root.Q<VisualElement>("student-state-container");
        studentClassesContainer =
            root.Q<VisualElement>("student-classes-container");
        studentQuizzesSection =
            root.Q<VisualElement>("student-quizzes-section");
        studentQuizzesContainer =
            root.Q<VisualElement>("student-quizzes-container");
        myClassesScrollView = root.Q<ScrollView>("my-classes-scroll-view");

        if (myClassesScrollView != null)
        {
            myClassesScrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            myClassesScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        }
    }

    private void InitializeRole()
    {
        currentRole =
            string.IsNullOrWhiteSpace(SupabaseSession.Role)
                ? PlayerPrefs.GetString("current_role", "student")
                    .Trim()
                    .ToLowerInvariant()
                : SupabaseSession.Role.Trim().ToLowerInvariant();
    }

    private void InitializeHeader()
    {
        headerController = new GeneralHeaderController(root);

        headerController.ConfigurePageWithTextAction(
            title: "My Classes",
            subtitle: IsTeacher() ? "Loading classes..." : "0 active courses",
            actionText: IsTeacher() ? "Create Class" : "Enroll New Class",
            actionPrefix: "+",
            actionStyleClass: IsTeacher()
                ? "header-action-create-class"
                : "header-action-enroll-class",
            showBackButton: true);

        headerController.SetCustomClass("my-classes-header");
        headerController.SetCompact(false);
        headerController.SetBottomBorderVisible(true);
        headerController.RightActionClicked += HandleHeaderActionClicked;
    }

    private void InitializeBottomNavigation()
    {
        bottomNavigationController = new BottomNavigationController(
            root,
            BottomNavigationTab.MyClasses);
    }

    private void UpdateRoleLayout()
    {
        SetVisible(teacherContent, IsTeacher());
        SetVisible(studentContent, !IsTeacher());
    }

    private void LoadTeacherClasses()
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            ShowTeacherMessage(
                "Không tìm thấy phiên đăng nhập. Hãy đăng nhập lại."
            );

            UpdateHeaderCount(0);
            return;
        }

        ShowTeacherMessage("Loading classes...");

        StartCoroutine(
            SupabaseClassService.GetTeacherClasses(
                records =>
                {
                    BuildTeacherClassCards(records);
                    UpdateHeaderCount(records?.Length ?? 0);
                },
                error =>
                {
                    Debug.LogError(
                        "Không tải được danh sách lớp: " +
                        error
                    );

                    ShowTeacherMessage(
                        "Unable to load your classes."
                    );

                    UpdateHeaderCount(0);
                }
            )
        );
    }

    private void BuildTeacherClassCards(SupabaseClass[] records)
    {
        if (teacherContent == null)
        {
            return;
        }

        teacherContent.Clear();
        loadedClassCount = records?.Length ?? 0;

        if (loadedClassCount == 0)
        {
            ShowTeacherMessage("You have not created any classes yet.");
            return;
        }

        foreach (SupabaseClass classRecord in records)
        {
            teacherContent.Add(CreateTeacherCard(classRecord));
        }
    }

    private VisualElement CreateTeacherCard(SupabaseClass data)
    {
        string theme = GetStableTheme(data.id);

        VisualElement card = new VisualElement();
        card.AddToClassList("teacher-card");
        card.AddToClassList($"teacher-card-{theme}");

        VisualElement topLine = new VisualElement();
        topLine.AddToClassList("teacher-card-top-line");
        topLine.AddToClassList($"card-line-{theme}");
        card.Add(topLine);

        VisualElement header = new VisualElement();
        header.AddToClassList("teacher-card-header");

        VisualElement iconShell = new VisualElement();
        iconShell.AddToClassList("course-icon-shell");
        iconShell.AddToClassList($"course-icon-{theme}");

        VisualElement courseIcon = new VisualElement();
        courseIcon.AddToClassList("course-icon");
        courseIcon.AddToClassList("icon-course-dynamic");
        courseIcon.AddToClassList($"icon-course-dynamic-{theme}");
        iconShell.Add(courseIcon);
        header.Add(iconShell);

        VisualElement info = new VisualElement();
        info.AddToClassList("teacher-course-info");

        VisualElement metaRow = new VisualElement();
        metaRow.AddToClassList("course-meta-row");

        Label code = new Label(data.class_code ?? string.Empty);
        code.AddToClassList("course-code");
        code.AddToClassList($"course-code-{theme}");
        metaRow.Add(code);

        bool isPublic = string.Equals(
            data.visibility,
            "public",
            StringComparison.OrdinalIgnoreCase);

        VisualElement visibilityDot = new VisualElement();
        visibilityDot.AddToClassList("status-dot");
        visibilityDot.AddToClassList(
            isPublic ? "status-dot-public" : "status-dot-private");
        metaRow.Add(visibilityDot);

        Label visibilityLabel = new Label(
            isPublic ? "Public" : "Private");
        visibilityLabel.AddToClassList("status-label");
        visibilityLabel.AddToClassList(
            isPublic ? "status-public" : "status-private");
        metaRow.Add(visibilityLabel);

        info.Add(metaRow);

        Label title = new Label(data.class_name ?? "Untitled Class");
        title.AddToClassList("course-title");
        info.Add(title);
        header.Add(info);

        VisualElement actions = new VisualElement();
        actions.AddToClassList("teacher-header-actions");

        Button editButton = new Button(() => EditClass(data));
        editButton.AddToClassList("round-edit-button");
        VisualElement editIcon = new VisualElement();
        editIcon.AddToClassList("round-edit-symbol");
        editButton.Add(editIcon);
        actions.Add(editButton);

        Button deleteButton = new Button(() => DeleteClass(data));
        deleteButton.AddToClassList("delete-button");
        VisualElement deleteIcon = new VisualElement();
        deleteIcon.AddToClassList("delete-icon");
        deleteButton.Add(deleteIcon);
        Label deleteLabel = new Label("Delete");
        deleteLabel.AddToClassList("delete-label");
        deleteButton.Add(deleteLabel);
        actions.Add(deleteButton);

        header.Add(actions);
        card.Add(header);

        VisualElement stats = new VisualElement();
        stats.AddToClassList("teacher-stats-row");
        stats.Add(CreateStatBox("icon-stat-students", "0", "Students"));
        stats.Add(CreateStatBox("icon-stat-modules", "0", "Active Modules"));
        stats.Add(CreateStatBox("icon-stat-score", "0%", "Avg Score"));
        card.Add(stats);

        VisualElement divider = new VisualElement();
        divider.AddToClassList("card-divider");
        card.Add(divider);

        Button manageButton = new Button(() => OpenTeacherClass(data));
        manageButton.AddToClassList("manage-button");
        manageButton.AddToClassList($"manage-button-{theme}");
        VisualElement manageIcon = new VisualElement();
        manageIcon.AddToClassList("manage-icon");
        manageIcon.AddToClassList("manage-image-icon");
        manageButton.Add(manageIcon);
        Label manageLabel = new Label("Manage");
        manageLabel.AddToClassList("manage-label");
        manageButton.Add(manageLabel);
        card.Add(manageButton);

        return card;
    }

    private static VisualElement CreateStatBox(
        string iconClass,
        string value,
        string caption)
    {
        VisualElement box = new VisualElement();
        box.AddToClassList("stat-box");

        VisualElement icon = new VisualElement();
        icon.AddToClassList("stat-image-icon");
        icon.AddToClassList(iconClass);
        box.Add(icon);

        Label valueLabel = new Label(value);
        valueLabel.AddToClassList("stat-value");
        box.Add(valueLabel);

        Label captionLabel = new Label(caption);
        captionLabel.AddToClassList("stat-caption");
        box.Add(captionLabel);

        return box;
    }

    private string GetStableTheme(string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
        {
            return cardThemes[UnityEngine.Random.Range(0, cardThemes.Length)];
        }

        int hash = 17;

        foreach (char character in classId)
        {
            hash = (hash * 31 + character) & 0x7fffffff;
        }

        return cardThemes[hash % cardThemes.Length];
    }

    private static string GetCourseInitials(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return "CL";
        }

        string[] words = className.Trim().Split(' ');
        string first = words[0].Substring(0, 1).ToUpperInvariant();
        string second = words.Length > 1
            ? words[1].Substring(0, 1).ToUpperInvariant()
            : string.Empty;

        return first + second;
    }

    private static string ToTitleCase(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        value = value.Trim();
        return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
    }

    private void ShowTeacherMessage(string message)
    {
        if (teacherContent == null)
        {
            return;
        }

        teacherContent.Clear();
        Label label = new Label(message);
        label.AddToClassList("classes-state-message");
        teacherContent.Add(label);
    }

    private void UpdateHeaderCount(int count)
    {
        loadedClassCount = count;
        string subtitle = count == 1
            ? "1 class managed"
            : $"{count} classes managed";

        Label subtitleLabel =
            root?.Q<Label>("header-subtitle-label") ??
            root?.Q<Label>("page-subtitle-label") ??
            root?.Q<Label>(className: "header-subtitle");

        if (subtitleLabel != null)
        {
            subtitleLabel.text = subtitle;
        }
    }

    private void LoadStudentClasses()
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            ShowStudentEmptyState();
            Debug.LogError(
                "Không tìm thấy phiên đăng nhập của student."
            );
            return;
        }

        SetVisible(studentStateContainer, false);
        SetVisible(studentClassesContainer, true);
        SetVisible(studentQuizzesSection, false);

        if (studentClassesContainer != null)
        {
            studentClassesContainer.Clear();

            Label loadingLabel =
                new Label("Loading your classes...");
            loadingLabel.AddToClassList(
                "classes-state-message");
            studentClassesContainer.Add(loadingLabel);
        }

        StartCoroutine(
            SupabaseClassService.GetStudentEnrolledClasses(
                records =>
                {
                    BuildStudentClassCards(records);
                    LoadStudentActiveQuizzes();
                },
                error =>
                {
                    Debug.LogError(
                        "Không tải được lớp của student: " +
                        error
                    );

                    ShowStudentEmptyState();
                }
            )
        );
    }

    private void BuildStudentClassCards(
        StudentEnrolledClass[] records)
    {
        if (studentClassesContainer == null)
        {
            return;
        }

        studentClassesContainer.Clear();

        int count = records?.Length ?? 0;

        if (count == 0)
        {
            ShowStudentEmptyState();
            return;
        }

        SetVisible(studentStateContainer, false);
        SetVisible(studentClassesContainer, true);

        // Student đã có ít nhất một lớp:
        // luôn hiển thị tiêu đề Active Quizzes.
        SetVisible(studentQuizzesSection, true);

        if (studentQuizzesContainer != null)
        {
            studentQuizzesContainer.Clear();
        }

        UpdateStudentHeaderCount(count);

        foreach (StudentEnrolledClass record in records)
        {
            if (record == null)
            {
                continue;
            }

            studentClassesContainer.Add(
                CreateStudentCard(record)
            );
        }
    }

    private VisualElement CreateStudentCard(
        StudentEnrolledClass data)
    {
        string theme = GetStableTheme(data.class_id);

        VisualElement card = new VisualElement();
        card.AddToClassList("student-card");
        card.AddToClassList($"teacher-card-{theme}");

        VisualElement topLine = new VisualElement();
        topLine.AddToClassList("student-card-top-line");
        topLine.AddToClassList($"card-line-{theme}");
        card.Add(topLine);

        VisualElement header = new VisualElement();
        header.AddToClassList("student-card-header");

        VisualElement iconShell = new VisualElement();
        iconShell.AddToClassList("course-icon-shell");
        iconShell.AddToClassList($"course-icon-{theme}");

        VisualElement courseIcon = new VisualElement();
        courseIcon.AddToClassList("course-icon");
        courseIcon.AddToClassList("icon-course-dynamic");
        courseIcon.AddToClassList(
            $"icon-course-dynamic-{theme}");
        iconShell.Add(courseIcon);
        header.Add(iconShell);

        VisualElement info = new VisualElement();
        info.AddToClassList("student-course-info");

        VisualElement metaRow = new VisualElement();
        metaRow.AddToClassList("course-meta-row");

        Label code = new Label(
            data.class_code ?? string.Empty);
        code.AddToClassList("course-code");
        code.AddToClassList($"course-code-{theme}");
        metaRow.Add(code);

        VisualElement activeDot = new VisualElement();
        activeDot.AddToClassList("status-dot");
        activeDot.AddToClassList("status-dot-public");
        metaRow.Add(activeDot);

        Label activeLabel = new Label("Active");
        activeLabel.AddToClassList("status-label");
        activeLabel.AddToClassList("status-public");
        metaRow.Add(activeLabel);

        info.Add(metaRow);

        Label title = new Label(
            string.IsNullOrWhiteSpace(data.class_name)
                ? "Untitled Class"
                : data.class_name);
        title.AddToClassList("course-title");
        info.Add(title);

        VisualElement teacherRow = new VisualElement();
        teacherRow.AddToClassList("teacher-row");

        VisualElement teacherIcon = new VisualElement();
        teacherIcon.AddToClassList("teacher-icon");
        teacherRow.Add(teacherIcon);

        Label teacherName = new Label(
            string.IsNullOrWhiteSpace(data.teacher_name)
                ? "Unknown Teacher"
                : data.teacher_name);
        teacherName.AddToClassList("teacher-name");
        teacherRow.Add(teacherName);

        info.Add(teacherRow);
        header.Add(info);
        card.Add(header);

        float progress =
            Mathf.Clamp(data.progress_percent, 0f, 100f);

        VisualElement progressHeader =
            new VisualElement();
        progressHeader.AddToClassList(
            "progress-header-row");

        Label progressTitle = new Label("Progress");
        progressTitle.AddToClassList("progress-title");
        progressHeader.Add(progressTitle);

        Label progressValue =
            new Label($"{Mathf.RoundToInt(progress)}%");
        progressValue.AddToClassList("progress-value");
        progressValue.AddToClassList(
            theme == "pink"
                ? "progress-purple"
                : "progress-blue");
        progressHeader.Add(progressValue);
        card.Add(progressHeader);

        VisualElement progressTrack =
            new VisualElement();
        progressTrack.AddToClassList("progress-track");

        VisualElement progressFill =
            new VisualElement();
        progressFill.AddToClassList("progress-fill");
        progressFill.AddToClassList(
            theme == "pink"
                ? "progress-fill-purple"
                : "progress-fill-blue");
        progressFill.style.width =
            new Length(progress, LengthUnit.Percent);

        progressTrack.Add(progressFill);
        card.Add(progressTrack);

        VisualElement divider = new VisualElement();
        divider.AddToClassList("card-divider");
        card.Add(divider);

        VisualElement actions = new VisualElement();
        actions.AddToClassList("student-card-actions");

        Button continueButton =
            new Button(() => OpenStudentClass(data));
        continueButton.AddToClassList("continue-button");
        continueButton.AddToClassList(
            theme == "pink"
                ? "continue-purple"
                : "continue-blue");

        VisualElement bookIcon = new VisualElement();
        bookIcon.AddToClassList("continue-icon");
        bookIcon.AddToClassList("continue-book-icon");
        continueButton.Add(bookIcon);

        Label continueLabel =
            new Label("Continue Learning");
        continueLabel.AddToClassList("continue-label");
        continueButton.Add(continueLabel);
        actions.Add(continueButton);

        Button unenrollButton =
            new Button(() => UnenrollStudentClass(data));
        unenrollButton.AddToClassList("unenroll-button");

        Label unenrollIcon = new Label("×");
        unenrollIcon.AddToClassList("unenroll-icon");
        unenrollButton.Add(unenrollIcon);

        Label unenrollLabel = new Label("Unenroll");
        unenrollLabel.AddToClassList("unenroll-label");
        unenrollButton.Add(unenrollLabel);
        actions.Add(unenrollButton);

        card.Add(actions);
        return card;
    }

    private void LoadStudentActiveQuizzes()
    {
        StartCoroutine(
            SupabaseClassService.GetStudentActiveQuizzes(
                records =>
                {
                    BuildStudentQuizCards(records);
                },
                error =>
                {
                    Debug.LogWarning(
                        "Không tải được active quizzes: " +
                        error
                    );

                    if (studentQuizzesContainer != null)
                    {
                        studentQuizzesContainer.Clear();
                    }

                    // Student vẫn có class nên giữ phần
                    // Active Quizzes, chỉ để trống danh sách.
                    SetVisible(
                        studentQuizzesSection,
                        true
                    );
                }
            )
        );
    }

    private void BuildStudentQuizCards(
        StudentActiveQuiz[] records)
    {
        if (studentQuizzesContainer == null)
        {
            return;
        }

        studentQuizzesContainer.Clear();

        // Khi student đã đăng ký lớp, tiêu đề Active Quizzes
        // luôn hiển thị. Không có quiz thì container để trống.
        SetVisible(studentQuizzesSection, true);

        int count = records?.Length ?? 0;

        if (count == 0)
        {
            return;
        }

        foreach (StudentActiveQuiz quiz in records)
        {
            if (quiz == null)
            {
                continue;
            }

            VisualElement card = new VisualElement();
            card.AddToClassList("quiz-card");

            VisualElement iconShell =
                new VisualElement();
            iconShell.AddToClassList(
                "quiz-icon-shell");
            iconShell.AddToClassList(
                "quiz-icon-blue");

            Label icon = new Label("≡");
            icon.AddToClassList("quiz-icon");
            iconShell.Add(icon);
            card.Add(iconShell);

            VisualElement info =
                new VisualElement();
            info.AddToClassList("quiz-info");

            Label title = new Label(
                string.IsNullOrWhiteSpace(
                    quiz.quiz_title)
                    ? "Quiz"
                    : quiz.quiz_title);
            title.AddToClassList("quiz-title");
            info.Add(title);

            string subtitleText =
                string.IsNullOrWhiteSpace(
                    quiz.closes_at)
                    ? quiz.class_name
                    : $"Due: {quiz.closes_at}";

            Label subtitle =
                new Label(subtitleText);
            subtitle.AddToClassList(
                "quiz-subtitle");
            info.Add(subtitle);
            card.Add(info);

            Label badge = new Label("Open");
            badge.AddToClassList(
                "quiz-open-badge");
            card.Add(badge);

            studentQuizzesContainer.Add(card);
        }
    }

    private void OpenStudentClass(
        StudentEnrolledClass data)
    {
        PlayerPrefs.SetString(
            "selected_class_id",
            data.class_id ?? string.Empty);
        PlayerPrefs.SetString(
            "selected_class_name",
            data.class_name ?? string.Empty);
        PlayerPrefs.Save();

        const string sceneName = "ClassDetailScene";

        if (Application.CanStreamedLevelBeLoaded(
                sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogWarning(
                $"{sceneName} chưa có trong Scene List."
            );
        }
    }

    private void UnenrollStudentClass(
        StudentEnrolledClass data)
    {
        if (data == null ||
            string.IsNullOrWhiteSpace(data.class_id))
        {
            return;
        }

        StartCoroutine(
            SupabaseClassService.UnenrollStudent(
                data.class_id,
                () =>
                {
                    Debug.Log(
                        $"Unenrolled: {data.class_code}"
                    );
                    LoadStudentClasses();
                },
                error =>
                {
                    Debug.LogError(
                        "Unenroll failed: " + error
                    );
                }
            )
        );
    }

    private void ShowStudentEmptyState()
    {
        SetVisible(studentStateContainer, true);
        SetVisible(studentClassesContainer, false);
        SetVisible(studentQuizzesSection, false);
        UpdateStudentHeaderCount(0);
    }

    private void ShowStudentContent(int classCount, bool hasActiveQuizzes)
    {
        bool hasClasses = classCount > 0;

        SetVisible(studentStateContainer, !hasClasses);
        SetVisible(studentClassesContainer, hasClasses);
        SetVisible(studentQuizzesSection, hasClasses && hasActiveQuizzes);
        UpdateStudentHeaderCount(classCount);
    }

    private void UpdateStudentHeaderCount(int count)
    {
        string subtitle = count == 1
            ? "1 active course"
            : $"{count} active courses";

        if (headerController != null)
        {
            headerController.ConfigurePageWithTextAction(
                title: "My Classes",
                subtitle: subtitle,
                actionText: "Enroll New Class",
                actionPrefix: "+",
                actionStyleClass:
                    "header-action-enroll-class",
                showBackButton: true
            );

            headerController.SetCustomClass(
                "my-classes-header");
            headerController.SetCompact(false);
            headerController.SetBottomBorderVisible(true);
            return;
        }

        Label subtitleLabel =
            root?.Q<Label>("header-subtitle-label") ??
            root?.Q<Label>("page-subtitle-label") ??
            root?.Q<Label>(
                className: "header-subtitle");

        if (subtitleLabel != null)
        {
            subtitleLabel.text = subtitle;
        }
    }

    private bool IsTeacher()
    {
        return string.Equals(
            currentRole,
            "teacher",
            StringComparison.OrdinalIgnoreCase);
    }

    private void HandleHeaderActionClicked()
    {
        if (IsTeacher())
        {
            OpenCreateClassScene();
            return;
        }

        OpenEnrollClassScene();
    }

    private void OpenCreateClassScene()
    {
        const string sceneName = "CreateClassScene";

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"{sceneName} chưa được thêm vào Build Profiles/Scene List."
            );

            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    private void OpenEnrollClassScene()
    {
        const string sceneName = "EnrollClassScene";

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"{sceneName} chưa được thêm vào Build Profiles/Scene List."
            );

            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    private void OpenTeacherClass(SupabaseClass data)
    {
        if (data == null)
            return;

        // Lưu thông tin class để ClassDetailScene sử dụng
        PlayerPrefs.SetString("selected_class_id", data.id ?? string.Empty);
        PlayerPrefs.SetString("selected_class_name", data.class_name ?? string.Empty);
        PlayerPrefs.Save();

        const string sceneName = "ClassDetailScene";

        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError(
                $"{sceneName} chưa được thêm vào Build Profiles / Scene List."
            );
        }
    }

    private void EditClass(SupabaseClass data)
    {
        PlayerPrefs.SetString("selected_class_id", data.id ?? string.Empty);
        PlayerPrefs.Save();
        Debug.Log($"Edit class: {data.class_name}");
    }

    private void DeleteClass(SupabaseClass data)
    {
        StartCoroutine(
            SupabaseClassService.DeleteClass(
                data.id,
                () => LoadTeacherClasses(),
                error => Debug.LogError(error)
            )
        );
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        if (element != null)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void DisposeHeader()
    {
        if (headerController == null)
        {
            return;
        }

        headerController.RightActionClicked -= HandleHeaderActionClicked;
        headerController.Dispose();
        headerController = null;
    }

    private void DisposeBottomNavigation()
    {
        bottomNavigationController?.Dispose();
        bottomNavigationController = null;
    }
}