using System;

// =====================================================================
// Models for reports, moderation cases and notifications (2026-09).
// Field names match the Supabase columns / RPC JSON keys.
// =====================================================================

[Serializable]
public class ModerationNotificationData
{
    public string title_en;
    public string body_en;
    public string case_id;
    public string lesson_id;
    public string target_type;
    public string report_id;
    public string reason;
    // class activity notifications (2026-09)
    public string class_id;
    public string class_name;
    public string quiz_id;
    public string student_id;
}

[Serializable]
public class ModerationNotification
{
    public string id;
    public string type;
    public string title;
    public string body;
    public bool is_read;
    public string created_at;
    public ModerationNotificationData data;
}

[Serializable]
public class ModerationNotificationArray
{
    public ModerationNotification[] items;
}

[Serializable]
public class ChatSnapshotMessage
{
    public string id;
    public string sender_id;
    public string sender_name;
    public string content;
    public string message_type;
    public string created_at;
}

[Serializable]
public class ProfileNameRef
{
    public string full_name;
    public string role;
}

[Serializable]
public class ChatReportRecord
{
    public string id;
    public string reporter_id;
    public string reported_user_id;
    public string conversation_id;
    public string reason;
    public string description;
    public string status;
    public string admin_message;
    public string created_at;
    public string resolved_at;
    public ChatSnapshotMessage[] message_snapshot;
    public ProfileNameRef reporter;
    public ProfileNameRef reported;
    public string class_name;   // optional (demo data / future use)
}

[Serializable]
public class ChatReportArray
{
    public ChatReportRecord[] items;
}

[Serializable]
public class ContentQueueItem
{
    public string target_type;
    public string lesson_id;
    public string quiz_id;
    public string asset_id;
    public string target_title;
    public string class_name;
    public string teacher_id;
    public string teacher_name;
    public int report_count;
    public string[] reasons;
    public string latest_at;
    public string case_id;
    public string case_status;
}

[Serializable]
public class ContentQueueArray
{
    public ContentQueueItem[] items;
}

[Serializable]
public class ModerationCaseRecord
{
    public string id;
    public string target_type;
    public string lesson_id;
    public string quiz_id;
    public string asset_id;
    public string teacher_id;
    public string target_title;
    public string class_name;
    public string status;
    public string reason;
    public string admin_question;
    public string explanation_topic;
    public string explanation_text;
    public string explained_at;
    public string decision_note;
    public string created_at;
    public ProfileNameRef teacher;
}

[Serializable]
public class ModerationCaseArray
{
    public ModerationCaseRecord[] items;
}

[Serializable]
public class ContentReportRecord
{
    public string id;
    public string reason;
    public string description;
    public string status;
    public string created_at;
    public ProfileNameRef reporter;
}

[Serializable]
public class ContentReportArray
{
    public ContentReportRecord[] items;
}

[Serializable]
public class AdminDashboardCounts
{
    public int pending_chat_reports;
    public int pending_content_targets;
    public int awaiting_explanation;
    public int explanations_to_review;
    public int warnings_this_month;
}

[Serializable]
public class AdminContentLesson
{
    public string id;
    public string title;
    public string description;
    public string youtube_url;
    public string status;
    public bool moderation_hidden;
}

[Serializable]
public class AdminContentAsset
{
    public string id;
    public string asset_type;
    public string file_name;
    public string storage_bucket;
    public string storage_path;
}

[Serializable]
public class AdminQuizQuestion
{
    public string text;
    public string[] options;
}

[Serializable]
public class AdminQuizDetail
{
    public string id;
    public string title;
    public AdminQuizQuestion[] questions;
}

[Serializable]
public class AdminContentDetail
{
    public AdminContentLesson lesson;
    public string class_name;
    public string teacher_name;
    public string[] objectives;
    public AdminContentAsset[] assets;
    public AdminQuizDetail quiz;
}
