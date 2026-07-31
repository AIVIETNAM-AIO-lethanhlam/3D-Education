using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
[RequireComponent(typeof(SupabaseRuntimeRestService))]
[RequireComponent(typeof(SupabaseLessonService))]
public class ClassDetailPageController : MonoBehaviour
{
    // =========================================================
    // UI REFERENCES
    // =========================================================

    private VisualElement root;

    private Button backButton;
    private Button moreButton;
    private Button teacherMessageButton;

    private Button syllabusTabButton;
    private Button labsTabButton;
    private Button studentListTabButton;

    private VisualElement syllabusContent;
    private VisualElement labsContent;
    private VisualElement studentListContent;

    private VisualElement chapterList;
    private Button addChapterButton;
    private Button editContentButton;

    private VisualElement editorModalOverlay;
    private Label editorModalTitle;
    private Label editorModalMessage;
    private TextField editorModalInput;
    private Button editorModalCancel;
    private Button editorModalConfirm;

    private Label semesterLabel;
    private Label classTitleLabel;
    private Label teacherInitialLabel;
    private Label teacherNameLabel;
    private Label teacherPositionLabel;

    private Label studentCountLabel;
    private Label moduleCountLabel;
    private Label averageScoreLabel;

    private Label progressPercentLabel;
    private Label progressDetailLabel;
    private VisualElement progressFill;

    private SupabaseLessonService lessonService;
    private SupabaseRuntimeRestService runtimeRestService;
    private bool isCreatingChapter;
    private bool isTeacher;
    private bool isEditMode;
    private Action modalConfirmAction;

    // =========================================================
    // PAGE DATA
    // =========================================================

    private readonly List<ChapterData> chapters = new();

    private const string ActiveTabClass = "tab-button-active";
    private const string HiddenClass = "hidden";

    // =========================================================
    // UNITY LIFECYCLE
    // =========================================================

    private void OnEnable()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogError(
                "[ClassDetailPageController] UIDocument was not found."
            );

            return;
        }

        root = uiDocument.rootVisualElement;

        CacheUIReferences();
        HideVerticalScrollbar();
        ResolveServices();
        RegisterEvents();

        LoadClassInformation();
        ConfigureRoleUi();
        StartCoroutine(LoadChapterData());
    }

    private void OnDisable()
    {
        UnregisterEvents();
    }

    // =========================================================
    // UI REFERENCES
    // =========================================================

    private void ResolveServices()
    {
        lessonService = GetComponent<SupabaseLessonService>();
        runtimeRestService = GetComponent<SupabaseRuntimeRestService>();

        if (lessonService == null)
        {
            Debug.LogError(
                "[ClassDetailPageController] SupabaseLessonService is missing."
            );
        }

        if (runtimeRestService == null)
        {
            Debug.LogError(
                "[ClassDetailPageController] SupabaseRuntimeRestService is missing."
            );
        }
    }

    private void CacheUIReferences()
    {
        backButton =
            root.Q<Button>("back-button");

        moreButton =
            root.Q<Button>("more-button");

        teacherMessageButton =
            root.Q<Button>("teacher-message-button");

        syllabusTabButton =
            root.Q<Button>("syllabus-tab-button");

        labsTabButton =
            root.Q<Button>("labs-tab-button");

        studentListTabButton =
            root.Q<Button>("student-list-tab-button");

        syllabusContent =
            root.Q<VisualElement>("syllabus-content");

        labsContent =
            root.Q<VisualElement>("labs-content");

        studentListContent =
            root.Q<VisualElement>("student-list-content");

        chapterList =
            root.Q<VisualElement>("chapter-list");

        addChapterButton =
            root.Q<Button>("add-chapter-button");

        editContentButton =
            root.Q<Button>("edit-content-button");

        editorModalOverlay =
            root.Q<VisualElement>("editor-modal-overlay");

        editorModalTitle =
            root.Q<Label>("editor-modal-title");

        editorModalMessage =
            root.Q<Label>("editor-modal-message");

        editorModalInput =
            root.Q<TextField>("editor-modal-input");

        editorModalCancel =
            root.Q<Button>("editor-modal-cancel");

        editorModalConfirm =
            root.Q<Button>("editor-modal-confirm");

        semesterLabel =
            root.Q<Label>("semester-label");

        classTitleLabel =
            root.Q<Label>("class-title-label");

        teacherInitialLabel =
            root.Q<Label>("teacher-initial-label");

        teacherNameLabel =
            root.Q<Label>("teacher-name-label");

        teacherPositionLabel =
            root.Q<Label>("teacher-position-label");

        studentCountLabel =
            root.Q<Label>("student-count-label");

        moduleCountLabel =
            root.Q<Label>("module-count-label");

        averageScoreLabel =
            root.Q<Label>("average-score-label");

        progressPercentLabel =
            root.Q<Label>("progress-percent-label");

        progressDetailLabel =
            root.Q<Label>("progress-detail-label");

        progressFill =
            root.Q<VisualElement>("progress-fill");
    }


    private void HideVerticalScrollbar()
    {
        ScrollView scrollView = root.Q<ScrollView>("content-scroll-view");
        if (scrollView == null)
            return;

        scrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
    }

    // =========================================================
    // EVENT REGISTRATION
    // =========================================================

    private void RegisterEvents()
    {
        if (backButton != null)
            backButton.clicked += OnBackClicked;

        if (moreButton != null)
            moreButton.clicked += OnMoreClicked;

        if (teacherMessageButton != null)
            teacherMessageButton.clicked += OnTeacherMessageClicked;

        if (syllabusTabButton != null)
            syllabusTabButton.clicked += ShowSyllabusTab;

        if (labsTabButton != null)
            labsTabButton.clicked += ShowLabsTab;

        if (studentListTabButton != null)
            studentListTabButton.clicked += ShowStudentListTab;

        if (addChapterButton != null)
            addChapterButton.clicked += OnAddChapterClicked;

        if (editContentButton != null)
            editContentButton.clicked += ToggleEditMode;

        if (editorModalCancel != null)
            editorModalCancel.clicked += CloseEditorModal;

        if (editorModalConfirm != null)
            editorModalConfirm.clicked += ConfirmEditorModal;
    }

    private void UnregisterEvents()
    {
        if (backButton != null)
            backButton.clicked -= OnBackClicked;

        if (moreButton != null)
            moreButton.clicked -= OnMoreClicked;

        if (teacherMessageButton != null)
            teacherMessageButton.clicked -= OnTeacherMessageClicked;

        if (syllabusTabButton != null)
            syllabusTabButton.clicked -= ShowSyllabusTab;

        if (labsTabButton != null)
            labsTabButton.clicked -= ShowLabsTab;

        if (studentListTabButton != null)
            studentListTabButton.clicked -= ShowStudentListTab;

        if (addChapterButton != null)
            addChapterButton.clicked -= OnAddChapterClicked;

        if (editContentButton != null)
            editContentButton.clicked -= ToggleEditMode;

        if (editorModalCancel != null)
            editorModalCancel.clicked -= CloseEditorModal;

        if (editorModalConfirm != null)
            editorModalConfirm.clicked -= ConfirmEditorModal;
    }

    private void ConfigureRoleUi()
    {
        string role = PlayerPrefs.GetString(
            "current_role",
            PlayerPrefs.GetString("role", string.Empty)
        );

        isTeacher = string.Equals(
            role,
            "teacher",
            StringComparison.OrdinalIgnoreCase
        );

        SetVisible(editContentButton, isTeacher);
        SetVisible(addChapterButton, isTeacher);

        if (!isTeacher)
            isEditMode = false;
    }

    private void ToggleEditMode()
    {
        if (!isTeacher)
            return;

        isEditMode = !isEditMode;

        if (editContentButton != null)
        {
            editContentButton.text =
                isEditMode ? "Hoàn tất chỉnh sửa" : "Chỉnh sửa";
        }

        RenderChapterList();
    }

    private static void SetVisible(
        VisualElement element,
        bool visible
    )
    {
        if (element == null)
            return;

        element.EnableInClassList(
            HiddenClass,
            !visible
        );
    }

    // =========================================================
    // CLASS INFORMATION
    // =========================================================

    private void LoadClassInformation()
    {
        /*
         * Hiện tại dùng dữ liệu mẫu.
         *
         * Sau này có thể lấy selected_class_id:
         *
         * string classId =
         *     PlayerPrefs.GetString("selected_class_id", "");
         *
         * Sau đó gọi SupabaseClassService để lấy:
         * - class_name
         * - semester
         * - teacher_name
         * - student count
         * - chapter/module count
         * - average score
         */

        semesterLabel.text =
            "SEMESTER 2 · 2024–2025";

        classTitleLabel.text =
            "EE301 – Circuit Analysis";

        teacherInitialLabel.text =
            "TQ";

        teacherNameLabel.text =
            "Dr. Trần Minh Quân";

        teacherPositionLabel.text =
            "Associate Professor";

        studentCountLabel.text =
            "34";

        moduleCountLabel.text =
            "5";

        averageScoreLabel.text =
            "89%";
    }

    // =========================================================
    // CHAPTER DATA
    // =========================================================

    private IEnumerator LoadChapterData()
    {
        chapters.Clear();
        RenderChapterList();
        UpdateProgress();

        if (lessonService == null)
            yield break;

        string classId = PlayerPrefs.GetString(
            "selected_class_id",
            string.Empty
        );

        if (!Guid.TryParse(classId, out _))
        {
            Debug.LogError(
                "[ClassDetailPageController] selected_class_id is invalid."
            );
            yield break;
        }

        List<ChapterRecord> records = null;
        string error = null;

        yield return lessonService.GetChaptersByClass(
            classId,
            result => records = result,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError(
                "Unable to load chapters: " + error
            );
            yield break;
        }

        chapters.Clear();

        if (records != null)
        {
            foreach (ChapterRecord record in records)
            {
                if (record == null)
                    continue;

                chapters.Add(
                    new ChapterData
                    {
                        Id = record.id,
                        ClassId = record.class_id,
                        Title = record.title,
                        Order = record.chapter_order,
                        Status = ChapterStatus.Upcoming,
                        Lessons = new List<LessonData>()
                    }
                );
            }
        }

        chapters.Sort(
            (left, right) =>
                left.Order.CompareTo(right.Order)
        );

        // Load the real lessons belonging to each chapter.
        for (int i = 0; i < chapters.Count; i++)
        {
            yield return LoadLessonsForChapter(chapters[i]);
        }

        RenderChapterList();
        UpdateProgress();
    }

    private IEnumerator LoadLessonsForChapter(
        ChapterData chapter
    )
    {
        if (chapter == null)
            yield break;

        chapter.Lessons ??= new List<LessonData>();
        chapter.Lessons.Clear();

        if (runtimeRestService == null)
            yield break;

        if (!Guid.TryParse(chapter.Id, out _))
        {
            Debug.LogWarning(
                $"Skip loading lessons because chapter ID is invalid: {chapter.Id}"
            );
            yield break;
        }

        string encodedChapterId =
            UnityWebRequest.EscapeURL(chapter.Id);

        string response = null;
        string error = null;

        string endpoint =
            "rest/v1/lessons" +
            "?select=id,chapter_id,title,status,created_at" +
            $"&chapter_id=eq.{encodedChapterId}" +
            "&order=created_at.asc";

        yield return runtimeRestService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            endpoint,
            null,
            null,
            value => response = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError(
                $"Unable to load lessons for chapter {chapter.Title}: {error}"
            );
            yield break;
        }

        if (string.IsNullOrWhiteSpace(response))
            yield break;

        try
        {
            LessonRecordList wrapper =
                JsonUtility.FromJson<LessonRecordList>(
                    $"{{\"items\":{response}}}"
                );

            if (wrapper?.items == null)
                yield break;

            foreach (LessonRecord record in wrapper.items)
            {
                if (record == null)
                    continue;

                chapter.Lessons.Add(
                    new LessonData
                    {
                        Id = record.id,
                        Title = string.IsNullOrWhiteSpace(record.title)
                            ? "Untitled Lesson"
                            : record.title,
                        IsComplete = false,
                        Has3DContent = false
                    }
                );
            }

            if (chapter.Lessons.Count > 0)
            {
                chapter.Status = ChapterStatus.InProgress;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Cannot parse lessons for chapter {chapter.Title}: " +
                exception.Message
            );
        }
    }

    public void ApplyLoadedChapters(IEnumerable<ChapterData> records)
    {
        chapters.Clear();

        if (records != null)
        {
            foreach (ChapterData chapter in records)
            {
                if (chapter == null)
                    continue;

                chapter.Lessons ??= new List<LessonData>();
                chapters.Add(chapter);
            }
        }

        chapters.Sort((left, right) =>
            left.Order.CompareTo(right.Order));

        RenderChapterList();
        UpdateProgress();
    }

    // =========================================================
    // CHAPTER RENDERING
    // =========================================================

    private void RenderChapterList()
    {
        if (chapterList == null)
        {
            Debug.LogError(
                "[ClassDetailPageController] chapter-list was not found."
            );

            return;
        }

        chapterList.Clear();

        for (int i = 0; i < chapters.Count; i++)
        {
            ChapterData chapter = chapters[i];

            VisualElement chapterCard =
                CreateChapterCard(chapter, i);

            chapterList.Add(chapterCard);
        }

        moduleCountLabel.text =
            chapters.Count.ToString();
    }

    private VisualElement CreateChapterCard(
        ChapterData chapter,
        int chapterIndex
    )
    {
        VisualElement card = new();
        card.AddToClassList("chapter-card");

        VisualElement header =
            CreateChapterHeader(chapter, chapterIndex);

        VisualElement lessonListElement = new();
        lessonListElement.AddToClassList("lesson-list");

        for (int i = 0; i < (chapter.Lessons?.Count ?? 0); i++)
        {
            LessonData lesson = chapter.Lessons[i];

            lessonListElement.Add(
                CreateLessonRow(chapter, lesson)
            );
        }

        if (isTeacher)
        {
            Button addLessonButton =
                CreateAddLessonButton(chapter);

            lessonListElement.Add(addLessonButton);
        }

        card.Add(header);
        card.Add(lessonListElement);

        Button collapseButton =
            header.Q<Button>("collapse-button");

        VisualElement collapseArrow =
            header.Q<VisualElement>("collapse-arrow");

        if (collapseButton != null)
        {
            collapseButton.clicked += () =>
            {
                chapter.IsCollapsed =
                    !chapter.IsCollapsed;

                lessonListElement.style.display =
                    chapter.IsCollapsed
                        ? DisplayStyle.None
                        : DisplayStyle.Flex;

                if (collapseArrow != null)
                {
                    collapseArrow.EnableInClassList(
                        "arrow-up-icon",
                        !chapter.IsCollapsed
                    );

                    collapseArrow.EnableInClassList(
                        "arrow-down-icon",
                        chapter.IsCollapsed
                    );
                }
            };
        }

        return card;
    }

    private VisualElement CreateChapterHeader(
        ChapterData chapter,
        int chapterIndex
    )
    {
        VisualElement header = new();
        header.AddToClassList("chapter-header-row");

        VisualElement dragHandle = new();
        dragHandle.AddToClassList("drag-icon");

        VisualElement statusCircle = new();
        statusCircle.AddToClassList("chapter-status-circle");

        if (chapter.Status == ChapterStatus.Complete)
        {
            statusCircle.AddToClassList("chapter-status-complete");

            VisualElement checkIcon = new();
            checkIcon.AddToClassList("chapter-status-icon");
            checkIcon.AddToClassList("chapter-check-icon");
            statusCircle.Add(checkIcon);
        }
        else if (chapter.Status == ChapterStatus.InProgress)
        {
            statusCircle.AddToClassList("chapter-status-in-progress");

            Label progressLabel =
                new($"{Mathf.Clamp(chapter.ProgressPercent, 0, 100)}%");

            progressLabel.AddToClassList(
                "chapter-progress-percent"
            );

            statusCircle.Add(progressLabel);
        }
        else
        {
            statusCircle.AddToClassList("chapter-status-upcoming");

            VisualElement lockIcon = new();
            lockIcon.AddToClassList("chapter-status-icon");
            lockIcon.AddToClassList("chapter-upcoming-icon");
            statusCircle.Add(lockIcon);
        }

        VisualElement information = new();
        information.AddToClassList("chapter-information");

        int displayOrder =
            chapter.Order > 0
                ? chapter.Order
                : chapterIndex + 1;

        Label indexLabel =
            new($"CHAPTER {displayOrder}");

        indexLabel.AddToClassList(
            "chapter-index-label"
        );

        Label titleLabel =
            new(chapter.Title);

        titleLabel.AddToClassList(
            "chapter-title-label"
        );

        VisualElement metaRow = new();
        metaRow.AddToClassList("chapter-meta-row");

        Label lessonCount =
            new($"{chapter.Lessons?.Count ?? 0} lessons");

        lessonCount.AddToClassList(
            "chapter-lesson-count"
        );

        Label separator = new("·");
        separator.AddToClassList(
            "chapter-meta-separator"
        );

        Label statusLabel =
            new(GetChapterStatusText(chapter.Status));

        statusLabel.AddToClassList(
            "chapter-status-label"
        );

        statusLabel.AddToClassList(
            chapter.Status == ChapterStatus.Complete
                ? "status-text-complete"
                : chapter.Status == ChapterStatus.InProgress
                    ? "status-text-in-progress"
                    : "status-text-upcoming"
        );

        metaRow.Add(lessonCount);
        metaRow.Add(separator);
        metaRow.Add(statusLabel);

        information.Add(indexLabel);
        information.Add(titleLabel);
        information.Add(metaRow);

        VisualElement editActions = new();
        editActions.AddToClassList("chapter-edit-actions");

        if (isTeacher && isEditMode)
        {
            Button renameButton =
                CreateInlineActionButton(
                    "inline-edit-button",
                    "inline-edit-icon",
                    () => ShowRenameChapterModal(chapter)
                );

            Button deleteButton =
                CreateInlineActionButton(
                    "inline-delete-button",
                    "inline-delete-icon",
                    () => ShowDeleteChapterModal(chapter)
                );

            editActions.Add(renameButton);
            editActions.Add(deleteButton);
        }

        Button collapseButton = new();
        collapseButton.name = "collapse-button";

        collapseButton.AddToClassList(
            "chapter-action-button"
        );
        collapseButton.AddToClassList(
            "collapse-chapter-button"
        );

        VisualElement collapseArrow = new();
        collapseArrow.name = "collapse-arrow";
        collapseArrow.AddToClassList(
            "collapse-arrow-icon"
        );
        collapseArrow.AddToClassList(
            chapter.IsCollapsed
                ? "arrow-down-icon"
                : "arrow-up-icon"
        );

        collapseButton.Add(collapseArrow);

        header.Add(dragHandle);
        header.Add(statusCircle);
        header.Add(information);

        if (isTeacher && isEditMode)
            header.Add(editActions);

        header.Add(collapseButton);

        return header;
    }

    // =========================================================
    // LESSON RENDERING
    // =========================================================

    private VisualElement CreateLessonRow(
        ChapterData chapter,
        LessonData lesson
    )
    {
        VisualElement row = new();
        row.AddToClassList("lesson-row");

        VisualElement dragHandle = new();
        dragHandle.AddToClassList(
            "lesson-drag-icon"
        );

        VisualElement statusCircle = new();
        statusCircle.AddToClassList(
            "lesson-status-circle"
        );

        VisualElement lessonStatusIcon = new();
        lessonStatusIcon.AddToClassList(
            "lesson-status-icon"
        );

        if (lesson.IsComplete)
        {
            statusCircle.AddToClassList(
                "lesson-complete-circle"
            );

            lessonStatusIcon.AddToClassList(
                "lesson-check-icon"
            );
        }
        else
        {
            lessonStatusIcon.AddToClassList(
                "lesson-book-icon"
            );
        }

        statusCircle.Add(lessonStatusIcon);

        VisualElement information = new();
        information.AddToClassList(
            "lesson-information"
        );

        Label title = new(lesson.Title);
        title.AddToClassList("lesson-title");

        information.Add(title);

        if (lesson.Has3DContent)
        {
            Label badge = new("3D");
            badge.AddToClassList("lesson-badge");

            information.Add(badge);
        }

        row.Add(dragHandle);
        row.Add(statusCircle);
        row.Add(information);

        if (isTeacher && isEditMode)
        {
            VisualElement editActions = new();
            editActions.AddToClassList("lesson-edit-actions");

            Button editButton =
                CreateInlineActionButton(
                    "inline-edit-button",
                    "inline-edit-icon",
                    () => OpenLessonForUpdate(chapter, lesson)
                );

            Button deleteButton =
                CreateInlineActionButton(
                    "inline-delete-button",
                    "inline-delete-icon",
                    () => ShowDeleteLessonModal(chapter, lesson)
                );

            editActions.Add(editButton);
            editActions.Add(deleteButton);
            row.Add(editActions);
        }

        row.RegisterCallback<ClickEvent>(_ =>
        {
            if (!isEditMode)
                OpenLesson(chapter, lesson);
        });

        return row;
    }

    private Button CreateAddLessonButton(
        ChapterData chapter
    )
    {
        Button button = new();
        button.AddToClassList("add-lesson-button");

        Label plusLabel = new("+");
        plusLabel.AddToClassList("add-lesson-plus");

        Label textLabel = new("Add Lesson");

        button.Add(plusLabel);
        button.Add(textLabel);

        button.clicked += () =>
        {
            AddLessonToChapter(chapter);
        };

        return button;
    }

    // =========================================================
    // PROGRESS
    // =========================================================

    private void UpdateProgress()
    {
        int totalLessons = 0;
        int completedLessons = 0;

        foreach (ChapterData chapter in chapters)
        {
            foreach (LessonData lesson in chapter.Lessons)
            {
                totalLessons++;

                if (lesson.IsComplete)
                {
                    completedLessons++;
                }
            }
        }

        float progress = totalLessons <= 0
            ? 0f
            : (float)completedLessons / totalLessons;

        int percent =
            Mathf.RoundToInt(progress * 100f);

        if (progressPercentLabel != null)
        {
            progressPercentLabel.text =
                $"{percent}%";
        }

        if (progressDetailLabel != null)
        {
            progressDetailLabel.text =
                $"{completedLessons} / {totalLessons} lessons";
        }

        if (progressFill != null)
        {
            progressFill.style.width =
                new Length(
                    percent,
                    LengthUnit.Percent
                );
        }
    }

    // =========================================================
    // TAB HANDLING
    // =========================================================

    private void ShowSyllabusTab()
    {
        SetActiveTab(
            syllabusTabButton,
            syllabusContent
        );
    }

    private void ShowLabsTab()
    {
        SetActiveTab(
            labsTabButton,
            labsContent
        );
    }

    private void ShowStudentListTab()
    {
        SetActiveTab(
            studentListTabButton,
            studentListContent
        );
    }

    private void SetActiveTab(
        Button activeButton,
        VisualElement activeContent
    )
    {
        syllabusTabButton.RemoveFromClassList(
            ActiveTabClass
        );

        labsTabButton.RemoveFromClassList(
            ActiveTabClass
        );

        studentListTabButton.RemoveFromClassList(
            ActiveTabClass
        );

        syllabusContent.AddToClassList(
            HiddenClass
        );

        labsContent.AddToClassList(
            HiddenClass
        );

        studentListContent.AddToClassList(
            HiddenClass
        );

        activeButton.AddToClassList(
            ActiveTabClass
        );

        activeContent.RemoveFromClassList(
            HiddenClass
        );
    }

    // =========================================================
    // BUTTON EVENTS
    // =========================================================

    private void OnBackClicked()
    {
        if (Application.CanStreamedLevelBeLoaded(
                "MyClassesScene"
            ))
        {
            SceneManager.LoadScene(
                "MyClassesScene"
            );
        }
        else
        {
            Debug.LogWarning(
                "MyClassesScene is not included in Build Profiles."
            );
        }
    }

    private void OnMoreClicked()
    {
        Debug.Log(
            "Open class options menu."
        );

        /*
         * Có thể mở popup gồm:
         * - Edit class
         * - Class settings
         * - Archive class
         * - Delete class
         */
    }

    private void OnTeacherMessageClicked()
    {
        Debug.Log(
            "Open teacher message screen."
        );
    }

    private void OnAddChapterClicked()
    {
        if (isCreatingChapter)
            return;

        StartCoroutine(CreateChapterRoutine());
    }

    private IEnumerator CreateChapterRoutine()
    {
        if (lessonService == null)
        {
            Debug.LogError(
                "Cannot create chapter because SupabaseLessonService is missing."
            );
            yield break;
        }

        string classId = PlayerPrefs.GetString(
            "selected_class_id",
            string.Empty
        );

        if (!Guid.TryParse(classId, out _))
        {
            Debug.LogError(
                "Cannot create chapter because selected_class_id is invalid."
            );
            yield break;
        }

        isCreatingChapter = true;
        addChapterButton?.SetEnabled(false);

        int nextChapterOrder = GetNextChapterOrder();

        CreateChapterRequest request = new()
        {
            class_id = classId,
            title = $"New Chapter {nextChapterOrder}",
            chapter_order = nextChapterOrder
        };

        ChapterRecord createdRecord = null;
        string error = null;

        yield return lessonService.CreateChapter(
            request,
            result => createdRecord = result,
            message => error = message
        );

        isCreatingChapter = false;
        addChapterButton?.SetEnabled(true);

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError(
                "Unable to create chapter: " + error
            );
            yield break;
        }

        if (createdRecord == null ||
            string.IsNullOrWhiteSpace(createdRecord.id))
        {
            Debug.LogError(
                "Supabase did not return the created chapter."
            );
            yield break;
        }

        ChapterData newChapter = new()
        {
            Id = createdRecord.id,
            ClassId = createdRecord.class_id,
            Order = createdRecord.chapter_order,
            Title = createdRecord.title,
            Status = ChapterStatus.Upcoming,
            Lessons = new List<LessonData>()
        };

        chapters.Add(newChapter);
        chapters.Sort(
            (left, right) =>
                left.Order.CompareTo(right.Order)
        );

        RenderChapterList();
        UpdateProgress();

        Debug.Log(
            $"Created chapter in Supabase: {newChapter.Id}"
        );
    }

    private void AddLessonToChapter(
        ChapterData chapter
    )
    {
        if (chapter == null ||
            string.IsNullOrWhiteSpace(chapter.Id) ||
            !Guid.TryParse(chapter.Id, out _))
        {
            Debug.LogError(
                "Cannot open CreateLessonScene because chapter_id is empty."
            );
            return;
        }

        string classId = !string.IsNullOrWhiteSpace(chapter.ClassId)
            ? chapter.ClassId
            : PlayerPrefs.GetString(
                "selected_class_id",
                string.Empty
            );

        PlayerPrefs.SetString(
            "selected_class_id",
            classId
        );

        PlayerPrefs.SetString(
            "selected_chapter_id",
            chapter.Id
        );

        PlayerPrefs.SetInt(
            "selected_chapter_order",
            chapter.Order
        );

        PlayerPrefs.SetString(
            "selected_chapter_title",
            chapter.Title ?? string.Empty
        );

        PlayerPrefs.SetString(
            "previous_scene",
            "ClassDetailScene"
        );

        PlayerPrefs.SetString(
            "lesson_editor_mode",
            "create"
        );

        PlayerPrefs.DeleteKey(
            "selected_lesson_id"
        );

        PlayerPrefs.Save();

        const string sceneName = "CreateLessonScene";

        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError(
                $"{sceneName} is not included in Build Profiles / Scene List."
            );
        }
    }

    private Button CreateInlineActionButton(
        string buttonClass,
        string iconClass,
        Action action
    )
    {
        Button button = new();
        button.AddToClassList(buttonClass);

        VisualElement icon = new();
        icon.AddToClassList(iconClass);
        button.Add(icon);

        button.clicked += () => action?.Invoke();
        return button;
    }

    private void ShowDeleteChapterModal(
        ChapterData chapter
    )
    {
        ShowEditorModal(
            "Xóa chapter",
            "Bạn có chắc chắn muốn xóa chapter này và toàn bộ bài học bên trong không?",
            false,
            null,
            () => StartCoroutine(DeleteChapterRoutine(chapter))
        );
    }

    private void ShowRenameChapterModal(
        ChapterData chapter
    )
    {
        ShowEditorModal(
            "Đổi tên chapter",
            "Nhập tên mới cho chapter.",
            true,
            chapter.Title,
            () =>
            {
                string newTitle =
                    editorModalInput?.value?.Trim();

                if (!string.IsNullOrWhiteSpace(newTitle))
                    StartCoroutine(
                        RenameChapterRoutine(chapter, newTitle)
                    );
            }
        );
    }

    private void ShowDeleteLessonModal(
        ChapterData chapter,
        LessonData lesson
    )
    {
        ShowEditorModal(
            "Xóa bài học",
            $"Bạn có chắc chắn muốn xóa bài học “{lesson.Title}” không?",
            false,
            null,
            () => StartCoroutine(
                DeleteLessonRoutine(chapter, lesson)
            )
        );
    }

    private void ShowEditorModal(
        string title,
        string message,
        bool showInput,
        string inputValue,
        Action confirmAction
    )
    {
        modalConfirmAction = confirmAction;

        if (editorModalTitle != null)
            editorModalTitle.text = title;

        if (editorModalMessage != null)
            editorModalMessage.text = message;

        if (editorModalInput != null)
        {
            editorModalInput.SetValueWithoutNotify(
                inputValue ?? string.Empty
            );

            SetVisible(editorModalInput, showInput);
        }

        if (editorModalCancel != null)
            editorModalCancel.text = showInput ? "Hủy" : "No";

        if (editorModalConfirm != null)
            editorModalConfirm.text = showInput ? "Lưu" : "Yes";

        SetVisible(editorModalOverlay, true);

        if (showInput)
            editorModalInput?.Focus();
    }

    private void CloseEditorModal()
    {
        modalConfirmAction = null;
        SetVisible(editorModalOverlay, false);
    }

    private void ConfirmEditorModal()
    {
        Action action = modalConfirmAction;
        CloseEditorModal();
        action?.Invoke();
    }

    private IEnumerator RenameChapterRoutine(
        ChapterData chapter,
        string newTitle
    )
    {
        string error = null;

        yield return runtimeRestService.SendJson(
            "PATCH",
            $"rest/v1/chapters?id=eq.{UnityWebRequest.EscapeURL(chapter.Id)}",
            JsonUtility.ToJson(
                new ChapterTitleUpdate { title = newTitle }
            ),
            "return=minimal",
            _ => { },
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError(
                "Unable to rename chapter: " + error
            );
            yield break;
        }

        chapter.Title = newTitle;
        RenderChapterList();
    }

    private IEnumerator DeleteLessonRoutine(
        ChapterData chapter,
        LessonData lesson
    )
    {
        string lessonId =
            UnityWebRequest.EscapeURL(lesson.Id);

        string error = null;

        yield return DeleteByLessonId(
            "lesson_objectives",
            lessonId,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError(error);
            yield break;
        }

        yield return DeleteByLessonId(
            "lesson_assets",
            lessonId,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError(error);
            yield break;
        }

        yield return runtimeRestService.SendJson(
            "DELETE",
            $"rest/v1/lessons?id=eq.{lessonId}",
            null,
            "return=minimal",
            _ => { },
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError(
                "Unable to delete lesson: " + error
            );
            yield break;
        }

        chapter.Lessons.Remove(lesson);
        RenderChapterList();
        UpdateProgress();
    }

    private IEnumerator DeleteChapterRoutine(
        ChapterData chapter
    )
    {
        // Database foreign keys should ideally use ON DELETE CASCADE.
        // The explicit lesson cleanup below also works without cascade.
        List<LessonData> copy =
            new(chapter.Lessons ?? new List<LessonData>());

        foreach (LessonData lesson in copy)
        {
            bool deleted = false;

            yield return DeleteLessonRoutine(
                chapter,
                lesson
            );

            deleted = !chapter.Lessons.Contains(lesson);

            if (!deleted)
                yield break;
        }

        string error = null;

        yield return runtimeRestService.SendJson(
            "DELETE",
            $"rest/v1/chapters?id=eq.{UnityWebRequest.EscapeURL(chapter.Id)}",
            null,
            "return=minimal",
            _ => { },
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError(
                "Unable to delete chapter: " + error
            );
            yield break;
        }

        chapters.Remove(chapter);
        RenderChapterList();
        UpdateProgress();
    }

    private IEnumerator DeleteByLessonId(
        string table,
        string encodedLessonId,
        Action<string> onError
    )
    {
        string error = null;

        yield return runtimeRestService.SendJson(
            "DELETE",
            $"rest/v1/{table}?lesson_id=eq.{encodedLessonId}",
            null,
            "return=minimal",
            _ => { },
            message => error = message
        );

        onError?.Invoke(error);
    }

    private void OpenLessonForUpdate(
        ChapterData chapter,
        LessonData lesson
    )
    {
        PlayerPrefs.SetString("lesson_editor_mode", "update");
        PlayerPrefs.SetString("selected_class_id", chapter.ClassId);
        PlayerPrefs.SetString("selected_chapter_id", chapter.Id);
        PlayerPrefs.SetString("selected_chapter_title", chapter.Title);
        PlayerPrefs.SetInt("selected_chapter_order", chapter.Order);
        PlayerPrefs.SetString("selected_lesson_id", lesson.Id);
        PlayerPrefs.SetString("previous_scene", "ClassDetailScene");
        PlayerPrefs.Save();

        if (Application.CanStreamedLevelBeLoaded("CreateLessonScene"))
            SceneManager.LoadScene("CreateLessonScene");
        else
            Debug.LogError("CreateLessonScene is not in the Scene List.");
    }

    private void OpenLesson(
        ChapterData chapter,
        LessonData lesson
    )
    {
        PlayerPrefs.SetString(
            "selected_chapter_id",
            chapter.Id
        );

        PlayerPrefs.SetString(
            "selected_lesson_id",
            lesson.Id
        );

        PlayerPrefs.Save();

        Debug.Log(
            $"Open lesson: {lesson.Title}"
        );

        const string sceneName = "ShowLessonScene";

        PlayerPrefs.SetString(
            "previous_scene",
            "ClassDetailScene"
        );
        PlayerPrefs.Save();

        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError(
                $"{sceneName} is not included in Build Profiles / Scene List."
            );
        }
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private int GetNextChapterOrder()
    {
        int highestOrder = 0;

        foreach (ChapterData chapter in chapters)
        {
            if (chapter == null)
                continue;

            highestOrder = Mathf.Max(
                highestOrder,
                chapter.Order
            );
        }

        return highestOrder + 1;
    }

    private int GetTotalLessonCount()
    {
        int count = 0;

        foreach (ChapterData chapter in chapters)
        {
            count += chapter.Lessons.Count;
        }

        return count;
    }

    private string GetChapterStatusText(
        ChapterStatus status
    )
    {
        return status switch
        {
            ChapterStatus.Complete =>
                "Complete",

            ChapterStatus.InProgress =>
                "In Progress",

            ChapterStatus.Upcoming =>
                "Upcoming",

            _ => "Upcoming"
        };
    }
}

[Serializable]
public class ChapterTitleUpdate
{
    public string title;
}

// =============================================================
// DATA MODELS
// =============================================================

[Serializable]
public class ChapterData
{
    public string Id;
    public string ClassId;
    public string Title;
    public int Order;

    public ChapterStatus Status;

    [Range(0, 100)]
    public int ProgressPercent;

    public bool IsCollapsed;

    public List<LessonData> Lessons =
        new();
}

[Serializable]
public class LessonData
{
    public string Id;
    public string Title;

    public bool IsComplete;
    public bool Has3DContent;
}

public enum ChapterStatus
{
    Complete,
    InProgress,
    Upcoming
}