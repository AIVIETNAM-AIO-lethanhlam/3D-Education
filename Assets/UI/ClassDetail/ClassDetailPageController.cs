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
[RequireComponent(typeof(R2StorageService))]
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
    private VisualElement studentListUnreadDot;

    private VisualElement syllabusContent;
    private VisualElement labsContent;
    private VisualElement studentListContent;

    private VisualElement studentCardList;
    private VisualElement studentListLoadingState;
    private VisualElement studentListEmptyState;
    private VisualElement studentListErrorState;
    private Label studentListErrorLabel;
    private Label studentListCountLabel;
    private Label studentOnlineCountLabel;
    private Button studentListRetryButton;

    private VisualElement modelCardList;
    private VisualElement labsLoadingState;
    private VisualElement labsEmptyState;
    private VisualElement labsErrorState;
    private Label labsErrorLabel;
    private Button labsRetryButton;

    private VisualElement chapterList;
    private VisualElement reorderPreview;
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
    private Label teacherMessageUnreadBadge;

    private Label studentCountLabel;
    private Label moduleCountLabel;
    private Label averageScoreLabel;

    private Label progressPercentLabel;
    private Label progressDetailLabel;
    private VisualElement progressFill;

    private SupabaseLessonService lessonService;
    private SupabaseRuntimeRestService runtimeRestService;
    private R2StorageService r2StorageService;
    private bool isCreatingChapter;
    private bool isTeacher;
    private bool isEditMode;
    private Action modalConfirmAction;

    // =========================================================
    // PAGE DATA
    // =========================================================

    private readonly List<ChapterData> chapters = new();
    private readonly List<Class3DModelData> class3DModels = new();
    private readonly List<ClassMemberStudent> enrolledStudents = new();
    private bool isLoading3DModels;
    private bool isLoadingStudents;
    private bool hasLoadedStudents;

    // Teacher-side unread state.
    // Key = student user id, Value = unread messages sent by that student.
    private readonly Dictionary<string, int> studentUnreadCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private Coroutine studentUnreadPollingCoroutine;
    private const float StudentUnreadPollInterval = 2f;

    // The tab dot is an "attention" indicator, not the read state itself.
    // Opening Student List acknowledges the current unread set. If a NEW unread
    // message arrives later, the signature changes and the red dot appears again.
    private string latestStudentUnreadSignature = string.Empty;
    private string acknowledgedStudentUnreadSignature = string.Empty;

    // Student -> teacher direct-chat state for the current class.
    private string currentTeacherId;
    private string currentTeacherName = "Teacher";
    private string currentTeacherConversationId;
    private Coroutine teacherUnreadPollingCoroutine;
    private const float TeacherUnreadPollInterval = 2f;

    [Serializable] private class ClassOwnerRecord { public string teacher_id; }
    [Serializable] private class ClassOwnerArray { public ClassOwnerRecord[] items; }
    [Serializable] private class ChatProfileRecord { public string id; public string full_name; public string role; }
    [Serializable] private class ChatProfileArray { public ChatProfileRecord[] items; }
    [Serializable] private class DirectConversationRpcBody { public string p_other_user_id; public string p_class_id; }
    [Serializable] private class UnreadMessageRecord { public string id; }
    [Serializable] private class UnreadMessageArray { public UnreadMessageRecord[] items; }

    [Serializable]
    private class TeacherConversationRecord
    {
        public string id;
        public string class_id;
        public string user_a_id;
        public string user_b_id;
    }

    [Serializable] private class TeacherConversationArray { public TeacherConversationRecord[] items; }

    [Serializable]
    private class TeacherUnreadMessageRecord
    {
        public string id;
        public string conversation_id;
        public string sender_id;
        public string receiver_id;
    }

    [Serializable] private class TeacherUnreadMessageArray { public TeacherUnreadMessageRecord[] items; }

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

        SetEmptyClassInformation();
        ConfigureRoleUi();
        ShowSyllabusTab();
        StartCoroutine(LoadClassInformationFromSupabase());
        StartCoroutine(LoadChapterData());

        if (isTeacher)
        {
            studentUnreadPollingCoroutine =
                StartCoroutine(TeacherStudentUnreadPollingLoop());
        }
    }

    private void OnDisable()
    {
        UnregisterEvents();

        if (teacherUnreadPollingCoroutine != null)
        {
            StopCoroutine(teacherUnreadPollingCoroutine);
            teacherUnreadPollingCoroutine = null;
        }

        if (studentUnreadPollingCoroutine != null)
        {
            StopCoroutine(studentUnreadPollingCoroutine);
            studentUnreadPollingCoroutine = null;
        }
    }

    // =========================================================
    // UI REFERENCES
    // =========================================================

    private void ResolveServices()
    {
        lessonService = GetComponent<SupabaseLessonService>();
        runtimeRestService = GetComponent<SupabaseRuntimeRestService>();
        r2StorageService = GetComponent<R2StorageService>();

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

        studentListUnreadDot =
            root.Q<VisualElement>("student-list-unread-dot");

        syllabusContent =
            root.Q<VisualElement>("syllabus-content");

        labsContent =
            root.Q<VisualElement>("labs-content");

        studentListContent =
            root.Q<VisualElement>("student-list-content");

        studentCardList =
            root.Q<VisualElement>("student-card-list");

        studentListLoadingState =
            root.Q<VisualElement>("student-list-loading-state");

        studentListEmptyState =
            root.Q<VisualElement>("student-list-empty-state");

        studentListErrorState =
            root.Q<VisualElement>("student-list-error-state");

        studentListErrorLabel =
            root.Q<Label>("student-list-error-label");

        studentListCountLabel =
            root.Q<Label>("student-list-count-label");

        studentOnlineCountLabel =
            root.Q<Label>("student-online-count-label");

        studentListRetryButton =
            root.Q<Button>("student-list-retry-button");

        modelCardList =
            root.Q<VisualElement>("model-card-list");

        labsLoadingState =
            root.Q<VisualElement>("labs-loading-state");

        labsEmptyState =
            root.Q<VisualElement>("labs-empty-state");

        labsErrorState =
            root.Q<VisualElement>("labs-error-state");

        labsErrorLabel =
            root.Q<Label>("labs-error-label");

        labsRetryButton =
            root.Q<Button>("labs-retry-button");

        chapterList =
            root.Q<VisualElement>("chapter-list");

        reorderPreview =
            root.Q<VisualElement>("reorder-preview");

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

        teacherMessageUnreadBadge =
            root.Q<Label>("teacher-message-unread-badge");

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

        if (labsRetryButton != null)
            labsRetryButton.clicked += RetryLoad3DModels;

        if (studentListRetryButton != null)
            studentListRetryButton.clicked += RetryLoadStudents;

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

        if (labsRetryButton != null)
            labsRetryButton.clicked -= RetryLoad3DModels;

        if (studentListRetryButton != null)
            studentListRetryButton.clicked -= RetryLoadStudents;

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
        string role = !string.IsNullOrWhiteSpace(SupabaseSession.Role)
            ? SupabaseSession.Role.Trim()
            : PlayerPrefs.GetString(
                "current_role",
                PlayerPrefs.GetString("role", string.Empty)
            ).Trim();

        isTeacher = string.Equals(
            role,
            "teacher",
            StringComparison.OrdinalIgnoreCase
        );

        if (root != null)
        {
            root.EnableInClassList("role-teacher", isTeacher);
            root.EnableInClassList("role-student", !isTeacher);
        }

        // Teacher-only controls.
        SetVisible(editContentButton, isTeacher);
        SetVisible(addChapterButton, isTeacher);
        SetVisible(reorderPreview, isTeacher);
        SetVisible(moreButton, isTeacher);

        if (isTeacher)
        {
            SetTeacherUnreadBadge(0);
            SetStudentListUnreadDot(false);
        }

        if (!isTeacher)
        {
            isEditMode = false;

            if (editContentButton != null)
                editContentButton.text = "Chỉnh sửa";
        }

        Debug.Log(
            $"[ClassDetail] Role = {(isTeacher ? "teacher" : "student")}"
        );
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

    private void SetEmptyClassInformation()
    {
        // A newly-created class starts with no students, lessons or scores.
        if (semesterLabel != null)
            semesterLabel.text = "CLASS OVERVIEW";

        if (classTitleLabel != null)
            classTitleLabel.text = "Loading class...";

        if (teacherInitialLabel != null)
            teacherInitialLabel.text = "T";

        if (teacherNameLabel != null)
            teacherNameLabel.text = "Teacher";

        if (teacherPositionLabel != null)
            teacherPositionLabel.text = "Class instructor";

        if (studentCountLabel != null)
            studentCountLabel.text = "0";

        if (moduleCountLabel != null)
            moduleCountLabel.text = "0";

        if (averageScoreLabel != null)
            averageScoreLabel.text = "0%";
    }

    private IEnumerator LoadClassInformationFromSupabase()
    {
        string classId = PlayerPrefs.GetString(
            "selected_class_id",
            string.Empty
        );

        Debug.Log(
            $"[ClassDetail] Loading chapters for class_id = {classId}"
        );

        if (!Guid.TryParse(classId, out _))
        {
            Debug.LogError(
                "[ClassDetailPageController] selected_class_id is invalid."
            );
            yield break;
        }

        ClassDetailStats stats = null;
        string error = null;

        yield return SupabaseClassService.GetClassDetailStats(
            classId,
            result => stats = result,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError(
                "Unable to load class statistics: " + error
            );
            yield break;
        }

        if (stats == null)
            yield break;

        string className = string.IsNullOrWhiteSpace(stats.class_name)
            ? "Untitled Class"
            : stats.class_name.Trim();

        string classCode = string.IsNullOrWhiteSpace(stats.class_code)
            ? string.Empty
            : stats.class_code.Trim();

        if (classTitleLabel != null)
        {
            classTitleLabel.text = string.IsNullOrWhiteSpace(classCode)
                ? className
                : $"{classCode} – {className}";
        }

        string teacherName = string.IsNullOrWhiteSpace(stats.teacher_name)
            ? "Teacher"
            : stats.teacher_name.Trim();

        if (teacherNameLabel != null)
            teacherNameLabel.text = teacherName;

        if (teacherInitialLabel != null)
            teacherInitialLabel.text = GetInitials(teacherName);

        if (studentCountLabel != null)
            studentCountLabel.text = Mathf.Max(0, stats.student_count).ToString();

        if (moduleCountLabel != null)
            moduleCountLabel.text = Mathf.Max(0, stats.lesson_count).ToString();

        if (averageScoreLabel != null)
        {
            averageScoreLabel.text = stats.has_average_score
                ? $"{Mathf.RoundToInt(Mathf.Clamp(stats.average_score, 0f, 100f))}%"
                : "0%";
        }

        // Students need the teacher user id to open the same direct conversation
        // and to display the unread-message badge on the teacher chat icon.
        if (!isTeacher)
            yield return LoadTeacherChatContext(classId, teacherName);
    }

    private IEnumerator LoadTeacherChatContext(string classId, string fallbackTeacherName)
    {
        currentTeacherId = string.Empty;
        currentTeacherConversationId = string.Empty;
        currentTeacherName = string.IsNullOrWhiteSpace(fallbackTeacherName)
            ? "Teacher"
            : fallbackTeacherName.Trim();
        SetTeacherUnreadBadge(0);

        if (runtimeRestService == null)
        {
            Debug.LogError("[ClassDetail] Cannot load teacher chat context: SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string classResponse = null;
        string classError = null;
        string encodedClassId = UnityWebRequest.EscapeURL(classId);

        yield return runtimeRestService.SendJson(
            "GET",
            $"rest/v1/classes?id=eq.{encodedClassId}&select=teacher_id&limit=1",
            null,
            null,
            value => classResponse = value,
            message => classError = message
        );

        if (!string.IsNullOrWhiteSpace(classError))
        {
            Debug.LogError("[ClassDetail] Unable to resolve class teacher: " + classError);
            yield break;
        }

        ClassOwnerRecord[] owners = ParseRestArray<ClassOwnerArray, ClassOwnerRecord>(
            classResponse,
            wrapper => wrapper.items
        );

        if (owners.Length == 0 || !Guid.TryParse(owners[0].teacher_id, out _))
        {
            Debug.LogError("[ClassDetail] Class teacher id (classes.teacher_id) is missing or invalid.");
            yield break;
        }

        currentTeacherId = owners[0].teacher_id;

        // Fetch the canonical teacher name from profiles. If RLS blocks this query,
        // keep the teacher_name already returned by GetClassDetailStats.
        string profileResponse = null;
        string profileError = null;
        yield return runtimeRestService.SendJson(
            "GET",
            $"rest/v1/profiles?id=eq.{UnityWebRequest.EscapeURL(currentTeacherId)}&select=id,full_name,role&limit=1",
            null,
            null,
            value => profileResponse = value,
            message => profileError = message
        );

        if (string.IsNullOrWhiteSpace(profileError))
        {
            ChatProfileRecord[] profiles = ParseRestArray<ChatProfileArray, ChatProfileRecord>(
                profileResponse,
                wrapper => wrapper.items
            );

            if (profiles.Length > 0 && !string.IsNullOrWhiteSpace(profiles[0].full_name))
                currentTeacherName = profiles[0].full_name.Trim();
        }

        yield return ResolveTeacherConversation(classId);
        yield return RefreshTeacherUnreadCount();

        if (teacherUnreadPollingCoroutine != null)
            StopCoroutine(teacherUnreadPollingCoroutine);

        teacherUnreadPollingCoroutine = StartCoroutine(TeacherUnreadPollingLoop());
    }

    private IEnumerator ResolveTeacherConversation(string classId)
    {
        if (!Guid.TryParse(currentTeacherId, out _))
            yield break;

        string response = null;
        string error = null;
        DirectConversationRpcBody body = new()
        {
            p_other_user_id = currentTeacherId,
            p_class_id = classId
        };

        yield return runtimeRestService.SendJson(
            "POST",
            "rest/v1/rpc/get_or_create_direct_conversation",
            JsonUtility.ToJson(body),
            "return=representation",
            value => response = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError("[ClassDetail] Unable to resolve teacher conversation: " + error);
            yield break;
        }

        currentTeacherConversationId = ExtractRpcUuid(response);

        if (!Guid.TryParse(currentTeacherConversationId, out _))
        {
            Debug.LogError("[ClassDetail] Invalid teacher conversation id returned by RPC: " + response);
            currentTeacherConversationId = string.Empty;
        }
    }

    private IEnumerator TeacherUnreadPollingLoop()
    {
        while (!isTeacher && isActiveAndEnabled)
        {
            yield return new WaitForSeconds(TeacherUnreadPollInterval);
            yield return RefreshTeacherUnreadCount();
        }
    }

    private IEnumerator RefreshTeacherUnreadCount()
    {
        if (runtimeRestService == null ||
            string.IsNullOrWhiteSpace(currentTeacherConversationId))
        {
            SetTeacherUnreadBadge(0);
            yield break;
        }

        string currentUserId = GetCurrentUserId();
        if (!Guid.TryParse(currentUserId, out _))
        {
            SetTeacherUnreadBadge(0);
            yield break;
        }

        string response = null;
        string error = null;
        string path =
            $"rest/v1/chat_messages?conversation_id=eq.{UnityWebRequest.EscapeURL(currentTeacherConversationId)}" +
            $"&sender_id=eq.{UnityWebRequest.EscapeURL(currentTeacherId)}" +
            $"&receiver_id=eq.{UnityWebRequest.EscapeURL(currentUserId)}" +
            "&seen_at=is.null&select=id&order=created_at.asc";

        yield return runtimeRestService.SendJson(
            "GET", path, null, null,
            value => response = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogWarning("[ClassDetail] Unable to load unread teacher messages: " + error);
            yield break;
        }

        UnreadMessageRecord[] unread = ParseRestArray<UnreadMessageArray, UnreadMessageRecord>(
            response,
            wrapper => wrapper.items
        );

        SetTeacherUnreadBadge(unread.Length);
    }

    private void SetTeacherUnreadBadge(int count)
    {
        if (teacherMessageUnreadBadge == null)
            return;

        bool visible = !isTeacher && count > 0;
        teacherMessageUnreadBadge.text = count > 99 ? "99+" : Mathf.Max(0, count).ToString();
        teacherMessageUnreadBadge.EnableInClassList(HiddenClass, !visible);
    }

    private static string GetCurrentUserId()
    {
        if (!string.IsNullOrWhiteSpace(SupabaseSession.UserId))
            return SupabaseSession.UserId.Trim();

        string id = PlayerPrefs.GetString("user_id", string.Empty);
        if (string.IsNullOrWhiteSpace(id))
            id = PlayerPrefs.GetString("current_user_id", string.Empty);
        return id?.Trim() ?? string.Empty;
    }

    private static string ExtractRpcUuid(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        string value = response.Trim().Trim('"');
        if (value.StartsWith("[") && value.EndsWith("]"))
            value = value.Trim('[', ']', ' ', '"');
        return value;
    }

    private static TItem[] ParseRestArray<TWrapper, TItem>(
        string json,
        Func<TWrapper, TItem[]> selector
    )
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
            return Array.Empty<TItem>();

        try
        {
            TWrapper wrapper = JsonUtility.FromJson<TWrapper>("{\"items\":" + json + "}");
            return selector(wrapper) ?? Array.Empty<TItem>();
        }
        catch (Exception exception)
        {
            Debug.LogError("[ClassDetail] Unable to parse Supabase REST response: " + exception.Message + "\n" + json);
            return Array.Empty<TItem>();
        }
    }

    private static string GetInitials(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return "T";

        string[] parts = fullName.Split(
            new[] { ' ' },
            StringSplitOptions.RemoveEmptyEntries
        );

        if (parts.Length == 1)
            return parts[0].Substring(0, 1).ToUpperInvariant();

        string first = parts[0].Substring(0, 1);
        string last = parts[parts.Length - 1].Substring(0, 1);
        return (first + last).ToUpperInvariant();
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

        Debug.Log(
            $"[ClassDetail] Supabase returned " +
            $"{(records == null ? 0 : records.Count)} chapter(s)."
        );

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

        int loadedLessonCount = 0;

        foreach (ChapterData loadedChapter in chapters)
        {
            loadedLessonCount +=
                loadedChapter?.Lessons?.Count ?? 0;
        }

        Debug.Log(
            $"[ClassDetail] Rendered {chapters.Count} chapter(s) " +
            $"and {loadedLessonCount} lesson(s)."
        );

        if (chapters.Count == 0)
        {
            Debug.LogWarning(
                "[ClassDetail] No chapters were returned. " +
                "If the class has chapters in Supabase, check the chapters SELECT RLS policy for enrolled students."
            );
        }

        yield return Load3DModelsForCurrentClass();
    }

    private IEnumerator LoadLessonsForChapter(
        ChapterData chapter
    )
    {
        if (chapter == null)
            yield break;

        chapter.Lessons ??= new List<LessonData>();
        chapter.Lessons.Clear();

        if (lessonService == null)
        {
            Debug.LogError(
                "[ClassDetail] Cannot load lessons because SupabaseLessonService is missing."
            );
            yield break;
        }

        if (!Guid.TryParse(chapter.Id, out _))
        {
            Debug.LogWarning(
                $"[ClassDetail] Skip invalid chapter ID: {chapter.Id}"
            );
            yield break;
        }

        List<LessonRecord> records = null;
        string error = null;

        yield return lessonService.GetLessonsByChapter(
            chapter.Id,
            result => records = result,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError(
                $"[ClassDetail] Unable to load lessons for chapter " +
                $"{chapter.Title}: {error}"
            );
            yield break;
        }

        Debug.Log(
            $"[ClassDetail] Supabase returned " +
            $"{(records == null ? 0 : records.Count)} lesson(s) " +
            $"for chapter {chapter.Title} ({chapter.Id})."
        );

        if (records == null)
            yield break;

        foreach (LessonRecord record in records)
        {
            if (record == null ||
                string.IsNullOrWhiteSpace(record.id))
            {
                continue;
            }

            chapter.Lessons.Add(
                new LessonData
                {
                    Id = record.id,
                    Title = string.IsNullOrWhiteSpace(record.title)
                        ? "Untitled Lesson"
                        : record.title.Trim(),
                    IsComplete = false,
                    Has3DContent = false
                }
            );
        }

        chapter.Status = chapter.Lessons.Count > 0
            ? ChapterStatus.InProgress
            : ChapterStatus.Upcoming;

        Debug.Log(
            $"[ClassDetail] Loaded {chapter.Lessons.Count} lesson(s) " +
            $"for chapter {chapter.Title}."
        );
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

        if (isTeacher)
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

        if (isTeacher)
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
    // 3D LABS
    // =========================================================

    private IEnumerator Load3DModelsForCurrentClass()
    {
        if (isLoading3DModels)
            yield break;

        if (lessonService == null)
        {
            Show3DModelsError("SupabaseLessonService is missing.");
            yield break;
        }

        string selectedClassId = PlayerPrefs.GetString(
            "selected_class_id",
            string.Empty
        );

        if (!Guid.TryParse(selectedClassId, out _))
        {
            Show3DModelsError("selected_class_id is invalid.");
            yield break;
        }

        isLoading3DModels = true;
        class3DModels.Clear();
        modelCardList?.Clear();

        SetVisible(labsLoadingState, true);
        SetVisible(labsEmptyState, false);
        SetVisible(labsErrorState, false);

        foreach (ChapterData chapter in chapters)
        {
            if (chapter == null ||
                !string.Equals(
                    chapter.ClassId,
                    selectedClassId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (LessonData lesson in chapter.Lessons)
            {
                if (lesson == null || !Guid.TryParse(lesson.Id, out _))
                    continue;

                List<LessonAssetRecord> assets = null;
                string error = null;

                yield return lessonService.GetLessonAssetsByLesson(
                    lesson.Id,
                    result => assets = result,
                    message => error = message
                );

                if (!string.IsNullOrWhiteSpace(error))
                {
                    isLoading3DModels = false;
                    Show3DModelsError(
                        $"Cannot load models for {lesson.Title}: {error}"
                    );
                    yield break;
                }

                if (assets == null)
                    continue;

                foreach (LessonAssetRecord asset in assets)
                {
                    if (!Is3DModelAsset(asset))
                        continue;

                    class3DModels.Add(
                        new Class3DModelData
                        {
                            asset_id = asset.id,
                            lesson_id = lesson.Id,
                            lesson_title = lesson.Title,
                            chapter_id = chapter.Id,
                            chapter_title = chapter.Title,
                            chapter_order = chapter.Order,
                            file_name = asset.file_name,
                            storage_bucket = asset.storage_bucket,
                            storage_path = asset.storage_path,
                            mime_type = asset.mime_type,
                            file_extension = asset.file_extension,
                            file_size_bytes = asset.file_size_bytes,
                            display_order = asset.display_order
                        }
                    );
                }
            }
        }

        class3DModels.Sort((left, right) =>
        {
            int chapterCompare =
                left.chapter_order.CompareTo(right.chapter_order);

            if (chapterCompare != 0)
                return chapterCompare;

            int lessonCompare = string.Compare(
                left.lesson_title,
                right.lesson_title,
                StringComparison.OrdinalIgnoreCase
            );

            return lessonCompare != 0
                ? lessonCompare
                : left.display_order.CompareTo(right.display_order);
        });

        isLoading3DModels = false;
        Render3DModelCards();
    }

    private void RetryLoad3DModels()
    {
        if (!isLoading3DModels)
            StartCoroutine(Load3DModelsForCurrentClass());
    }

    private void Render3DModelCards()
    {
        modelCardList?.Clear();

        SetVisible(labsLoadingState, false);
        SetVisible(labsErrorState, false);
        SetVisible(labsEmptyState, class3DModels.Count == 0);

        if (modelCardList == null)
            return;

        foreach (Class3DModelData model in class3DModels)
        {
            modelCardList.Add(Create3DModelCard(model));
        }
    }

    private VisualElement Create3DModelCard(Class3DModelData model)
    {
        VisualElement card = new();
        card.AddToClassList("model-card");

        VisualElement iconContainer = new();
        iconContainer.AddToClassList("model-icon-container");

        VisualElement icon = new();
        icon.AddToClassList("model-icon");
        iconContainer.Add(icon);

        VisualElement information = new();
        information.AddToClassList("model-information");

        Label title = new(GetModelDisplayName(model.file_name));
        title.AddToClassList("model-title");

        string contextText =
            $"{model.lesson_title} · Chapter {Mathf.Max(1, model.chapter_order)}";

        Label context = new(contextText);
        context.AddToClassList("model-context");

        Label badge = new("◉  Launch 3D Viewer");
        badge.AddToClassList("model-launch-badge");

        information.Add(title);
        information.Add(context);
        information.Add(badge);

        Label arrow = new("›");
        arrow.AddToClassList("model-card-arrow");

        card.Add(iconContainer);
        card.Add(information);
        card.Add(arrow);

        card.RegisterCallback<ClickEvent>(_ => Open3DModel(model));

        return card;
    }

    private void Open3DModel(Class3DModelData model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.storage_path))
        {
            Debug.LogError("The selected 3D model has no storage_path.");
            return;
        }

        string modelUrl = r2StorageService != null
            ? r2StorageService.BuildFullR2Url(model.storage_path)
            : model.storage_path;

        PlayerPrefs.SetString("selected_model_asset_id", model.asset_id ?? "");
        PlayerPrefs.SetString("selected_model_name", model.file_name ?? "");
        PlayerPrefs.SetString("selected_model_path", model.storage_path ?? "");
        PlayerPrefs.SetString("selected_model_url", modelUrl ?? "");
        PlayerPrefs.SetString("selected_lesson_id", model.lesson_id ?? "");
        PlayerPrefs.SetString("selected_chapter_id", model.chapter_id ?? "");
        PlayerPrefs.SetString("previous_scene", "ClassDetailScene");
        PlayerPrefs.Save();

        const string sceneName = "ARScene";

        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneHistory.LoadScene(sceneName);
        }
        else
        {
            Debug.LogWarning(
                $"{sceneName} is not included in Build Profiles. " +
                $"Selected model URL: {modelUrl}"
            );
        }
    }

    private void Show3DModelsError(string message)
    {
        isLoading3DModels = false;

        SetVisible(labsLoadingState, false);
        SetVisible(labsEmptyState, false);
        SetVisible(labsErrorState, true);

        if (labsErrorLabel != null)
            labsErrorLabel.text = message ?? "Unknown error.";
    }

    private static bool Is3DModelAsset(LessonAssetRecord asset)
    {
        if (asset == null)
            return false;

        string type = asset.asset_type?.Trim().ToLowerInvariant() ?? "";
        string extension = asset.file_extension?.Trim().TrimStart('.').ToLowerInvariant() ?? "";
        string mime = asset.mime_type?.Trim().ToLowerInvariant() ?? "";

        return type.Contains("3d") ||
               type.Contains("model") ||
               extension == "glb" ||
               extension == "gltf" ||
               mime.Contains("gltf") ||
               mime.Contains("model/");
    }

    private static string GetModelDisplayName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "Untitled 3D Model";

        string name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        name = name.Replace('_', ' ').Replace('-', ' ').Trim();

        return string.IsNullOrWhiteSpace(name)
            ? "Untitled 3D Model"
            : name;
    }


    // =========================================================
    // TEACHER: UNREAD STUDENT CHAT NOTIFICATIONS
    // =========================================================

    private IEnumerator TeacherStudentUnreadPollingLoop()
    {
        // Refresh immediately so the teacher does not need to open Student List
        // before seeing its red notification dot.
        yield return RefreshTeacherStudentUnreadState();

        while (isTeacher && isActiveAndEnabled)
        {
            yield return new WaitForSeconds(StudentUnreadPollInterval);
            yield return RefreshTeacherStudentUnreadState();
        }
    }

    private IEnumerator RefreshTeacherStudentUnreadState()
    {
        if (!isTeacher || runtimeRestService == null)
            yield break;

        string teacherId = GetCurrentUserId();
        string classId = PlayerPrefs.GetString("selected_class_id", string.Empty);

        if (!Guid.TryParse(teacherId, out _) ||
            !Guid.TryParse(classId, out _))
        {
            SetStudentListUnreadDot(false);
            yield break;
        }

        string conversationResponse = null;
        string conversationError = null;

        string encodedTeacherId = UnityWebRequest.EscapeURL(teacherId);
        string encodedClassId = UnityWebRequest.EscapeURL(classId);

        // Only conversations belonging to the currently opened class and
        // containing this teacher are considered.
        string conversationPath =
            $"rest/v1/chat_conversations?class_id=eq.{encodedClassId}" +
            $"&or=(user_a_id.eq.{encodedTeacherId},user_b_id.eq.{encodedTeacherId})" +
            "&select=id,class_id,user_a_id,user_b_id";

        yield return runtimeRestService.SendJson(
            "GET",
            conversationPath,
            null,
            null,
            value => conversationResponse = value,
            message => conversationError = message
        );

        if (!string.IsNullOrWhiteSpace(conversationError))
        {
            Debug.LogWarning(
                "[ClassDetail] Unable to load teacher chat conversations: " +
                conversationError
            );
            yield break;
        }

        TeacherConversationRecord[] conversations =
            ParseRestArray<TeacherConversationArray, TeacherConversationRecord>(
                conversationResponse,
                wrapper => wrapper.items
            );

        Dictionary<string, string> conversationToStudent =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (TeacherConversationRecord conversation in conversations)
        {
            if (conversation == null ||
                !Guid.TryParse(conversation.id, out _))
            {
                continue;
            }

            string studentId = string.Equals(
                conversation.user_a_id,
                teacherId,
                StringComparison.OrdinalIgnoreCase)
                    ? conversation.user_b_id
                    : conversation.user_a_id;

            if (!Guid.TryParse(studentId, out _))
                continue;

            conversationToStudent[conversation.id] = studentId;
        }

        Dictionary<string, int> nextCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        List<string> unreadIds = new List<string>();

        if (conversationToStudent.Count > 0)
        {
            string unreadResponse = null;
            string unreadError = null;

            // One request gets every unread message addressed to this teacher.
            // We then filter it to conversations from the current class.
            string unreadPath =
                $"rest/v1/chat_messages?receiver_id=eq.{encodedTeacherId}" +
                "&seen_at=is.null" +
                "&select=id,conversation_id,sender_id,receiver_id" +
                "&order=created_at.asc";

            yield return runtimeRestService.SendJson(
                "GET",
                unreadPath,
                null,
                null,
                value => unreadResponse = value,
                message => unreadError = message
            );

            if (!string.IsNullOrWhiteSpace(unreadError))
            {
                Debug.LogWarning(
                    "[ClassDetail] Unable to load unread student messages: " +
                    unreadError
                );
                yield break;
            }

            TeacherUnreadMessageRecord[] unreadMessages =
                ParseRestArray<TeacherUnreadMessageArray, TeacherUnreadMessageRecord>(
                    unreadResponse,
                    wrapper => wrapper.items
                );

            foreach (TeacherUnreadMessageRecord message in unreadMessages)
            {
                if (message == null ||
                    string.IsNullOrWhiteSpace(message.id) ||
                    string.IsNullOrWhiteSpace(message.conversation_id))
                {
                    continue;
                }

                if (!conversationToStudent.TryGetValue(
                        message.conversation_id,
                        out string studentId))
                {
                    // Unread message belongs to another class/conversation.
                    continue;
                }

                // Extra safety: the sender must be the student counterpart.
                if (!string.Equals(
                        message.sender_id,
                        studentId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                nextCounts.TryGetValue(studentId, out int count);
                nextCounts[studentId] = count + 1;
                unreadIds.Add(message.id);
            }
        }

        unreadIds.Sort(StringComparer.Ordinal);
        string nextSignature = string.Join("|", unreadIds);

        bool countsChanged = !UnreadCountMapsEqual(
            studentUnreadCounts,
            nextCounts
        );

        studentUnreadCounts.Clear();
        foreach (KeyValuePair<string, int> pair in nextCounts)
            studentUnreadCounts[pair.Key] = pair.Value;

        latestStudentUnreadSignature = nextSignature;

        if (studentUnreadCounts.Count == 0)
        {
            // Reset acknowledgement after everything has actually been read.
            acknowledgedStudentUnreadSignature = string.Empty;
            SetStudentListUnreadDot(false);
        }
        else
        {
            // If Student List has already acknowledged the current set, keep the
            // tab dot hidden. Any newly arrived unread message changes the signature
            // and causes the dot to appear again.
            bool hasNewAttention =
                !string.Equals(
                    latestStudentUnreadSignature,
                    acknowledgedStudentUnreadSignature,
                    StringComparison.Ordinal
                );

            SetStudentListUnreadDot(hasNewAttention);
        }

        // Student cards are dynamic C# UI, so re-render only when a student's
        // unread count actually changed.
        if (countsChanged && hasLoadedStudents)
            RenderStudentCards();
    }

    private int GetStudentUnreadCount(string studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            return 0;

        return studentUnreadCounts.TryGetValue(studentId, out int count)
            ? Mathf.Max(0, count)
            : 0;
    }

    private static bool UnreadCountMapsEqual(
        Dictionary<string, int> left,
        Dictionary<string, int> right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left == null || right == null || left.Count != right.Count)
            return false;

        foreach (KeyValuePair<string, int> pair in left)
        {
            if (!right.TryGetValue(pair.Key, out int rightValue) ||
                rightValue != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private void SetStudentListUnreadDot(bool visible)
    {
        if (studentListUnreadDot == null)
            return;

        studentListUnreadDot.EnableInClassList(
            HiddenClass,
            !isTeacher || !visible
        );
    }

    private void AcknowledgeStudentListNotificationDot()
    {
        if (!isTeacher)
            return;

        acknowledgedStudentUnreadSignature =
            latestStudentUnreadSignature ?? string.Empty;

        SetStudentListUnreadDot(false);
    }

    // =========================================================
    // STUDENT LIST
    // =========================================================

    private IEnumerator LoadStudentsForCurrentClass()
    {
        if (isLoadingStudents)
            yield break;

        string classId = PlayerPrefs.GetString(
            "selected_class_id",
            string.Empty
        );

        if (!Guid.TryParse(classId, out _))
        {
            ShowStudentListError("selected_class_id is invalid.");
            yield break;
        }

        isLoadingStudents = true;
        hasLoadedStudents = false;
        enrolledStudents.Clear();
        studentCardList?.Clear();

        SetVisible(studentListLoadingState, true);
        SetVisible(studentListEmptyState, false);
        SetVisible(studentListErrorState, false);

        ClassMemberStudent[] students = null;
        string error = null;

        yield return SupabaseClassService.GetClassEnrolledStudents(
            classId,
            result => students = result,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            isLoadingStudents = false;
            ShowStudentListError(error);
            yield break;
        }

        if (students != null)
        {
            foreach (ClassMemberStudent student in students)
            {
                if (student == null || !Guid.TryParse(student.user_id, out _))
                    continue;

                // Keep EVERY enrolled student in the list, including the
                // currently logged-in student. The current student will be
                // rendered first and without a message button.
                UserPresenceRecord presence = null;
                string presenceError = null;

                yield return SupabaseClassService.GetUserPresence(
                    student.user_id,
                    result => presence = result,
                    message => presenceError = message
                );

                // Presence is optional. The card must still render even if
                // user_presence has no row or its RLS does not allow reading.
                if (!string.IsNullOrWhiteSpace(presenceError))
                {
                    Debug.LogWarning(
                        $"[ClassDetail] Presence unavailable for " +
                        $"{student.user_id}: {presenceError}"
                    );
                }

                if (presence != null)
                {
                    student.is_online = presence.is_online;
                    student.last_seen_at = !string.IsNullOrWhiteSpace(
                        presence.last_seen_at
                    )
                        ? presence.last_seen_at
                        : presence.updated_at;
                }

                enrolledStudents.Add(student);
            }
        }

        isLoadingStudents = false;
        hasLoadedStudents = true;
        RenderStudentCards();

        Debug.Log(
            $"[ClassDetail] Rendered {enrolledStudents.Count} enrolled student(s)."
        );
    }

    private void RetryLoadStudents()
    {
        if (!isLoadingStudents)
            StartCoroutine(LoadStudentsForCurrentClass());
    }

    private void ShowStudentListError(string message)
    {
        isLoadingStudents = false;
        hasLoadedStudents = false;

        SetVisible(studentListLoadingState, false);
        SetVisible(studentListEmptyState, false);
        SetVisible(studentListErrorState, true);

        if (studentListErrorLabel != null)
        {
            studentListErrorLabel.text =
                string.IsNullOrWhiteSpace(message)
                    ? "Unable to load students."
                    : message;
        }
    }

    private void RenderStudentCards()
    {
        studentCardList?.Clear();

        SetVisible(studentListLoadingState, false);
        SetVisible(studentListErrorState, false);
        SetVisible(studentListEmptyState, enrolledStudents.Count == 0);

        int onlineCount = 0;
        string currentUserId = GetCurrentUserId();

        // Render a copy so the backend-loaded order remains untouched.
        List<ClassMemberStudent> orderedStudents =
            new List<ClassMemberStudent>(enrolledStudents);

        orderedStudents.Sort((left, right) =>
        {
            if (left == null && right == null) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            bool leftIsCurrent =
                !string.IsNullOrWhiteSpace(currentUserId) &&
                string.Equals(
                    left.user_id,
                    currentUserId,
                    StringComparison.OrdinalIgnoreCase
                );

            bool rightIsCurrent =
                !string.IsNullOrWhiteSpace(currentUserId) &&
                string.Equals(
                    right.user_id,
                    currentUserId,
                    StringComparison.OrdinalIgnoreCase
                );

            // Logged-in student is always first.
            if (leftIsCurrent && !rightIsCurrent) return -1;
            if (!leftIsCurrent && rightIsCurrent) return 1;

            string leftName =
                left.profiles != null
                    ? left.profiles.full_name ?? string.Empty
                    : string.Empty;

            string rightName =
                right.profiles != null
                    ? right.profiles.full_name ?? string.Empty
                    : string.Empty;

            return string.Compare(
                leftName,
                rightName,
                StringComparison.OrdinalIgnoreCase
            );
        });

        foreach (ClassMemberStudent student in orderedStudents)
        {
            if (student == null)
                continue;

            if (student.is_online)
                onlineCount++;

            bool isCurrentStudent =
                !string.IsNullOrWhiteSpace(currentUserId) &&
                string.Equals(
                    student.user_id,
                    currentUserId,
                    StringComparison.OrdinalIgnoreCase
                );

            studentCardList?.Add(
                CreateStudentCard(student, isCurrentStudent)
            );
        }

        if (studentListCountLabel != null)
        {
            studentListCountLabel.text =
                $"{enrolledStudents.Count} ENROLLED " +
                $"{(enrolledStudents.Count == 1 ? "STUDENT" : "STUDENTS")}";
        }

        if (studentOnlineCountLabel != null)
            studentOnlineCountLabel.text = $"{onlineCount} Online";

        if (studentCountLabel != null)
            studentCountLabel.text = enrolledStudents.Count.ToString();
    }

    private VisualElement CreateStudentCard(
        ClassMemberStudent student,
        bool isCurrentStudent
    )
    {
        string fullName =
            student.profiles != null &&
            !string.IsNullOrWhiteSpace(student.profiles.full_name)
                ? student.profiles.full_name.Trim()
                : "Student";

        VisualElement card = new();
        card.AddToClassList("student-card");

        VisualElement avatarWrap = new();
        avatarWrap.AddToClassList("student-avatar-wrap");

        VisualElement avatar = new();
        avatar.AddToClassList("student-avatar");

        Label initials = new(GetInitials(fullName));
        initials.AddToClassList("student-avatar-initials");
        avatar.Add(initials);

        VisualElement presenceDot = new();
        presenceDot.AddToClassList("student-presence-dot");
        presenceDot.EnableInClassList(
            "student-presence-dot-online",
            student.is_online
        );

        avatarWrap.Add(avatar);
        avatarWrap.Add(presenceDot);

        VisualElement information = new();
        information.AddToClassList("student-information");

        Label nameLabel = new(fullName);
        nameLabel.AddToClassList("student-name-label");

        Label activityLabel = new(GetStudentActivityText(student));
        activityLabel.AddToClassList("student-last-active-label");

        Label enrollmentLabel = new(
            GetEnrollmentText(student.joined_at)
        );
        enrollmentLabel.AddToClassList("student-enrollment-label");

        information.Add(nameLabel);
        information.Add(activityLabel);
        information.Add(enrollmentLabel);

        card.Add(avatarWrap);
        card.Add(information);

        // Only OTHER students get a message button.
        // The logged-in student's own row remains visible (and is rendered first),
        // but there is intentionally no self-chat action.
        if (!isCurrentStudent)
        {
            Button messageButton = new();
            messageButton.AddToClassList("student-message-button");
            messageButton.tooltip = $"Message {fullName}";

            VisualElement messageIcon = new();
            messageIcon.AddToClassList("student-message-icon");
            messageButton.Add(messageIcon);

            int unreadCount = GetStudentUnreadCount(student.user_id);
            if (unreadCount > 0)
            {
                Label unreadBadge = new(
                    unreadCount > 99 ? "99+" : unreadCount.ToString()
                );
                unreadBadge.AddToClassList("student-message-unread-badge");
                unreadBadge.pickingMode = PickingMode.Ignore;
                messageButton.Add(unreadBadge);
            }

            messageButton.clicked += () =>
                OpenChatForStudent(student, fullName);

            card.Add(messageButton);
        }

        return card;
    }

    private static string GetStudentActivityText(
        ClassMemberStudent student
    )
    {
        if (student == null)
            return "Offline";

        if (student.is_online)
            return "Active now";

        if (!TryParseSupabaseDate(
                student.last_seen_at,
                out DateTime lastSeen))
        {
            return "Offline";
        }

        TimeSpan elapsed = DateTime.UtcNow - lastSeen.ToUniversalTime();

        if (elapsed.TotalMinutes < 1)
            return "Active just now";

        if (elapsed.TotalMinutes < 60)
            return $"Last active {Mathf.Max(1, (int)elapsed.TotalMinutes)} min ago";

        if (elapsed.TotalHours < 24)
            return $"Last active {Mathf.Max(1, (int)elapsed.TotalHours)} hours ago";

        return $"Last active {Mathf.Max(1, (int)elapsed.TotalDays)} days ago";
    }

    private static string GetEnrollmentText(string joinedAt)
    {
        if (!TryParseSupabaseDate(joinedAt, out DateTime joined))
            return "Enrolled student";

        return $"Enrolled {joined.ToLocalTime():dd/MM/yyyy}";
    }

    private static bool TryParseSupabaseDate(
        string value,
        out DateTime dateTime)
    {
        return DateTime.TryParse(
            value,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out dateTime
        );
    }

    private void OpenChatForStudent(
        ClassMemberStudent student,
        string fullName
    )
    {
        if (student == null ||
            !Guid.TryParse(student.user_id, out _))
        {
            Debug.LogError(
                "[ClassDetail] Cannot open ChatScene: invalid student user_id."
            );
            return;
        }

        string currentUserId = GetCurrentUserId();

        if (!string.IsNullOrWhiteSpace(currentUserId) &&
            string.Equals(
                student.user_id,
                currentUserId,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            Debug.LogWarning(
                "[ClassDetail] Ignored self-chat request. " +
                "A user cannot create a direct conversation with themselves."
            );
            return;
        }

        PlayerPrefs.SetString(
            "selected_chat_user_id",
            student.user_id
        );

        PlayerPrefs.SetString(
            "selected_chat_user_name",
            fullName ?? "Student"
        );

        PlayerPrefs.SetString(
            "selected_chat_user_role",
            "student"
        );

        PlayerPrefs.SetString(
            "previous_scene",
            "ClassDetailScene"
        );

        // Force ChatScene to resolve/create the correct direct conversation.
        PlayerPrefs.DeleteKey(
            "selected_chat_conversation_id"
        );

        PlayerPrefs.Save();

        const string sceneName = "ChatScene";

        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneHistory.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError(
                "ChatScene is not included in Build Profiles / Scene List."
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

        if (!isLoading3DModels &&
            class3DModels.Count == 0 &&
            chapters.Count > 0)
        {
            StartCoroutine(Load3DModelsForCurrentClass());
        }
    }

    private void ShowStudentListTab()
    {
        SetActiveTab(
            studentListTabButton,
            studentListContent
        );

        // Teacher has explicitly checked the Student List.
        // Hide the tab-level attention dot for the unread set that currently
        // exists. Per-student unread badges remain until ChatScene marks those
        // messages seen.
        if (isTeacher)
            AcknowledgeStudentListNotificationDot();

        if (!hasLoadedStudents && !isLoadingStudents)
        {
            StartCoroutine(LoadStudentsForCurrentClass());
        }
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
        // Không LoadScene("MyClassesScene") trực tiếp.
        // SceneHistory sẽ pop đúng Scene trước đó trong stack.
        SceneHistory.GoBack("MyClassesScene");
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
        // This header button is the student -> class teacher entry point.
        if (isTeacher)
        {
            Debug.Log("[ClassDetail] Teacher header chat button ignored for teacher role.");
            return;
        }

        if (!Guid.TryParse(currentTeacherId, out _))
        {
            Debug.LogError("[ClassDetail] Cannot open ChatScene: teacher id has not been resolved yet.");
            return;
        }

        PlayerPrefs.SetString("selected_chat_user_id", currentTeacherId);
        PlayerPrefs.SetString("selected_chat_user_name", currentTeacherName ?? "Teacher");
        PlayerPrefs.SetString("selected_chat_user_role", "teacher");
        PlayerPrefs.SetString("previous_scene", "ClassDetailScene");

        // Reuse the exact class-scoped conversation already resolved for the badge.
        // If it is unavailable, ChatPageController will call the same RPC itself.
        if (Guid.TryParse(currentTeacherConversationId, out _))
            PlayerPrefs.SetString("selected_chat_conversation_id", currentTeacherConversationId);
        else
            PlayerPrefs.DeleteKey("selected_chat_conversation_id");

        PlayerPrefs.Save();

        const string sceneName = "ChatScene";
        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneHistory.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError("ChatScene is not included in Build Profiles / Scene List.");
        }
    }

    private void OnAddChapterClicked()
    {
        if (!isTeacher || isCreatingChapter)
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
        if (!isTeacher)
            return;

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
            SceneHistory.LoadScene(sceneName);
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
            SceneHistory.LoadScene("CreateLessonScene");
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
            SceneHistory.LoadScene(sceneName);
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