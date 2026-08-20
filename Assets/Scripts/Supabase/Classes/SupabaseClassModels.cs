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

// One row returned by public.class_detail_stats_view.
[Serializable]
public class ClassDetailStats
{
    public string class_id;
    public string class_name;
    public string class_code;
    public string teacher_id;
    public string teacher_name;
    public string teacher_avatar_url;
    public int student_count;
    public int lesson_count;
    public float average_score;
    public bool has_average_score;
}

[Serializable]
public class ClassDetailStatsArrayWrapper
{
    public ClassDetailStats[] items;
}

// One enrolled student in a class, returned from class_members with an embedded profile.
[Serializable]
public class ClassMemberStudent
{
    public string id;
    public string class_id;
    public string user_id;
    public string member_role;
    public string status;
    public string joined_at;
    public ClassMemberProfile profiles;

    [NonSerialized] public bool is_online;
    [NonSerialized] public string last_seen_at;
}

[Serializable]
public class ClassMemberProfile
{
    public string full_name;
    public string avatar_url;
    public string role;
}

[Serializable]
public class ClassMemberStudentArrayWrapper
{
    public ClassMemberStudent[] items;
}

[Serializable]
public class UserPresenceRecord
{
    public string user_id;
    public bool is_online;
    public string last_seen_at;
    public string updated_at;
}

[Serializable]
public class UserPresenceRecordArrayWrapper
{
    public UserPresenceRecord[] items;
}
