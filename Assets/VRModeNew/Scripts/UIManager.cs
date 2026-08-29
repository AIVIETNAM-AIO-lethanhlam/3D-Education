using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public ModelSpawner spawner;

    [Header("Panels")]
    public GameObject modelPanel;

    [Header("Legacy Canvas Buttons")]
    public GameObject spawnButton;
    public GameObject deleteButton;
    public GameObject holdButton;
    public GameObject dropButton;
    public GameObject interactButton;
    public GameObject openModelButton;
    public GameObject leftRotateButton;
    public GameObject rightRotateButton;
    public GameObject scaleDownButton;
    public GameObject scaleUpButton;
    public GameObject resetTransformButton;

    [Header("Mode UI")]
    public RotateUI rotateUI;
    public ScaleUI scaleUI;

    [SerializeField] private bool permanentlyHideLegacyToolbar = true;
    private bool legacyToolbarAllowed;

    private void Awake()
    {
        legacyToolbarAllowed = !permanentlyHideLegacyToolbar;
        if (permanentlyHideLegacyToolbar)
            HideAllLegacyToolbarButtons();
    }

    private void Start()
    {
        if (permanentlyHideLegacyToolbar)
            HideAllLegacyToolbarButtons();
    }

    private void LateUpdate()
    {
        if (permanentlyHideLegacyToolbar)
            HideAllLegacyToolbarButtons();
    }

    public void OpenModelPanel()
    {
        if (permanentlyHideLegacyToolbar)
        {
            HideAllLegacyToolbarButtons();
            return;
        }

        SetActiveSafe(modelPanel, true);
        SetMainButtonsVisible(false);
    }

    public void CloseModelPanel()
    {
        SetActiveSafe(modelPanel, false);
        rotateUI?.Refresh();
        scaleUI?.Refresh();

        if (permanentlyHideLegacyToolbar || !legacyToolbarAllowed)
        {
            HideAllLegacyToolbarButtons();
            return;
        }

        SetMainButtonsVisible(true);
        RefreshModeButtons();
    }

    public void SetLegacyToolbarVisible(bool visible)
    {
        if (permanentlyHideLegacyToolbar)
        {
            legacyToolbarAllowed = false;
            HideAllLegacyToolbarButtons();
            return;
        }

        legacyToolbarAllowed = visible;

        if (!visible)
            HideAllLegacyToolbarButtons();
        else
        {
            SetMainButtonsVisible(true);
            RefreshModeButtons();
        }
    }

    public void HideAllLegacyToolbarButtons()
    {
        SetMainButtonsVisible(false);

        SetActiveSafe(leftRotateButton, false);
        SetActiveSafe(rightRotateButton, false);
        SetActiveSafe(scaleDownButton, false);
        SetActiveSafe(scaleUpButton, false);

        // Remove the two legacy APK buttons.
        SetActiveSafe(interactButton, false);       // Hand
        SetActiveSafe(resetTransformButton, false); // Reset

        SetActiveSafe(modelPanel, false);
    }

    public void RefreshModeButtons()
    {
        if (permanentlyHideLegacyToolbar || !legacyToolbarAllowed || spawner == null)
        {
            HideAllLegacyToolbarButtons();
            return;
        }

        SetActiveSafe(leftRotateButton, spawner.RotateMode);
        SetActiveSafe(rightRotateButton, spawner.RotateMode);
        SetActiveSafe(scaleDownButton, spawner.ScaleMode);
        SetActiveSafe(scaleUpButton, spawner.ScaleMode);
    }

    public void InvokeSpawn() => InvokeButton(spawnButton, "spawnButton");
    public void InvokeDelete() => InvokeButton(deleteButton, "deleteButton");
    public void InvokeOpenModel() => InvokeButton(openModelButton, "openModelButton");

    public void InvokeReset()
    {
        HideAllLegacyToolbarButtons();
    }

    public void InvokeScale()
    {
        HideAllLegacyToolbarButtons();
    }

    private static void InvokeButton(GameObject target, string fieldName)
    {
        if (target == null)
        {
            Debug.LogWarning($"[UIManager] {fieldName} is not assigned.");
            return;
        }

        Button button = target.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning($"[UIManager] '{target.name}' has no Button.");
            return;
        }

        button.onClick.Invoke();
    }

    private void SetMainButtonsVisible(bool visible)
    {
        SetActiveSafe(spawnButton, visible);
        SetActiveSafe(deleteButton, visible);
        SetActiveSafe(holdButton, visible);
        SetActiveSafe(dropButton, visible);
        SetActiveSafe(interactButton, visible);
        SetActiveSafe(openModelButton, visible);
        SetActiveSafe(resetTransformButton, visible);
    }

    private static void SetActiveSafe(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
