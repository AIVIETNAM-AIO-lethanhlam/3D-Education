using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// QA BUG-003..BUG-010: on Android, a UI Toolkit TextField could not be edited
/// again after the soft keyboard had been closed (tap outside / Back / Done).
///
/// With the GameActivity entry point, Unity edits text "in place" and the
/// TextField keeps keyboard focus after the keyboard hides. Tapping the same
/// field again does not change focus, so the keyboard never reopens and the
/// field looks locked (no caret, Backspace does nothing).
///
/// This component is installed automatically for every scene and applies to
/// every TextField in every UIDocument, including fields created at runtime:
///  - tap on a focused field while the keyboard is hidden -> blur + focus again
///    (reopens the keyboard);
///  - tap on a field that does not bring the keyboard up -> retry once;
///  - tap outside any text field -> the focused field is blurred, so the next
///    tap starts a clean edit session.
/// Device builds only; the Editor and desktop builds are not affected.
/// </summary>
public class MobileTextFieldFocusFix : MonoBehaviour
{
    private const float RescanInterval = 0.5f;
    private const long RefocusDelayMs = 80;
    private const long KeyboardCheckDelayMs = 500;

    private static MobileTextFieldFocusFix instance;

    private readonly HashSet<IPanel> registeredPanels = new HashSet<IPanel>();
    private float nextScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
#if UNITY_EDITOR
        return;
#else
        if (!Application.isMobilePlatform || !TouchScreenKeyboard.isSupported || instance != null)
            return;

        GameObject host = new GameObject("[MobileTextFieldFocusFix]");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<MobileTextFieldFocusFix>();
#endif
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        nextScanTime = 0f;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScanTime)
            return;

        nextScanTime = Time.unscaledTime + RescanInterval;
        RegisterPanels();
    }

    private void RegisterPanels()
    {
        registeredPanels.RemoveWhere(panel => panel == null || panel.visualTree == null);

        foreach (UIDocument document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
        {
            VisualElement root = document != null ? document.rootVisualElement : null;
            IPanel panel = root?.panel;

            if (panel == null || panel.visualTree == null || registeredPanels.Contains(panel))
                continue;

            // TrickleDown: runs before the TextField handles the pointer itself.
            panel.visualTree.RegisterCallback<PointerDownEvent>(HandlePointerDown, TrickleDown.TrickleDown);
            registeredPanels.Add(panel);
        }
    }

    private static void HandlePointerDown(PointerDownEvent evt)
    {
        VisualElement target = evt.target as VisualElement;
        IPanel panel = target?.panel;
        if (panel == null)
            return;

        TextField tappedField = FindTextField(target);
        TextField focusedField = FindTextField(panel.focusController?.focusedElement as VisualElement);

        if (tappedField == null)
        {
            // Tap outside every text field: end the edit session cleanly.
            // Buttons are excluded: blurring on PointerDown would close the
            // keyboard, move the composer and make Send buttons miss the click.
            if (!IsInsideButton(target))
                focusedField?.Blur();
            return;
        }

        if (tappedField.isReadOnly || !tappedField.enabledInHierarchy)
            return;

        if (tappedField == focusedField && !TouchScreenKeyboard.visible)
        {
            // Still focused but the keyboard is gone -> reopen it.
            Refocus(tappedField);
            return;
        }

        // Normal focus. If the keyboard still does not come up, retry once.
        tappedField.schedule.Execute(() =>
        {
            if (IsFocused(tappedField) && !TouchScreenKeyboard.visible)
                Refocus(tappedField);
        }).ExecuteLater(KeyboardCheckDelayMs);
    }

    private static void Refocus(TextField field)
    {
        field.Blur();
        field.schedule.Execute(() =>
        {
            if (field.panel == null || !field.enabledInHierarchy)
                return;

            field.Focus();

            // Focus() selects the whole text (selectAllOnFocus); put the caret
            // at the end instead so the first key does not wipe the draft.
            int length = field.value?.Length ?? 0;
            field.SelectRange(length, length);
        }).ExecuteLater(RefocusDelayMs);
    }

    private static bool IsInsideButton(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
        {
            if (current is Button)
                return true;
        }

        return false;
    }

    private static bool IsFocused(TextField field)
    {
        VisualElement focused = field.panel?.focusController?.focusedElement as VisualElement;
        return focused != null && FindTextField(focused) == field;
    }

    private static TextField FindTextField(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
        {
            if (current is TextField field)
                return field;
        }

        return null;
    }
}
