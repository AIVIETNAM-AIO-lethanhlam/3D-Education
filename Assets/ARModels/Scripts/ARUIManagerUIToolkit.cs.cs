using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ARHeartTest
{
    /// <summary>
    /// UI Toolkit manager cho ARScene.
    /// Danh sách model bây giờ lấy trực tiếp từ ARHeartPlacementController.ModelCount /
    /// GetModelName(), vì controller có thể chứa runtime models từ Supabase hoặc fallback prefabs.
    /// </summary>
    public class ARUIManagerUIToolkit : MonoBehaviour
    {
        [Header("UI Document Reference")]
        [SerializeField] private UIDocument uiDocument;

        [Header("AR Controller Reference")]
        [SerializeField] private ARHeartPlacementController arController;

        [Header("Sprites for Visibility Toggle")]
        [SerializeField] private Sprite showWhiteSprite;
        [SerializeField] private Sprite hideWhiteSprite;

        [Header("Sprites for Auto-Rotate Toggle")]
        [SerializeField] private Sprite rotateWhiteSprite;
        [SerializeField] private Sprite noRotateWhiteSprite;

        [Header("Sprite for Model List Button")]
        [SerializeField] private Sprite modelListIconSprite;

        private Button backButton;
        private Button menuButton;
        private VisualElement menuPanel;

        private Button toggleVisibilityBtn;
        private Button toggleRotateBtn;
        private Button modelListBtn;

        private VisualElement visibilityIcon;
        private VisualElement rotateIcon;
        private VisualElement modelListIcon;

        private VisualElement modelPopup;
        private Button closePopupBtn;
        private VisualElement modelListContainer;

        private VisualElement loadingPanel;
        private Label loadingLabel;

        private bool isMenuOpen;
        private bool isModelVisible = true;
        private bool isAutoRotating;

        private void OnEnable()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();

            if (arController == null)
                arController = FindAnyObjectByType<ARHeartPlacementController>();

            if (uiDocument == null)
            {
                Debug.LogError("[AR UI] UIDocument is missing.");
                return;
            }

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null)
                return;

            backButton = root.Q<Button>("back-button");
            menuButton = root.Q<Button>("menu-button");
            menuPanel = root.Q<VisualElement>("menu-panel");

            toggleVisibilityBtn = root.Q<Button>("toggle-visibility-btn");
            toggleRotateBtn = root.Q<Button>("toggle-rotate-btn");
            modelListBtn = root.Q<Button>("model-list-btn");

            visibilityIcon = root.Q<VisualElement>("visibility-icon");
            rotateIcon = root.Q<VisualElement>("rotate-icon");
            modelListIcon = root.Q<VisualElement>("model-list-icon");

            modelPopup = root.Q<VisualElement>("model-popup");
            closePopupBtn = root.Q<Button>("close-popup-btn");
            modelListContainer = root.Q<VisualElement>("model-list-container");

            loadingPanel = root.Q<VisualElement>("loading-panel");
            loadingLabel = root.Q<Label>("loading-label");

            if (backButton != null) backButton.clicked += OnBackClicked;
            if (menuButton != null) menuButton.clicked += ToggleMenuPanel;
            if (toggleVisibilityBtn != null) toggleVisibilityBtn.clicked += OnToggleVisibilityClicked;
            if (toggleRotateBtn != null) toggleRotateBtn.clicked += OnToggleRotateClicked;
            if (modelListBtn != null) modelListBtn.clicked += OpenModelPopup;
            if (closePopupBtn != null) closePopupBtn.clicked += CloseModelPopup;

            if (arController != null)
            {
                arController.ModelListChanged += OnModelListChanged;
                arController.ModelChanged += OnModelChanged;
                arController.LoadingStateChanged += OnLoadingStateChanged;

                isModelVisible = arController.IsModelVisible;
                isAutoRotating = arController.IsAutoRotating;
            }

            if (modelListIcon != null && modelListIconSprite != null)
                modelListIcon.style.backgroundImage = new StyleBackground(modelListIconSprite);

            UpdateVisibilityUI();
            UpdateRotateUI();
            UpdateModelListButtonState();
            HideLoadingPanel();
        }

        private void Start()
        {
            // OnEnable có thể chạy trước ARHeartPlacementController.Start.
            // Refresh một frame sau để count/list chắc chắn đồng bộ.
            PopulateModelList();
            UpdateModelListButtonState();
        }

        private void OnDisable()
        {
            if (backButton != null) backButton.clicked -= OnBackClicked;
            if (menuButton != null) menuButton.clicked -= ToggleMenuPanel;
            if (toggleVisibilityBtn != null) toggleVisibilityBtn.clicked -= OnToggleVisibilityClicked;
            if (toggleRotateBtn != null) toggleRotateBtn.clicked -= OnToggleRotateClicked;
            if (modelListBtn != null) modelListBtn.clicked -= OpenModelPopup;
            if (closePopupBtn != null) closePopupBtn.clicked -= CloseModelPopup;

            if (arController != null)
            {
                arController.ModelListChanged -= OnModelListChanged;
                arController.ModelChanged -= OnModelChanged;
                arController.LoadingStateChanged -= OnLoadingStateChanged;
            }
        }

        private void OnBackClicked()
        {
            string targetScene = PlayerPrefs.GetString("previous_scene", "ShowLessonScene");
            if (string.IsNullOrWhiteSpace(targetScene))
                targetScene = "ShowLessonScene";

            string currentRole = PlayerPrefs.GetString("current_role", "student");
            PlayerPrefs.SetString("current_role", currentRole);
            PlayerPrefs.Save();

            Debug.Log($"[AR UI] Returning to {targetScene}. Role={currentRole}");

            if (Application.CanStreamedLevelBeLoaded(targetScene))
                SceneManager.LoadScene(targetScene);
            else
                Debug.LogError($"[AR UI] Scene '{targetScene}' is not in Build Profiles.");
        }

        private void ToggleMenuPanel()
        {
            isMenuOpen = !isMenuOpen;

            if (menuPanel == null)
                return;

            if (isMenuOpen)
                menuPanel.RemoveFromClassList("hidden");
            else
                menuPanel.AddToClassList("hidden");
        }

        private void OnToggleVisibilityClicked()
        {
            isModelVisible = arController != null
                ? arController.ToggleVisibility()
                : !isModelVisible;

            UpdateVisibilityUI();
        }

        private void UpdateVisibilityUI()
        {
            if (visibilityIcon == null) return;

            Sprite targetSprite = isModelVisible ? showWhiteSprite : hideWhiteSprite;
            if (targetSprite != null)
                visibilityIcon.style.backgroundImage = new StyleBackground(targetSprite);
        }

        private void OnToggleRotateClicked()
        {
            isAutoRotating = arController != null
                ? arController.ToggleAutoRotate()
                : !isAutoRotating;

            UpdateRotateUI();
        }

        private void UpdateRotateUI()
        {
            if (rotateIcon == null) return;

            Sprite targetSprite = isAutoRotating ? rotateWhiteSprite : noRotateWhiteSprite;
            if (targetSprite != null)
                rotateIcon.style.backgroundImage = new StyleBackground(targetSprite);
        }

        private void OpenModelPopup()
        {
            PopulateModelList();

            if (modelPopup != null)
                modelPopup.RemoveFromClassList("hidden");

            if (isMenuOpen)
                ToggleMenuPanel();
        }

        private void CloseModelPopup()
        {
            if (modelPopup != null)
                modelPopup.AddToClassList("hidden");
        }

        private void PopulateModelList()
        {
            if (modelListContainer == null)
                return;

            modelListContainer.Clear();

            if (arController == null || arController.ModelCount <= 0)
            {
                Label empty = new Label("Bài học này chưa có mô hình 3D.");
                empty.AddToClassList("model-empty-label");
                modelListContainer.Add(empty);
                return;
            }

            int currentIndex = arController.CurrentModelIndex;

            for (int i = 0; i < arController.ModelCount; i++)
            {
                int modelIndex = i;
                string modelName = arController.GetModelName(i);

                Button itemButton = new Button();
                itemButton.AddToClassList("model-item-btn");

                VisualElement textArea = new VisualElement();
                textArea.AddToClassList("model-item-text-area");

                Label nameLabel = new Label(modelName);
                nameLabel.AddToClassList("model-item-name");

                Label stateLabel = new Label();
                stateLabel.AddToClassList("model-item-state");
                stateLabel.text = arController.IsModelCached(i) ? "Đã tải" : "Nhấn để mở";

                textArea.Add(nameLabel);
                textArea.Add(stateLabel);
                itemButton.Add(textArea);

                if (modelIndex == currentIndex)
                    itemButton.AddToClassList("model-item-btn-selected");

                itemButton.clicked += () =>
                {
                    if (arController == null || arController.IsLoadingModel)
                        return;

                    arController.SpawnModelIndex(modelIndex);
                    CloseModelPopup();
                };

                modelListContainer.Add(itemButton);
            }
        }

        private void OnModelListChanged()
        {
            PopulateModelList();
            UpdateModelListButtonState();
        }

        private void OnModelChanged(int index)
        {
            PopulateModelList();
        }

        private void OnLoadingStateChanged(bool loading, string message)
        {
            if (loading)
            {
                if (loadingLabel != null)
                    loadingLabel.text = string.IsNullOrWhiteSpace(message)
                        ? "Đang tải mô hình 3D..."
                        : message;

                if (loadingPanel != null)
                    loadingPanel.RemoveFromClassList("hidden");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    HideLoadingPanel();
                }
                else
                {
                    // Dùng cùng panel để hiển thị lỗi ngắn gọn.
                    if (loadingLabel != null)
                        loadingLabel.text = message;

                    if (loadingPanel != null)
                        loadingPanel.RemoveFromClassList("hidden");
                }

                PopulateModelList();
            }

            UpdateModelListButtonState();
        }

        private void HideLoadingPanel()
        {
            if (loadingPanel != null)
                loadingPanel.AddToClassList("hidden");
        }

        private void UpdateModelListButtonState()
        {
            if (modelListBtn != null)
                modelListBtn.SetEnabled(arController != null && arController.ModelCount > 0 && !arController.IsLoadingModel);
        }
    }
}
