using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class ARModelSceneUIController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private ARModelSceneController sceneController;
    [SerializeField] private ARModelGestureController gestureController;

    private Button backButton;
    private Button resetButton;
    private Button rotateLeftButton;
    private Button rotateRightButton;
    private Button zoomInButton;
    private Button zoomOutButton;

    private void OnEnable()
    {
        if (uiDocument == null)
        {
            Debug.LogError("[ARModelSceneUI] UIDocument is missing.");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;

        backButton = root.Q<Button>("back-button");
        resetButton = root.Q<Button>("reset-button");
        rotateLeftButton = root.Q<Button>("rotate-left-button");
        rotateRightButton = root.Q<Button>("rotate-right-button");
        zoomInButton = root.Q<Button>("zoom-in-button");
        zoomOutButton = root.Q<Button>("zoom-out-button");

        if (backButton != null)
        {
            backButton.clicked += GoBack;
        }

        if (resetButton != null)
        {
            resetButton.clicked += ResetModel;
        }

        if (rotateLeftButton != null)
        {
            rotateLeftButton.clicked += gestureController.RotateLeft;
        }

        if (rotateRightButton != null)
        {
            rotateRightButton.clicked += gestureController.RotateRight;
        }

        if (zoomInButton != null)
        {
            zoomInButton.clicked += gestureController.ZoomIn;
        }

        if (zoomOutButton != null)
        {
            zoomOutButton.clicked += gestureController.ZoomOut;
        }
    }

    private void OnDisable()
    {
        if (backButton != null)
        {
            backButton.clicked -= GoBack;
        }

        if (resetButton != null)
        {
            resetButton.clicked -= ResetModel;
        }

        if (rotateLeftButton != null)
        {
            rotateLeftButton.clicked -= gestureController.RotateLeft;
        }

        if (rotateRightButton != null)
        {
            rotateRightButton.clicked -= gestureController.RotateRight;
        }

        if (zoomInButton != null)
        {
            zoomInButton.clicked -= gestureController.ZoomIn;
        }

        if (zoomOutButton != null)
        {
            zoomOutButton.clicked -= gestureController.ZoomOut;
        }
    }

    private void GoBack()
    {
        SceneManager.LoadScene("ShowLessonScene");
    }

    private void ResetModel()
    {
        sceneController.ResetPlacement();
    }
}