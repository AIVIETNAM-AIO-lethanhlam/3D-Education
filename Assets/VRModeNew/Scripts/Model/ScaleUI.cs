using UnityEngine;
using UnityEngine.UI;

public class ScaleUI : MonoBehaviour
{
    public ModelSpawner spawner;

    public Button scaleButton;
    public Button plusButton;
    public Button minusButton;

    public UIManager uiManager;

    private Image scaleImage;

    private void Start()
    {
        if (scaleButton != null)
            scaleImage = scaleButton.GetComponent<Image>();

        if (spawner != null)
            spawner.OnModeChanged += Refresh;

        Refresh();
    }

    private void OnDestroy()
    {
        if (spawner != null)
            spawner.OnModeChanged -= Refresh;
    }

    public void ToggleScale()
    {
        if (spawner == null)
        {
            Debug.LogWarning("[ScaleUI] ModelSpawner is not assigned.");
            return;
        }

        spawner.ToggleScaleMode();

        Refresh();

        if (uiManager != null)
            uiManager.SetLegacyToolbarVisible(false);
    }

    public void Refresh()
    {
        if (spawner == null)
            return;

        bool on = spawner.ScaleMode;

        if (scaleImage != null)
        {
            scaleImage.color = on
                ? new Color(0.2f, 0.8f, 0.3f)
                : Color.white;
        }
    }
}
