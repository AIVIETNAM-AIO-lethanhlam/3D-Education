using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// Teacher view of a quiz (2026-10): shows every question with its answer key and lets the
/// quiz teacher edit the question text, the A–D option texts, the correct option, and the
/// reference answer / key points of essay questions. Data comes from the security-definer
/// RPCs teacher_get_quiz_for_edit and teacher_update_quiz_question, which only accept the
/// teacher who owns the quiz. Students never receive this data: they keep using
/// get_quiz_options_for_student and only see the answers in review mode after submitting.
/// </summary>
public class TeacherQuizEditor
{
    private const int MaxOptions = 4;
    private static readonly string[] OptionKeys = { "A", "B", "C", "D" };

    private readonly MonoBehaviour host;
    private readonly SupabaseRuntimeRestService rest;
    private readonly string quizId;

    private readonly VisualElement section;
    private readonly Label titleLabel;
    private readonly Label hintLabel;
    private readonly Label messageLabel;
    private readonly VisualElement list;

    private TeacherQuizQuestion[] questions = Array.Empty<TeacherQuizQuestion>();
    private readonly HashSet<string> editingIds = new();
    private readonly HashSet<string> savingIds = new();
    private readonly Dictionary<string, EditState> editStates = new();
    private bool isLoading;
    private string loadError;

    private class EditState
    {
        public TextField questionField;
        public readonly List<TextField> optionFields = new();
        public readonly List<Button> optionKeyButtons = new();
        public string correctKey;
        public TextField referenceField;
        public TextField keyPointsField;
        public Label errorLabel;
        public Button saveButton;
    }

    public TeacherQuizEditor(
        MonoBehaviour host,
        SupabaseRuntimeRestService rest,
        VisualElement parent,
        VisualElement insertAfter,
        string quizId)
    {
        this.host = host;
        this.rest = rest;
        this.quizId = quizId;

        section = new VisualElement();
        section.AddToClassList("tq-section");

        VisualElement heading = new VisualElement();
        heading.AddToClassList("tq-heading");

        titleLabel = new Label();
        titleLabel.AddToClassList("tq-heading-title");
        heading.Add(titleLabel);

        VisualElement line = new VisualElement();
        line.AddToClassList("tq-heading-line");
        heading.Add(line);
        section.Add(heading);

        hintLabel = new Label();
        hintLabel.AddToClassList("tq-hint");
        section.Add(hintLabel);

        messageLabel = new Label();
        messageLabel.AddToClassList("tq-message");
        messageLabel.AddToClassList("hidden");
        section.Add(messageLabel);

        list = new VisualElement();
        list.AddToClassList("tq-list");
        section.Add(list);

        if (insertAfter != null && insertAfter.parent == parent)
            parent.Insert(parent.IndexOf(insertAfter) + 1, section);
        else
            parent.Add(section);

        Render();
    }

    private static string T(string english, string vietnamese) =>
        AppLanguageManager.IsVietnamese ? vietnamese : english;

    public void Load()
    {
        if (host != null)
            host.StartCoroutine(LoadRoutine());
    }

    /// <summary>Re-builds the texts after a language change; edits in progress are kept.</summary>
    public void Refresh()
    {
        Render();
    }

    // -----------------------------------------------------------------
    // Data
    // -----------------------------------------------------------------

    private IEnumerator LoadRoutine()
    {
        if (rest == null || !Guid.TryParse(quizId, out _))
        {
            loadError = T("Quiz not found.", "Không tìm thấy bài kiểm tra.");
            Render();
            yield break;
        }

        isLoading = true;
        loadError = null;
        Render();

        string response = null;
        string error = null;

        yield return rest.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "rest/v1/rpc/teacher_get_quiz_for_edit",
            "{\"p_quiz_id\":\"" + quizId + "\"}",
            null,
            value => response = value,
            message => error = message);

        isLoading = false;

        if (!string.IsNullOrWhiteSpace(error))
        {
            loadError = FriendlyError(error);
            Debug.LogWarning("[TeacherQuizEditor] Cannot load quiz: " + error);
            Render();
            yield break;
        }

        try
        {
            TeacherQuizQuestionList wrapper = JsonUtility.FromJson<TeacherQuizQuestionList>(
                "{\"items\":" + (string.IsNullOrWhiteSpace(response) ? "[]" : response) + "}");
            questions = wrapper?.items ?? Array.Empty<TeacherQuizQuestion>();
        }
        catch (Exception exception)
        {
            Debug.LogError("[TeacherQuizEditor] Cannot parse quiz: " + exception.Message);
            questions = Array.Empty<TeacherQuizQuestion>();
            loadError = T("Cannot read the quiz data.", "Không đọc được dữ liệu bài kiểm tra.");
        }

        Render();
    }

    private IEnumerator SaveRoutine(TeacherQuizQuestion question, EditState state)
    {
        TeacherQuizUpdatePayload payload = new TeacherQuizUpdatePayload
        {
            p_question_id = question.id,
            p_question_text = state.questionField?.value?.Trim() ?? string.Empty,
            p_options = Array.Empty<TeacherQuizOptionInput>(),
            p_correct_key = string.Empty,
            p_reference_answer = string.Empty,
            p_key_points = string.Empty
        };

        string validation = null;

        if (string.IsNullOrWhiteSpace(payload.p_question_text))
            validation = T("Enter the question.", "Hãy nhập nội dung câu hỏi.");

        if (IsEssay(question))
        {
            payload.p_reference_answer = state.referenceField?.value?.Trim() ?? string.Empty;
            payload.p_key_points = state.keyPointsField?.value?.Trim() ?? string.Empty;

            if (validation == null && string.IsNullOrWhiteSpace(payload.p_reference_answer))
                validation = T("Enter the reference answer.", "Hãy nhập đáp án gợi ý.");
        }
        else
        {
            List<TeacherQuizOptionInput> options = new();
            for (int i = 0; i < state.optionFields.Count; i++)
            {
                string text = state.optionFields[i].value?.Trim() ?? string.Empty;
                if (validation == null && string.IsNullOrEmpty(text))
                    validation = T($"Enter the text of option {OptionKeys[i]}.",
                        $"Hãy nhập nội dung đáp án {OptionKeys[i]}.");
                options.Add(new TeacherQuizOptionInput { option_key = OptionKeys[i], option_text = text });
            }

            payload.p_options = options.ToArray();
            payload.p_correct_key = state.correctKey ?? string.Empty;

            if (validation == null && options.Count < 2)
                validation = T("A question needs at least two options.", "Câu hỏi cần ít nhất hai đáp án.");
            if (validation == null && options.FindIndex(o => o.option_key == payload.p_correct_key) < 0)
                validation = T("Choose the correct option.", "Hãy chọn đáp án đúng.");
        }

        if (validation != null)
        {
            ShowCardError(state, validation);
            yield break;
        }

        savingIds.Add(question.id);
        ShowCardError(state, null);
        if (state.saveButton != null)
        {
            state.saveButton.SetEnabled(false);
            state.saveButton.text = T("Saving...", "Đang lưu...");
        }

        string response = null;
        string error = null;

        yield return rest.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "rest/v1/rpc/teacher_update_quiz_question",
            JsonUtility.ToJson(payload),
            null,
            value => response = value,
            message => error = message);

        savingIds.Remove(question.id);

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogWarning("[TeacherQuizEditor] Update failed: " + error);
            if (state.saveButton != null)
            {
                state.saveButton.SetEnabled(true);
                state.saveButton.text = T("Save", "Lưu");
            }
            ShowCardError(state, FriendlyError(error));
            yield break;
        }

        // Apply the saved values locally.
        question.question_text = payload.p_question_text;
        question.teacher_edited = true;

        if (IsEssay(question))
        {
            question.reference_answer = payload.p_reference_answer;
            question.key_points = payload.p_key_points;
        }
        else
        {
            List<TeacherQuizOption> updated = new();
            foreach (TeacherQuizOptionInput input in payload.p_options)
            {
                updated.Add(new TeacherQuizOption
                {
                    option_key = input.option_key,
                    option_text = input.option_text,
                    is_correct = input.option_key == payload.p_correct_key
                });
            }
            question.options = updated.ToArray();
        }

        int regraded = 0;
        try
        {
            TeacherQuizUpdateResult result = JsonUtility.FromJson<TeacherQuizUpdateResult>(response);
            regraded = result?.regraded_attempts ?? 0;
        }
        catch
        {
            // The result is informational only.
        }

        editingIds.Remove(question.id);
        editStates.Remove(question.id);

        string message = T($"Question {question.question_order} was updated.",
            $"Đã cập nhật câu {question.question_order}.");
        if (regraded > 0)
            message += T($" {regraded} submitted attempt(s) were re-graded.",
                $" Đã chấm lại {regraded} bài đã nộp.");

        ShowMessage(message, false);
        Render();
    }

    private static string FriendlyError(string error)
    {
        string e = error ?? string.Empty;
        if (e.Contains("not_quiz_teacher"))
            return T("Only the teacher of this quiz can view and edit the answers.",
                "Chỉ giáo viên của bài kiểm tra này mới được xem và chỉnh sửa đáp án.");
        if (e.Contains("question_text_required"))
            return T("Enter the question.", "Hãy nhập nội dung câu hỏi.");
        if (e.Contains("options_must_be_2_to_4"))
            return T("A question needs two to four options.", "Câu hỏi cần từ hai đến bốn đáp án.");
        if (e.Contains("option_invalid"))
            return T("Every option needs a text.", "Mỗi đáp án đều cần có nội dung.");
        if (e.Contains("correct_key_invalid"))
            return T("Choose the correct option.", "Hãy chọn đáp án đúng.");
        if (e.Contains("reference_answer_required"))
            return T("Enter the reference answer.", "Hãy nhập đáp án gợi ý.");
        if (e.Contains("question_not_found"))
            return T("This question no longer exists.", "Câu hỏi này không còn tồn tại.");
        return T("Cannot connect to the server. Please try again.",
            "Không kết nối được máy chủ. Vui lòng thử lại.");
    }

    private static bool IsEssay(TeacherQuizQuestion question) =>
        string.Equals(question?.question_type, "essay", StringComparison.OrdinalIgnoreCase);

    // -----------------------------------------------------------------
    // Rendering
    // -----------------------------------------------------------------

    private void Render()
    {
        titleLabel.text = T("QUESTIONS & ANSWER KEY", "CÂU HỎI & ĐÁP ÁN");
        hintLabel.text = T(
            "Only you can see the answers. Tap Edit to change a question; students see the changes immediately.",
            "Chỉ giáo viên được xem đáp án. Nhấn Sửa để chỉnh câu hỏi; học sinh sẽ thấy nội dung mới ngay.");

        list.Clear();

        if (isLoading)
        {
            list.Add(MakeInfo(T("Loading questions...", "Đang tải câu hỏi...")));
            return;
        }

        if (!string.IsNullOrEmpty(loadError))
        {
            list.Add(MakeInfo(loadError));
            return;
        }

        if (questions.Length == 0)
        {
            list.Add(MakeInfo(T("This quiz has no questions.", "Bài kiểm tra chưa có câu hỏi.")));
            return;
        }

        foreach (TeacherQuizQuestion question in questions)
        {
            if (question == null) continue;
            list.Add(editingIds.Contains(question.id) ? BuildEditCard(question) : BuildViewCard(question));
        }
    }

    private static Label MakeInfo(string text)
    {
        Label label = new Label(text);
        label.AddToClassList("tq-info");
        return label;
    }

    private VisualElement BuildCardHeader(TeacherQuizQuestion question, VisualElement card, bool editing)
    {
        VisualElement header = new VisualElement();
        header.AddToClassList("tq-card-header");

        VisualElement left = new VisualElement();
        left.AddToClassList("tq-card-header-left");

        Label number = new Label(T("Q", "Câu ") + question.question_order);
        number.AddToClassList("tq-number");
        left.Add(number);

        Label type = new Label(IsEssay(question)
            ? T("Essay", "Tự luận")
            : T("Multiple choice", "Trắc nghiệm"));
        type.AddToClassList("tq-chip");
        type.AddToClassList(IsEssay(question) ? "tq-chip-essay" : "tq-chip-mcq");
        left.Add(type);

        if (question.teacher_edited)
        {
            Label edited = new Label(T("Edited", "Đã sửa"));
            edited.AddToClassList("tq-chip");
            edited.AddToClassList("tq-chip-edited");
            left.Add(edited);
        }
        else if (string.Equals(question.answer_source, "ai_generated", StringComparison.OrdinalIgnoreCase))
        {
            Label ai = new Label(T("AI key", "Đáp án AI"));
            ai.AddToClassList("tq-chip");
            ai.AddToClassList("tq-chip-ai");
            left.Add(ai);
        }

        header.Add(left);

        if (!editing)
        {
            Button edit = new Button(() => BeginEdit(question)) { text = T("Edit", "Sửa") };
            edit.AddToClassList("tq-edit-button");
            header.Add(edit);
        }

        return header;
    }

    private VisualElement BuildViewCard(TeacherQuizQuestion question)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("tq-card");
        card.Add(BuildCardHeader(question, card, false));

        Label text = new Label(question.question_text);
        text.AddToClassList("tq-question-text");
        card.Add(text);

        if (IsEssay(question))
        {
            card.Add(MakeBlock(T("Reference answer", "Đáp án gợi ý"),
                string.IsNullOrWhiteSpace(question.reference_answer) ? "—" : question.reference_answer,
                "tq-block-answer"));

            if (!string.IsNullOrWhiteSpace(question.key_points))
                card.Add(MakeBlock(T("Key points", "Các ý chính"), question.key_points, "tq-block-points"));
        }
        else
        {
            foreach (TeacherQuizOption option in question.options ?? Array.Empty<TeacherQuizOption>())
            {
                if (option == null) continue;

                VisualElement row = new VisualElement();
                row.AddToClassList("tq-option");
                if (option.is_correct) row.AddToClassList("tq-option-correct");

                Label key = new Label(option.option_key);
                key.AddToClassList("tq-option-key");
                row.Add(key);

                Label optionText = new Label(option.option_text);
                optionText.AddToClassList("tq-option-text");
                row.Add(optionText);

                if (option.is_correct)
                {
                    Label tick = new Label(T("Correct", "Đúng"));
                    tick.AddToClassList("tq-correct-tag");
                    row.Add(tick);
                }

                card.Add(row);
            }
        }

        return card;
    }

    private static VisualElement MakeBlock(string title, string body, string extraClass)
    {
        VisualElement block = new VisualElement();
        block.AddToClassList("tq-block");
        block.AddToClassList(extraClass);

        Label titleLabel = new Label(title);
        titleLabel.AddToClassList("tq-block-title");
        block.Add(titleLabel);

        Label bodyLabel = new Label(body);
        bodyLabel.AddToClassList("tq-block-body");
        block.Add(bodyLabel);

        return block;
    }

    private void BeginEdit(TeacherQuizQuestion question)
    {
        editingIds.Add(question.id);
        editStates.Remove(question.id);
        ShowMessage(null, false);
        Render();
    }

    private void CancelEdit(TeacherQuizQuestion question)
    {
        if (savingIds.Contains(question.id)) return;
        editingIds.Remove(question.id);
        editStates.Remove(question.id);
        Render();
    }

    private VisualElement BuildEditCard(TeacherQuizQuestion question)
    {
        // Keep values typed before a re-render (e.g. language change).
        editStates.TryGetValue(question.id, out EditState previous);
        EditState state = new EditState();
        editStates[question.id] = state;

        VisualElement card = new VisualElement();
        card.AddToClassList("tq-card");
        card.AddToClassList("tq-card-editing");
        card.Add(BuildCardHeader(question, card, true));

        card.Add(MakeFieldLabel(T("Question", "Câu hỏi")));
        state.questionField = MakeTextField(previous?.questionField?.value ?? question.question_text, true);
        card.Add(state.questionField);

        if (IsEssay(question))
        {
            card.Add(MakeFieldLabel(T("Reference answer", "Đáp án gợi ý (dùng để AI chấm)")));
            state.referenceField = MakeTextField(
                previous?.referenceField?.value ?? question.reference_answer, true);
            card.Add(state.referenceField);

            card.Add(MakeFieldLabel(T("Key points (one per line)", "Các ý chính (mỗi ý một dòng)")));
            state.keyPointsField = MakeTextField(previous?.keyPointsField?.value ?? question.key_points, true);
            card.Add(state.keyPointsField);
        }
        else
        {
            card.Add(MakeFieldLabel(T("Options — tap a letter to mark the correct answer",
                "Đáp án — chạm vào chữ cái để chọn đáp án đúng")));

            List<string> texts = new();
            string correct = previous?.correctKey;

            if (previous != null && previous.optionFields.Count > 0)
            {
                foreach (TextField field in previous.optionFields) texts.Add(field.value);
            }
            else
            {
                foreach (TeacherQuizOption option in question.options ?? Array.Empty<TeacherQuizOption>())
                {
                    if (option == null) continue;
                    texts.Add(option.option_text);
                    if (option.is_correct) correct = option.option_key;
                }
            }

            state.correctKey = correct;
            VisualElement optionsBox = new VisualElement();
            optionsBox.AddToClassList("tq-edit-options");
            card.Add(optionsBox);

            for (int i = 0; i < texts.Count && i < MaxOptions; i++)
                AddEditOptionRow(optionsBox, state, i, texts[i]);

            if (texts.Count < MaxOptions)
            {
                Button add = new Button { text = T("+ Add option", "+ Thêm đáp án") };
                add.AddToClassList("tq-add-option");
                add.clicked += () =>
                {
                    int index = state.optionFields.Count;
                    if (index >= MaxOptions) return;
                    AddEditOptionRow(optionsBox, state, index, string.Empty);
                    if (state.optionFields.Count >= MaxOptions) add.AddToClassList("hidden");
                };
                card.Add(add);
            }
        }

        state.errorLabel = new Label();
        state.errorLabel.AddToClassList("tq-card-error");
        state.errorLabel.AddToClassList("hidden");
        card.Add(state.errorLabel);

        VisualElement actions = new VisualElement();
        actions.AddToClassList("tq-actions");

        Button cancel = new Button(() => CancelEdit(question)) { text = T("Cancel", "Hủy") };
        cancel.AddToClassList("tq-cancel-button");
        actions.Add(cancel);

        state.saveButton = new Button(() =>
        {
            if (!savingIds.Contains(question.id) && host != null)
                host.StartCoroutine(SaveRoutine(question, state));
        })
        { text = T("Save", "Lưu") };
        state.saveButton.AddToClassList("tq-save-button");
        actions.Add(state.saveButton);

        card.Add(actions);
        return card;
    }

    private void AddEditOptionRow(VisualElement parent, EditState state, int index, string text)
    {
        string key = OptionKeys[index];

        VisualElement row = new VisualElement();
        row.AddToClassList("tq-edit-option");

        Button keyButton = new Button { text = key };
        keyButton.AddToClassList("tq-key-button");
        keyButton.tooltip = T("Mark as correct", "Chọn làm đáp án đúng");
        keyButton.clicked += () =>
        {
            state.correctKey = key;
            UpdateKeyButtons(state);
        };
        row.Add(keyButton);

        TextField field = MakeTextField(text, true);
        field.AddToClassList("tq-option-field");
        row.Add(field);

        parent.Add(row);
        state.optionFields.Add(field);
        state.optionKeyButtons.Add(keyButton);
        UpdateKeyButtons(state);
    }

    private static void UpdateKeyButtons(EditState state)
    {
        for (int i = 0; i < state.optionKeyButtons.Count; i++)
        {
            bool selected = OptionKeys[i] == state.correctKey;
            state.optionKeyButtons[i].EnableInClassList("tq-key-button-correct", selected);
            state.optionFields[i].EnableInClassList("tq-option-field-correct", selected);
        }
    }

    private static Label MakeFieldLabel(string text)
    {
        Label label = new Label(text);
        label.AddToClassList("tq-field-label");
        return label;
    }

    private static TextField MakeTextField(string value, bool multiline)
    {
        TextField field = new TextField
        {
            multiline = multiline,
            value = value ?? string.Empty,
            maxLength = 4000
        };
        field.AddToClassList("tq-input");
        field.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        return field;
    }

    private static void ShowCardError(EditState state, string message)
    {
        if (state?.errorLabel == null) return;
        state.errorLabel.text = message ?? string.Empty;
        state.errorLabel.EnableInClassList("hidden", string.IsNullOrEmpty(message));
    }

    private void ShowMessage(string message, bool isError)
    {
        messageLabel.text = message ?? string.Empty;
        messageLabel.EnableInClassList("hidden", string.IsNullOrEmpty(message));
        messageLabel.EnableInClassList("tq-message-error", isError);
    }
}

[Serializable]
public class TeacherQuizOption
{
    public string id;
    public string option_key;
    public string option_text;
    public bool is_correct;
}

[Serializable]
public class TeacherQuizQuestion
{
    public string id;
    public int question_order;
    public string question_text;
    public string question_type;
    public string answer_source;
    public bool teacher_edited;
    public string reference_answer;
    public string key_points;
    public TeacherQuizOption[] options;
}

[Serializable]
public class TeacherQuizQuestionList
{
    public TeacherQuizQuestion[] items;
}

[Serializable]
public class TeacherQuizOptionInput
{
    public string option_key;
    public string option_text;
}

[Serializable]
public class TeacherQuizUpdatePayload
{
    public string p_question_id;
    public string p_question_text;
    public TeacherQuizOptionInput[] p_options;
    public string p_correct_key;
    public string p_reference_answer;
    public string p_key_points;
}

[Serializable]
public class TeacherQuizUpdateResult
{
    public string question_id;
    public string question_type;
    public int regraded_attempts;
}
