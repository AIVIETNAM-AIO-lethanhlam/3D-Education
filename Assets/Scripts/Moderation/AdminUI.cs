using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

/// <summary>
/// Small UI Toolkit builders shared by AdminScene and NotificationScene.
/// All classes live in Assets/UI/Admin/AdminPage.uss (values follow the Figma
/// "3D Education – Admin Role" frames, scaled 390 → 430 px).
/// </summary>
public static class AdminUI
{
    // ------------------------------------------------------------------
    // Basics
    // ------------------------------------------------------------------

    public static Label Text(VisualElement parent, string text, params string[] classes)
    {
        Label label = new Label(text ?? string.Empty);
        foreach (string c in classes) if (!string.IsNullOrEmpty(c)) label.AddToClassList(c);
        parent?.Add(label);
        return label;
    }

    public static VisualElement Box(VisualElement parent, params string[] classes)
    {
        VisualElement element = new VisualElement();
        foreach (string c in classes) if (!string.IsNullOrEmpty(c)) element.AddToClassList(c);
        parent?.Add(element);
        return element;
    }

    /// <summary>PNG icon. tint: red | amber | purple | blue | green | gray | white | light (only for white glyph icons).</summary>
    public static VisualElement Icon(VisualElement parent, string iconName, string tint = null)
    {
        VisualElement icon = Box(parent, "adm-icon", "adm-icon--" + iconName);
        if (!string.IsNullOrEmpty(tint)) icon.AddToClassList("adm-tint--" + tint);
        icon.pickingMode = PickingMode.Ignore;
        return icon;
    }

    /// <summary>Rounded square with an icon. color: red | amber | purple | blue | green (soft background).</summary>
    public static VisualElement IconBox(VisualElement parent, string iconName, string color, string size = null)
    {
        VisualElement box = Box(parent, "adm-icon-box", "adm-icon-box--" + color);
        if (!string.IsNullOrEmpty(size)) box.AddToClassList("adm-icon-box--" + size);
        box.pickingMode = PickingMode.Ignore;
        Icon(box, iconName);
        return box;
    }

    /// <summary>Soft coloured box with a tinted white glyph icon (⚑ ▣ ✎ …) as in the Figma design.</summary>
    public static VisualElement GlyphBox(VisualElement parent, string glyphIcon, string color, string size = null)
    {
        VisualElement box = Box(parent, "adm-icon-box", "adm-icon-box--" + color);
        if (!string.IsNullOrEmpty(size)) box.AddToClassList("adm-icon-box--" + size);
        box.pickingMode = PickingMode.Ignore;
        Icon(box, glyphIcon, color);
        return box;
    }

    /// <summary>Soft coloured box with a text glyph ("!", "?").</summary>
    public static VisualElement TextGlyphBox(VisualElement parent, string glyph, string color, string size = null, string boxColor = null)
    {
        VisualElement box = Box(parent, "adm-icon-box", "adm-icon-box--" + (boxColor ?? color));
        if (!string.IsNullOrEmpty(size)) box.AddToClassList("adm-icon-box--" + size);
        box.pickingMode = PickingMode.Ignore;
        Text(box, glyph, "adm-glyph-text", "adm-text--" + color).pickingMode = PickingMode.Ignore;
        return box;
    }

    public static VisualElement Card(VisualElement parent, Action onClick = null, params string[] extra)
    {
        VisualElement card = Box(parent, "adm-card");
        foreach (string c in extra) if (!string.IsNullOrEmpty(c)) card.AddToClassList(c);
        if (onClick != null)
        {
            card.AddToClassList("adm-card--tap");
            card.RegisterCallback<ClickEvent>(_ => onClick());
        }
        return card;
    }

    public static Button Btn(VisualElement parent, string text, string kind, Action onClick, bool gap = false)
    {
        Button button = new Button(() => onClick?.Invoke()) { text = text };
        button.AddToClassList("adm-btn");
        button.AddToClassList("adm-btn--" + kind);
        if (gap) button.AddToClassList("adm-btn--gap");
        parent?.Add(button);
        return button;
    }

    public static Button SmallButton(VisualElement parent, string text, Action onClick, bool purple = false)
    {
        Button button = new Button(() => onClick?.Invoke()) { text = text };
        button.AddToClassList("adm-btn-small");
        if (purple) button.AddToClassList("adm-btn-small--purple");
        button.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        parent?.Add(button);
        return button;
    }

    /// <summary>Legacy outlined chip (NotificationScene).</summary>
    public static Button Chip(VisualElement parent, string text, bool active, Action onClick)
    {
        Button chip = new Button(() => onClick?.Invoke()) { text = text };
        chip.AddToClassList("adm-chip");
        if (active) chip.AddToClassList("adm-chip--active");
        parent?.Add(chip);
        return chip;
    }

    /// <summary>Figma filter pill. style: active | blue | purple | soft | plain.</summary>
    public static Button Pill(VisualElement parent, string text, string style, Action onClick, bool selected = false)
    {
        Button pill = new Button(() => onClick?.Invoke()) { text = text };
        pill.AddToClassList("adm-pill");
        if (!string.IsNullOrEmpty(style) && style != "plain") pill.AddToClassList("adm-pill--" + style);
        if (selected) pill.AddToClassList("adm-pill--selected");
        parent?.Add(pill);
        return pill;
    }

    /// <summary>color: red | amber | purple | blue | green</summary>
    public static Label Badge(VisualElement parent, string text, string color)
    {
        return Text(parent, text, "adm-badge", "adm-badge--" + color);
    }

    /// <summary>Red "⚑ N báo cáo" badge.</summary>
    public static VisualElement FlagBadge(VisualElement parent, string text)
    {
        VisualElement badge = Box(parent, "adm-badge-flag");
        Icon(badge, "flag", "red");
        Text(badge, text, "adm-badge-flag-label");
        return badge;
    }

    public static VisualElement Avatar(VisualElement parent, string name, string size = null)
    {
        VisualElement avatar = Box(parent, "adm-avatar", "adm-avatar--c" + ColorIndex(name));
        if (!string.IsNullOrEmpty(size)) avatar.AddToClassList("adm-avatar--" + size);
        Text(avatar, Initials(name), "adm-avatar-label");
        return avatar;
    }

    private static int ColorIndex(string name)
    {
        if (string.IsNullOrEmpty(name)) return 1;
        int h = 0;
        foreach (char ch in name) h = (h * 31 + ch) & 0x7fffffff;
        return h % 5;
    }

    public static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        string[] parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string first = parts[0].Substring(0, 1);
        string last = parts.Length > 1 ? parts[parts.Length - 1].Substring(0, 1) : string.Empty;
        return (first + last).ToUpperInvariant();
    }

    // ------------------------------------------------------------------
    // Page structure
    // ------------------------------------------------------------------

    /// <summary>White page header (Figma A2/A3/B1–B4). large = list page title.</summary>
    public static VisualElement Header(VisualElement parent, string title, string subtitle, Action onBack, bool large = false)
    {
        VisualElement header = Box(parent, "adm-header");
        if (onBack != null)
        {
            Button back = new Button(() => onBack()) { name = "adm-back-button" };
            back.AddToClassList("adm-circle-button");
            Icon(back, "back");
            header.Add(back);
        }

        VisualElement text = Box(header, "adm-header-text");
        if (onBack == null) text.AddToClassList("adm-header-text--noback");
        Label t = Text(text, title, "adm-header-title");
        if (large) t.AddToClassList("adm-header-title--large");
        if (!string.IsNullOrWhiteSpace(subtitle)) Text(text, subtitle, "adm-header-sub");
        return header;
    }

    /// <summary>Invisible 46px box at the right of a header without action, so the title stays centred between equal margins.</summary>
    public static VisualElement HeaderSpacer(VisualElement header)
    {
        VisualElement spacer = Box(header, "adm-header-spacer");
        spacer.pickingMode = PickingMode.Ignore;
        return spacer;
    }

    /// <summary>Round light-blue action button at the right of a header.</summary>
    public static Button HeaderAction(VisualElement header, string iconName, Action onClick)
    {
        Button button = new Button(() => onClick?.Invoke());
        button.AddToClassList("adm-circle-button");
        button.AddToClassList("adm-circle-button--action");
        Icon(button, iconName);
        header?.Add(button);
        return button;
    }

    public static ScrollView Scroll(VisualElement parent)
    {
        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.AddToClassList("adm-scroll");
        scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        // Clamped: content can never be dragged past its top/bottom (fixes the empty gap under the header).
        scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
        scroll.mode = ScrollViewMode.Vertical;
        parent?.Add(scroll);
        return scroll;
    }

    public static TextField Field(VisualElement parent, string placeholder, bool multiline, string value = null)
    {
        TextField field = new TextField { multiline = multiline, maxLength = 2000 };
        field.AddToClassList("adm-field");
        if (multiline) field.AddToClassList("adm-field--multiline");
        field.textEdition.placeholder = placeholder ?? string.Empty;
        if (!string.IsNullOrEmpty(value)) field.SetValueWithoutNotify(value);
        parent?.Add(field);
        return field;
    }

    public static VisualElement Empty(VisualElement parent, string iconName, string title, string text)
    {
        VisualElement empty = Box(parent, "adm-empty");
        IconBox(empty, iconName, "blue", "large");
        Text(empty, title, "adm-empty-title");
        if (!string.IsNullOrWhiteSpace(text)) Text(empty, text, "adm-empty-text");
        return empty;
    }

    public static VisualElement Divider(VisualElement parent)
    {
        return Box(parent, "adm-divider");
    }

    /// <summary>Label + value row inside a card (Figma B2 info card).</summary>
    public static void InfoRow(VisualElement parent, string label, string value)
    {
        VisualElement row = Box(parent, "adm-info-row");
        Text(row, label, "adm-info-label");
        Text(row, value ?? string.Empty, "adm-info-value");
    }

    /// <summary>Blue info note with "i" icon (Figma A3).</summary>
    public static VisualElement Note(VisualElement parent, string text)
    {
        VisualElement note = Box(parent, "adm-note");
        Icon(note, "info", "blue");
        Text(note, text, "adm-note-text");
        return note;
    }

    // ------------------------------------------------------------------
    // Bottom sheet (Figma A4 / B2 / B4)
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a bottom sheet over <paramref name="root"/>. When iconBox is given the title is centred
    /// (A4, B4); otherwise it is left aligned (B2). Returns the sheet content container.
    /// </summary>
    public static VisualElement Sheet(VisualElement root, string title, string description, out VisualElement overlay,
        VisualElement iconBox = null)
    {
        root.Q<VisualElement>("adm-sheet-overlay")?.RemoveFromHierarchy();
        VisualElement ov = new VisualElement { name = "adm-sheet-overlay" };
        ov.AddToClassList("adm-sheet-overlay");
        ov.RegisterCallback<PointerDownEvent>(evt => { if (evt.target == ov) ov.RemoveFromHierarchy(); });

        ScrollView sheet = new ScrollView(ScrollViewMode.Vertical);
        sheet.AddToClassList("adm-sheet");
        sheet.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        sheet.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        sheet.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
        ov.Add(sheet);

        Box(sheet, "adm-sheet-handle");
        if (iconBox != null)
        {
            sheet.Add(iconBox);
            Text(sheet, title, "adm-sheet-title", "adm-sheet-title--center");
        }
        else
        {
            Text(sheet, title, "adm-sheet-title");
        }
        if (!string.IsNullOrWhiteSpace(description)) Text(sheet, description, "adm-sheet-desc");

        root.Add(ov);
        overlay = ov;
        return sheet;
    }

    /// <summary>Single-choice pill row used inside sheets. Returns a getter for the chosen value.</summary>
    public static Func<string> PillChoice(VisualElement parent, IList<string> values, Func<string, string> label, string selected,
        Action<string> onChanged = null)
    {
        VisualElement row = Box(parent, "adm-pill-row");
        string current = selected;
        List<Button> pills = new List<Button>();
        foreach (string v in values)
        {
            string value = v;
            Button pill = Pill(row, label(value), value == current ? "active" : "soft", null);
            pill.style.marginBottom = 8;
            pill.clicked += () =>
            {
                current = value;
                foreach (Button p in pills)
                {
                    p.RemoveFromClassList("adm-pill--active");
                    p.AddToClassList("adm-pill--soft");
                }
                pill.RemoveFromClassList("adm-pill--soft");
                pill.AddToClassList("adm-pill--active");
                onChanged?.Invoke(value);
            };
            pills.Add(pill);
        }
        return () => current;
    }

    public static void Bullets(VisualElement parent, IEnumerable<string> lines)
    {
        VisualElement box = Box(parent, "adm-bullets");
        foreach (string line in lines)
        {
            VisualElement row = Box(box, "adm-bullet");
            Box(row, "adm-bullet-dot");
            Text(row, line, "adm-bullet-text");
        }
    }
}
