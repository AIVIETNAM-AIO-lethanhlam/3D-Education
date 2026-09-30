using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// NotificationScene (2026-09): notifications from the admin for students and teachers.
///  - Students: warnings about chat messages, results of their reports.
///  - Teachers: explanation requests for reported content (+ form to explain),
///    accepted explanations and removed content.
/// There are no account lock / delete notifications.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class NotificationPageController : MonoBehaviour
{
    private const string FallbackScene = "MainHomeScene";

    private VisualElement root;
    private VisualElement main;
    private VisualElement overlayLayer;
    private ScrollView scroll;
    private Label headerSub;

    private readonly List<ModerationNotification> notifications = new();
    private readonly List<ModerationCaseRecord> openCases = new();
    private bool showUnreadOnly;
    private bool isLoading;

    private static string T(string en, string vi) => AppLanguageManager.T(en, vi);

    private void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        main = root.Q<VisualElement>("notification-main");
        overlayLayer = root.Q<VisualElement>("notification-overlay-layer");
        ModerationReportSheet.EnsureStyles(root);

        BuildShell();
        StartCoroutine(LoadAll());
    }

    // ------------------------------------------------------------------
    // Layout
    // ------------------------------------------------------------------

    private void BuildShell()
    {
        main.Clear();
        VisualElement header = AdminUI.Header(main, T("Notifications", "Thông báo"), string.Empty, GoBack);
        headerSub = header.Q<Label>(className: "adm-header-sub") ?? AdminUI.Text(header.Q<VisualElement>(className: "adm-header-text"), string.Empty, "adm-header-sub");
        scroll = AdminUI.Scroll(main);
    }

    private IEnumerator LoadAll()
    {
        if (isLoading) yield break;
        isLoading = true;

        if (!SupabaseSession.IsLoggedIn)
        {
            isLoading = false;
            Render(T("Please sign in again.", "Vui lòng đăng nhập lại."));
            yield break;
        }

        string error = null;
        ModerationNotification[] rows = null;
        yield return SupabaseModerationService.GetMyNotifications(100, r => rows = r, e => error = e);
        notifications.Clear();
        if (rows != null) notifications.AddRange(rows);

        openCases.Clear();
        if (SupabaseSession.IsTeacher)
        {
            ModerationCaseRecord[] cases = null;
            yield return SupabaseModerationService.GetMyOpenCases(c => cases = c, e => Debug.LogWarning("[Notifications] " + e));
            if (cases != null) openCases.AddRange(cases);
        }

        isLoading = false;
        Render(error);
    }

    private void Render(string error)
    {
        scroll.Clear();

        int unread = 0;
        foreach (ModerationNotification n in notifications) if (!n.is_read) unread++;
        if (headerSub != null)
            headerSub.text = unread > 0 ? T($"{unread} unread", $"{unread} chưa đọc") : T("All caught up", "Đã đọc hết");

        if (!string.IsNullOrWhiteSpace(error))
        {
            AdminUI.Empty(scroll, "bell", T("Cannot load notifications", "Không tải được thông báo"), error);
            return;
        }

        // Teacher to-do: explanations
        if (openCases.Count > 0)
        {
            AdminUI.Text(scroll, T("TO DO", "VIỆC CẦN LÀM"), "adm-section-label");
            foreach (ModerationCaseRecord c in openCases)
                RenderCaseCard(c);
            AdminUI.Box(scroll, "adm-gap");
        }

        // Filter chips + mark all read
        VisualElement filters = AdminUI.Box(scroll, "adm-row", "adm-row--wrap");
        AdminUI.Chip(filters, T("All", "Tất cả"), !showUnreadOnly, () => { showUnreadOnly = false; Render(null); });
        AdminUI.Chip(filters, T($"Unread · {unread}", $"Chưa đọc · {unread}"), showUnreadOnly, () => { showUnreadOnly = true; Render(null); });
        if (unread > 0)
            AdminUI.Chip(filters, T("Mark all read", "Đánh dấu đã đọc"), false, () => StartCoroutine(MarkAllRead()));

        int shown = 0;
        foreach (ModerationNotification n in notifications)
        {
            if (showUnreadOnly && n.is_read) continue;
            RenderNotification(n);
            shown++;
        }

        if (shown == 0)
            AdminUI.Empty(scroll, "bell", T("No notifications", "Chưa có thông báo"),
                T("Messages from the admin will appear here.", "Thông báo từ Admin sẽ hiển thị ở đây."));
    }

    private void RenderCaseCard(ModerationCaseRecord c)
    {
        bool submitted = c.status == "explanation_submitted";
        VisualElement card = AdminUI.Card(scroll, () => OpenExplanationForm(c), "adm-card--purple");
        VisualElement row = AdminUI.Box(card, "adm-row");
        AdminUI.IconBox(row, "explain", "purple");
        VisualElement text = AdminUI.Box(row, "adm-grow");
        AdminUI.Text(text, T("Explanation requested", "Yêu cầu giải trình nội dung"), "adm-body", "adm-body--strong");
        AdminUI.Text(text, $"{SupabaseModerationService.TargetTypeLabel(c.target_type)} · {c.target_title}" +
                           (string.IsNullOrWhiteSpace(c.class_name) ? string.Empty : " · " + c.class_name), "adm-caption");
        AdminUI.Badge(text, submitted
            ? T("Sent · waiting for admin", "Đã gửi · chờ admin xét duyệt")
            : T("Hidden from students", "Đang tạm ẩn với học sinh"), submitted ? "blue" : "purple");
        AdminUI.SmallButton(text, submitted ? T("Edit explanation", "Sửa giải trình") : T("Send explanation ›", "Gửi giải trình ›"),
            () => OpenExplanationForm(c), !submitted);
    }

    private void RenderNotification(ModerationNotification n)
    {
        string icon, color;
        switch (n.type)
        {
            case "chat_warning": icon = "warning"; color = "amber"; break;
            case "report_resolved":
            case "explanation_approved": icon = "check"; color = "green"; break;
            case "report_dismissed":
            case "report_received": icon = "check"; color = "blue"; break;
            case "explanation_request": icon = "explain"; color = "purple"; break;
            case "content_removed": icon = "delete"; color = "red"; break;
            case "class_enrolled": icon = "classes"; color = "green"; break;
            case "student_enrolled": icon = "students"; color = "blue"; break;
            case "lesson_new": icon = "book"; color = "blue"; break;
            case "lesson_updated": icon = "edit"; color = "purple"; break;
            case "quiz_new": icon = "quiz"; color = "green"; break;
            case "quiz_deadline": icon = "clock"; color = "red"; break;
            default: icon = "bell"; color = "blue"; break;
        }

        VisualElement card = n.is_read
            ? AdminUI.Card(scroll, () => OpenNotification(n))
            : AdminUI.Card(scroll, () => OpenNotification(n), "adm-card--unread");
        VisualElement row = AdminUI.Box(card, "adm-row");
        row.style.alignItems = Align.FlexStart;
        AdminUI.IconBox(row, icon, color);
        VisualElement text = AdminUI.Box(row, "adm-grow");
        VisualElement titleRow = AdminUI.Box(text, "adm-row");
        titleRow.style.alignItems = Align.FlexStart;
        Label titleLabel = AdminUI.Text(titleRow, Title(n), "adm-body", "adm-body--strong");
        titleLabel.style.flexGrow = 1;
        titleLabel.style.flexShrink = 1;
        titleLabel.style.flexBasis = 0;
        titleLabel.style.whiteSpace = WhiteSpace.Normal;
        if (!n.is_read)
        {
            VisualElement dot = AdminUI.Box(titleRow, "adm-dot");
            dot.style.marginTop = 6;
            dot.style.flexShrink = 0;
        }
        Label body = AdminUI.Text(text, Body(n), "adm-caption");
        body.style.marginTop = 4;
        AdminUI.Text(text, SupabaseModerationService.RelativeTime(n.created_at), "adm-caption", "adm-caption--small").style.marginTop = 4;
    }

    private static string Title(ModerationNotification n) =>
        AppLanguageManager.IsVietnamese || string.IsNullOrWhiteSpace(n.data?.title_en) ? n.title : n.data.title_en;

    private static string Body(ModerationNotification n) =>
        AppLanguageManager.IsVietnamese || string.IsNullOrWhiteSpace(n.data?.body_en) ? n.body : n.data.body_en;

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    private void OpenNotification(ModerationNotification n)
    {
        if (!n.is_read)
        {
            n.is_read = true;
            StartCoroutine(SupabaseModerationService.MarkNotificationsRead(n.id, null, e => Debug.LogWarning(e)));
        }

        if (n.type == "explanation_request" && !string.IsNullOrWhiteSpace(n.data?.case_id))
        {
            ModerationCaseRecord open = openCases.Find(c => c.id == n.data.case_id);
            if (open != null) { OpenExplanationForm(open); return; }
            StartCoroutine(OpenCaseById(n.data.case_id));
            return;
        }

        // Class activity (enrolment, lessons, quizzes): open the class.
        if (!string.IsNullOrWhiteSpace(n.data?.class_id) && IsClassActivity(n.type))
        {
            OpenClass(n.data.class_id, n.data.class_name);
            return;
        }

        OpenDetail(n);
    }

    private static bool IsClassActivity(string type) =>
        type == "class_enrolled" || type == "student_enrolled" || type == "lesson_new" ||
        type == "lesson_updated" || type == "quiz_new" || type == "quiz_deadline";

    private void OpenClass(string classId, string className)
    {
        const string sceneName = "ClassDetailScene";
        PlayerPrefs.SetString("selected_class_id", classId);
        PlayerPrefs.SetString("selected_class_name", className ?? string.Empty);
        PlayerPrefs.Save();

        if (Application.CanStreamedLevelBeLoaded(sceneName))
            SceneHistory.LoadScene(sceneName);
        else
            Debug.LogError(sceneName + " is not in Build Settings.");
    }

    private IEnumerator OpenCaseById(string caseId)
    {
        ModerationCaseRecord record = null;
        yield return SupabaseModerationService.GetCase(caseId, r => record = r, e => Debug.LogWarning(e));
        if (record != null && (record.status == "awaiting_explanation" || record.status == "explanation_submitted"))
            OpenExplanationForm(record);
        else
            Render(null);
    }

    private IEnumerator MarkAllRead()
    {
        yield return SupabaseModerationService.MarkNotificationsRead(null, null, e => Debug.LogWarning(e));
        foreach (ModerationNotification n in notifications) n.is_read = true;
        Render(null);
    }

    /// <summary>Full-screen detail (used for warnings and results).</summary>
    private void OpenDetail(ModerationNotification n)
    {
        VisualElement page = NewOverlayPage();
        AdminUI.Header(page, Title(n), SupabaseModerationService.RelativeTime(n.created_at), () => ClosePage(page));
        ScrollView body = AdminUI.Scroll(page);

        bool warning = n.type == "chat_warning";
        string icon = warning ? "warning" : n.type == "content_removed" ? "delete" : "check";
        string color = warning ? "amber" : n.type == "content_removed" ? "red" : "green";
        VisualElement top = AdminUI.Box(body);
        top.style.alignItems = Align.Center;
        top.style.marginTop = 12;
        top.style.marginBottom = 16;
        AdminUI.IconBox(top, icon, color, "large");
        Label title = AdminUI.Text(top, warning ? T("You received a warning", "Bạn nhận được một cảnh cáo") : Title(n), "adm-empty-title");
        title.style.fontSize = 19;

        VisualElement card = warning ? AdminUI.Card(body, null, "adm-card--amber") : AdminUI.Card(body);
        AdminUI.Text(card, warning ? T("MESSAGE FROM ADMIN", "LỜI NHẮN TỪ ADMIN") : T("DETAILS", "CHI TIẾT"), "adm-section-label");
        AdminUI.Text(card, Body(n), "adm-body");

        if (warning)
        {
            Label note = AdminUI.Text(body, T(
                "Please keep the learning community respectful. Your messages are not deleted.",
                "Hãy giữ môi trường học tập văn minh, tôn trọng mọi người."), "adm-caption");
            note.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        VisualElement footer = AdminUI.Box(page, "adm-footer");
        AdminUI.Btn(footer, T("Got it", "Đã hiểu"), "primary", () => ClosePage(page));
    }

    /// <summary>Teacher explains what the reported content teaches.</summary>
    private void OpenExplanationForm(ModerationCaseRecord c)
    {
        VisualElement page = NewOverlayPage();
        AdminUI.Header(page, T("Explain content", "Giải trình nội dung"),
            SupabaseModerationService.TargetTypeLabel(c.target_type) + " · " + c.target_title, () => ClosePage(page));
        ScrollView body = AdminUI.Scroll(page);

        VisualElement alert = AdminUI.Card(body, null, "adm-card--purple");
        AdminUI.Text(alert, T(
            "This content is hidden from students until the admin reviews your explanation. If the explanation is not accepted, the admin will delete the content.",
            "Nội dung đang tạm ẩn với học sinh cho tới khi admin xét duyệt lời giải trình. Nếu lời giải trình không hợp lệ, admin sẽ xóa nội dung này."), "adm-body");

        VisualElement info = AdminUI.Card(body);
        AdminUI.InfoRow(info, T("Content", "Nội dung"), c.target_title);
        AdminUI.InfoRow(info, T("Type", "Loại"), SupabaseModerationService.TargetTypeLabel(c.target_type));
        if (!string.IsNullOrWhiteSpace(c.class_name)) AdminUI.InfoRow(info, T("Class", "Lớp"), c.class_name);
        if (!string.IsNullOrWhiteSpace(c.reason)) AdminUI.InfoRow(info, T("Reason", "Lý do"), SupabaseModerationService.ContentReasonLabel(c.reason));

        if (!string.IsNullOrWhiteSpace(c.admin_question))
        {
            VisualElement q = AdminUI.Card(body);
            AdminUI.Text(q, T("ADMIN'S QUESTION", "CÂU HỎI CỦA ADMIN"), "adm-section-label");
            AdminUI.Text(q, c.admin_question, "adm-body");
        }

        AdminUI.Text(body, T("The content teaches about", "Nội dung giáo dục về"), "adm-body", "adm-body--strong");
        TextField topic = AdminUI.Field(body, T("e.g. Human anatomy – Biology 8", "Ví dụ: Giải phẫu cơ thể người – Sinh học 8"), false, c.explanation_topic);
        AdminUI.Text(body, T("Detailed explanation", "Lời giải thích chi tiết"), "adm-body", "adm-body--strong");
        TextField text = AdminUI.Field(body, T("Explain how this content is used in your lesson...", "Giải thích nội dung này được dùng trong bài học như thế nào..."), true, c.explanation_text);
        Label status = AdminUI.Text(body, string.Empty, "mod-status");

        VisualElement footer = AdminUI.Box(page, "adm-footer");
        Button send = AdminUI.Btn(footer, T("Send explanation", "Gửi giải trình"), "primary", null);
        send.clicked += () =>
        {
            if ((text.value ?? string.Empty).Trim().Length < 10)
            {
                status.AddToClassList("mod-status--error");
                status.text = T("Please write at least 10 characters.", "Vui lòng viết ít nhất 10 ký tự.");
                return;
            }

            send.SetEnabled(false);
            status.RemoveFromClassList("mod-status--error");
            status.text = T("Sending...", "Đang gửi...");
            StartCoroutine(SupabaseModerationService.SubmitExplanation(c.id, topic.value, text.value,
                () =>
                {
                    ClosePage(page);
                    ModerationReportSheet.ShowToast(root, this, T(
                        "Explanation sent. The admin will review it.",
                        "Đã gửi giải trình. Admin sẽ xem xét và phản hồi."));
                    StartCoroutine(LoadAll());
                },
                e =>
                {
                    send.SetEnabled(true);
                    status.AddToClassList("mod-status--error");
                    status.text = T("Cannot send: ", "Không gửi được: ") + e;
                }));
        };
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private VisualElement NewOverlayPage()
    {
        overlayLayer.pickingMode = PickingMode.Position;
        VisualElement page = AdminUI.Box(overlayLayer, "adm-page-overlay");
        return page;
    }

    private void ClosePage(VisualElement page)
    {
        page?.RemoveFromHierarchy();
        if (overlayLayer.childCount == 0) overlayLayer.pickingMode = PickingMode.Ignore;
        Render(null);
    }

    private void GoBack()
    {
        if (overlayLayer.childCount > 0)
        {
            ClosePage(overlayLayer[overlayLayer.childCount - 1]);
            return;
        }
        SceneHistory.GoBack(FallbackScene);
    }
}
