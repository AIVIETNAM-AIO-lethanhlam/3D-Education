using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class DoQuizPageController : MonoBehaviour
{
    [Header("UI Document")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Scene Navigation")]
    [SerializeField] private string previousSceneName = "StartQuizScene";
    [SerializeField] private string resultSceneName = "QuizResultScene";

    private Button backButton;
    private Button nextQuestionButton;

    private Label quizNameLabel;
    private Label questionNumberLabel;
    private Label questionBadgeLabel;
    private Label questionCategoryLabel;
    private Label questionTextLabel;

    private Label answerALabel;
    private Label answerBLabel;
    private Label answerCLabel;
    private Label answerDLabel;

    private VisualElement progressFill;
    private VisualElement progressDotsContainer;

    private readonly List<Button> answerButtons = new();
    private readonly List<QuizQuestionData> questions = new();

    private int currentQuestionIndex;
    private int selectedAnswerIndex = -1;
    private int correctAnswerCount;

    private const string SelectedClass = "answer-button-selected";
    private const string CorrectClass = "answer-button-correct";
    private const string WrongClass = "answer-button-wrong";

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
                "[DoQuizPageController] Không tìm thấy UIDocument.",
                this
            );

            return;
        }

        VisualElement root = uiDocument.rootVisualElement;

        CacheUIElements(root);
        RegisterCallbacks();
        CreateDemoQuestions();

        currentQuestionIndex = 0;
        selectedAnswerIndex = -1;
        correctAnswerCount = 0;

        ShowQuestion(currentQuestionIndex);
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
    }

    private void CacheUIElements(VisualElement root)
    {
        backButton = root.Q<Button>("back-button");
        nextQuestionButton = root.Q<Button>("next-question-button");

        quizNameLabel = root.Q<Label>("quiz-name-label");
        questionNumberLabel = root.Q<Label>("question-number-label");
        questionBadgeLabel = root.Q<Label>("question-badge-label");
        questionCategoryLabel = root.Q<Label>("question-category-label");
        questionTextLabel = root.Q<Label>("question-text-label");

        answerALabel = root.Q<Label>("answer-a-label");
        answerBLabel = root.Q<Label>("answer-b-label");
        answerCLabel = root.Q<Label>("answer-c-label");
        answerDLabel = root.Q<Label>("answer-d-label");

        progressFill = root.Q<VisualElement>("progress-fill");
        progressDotsContainer =
            root.Q<VisualElement>("progress-dots-container");

        answerButtons.Clear();

        answerButtons.Add(root.Q<Button>("answer-a-button"));
        answerButtons.Add(root.Q<Button>("answer-b-button"));
        answerButtons.Add(root.Q<Button>("answer-c-button"));
        answerButtons.Add(root.Q<Button>("answer-d-button"));
    }

    private void RegisterCallbacks()
    {
        if (backButton != null)
        {
            backButton.clicked += HandleBackClicked;
        }

        if (nextQuestionButton != null)
        {
            nextQuestionButton.clicked += HandleNextQuestionClicked;
        }

        for (int index = 0; index < answerButtons.Count; index++)
        {
            int capturedIndex = index;
            Button button = answerButtons[index];

            if (button != null)
            {
                button.clicked += () => HandleAnswerSelected(capturedIndex);
            }
        }
    }

    private void UnregisterCallbacks()
    {
        if (backButton != null)
        {
            backButton.clicked -= HandleBackClicked;
        }

        if (nextQuestionButton != null)
        {
            nextQuestionButton.clicked -= HandleNextQuestionClicked;
        }

        /*
         * Các callback đáp án được tạo bằng lambda có captured index.
         * UIDocument thường chỉ tồn tại trong scene hiện tại nên callback
         * sẽ được giải phóng cùng UIDocument khi scene bị unload.
         */
    }

    private void CreateDemoQuestions()
    {
        questions.Clear();

        questions.Add(
            new QuizQuestionData(
                "Lesson 8 Mini-Quiz",
                "Electronics · Fundamentals",
                "Which component is primarily responsible for voltage " +
                "regulation in a power supply circuit?",
                new[]
                {
                    "Capacitor",
                    "Voltage Regulator",
                    "Diode",
                    "Resistor"
                },
                1
            )
        );

        questions.Add(
            new QuizQuestionData(
                "Lesson 8 Mini-Quiz",
                "Electronics · Components",
                "Which component stores electrical energy in an electric field?",
                new[]
                {
                    "Resistor",
                    "Transistor",
                    "Capacitor",
                    "Fuse"
                },
                2
            )
        );

        questions.Add(
            new QuizQuestionData(
                "Lesson 8 Mini-Quiz",
                "Electronics · Fundamentals",
                "What is the primary function of a resistor in a circuit?",
                new[]
                {
                    "To limit electric current",
                    "To generate electric current",
                    "To increase voltage",
                    "To store electrical energy"
                },
                0
            )
        );

        questions.Add(
            new QuizQuestionData(
                "Lesson 8 Mini-Quiz",
                "Electronics · Semiconductor",
                "Which component allows current to flow mainly in one direction?",
                new[]
                {
                    "Transformer",
                    "Capacitor",
                    "Resistor",
                    "Diode"
                },
                3
            )
        );

        questions.Add(
            new QuizQuestionData(
                "Lesson 8 Mini-Quiz",
                "Electronics · Measurement",
                "Which instrument is commonly used to measure voltage?",
                new[]
                {
                    "Ammeter",
                    "Voltmeter",
                    "Oscillator",
                    "Generator"
                },
                1
            )
        );
    }

    private void ShowQuestion(int questionIndex)
    {
        if (questions.Count == 0)
        {
            Debug.LogWarning("[DoQuizPageController] Không có câu hỏi.");

            if (nextQuestionButton != null)
            {
                nextQuestionButton.SetEnabled(false);
            }

            return;
        }

        if (questionIndex < 0 || questionIndex >= questions.Count)
        {
            Debug.LogError(
                $"[DoQuizPageController] Question index không hợp lệ: " +
                $"{questionIndex}"
            );

            return;
        }

        QuizQuestionData question = questions[questionIndex];

        selectedAnswerIndex = -1;

        ResetAnswerStyles();

        if (quizNameLabel != null)
        {
            quizNameLabel.text = question.QuizName;
        }

        if (questionNumberLabel != null)
        {
            questionNumberLabel.text =
                $"Question {questionIndex + 1} of {questions.Count}";
        }

        if (questionBadgeLabel != null)
        {
            questionBadgeLabel.text = $"Q{questionIndex + 1}";
        }

        if (questionCategoryLabel != null)
        {
            questionCategoryLabel.text = question.Category;
        }

        if (questionTextLabel != null)
        {
            questionTextLabel.text = question.QuestionText;
        }

        SetAnswerText(answerALabel, question.Answers, 0);
        SetAnswerText(answerBLabel, question.Answers, 1);
        SetAnswerText(answerCLabel, question.Answers, 2);
        SetAnswerText(answerDLabel, question.Answers, 3);

        if (nextQuestionButton != null)
        {
            bool isLastQuestion = questionIndex == questions.Count - 1;

            nextQuestionButton.text =
                isLastQuestion ? "Submit Quiz" : "Next Question →";

            nextQuestionButton.SetEnabled(false);
        }

        UpdateProgress();
    }

    private static void SetAnswerText(
        Label label,
        IReadOnlyList<string> answers,
        int index
    )
    {
        if (label == null)
        {
            return;
        }

        label.text = index >= 0 && index < answers.Count
            ? answers[index]
            : string.Empty;
    }

    private void HandleAnswerSelected(int answerIndex)
    {
        if (answerIndex < 0 || answerIndex >= answerButtons.Count)
        {
            return;
        }

        selectedAnswerIndex = answerIndex;

        ResetAnswerStyles();

        Button selectedButton = answerButtons[answerIndex];

        if (selectedButton != null)
        {
            selectedButton.AddToClassList(SelectedClass);
        }

        if (nextQuestionButton != null)
        {
            nextQuestionButton.SetEnabled(true);
        }
    }

    private void HandleNextQuestionClicked()
    {
        if (selectedAnswerIndex < 0)
        {
            return;
        }

        QuizQuestionData currentQuestion = questions[currentQuestionIndex];

        if (selectedAnswerIndex == currentQuestion.CorrectAnswerIndex)
        {
            correctAnswerCount++;
        }

        bool isLastQuestion =
            currentQuestionIndex >= questions.Count - 1;

        if (isLastQuestion)
        {
            SubmitQuiz();
            return;
        }

        currentQuestionIndex++;
        ShowQuestion(currentQuestionIndex);
    }

    private void SubmitQuiz()
    {
        float score = questions.Count > 0
            ? correctAnswerCount * 10f / questions.Count
            : 0f;

        PlayerPrefs.SetInt(
            "quiz_correct_count",
            correctAnswerCount
        );

        PlayerPrefs.SetInt(
            "quiz_total_questions",
            questions.Count
        );

        PlayerPrefs.SetFloat(
            "quiz_score",
            score
        );

        PlayerPrefs.Save();

        Debug.Log(
            $"[DoQuizPageController] Quiz submitted. " +
            $"Correct: {correctAnswerCount}/{questions.Count}. " +
            $"Score: {score:0.##}"
        );

        if (!string.IsNullOrWhiteSpace(resultSceneName) &&
            Application.CanStreamedLevelBeLoaded(resultSceneName))
        {
            SceneManager.LoadScene(resultSceneName);
            return;
        }

        Debug.LogWarning(
            $"[DoQuizPageController] Không tìm thấy scene " +
            $"'{resultSceneName}' trong Build Profiles."
        );

        nextQuestionButton.text =
            $"Completed: {correctAnswerCount}/{questions.Count}";

        nextQuestionButton.SetEnabled(false);

        SetAnswerButtonsEnabled(false);
    }

    private void UpdateProgress()
    {
        int totalQuestions = Mathf.Max(questions.Count, 1);

        float progressPercentage =
            (currentQuestionIndex + 1f) / totalQuestions * 100f;

        if (progressFill != null)
        {
            progressFill.style.width =
                new StyleLength(
                    new Length(
                        progressPercentage,
                        LengthUnit.Percent
                    )
                );
        }

        UpdateProgressDots();
    }

    private void UpdateProgressDots()
    {
        if (progressDotsContainer == null)
        {
            return;
        }

        progressDotsContainer.Clear();

        for (int index = 0; index < questions.Count; index++)
        {
            VisualElement dot = new VisualElement();
            dot.AddToClassList("progress-dot");

            if (index <= currentQuestionIndex)
            {
                dot.AddToClassList("progress-dot-active");
            }

            progressDotsContainer.Add(dot);
        }
    }

    private void ResetAnswerStyles()
    {
        foreach (Button button in answerButtons)
        {
            if (button == null)
            {
                continue;
            }

            button.RemoveFromClassList(SelectedClass);
            button.RemoveFromClassList(CorrectClass);
            button.RemoveFromClassList(WrongClass);
            button.SetEnabled(true);
        }
    }

    private void SetAnswerButtonsEnabled(bool enabled)
    {
        foreach (Button button in answerButtons)
        {
            button?.SetEnabled(enabled);
        }
    }

    private void HandleBackClicked()
    {
        if (!string.IsNullOrWhiteSpace(previousSceneName) &&
            Application.CanStreamedLevelBeLoaded(previousSceneName))
        {
            SceneManager.LoadScene(previousSceneName);
            return;
        }

        Debug.LogWarning(
            $"[DoQuizPageController] Không tìm thấy scene " +
            $"'{previousSceneName}' trong Build Profiles."
        );
    }

    [Serializable]
    private class QuizQuestionData
    {
        public string QuizName { get; }
        public string Category { get; }
        public string QuestionText { get; }
        public string[] Answers { get; }
        public int CorrectAnswerIndex { get; }

        public QuizQuestionData(
            string quizName,
            string category,
            string questionText,
            string[] answers,
            int correctAnswerIndex
        )
        {
            QuizName = quizName;
            Category = category;
            QuestionText = questionText;
            Answers = answers;
            CorrectAnswerIndex = correctAnswerIndex;
        }
    }
}