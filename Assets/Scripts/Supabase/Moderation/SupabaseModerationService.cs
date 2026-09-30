using System;
using System.Collections;
using System.Text;
using UnityEngine;

/// <summary>
/// Reports, admin moderation and notifications (2026-09).
///
/// All writes go through SECURITY DEFINER RPCs in Supabase that check the caller:
///  - users: submit_chat_report, submit_content_report, mark_notifications_read
///  - teachers: teacher_submit_explanation
///  - admin: admin_* functions (fail for non-admin accounts)
/// Chat messages are never modified: a chat report only stores a snapshot.
/// </summary>
public static class SupabaseModerationService
{
    // Reason keys accepted by the database.
    public static readonly string[] ChatReasons = { "harassment", "sexual", "spam", "scam", "other" };
    public static readonly string[] ContentReasons = { "sensitive", "not_educational", "other" };

    public static string ChatReasonLabel(string reason)
    {
        switch (reason)
        {
            case "harassment": return AppLanguageManager.T("Insults, harassment", "Ngôn từ xúc phạm, lăng mạ");
            case "sexual": return AppLanguageManager.T("Sexual / offensive content", "Nội dung đồi trụy, phản cảm");
            case "spam": return AppLanguageManager.T("Spam / annoying messages", "Spam / quấy rối");
            case "scam": return AppLanguageManager.T("Scam, asking for personal info", "Lừa đảo, xin thông tin cá nhân");
            default: return AppLanguageManager.T("Other", "Lý do khác");
        }
    }

    public static string ContentReasonLabel(string reason)
    {
        switch (reason)
        {
            case "sensitive": return AppLanguageManager.T("Sensitive / obscene content", "Nội dung nhạy cảm, đồi trụy");
            case "not_educational": return AppLanguageManager.T("Not suitable for teaching", "Không phù hợp để dạy học");
            default: return AppLanguageManager.T("Other", "Lý do khác");
        }
    }

    public static string TargetTypeLabel(string targetType)
    {
        switch (targetType)
        {
            case "quiz": return "Quiz";
            case "model_3d": return AppLanguageManager.T("3D Model", "Model 3D");
            default: return AppLanguageManager.T("Lesson", "Bài học");
        }
    }

    // ------------------------------------------------------------------
    // USER
    // ------------------------------------------------------------------

    public static IEnumerator SubmitChatReport(
        string conversationId, string reportedUserId, string reason, string description,
        Action onSuccess, Action<string> onError)
    {
        string body = "{" +
            Field("p_conversation_id", conversationId) + "," +
            Field("p_reported_user_id", reportedUserId) + "," +
            Field("p_reason", reason) + "," +
            Field("p_description", description) + "}";

        yield return Rpc("submit_chat_report", body, _ => onSuccess?.Invoke(), onError);
    }

    public static IEnumerator SubmitContentReport(
        string targetType, string lessonId, string quizId, string assetId, string reason, string description,
        Action onSuccess, Action<string> onError)
    {
        string body = "{" +
            Field("p_target_type", targetType) + "," +
            Field("p_lesson_id", lessonId) + "," +
            Field("p_quiz_id", quizId) + "," +
            Field("p_asset_id", assetId) + "," +
            Field("p_reason", reason) + "," +
            Field("p_description", description) + "}";

        yield return Rpc("submit_content_report", body, _ => onSuccess?.Invoke(), onError);
    }

    public static IEnumerator GetMyNotifications(int limit, Action<ModerationNotification[]> onSuccess, Action<string> onError)
    {
        string query = "notifications?select=id,type,title,body,is_read,created_at,data" +
                       $"&user_id=eq.{Uri.EscapeDataString(SupabaseSession.UserId)}" +
                       $"&order=created_at.desc&limit={Mathf.Max(1, limit)}";

        yield return SupabaseRestService.Get(query,
            json => onSuccess?.Invoke(ParseArray<ModerationNotificationArray, ModerationNotification>(json, w => w.items)),
            onError);
    }

    public static IEnumerator GetUnreadNotificationCount(Action<int> onSuccess, Action<string> onError)
    {
        string query = "notifications?select=id&is_read=eq.false" +
                       $"&user_id=eq.{Uri.EscapeDataString(SupabaseSession.UserId)}&limit=100";

        yield return SupabaseRestService.Get(query,
            json => onSuccess?.Invoke(ParseArray<ModerationNotificationArray, ModerationNotification>(json, w => w.items).Length),
            onError);
    }

    /// <summary>Marks one notification (or all when id is null) as read.</summary>
    public static IEnumerator MarkNotificationsRead(string notificationId, Action onSuccess, Action<string> onError)
    {
        yield return Rpc("mark_notifications_read", "{" + Field("p_id", notificationId) + "}", _ => onSuccess?.Invoke(), onError);
    }

    // ------------------------------------------------------------------
    // TEACHER
    // ------------------------------------------------------------------

    public static IEnumerator GetCase(string caseId, Action<ModerationCaseRecord> onSuccess, Action<string> onError)
    {
        string query = $"moderation_cases?id=eq.{Uri.EscapeDataString(caseId ?? string.Empty)}" +
                       "&select=*,teacher:profiles!moderation_cases_teacher_id_fkey(full_name,role)";

        yield return SupabaseRestService.Get(query, json =>
        {
            ModerationCaseRecord[] rows = ParseArray<ModerationCaseArray, ModerationCaseRecord>(json, w => w.items);
            onSuccess?.Invoke(rows.Length > 0 ? rows[0] : null);
        }, onError);
    }

    /// <summary>Open cases of the signed-in teacher (awaiting or submitted explanation).</summary>
    public static IEnumerator GetMyOpenCases(Action<ModerationCaseRecord[]> onSuccess, Action<string> onError)
    {
        string query = $"moderation_cases?teacher_id=eq.{Uri.EscapeDataString(SupabaseSession.UserId)}" +
                       "&status=in.(awaiting_explanation,explanation_submitted)&order=created_at.desc&select=*";

        yield return SupabaseRestService.Get(query,
            json => onSuccess?.Invoke(ParseArray<ModerationCaseArray, ModerationCaseRecord>(json, w => w.items)),
            onError);
    }

    public static IEnumerator SubmitExplanation(string caseId, string topic, string text, Action onSuccess, Action<string> onError)
    {
        string body = "{" + Field("p_case_id", caseId) + "," + Field("p_topic", topic) + "," + Field("p_text", text) + "}";
        yield return Rpc("teacher_submit_explanation", body, _ => onSuccess?.Invoke(), onError);
    }

    // ------------------------------------------------------------------
    // ADMIN
    // ------------------------------------------------------------------

    public static IEnumerator GetDashboardCounts(Action<AdminDashboardCounts> onSuccess, Action<string> onError)
    {
        yield return Rpc("admin_dashboard_counts", "{}", json =>
        {
            AdminDashboardCounts counts = null;
            try { counts = JsonUtility.FromJson<AdminDashboardCounts>(json); } catch { }
            onSuccess?.Invoke(counts ?? new AdminDashboardCounts());
        }, onError);
    }

    /// <summary>status: pending | warned | dismissed</summary>
    public static IEnumerator GetChatReports(string status, Action<ChatReportRecord[]> onSuccess, Action<string> onError)
    {
        string query = $"chat_reports?status=eq.{Uri.EscapeDataString(status)}&order=created_at.desc&limit=100" +
                       "&select=*,reporter:profiles!chat_reports_reporter_id_fkey(full_name,role)," +
                       "reported:profiles!chat_reports_reported_user_id_fkey(full_name,role)";

        yield return SupabaseRestService.Get(query,
            json => onSuccess?.Invoke(ParseArray<ChatReportArray, ChatReportRecord>(json, w => w.items)),
            onError);
    }

    /// <summary>How many warnings a user already received (shown to the admin as information only).</summary>
    public static IEnumerator GetWarningCount(string userId, Action<int> onSuccess, Action<string> onError)
    {
        string query = $"chat_reports?select=id&status=eq.warned&reported_user_id=eq.{Uri.EscapeDataString(userId ?? string.Empty)}";
        yield return SupabaseRestService.Get(query,
            json => onSuccess?.Invoke(ParseArray<ChatReportArray, ChatReportRecord>(json, w => w.items).Length),
            onError);
    }

    public static IEnumerator ResolveChatReport(string reportId, bool warn, string message, Action onSuccess, Action<string> onError)
    {
        string body = "{" + Field("p_report_id", reportId) + ",\"p_warn\":" + (warn ? "true" : "false") + "," + Field("p_message", message) + "}";
        yield return Rpc("admin_resolve_chat_report", body, _ => onSuccess?.Invoke(), onError);
    }

    public static IEnumerator GetContentQueue(Action<ContentQueueItem[]> onSuccess, Action<string> onError)
    {
        yield return Rpc("admin_list_content_queue", "{}",
            json => onSuccess?.Invoke(ParseArray<ContentQueueArray, ContentQueueItem>(json, w => w.items)),
            onError);
    }

    /// <summary>Cases in the given statuses (e.g. "explanation_submitted").</summary>
    public static IEnumerator GetCases(string statusCsv, Action<ModerationCaseRecord[]> onSuccess, Action<string> onError)
    {
        string query = $"moderation_cases?status=in.({statusCsv})&order=updated_at.desc&limit=100" +
                       "&select=*,teacher:profiles!moderation_cases_teacher_id_fkey(full_name,role)";

        yield return SupabaseRestService.Get(query,
            json => onSuccess?.Invoke(ParseArray<ModerationCaseArray, ModerationCaseRecord>(json, w => w.items)),
            onError);
    }

    public static IEnumerator GetContentReportsForTarget(ContentQueueItem item, Action<ContentReportRecord[]> onSuccess, Action<string> onError)
    {
        StringBuilder q = new StringBuilder("content_reports?status=in.(pending,in_review)&order=created_at.desc");
        q.Append("&target_type=eq.").Append(item.target_type);
        q.Append(string.IsNullOrEmpty(item.lesson_id) ? "&lesson_id=is.null" : "&lesson_id=eq." + item.lesson_id);
        q.Append(string.IsNullOrEmpty(item.quiz_id) ? "&quiz_id=is.null" : "&quiz_id=eq." + item.quiz_id);
        q.Append(string.IsNullOrEmpty(item.asset_id) ? "&asset_id=is.null" : "&asset_id=eq." + item.asset_id);
        q.Append("&select=id,reason,description,status,created_at,reporter:profiles!content_reports_reporter_id_fkey(full_name,role)");

        yield return SupabaseRestService.Get(q.ToString(),
            json => onSuccess?.Invoke(ParseArray<ContentReportArray, ContentReportRecord>(json, w => w.items)),
            onError);
    }

    public static IEnumerator GetContentDetail(string lessonId, string quizId, Action<AdminContentDetail> onSuccess, Action<string> onError)
    {
        string body = "{" + Field("p_lesson_id", lessonId) + "," + Field("p_quiz_id", quizId) + "}";
        yield return Rpc("admin_get_content_detail", body, json =>
        {
            AdminContentDetail detail = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(json) && json.Trim() != "null")
                    detail = JsonUtility.FromJson<AdminContentDetail>(json);
            }
            catch (Exception e) { Debug.LogWarning("[Moderation] Cannot parse content detail: " + e.Message); }
            onSuccess?.Invoke(detail);
        }, onError);
    }

    public static IEnumerator DismissContentReports(ContentQueueItem item, Action onSuccess, Action<string> onError)
    {
        string body = "{" + TargetFields(item) + "}";
        yield return Rpc("admin_dismiss_content_reports", body, _ => onSuccess?.Invoke(), onError);
    }

    public static IEnumerator RequestExplanation(ContentQueueItem item, string reason, string question, Action onSuccess, Action<string> onError)
    {
        string body = "{" + TargetFields(item) + "," + Field("p_reason", reason) + "," + Field("p_question", question) + "}";
        yield return Rpc("admin_request_explanation", body, _ => onSuccess?.Invoke(), onError);
    }

    public static IEnumerator DecideCase(string caseId, bool approve, string note, Action onSuccess, Action<string> onError)
    {
        string body = "{" + Field("p_case_id", caseId) + ",\"p_approve\":" + (approve ? "true" : "false") + "," + Field("p_note", note) + "}";
        yield return Rpc("admin_decide_case", body, _ => onSuccess?.Invoke(), onError);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string TargetFields(ContentQueueItem item)
    {
        return Field("p_target_type", item.target_type) + "," +
               Field("p_lesson_id", item.lesson_id) + "," +
               Field("p_quiz_id", item.quiz_id) + "," +
               Field("p_asset_id", item.asset_id);
    }

    private static IEnumerator Rpc(string function, string body, Action<string> onSuccess, Action<string> onError)
    {
        yield return SupabaseRestService.Post("rpc/" + function, body, onSuccess, onError, false);
    }

    /// <summary>"key": "value" or "key": null for empty values.</summary>
    public static string Field(string key, string value)
    {
        return "\"" + key + "\":" + (string.IsNullOrWhiteSpace(value) ? "null" : "\"" + Escape(value.Trim()) + "\"");
    }

    public static string Escape(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    public static TItem[] ParseArray<TWrapper, TItem>(string json, Func<TWrapper, TItem[]> getter)
    {
        string trimmed = json?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith("["))
            return Array.Empty<TItem>();

        try
        {
            TWrapper wrapper = JsonUtility.FromJson<TWrapper>("{\"items\":" + trimmed + "}");
            return getter(wrapper) ?? Array.Empty<TItem>();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Moderation] Cannot parse array: " + e.Message);
            return Array.Empty<TItem>();
        }
    }

    /// <summary>Friendly relative time from an ISO timestamp.</summary>
    public static string RelativeTime(string iso)
    {
        if (!DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime time))
            return string.Empty;

        TimeSpan elapsed = DateTime.UtcNow - time.ToUniversalTime();
        if (elapsed.TotalMinutes < 1) return AppLanguageManager.T("just now", "vừa xong");
        if (elapsed.TotalMinutes < 60) return AppLanguageManager.T($"{(int)elapsed.TotalMinutes} min ago", $"{(int)elapsed.TotalMinutes} phút trước");
        if (elapsed.TotalHours < 24) return AppLanguageManager.T($"{(int)elapsed.TotalHours} h ago", $"{(int)elapsed.TotalHours} giờ trước");
        if (elapsed.TotalDays < 2) return AppLanguageManager.T("yesterday", "Hôm qua");
        if (elapsed.TotalDays < 7) return AppLanguageManager.T($"{(int)elapsed.TotalDays} days ago", $"{(int)elapsed.TotalDays} ngày trước");
        return time.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }
}
