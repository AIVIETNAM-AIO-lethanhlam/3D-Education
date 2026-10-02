using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(SupabaseRuntimeRestService))]
public class StartQuizPageController : MonoBehaviour
{
    [Header("UI Document")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Navigation")]
    [SerializeField] private string fallbackBackScene = "ShowLessonScene";
    [SerializeField] private string quizSceneName = "DoQuizScene";

    // Keep the scene that originally opened StartQuizScene separate from
    // "previous_scene", because DoQuizScene also uses "previous_scene".
    private const string StartQuizOriginSceneKey = "start_quiz_origin_scene";
    private const string PreviousSceneKey = "previous_scene";

    [Header("Demo Data")]
    [SerializeField] private string lessonName = "Lesson 8";
    [SerializeField] private string quizHeaderTitle = "Quiz 01";
    [SerializeField] private string quizTitle =
        "Quiz 01: Fundamentals of Circuits";

    [SerializeField] private string quizSubtitle =
        "Electronics · Chapter 1–3";

    [SerializeField] private string openTime =
        "Oct 25, 2026 · 08:00 AM";

    [SerializeField] private string closeTime =
        "Oct 27, 2026 · 11:59 PM";

    [SerializeField] private int totalQuestions = 5;
    [SerializeField] private float maximumGrade = 10f;

    [Header("Quiz Availability")]
    [SerializeField] private bool checkAvailabilityByDate = true;
    [SerializeField] private string openDateIso = "2026-10-25T08:00:00";
    [SerializeField] private string closeDateIso = "2026-10-27T23:59:00";

    private VisualElement root;
    private ScrollView quizScrollView;
    private SupabaseRuntimeRestService restService;

    private Button backButton;
    private Button startQuizButton;
    private Button cancelStartButton;
    private Button confirmStartButton;

    private Label lessonLabel;
    private Label headerQuizTitle;
    private Label statusLabel;
    private Label quizTitleLabel;
    private Label quizSubtitleLabel;
    private Label openTimeLabel;
    private Label closeTimeLabel;
    private Label questionCountLabel;
    private Label maximumGradeLabel;
    private Label quizTypeLabel;
    private Label opensLabel;
    private Label closesLabel;
    private Label questionsLabel;
    private Label maximumGradeTitleLabel;
    private Label attemptHistoryTitleLabel;
    private Label noticeTitleLabel;
    private Label noticeDescriptionLabel;
    private Label confirmationTitleLabel;
    private Label confirmationMessageLabel;

    private VisualElement statusBadge;
    private VisualElement confirmationOverlay;
    private VisualElement attemptHistorySection;
    private VisualElement attemptHistoryContainer;
    private VisualElement noticeCard;

    private bool isStartingQuiz;
    private QuizAttemptView[] loadedAttempts = Array.Empty<QuizAttemptView>();
    private Coroutine availabilityMonitor;

    // Teacher mode (2026-10): the teacher sees the answer key and can edit the questions
    // instead of starting an attempt.
    private bool isTeacherMode;
    private TeacherQuizEditor teacherEditor;

    private void Awake()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        restService = GetComponent<SupabaseRuntimeRestService>();
    }

    private Button quizReportButton;

    private void AddQuizReportButton()
    {
        bool isTeacher = string.Equals(
            PlayerPrefs.GetString("current_role", "student"), "teacher", StringComparison.OrdinalIgnoreCase);

        string quizId = PlayerPrefs.GetString("selected_quiz_id", string.Empty);
        string lessonId = PlayerPrefs.GetString("selected_lesson_id", string.Empty);

        if (isTeacher || SupabaseSession.IsAdmin || quizReportButton != null || statusBadge?.parent == null ||
            !Guid.TryParse(quizId, out _) || !Guid.TryParse(lessonId, out _))
            return;

        ModerationReportSheet.EnsureStyles(root);
        quizReportButton = ModerationReportSheet.CreateReportButton(() =>
            ModerationReportSheet.Show(root, this, new[]
            {
                new ModerationReportSheet.Target
                {
                    TargetType = "quiz",
                    LessonId = lessonId,
                    QuizId = quizId,
                    Label = "Quiz"
                }
            }));
        statusBadge.parent.Add(quizReportButton);
    }

    private void OnEnable()
    {
        if (uiDocument == null)
        {
            Debug.LogError(
                "[StartQuizPageController] UIDocument is not assigned."
            );
            return;
        }

        root = uiDocument.rootVisualElement;

        CacheStartQuizOriginScene();

        FindElements();
        HideScrollbars();
        RegisterEvents();
        AppLanguageManager.LanguageChanged += OnLanguageChanged;
        LoadQuizData();

        isTeacherMode = IsTeacherAccount();
        if (isTeacherMode)
            SetupTeacherMode();

        ApplyCurrentLanguage();
        RefreshAvailability();
        availabilityMonitor = StartCoroutine(MonitorQuizAvailability());
        StartCoroutine(RefreshQuizMetadataRoutine());

        // Teachers do not take the quiz, so they have no attempt history here.
        if (!isTeacherMode)
            StartCoroutine(LoadAttemptHistoryRoutine());
    }

    private static bool IsTeacherAccount()
    {
        return string.Equals(
            PlayerPrefs.GetString("current_role", "student"),
            "teacher",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Teacher view: hide the Start button and the notice, and show every question with its
    /// answer key in an editable list (TeacherQuizEditor). The RPCs used by the editor only
    /// accept the teacher who owns the quiz.
    /// </summary>
    private void SetupTeacherMode()
    {
        root.Q<VisualElement>(className: "bottom-area")?.AddToClassList("hidden");
        noticeCard?.AddToClassList("hidden");
        HideConfirmation();
        HideAttemptHistory();

        if (teacherEditor != null)
            return;

        VisualElement quizCard = root.Q<VisualElement>(className: "quiz-card");
        VisualElement container = quizCard?.parent ?? root.Q<VisualElement>(className: "content-container");
        if (container == null)
            return;

        string quizId = PlayerPrefs.GetString("selected_quiz_id", string.Empty);
        teacherEditor = new TeacherQuizEditor(this, restService, container, quizCard, quizId);
        teacherEditor.Load();
    }

    private void OnDisable()
    {
        if (availabilityMonitor != null)
        {
            StopCoroutine(availabilityMonitor);
            availabilityMonitor = null;
        }

        AppLanguageManager.LanguageChanged -= OnLanguageChanged;
        UnregisterEvents();
    }

    private void FindElements()
    {
        quizScrollView = root.Q<ScrollView>("content-scroll");
        backButton = root.Q<Button>("back-button");
        startQuizButton = root.Q<Button>("start-quiz-button");
        cancelStartButton = root.Q<Button>("cancel-start-button");
        confirmStartButton = root.Q<Button>("confirm-start-button");

        lessonLabel = root.Q<Label>("lesson-label");
        headerQuizTitle = root.Q<Label>("header-quiz-title");
        statusLabel = root.Q<Label>("status-label");

        quizTitleLabel = root.Q<Label>("quiz-title-label");
        quizSubtitleLabel = root.Q<Label>("quiz-subtitle-label");

        openTimeLabel = root.Q<Label>("open-time-label");
        closeTimeLabel = root.Q<Label>("close-time-label");

        questionCountLabel = root.Q<Label>(
            "question-count-label"
        );

        maximumGradeLabel = root.Q<Label>(
            "maximum-grade-label"
        );

        quizTypeLabel = root.Q<Label>("quiz-type-label");
        opensLabel = root.Q<Label>("opens-label");
        closesLabel = root.Q<Label>("closes-label");
        questionsLabel = root.Q<Label>("questions-label");
        maximumGradeTitleLabel = root.Q<Label>("maximum-grade-title-label");
        attemptHistoryTitleLabel = root.Q<Label>("attempt-history-title-label");
        noticeTitleLabel = root.Q<Label>("notice-title-label");
        noticeDescriptionLabel = root.Q<Label>("notice-description");
        confirmationTitleLabel = root.Q<Label>("confirmation-title-label");
        confirmationMessageLabel = root.Q<Label>("confirmation-message-label");

        statusBadge = root.Q<VisualElement>("status-badge");

        // Students can report this quiz to the admin (2026-09).
        AddQuizReportButton();

        confirmationOverlay = root.Q<VisualElement>(
            "confirmation-overlay"
        );

        attemptHistorySection = root.Q<VisualElement>(
            "attempt-history-section"
        );

        attemptHistoryContainer = root.Q<VisualElement>(
            "attempt-history-container"
        );

        noticeCard = root.Q<VisualElement>("notice-card");
    }

    private void HideScrollbars()
    {
        if (quizScrollView == null)
        {
            return;
        }

        quizScrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        quizScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
    }

    private void RegisterEvents()
    {
        if (backButton != null)
        {
            backButton.clicked += HandleBackClicked;
        }

        if (startQuizButton != null)
        {
            startQuizButton.clicked += HandleStartQuizClicked;
        }

        if (cancelStartButton != null)
        {
            cancelStartButton.clicked += HideConfirmation;
        }

        if (confirmStartButton != null)
        {
            confirmStartButton.clicked += ConfirmStartQuiz;
        }
    }

    private void UnregisterEvents()
    {
        if (backButton != null)
        {
            backButton.clicked -= HandleBackClicked;
        }

        if (startQuizButton != null)
        {
            startQuizButton.clicked -= HandleStartQuizClicked;
        }

        if (cancelStartButton != null)
        {
            cancelStartButton.clicked -= HideConfirmation;
        }

        if (confirmStartButton != null)
        {
            confirmStartButton.clicked -= ConfirmStartQuiz;
        }
    }

    private static string T(string english, string vietnamese) =>
        AppLanguageManager.IsVietnamese ? vietnamese : english;

    private static readonly Dictionary<string, string> StaticVietnameseText = new()
    {
        { "MULTIPLE CHOICE QUIZ", "BÀI KIỂM TRA TRẮC NGHIỆM" },
        { "Opens", "Mở lúc" },
        { "Closes", "Đóng lúc" },
        { "Questions", "Số câu hỏi" },
        { "Maximum Grade", "Điểm tối đa" },
        { "ATTEMPT HISTORY", "LỊCH SỬ LÀM BÀI" },
        { "Before you begin", "Trước khi bắt đầu" },
        { "Once started, the quiz must be completed in one session. Make sure you have a stable internet connection before clicking “Start Quiz”.", "Sau khi bắt đầu, bạn cần hoàn thành bài kiểm tra trong một lần. Hãy bảo đảm kết nối Internet ổn định trước khi nhấn “Bắt đầu”." },
        { "Start Quiz?", "Bắt đầu bài kiểm tra?" },
        { "The quiz timer will begin immediately after you start.", "Thời gian làm bài sẽ bắt đầu ngay khi bạn xác nhận." },
        { "Cancel", "Hủy" },
        { "Start", "Bắt đầu" }
    };

    private static void SetText(Label label, string english, string vietnamese)
    {
        if (label != null)
            label.text = T(english, vietnamese);
    }

    private void OnLanguageChanged(string language)
    {
        ApplyCurrentLanguage();
    }

    private void ApplyCurrentLanguage()
    {
        ApplyStaticTranslationsRecursive(root);

        SetText(quizTypeLabel, "MULTIPLE CHOICE QUIZ", "BÀI KIỂM TRA TRẮC NGHIỆM");
        SetText(opensLabel, "Opens", "Mở lúc");
        SetText(closesLabel, "Closes", "Đóng lúc");
        SetText(questionsLabel, "Questions", "Số câu hỏi");
        SetText(maximumGradeTitleLabel, "Maximum Grade", "Điểm tối đa");
        SetText(attemptHistoryTitleLabel, "ATTEMPT HISTORY", "LỊCH SỬ LÀM BÀI");
        SetText(noticeTitleLabel, "Before you begin", "Trước khi bắt đầu");
        SetText(
            noticeDescriptionLabel,
            "Once started, the quiz must be completed in one session. Make sure you have a stable internet connection before clicking “Start Quiz”.",
            "Sau khi bắt đầu, bạn cần hoàn thành bài kiểm tra trong một lần. Hãy bảo đảm kết nối Internet ổn định trước khi nhấn “Bắt đầu”."
        );
        SetText(confirmationTitleLabel, "Start Quiz?", "Bắt đầu bài kiểm tra?");
        SetText(
            confirmationMessageLabel,
            "The quiz timer will begin immediately after you start.",
            "Thời gian làm bài sẽ bắt đầu ngay khi bạn xác nhận."
        );

        if (backButton != null)
            backButton.tooltip = T("Back", "Quay lại");
        if (cancelStartButton != null)
            cancelStartButton.text = T("Cancel", "Hủy");
        if (confirmStartButton != null)
            confirmStartButton.text = T("Start", "Bắt đầu");

        if (openTimeLabel != null)
            openTimeLabel.text = FormatQuizDate(
                openDateIso,
                T("Available now", "Có thể làm ngay")
            );
        if (closeTimeLabel != null)
            closeTimeLabel.text = FormatQuizDate(
                closeDateIso,
                T("No deadline", "Không có thời hạn")
            );

        int count = PlayerPrefs.GetInt("selected_quiz_questions", totalQuestions);
        if (questionCountLabel != null)
            questionCountLabel.text = FormatQuestionCount(count);

        float grade = PlayerPrefs.HasKey("selected_quiz_max_score")
            ? PlayerPrefs.GetFloat("selected_quiz_max_score", maximumGrade)
            : PlayerPrefs.GetFloat("selected_quiz_maximum_grade", maximumGrade);
        if (maximumGradeLabel != null)
            maximumGradeLabel.text = FormatPoints(grade);

        RenderAttemptHistory();
        RefreshAvailability();
        ApplyAttemptedQuizState();

        if (isTeacherMode)
        {
            SetText(quizTypeLabel, "QUIZ · TEACHER VIEW", "BÀI KIỂM TRA · CHẾ ĐỘ GIÁO VIÊN");
            teacherEditor?.Refresh();
        }
    }

    private static void ApplyStaticTranslationsRecursive(VisualElement element)
    {
        if (element == null) return;

        if (element is Label label)
            label.text = TranslateKnownStaticText(label.text);
        else if (element is Button button)
            button.text = TranslateKnownStaticText(button.text);

        foreach (VisualElement child in element.Children())
            ApplyStaticTranslationsRecursive(child);
    }

    private static string TranslateKnownStaticText(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        foreach (KeyValuePair<string, string> pair in StaticVietnameseText)
        {
            if (value == pair.Key || value == pair.Value)
                return AppLanguageManager.IsVietnamese ? pair.Value : pair.Key;
        }

        return value;
    }

    private static string FormatQuestionCount(int count)
    {
        if (AppLanguageManager.IsVietnamese)
            return $"{count} câu hỏi";

        return count == 1 ? "1 question" : $"{count} questions";
    }

    private static string FormatPoints(float points)
    {
        return AppLanguageManager.IsVietnamese
            ? $"{points:0.##} điểm"
            : $"{points:0.##} points";
    }

    private void LoadQuizData()
    {
        /*
         * Có thể lưu dữ liệu từ scene trước bằng PlayerPrefs.
         *
         * Ví dụ:
         * PlayerPrefs.SetString("selected_quiz_title", quizTitle);
         * PlayerPrefs.SetInt("selected_quiz_questions", 5);
         */

        string savedLessonName = PlayerPrefs.GetString(
            "selected_lesson_name",
            lessonName
        );

        string savedQuizHeaderTitle = PlayerPrefs.GetString(
            "selected_quiz_name",
            quizHeaderTitle
        );

        string savedQuizTitle = PlayerPrefs.GetString(
            "selected_quiz_title",
            quizTitle
        );

        string savedQuizSubtitle = PlayerPrefs.GetString(
            "selected_quiz_subtitle",
            quizSubtitle
        );

        openDateIso = PlayerPrefs.GetString("selected_quiz_open_at", string.Empty);
        closeDateIso = PlayerPrefs.GetString("selected_quiz_close_at", string.Empty);

        string savedOpenTime = FormatQuizDate(
            openDateIso,
            T("Available now", "Có thể làm ngay")
        );
        string savedCloseTime = FormatQuizDate(
            closeDateIso,
            T("No deadline", "Không có thời hạn")
        );

        int savedQuestionCount = PlayerPrefs.GetInt(
            "selected_quiz_questions",
            totalQuestions
        );

        float savedMaximumGrade = PlayerPrefs.HasKey("selected_quiz_max_score")
            ? PlayerPrefs.GetFloat("selected_quiz_max_score", maximumGrade)
            : PlayerPrefs.GetFloat("selected_quiz_maximum_grade", maximumGrade);

        if (lessonLabel != null)
        {
            lessonLabel.text = savedLessonName;
        }

        if (headerQuizTitle != null)
        {
            headerQuizTitle.text = savedQuizHeaderTitle;
        }

        if (quizTitleLabel != null)
        {
            quizTitleLabel.text = savedQuizTitle;
        }

        if (quizSubtitleLabel != null)
        {
            quizSubtitleLabel.text = savedQuizSubtitle;
        }

        if (openTimeLabel != null)
        {
            openTimeLabel.text = savedOpenTime;
        }

        if (closeTimeLabel != null)
        {
            closeTimeLabel.text = savedCloseTime;
        }

        if (questionCountLabel != null)
        {
            questionCountLabel.text = FormatQuestionCount(savedQuestionCount);
        }

        if (maximumGradeLabel != null)
        {
            maximumGradeLabel.text = FormatPoints(savedMaximumGrade);
        }
    }


    private IEnumerator RefreshQuizMetadataRoutine()
    {
        if (restService == null) yield break;

        string quizId = PlayerPrefs.GetString("selected_quiz_id", string.Empty);
        if (!Guid.TryParse(quizId, out _)) yield break;

        string response = null;
        string error = null;
        string path =
            "rest/v1/quizzes" +
            "?select=id,title,opens_at,closes_at,total_questions,max_score" +
            "&id=eq." + UnityEngine.Networking.UnityWebRequest.EscapeURL(quizId) +
            "&limit=1";

        yield return restService.SendJson(
            UnityEngine.Networking.UnityWebRequest.kHttpVerbGET,
            path,
            null,
            null,
            value => response = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogWarning(
                "[StartQuizPageController] Cannot refresh quiz metadata: " + error
            );
            yield break;
        }

        StartQuizMetadataList wrapper = ParseList<StartQuizMetadataList>(response);
        if (wrapper?.items == null || wrapper.items.Length == 0) yield break;

        StartQuizMetadataRow quiz = wrapper.items[0];
        openDateIso = quiz.opens_at ?? string.Empty;
        closeDateIso = quiz.closes_at ?? string.Empty;

        PlayerPrefs.SetString("selected_quiz_open_at", openDateIso);
        PlayerPrefs.SetString("selected_quiz_close_at", closeDateIso);
        PlayerPrefs.SetInt("selected_quiz_questions", quiz.total_questions);
        PlayerPrefs.SetFloat("selected_quiz_max_score", quiz.max_score);
        PlayerPrefs.Save();

        if (quizTitleLabel != null && !string.IsNullOrWhiteSpace(quiz.title))
            quizTitleLabel.text = quiz.title;
        if (openTimeLabel != null)
            openTimeLabel.text = FormatQuizDate(
                openDateIso,
                T("Available now", "Có thể làm ngay")
            );
        if (closeTimeLabel != null)
            closeTimeLabel.text = FormatQuizDate(
                closeDateIso,
                T("No deadline", "Không có thời hạn")
            );
        if (questionCountLabel != null)
            questionCountLabel.text = FormatQuestionCount(quiz.total_questions);
        if (maximumGradeLabel != null)
            maximumGradeLabel.text = FormatPoints(quiz.max_score);

        RefreshAvailability();
    }

    private IEnumerator LoadAttemptHistoryRoutine()
    {
        HideAttemptHistory();
        loadedAttempts = Array.Empty<QuizAttemptView>();

        if (restService == null)
        {
            Debug.LogError("[StartQuizPageController] SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string quizId = PlayerPrefs.GetString("selected_quiz_id", string.Empty);
        string studentId = PlayerPrefs.GetString("user_id", string.Empty);

        if (!Guid.TryParse(quizId, out _) || !Guid.TryParse(studentId, out _))
        {
            Debug.LogWarning("[StartQuizPageController] selected_quiz_id or user_id is invalid.");
            yield break;
        }

        string response = null;
        string error = null;

        string path =
            "rest/v1/quiz_attempts" +
            "?select=id,quiz_id,student_id,status,score,started_at,submitted_at" +
            "&quiz_id=eq." + UnityEngine.Networking.UnityWebRequest.EscapeURL(quizId) +
            "&student_id=eq." + UnityEngine.Networking.UnityWebRequest.EscapeURL(studentId) +
            "&status=eq.submitted" +
            "&order=started_at.asc";

        yield return restService.SendJson(
            UnityEngine.Networking.UnityWebRequest.kHttpVerbGET,
            path,
            null,
            null,
            value => response = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError("[StartQuizPageController] Cannot load attempt history: " + error);
            HideAttemptHistory();
            yield break;
        }

        QuizAttemptDbList wrapper = ParseList<QuizAttemptDbList>(response);

        if (wrapper?.items == null || wrapper.items.Length == 0)
        {
            loadedAttempts = Array.Empty<QuizAttemptView>();
            HideAttemptHistory();
            RefreshAvailability();
            Debug.Log("[StartQuizPageController] No previous submitted attempts.");
            yield break;
        }

        QuizAttemptView[] attempts = new QuizAttemptView[wrapper.items.Length];
        int fallbackTotalQuestions = PlayerPrefs.GetInt(
            "selected_quiz_questions",
            totalQuestions
        );

        for (int i = 0; i < wrapper.items.Length; i++)
        {
            QuizAttemptDbRow dbAttempt = wrapper.items[i];

            QuizAttemptView view = new QuizAttemptView
            {
                attempt_id = dbAttempt.id,
                quiz_id = dbAttempt.quiz_id,
                student_id = dbAttempt.student_id,
                attempt_number = i + 1,
                started_at = dbAttempt.started_at,
                submitted_at = dbAttempt.submitted_at,
                duration_seconds = CalculateDurationSeconds(
                    dbAttempt.started_at,
                    dbAttempt.submitted_at
                ),
                total_questions = fallbackTotalQuestions,
                correct_count = 0,
                score = (float)Math.Round(
                    dbAttempt.score,
                    2,
                    MidpointRounding.AwayFromZero
                ),
                status = dbAttempt.status
            };

            string responsesJson = null;
            string responsesError = null;

            yield return restService.SendJson(
                UnityEngine.Networking.UnityWebRequest.kHttpVerbGET,
                "rest/v1/quiz_responses?select=id,is_correct&attempt_id=eq." +
                UnityEngine.Networking.UnityWebRequest.EscapeURL(dbAttempt.id),
                null,
                null,
                value => responsesJson = value,
                message => responsesError = message
            );

            if (string.IsNullOrWhiteSpace(responsesError))
            {
                QuizResponseHistoryList responseWrapper =
                    ParseList<QuizResponseHistoryList>(responsesJson);

                if (responseWrapper?.items != null)
                {
                    view.total_questions = responseWrapper.items.Length;
                    int correct = 0;

                    foreach (QuizResponseHistoryRow item in responseWrapper.items)
                    {
                        if (item != null && item.is_correct)
                            correct++;
                    }

                    view.correct_count = correct;
                }
            }

            attempts[i] = view;
        }

        loadedAttempts = attempts;
        RenderAttemptHistory();
        ApplyAttemptedQuizState();
    }

    private static int CalculateDurationSeconds(string startedAt, string submittedAt)
    {
        if (!DateTime.TryParse(
                startedAt,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTime started))
            return 0;

        if (!DateTime.TryParse(
                submittedAt,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTime submitted))
            return 0;

        return Mathf.Max(
            0,
            Mathf.RoundToInt((float)(submitted - started).TotalSeconds)
        );
    }

    private void RenderAttemptHistory()
    {
        if (attemptHistoryContainer == null ||
            attemptHistorySection == null)
        {
            return;
        }

        attemptHistoryContainer.Clear();

        if (loadedAttempts == null || loadedAttempts.Length == 0)
        {
            attemptHistorySection.AddToClassList("hidden");
            return;
        }

        for (int i = 0; i < loadedAttempts.Length; i++)
        {
            QuizAttemptView attempt = loadedAttempts[i];
            if (attempt == null) continue;

            attemptHistoryContainer.Add(
                CreateAttemptCard(attempt, i)
            );
        }

        attemptHistorySection.RemoveFromClassList("hidden");
    }

    private VisualElement CreateAttemptCard(
        QuizAttemptView attempt,
        int index)
    {
        VisualElement card = new();
        card.AddToClassList("attempt-card");

        VisualElement header = new();
        header.AddToClassList("attempt-card-header");

        VisualElement headingLeft = new();
        headingLeft.AddToClassList("attempt-heading-left");

        int displayAttemptNumber =
            attempt.attempt_number > 0
                ? attempt.attempt_number
                : index + 1;

        Label number = new(displayAttemptNumber.ToString());
        number.AddToClassList("attempt-number");

        Label title = new(T("Attempt", "Lần làm"));
        title.AddToClassList("attempt-title");

        Label submittedStatus = new(
            string.IsNullOrWhiteSpace(attempt.submitted_at)
                ? T("In Progress", "Đang làm")
                : T("Submitted", "Đã nộp")
        );
        submittedStatus.AddToClassList("attempt-status");

        headingLeft.Add(number);
        headingLeft.Add(title);
        header.Add(headingLeft);
        header.Add(submittedStatus);

        VisualElement body = new();
        body.AddToClassList("attempt-body");

        AddAttemptRow(
            body,
            T("Started On", "Bắt đầu lúc"),
            FormatAttemptDate(attempt.started_at)
        );

        AddAttemptDivider(body);

        AddAttemptRow(
            body,
            T("Submitted On", "Nộp lúc"),
            string.IsNullOrWhiteSpace(attempt.submitted_at)
                ? "—"
                : FormatAttemptDate(attempt.submitted_at)
        );

        AddAttemptDivider(body);

        AddAttemptRow(
            body,
            T("Time Taken", "Thời gian làm"),
            FormatDuration(attempt.duration_seconds)
        );

        AddAttemptDivider(body);

        float maxGrade = PlayerPrefs.HasKey("selected_quiz_max_score")
            ? PlayerPrefs.GetFloat("selected_quiz_max_score", maximumGrade)
            : PlayerPrefs.GetFloat("selected_quiz_maximum_grade", maximumGrade);

        Label scoreValue = AddAttemptRow(
            body,
            T("Grade / Score", "Điểm số"),
            $"{attempt.score:0.##} / {maxGrade:0.##}"
        );
        scoreValue?.AddToClassList("attempt-score");

        Button review = new();
        review.text = T("Review Attempt", "Xem lại bài làm");
        review.AddToClassList("review-attempt-button");

        string capturedAttemptId = attempt.attempt_id;
        review.clicked += () =>
        {
            if (string.IsNullOrWhiteSpace(capturedAttemptId))
            {
                Debug.LogWarning(
                    "[StartQuizPageController] Review Attempt has no attempt id."
                );
                return;
            }

            PlayerPrefs.SetString("selected_attempt_id", capturedAttemptId);
            PlayerPrefs.SetString("quiz_mode", "review");
            PlayerPrefs.SetString(
                PreviousSceneKey,
                SceneManager.GetActiveScene().name
            );
            PlayerPrefs.Save();

            Debug.Log(
                "[StartQuizPageController] Opening review mode. " +
                $"Attempt ID: {capturedAttemptId}"
            );

            if (Application.CanStreamedLevelBeLoaded(quizSceneName))
            {
                SceneManager.LoadScene(quizSceneName);
            }
            else
            {
                Debug.LogError(
                    $"[StartQuizPageController] Scene '{quizSceneName}' " +
                    "was not found in Build Profiles."
                );
            }
        };

        body.Add(review);

        card.Add(header);
        card.Add(body);

        return card;
    }

    private static Label AddAttemptRow(
        VisualElement parent,
        string labelText,
        string valueText)
    {
        VisualElement row = new();
        row.AddToClassList("attempt-row");

        Label label = new(labelText);
        label.AddToClassList("attempt-row-label");

        Label value = new(valueText);
        value.AddToClassList("attempt-row-value");

        row.Add(label);
        row.Add(value);
        parent.Add(row);

        return value;
    }

    private static void AddAttemptDivider(
        VisualElement parent)
    {
        VisualElement divider = new();
        divider.AddToClassList("attempt-row-divider");
        parent.Add(divider);
    }

    private void ApplyAttemptedQuizState()
    {
        QuizAttemptView bestAttempt = null;

        foreach (QuizAttemptView attempt in loadedAttempts)
        {
            if (attempt == null ||
                string.IsNullOrWhiteSpace(attempt.submitted_at))
            {
                continue;
            }

            if (bestAttempt == null ||
                attempt.score > bestAttempt.score)
            {
                bestAttempt = attempt;
            }
        }

        if (bestAttempt == null)
        {
            return;
        }

        // The deadline has priority over the completed/retake state.
        if (IsQuizClosed())
        {
            SetQuizUnavailable(
                T("CLOSED", "ĐÃ ĐÓNG"),
                T("Quiz Closed", "Đã hết hạn")
            );
            return;
        }

        SetStatus(T("COMPLETED", "HOÀN THÀNH"), "status-completed");

        if (startQuizButton != null)
        {
            startQuizButton.text = T("Start New Attempt", "Làm lại bài");
        }

        if (noticeCard != null)
        {
            noticeCard.AddToClassList("hidden");
        }
    }

    private void HideAttemptHistory()
    {
        attemptHistoryContainer?.Clear();
        attemptHistorySection?.AddToClassList("hidden");
    }

    private static string FormatAttemptDate(string iso)
    {
        if (DateTime.TryParse(
                iso,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTime date))
        {
            return AppLanguageManager.IsVietnamese
                ? date.ToLocalTime().ToString("dd/MM/yyyy · HH:mm")
                : date.ToLocalTime().ToString("MMM d, yyyy · hh:mm tt");
        }

        return string.IsNullOrWhiteSpace(iso) ? "—" : iso;
    }

    private static string FormatDuration(int seconds)
    {
        seconds = Mathf.Max(0, seconds);

        int hours = seconds / 3600;
        int minutes = (seconds % 3600) / 60;
        int secs = seconds % 60;

        if (hours > 0)
        {
            return AppLanguageManager.IsVietnamese
                ? $"{hours} giờ {minutes} phút {secs} giây"
                : $"{hours}h {minutes}m {secs}s";
        }

        if (minutes > 0)
        {
            return AppLanguageManager.IsVietnamese
                ? $"{minutes} phút {secs} giây"
                : $"{minutes} mins {secs} secs";
        }

        return AppLanguageManager.IsVietnamese
            ? $"{secs} giây"
            : $"{secs} secs";
    }

    private static T ParseList<T>(string json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<T>(
                $"{{\"items\":{json}}}"
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[StartQuizPageController] Cannot parse attempt history: " +
                exception.Message
            );
            return null;
        }
    }

    private void RefreshAvailability()
    {
        if (!checkAvailabilityByDate)
        {
            SetQuizAvailable();
            return;
        }

        bool hasOpenDate = TryParseUtcDate(openDateIso, out DateTimeOffset openDate);
        bool hasCloseDate = TryParseUtcDate(closeDateIso, out DateTimeOffset closeDate);

        if ((!string.IsNullOrWhiteSpace(openDateIso) && !hasOpenDate) ||
            (!string.IsNullOrWhiteSpace(closeDateIso) && !hasCloseDate))
        {
            Debug.LogWarning(
                "[StartQuizPageController] Invalid quiz date format. " +
                "The Start Quiz button will be disabled."
            );

            SetQuizUnavailable(
                T("UNAVAILABLE", "KHÔNG KHẢ DỤNG"),
                T("Invalid quiz schedule", "Lịch bài kiểm tra không hợp lệ")
            );
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (hasOpenDate && now < openDate)
        {
            SetQuizUnavailable(
                T("NOT OPEN YET", "CHƯA MỞ"),
                T("Available from ", "Mở từ ") + FormatLocalDate(openDate)
            );

            return;
        }

        if (hasCloseDate && now >= closeDate)
        {
            SetQuizUnavailable(
                T("CLOSED", "ĐÃ ĐÓNG"),
                T("Quiz Closed", "Đã hết hạn")
            );

            return;
        }

        SetQuizAvailable();
    }

    private bool IsQuizCurrentlyAvailable()
    {
        bool hasOpenDate = TryParseUtcDate(openDateIso, out DateTimeOffset openDate);
        bool hasCloseDate = TryParseUtcDate(closeDateIso, out DateTimeOffset closeDate);

        if ((!string.IsNullOrWhiteSpace(openDateIso) && !hasOpenDate) ||
            (!string.IsNullOrWhiteSpace(closeDateIso) && !hasCloseDate))
            return false;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (hasOpenDate && now < openDate) return false;
        if (hasCloseDate && now >= closeDate) return false;
        return true;
    }

    private bool IsQuizClosed()
    {
        return TryParseUtcDate(closeDateIso, out DateTimeOffset closeDate) &&
               DateTimeOffset.UtcNow >= closeDate;
    }

    private IEnumerator MonitorQuizAvailability()
    {
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(1f);

        while (true)
        {
            if (IsQuizClosed())
            {
                SetQuizUnavailable(
                    T("CLOSED", "ĐÃ ĐÓNG"),
                    T("Quiz Closed", "Đã hết hạn")
                );
                HideConfirmation();
            }

            yield return wait;
        }
    }

    private static bool TryParseUtcDate(string value, out DateTimeOffset date)
    {
        return DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out date
        );
    }

    private static string FormatQuizDate(string iso, string fallback)
    {
        return TryParseUtcDate(iso, out DateTimeOffset value)
            ? FormatLocalDate(value)
            : fallback;
    }

    private static string FormatLocalDate(DateTimeOffset value)
    {
        return AppLanguageManager.IsVietnamese
            ? value.ToLocalTime().ToString("dd/MM/yyyy · HH:mm")
            : value.ToLocalTime().ToString("MMM dd, yyyy · hh:mm tt");
    }

    private void SetQuizAvailable()
    {
        if (startQuizButton != null)
        {
            startQuizButton.SetEnabled(true);
            startQuizButton.text = T("Start Quiz", "Bắt đầu");
        }

        SetStatus(T("NOT ATTEMPTED", "CHƯA LÀM"), "status-not-attempted");
    }

    private void SetQuizUnavailable(
        string status,
        string buttonText)
    {
        if (startQuizButton != null)
        {
            startQuizButton.SetEnabled(false);
            startQuizButton.text = buttonText;
        }

        SetStatus(status, "status-not-attempted");
    }

    private void SetStatus(
        string status,
        string statusClass)
    {
        if (isTeacherMode)
        {
            status = T("ANSWER KEY", "ĐÁP ÁN");
            statusClass = "status-completed";
        }

        if (statusLabel != null)
        {
            statusLabel.text = status;
        }

        if (statusBadge == null)
        {
            return;
        }

        statusBadge.RemoveFromClassList(
            "status-not-attempted"
        );
        statusBadge.RemoveFromClassList(
            "status-completed"
        );

        if (!string.IsNullOrWhiteSpace(statusClass))
        {
            statusBadge.AddToClassList(statusClass);
        }
    }

    private void CacheStartQuizOriginScene()
    {
        string activeScene =
            SceneManager.GetActiveScene().name;

        string previousScene =
            PlayerPrefs.GetString(
                PreviousSceneKey,
                string.Empty
            );

        // Only store a real scene that opened StartQuizScene.
        // Do NOT replace the origin with StartQuizScene/DoQuizScene when
        // returning from quiz attempt or review.
        if (!string.IsNullOrWhiteSpace(previousScene) &&
            previousScene != activeScene &&
            previousScene != quizSceneName)
        {
            PlayerPrefs.SetString(
                StartQuizOriginSceneKey,
                previousScene
            );
            PlayerPrefs.Save();

            Debug.Log(
                "[StartQuizPageController] Cached StartQuiz origin scene: " +
                previousScene
            );

            return;
        }

        // If this is the first time and no valid origin has been saved,
        // use the configured fallback.
        if (!PlayerPrefs.HasKey(StartQuizOriginSceneKey) &&
            !string.IsNullOrWhiteSpace(fallbackBackScene))
        {
            PlayerPrefs.SetString(
                StartQuizOriginSceneKey,
                fallbackBackScene
            );
            PlayerPrefs.Save();
        }
    }

    private string ResolveBackScene()
    {
        string activeScene =
            SceneManager.GetActiveScene().name;

        string targetScene =
            PlayerPrefs.GetString(
                StartQuizOriginSceneKey,
                string.Empty
            );

        if (string.IsNullOrWhiteSpace(targetScene) ||
            targetScene == activeScene ||
            targetScene == quizSceneName)
        {
            string previousScene =
                PlayerPrefs.GetString(
                    PreviousSceneKey,
                    string.Empty
                );

            if (!string.IsNullOrWhiteSpace(previousScene) &&
                previousScene != activeScene &&
                previousScene != quizSceneName)
            {
                targetScene = previousScene;
            }
            else
            {
                targetScene = fallbackBackScene;
            }
        }

        return targetScene;
    }

    private void HandleBackClicked()
    {
        if (isStartingQuiz)
        {
            return;
        }

        // If the start confirmation is open, Back closes the popup first.
        if (confirmationOverlay != null &&
            !confirmationOverlay.ClassListContains("hidden"))
        {
            HideConfirmation();
            return;
        }

        string targetScene =
            ResolveBackScene();

        if (!Application.CanStreamedLevelBeLoaded(targetScene))
        {
            Debug.LogWarning(
                $"[StartQuizPageController] Scene '{targetScene}' " +
                "is not available. Using fallback scene."
            );

            targetScene = fallbackBackScene;
        }

        if (!Application.CanStreamedLevelBeLoaded(targetScene))
        {
            Debug.LogError(
                $"[StartQuizPageController] Cannot load scene '{targetScene}'. " +
                "Add it to Build Profiles."
            );
            return;
        }

        // Update the common navigation key for the destination scene,
        // but keep start_quiz_origin_scene intact until navigation succeeds.
        PlayerPrefs.SetString(
            PreviousSceneKey,
            SceneManager.GetActiveScene().name
        );
        PlayerPrefs.Save();

        Debug.Log(
            "[StartQuizPageController] Back -> " +
            targetScene
        );

        SceneManager.LoadScene(targetScene);
    }

    private void HandleStartQuizClicked()
    {
        if (isStartingQuiz || isTeacherMode)
        {
            return;
        }

        RefreshAvailability();
        if (!IsQuizCurrentlyAvailable())
            return;

        ShowConfirmation();
    }

    private void ShowConfirmation()
    {
        if (confirmationOverlay == null)
        {
            ConfirmStartQuiz();
            return;
        }

        confirmationOverlay.RemoveFromClassList("hidden");
    }

    private void HideConfirmation()
    {
        if (confirmationOverlay == null)
        {
            return;
        }

        confirmationOverlay.AddToClassList("hidden");
    }

    private void ConfirmStartQuiz()
    {
        if (isStartingQuiz || isTeacherMode)
        {
            return;
        }


        RefreshAvailability();
        if (!IsQuizCurrentlyAvailable())
        {
            HideConfirmation();
            return;
        }

        isStartingQuiz = true;

        HideConfirmation();

        string selectedQuizId = PlayerPrefs.GetString(
            "selected_quiz_id",
            string.Empty
        );

        PlayerPrefs.SetString("quiz_mode", "attempt");

        PlayerPrefs.SetString(
            "quiz_started_at",
            DateTime.UtcNow.ToString("O")
        );

        PlayerPrefs.SetString(
            PreviousSceneKey,
            SceneManager.GetActiveScene().name
        );

        PlayerPrefs.Save();

        Debug.Log(
            $"[StartQuizPageController] Starting quiz. " +
            $"Quiz ID: {selectedQuizId}"
        );

        StartCoroutine(LoadQuizScene());
    }

    private IEnumerator LoadQuizScene()
    {
        if (startQuizButton != null)
        {
            startQuizButton.SetEnabled(false);
            startQuizButton.text = T("Loading...", "Đang tải...");
        }

        yield return null;

        if (!Application.CanStreamedLevelBeLoaded(quizSceneName))
        {
            Debug.LogError(
                $"[StartQuizPageController] Scene '{quizSceneName}' " +
                "was not found in Build Profiles."
            );

            isStartingQuiz = false;

            if (startQuizButton != null)
            {
                startQuizButton.SetEnabled(true);
                startQuizButton.text = T("Start Quiz", "Bắt đầu");
            }

            yield break;
        }

        AsyncOperation operation =
            SceneManager.LoadSceneAsync(quizSceneName);

        if (operation == null)
        {
            Debug.LogError(
                "[StartQuizPageController] Cannot start scene loading."
            );

            isStartingQuiz = false;
            yield break;
        }

        while (!operation.isDone)
        {
            yield return null;
        }
    }
}

[Serializable]
public class QuizAttemptDbRow
{
    public string id;
    public string quiz_id;
    public string student_id;
    public string status;
    public float score;
    public string started_at;
    public string submitted_at;
}

[Serializable]
public class StartQuizMetadataRow
{
    public string id;
    public string title;
    public string opens_at;
    public string closes_at;
    public int total_questions;
    public float max_score;
}

[Serializable]
public class StartQuizMetadataList
{
    public StartQuizMetadataRow[] items;
}

[Serializable]
public class QuizAttemptDbList
{
    public QuizAttemptDbRow[] items;
}

[Serializable]
public class QuizResponseHistoryRow
{
    public string id;
    public bool is_correct;
}

[Serializable]
public class QuizResponseHistoryList
{
    public QuizResponseHistoryRow[] items;
}

[Serializable]
public class QuizAttemptView
{
    public string attempt_id;
    public string quiz_id;
    public string student_id;
    public int attempt_number;
    public string started_at;
    public string submitted_at;
    public int duration_seconds;
    public int total_questions;
    public int correct_count;
    public float score;
    public string status;
}

[Serializable]
public class QuizAttemptViewList
{
    public QuizAttemptView[] items;
}
