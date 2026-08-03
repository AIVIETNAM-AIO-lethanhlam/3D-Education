using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement; // 🔹 Đã thêm SceneManagement

namespace ARHeartTest
{
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

        // Buttons
        private Button backButton;
        private Button menuButton;
        private VisualElement menuPanel;

        private Button toggleVisibilityBtn;
        private Button toggleRotateBtn;
        private Button modelListBtn;

        // Inner Icon Elements
        private VisualElement visibilityIcon;
        private VisualElement rotateIcon;
        private VisualElement modelListIcon;

        // Pop-up Elements
        private VisualElement modelPopup;
        private Button closePopupBtn;
        private VisualElement modelListContainer;

        // States
        private bool isMenuOpen = false;
        private bool isModelVisible = true;
        private bool isAutoRotating = false;

        private void OnEnable()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (arController == null) arController = FindAnyObjectByType<ARHeartPlacementController>();

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            // Nút chính
            backButton = root.Q<Button>("back-button");
            menuButton = root.Q<Button>("menu-button");
            menuPanel = root.Q<VisualElement>("menu-panel");

            // Nút con
            toggleVisibilityBtn = root.Q<Button>("toggle-visibility-btn");
            toggleRotateBtn = root.Q<Button>("toggle-rotate-btn");
            modelListBtn = root.Q<Button>("model-list-btn");

            // Phần tử Icon con bên trong nút
            visibilityIcon = root.Q<VisualElement>("visibility-icon");
            rotateIcon = root.Q<VisualElement>("rotate-icon");
            modelListIcon = root.Q<VisualElement>("model-list-icon");

            // Pop-up
            modelPopup = root.Q<VisualElement>("model-popup");
            closePopupBtn = root.Q<Button>("close-popup-btn");
            modelListContainer = root.Q<VisualElement>("model-list-container");

            // Sự kiện Click
            if (backButton != null) backButton.clicked += OnBackClicked;
            if (menuButton != null) menuButton.clicked += ToggleMenuPanel;
            if (toggleVisibilityBtn != null) toggleVisibilityBtn.clicked += OnToggleVisibilityClicked;
            if (toggleRotateBtn != null) toggleRotateBtn.clicked += OnToggleRotateClicked;
            if (modelListBtn != null) modelListBtn.clicked += OpenModelPopup;
            if (closePopupBtn != null) closePopupBtn.clicked += CloseModelPopup;

            // Gán Icon cho nút Danh sách mô hình
            if (modelListIcon != null && modelListIconSprite != null)
            {
                modelListIcon.style.backgroundImage = new StyleBackground(modelListIconSprite);
            }

            // Khởi tạo trạng thái giao diện
            UpdateVisibilityUI();
            UpdateRotateUI();
        }

        private void OnDisable()
        {
            if (backButton != null) backButton.clicked -= OnBackClicked;
            if (menuButton != null) menuButton.clicked -= ToggleMenuPanel;
            if (toggleVisibilityBtn != null) toggleVisibilityBtn.clicked -= OnToggleVisibilityClicked;
            if (toggleRotateBtn != null) toggleRotateBtn.clicked -= OnToggleRotateClicked;
            if (modelListBtn != null) modelListBtn.clicked -= OpenModelPopup;
            if (closePopupBtn != null) closePopupBtn.clicked -= CloseModelPopup;
        }

        /// <summary>
        /// Xử lý khi nhấn nút Back: Quay về ShowLessonScene và giữ nguyên Role
        /// </summary>
        private void OnBackClicked()
        {
            Debug.Log("[AR UI] Back Button Clicked!");

            // Lấy lại Scene cần quay về (Mặc định là ShowLessonScene)
            string targetScene = PlayerPrefs.GetString("previous_scene", "ShowLessonScene");
            if (string.IsNullOrEmpty(targetScene))
            {
                targetScene = "ShowLessonScene";
            }

            // Đảm bảo Role vẫn được lưu lại chính xác trong PlayerPrefs
            string currentRole = PlayerPrefs.GetString("current_role", "student");
            PlayerPrefs.SetString("current_role", currentRole);
            PlayerPrefs.Save();

            Debug.Log($"[AR UI] Returning to {targetScene} with User Role: {currentRole}");

            // Thực hiện chuyển scene
            if (Application.CanStreamedLevelBeLoaded(targetScene))
            {
                SceneManager.LoadScene(targetScene);
            }
            else
            {
                Debug.LogError($"[AR UI] Scene '{targetScene}' chưa được thêm vào Build Settings!");
            }
        }

        private void ToggleMenuPanel()
        {
            isMenuOpen = !isMenuOpen;
            if (menuPanel != null)
            {
                if (isMenuOpen) menuPanel.RemoveFromClassList("hidden");
                else menuPanel.AddToClassList("hidden");
            }
        }

        private void OnToggleVisibilityClicked()
        {
            isModelVisible = (arController != null) ? arController.ToggleVisibility() : !isModelVisible;
            UpdateVisibilityUI();
        }

        private void UpdateVisibilityUI()
        {
            if (visibilityIcon == null) return;
            Sprite targetSprite = isModelVisible ? showWhiteSprite : hideWhiteSprite;
            if (targetSprite != null)
            {
                visibilityIcon.style.backgroundImage = new StyleBackground(targetSprite);
            }
        }

        private void OnToggleRotateClicked()
        {
            isAutoRotating = (arController != null) ? arController.ToggleAutoRotate() : !isAutoRotating;
            UpdateRotateUI();
        }

        private void UpdateRotateUI()
        {
            if (rotateIcon == null) return;
            Sprite targetSprite = isAutoRotating ? rotateWhiteSprite : noRotateWhiteSprite;
            if (targetSprite != null)
            {
                rotateIcon.style.backgroundImage = new StyleBackground(targetSprite);
            }
        }

        private void OpenModelPopup()
        {
            if (modelPopup != null)
            {
                PopulateModelList();
                modelPopup.RemoveFromClassList("hidden");
            }
            if (isMenuOpen) ToggleMenuPanel();
        }

        private void CloseModelPopup()
        {
            if (modelPopup != null)
            {
                modelPopup.AddToClassList("hidden");
            }
        }

        private void PopulateModelList()
        {
            if (modelListContainer == null || arController == null) return;

            modelListContainer.Clear();
            var prefabs = arController.ModelPrefabs;
            int currentIndex = arController.CurrentModelIndex;

            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] == null) continue;

                int modelIndex = i;
                string modelName = prefabs[i].name.Replace("Root", "").Replace("Prefab", "");

                Button itemBtn = new Button { text = modelName };
                itemBtn.AddToClassList("model-item-btn");

                if (modelIndex == currentIndex)
                {
                    itemBtn.AddToClassList("model-item-btn-selected");
                }

                itemBtn.clicked += () =>
                {
                    arController.SpawnModelIndex(modelIndex);
                    CloseModelPopup();
                };

                modelListContainer.Add(itemBtn);
            }
        }
    }
}