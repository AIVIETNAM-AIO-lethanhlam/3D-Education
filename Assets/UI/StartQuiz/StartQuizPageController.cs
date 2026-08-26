using System;
using System.Collections;
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
    [SerializeField] private bool checkAvailabilityByDate;
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

    private VisualElement statusBadge;
    private VisualElement confirmationOverlay;
    private VisualElement attemptHistorySection;
    private VisualElement attemptHistoryContainer;
    private VisualElement noticeCard;

    private bool isStartingQuiz;
    private QuizAttemptView[] loadedAttempts = Array.Empty<QuizAttemptView>();

    private void Awake()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        restService = GetComponent<SupabaseRuntimeRestService>();
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
        LoadQuizData();
        RefreshAvailability();
        StartCoroutine(LoadAttemptHistoryRoutine());
    }

    private void OnDisable()
    {
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

        statusBadge = root.Q<VisualElement>("status-badge");

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

        string savedOpenTime = PlayerPrefs.GetString(
            "quiz_open_time",
            openTime
        );

        string savedCloseTime = PlayerPrefs.GetString(
            "quiz_close_time",
            closeTime
        );

        int savedQuestionCount = PlayerPrefs.GetInt(
            "selected_quiz_questions",
            totalQuestions
        );

        float savedMaximumGrade = PlayerPrefs.GetFloat(
            "selected_quiz_maximum_grade",
            maximumGrade
        );

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
            string questionText = savedQuestionCount == 1
                ? "1 question"
                : $"{savedQuestionCount} questions";

            questionCountLabel.text = questionText;
        }

        if (maximumGradeLabel != null)
        {
            maximumGradeLabel.text =
                $"{savedMaximumGrade:0.##} points";
        }
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

        Label title = new("Attempt");
        title.AddToClassList("attempt-title");

        Label submittedStatus = new(
            string.IsNullOrWhiteSpace(attempt.submitted_at)
                ? "In Progress"
                : "Submitted"
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
            "Started On",
            FormatAttemptDate(attempt.started_at)
        );

        AddAttemptDivider(body);

        AddAttemptRow(
            body,
            "Submitted On",
            string.IsNullOrWhiteSpace(attempt.submitted_at)
                ? "—"
                : FormatAttemptDate(attempt.submitted_at)
        );

        AddAttemptDivider(body);

        AddAttemptRow(
            body,
            "Time Taken",
            FormatDuration(attempt.duration_seconds)
        );

        AddAttemptDivider(body);

        float maxGrade = PlayerPrefs.GetFloat(
            "selected_quiz_maximum_grade",
            maximumGrade
        );

        Label scoreValue = AddAttemptRow(
            body,
            "Grade / Score",
            $"{attempt.score:0.##} / {maxGrade:0.##}"
        );
        scoreValue?.AddToClassList("attempt-score");

        Button review = new();
        review.text = "Review Attempt";
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

        SetStatus("COMPLETED", "status-completed");

        if (startQuizButton != null)
        {
            startQuizButton.text = "Start New Attempt";
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
            return date.ToLocalTime().ToString(
                "MMM d, yyyy · hh:mm tt"
            );
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
            return $"{hours}h {minutes}m {secs}s";
        }

        if (minutes > 0)
        {
            return $"{minutes} mins {secs} secs";
        }

        return $"{secs} secs";
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

        if (!TryParseQuizDates(
                out DateTime openDate,
                out DateTime closeDate))
        {
            Debug.LogWarning(
                "[StartQuizPageController] Invalid quiz date format. " +
                "The Start Quiz button will remain enabled."
            );

            SetQuizAvailable();
            return;
        }

        DateTime now = DateTime.Now;

        if (now < openDate)
        {
            SetQuizUnavailable(
                "NOT OPEN YET",
                $"Available from {openDate:MMM dd, yyyy · hh:mm tt}"
            );

            return;
        }

        if (now > closeDate)
        {
            SetQuizUnavailable(
                "CLOSED",
                "This quiz is no longer available."
            );

            return;
        }

        SetQuizAvailable();
    }

    private bool TryParseQuizDates(
        out DateTime openDate,
        out DateTime closeDate)
    {
        bool openValid = DateTime.TryParse(
            openDateIso,
            out openDate
        );

        bool closeValid = DateTime.TryParse(
            closeDateIso,
            out closeDate
        );

        return openValid && closeValid;
    }

    private void SetQuizAvailable()
    {
        if (startQuizButton != null)
        {
            startQuizButton.SetEnabled(true);
            startQuizButton.text = "Start Quiz";
        }

        SetStatus("NOT ATTEMPTED", "status-not-attempted");
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
        if (isStartingQuiz)
        {
            return;
        }

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
        if (isStartingQuiz)
        {
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
            startQuizButton.text = "Loading...";
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
                startQuizButton.text = "Start Quiz";
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
