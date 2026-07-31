using System;

[Serializable]
public class SupabaseClass
{
    public string id;
    public string category_id;
    public string teacher_id;
    public string class_name;
    public string description;
    public string class_code;
    public string created_at;
    public string updated_at;
    public string visibility;
    public string cover_image_url;
    public string cover_template;
}

[Serializable]
public class CreateClassRequest
{
    public string category_id;
    public string teacher_id;
    public string class_name;
    public string description;
    public string class_code;
    public string visibility;
    public string cover_image_url;
    public string cover_template;
}

[Serializable]
public class UpdateClassRequest
{
    public string category_id;
    public string class_name;
    public string description;
    public string class_code;
    public string visibility;
    public string cover_image_url;
    public string cover_template;
}

[Serializable]
public class SupabaseClassArrayWrapper
{
    public SupabaseClass[] items;
}

// One row returned by public.student_enrolled_classes_view.
[Serializable]
public class StudentEnrolledClass
{
    public string membership_id;
    public string student_id;
    public string joined_at;
    public string class_id;
    public string category_id;
    public string teacher_id;
    public string class_name;
    public string description;
    public string class_code;
    public string visibility;
    public string cover_image_url;
    public string cover_template;
    public string teacher_name;
    public string teacher_avatar_url;
    public float progress_percent;
}

[Serializable]
public class StudentEnrolledClassArrayWrapper
{
    public StudentEnrolledClass[] items;
}

// One row returned by public.student_active_quizzes_view.
[Serializable]
public class StudentActiveQuiz
{
    public string quiz_id;
    public string student_id;
    public string class_id;
    public string class_name;
    public string class_code;
    public string lesson_id;
    public string lesson_title;
    public string quiz_title;
    public string opens_at;
    public string closes_at;
    public string attempt_status;
    public float score;
    public bool has_score;
}

[Serializable]
public class StudentActiveQuizArrayWrapper
{
    public StudentActiveQuiz[] items;
}