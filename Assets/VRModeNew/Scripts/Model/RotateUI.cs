using UnityEngine;
using UnityEngine.UI;

public class RotateUI : MonoBehaviour
{
    public ModelSpawner spawner;

    public Button rotateModeButton;
    public Button leftButton;
    public Button rightButton;

    public UIManager uiManager;

    private Image rotateImage;

    private void Start()
    {
        if (rotateModeButton != null)
            rotateImage = rotateModeButton.GetComponent<Image>();

        if (spawner != null)
            spawner.OnModeChanged += Refresh;

        Refresh();
    }

    private void OnDestroy()
    {
        if (spawner != null)
            spawner.OnModeChanged -= Refresh;
    }

    public void ToggleRotate()
    {
        if (spawner == null)
        {
            Debug.LogWarning("[RotateUI] ModelSpawner is not assigned.");
            return;
        }

        spawner.ToggleRotateMode();

        // Refresh state only. UIManager will keep the old Canvas toolbar hidden.
        Refresh();

        if (uiManager != null)
            uiManager.SetLegacyToolbarVisible(false);
    }

    public void Refresh()
    {
        if (spawner == null)
            return;

        bool on = spawner.RotateMode;

        if (rotateImage != null)
        {
            rotateImage.color = on
                ? new Color(0.2f, 0.8f, 0.3f)
                : Color.white;
        }
    }
}
