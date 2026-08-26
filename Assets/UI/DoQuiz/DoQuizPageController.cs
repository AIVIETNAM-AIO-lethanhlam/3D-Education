using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(SupabaseRuntimeRestService))]
public class DoQuizPageController : MonoBehaviour
{
    [Header("UI Document")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Scene Navigation")]
    [SerializeField] private string previousSceneName = "StartQuizScene";

    [Header("Scoring")]
    [SerializeField] private float maximumGrade = 10f;

    private SupabaseRuntimeRestService restService;

    private Button backButton;
    private Button nextQuestionButton;
    private Button resultBackButton;
    private Button reviewPrevButton;
    private Button reviewNextButton;

    private Button quizMenuButton;
    private Button quizDrawerCloseButton;
    private Button quizDrawerActionButton;
    private Button submitConfirmCancelButton;
    private Button submitConfirmSubmitButton;

    private Label quizNameLabel;
    private Label questionNumberLabel;
    private Label questionBadgeLabel;
    private Label questionCategoryLabel;
    private Label questionTextLabel;
    private Label quizMessageLabel;

    private Label answerALabel;
    private Label answerBLabel;
    private Label answerCLabel;
    private Label answerDLabel;

    private Label resultCorrectLabel;
    private Label resultScoreLabel;

    private VisualElement progressFill;
    private VisualElement progressDotsContainer;
    private VisualElement resultOverlay;
    private VisualElement reviewNavigation;
    private ScrollView quizScrollView;

    private VisualElement quizDrawerOverlay;
    private VisualElement quizDrawerScrim;
    private VisualElement quizDrawerQuestionGrid;
    private VisualElement submitConfirmOverlay;

    private Label quizDrawerTitle;
    private Label submitConfirmMessage;

    private readonly List<Button> answerButtons = new();
    private readonly List<QuizQuestionData> questions = new();

    // question_id -> selected option_id
    private readonly Dictionary<string, string> selectedOptionIds = new();

    // question_id -> selected A/B/C/D index
    private readonly Dictionary<string, int> selectedAnswerIndexes = new();

    private int currentQuestionIndex;
    private int selectedAnswerIndex = -1;
    private bool isLoading;
    private bool isSubmitting;
    private bool isReviewMode;

    // Review swipe navigation.
    // Swipe LEFT  -> next question.
    // Swipe RIGHT -> previous question.
    private bool isSwipeTracking;
    private Vector2 swipeStartPosition;

    private const float ReviewSwipeMinDistance = 70f;
    private const float ReviewSwipeHorizontalBias = 1.20f;

    private readonly Dictionary<string, string> reviewSelectedOptionByQuestion = new();
    private readonly Dictionary<string, string> reviewCorrectOptionByQuestion = new();

    private const string SelectedClass = "answer-button-selected";
    private const string ReviewSelectedClass = "answer-button-review-selected";
    private const string ReviewCorrectClass = "answer-button-review-correct";
    private const string ReviewWrongClass = "answer-button-review-wrong";

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
                "[DoQuizPageController] UIDocument is missing.",
                this
            );
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;

        CacheUIElements(root);
        RegisterCallbacks();

        currentQuestionIndex = 0;
        selectedAnswerIndex = -1;
        selectedOptionIds.Clear();
        selectedAnswerIndexes.Clear();
        reviewSelectedOptionByQuestion.Clear();
        reviewCorrectOptionByQuestion.Clear();

        isReviewMode =
            string.Equals(
                PlayerPrefs.GetString("quiz_mode", "attempt"),
                "review",
                StringComparison.OrdinalIgnoreCase
            );

        HideResult();
        CloseQuizDrawer();
        HideSubmitConfirmation();

        if (isReviewMode)
            StartCoroutine(LoadReviewModeRoutine());
        else
            StartCoroutine(LoadQuizFromSupabaseRoutine());
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
    }

    private void CacheUIElements(VisualElement root)
    {
        backButton = root.Q<Button>("back-button");
        nextQuestionButton = root.Q<Button>("next-question-button");
        resultBackButton = root.Q<Button>("result-back-button");
        reviewPrevButton = root.Q<Button>("review-prev-button");
        reviewNextButton = root.Q<Button>("review-next-button");

        quizMenuButton = root.Q<Button>("quiz-menu-button");
        quizDrawerCloseButton = root.Q<Button>("quiz-drawer-close-button");
        quizDrawerActionButton = root.Q<Button>("quiz-drawer-action-button");
        submitConfirmCancelButton = root.Q<Button>("submit-confirm-cancel-button");
        submitConfirmSubmitButton = root.Q<Button>("submit-confirm-submit-button");

        quizNameLabel = root.Q<Label>("quiz-name-label");
        questionNumberLabel = root.Q<Label>("question-number-label");
        questionBadgeLabel = root.Q<Label>("question-badge-label");
        questionCategoryLabel = root.Q<Label>("question-category-label");
        questionTextLabel = root.Q<Label>("question-text-label");
        quizMessageLabel = root.Q<Label>("quiz-message-label");

        answerALabel = root.Q<Label>("answer-a-label");
        answerBLabel = root.Q<Label>("answer-b-label");
        answerCLabel = root.Q<Label>("answer-c-label");
        answerDLabel = root.Q<Label>("answer-d-label");

        resultCorrectLabel = root.Q<Label>("result-correct-label");
        resultScoreLabel = root.Q<Label>("result-score-label");

        progressFill = root.Q<VisualElement>("progress-fill");
        progressDotsContainer =
            root.Q<VisualElement>("progress-dots-container");

        resultOverlay = root.Q<VisualElement>("result-overlay");
        reviewNavigation = root.Q<VisualElement>("review-navigation");
        quizScrollView = root.Q<ScrollView>("quiz-scroll-view");

        quizDrawerOverlay = root.Q<VisualElement>("quiz-drawer-overlay");
        quizDrawerScrim = root.Q<VisualElement>("quiz-drawer-scrim");
        quizDrawerQuestionGrid =
            root.Q<VisualElement>("quiz-drawer-question-grid");

        submitConfirmOverlay =
            root.Q<VisualElement>("submit-confirm-overlay");

        quizDrawerTitle =
            root.Q<Label>("quiz-drawer-title");
        submitConfirmMessage =
            root.Q<Label>("submit-confirm-message");

        answerButtons.Clear();
        answerButtons.Add(root.Q<Button>("answer-a-button"));
        answerButtons.Add(root.Q<Button>("answer-b-button"));
        answerButtons.Add(root.Q<Button>("answer-c-button"));
        answerButtons.Add(root.Q<Button>("answer-d-button"));
    }

    private void RegisterCallbacks()
    {
        if (backButton != null)
            backButton.clicked += HandleBackClicked;

        if (nextQuestionButton != null)
            nextQuestionButton.clicked += HandleNextQuestionClicked;

        if (resultBackButton != null)
            resultBackButton.clicked += ReturnToStartQuiz;

        if (reviewPrevButton != null)
            reviewPrevButton.clicked += HandleReviewPrevious;

        if (reviewNextButton != null)
            reviewNextButton.clicked += HandleReviewNext;

        if (quizMenuButton != null)
            quizMenuButton.clicked += OpenQuizDrawer;

        if (quizDrawerCloseButton != null)
            quizDrawerCloseButton.clicked += CloseQuizDrawer;

        if (quizDrawerActionButton != null)
            quizDrawerActionButton.clicked += HandleDrawerAction;

        if (submitConfirmCancelButton != null)
            submitConfirmCancelButton.clicked += HideSubmitConfirmation;

        if (submitConfirmSubmitButton != null)
            submitConfirmSubmitButton.clicked += ConfirmSubmitFromDrawer;

        if (quizDrawerScrim != null)
            quizDrawerScrim.RegisterCallback<ClickEvent>(
                HandleDrawerScrimClicked
            );

        if (quizScrollView != null)
        {
            quizScrollView.RegisterCallback<PointerDownEvent>(
                HandleReviewSwipePointerDown,
                TrickleDown.TrickleDown
            );

            quizScrollView.RegisterCallback<PointerUpEvent>(
                HandleReviewSwipePointerUp,
                TrickleDown.TrickleDown
            );

            quizScrollView.RegisterCallback<PointerCancelEvent>(
                HandleReviewSwipePointerCancel,
                TrickleDown.TrickleDown
            );
        }

        for (int index = 0; index < answerButtons.Count; index++)
        {
            int capturedIndex = index;
            Button button = answerButtons[index];

            if (button != null)
            {
                button.clicked += () =>
                    HandleAnswerSelected(capturedIndex);
            }
        }
    }

    private void UnregisterCallbacks()
    {
        if (backButton != null)
            backButton.clicked -= HandleBackClicked;

        if (nextQuestionButton != null)
            nextQuestionButton.clicked -= HandleNextQuestionClicked;

        if (resultBackButton != null)
            resultBackButton.clicked -= ReturnToStartQuiz;

        if (reviewPrevButton != null)
            reviewPrevButton.clicked -= HandleReviewPrevious;

        if (reviewNextButton != null)
            reviewNextButton.clicked -= HandleReviewNext;

        if (quizMenuButton != null)
            quizMenuButton.clicked -= OpenQuizDrawer;

        if (quizDrawerCloseButton != null)
            quizDrawerCloseButton.clicked -= CloseQuizDrawer;

        if (quizDrawerActionButton != null)
            quizDrawerActionButton.clicked -= HandleDrawerAction;

        if (submitConfirmCancelButton != null)
            submitConfirmCancelButton.clicked -= HideSubmitConfirmation;

        if (submitConfirmSubmitButton != null)
            submitConfirmSubmitButton.clicked -= ConfirmSubmitFromDrawer;

        if (quizDrawerScrim != null)
            quizDrawerScrim.UnregisterCallback<ClickEvent>(
                HandleDrawerScrimClicked
            );

        if (quizScrollView != null)
        {
            quizScrollView.UnregisterCallback<PointerDownEvent>(
                HandleReviewSwipePointerDown,
                TrickleDown.TrickleDown
            );

            quizScrollView.UnregisterCallback<PointerUpEvent>(
                HandleReviewSwipePointerUp,
                TrickleDown.TrickleDown
            );

            quizScrollView.UnregisterCallback<PointerCancelEvent>(
                HandleReviewSwipePointerCancel,
                TrickleDown.TrickleDown
            );
        }

        isSwipeTracking = false;
    }

    private IEnumerator LoadQuizFromSupabaseRoutine()
    {
        if (isLoading)
            yield break;

        isLoading = true;
        questions.Clear();

        // Clear any placeholder text that exists in UXML so the student
        // never sees the old Electronics demo while Supabase is loading.
        ClearQuestionUI();
        SetMessage("Loading quiz...");

        if (restService == null)
        {
            FailLoading("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string quizId = PlayerPrefs.GetString(
            "selected_quiz_id",
            string.Empty
        );

        if (!Guid.TryParse(quizId, out _))
        {
            FailLoading(
                "selected_quiz_id is missing or invalid. " +
                "Open the quiz again from ShowLessonScene."
            );
            yield break;
        }

        string questionsJson = null;
        string error = null;

        string questionsPath =
            "rest/v1/quiz_questions" +
            "?select=id,quiz_id,question_text,question_order,explanation" +
            "&quiz_id=eq." + UnityWebRequest.EscapeURL(quizId) +
            "&order=question_order.asc";

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            questionsPath,
            null,
            null,
            value => questionsJson = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            FailLoading("Cannot load quiz questions: " + error);
            yield break;
        }

        QuizQuestionRowList questionWrapper =
            ParseList<QuizQuestionRowList>(questionsJson);

        if (questionWrapper?.items == null ||
            questionWrapper.items.Length == 0)
        {
            FailLoading("This quiz does not contain any questions.");
            yield break;
        }

        string optionsJson = null;
        error = null;

        // Do not query quiz_options directly from the student client.
        // RLS correctly hides that table because it also contains is_correct.
        //
        // Instead call a SECURITY DEFINER RPC which returns only:
        // id, question_id, option_key, option_text, image_url.
        QuizOptionsRpcPayload optionsPayload =
            new QuizOptionsRpcPayload
            {
                p_quiz_id = quizId
            };

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "rest/v1/rpc/get_quiz_options_for_student",
            JsonUtility.ToJson(optionsPayload),
            null,
            value => optionsJson = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            FailLoading(
                "Cannot load quiz options from get_quiz_options_for_student RPC: " +
                error
            );
            yield break;
        }

        QuizOptionStudentRowList optionWrapper =
            ParseList<QuizOptionStudentRowList>(optionsJson);

        QuizOptionStudentRow[] optionRows =
            optionWrapper?.items ?? Array.Empty<QuizOptionStudentRow>();

        Debug.Log(
            $"[DoQuizPageController] Supabase returned " +
            $"{questionWrapper.items.Length} question row(s) and " +
            $"{optionRows.Length} option row(s)."
        );

        string quizTitle = PlayerPrefs.GetString(
            "selected_quiz_title",
            "Quiz"
        );

        string category = PlayerPrefs.GetString(
            "selected_quiz_subtitle",
            string.Empty
        );

        foreach (QuizQuestionRow row in questionWrapper.items)
        {
            if (row == null)
                continue;

            QuizOptionStudentRow[] options =
                optionRows
                    .Where(option =>
                        option != null &&
                        option.question_id == row.id)
                    .OrderBy(option =>
                        OptionKeyToIndex(option.option_key))
                    .ToArray();

            if (options.Length != 4)
            {
                Debug.LogWarning(
                    $"[DoQuizPageController] Question {row.id} has " +
                    $"{options.Length} visible options; expected 4."
                );
                continue;
            }

            questions.Add(
                new QuizQuestionData(
                    row.id,
                    quizTitle,
                    category,
                    row.question_text,
                    options
                )
            );
        }

        if (questions.Count == 0)
        {
            FailLoading(
                "No complete A/B/C/D question could be loaded."
            );
            yield break;
        }

        PlayerPrefs.SetInt(
            "selected_quiz_questions",
            questions.Count
        );
        PlayerPrefs.Save();

        isLoading = false;
        SetMessage(string.Empty);

        currentQuestionIndex = 0;
        ShowQuestion(currentQuestionIndex);

        Debug.Log(
            $"[DoQuizPageController] Loaded {questions.Count} " +
            $"questions for quiz {quizId}."
        );
    }

    private IEnumerator LoadReviewModeRoutine()
    {
        isLoading = true;
        questions.Clear();
        ClearQuestionUI();
        SetMessage("Loading review...");

        if (restService == null)
        {
            FailLoading("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string quizId = PlayerPrefs.GetString(
            "selected_quiz_id",
            string.Empty
        );

        string attemptId = PlayerPrefs.GetString(
            "selected_attempt_id",
            string.Empty
        );

        if (!Guid.TryParse(quizId, out _) ||
            !Guid.TryParse(attemptId, out _))
        {
            FailLoading(
                "Quiz ID or attempt ID is missing/invalid."
            );
            yield break;
        }

        string questionsJson = null;
        string error = null;

        string questionsPath =
            "rest/v1/quiz_questions" +
            "?select=id,quiz_id,question_text,question_order,explanation" +
            "&quiz_id=eq." + UnityWebRequest.EscapeURL(quizId) +
            "&order=question_order.asc";

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbGET,
            questionsPath,
            null,
            null,
            value => questionsJson = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            FailLoading(
                "Cannot load review questions: " + error
            );
            yield break;
        }

        QuizQuestionRowList questionWrapper =
            ParseList<QuizQuestionRowList>(questionsJson);

        if (questionWrapper?.items == null ||
            questionWrapper.items.Length == 0)
        {
            FailLoading(
                "This quiz does not contain any questions."
            );
            yield break;
        }

        string optionsJson = null;
        error = null;

        QuizOptionsRpcPayload optionsPayload =
            new QuizOptionsRpcPayload
            {
                p_quiz_id = quizId
            };

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "rest/v1/rpc/get_quiz_options_for_student",
            JsonUtility.ToJson(optionsPayload),
            null,
            value => optionsJson = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            FailLoading(
                "Cannot load review options: " + error
            );
            yield break;
        }

        QuizOptionStudentRowList optionWrapper =
            ParseList<QuizOptionStudentRowList>(optionsJson);

        QuizOptionStudentRow[] optionRows =
            optionWrapper?.items ??
            Array.Empty<QuizOptionStudentRow>();

        string quizTitle = PlayerPrefs.GetString(
            "selected_quiz_title",
            "Quiz"
        );

        string category = PlayerPrefs.GetString(
            "selected_quiz_subtitle",
            string.Empty
        );

        foreach (QuizQuestionRow row in questionWrapper.items)
        {
            if (row == null)
                continue;

            QuizOptionStudentRow[] options =
                optionRows
                    .Where(option =>
                        option != null &&
                        option.question_id == row.id)
                    .OrderBy(option =>
                        OptionKeyToIndex(option.option_key))
                    .ToArray();

            if (options.Length != 4)
                continue;

            questions.Add(
                new QuizQuestionData(
                    row.id,
                    quizTitle,
                    category,
                    row.question_text,
                    options
                )
            );
        }

        if (questions.Count == 0)
        {
            FailLoading(
                "No complete review question could be loaded."
            );
            yield break;
        }

        string reviewJson = null;
        error = null;

        ReviewAttemptRpcPayload reviewPayload =
            new ReviewAttemptRpcPayload
            {
                p_attempt_id = attemptId
            };

        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "rest/v1/rpc/get_quiz_attempt_review",
            JsonUtility.ToJson(reviewPayload),
            null,
            value => reviewJson = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            FailLoading(
                "Cannot load attempt review: " + error
            );
            yield break;
        }

        ReviewAttemptRowList reviewWrapper =
            ParseList<ReviewAttemptRowList>(reviewJson);

        if (reviewWrapper?.items == null ||
            reviewWrapper.items.Length == 0)
        {
            FailLoading(
                "No saved responses were found for this attempt."
            );
            yield break;
        }

        foreach (ReviewAttemptRow row in reviewWrapper.items)
        {
            if (row == null ||
                string.IsNullOrWhiteSpace(row.question_id))
            {
                continue;
            }

            reviewSelectedOptionByQuestion[
                row.question_id
            ] = row.selected_option_id;

            reviewCorrectOptionByQuestion[
                row.question_id
            ] = row.correct_option_id;
        }

        isLoading = false;
        SetMessage(string.Empty);

        currentQuestionIndex = 0;
        ShowQuestion(currentQuestionIndex);

        Debug.Log(
            "[DoQuizPageController] Review loaded. " +
            $"Attempt: {attemptId}. Questions: {questions.Count}."
        );
    }

    private void ApplyReviewAnswerStyles(
        QuizQuestionData question)
    {
        if (question == null)
            return;

        reviewSelectedOptionByQuestion.TryGetValue(
            question.QuestionId,
            out string selectedOptionId
        );

        reviewCorrectOptionByQuestion.TryGetValue(
            question.QuestionId,
            out string correctOptionId
        );

        for (int i = 0; i < question.Options.Length; i++)
        {
            QuizOptionStudentRow option =
                question.Options[i];

            Button button =
                i < answerButtons.Count
                    ? answerButtons[i]
                    : null;

            if (button == null || option == null)
                continue;

            bool isSelected =
                option.id == selectedOptionId;

            bool isCorrect =
                option.id == correctOptionId;

            // Review colors:
            // 1) Correct option is always green.
            // 2) If the student selected a wrong option, that selected option is red.
            // 3) If the student's selected option is also correct, keep GREEN only.
            //    Do not add the blue selected class because it would override green.
            if (isCorrect)
            {
                button.AddToClassList(
                    ReviewCorrectClass
                );
            }
            else if (isSelected)
            {
                button.AddToClassList(
                    ReviewWrongClass
                );
            }
        }
    }

    private void OpenQuizDrawer()
    {
        if (isLoading || questions.Count == 0)
            return;

        HideSubmitConfirmation();
        BuildDrawerQuestionGrid();

        if (quizDrawerTitle != null)
        {
            quizDrawerTitle.text =
                isReviewMode
                    ? "Kết quả câu hỏi"
                    : "Danh sách câu hỏi";
        }

        if (quizDrawerActionButton != null)
        {
            quizDrawerActionButton.text =
                isReviewMode
                    ? "Hoàn tất"
                    : "Nộp bài";
        }

        quizDrawerOverlay?.RemoveFromClassList("hidden");
    }

    private void CloseQuizDrawer()
    {
        quizDrawerOverlay?.AddToClassList("hidden");
    }

    private void HandleDrawerScrimClicked(
        ClickEvent evt)
    {
        CloseQuizDrawer();
    }

    private void BuildDrawerQuestionGrid()
    {
        if (quizDrawerQuestionGrid == null)
            return;

        quizDrawerQuestionGrid.Clear();

        for (int index = 0;
             index < questions.Count;
             index++)
        {
            int capturedIndex = index;
            QuizQuestionData question =
                questions[index];

            Button questionButton =
                new Button(
                    () =>
                    {
                        currentQuestionIndex = capturedIndex;
                        ShowQuestion(currentQuestionIndex);
                        ResetReviewScrollPosition();
                        CloseQuizDrawer();
                    }
                );

            questionButton.text =
                (index + 1).ToString();

            questionButton.AddToClassList(
                "quiz-drawer-question-button"
            );

            if (isReviewMode)
            {
                reviewSelectedOptionByQuestion.TryGetValue(
                    question.QuestionId,
                    out string selectedOptionId
                );

                reviewCorrectOptionByQuestion.TryGetValue(
                    question.QuestionId,
                    out string correctOptionId
                );

                bool isCorrect =
                    !string.IsNullOrWhiteSpace(selectedOptionId) &&
                    selectedOptionId == correctOptionId;

                questionButton.AddToClassList(
                    isCorrect
                        ? "quiz-drawer-question-correct"
                        : "quiz-drawer-question-wrong"
                );
            }
            else
            {
                bool hasAnswer =
                    selectedOptionIds.ContainsKey(
                        question.QuestionId
                    );

                questionButton.AddToClassList(
                    hasAnswer
                        ? "quiz-drawer-question-answered"
                        : "quiz-drawer-question-unanswered"
                );
            }

            if (index == currentQuestionIndex)
            {
                questionButton.AddToClassList(
                    "quiz-drawer-question-current"
                );
            }

            quizDrawerQuestionGrid.Add(
                questionButton
            );
        }
    }

    private void HandleDrawerAction()
    {
        if (isReviewMode)
        {
            CloseQuizDrawer();
            ReturnToStartQuiz();
            return;
        }

        ShowSubmitConfirmation();
    }

    private void ShowSubmitConfirmation()
    {
        int unanswered =
            Mathf.Max(
                questions.Count - selectedOptionIds.Count,
                0
            );

        if (submitConfirmMessage != null)
        {
            submitConfirmMessage.text =
                unanswered == 0
                    ? "Bạn có chắc chắn muốn nộp bài?"
                    : $"Bạn còn {unanswered} câu chưa chọn. " +
                      "Bạn có chắc chắn muốn nộp bài?";
        }

        submitConfirmOverlay?.RemoveFromClassList(
            "hidden"
        );
    }

    private void HideSubmitConfirmation()
    {
        submitConfirmOverlay?.AddToClassList(
            "hidden"
        );
    }

    private void ConfirmSubmitFromDrawer()
    {
        if (isReviewMode || isSubmitting)
            return;

        HideSubmitConfirmation();
        CloseQuizDrawer();

        StartCoroutine(
            SubmitQuizRoutine()
        );
    }

    private void HandleReviewSwipePointerDown(
        PointerDownEvent evt)
    {
        if (!isReviewMode ||
            isLoading ||
            isSubmitting)
        {
            isSwipeTracking = false;
            return;
        }

        isSwipeTracking = true;
        swipeStartPosition = new Vector2(
            evt.position.x,
            evt.position.y
        );
    }

    private void HandleReviewSwipePointerUp(
        PointerUpEvent evt)
    {
        if (!isReviewMode ||
            !isSwipeTracking ||
            isLoading ||
            isSubmitting)
        {
            isSwipeTracking = false;
            return;
        }

        isSwipeTracking = false;

        Vector2 endPosition = new Vector2(
            evt.position.x,
            evt.position.y
        );

        Vector2 delta =
            endPosition - swipeStartPosition;

        float horizontalDistance =
            Mathf.Abs(delta.x);

        float verticalDistance =
            Mathf.Abs(delta.y);

        // Ignore small drags/taps.
        if (horizontalDistance < ReviewSwipeMinDistance)
            return;

        // Keep normal vertical scrolling intact.
        // Only treat the gesture as question navigation when it is
        // clearly more horizontal than vertical.
        if (horizontalDistance <
            verticalDistance * ReviewSwipeHorizontalBias)
        {
            return;
        }

        if (delta.x < 0f)
        {
            // Finger/mouse moved from right to left.
            HandleReviewNext();
        }
        else
        {
            // Finger/mouse moved from left to right.
            HandleReviewPrevious();
        }
    }

    private void HandleReviewSwipePointerCancel(
        PointerCancelEvent evt)
    {
        isSwipeTracking = false;
    }

    private void HandleReviewPrevious()
    {
        if (!isReviewMode ||
            currentQuestionIndex <= 0)
        {
            return;
        }

        currentQuestionIndex--;
        ShowQuestion(currentQuestionIndex);
        ResetReviewScrollPosition();
    }

    private void HandleReviewNext()
    {
        if (!isReviewMode ||
            currentQuestionIndex >= questions.Count - 1)
        {
            return;
        }

        currentQuestionIndex++;
        ShowQuestion(currentQuestionIndex);
        ResetReviewScrollPosition();
    }

    private void ResetReviewScrollPosition()
    {
        if (quizScrollView == null)
            return;

        quizScrollView.scrollOffset =
            new Vector2(
                quizScrollView.scrollOffset.x,
                0f
            );
    }

    private void ShowQuestion(int questionIndex)
    {
        if (questions.Count == 0)
            return;

        if (questionIndex < 0 ||
            questionIndex >= questions.Count)
            return;

        QuizQuestionData question =
            questions[questionIndex];

        selectedAnswerIndex =
            selectedAnswerIndexes.TryGetValue(
                question.QuestionId,
                out int savedIndex)
                ? savedIndex
                : -1;

        ResetAnswerStyles();

        if (quizNameLabel != null)
            quizNameLabel.text = question.QuizName;

        if (questionNumberLabel != null)
        {
            questionNumberLabel.text =
                $"Question {questionIndex + 1} of {questions.Count}";
        }

        if (questionBadgeLabel != null)
            questionBadgeLabel.text = $"Q{questionIndex + 1}";

        if (questionCategoryLabel != null)
            questionCategoryLabel.text = question.Category;

        if (questionTextLabel != null)
            questionTextLabel.text = question.QuestionText;

        SetAnswerText(answerALabel, question.Options, 0);
        SetAnswerText(answerBLabel, question.Options, 1);
        SetAnswerText(answerCLabel, question.Options, 2);
        SetAnswerText(answerDLabel, question.Options, 3);

        if (isReviewMode)
        {
            SetAnswerButtonsEnabled(false);
            ApplyReviewAnswerStyles(question);

            reviewNavigation?.RemoveFromClassList("hidden");

            if (reviewPrevButton != null)
            {
                reviewPrevButton.text = "<";
                reviewPrevButton.SetEnabled(
                    questionIndex > 0
                );
            }

            if (reviewNextButton != null)
            {
                reviewNextButton.text = ">";
                reviewNextButton.SetEnabled(
                    questionIndex < questions.Count - 1
                );
            }

            if (nextQuestionButton != null)
            {
                nextQuestionButton.text = "Hoàn tất";
                nextQuestionButton.SetEnabled(true);
            }
        }
        else
        {
            reviewNavigation?.AddToClassList("hidden");

            if (selectedAnswerIndex >= 0 &&
                selectedAnswerIndex < answerButtons.Count)
            {
                answerButtons[selectedAnswerIndex]
                    ?.AddToClassList(SelectedClass);
            }

            if (nextQuestionButton != null)
            {
                bool isLastQuestion =
                    questionIndex == questions.Count - 1;

                nextQuestionButton.text =
                    isLastQuestion
                        ? "Submit Quiz"
                        : "Next Question →";

                nextQuestionButton.SetEnabled(
                    selectedAnswerIndex >= 0
                );
            }
        }

        UpdateProgress();

        if (quizDrawerOverlay != null &&
            !quizDrawerOverlay.ClassListContains("hidden"))
        {
            BuildDrawerQuestionGrid();
        }
    }

    private static void SetAnswerText(
        Label label,
        IReadOnlyList<QuizOptionStudentRow> options,
        int index)
    {
        if (label == null)
            return;

        // The answer label uses white-space: normal + flex-shrink in USS,
        // so long Supabase answer text automatically wraps and increases
        // the answer button height without creating horizontal overflow.
        label.text =
            index >= 0 && index < options.Count
                ? options[index].option_text
                : string.Empty;
    }

    private void HandleAnswerSelected(int answerIndex)
    {
        if (isReviewMode)
            return;

        if (isSubmitting ||
            currentQuestionIndex < 0 ||
            currentQuestionIndex >= questions.Count ||
            answerIndex < 0 ||
            answerIndex >= answerButtons.Count)
        {
            return;
        }

        QuizQuestionData question =
            questions[currentQuestionIndex];

        if (answerIndex >= question.Options.Length)
            return;

        selectedAnswerIndex = answerIndex;

        selectedAnswerIndexes[
            question.QuestionId
        ] = answerIndex;

        selectedOptionIds[
            question.QuestionId
        ] = question.Options[answerIndex].id;

        ResetAnswerStyles();

        answerButtons[answerIndex]
            ?.AddToClassList(SelectedClass);

        nextQuestionButton?.SetEnabled(true);

        if (quizDrawerOverlay != null &&
            !quizDrawerOverlay.ClassListContains("hidden"))
        {
            BuildDrawerQuestionGrid();
        }

        // No correct/wrong answer is shown before submission.
    }

    private void HandleNextQuestionClicked()
    {
        if (isReviewMode)
        {
            ReturnToStartQuiz();
            return;
        }

        if (isSubmitting ||
            selectedAnswerIndex < 0)
        {
            return;
        }

        bool isLastQuestion =
            currentQuestionIndex >= questions.Count - 1;

        if (isLastQuestion)
        {
            StartCoroutine(SubmitQuizRoutine());
            return;
        }

        currentQuestionIndex++;
        ShowQuestion(currentQuestionIndex);
    }

    private IEnumerator SubmitQuizRoutine()
    {
        if (isSubmitting)
            yield break;

        if (selectedOptionIds.Count != questions.Count)
        {
            SetMessage(
                "Please answer every question before submitting."
            );
            yield break;
        }

        isSubmitting = true;

        nextQuestionButton?.SetEnabled(false);
        SetAnswerButtonsEnabled(false);
        SetMessage("Submitting quiz...");

        string quizId = PlayerPrefs.GetString(
            "selected_quiz_id",
            string.Empty
        );

        if (!Guid.TryParse(quizId, out _))
        {
            FailSubmit("Quiz ID is invalid.");
            yield break;
        }

        DateTime submittedAt = DateTime.UtcNow;

        string startedAt = PlayerPrefs.GetString(
            "quiz_started_at",
            submittedAt.ToString("O")
        );

        if (!DateTime.TryParse(
                startedAt,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTime parsedStarted))
        {
            parsedStarted = submittedAt;
            startedAt = parsedStarted.ToString("O");
        }

        QuizSubmitSelection[] selections =
            questions
                .Select(question =>
                    new QuizSubmitSelection
                    {
                        question_id = question.QuestionId,
                        selected_option_id =
                            selectedOptionIds[question.QuestionId]
                    })
                .ToArray();

        SubmitQuizRpcPayload payload =
            new SubmitQuizRpcPayload
            {
                p_quiz_id = quizId,
                p_started_at = startedAt,
                p_responses = selections
            };

        string submitJson = null;
        string error = null;

        // IMPORTANT:
        // Grading now happens entirely inside Supabase.
        // The Unity client never reads quiz_options.is_correct.
        yield return restService.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "rest/v1/rpc/submit_quiz_attempt",
            JsonUtility.ToJson(payload),
            null,
            value => submitJson = value,
            message => error = message
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            FailSubmit(
                "Could not submit quiz: " + error
            );
            yield break;
        }

        SubmitQuizRpcResultList resultWrapper =
            ParseList<SubmitQuizRpcResultList>(submitJson);

        SubmitQuizRpcResult result =
            resultWrapper?.items != null &&
            resultWrapper.items.Length > 0
                ? resultWrapper.items[0]
                : null;

        if (result == null ||
            !Guid.TryParse(result.attempt_id, out _))
        {
            FailSubmit(
                "Supabase did not return a valid quiz result."
            );
            yield break;
        }

        float roundedScore = (float)Math.Round(
            result.score,
            2,
            MidpointRounding.AwayFromZero
        );

        PlayerPrefs.SetInt(
            "quiz_correct_count",
            result.correct_count
        );

        PlayerPrefs.SetInt(
            "quiz_total_questions",
            result.total_questions
        );

        PlayerPrefs.SetFloat(
            "quiz_score",
            roundedScore
        );

        PlayerPrefs.SetString(
            "selected_attempt_id",
            result.attempt_id
        );

        PlayerPrefs.Save();

        isSubmitting = false;
        SetMessage(string.Empty);

        ShowResult(
            result.correct_count,
            result.total_questions,
            roundedScore
        );

        Debug.Log(
            "[DoQuizPageController] Quiz submitted successfully. " +
            $"Attempt: {result.attempt_id}. " +
            $"Correct: {result.correct_count}/{result.total_questions}. " +
            $"Score: {roundedScore:0.##}/{maximumGrade:0.##}"
        );
    }

    private void ShowResult(
        int correct,
        int total,
        float score)
    {
        CloseQuizDrawer();
        HideSubmitConfirmation();
        if (resultCorrectLabel != null)
        {
            resultCorrectLabel.text =
                $"Correct: {correct} / {total}";
        }

        if (resultScoreLabel != null)
        {
            resultScoreLabel.text =
                $"Score: {score:0.##} / {maximumGrade:0.##}";
        }

        resultOverlay?.RemoveFromClassList("hidden");
    }

    private void HideResult()
    {
        resultOverlay?.AddToClassList("hidden");
    }

    private void UpdateProgress()
    {
        int totalQuestions =
            Mathf.Max(questions.Count, 1);

        float progressPercentage =
            (currentQuestionIndex + 1f) /
            totalQuestions *
            100f;

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
            return;

        progressDotsContainer.Clear();

        for (int index = 0;
             index < questions.Count;
             index++)
        {
            VisualElement dot = new VisualElement();
            dot.AddToClassList("progress-dot");

            if (index <= currentQuestionIndex)
                dot.AddToClassList("progress-dot-active");

            progressDotsContainer.Add(dot);
        }
    }

    private void ResetAnswerStyles()
    {
        foreach (Button button in answerButtons)
        {
            if (button == null)
                continue;

            button.RemoveFromClassList(SelectedClass);
            button.RemoveFromClassList(ReviewSelectedClass);
            button.RemoveFromClassList(ReviewCorrectClass);
            button.RemoveFromClassList(ReviewWrongClass);

            button.SetEnabled(
                !isSubmitting && !isReviewMode
            );
        }
    }

    private void SetAnswerButtonsEnabled(
        bool enabled)
    {
        foreach (Button button in answerButtons)
        {
            button?.SetEnabled(enabled);
        }
    }

    private void SetMessage(string message)
    {
        if (quizMessageLabel == null)
            return;

        quizMessageLabel.text = message ?? string.Empty;

        if (string.IsNullOrWhiteSpace(message))
            quizMessageLabel.AddToClassList("hidden");
        else
            quizMessageLabel.RemoveFromClassList("hidden");
    }

    private void ClearQuestionUI()
    {
        if (quizNameLabel != null)
            quizNameLabel.text = "Quiz";

        if (questionNumberLabel != null)
            questionNumberLabel.text = "Loading...";

        if (questionBadgeLabel != null)
            questionBadgeLabel.text = string.Empty;

        if (questionCategoryLabel != null)
            questionCategoryLabel.text = string.Empty;

        if (questionTextLabel != null)
            questionTextLabel.text = string.Empty;

        if (answerALabel != null)
            answerALabel.text = string.Empty;

        if (answerBLabel != null)
            answerBLabel.text = string.Empty;

        if (answerCLabel != null)
            answerCLabel.text = string.Empty;

        if (answerDLabel != null)
            answerDLabel.text = string.Empty;

        SetAnswerButtonsEnabled(false);

        if (nextQuestionButton != null)
        {
            nextQuestionButton.text = "Next Question →";
            nextQuestionButton.SetEnabled(false);
        }
    }

    private void FailLoading(string message)
    {
        isLoading = false;
        SetMessage(message);

        nextQuestionButton?.SetEnabled(false);
        SetAnswerButtonsEnabled(false);

        Debug.LogError(
            "[DoQuizPageController] " + message
        );
    }

    private void FailSubmit(string message)
    {
        isSubmitting = false;
        SetMessage(message);

        SetAnswerButtonsEnabled(true);

        if (nextQuestionButton != null)
        {
            nextQuestionButton.SetEnabled(
                selectedAnswerIndex >= 0
            );
        }

        Debug.LogError(
            "[DoQuizPageController] " + message
        );
    }

    private void HandleBackClicked()
    {
        if (isSubmitting)
            return;

        if (submitConfirmOverlay != null &&
            !submitConfirmOverlay.ClassListContains("hidden"))
        {
            HideSubmitConfirmation();
            return;
        }

        if (quizDrawerOverlay != null &&
            !quizDrawerOverlay.ClassListContains("hidden"))
        {
            CloseQuizDrawer();
            return;
        }

        ReturnToStartQuiz();
    }

    private void ReturnToStartQuiz()
    {
        if (isReviewMode)
        {
            PlayerPrefs.SetString(
                "quiz_mode",
                "attempt"
            );
            PlayerPrefs.Save();
        }

        if (!string.IsNullOrWhiteSpace(previousSceneName) &&
            Application.CanStreamedLevelBeLoaded(previousSceneName))
        {
            SceneManager.LoadScene(previousSceneName);
            return;
        }

        Debug.LogWarning(
            $"[DoQuizPageController] Scene '{previousSceneName}' " +
            "is not available in Build Profiles."
        );
    }

    private static int OptionKeyToIndex(
        string optionKey)
    {
        return (optionKey ?? string.Empty)
            .Trim()
            .ToUpperInvariant() switch
        {
            "A" => 0,
            "B" => 1,
            "C" => 2,
            "D" => 3,
            _ => 99
        };
    }

    private static T ParseList<T>(string json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonUtility.FromJson<T>(
                $"{{\"items\":{json}}}"
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[DoQuizPageController] JSON parse error: " +
                exception.Message
            );
            return null;
        }
    }

    private class QuizQuestionData
    {
        public string QuestionId { get; }
        public string QuizName { get; }
        public string Category { get; }
        public string QuestionText { get; }
        public QuizOptionStudentRow[] Options { get; }

        public QuizQuestionData(
            string questionId,
            string quizName,
            string category,
            string questionText,
            QuizOptionStudentRow[] options)
        {
            QuestionId = questionId;
            QuizName = quizName;
            Category = category;
            QuestionText = questionText;
            Options = options;
        }
    }
}

[Serializable]
public class QuizOptionsRpcPayload
{
    public string p_quiz_id;
}

[Serializable]
public class QuizQuestionRow
{
    public string id;
    public string quiz_id;
    public string question_text;
    public int question_order;
    public string explanation;
}

[Serializable]
public class QuizQuestionRowList
{
    public QuizQuestionRow[] items;
}

[Serializable]
public class QuizOptionStudentRow
{
    public string id;
    public string question_id;
    public string option_key;
    public string option_text;
    public string image_url;
}

[Serializable]
public class QuizOptionStudentRowList
{
    public QuizOptionStudentRow[] items;
}

[Serializable]
public class ReviewAttemptRpcPayload
{
    public string p_attempt_id;
}

[Serializable]
public class ReviewAttemptRow
{
    public string question_id;
    public string selected_option_id;
    public string correct_option_id;
    public bool is_correct;
}

[Serializable]
public class ReviewAttemptRowList
{
    public ReviewAttemptRow[] items;
}

[Serializable]
public class QuizSubmitSelection
{
    public string question_id;
    public string selected_option_id;
}

[Serializable]
public class SubmitQuizRpcPayload
{
    public string p_quiz_id;
    public string p_started_at;
    public QuizSubmitSelection[] p_responses;
}

[Serializable]
public class SubmitQuizRpcResult
{
    public string attempt_id;
    public int correct_count;
    public int total_questions;
    public float score;
}

[Serializable]
public class SubmitQuizRpcResultList
{
    public SubmitQuizRpcResult[] items;
}

