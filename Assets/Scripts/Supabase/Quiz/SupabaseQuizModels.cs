using System;
using System.Collections.Generic;

// Payload gửi sang Edge Function (Phía Giáo viên)
[Serializable]
public class ParseQuizPayload
{
    public string lesson_id;
    public string teacher_id;
    public string title;
    public string pdf_url;
}

// Model lựa chọn A, B, C, D (Phía Học sinh)
[Serializable]
public class QuizOption
{
    public string id;
    public string option_key; // "A", "B", "C", "D"
    public string option_text;
}

// Model câu hỏi (Phía Học sinh)
[Serializable]
public class QuizQuestion
{
    public string id;
    public string question_text;
    public int question_order;
    public List<QuizOption> quiz_options;
}

// Wrapper bọc danh sách câu hỏi để parse JSON Array
[Serializable]
public class QuestionListWrapper
{
    public List<QuizQuestion> items;
}