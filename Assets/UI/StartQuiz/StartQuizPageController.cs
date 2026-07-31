using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class StartQuizPageController : MonoBehaviour
{
    [Header("UI Document")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Navigation")]
    [SerializeField] private string fallbackBackScene = "ShowLessonScene";
    [SerializeField] private string quizSceneName = "DoQuizScene";

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

    private bool isStartingQuiz;

    private void Awake()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }
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

        FindElements();
        RegisterEvents();
        LoadQuizData();
        RefreshAvailability();
    }

    private void OnDisable()
    {
        UnregisterEvents();
    }

    private void FindElements()
    {
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

        if (!string.IsNullOrWhiteSpace(statusClass))
        {
            statusBadge.AddToClassList(statusClass);
        }
    }

    private void HandleBackClicked()
    {
        string previousScene = PlayerPrefs.GetString(
            "previous_scene",
            fallbackBackScene
        );

        if (string.IsNullOrWhiteSpace(previousScene))
        {
            previousScene = fallbackBackScene;
        }

        if (!Application.CanStreamedLevelBeLoaded(previousScene))
        {
            Debug.LogWarning(
                $"[StartQuizPageController] Scene '{previousScene}' " +
                "is not available. Using fallback scene."
            );

            previousScene = fallbackBackScene;
        }

        if (Application.CanStreamedLevelBeLoaded(previousScene))
        {
            SceneManager.LoadScene(previousScene);
        }
        else
        {
            Debug.LogError(
                $"[StartQuizPageController] Cannot load scene " +
                $"'{previousScene}'. Add it to Build Profiles."
            );
        }
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

        PlayerPrefs.SetString(
            "quiz_started_at",
            DateTime.UtcNow.ToString("O")
        );

        PlayerPrefs.SetString(
            "previous_scene",
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