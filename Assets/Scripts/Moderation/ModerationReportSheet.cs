using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Reusable "Report to Admin" bottom sheet (2026-09).
/// Used by ShowLessonScene, StartQuizScene and Mode3DScene to report a lesson,
/// a quiz or a 3D model as inappropriate for teaching.
/// Styles come from Resources/Moderation/ModerationUI.uss.
/// </summary>
public static class ModerationReportSheet
{
    private const string StyleSheetPath = "Moderation/ModerationUI";

    /// <summary>One reportable item shown as a chip (lesson / quiz / 3D model).</summary>
    public class Target
    {
        public string TargetType;   // lesson | quiz | model_3d
        public string LessonId;
        public string QuizId;
        public string AssetId;
        public string Label;
    }

    public static void EnsureStyles(VisualElement root)
    {
        if (root == null) return;
        StyleSheet sheet = Resources.Load<StyleSheet>(StyleSheetPath);
        if (sheet != null && !root.styleSheets.Contains(sheet))
            root.styleSheets.Add(sheet);
    }

    /// <summary>Round flag button (alert.png) that can be inserted into an existing header.</summary>
    public static Button CreateReportButton(Action onClick)
    {
        Button button = new Button(() => onClick?.Invoke()) { name = "report-content-button" };
        button.AddToClassList("mod-report-button");
        button.tooltip = AppLanguageManager.T("Report to Admin", "Báo cáo tới Admin");
        VisualElement icon = new VisualElement();
        icon.AddToClassList("mod-report-icon");
        icon.pickingMode = PickingMode.Ignore;
        button.Add(icon);
        return button;
    }

    /// <summary>Wide red "Report" button.</summary>
    public static Button CreateWideReportButton(string text, Action onClick)
    {
        Button button = new Button(() => onClick?.Invoke()) { name = "report-content-wide-button" };
        button.AddToClassList("mod-report-wide");
        VisualElement icon = new VisualElement();
        icon.AddToClassList("mod-report-icon");
        icon.pickingMode = PickingMode.Ignore;
        button.Add(icon);
        button.Add(new Label(text) { pickingMode = PickingMode.Ignore });
        return button;
    }

    /// <summary>Opens the sheet on top of <paramref name="root"/>.</summary>
    public static void Show(VisualElement root, MonoBehaviour host, IList<Target> targets)
    {
        if (root == null || host == null || targets == null || targets.Count == 0)
            return;

        EnsureStyles(root);
        root.Q<VisualElement>("mod-report-overlay")?.RemoveFromHierarchy();

        Target selectedTarget = targets[0];
        string selectedReason = SupabaseModerationService.ContentReasons[0];
        bool sending = false;

        VisualElement overlay = new VisualElement { name = "mod-report-overlay" };
        overlay.AddToClassList("mod-overlay");
        overlay.RegisterCallback<PointerDownEvent>(evt => { if (evt.target == overlay) overlay.RemoveFromHierarchy(); });

        ScrollView sheet = new ScrollView(ScrollViewMode.Vertical);
        sheet.AddToClassList("mod-sheet");
        sheet.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        overlay.Add(sheet);

        VisualElement handle = new VisualElement();
        handle.AddToClassList("mod-sheet-handle");
        sheet.Add(handle);

        AddLabel(sheet, AppLanguageManager.T("Report inappropriate content", "Báo cáo nội dung không phù hợp"), "mod-title");
        AddLabel(sheet, AppLanguageManager.T(
            "Admin will review it. If needed, the teacher will be asked to explain the content.",
            "Admin sẽ xem xét. Nếu cần, giáo viên sẽ được yêu cầu giải trình nội dung."), "mod-description");

        // --- target chips ---
        if (targets.Count > 1)
        {
            AddLabel(sheet, AppLanguageManager.T("Reported content", "Nội dung bị báo cáo"), "mod-section-label");
            VisualElement chipRow = new VisualElement();
            chipRow.AddToClassList("mod-chip-row");
            sheet.Add(chipRow);

            List<Button> chips = new List<Button>();
            foreach (Target target in targets)
            {
                Target captured = target;
                Button chip = new Button { text = target.Label };
                chip.AddToClassList("mod-chip");
                chip.clicked += () =>
                {
                    selectedTarget = captured;
                    foreach (Button c in chips) c.EnableInClassList("mod-chip--selected", c == chip);
                };
                chips.Add(chip);
                chipRow.Add(chip);
            }
            chips[0].AddToClassList("mod-chip--selected");
        }

        // --- reasons ---
        AddLabel(sheet, AppLanguageManager.T("Reason", "Lý do"), "mod-section-label");
        List<VisualElement> radios = new List<VisualElement>();
        foreach (string reason in SupabaseModerationService.ContentReasons)
        {
            string captured = reason;
            VisualElement radio = CreateRadio(SupabaseModerationService.ContentReasonLabel(reason));
            radio.RegisterCallback<ClickEvent>(_ =>
            {
                selectedReason = captured;
                foreach (VisualElement r in radios) r.EnableInClassList("mod-radio--selected", r == radio);
            });
            radios.Add(radio);
            sheet.Add(radio);
        }
        radios[0].AddToClassList("mod-radio--selected");

        // --- description ---
        TextField description = new TextField { multiline = true, maxLength = 1000 };
        description.AddToClassList("mod-textfield");
        description.textEdition.placeholder = AppLanguageManager.T(
            "More details (optional), e.g. video at 03:20...",
            "Mô tả thêm (không bắt buộc), ví dụ: video đoạn phút 03:20...");
        sheet.Add(description);

        // --- buttons ---
        VisualElement buttons = new VisualElement();
        buttons.AddToClassList("mod-button-row");
        Button cancel = new Button(() => overlay.RemoveFromHierarchy()) { text = AppLanguageManager.T("Cancel", "Hủy") };
        cancel.AddToClassList("mod-button");
        cancel.AddToClassList("mod-button--secondary");
        Button send = new Button { text = AppLanguageManager.T("Send to Admin", "Gửi tới Admin") };
        send.AddToClassList("mod-button");
        send.AddToClassList("mod-button--danger");
        buttons.Add(cancel);
        buttons.Add(send);
        sheet.Add(buttons);

        Label status = AddLabel(sheet, string.Empty, "mod-status");

        send.clicked += () =>
        {
            if (sending) return;
            sending = true;
            send.SetEnabled(false);
            status.RemoveFromClassList("mod-status--error");
            status.text = AppLanguageManager.T("Sending...", "Đang gửi...");

            host.StartCoroutine(SupabaseModerationService.SubmitContentReport(
                selectedTarget.TargetType,
                selectedTarget.LessonId,
                selectedTarget.QuizId,
                selectedTarget.AssetId,
                selectedReason,
                description.value,
                () =>
                {
                    overlay.RemoveFromHierarchy();
                    ShowToast(root, host, AppLanguageManager.T(
                        "Report sent to Admin. You will be notified when it is handled.",
                        "Đã gửi báo cáo tới Admin. Bạn sẽ nhận thông báo khi báo cáo được xử lý."));
                },
                error =>
                {
                    sending = false;
                    send.SetEnabled(true);
                    status.AddToClassList("mod-status--error");
                    status.text = AppLanguageManager.T("Cannot send report: ", "Không gửi được báo cáo: ") + error;
                }));
        };

        root.Add(overlay);
    }

    public static VisualElement CreateRadio(string text)
    {
        VisualElement radio = new VisualElement();
        radio.AddToClassList("mod-radio");
        VisualElement dot = new VisualElement { pickingMode = PickingMode.Ignore };
        dot.AddToClassList("mod-radio-dot");
        radio.Add(dot);
        Label label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("mod-radio-label");
        radio.Add(label);
        return radio;
    }

    public static void ShowToast(VisualElement root, MonoBehaviour host, string message)
    {
        if (root == null) return;
        EnsureStyles(root);
        root.Q<VisualElement>("mod-toast")?.RemoveFromHierarchy();

        VisualElement toast = new VisualElement { name = "mod-toast", pickingMode = PickingMode.Ignore };
        toast.AddToClassList("mod-toast");
        VisualElement icon = new VisualElement();
        icon.AddToClassList("mod-toast-icon");
        toast.Add(icon);
        Label label = new Label(message);
        label.AddToClassList("mod-toast-label");
        toast.Add(label);
        root.Add(toast);

        if (host != null)
            host.StartCoroutine(RemoveLater(toast, 3.5f));
    }

    private static IEnumerator RemoveLater(VisualElement element, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        element?.RemoveFromHierarchy();
    }

    private static Label AddLabel(VisualElement parent, string text, string className)
    {
        Label label = new Label(text);
        label.AddToClassList(className);
        parent.Add(label);
        return label;
    }
}
