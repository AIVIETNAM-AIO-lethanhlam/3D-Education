using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// AdminScene – built to match the Figma file "3D Education – Admin Role":
///  A1 Tổng quan · A2 Báo cáo tin nhắn · A3 Chi tiết báo cáo · A4 Gửi cảnh cáo (sheet)
///  B1 Nội dung bị báo cáo · B2 Chi tiết + yêu cầu giải trình (sheet) · B3 Xét duyệt giải trình · B4 Xóa nội dung (sheet)
/// Rules: chat reports → dismiss or WARNING notification only (no lock/delete, messages untouched).
///        content reports → dismiss or ask the teacher to explain; invalid explanation → content deleted.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class AdminPageController : MonoBehaviour
{
    private const string LastTabKey = "admin_last_tab";
    private const string HomeSceneName = "HomeScene";
    private const string Mode3DSceneName = "Mode3DScene";
    private const string SignedUrlFunction = "r2-signed-url";

    private enum Tab { Overview, Chat, Content, Settings }

    private VisualElement root;
    private VisualElement main;
    private VisualElement nav;
    private VisualElement overlayLayer;

    private Tab currentTab = Tab.Overview;
    private string chatFilter = "pending";      // pending | warned | dismissed
    private string contentType = "all";         // all | lesson | quiz | model_3d
    private string contentStatus = "all";       // all | review | awaiting | done
    private int loadVersion;
    private readonly List<Tab> tabHistory = new List<Tab>();

    private static string T(string en, string vi) => AppLanguageManager.T(en, vi);

    // ------------------------------------------------------------------
    // Lifecycle + navigation
    // ------------------------------------------------------------------

    private void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        main = root.Q<VisualElement>("admin-main");
        nav = root.Q<VisualElement>("admin-nav");
        overlayLayer = root.Q<VisualElement>("admin-overlay-layer");
        ModerationReportSheet.EnsureStyles(root);

        if (!SupabaseSession.IsLoggedIn || !SupabaseSession.IsAdmin)
        {
            Debug.LogWarning("[Admin] Not signed in as admin. Returning to HomeScene.");
            SceneManager.LoadScene(HomeSceneName);
            return;
        }

        currentTab = (Tab)Mathf.Clamp(PlayerPrefs.GetInt(LastTabKey, 0), 0, 3);
        ShowTab(currentTab);
    }

    private void BuildNav()
    {
        nav.Clear();
        AddNavItem(Tab.Overview, "grid", T("Overview", "Tổng quan"));
        AddNavItem(Tab.Chat, "flag", T("Chat reports", "Báo cáo chat"));
        AddNavItem(Tab.Content, "square", T("Content", "Nội dung"));
        AddNavItem(Tab.Settings, "gear", T("Settings", "Cài đặt"));
    }

    private void AddNavItem(Tab tab, string icon, string label)
    {
        VisualElement item = AdminUI.Box(nav, "adm-nav-item");
        if (tab == currentTab) item.AddToClassList("adm-nav-item--active");
        AdminUI.Icon(item, icon);
        AdminUI.Text(item, label, "adm-nav-label").pickingMode = PickingMode.Ignore;
        item.RegisterCallback<ClickEvent>(_ => OpenTab(tab));
    }

    /// <summary>Navigate to a tab and remember the current one for the header back button.</summary>
    private void OpenTab(Tab tab)
    {
        if (tab != currentTab)
        {
            tabHistory.Add(currentTab);
            if (tabHistory.Count > 20) tabHistory.RemoveAt(0);
        }
        ShowTab(tab);
    }

    /// <summary>Header back button of the Chat / Content / Settings tabs: return to the previously opened tab.</summary>
    private void GoBackTab()
    {
        Tab previous = Tab.Overview;
        while (tabHistory.Count > 0)
        {
            previous = tabHistory[tabHistory.Count - 1];
            tabHistory.RemoveAt(tabHistory.Count - 1);
            if (previous != currentTab) break;
            previous = Tab.Overview;
        }
        ShowTab(previous);
    }

    private void ShowTab(Tab tab)
    {
        currentTab = tab;
        PlayerPrefs.SetInt(LastTabKey, (int)tab);
        CloseAllPages();
        BuildNav();
        main.Clear();
        loadVersion++;

        switch (tab)
        {
            case Tab.Overview: StartCoroutine(RenderOverview(loadVersion)); break;
            case Tab.Chat: StartCoroutine(RenderChatTab(loadVersion)); break;
            case Tab.Content: StartCoroutine(RenderContentTab(loadVersion)); break;
            default: RenderSettings(); break;
        }
    }

    private void Refresh() => ShowTab(currentTab);

    // ------------------------------------------------------------------
    // A1 · Tổng quan
    // ------------------------------------------------------------------

    private IEnumerator RenderOverview(int version)
    {
        VisualElement hero = AdminUI.Box(main, "adm-hero");
        VisualElement heroText = AdminUI.Box(hero, "adm-hero-text");
        AdminUI.Text(heroText, T("Hello, administrator", "Xin chào, Quản trị viên"), "adm-hero-caption");
        AdminUI.Text(heroText, T("Admin dashboard", "Bảng điều khiển Admin"), "adm-hero-title");
        VisualElement avatar = AdminUI.Box(hero, "adm-hero-avatar");
        AdminUI.Text(avatar, "AD", "adm-hero-avatar-label");

        ScrollView scroll = AdminUI.Scroll(main);
        Label loading = AdminUI.Text(scroll, T("Loading...", "Đang tải..."), "adm-caption");

        AdminDashboardCounts counts = null;
        string error = null;
        yield return SupabaseModerationService.GetDashboardCounts(c => counts = c, e => error = e);

        ChatReportRecord[] pendingChats = null;
        ModerationCaseRecord[] submitted = null;
        ContentQueueItem[] queue = null;
        yield return SupabaseModerationService.GetChatReports("pending", r => pendingChats = r, _ => { });
        yield return SupabaseModerationService.GetCases("explanation_submitted", c => submitted = c, _ => { });
        yield return SupabaseModerationService.GetContentQueue(q => queue = q, _ => { });
        if (version != loadVersion) yield break;
        loading.RemoveFromHierarchy();

        if (counts == null)
        {
            AdminUI.Empty(scroll, "alert", T("Cannot load data", "Không tải được dữ liệu"), error ?? string.Empty);
            yield break;
        }

        VisualElement row1 = AdminUI.Box(scroll, "adm-stat-row");
        StatCard(row1, false, "flag", "red", counts.pending_chat_reports,
            T("Chat reports pending", "Báo cáo tin nhắn chờ xử lý"),
            () => { chatFilter = "pending"; OpenTab(Tab.Chat); });
        StatCard(row1, true, "square", "amber", counts.pending_content_targets,
            T("Reported content", "Nội dung bị báo cáo"),
            () => { contentStatus = "all"; contentType = "all"; OpenTab(Tab.Content); });
        VisualElement row2 = AdminUI.Box(scroll, "adm-stat-row");
        StatCard(row2, false, "pencil", "purple", counts.awaiting_explanation,
            T("Waiting for teachers to explain", "Chờ giáo viên giải trình"),
            () => { contentStatus = "awaiting"; contentType = "all"; OpenTab(Tab.Content); });
        StatCard(row2, true, null, "blue", counts.warnings_this_month,
            T("Warnings sent (this month)", "Cảnh cáo đã gửi (tháng này)"),
            () => { chatFilter = "warned"; OpenTab(Tab.Chat); });

        AdminUI.Text(scroll, T("To do", "Việc cần làm"), "adm-section-title");

        // newest items that need the admin, mixed and sorted by time
        List<(DateTime time, Action build, int kind)> tasks = new List<(DateTime, Action, int)>();
        if (pendingChats != null)
            foreach (ChatReportRecord r in pendingChats)
            {
                ChatReportRecord rr = r;
                tasks.Add((ParseTime(rr.created_at), () => TaskItem(scroll, "flag", "red",
                    T("New report: ", "Báo cáo mới: ") + ShortChatReason(rr.reason).ToLowerInvariant(),
                    T($"{rr.reported?.full_name} reported by {rr.reporter?.full_name}", $"{rr.reported?.full_name} bị báo cáo bởi {rr.reporter?.full_name}") +
                    " · " + SupabaseModerationService.RelativeTime(rr.created_at),
                    () => { currentTab = Tab.Chat; OpenChatReport(rr, 0); }), 0));
            }
        if (submitted != null)
            foreach (ModerationCaseRecord c in submitted)
            {
                ModerationCaseRecord cc = c;
                tasks.Add((ParseTime(cc.explained_at ?? cc.created_at), () => TaskItem(scroll, "pencil", "purple",
                    T($"{cc.teacher?.full_name} sent an explanation", $"GV {cc.teacher?.full_name} đã gửi giải trình"),
                    $"{SupabaseModerationService.TargetTypeLabel(cc.target_type)} “{cc.target_title}” · " + SupabaseModerationService.RelativeTime(cc.explained_at ?? cc.created_at),
                    () => OpenCase(cc, 0)), 1));
            }
        if (queue != null)
            foreach (ContentQueueItem q in queue)
            {
                if (!string.IsNullOrWhiteSpace(q.case_id)) continue;
                ContentQueueItem qq = q;
                tasks.Add((ParseTime(qq.latest_at), () => TaskItem(scroll, "square", "amber",
                    T($"{SupabaseModerationService.TargetTypeLabel(qq.target_type)} reported: {ReasonsText(qq.reasons)}",
                      $"{SupabaseModerationService.TargetTypeLabel(qq.target_type)} bị báo cáo {FirstReasonLong(qq.reasons)}"),
                    (qq.class_name ?? string.Empty) + " · " + SupabaseModerationService.RelativeTime(qq.latest_at),
                    () => OpenQueueItem(qq)), 2));
            }

        // like Figma A1: the newest item of each kind (chat report, explanation, reported content)
        tasks.Sort((a, b) => b.time.CompareTo(a.time));
        bool[] shownKind = new bool[3];
        foreach (var task in tasks)
        {
            if (shownKind[task.kind]) continue;
            shownKind[task.kind] = true;
            task.build();
        }
        if (tasks.Count == 0)
            AdminUI.Empty(scroll, "check", T("All done", "Không có việc cần xử lý"), T("New reports will appear here.", "Báo cáo mới sẽ hiển thị ở đây."));
    }

    private void StatCard(VisualElement parent, bool right, string glyph, string color, int value, string label, Action onClick)
    {
        VisualElement card = AdminUI.Box(parent, "adm-stat-card");
        if (right) card.AddToClassList("adm-stat-card--right");
        card.RegisterCallback<ClickEvent>(_ => onClick());
        if (glyph != null) AdminUI.GlyphBox(card, glyph, color, "stat");
        else AdminUI.TextGlyphBox(card, "!", color, "stat");
        AdminUI.Text(card, value.ToString(), "adm-stat-value", "adm-text--" + color);
        AdminUI.Text(card, label, "adm-stat-label");
    }

    private static void TaskItem(VisualElement parent, string glyph, string color, string title, string sub, Action onClick)
    {
        VisualElement item = AdminUI.Box(parent, "adm-task");
        item.RegisterCallback<ClickEvent>(_ => onClick());
        AdminUI.GlyphBox(item, glyph, color);
        VisualElement text = AdminUI.Box(item, "adm-task-text");
        AdminUI.Text(text, title, "adm-task-title");
        AdminUI.Text(text, sub, "adm-task-sub");
        AdminUI.Text(item, "›", "adm-chevron");
    }

    // ------------------------------------------------------------------
    // A2 · Báo cáo tin nhắn
    // ------------------------------------------------------------------

    private IEnumerator RenderChatTab(int version)
    {
        VisualElement header = AdminUI.Header(main, T("Chat reports", "Báo cáo tin nhắn"), T("Loading...", "Đang tải..."), GoBackTab, true);
        AdminUI.HeaderAction(header, "filter", () => Refresh());
        Label sub = header.Q<Label>(className: "adm-header-sub");
        VisualElement tabs = AdminUI.Box(main, "adm-tab-row");
        ScrollView scroll = AdminUI.Scroll(main);
        scroll.AddToClassList("adm-scroll--tight");

        ChatReportRecord[] pending = null, warned = null, dismissed = null;
        string error = null;
        yield return SupabaseModerationService.GetChatReports("pending", r => pending = r, e => error = e);
        yield return SupabaseModerationService.GetChatReports("warned", r => warned = r, e => error = e);
        yield return SupabaseModerationService.GetChatReports("dismissed", r => dismissed = r, e => error = e);
        if (version != loadVersion) yield break;

        pending ??= new ChatReportRecord[0];
        warned ??= new ChatReportRecord[0];
        dismissed ??= new ChatReportRecord[0];
        if (pending.Length + warned.Length + dismissed.Length > 0) error = null;
        Dictionary<string, int> warnCounts = new Dictionary<string, int>();
        foreach (ChatReportRecord w in warned)
        {
            string key = w.reported_user_id ?? string.Empty;
            warnCounts[key] = warnCounts.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        if (sub != null) sub.text = T($"{pending.Length} reports pending", $"{pending.Length} báo cáo chờ xử lý");
        AdminUI.Pill(tabs, T($"Pending · {pending.Length}", $"Chờ xử lý · {pending.Length}"), chatFilter == "pending" ? "active" : "plain", () => { chatFilter = "pending"; Refresh(); });
        AdminUI.Pill(tabs, T($"Warned · {warned.Length}", $"Đã cảnh cáo · {warned.Length}"), chatFilter == "warned" ? "active" : "plain", () => { chatFilter = "warned"; Refresh(); });
        AdminUI.Pill(tabs, T($"Dismissed · {dismissed.Length}", $"Bác bỏ · {dismissed.Length}"), chatFilter == "dismissed" ? "active" : "plain", () => { chatFilter = "dismissed"; Refresh(); });

        ChatReportRecord[] list = chatFilter == "warned" ? warned : chatFilter == "dismissed" ? dismissed : pending;
        if (list.Length == 0)
        {
            AdminUI.Empty(scroll, "check", error != null ? T("Cannot load reports", "Không tải được báo cáo") : T("No reports", "Không có báo cáo"), error ?? string.Empty);
            yield break;
        }

        foreach (ChatReportRecord report in list)
        {
            ChatReportRecord r = report;
            int warnings = warnCounts.TryGetValue(r.reported_user_id ?? string.Empty, out int c) ? c : 0;
            VisualElement card = AdminUI.Card(scroll, () => OpenChatReport(r, warnings));
            VisualElement row = AdminUI.Box(card, "adm-row");
            AdminUI.Avatar(row, r.reported?.full_name);
            VisualElement col = AdminUI.Box(row, "adm-grow");
            AdminUI.Text(col, r.reported?.full_name ?? T("Unknown user", "Người dùng"), "adm-person-name");
            AdminUI.Text(col, SupabaseModerationService.RelativeTime(r.created_at), "adm-person-time");
            if (warnings > 0) AdminUI.Badge(row, T($"Warned {warnings}×", $"Đã cảnh cáo {warnings} lần"), "amber").style.alignSelf = Align.Center;
            else AdminUI.Badge(row, T("No warnings yet", "Chưa có cảnh cáo"), "blue").style.alignSelf = Align.Center;

            VisualElement reason = AdminUI.Box(card, "adm-reason");
            AdminUI.Icon(reason, "flag", "red");
            AdminUI.Text(reason, SupabaseModerationService.ChatReasonLabel(r.reason), "adm-reason-label");
            AdminUI.Text(card, T("Reported by: ", "Người báo cáo: ") + (r.reporter?.full_name ?? "?") +
                (string.IsNullOrWhiteSpace(r.class_name) ? RoleSuffix(r.reporter?.role) : " · " + r.class_name), "adm-reporter-line");
        }
    }

    // ------------------------------------------------------------------
    // A3 · Chi tiết báo cáo
    // ------------------------------------------------------------------

    private void OpenChatReport(ChatReportRecord r, int warnings)
    {
        VisualElement page = NewPage();
        AdminUI.Header(page, T("Report details", "Chi tiết báo cáo"),
            "#RP-" + ShortId(r.id) + " · " + SupabaseModerationService.RelativeTime(r.created_at), () => ClosePage(page));
        ScrollView body = AdminUI.Scroll(page);

        // reported user
        VisualElement userCard = AdminUI.Card(body, null, "adm-card--compact");
        VisualElement row = AdminUI.Box(userCard, "adm-row");
        AdminUI.Avatar(row, r.reported?.full_name, "large");
        VisualElement col = AdminUI.Box(row, "adm-grow");
        AdminUI.Text(col, (r.reported?.full_name ?? "?") + T(" (reported user)", " (người bị báo cáo)"), "adm-person-name").style.fontSize = 15;
        AdminUI.Text(col, RoleLabel(r.reported?.role) + (string.IsNullOrWhiteSpace(r.class_name) ? string.Empty : " · " + r.class_name), "adm-caption");
        VisualElement countCol = AdminUI.Box(row, "adm-count-col");
        Label count = AdminUI.Text(countCol, warnings.ToString(), "adm-count-value");
        AdminUI.Text(countCol, T("previous warnings", "cảnh cáo trước"), "adm-count-label");
        StartCoroutine(SupabaseModerationService.GetWarningCount(r.reported_user_id, n => count.text = n.ToString(), _ => { }));

        // reason
        VisualElement reasonCard = AdminUI.Card(body, null, "adm-card--compact");
        AdminUI.Text(reasonCard, T("REPORT REASON", "LÝ DO BÁO CÁO"), "adm-section-label");
        AdminUI.Text(reasonCard, SupabaseModerationService.ChatReasonLabel(r.reason), "adm-reason-title");
        if (!string.IsNullOrWhiteSpace(r.description))
            AdminUI.Text(reasonCard, "“" + r.description.Trim() + "” — " + (r.reporter?.full_name ?? string.Empty), "adm-quote");

        // messages (snapshot – the real chat is never changed)
        VisualElement convo = AdminUI.Card(body, null, "adm-card--compact");
        AdminUI.Text(convo, T("REPORTED MESSAGES", "TIN NHẮN BỊ BÁO CÁO"), "adm-section-label");
        if (r.message_snapshot == null || r.message_snapshot.Length == 0)
            AdminUI.Text(convo, T("No messages captured.", "Không có tin nhắn."), "adm-caption");
        else
        {
            foreach (ChatSnapshotMessage m in r.message_snapshot)
            {
                bool fromReported = m.sender_id == r.reported_user_id;
                VisualElement bubbleRow = AdminUI.Box(convo, "adm-bubble-row");
                if (!fromReported) bubbleRow.AddToClassList("adm-bubble-row--right");
                VisualElement bubble = AdminUI.Box(bubbleRow, "adm-bubble", fromReported ? "adm-bubble--reported" : "adm-bubble--reporter");
                string name = m.sender_name ?? "?";
                AdminUI.Text(bubble, fromReported ? name + " · " + ClockTime(m.created_at) : name, "adm-bubble-name");
                string content = SnapshotText(m);
                AdminUI.Text(bubble, content, "adm-bubble-text");
            }
        }

        if (!string.IsNullOrWhiteSpace(r.admin_message))
        {
            VisualElement msg = AdminUI.Card(body, null, "adm-card--amber", "adm-card--compact");
            AdminUI.Text(msg, T("WARNING SENT", "CẢNH CÁO ĐÃ GỬI"), "adm-section-label");
            AdminUI.Text(msg, r.admin_message, "adm-body");
        }

        AdminUI.Note(body, T("If the report is valid, the user only receives a warning notification. The account is not locked or deleted.",
            "Nếu báo cáo đúng, người vi phạm chỉ nhận thông báo cảnh cáo. Tài khoản không bị khóa hay xóa."));

        if (r.status == "pending")
        {
            VisualElement footer = AdminUI.Box(page, "adm-footer");
            AdminUI.Btn(footer, T("Dismiss", "Bác bỏ"), "secondary", () => ConfirmDismissChat(r, page));
            AdminUI.Btn(footer, T("Send warning", "Gửi cảnh cáo"), "warn", () => OpenWarningSheet(r, page), true);
        }
    }

    // ------------------------------------------------------------------
    // A4 · Gửi thông báo cảnh cáo (sheet)
    // ------------------------------------------------------------------

    private void OpenWarningSheet(ChatReportRecord r, VisualElement page)
    {
        string name = r.reported?.full_name ?? T("this user", "người dùng");
        VisualElement icon = AdminUI.TextGlyphBox(null, "!", "amber", "sheet");
        VisualElement sheet = AdminUI.Sheet(root, T($"Send a warning to {name}", $"Gửi cảnh cáo tới {name}"), null, out VisualElement overlay, icon);

        AdminUI.Text(sheet, T("Violation", "Vi phạm"), "adm-sheet-label");
        TextField message = null;
        string startReason = Array.IndexOf(SupabaseModerationService.ChatReasons, r.reason) >= 0 ? r.reason : "other";
        AdminUI.PillChoice(sheet, SupabaseModerationService.ChatReasons, ShortChatReason, startReason,
            value => message?.SetValueWithoutNotify(WarningTemplate(value)));

        AdminUI.Text(sheet, T("Warning message", "Nội dung cảnh cáo"), "adm-sheet-label");
        message = AdminUI.Field(sheet, T("Warning message", "Nội dung cảnh cáo"), true, WarningTemplate(startReason));
        AdminUI.Text(sheet, T("The notification appears in the user's inbox. The reporter is told the report was handled.",
            "Thông báo sẽ xuất hiện trong hộp thư của người dùng. Người báo cáo được báo là báo cáo đã được xử lý."), "adm-sheet-help");
        Label status = AdminUI.Text(sheet, string.Empty, "adm-status");

        VisualElement buttons = AdminUI.Box(sheet, "adm-sheet-buttons");
        AdminUI.Btn(buttons, T("Cancel", "Hủy"), "secondary", () => overlay.RemoveFromHierarchy());
        Button send = AdminUI.Btn(buttons, T("Send warning", "Gửi cảnh cáo"), "warn", null, true);
        send.clicked += () =>
        {
            if (string.IsNullOrWhiteSpace(message.value))
            {
                SetError(status, T("Please enter a message.", "Vui lòng nhập nội dung cảnh cáo."));
                return;
            }
            send.SetEnabled(false);
            status.text = T("Sending...", "Đang gửi...");
            StartCoroutine(SupabaseModerationService.ResolveChatReport(r.id, true, message.value.Trim(),
                () =>
                {
                    overlay.RemoveFromHierarchy();
                    ClosePage(page);
                    ModerationReportSheet.ShowToast(root, this, T("Warning sent", "Đã gửi cảnh cáo"));
                    Refresh();
                },
                e => { send.SetEnabled(true); SetError(status, e); }));
        };
    }

    private void ConfirmDismissChat(ChatReportRecord r, VisualElement page)
    {
        VisualElement icon = AdminUI.GlyphBox(null, "tick", "blue", "sheet");
        VisualElement sheet = AdminUI.Sheet(root, T("Dismiss this report?", "Bác bỏ báo cáo này?"),
            T("No warning is sent. The reporter is told that no violation was found.",
              "Không gửi cảnh cáo. Người báo cáo sẽ được thông báo là không phát hiện vi phạm."), out VisualElement overlay, icon);
        sheet.Q<Label>(className: "adm-sheet-desc").style.unityTextAlign = TextAnchor.MiddleCenter;
        Label status = AdminUI.Text(sheet, string.Empty, "adm-status");
        VisualElement buttons = AdminUI.Box(sheet, "adm-sheet-buttons");
        AdminUI.Btn(buttons, T("Cancel", "Hủy"), "secondary", () => overlay.RemoveFromHierarchy());
        Button ok = AdminUI.Btn(buttons, T("Dismiss", "Bác bỏ"), "primary", null, true);
        ok.clicked += () =>
        {
            ok.SetEnabled(false);
            StartCoroutine(SupabaseModerationService.ResolveChatReport(r.id, false, null,
                () =>
                {
                    overlay.RemoveFromHierarchy();
                    ClosePage(page);
                    ModerationReportSheet.ShowToast(root, this, T("Report dismissed", "Đã bác bỏ báo cáo"));
                    Refresh();
                },
                e => { ok.SetEnabled(true); SetError(status, e); }));
        };
    }

    /// <summary>Readable text for a snapshot message (calls, images and files instead of raw markers/URLs).</summary>
    private static string SnapshotText(ChatSnapshotMessage m)
    {
        string content = m.content ?? string.Empty;
        string type = (m.message_type ?? string.Empty).ToLowerInvariant();
        if (content.StartsWith("__VOICE_CALL__", StringComparison.Ordinal)) return T("[Voice call]", "[Cuộc gọi thoại]");
        if (type == "image") return T("[Image]", "[Hình ảnh]");
        if (type == "file") return T("[File] ", "[Tệp] ") + System.IO.Path.GetFileName(content.Split('?')[0]);
        if (string.IsNullOrWhiteSpace(content))
            return "[" + (string.IsNullOrWhiteSpace(type) ? T("attachment", "tệp đính kèm") : type) + "]";
        return content;
    }

    private static string ShortChatReason(string reason)
    {
        switch (reason)
        {
            case "harassment": return T("Insults", "Ngôn từ xúc phạm");
            case "sexual": return T("Obscene content", "Nội dung đồi trụy");
            case "spam": return T("Harassment / spam", "Quấy rối");
            case "scam": return T("Scam", "Lừa đảo");
            default: return T("Other", "Khác");
        }
    }

    private static string WarningTemplate(string reason)
    {
        switch (reason)
        {
            case "harassment":
                return T("Your messages contain insulting language towards others. Please keep a respectful attitude in the learning environment.",
                         "Tin nhắn của bạn có ngôn từ xúc phạm người khác. Vui lòng giữ thái độ tôn trọng trong môi trường học tập.");
            case "sexual":
                return T("Your messages contain obscene content, which is not allowed in the learning environment.",
                         "Tin nhắn của bạn có nội dung đồi trụy, không phù hợp với môi trường học tập.");
            case "spam":
                return T("Your messages are disturbing other users. Please stop sending spam.",
                         "Tin nhắn của bạn đang làm phiền người khác. Vui lòng không gửi tin nhắn quấy rối, spam.");
            case "scam":
                return T("Do not ask other users for money or personal information.",
                         "Không được lừa đảo hoặc xin thông tin cá nhân của người dùng khác.");
            default:
                return T("Your messages violated the community rules. Please communicate respectfully.",
                         "Tin nhắn của bạn vi phạm quy tắc cộng đồng. Vui lòng giao tiếp văn minh, tôn trọng mọi người.");
        }
    }

    // ------------------------------------------------------------------
    // B1 · Nội dung bị báo cáo
    // ------------------------------------------------------------------

    private class ContentRow
    {
        public ContentQueueItem Queue;          // new report (no case yet)
        public ModerationCaseRecord Case;       // case in progress or done
        public string Type;
        public string Title;
        public string Teacher;
        public string ClassName;
        public int Reports;
        public string Status;                   // review | awaiting | done
        public DateTime Time;
    }

    private IEnumerator RenderContentTab(int version)
    {
        VisualElement header = AdminUI.Header(main, T("Reported content", "Nội dung bị báo cáo"), T("Loading...", "Đang tải..."), GoBackTab, true);
        AdminUI.HeaderAction(header, "filter", () => Refresh());
        Label sub = header.Q<Label>(className: "adm-header-sub");
        VisualElement typeRow = AdminUI.Box(main, "adm-tab-row");
        VisualElement statusRow = AdminUI.Box(main, "adm-tab-row", "adm-tab-row--second");
        ScrollView scroll = AdminUI.Scroll(main);
        scroll.AddToClassList("adm-scroll--tight");

        ContentQueueItem[] queue = null;
        ModerationCaseRecord[] cases = null;
        string error = null;
        yield return SupabaseModerationService.GetContentQueue(q => queue = q, e => error = e);
        yield return SupabaseModerationService.GetCases("awaiting_explanation,explanation_submitted,approved,removed", c => cases = c, e => error = e);
        if (version != loadVersion) yield break;

        queue ??= new ContentQueueItem[0];
        cases ??= new ModerationCaseRecord[0];
        if (queue.Length + cases.Length > 0) error = null;

        List<ContentRow> rows = new List<ContentRow>();
        Dictionary<string, int> reportsByCase = new Dictionary<string, int>();
        if (queue != null)
        {
            foreach (ContentQueueItem q in queue)
            {
                if (!string.IsNullOrWhiteSpace(q.case_id)) { reportsByCase[q.case_id] = q.report_count; continue; }
                rows.Add(new ContentRow
                {
                    Queue = q, Type = q.target_type, Title = q.target_title, Teacher = q.teacher_name, ClassName = q.class_name,
                    Reports = q.report_count, Status = "review", Time = ParseTime(q.latest_at)
                });
            }
        }
        if (cases != null)
        {
            foreach (ModerationCaseRecord c in cases)
            {
                rows.Add(new ContentRow
                {
                    Case = c, Type = c.target_type, Title = c.target_title, Teacher = c.teacher?.full_name, ClassName = c.class_name,
                    Reports = reportsByCase.TryGetValue(c.id ?? string.Empty, out int n) ? n : 0,
                    Status = c.status == "awaiting_explanation" ? "awaiting" : c.status == "explanation_submitted" ? "review" : "done",
                    Time = ParseTime(c.explained_at ?? c.created_at)
                });
            }
        }
        rows.Sort((a, b) => b.Time.CompareTo(a.Time));

        int review = 0, awaiting = 0;
        foreach (ContentRow r in rows)
        {
            if (contentType != "all" && r.Type != contentType) continue;
            if (r.Status == "review") review++;
            else if (r.Status == "awaiting") awaiting++;
        }
        if (sub != null) sub.text = T($"{review} items to review", $"{review} nội dung chờ xem xét");

        AdminUI.Pill(typeRow, T("All", "Tất cả"), contentType == "all" ? "active" : "plain", () => { contentType = "all"; Refresh(); });
        AdminUI.Pill(typeRow, T("Lessons", "Bài học"), contentType == "lesson" ? "active" : "plain", () => { contentType = "lesson"; Refresh(); });
        AdminUI.Pill(typeRow, "Quiz", contentType == "quiz" ? "active" : "plain", () => { contentType = "quiz"; Refresh(); });
        AdminUI.Pill(typeRow, T("3D models", "Model 3D"), contentType == "model_3d" ? "active" : "plain", () => { contentType = "model_3d"; Refresh(); });

        AdminUI.Pill(statusRow, T($"To review · {review}", $"Chờ xem xét · {review}"), "blue", () => ToggleStatus("review"), contentStatus == "review");
        AdminUI.Pill(statusRow, T($"Waiting for teacher · {awaiting}", $"Chờ giải trình · {awaiting}"), "purple", () => ToggleStatus("awaiting"), contentStatus == "awaiting");
        AdminUI.Pill(statusRow, T("Done", "Đã xử lý"), contentStatus == "done" ? "active" : "plain", () => ToggleStatus("done"));

        int shown = 0;
        foreach (ContentRow row in rows)
        {
            if (contentType != "all" && row.Type != contentType) continue;
            if (contentStatus == "all" ? row.Status == "done" : row.Status != contentStatus) continue;
            RenderContentCard(scroll, row);
            shown++;
        }
        if (shown == 0)
            AdminUI.Empty(scroll, "check", error != null ? T("Cannot load", "Không tải được dữ liệu") : T("Nothing here", "Không có nội dung nào"), error ?? string.Empty);
    }

    private void ToggleStatus(string status)
    {
        contentStatus = contentStatus == status ? "all" : status;
        Refresh();
    }

    private void RenderContentCard(VisualElement parent, ContentRow row)
    {
        VisualElement card = AdminUI.Card(parent, () =>
        {
            if (row.Queue != null) OpenQueueItem(row.Queue);
            else OpenCase(row.Case, row.Reports);
        });
        VisualElement line = AdminUI.Box(card, "adm-row");
        line.style.alignItems = Align.FlexStart;
        ContentTypeBox(line, row.Type);

        VisualElement col = AdminUI.Box(line, "adm-grow");
        AdminUI.Text(col, TypeCaps(row.Type), "adm-content-type");
        AdminUI.Text(col, row.Title ?? "?", "adm-content-title");
        AdminUI.Text(col, "GV " + (row.Teacher ?? "?") + (string.IsNullOrWhiteSpace(row.ClassName) ? string.Empty : " · " + row.ClassName), "adm-content-sub");

        VisualElement badges = AdminUI.Box(col, "adm-badge-row");
        if (row.Queue != null) AdminUI.Badge(badges, T("To review", "Chờ xem xét"), "blue");
        else
        {
            switch (row.Case.status)
            {
                case "awaiting_explanation": AdminUI.Badge(badges, T("Waiting for explanation", "Chờ giải trình"), "purple"); break;
                case "explanation_submitted": AdminUI.Badge(badges, T("Explanation to review", "Chờ xét duyệt giải trình"), "purple"); break;
                case "approved": AdminUI.Badge(badges, T("Kept", "Đã giữ nội dung"), "green"); break;
                default: AdminUI.Badge(badges, T("Deleted", "Đã xóa nội dung"), "red"); break;
            }
        }
        if (row.Reports > 0) AdminUI.FlagBadge(badges, T($"{row.Reports} reports", $"{row.Reports} báo cáo"));
    }

    private static void ContentTypeBox(VisualElement parent, string type)
    {
        switch (type)
        {
            case "quiz": AdminUI.TextGlyphBox(parent, "?", "white", "content", "solid-green"); break;
            case "model_3d": ContentIcon(parent, "diamond", "solid-blue"); break;
            default: ContentIcon(parent, "lines", "solid-orange"); break;
        }
    }

    private static void ContentIcon(VisualElement parent, string glyph, string box)
    {
        VisualElement b = AdminUI.Box(parent, "adm-icon-box", "adm-icon-box--content", "adm-icon-box--" + box);
        b.pickingMode = PickingMode.Ignore;
        AdminUI.Icon(b, glyph, "white");
    }

    private static string TypeCaps(string type)
    {
        switch (type)
        {
            case "quiz": return "QUIZ";
            case "model_3d": return "MODEL 3D";
            default: return T("LESSON", "BÀI HỌC");
        }
    }

    private static string ReasonsText(string[] reasons)
    {
        if (reasons == null || reasons.Length == 0) return string.Empty;
        List<string> labels = new List<string>();
        foreach (string r in reasons) labels.Add(ShortContentReason(r));
        return string.Join(", ", labels);
    }

    private static string FirstReasonLong(string[] reasons)
    {
        if (reasons == null || reasons.Length == 0) return string.Empty;
        return reasons[0] == "sensitive" ? T("sensitive content", "nội dung nhạy cảm")
             : reasons[0] == "not_educational" ? T("not suitable for teaching", "không phù hợp dạy học")
             : T("for another reason", "lý do khác");
    }

    private static string ShortContentReason(string reason)
    {
        switch (reason)
        {
            case "sensitive": return T("Sensitive / obscene", "Nhạy cảm / đồi trụy");
            case "not_educational": return T("Not suitable for teaching", "Không phù hợp dạy học");
            default: return T("Other", "Khác");
        }
    }

    // ------------------------------------------------------------------
    // B2 · Chi tiết báo cáo nội dung + yêu cầu giải trình
    // ------------------------------------------------------------------

    private void OpenQueueItem(ContentQueueItem item)
    {
        VisualElement page = NewPage();
        AdminUI.Header(page, T("Reported content details", "Chi tiết báo cáo nội dung"),
            SupabaseModerationService.TargetTypeLabel(item.target_type) + " · " + (item.target_title ?? string.Empty), () => ClosePage(page));
        ScrollView body = AdminUI.Scroll(page);

        VisualElement host = AdminUI.Box(body);
        StartCoroutine(LoadContentDetail(host, item.lesson_id, item.quiz_id, item.asset_id, item.target_type, item.report_count));

        AdminUI.Text(body, T("REPORTS", "DANH SÁCH BÁO CÁO"), "adm-section-label");
        VisualElement reportsHost = AdminUI.Box(body);
        Label loading = AdminUI.Text(reportsHost, T("Loading...", "Đang tải..."), "adm-caption");
        StartCoroutine(LoadReports(item,
            list =>
            {
                loading.RemoveFromHierarchy();
                if (list == null) return;
                foreach (ContentReportRecord r in list)
                {
                    VisualElement card = AdminUI.Card(reportsHost, null, "adm-card--compact");
                    VisualElement row = AdminUI.Box(card, "adm-row");
                    AdminUI.Avatar(row, r.reporter?.full_name, "small");
                    VisualElement col = AdminUI.Box(row, "adm-grow");
                    AdminUI.Text(col, r.reporter?.full_name ?? "?", "adm-person-name").style.fontSize = 15;
                    AdminUI.Text(col, SupabaseModerationService.RelativeTime(r.created_at), "adm-person-time");
                    VisualElement reason = AdminUI.Box(card, "adm-reason");
                    AdminUI.Icon(reason, "flag", "red");
                    AdminUI.Text(reason, ShortContentReason(r.reason), "adm-reason-label");
                    if (!string.IsNullOrWhiteSpace(r.description)) AdminUI.Text(card, "“" + r.description.Trim() + "”", "adm-quote");
                }
            },
            e => loading.text = e));

        VisualElement footer = AdminUI.Box(page, "adm-footer");
        AdminUI.Btn(footer, T("Dismiss reports", "Bác bỏ báo cáo"), "secondary", () => DismissContent(item, page, null));
        AdminUI.Btn(footer, T("Ask to explain", "Yêu cầu giải trình"), "primary", () => OpenRequestExplanation(item, page), true);
    }

    private void OpenRequestExplanation(ContentQueueItem item, VisualElement page)
    {
        VisualElement sheet = AdminUI.Sheet(root, T("Ask the teacher to explain", "Yêu cầu giáo viên giải trình"),
            T("The content is hidden from students until the admin reviews the explanation.",
              "Nội dung được tạm ẩn với học sinh cho tới khi admin xét duyệt lời giải trình."), out VisualElement overlay);

        AdminUI.Text(sheet, T("Suspected issue", "Lý do nghi vấn"), "adm-sheet-label");
        string start = item.reasons != null && item.reasons.Length > 0 ? item.reasons[0] : SupabaseModerationService.ContentReasons[0];
        Func<string> reason = AdminUI.PillChoice(sheet, SupabaseModerationService.ContentReasons, ShortContentReason, start);

        string type = SupabaseModerationService.TargetTypeLabel(item.target_type);
        TextField question = AdminUI.Field(sheet, T("Question for the teacher", "Câu hỏi cho giáo viên"), true,
            T($"This {type.ToLowerInvariant()} has sensitive details. Please explain what it is used to teach.",
              $"{type} có chi tiết nhạy cảm. Thầy/cô vui lòng cho biết {type.ToLowerInvariant()} được dùng để dạy nội dung gì?"));
        Label status = AdminUI.Text(sheet, string.Empty, "adm-status");

        VisualElement buttons = AdminUI.Box(sheet, "adm-sheet-buttons");
        AdminUI.Btn(buttons, T("Dismiss reports", "Bác bỏ báo cáo"), "secondary", () => DismissContent(item, page, overlay));
        Button send = AdminUI.Btn(buttons, T("Send request", "Gửi yêu cầu"), "primary", null, true);
        send.clicked += () =>
        {
            send.SetEnabled(false);
            status.text = T("Sending...", "Đang gửi...");
            StartCoroutine(SupabaseModerationService.RequestExplanation(item, reason(), question.value,
                () =>
                {
                    overlay.RemoveFromHierarchy();
                    ClosePage(page);
                    ModerationReportSheet.ShowToast(root, this, T("Request sent to the teacher", "Đã gửi yêu cầu giải trình tới giáo viên"));
                    Refresh();
                },
                e => { send.SetEnabled(true); SetError(status, e); }));
        };
    }

    private void DismissContent(ContentQueueItem item, VisualElement page, VisualElement overlay)
    {
        StartCoroutine(SupabaseModerationService.DismissContentReports(item,
            () =>
            {
                overlay?.RemoveFromHierarchy();
                ClosePage(page);
                ModerationReportSheet.ShowToast(root, this, T("Reports dismissed", "Đã bác bỏ báo cáo"));
                Refresh();
            },
            e => ModerationReportSheet.ShowToast(root, this, e)));
    }

    // ------------------------------------------------------------------
    // B3 · Xét duyệt giải trình
    // ------------------------------------------------------------------

    private void OpenCase(ModerationCaseRecord c, int reportCount)
    {
        VisualElement page = NewPage();
        AdminUI.Header(page, T("Review explanation", "Xét duyệt giải trình"),
            SupabaseModerationService.TargetTypeLabel(c.target_type) + " · " + (c.target_title ?? string.Empty), () => ClosePage(page));
        ScrollView body = AdminUI.Scroll(page);

        // progress
        VisualElement progress = AdminUI.Card(body, null, "adm-card--compact");
        AdminUI.Text(progress, T("PROGRESS", "TIẾN TRÌNH"), "adm-section-label").style.marginBottom = 0;
        Label firstReportTime = Step(progress, "done", T("Students reported the content", "Học sinh báo cáo nội dung"), string.Empty);
        Step(progress, "done", T("Admin asked for an explanation", "Admin gửi yêu cầu giải trình"), FullTime(c.created_at));
        bool explained = c.status != "awaiting_explanation";
        Step(progress, explained ? "done" : "current", explained ? T("Teacher explained", "Giáo viên đã giải trình") : T("Waiting for the teacher", "Chờ giáo viên giải trình"),
            explained ? FullTime(c.explained_at) : T("Now", "Hiện tại"));
        switch (c.status)
        {
            case "explanation_submitted": Step(progress, "current", T("Waiting for admin review", "Chờ admin xét duyệt"), T("Now", "Hiện tại")); break;
            case "approved": Step(progress, "done", T("Valid – content kept", "Hợp lệ – đã giữ nội dung"), string.Empty); break;
            case "removed": Step(progress, "done", T("Invalid – content deleted", "Không hợp lệ – đã xóa nội dung"), string.Empty); break;
            default: Step(progress, "pending", T("Admin review", "Admin xét duyệt"), string.Empty); break;
        }
        ContentQueueItem target = TargetOf(c);
        StartCoroutine(LoadReports(target, list =>
        {
            if (list == null || list.Length == 0) { firstReportTime.style.display = DisplayStyle.None; return; }
            firstReportTime.text = FullTime(list[list.Length - 1].created_at);
        }, _ => firstReportTime.style.display = DisplayStyle.None));

        // explanation
        if (explained && !string.IsNullOrWhiteSpace(c.explanation_text))
        {
            VisualElement card = AdminUI.Card(body, null, "adm-card--compact");
            VisualElement row = AdminUI.Box(card, "adm-row");
            AdminUI.Avatar(row, c.teacher?.full_name, "small");
            VisualElement col = AdminUI.Box(row, "adm-grow");
            AdminUI.Text(col, "GV " + (c.teacher?.full_name ?? "?"), "adm-teacher-name");
            if (!string.IsNullOrWhiteSpace(c.explanation_topic))
                AdminUI.Text(col, T("Teaches about: ", "Nội dung giáo dục về: ") + c.explanation_topic, "adm-teacher-sub");
            AdminUI.Text(card, c.explanation_text, "adm-explanation");
        }
        else if (!string.IsNullOrWhiteSpace(c.admin_question))
        {
            VisualElement q = AdminUI.Card(body, null, "adm-card--compact");
            AdminUI.Text(q, T("QUESTION SENT TO THE TEACHER", "CÂU HỎI ĐÃ GỬI GIÁO VIÊN"), "adm-section-label");
            AdminUI.Text(q, c.admin_question, "adm-quote");
        }

        if (!string.IsNullOrWhiteSpace(c.decision_note))
        {
            VisualElement note = AdminUI.Card(body, null, "adm-card--compact");
            AdminUI.Text(note, T("NOTE TO THE TEACHER", "GHI CHÚ GỬI GIÁO VIÊN"), "adm-section-label");
            AdminUI.Text(note, c.decision_note, "adm-quote");
        }

        if (c.status != "removed")
        {
            VisualElement link = AdminUI.Box(body, "adm-link-row");
            AdminUI.Text(link, T("View reported content ›", "Xem nội dung bị báo cáo ›"), "adm-link");
            link.RegisterCallback<ClickEvent>(_ => OpenContentPreview(c, reportCount));
        }

        if (c.status == "explanation_submitted")
        {
            VisualElement footer = AdminUI.Box(page, "adm-footer", "adm-footer--column");
            Button keep = AdminUI.Btn(footer, T("Valid – Keep content", "Hợp lệ – Giữ nội dung"), "success", null);
            keep.clicked += () =>
            {
                keep.SetEnabled(false);
                StartCoroutine(SupabaseModerationService.DecideCase(c.id, true, null,
                    () =>
                    {
                        ClosePage(page);
                        ModerationReportSheet.ShowToast(root, this, T("Content kept and visible again", "Đã giữ nội dung, học sinh xem lại được"));
                        Refresh();
                    },
                    e => { keep.SetEnabled(true); ModerationReportSheet.ShowToast(root, this, e); }));
            };
            AdminUI.Btn(footer, T("Invalid – Delete content", "Không hợp lệ – Xóa nội dung"), "danger-soft", () => OpenDeleteSheet(c, page), true);
        }
    }

    private void OpenContentPreview(ModerationCaseRecord c, int reportCount)
    {
        VisualElement page = NewPage();
        AdminUI.Header(page, T("Reported content details", "Chi tiết báo cáo nội dung"),
            SupabaseModerationService.TargetTypeLabel(c.target_type) + " · " + (c.target_title ?? string.Empty), () => ClosePage(page));
        ScrollView body = AdminUI.Scroll(page);
        VisualElement host = AdminUI.Box(body);
        StartCoroutine(LoadContentDetail(host, c.lesson_id, c.quiz_id, c.asset_id, c.target_type, reportCount));
    }

    private static Label Step(VisualElement parent, string state, string title, string time)
    {
        VisualElement row = AdminUI.Box(parent, "adm-step");
        VisualElement mark = AdminUI.Box(row, "adm-step-mark");
        if (state == "done") AdminUI.Icon(mark, "tick", "green");
        else AdminUI.Box(mark, "adm-step-dot", state == "pending" ? "adm-step-dot--pending" : null);
        VisualElement col = AdminUI.Box(row, "adm-grow");
        AdminUI.Text(col, title, "adm-step-title", state == "pending" ? "adm-step-title--pending" : null);
        Label t = AdminUI.Text(col, time, "adm-step-time");
        if (string.IsNullOrEmpty(time) && state != "done") t.style.display = DisplayStyle.None;
        return t;
    }

    private static ContentQueueItem TargetOf(ModerationCaseRecord c) => new ContentQueueItem
    {
        target_type = c.target_type, lesson_id = c.lesson_id, quiz_id = c.quiz_id, asset_id = c.asset_id,
        target_title = c.target_title, class_name = c.class_name, case_id = c.id
    };

    // ------------------------------------------------------------------
    // B4 · Không hợp lệ – Xóa nội dung (sheet)
    // ------------------------------------------------------------------

    private void OpenDeleteSheet(ModerationCaseRecord c, VisualElement page)
    {
        string what;
        string first;
        switch (c.target_type)
        {
            case "quiz":
                what = T("this quiz", "quiz này");
                first = T($"Quiz “{c.target_title}” and all its questions are removed from the lesson.", $"Quiz “{c.target_title}” và toàn bộ câu hỏi bị xóa khỏi bài học.");
                break;
            case "model_3d":
                what = T("this 3D model", "model 3D này");
                first = T($"3D model “{c.target_title}” is removed from the lesson.", $"Model 3D “{c.target_title}” bị xóa khỏi bài học.");
                break;
            default:
                what = T("this lesson", "bài học này");
                first = T($"Lesson “{c.target_title}” (video, PDF, quiz, attached models) is removed from the class.", $"Bài học “{c.target_title}” (video, PDF, quiz, model đi kèm) bị xóa khỏi lớp.");
                break;
        }

        VisualElement icon = AdminUI.GlyphBox(null, "trash", "red", "sheet");
        icon.Q<VisualElement>(className: "adm-icon").AddToClassList("adm-tint--gray");
        VisualElement sheet = AdminUI.Sheet(root, T($"Delete {what}?", $"Xóa {what}?"), null, out VisualElement overlay, icon);
        AdminUI.Bullets(sheet, new[]
        {
            first,
            T("The teacher is notified that the explanation was not accepted and the content was deleted.",
              "Giáo viên nhận thông báo: lời giải trình không hợp lệ và nội dung đã bị xóa."),
            T("The teacher's account is not affected.", "Tài khoản giáo viên không bị ảnh hưởng.")
        });
        AdminUI.Text(sheet, T("Note to the teacher", "Ghi chú gửi giáo viên"), "adm-sheet-label");
        TextField note = AdminUI.Field(sheet, T("Note to the teacher", "Ghi chú gửi giáo viên"), true,
            T("The content does not serve the learning objectives and contains inappropriate images.",
              "Nội dung không phục vụ mục tiêu giáo dục và có hình ảnh không phù hợp."));
        note.AddToClassList("adm-field--note");
        Label status = AdminUI.Text(sheet, string.Empty, "adm-status");

        VisualElement buttons = AdminUI.Box(sheet, "adm-sheet-buttons");
        AdminUI.Btn(buttons, T("Cancel", "Hủy"), "secondary", () => overlay.RemoveFromHierarchy());
        Button delete = AdminUI.Btn(buttons, T("Delete content", "Xóa nội dung"), "danger", null, true);
        delete.clicked += () =>
        {
            delete.SetEnabled(false);
            status.text = T("Deleting...", "Đang xóa...");
            StartCoroutine(SupabaseModerationService.DecideCase(c.id, false, note.value,
                () =>
                {
                    overlay.RemoveFromHierarchy();
                    ClosePage(page);
                    ModerationReportSheet.ShowToast(root, this, T("Content deleted", "Đã xóa nội dung"));
                    Refresh();
                },
                e => { delete.SetEnabled(true); SetError(status, e); }));
        };
    }

    // ------------------------------------------------------------------
    // Content preview (B2 top part)
    // ------------------------------------------------------------------

    private IEnumerator LoadContentDetail(VisualElement host, string lessonId, string quizId, string assetId, string targetType, int reportCount)
    {
        if (string.IsNullOrWhiteSpace(lessonId)) yield break;
        Label loading = AdminUI.Text(host, T("Loading content...", "Đang tải nội dung..."), "adm-caption");

        AdminContentDetail detail = null;
        string error = null;
        yield return SupabaseModerationService.GetContentDetail(lessonId, quizId, d => detail = d, e => error = e);
        loading.RemoveFromHierarchy();
        if (detail == null || detail.lesson == null)
        {
            AdminUI.Text(host, error ?? T("Content no longer exists.", "Nội dung không còn tồn tại."), "adm-caption");
            yield break;
        }

        // pick what the dark preview box opens
        AdminContentAsset focusAsset = null;
        if (detail.assets != null)
            foreach (AdminContentAsset a in detail.assets)
                if (a != null && (a.id == assetId || (focusAsset == null && targetType == "lesson" && string.IsNullOrWhiteSpace(detail.lesson.youtube_url))))
                    focusAsset = a;

        VisualElement preview = AdminUI.Box(host, "adm-preview");
        AdminUI.Icon(preview, targetType == "quiz" ? "square" : targetType == "model_3d" ? "diamond" : "lines", "light");
        AdminUI.Text(preview, T("Preview content", "Xem trước nội dung"), "adm-preview-label");
        preview.RegisterCallback<ClickEvent>(_ =>
        {
            if (targetType == "lesson" && !string.IsNullOrWhiteSpace(detail.lesson.youtube_url)) Application.OpenURL(detail.lesson.youtube_url);
            else if (focusAsset != null) StartCoroutine(focusAsset.asset_type == "model_3d" ? OpenModel(focusAsset, detail.lesson) : OpenDocument(focusAsset));
        });

        VisualElement info = AdminUI.Card(host, null, "adm-card--compact");
        AdminUI.InfoRow(info, T("Teacher", "Giáo viên"), detail.teacher_name ?? string.Empty);
        AdminUI.InfoRow(info, T("Class / Lesson", "Lớp / Bài học"), (detail.class_name ?? string.Empty) + " · " + (detail.lesson.title ?? string.Empty));
        if (reportCount > 0) AdminUI.InfoRow(info, T("Reports", "Số báo cáo"), T($"{reportCount} students", $"{reportCount} học sinh"));
        AdminUI.InfoRow(info, T("Visible to students", "Hiển thị với học sinh"), detail.lesson.moderation_hidden ? T("No (hidden)", "Không (tạm ẩn)") : T("Yes", "Có"));
        if (!string.IsNullOrWhiteSpace(detail.lesson.description))
        {
            AdminUI.Divider(info);
            AdminUI.Text(info, detail.lesson.description, "adm-body");
        }

        // other files of the lesson
        if (detail.assets != null && targetType == "lesson")
        {
            foreach (AdminContentAsset asset in detail.assets)
            {
                if (asset == null) continue;
                AdminContentAsset a = asset;
                bool isModel = a.asset_type == "model_3d";
                VisualElement card = AdminUI.Card(host, () => StartCoroutine(isModel ? OpenModel(a, detail.lesson) : OpenDocument(a)), "adm-card--compact");
                VisualElement row = AdminUI.Box(card, "adm-row");
                AdminUI.GlyphBox(row, isModel ? "diamond" : "lines", isModel ? "blue" : "amber", "small");
                VisualElement col = AdminUI.Box(row, "adm-grow");
                col.style.marginLeft = 12;
                AdminUI.Text(col, a.file_name ?? "?", "adm-task-title");
                AdminUI.Text(col, isModel ? T("3D model", "Model 3D") : T("Document", "Tài liệu"), "adm-task-sub");
                AdminUI.Text(row, "›", "adm-chevron");
            }
        }

        if (detail.quiz != null && detail.quiz.questions != null)
        {
            AdminUI.Text(host, T("QUIZ QUESTIONS", "CÂU HỎI QUIZ"), "adm-section-label");
            int i = 1;
            foreach (AdminQuizQuestion q in detail.quiz.questions)
            {
                VisualElement card = AdminUI.Card(host, null, "adm-card--compact");
                AdminUI.Text(card, $"{i++}. {q.text}", "adm-task-title");
                if (q.options != null)
                    foreach (string o in q.options) AdminUI.Text(card, o, "adm-task-sub");
            }
        }
    }

    private IEnumerator SignAsset(string assetId, Action<string> onUrl, Action<string> onError)
    {
        yield return SupabaseTokenRefresher.EnsureFreshToken();
        string endpoint = SupabaseConfig.FunctionsUrl.TrimEnd('/') + "/" + SignedUrlFunction;
        string body = "{" + SupabaseModerationService.Field("asset_id", assetId) + "}";
        using UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = SupabaseConfig.RequestTimeoutSeconds;
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("apikey", SupabaseConfig.PublishableKey);
        request.SetRequestHeader("Authorization", "Bearer " + SupabaseSession.AccessToken);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(request.downloadHandler?.text ?? request.error);
            yield break;
        }

        SignedUrlResponse response = null;
        try { response = JsonUtility.FromJson<SignedUrlResponse>(request.downloadHandler.text); }
        catch (Exception e) { onError?.Invoke(e.Message); yield break; }

        string url = response?.url;
        if (string.IsNullOrWhiteSpace(url)) url = response?.signed_url;
        if (string.IsNullOrWhiteSpace(url)) onError?.Invoke("Empty signed URL");
        else onUrl?.Invoke(url);
    }

    private IEnumerator OpenDocument(AdminContentAsset asset)
    {
        string url = null, error = null;
        yield return SignAsset(asset.id, u => url = u, e => error = e);
        if (url != null) Application.OpenURL(url);
        else ModerationReportSheet.ShowToast(root, this, T("Cannot open file: ", "Không mở được tệp: ") + error);
    }

    private IEnumerator OpenModel(AdminContentAsset asset, AdminContentLesson lesson)
    {
        ModerationReportSheet.ShowToast(root, this, T("Opening 3D model...", "Đang mở model 3D..."));
        string url = null, error = null;
        yield return SignAsset(asset.id, u => url = u, e => error = e);
        if (url == null)
        {
            ModerationReportSheet.ShowToast(root, this, T("Cannot open model: ", "Không mở được model: ") + error);
            yield break;
        }

        string name = System.IO.Path.GetFileNameWithoutExtension(asset.file_name ?? "3D Model");
        AdminModelItem item = new AdminModelItem
        {
            asset_id = asset.id, lesson_id = lesson.id, lesson_title = lesson.title, chapter_order = 0,
            name = name, file_name = asset.file_name, bucket = asset.storage_bucket,
            storage_path = asset.storage_path, url = url, fallback_url = string.Empty, display_order = 0
        };
        AdminModelManifest manifest = new AdminModelManifest { class_id = string.Empty, lesson_id = lesson.id, mode = "3d", models = new[] { item } };
        string manifestJson = JsonUtility.ToJson(manifest);

        PlayerPrefs.SetString("interactive_mode", "3d");
        PlayerPrefs.SetString("selected_model_asset_id", asset.id ?? string.Empty);
        PlayerPrefs.SetString("selected_model_bucket", asset.storage_bucket ?? string.Empty);
        PlayerPrefs.SetString("selected_model_storage_path", asset.storage_path ?? string.Empty);
        PlayerPrefs.SetString("selected_model_file_name", asset.file_name ?? string.Empty);
        PlayerPrefs.SetString("selected_model_url", url);
        PlayerPrefs.SetString("selected_model_name", name);
        PlayerPrefs.SetString("selected_model_lesson_id", lesson.id ?? string.Empty);
        PlayerPrefs.SetString("selected_model_lesson_title", lesson.title ?? string.Empty);
        PlayerPrefs.SetInt("selected_model_chapter_order", 0);
        PlayerPrefs.SetString("selected_lesson_models_json", manifestJson);
        PlayerPrefs.SetString("selected_class_models_json", manifestJson);
        PlayerPrefs.SetInt("selected_lesson_model_count", 1);
        PlayerPrefs.SetInt("selected_lesson_model_index", 0);
        PlayerPrefs.SetString("previous_scene", SceneManager.GetActiveScene().name);
        PlayerPrefs.Save();

        SceneManager.LoadScene(Mode3DSceneName);
    }

    // ------------------------------------------------------------------
    // Settings (not in Figma – same visual language)
    // ------------------------------------------------------------------

    private void RenderSettings()
    {
        VisualElement settingsHeader = AdminUI.Header(main, T("Settings", "Cài đặt"), T("Admin account", "Tài khoản quản trị"), GoBackTab, true);
        AdminUI.HeaderSpacer(settingsHeader);
        ScrollView scroll = AdminUI.Scroll(main);

        VisualElement account = AdminUI.Card(scroll, null, "adm-card--compact");
        VisualElement row = AdminUI.Box(account, "adm-row");
        AdminUI.Avatar(row, "A D", "large");
        VisualElement col = AdminUI.Box(row, "adm-grow");
        AdminUI.Text(col, string.IsNullOrWhiteSpace(SupabaseSession.FullName) ? T("Administrator", "Quản trị viên") : SupabaseSession.FullName, "adm-person-name");
        AdminUI.Text(col, SupabaseSession.Email, "adm-person-time");
        AdminUI.Badge(row, "ADMIN", "red").style.alignSelf = Align.Center;

        AdminUI.Text(scroll, T("Language", "Ngôn ngữ"), "adm-section-title");
        VisualElement lang = AdminUI.Box(scroll, "adm-task");
        AdminUI.GlyphBox(lang, "grid", "blue");
        VisualElement pills = AdminUI.Box(lang, "adm-task-text", "adm-row");
        AdminUI.Pill(pills, "English", AppLanguageManager.IsVietnamese ? "soft" : "active", () => { AppLanguageManager.SetLanguage("EN"); ShowTab(Tab.Settings); }).style.marginBottom = 0;
        AdminUI.Pill(pills, "Tiếng Việt", AppLanguageManager.IsVietnamese ? "active" : "soft", () => { AppLanguageManager.SetLanguage("VI"); ShowTab(Tab.Settings); }).style.marginBottom = 0;

        AdminUI.Text(scroll, T("Account", "Tài khoản"), "adm-section-title");
        TaskItem(scroll, "flag", "red", T("Log out", "Đăng xuất"), T("Return to the sign-in screen", "Quay về màn hình đăng nhập"), Logout);
    }

    private void Logout()
    {
        SupabaseAuthService.SignOutLocally();
        PlayerPrefs.DeleteKey(LastTabKey);
        PlayerPrefs.Save();
        SceneManager.LoadScene(HomeSceneName);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private VisualElement NewPage()
    {
        overlayLayer.pickingMode = PickingMode.Position;
        return AdminUI.Box(overlayLayer, "adm-page-overlay");
    }

    private void ClosePage(VisualElement page)
    {
        page?.RemoveFromHierarchy();
        if (overlayLayer.childCount == 0) overlayLayer.pickingMode = PickingMode.Ignore;
    }

    private void CloseAllPages()
    {
        overlayLayer?.Clear();
        if (overlayLayer != null) overlayLayer.pickingMode = PickingMode.Ignore;
        root.Q<VisualElement>("adm-sheet-overlay")?.RemoveFromHierarchy();
    }

    private IEnumerator LoadReports(ContentQueueItem item, Action<ContentReportRecord[]> onSuccess, Action<string> onError)
    {
        yield return SupabaseModerationService.GetContentReportsForTarget(item, onSuccess, onError);
    }

    private static void SetError(Label label, string message)
    {
        label.AddToClassList("adm-status--error");
        label.text = message;
    }

    private static string ShortId(string id) =>
        string.IsNullOrEmpty(id) ? "0000" : id.Replace("-", string.Empty).Substring(0, Math.Min(4, id.Length)).ToUpperInvariant();

    private static string RoleLabel(string role)
    {
        switch (role)
        {
            case "teacher": return T("Teacher", "Giáo viên");
            case "admin": return "Admin";
            default: return T("Student", "Học sinh");
        }
    }

    private static string RoleSuffix(string role) => string.IsNullOrEmpty(role) ? string.Empty : " · " + RoleLabel(role);

    private static DateTime ParseTime(string iso)
    {
        if (!string.IsNullOrWhiteSpace(iso) &&
            DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t))
            return t;
        return DateTime.MinValue;
    }

    private static string FullTime(string iso)
    {
        DateTime t = ParseTime(iso);
        return t == DateTime.MinValue ? string.Empty : t.ToLocalTime().ToString("dd/MM · HH:mm", CultureInfo.InvariantCulture);
    }

    private static string ClockTime(string iso)
    {
        DateTime t = ParseTime(iso);
        return t == DateTime.MinValue ? string.Empty : t.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    [Serializable]
    private class SignedUrlResponse
    {
        public string url;
        public string signed_url;
    }

    [Serializable]
    private class AdminModelManifest
    {
        public string class_id;
        public string lesson_id;
        public string mode;
        public AdminModelItem[] models;
    }

    [Serializable]
    private class AdminModelItem
    {
        public string asset_id;
        public string lesson_id;
        public string lesson_title;
        public int chapter_order;
        public string name;
        public string file_name;
        public string bucket;
        public string storage_path;
        public string url;
        public string fallback_url;
        public int display_order;
    }
}
