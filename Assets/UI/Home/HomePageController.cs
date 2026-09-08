using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class HomePageController : MonoBehaviour
{
    private Button headerLoginButton;
    private Button loginButton;
    private Button registerButton;
    private Button viewAllModelsButton;
    private Button privacyButton;

    private Label heroTitleLabel;
    private Label heroSubtitleLabel;
    private Label featuredModelsTitleLabel;

    private void OnEnable()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("Không tìm thấy UIDocument.");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        headerLoginButton = root.Q<Button>("header-login-button");
        loginButton = root.Q<Button>("login-button");
        registerButton = root.Q<Button>("register-button");
        viewAllModelsButton = root.Q<Button>("view-all-models-button");
        privacyButton = root.Q<Button>("privacy-button");

        heroTitleLabel = root.Q<Label>("hero-title-label");
        heroSubtitleLabel = root.Q<Label>("hero-subtitle-label");
        featuredModelsTitleLabel = root.Q<Label>("featured-models-title-label");

        AppLanguageManager.LanguageChanged += OnLanguageChanged;

        RegisterButtonEvents();
        ApplyCurrentLanguage();
    }

    private void OnDisable()
    {
        AppLanguageManager.LanguageChanged -= OnLanguageChanged;
        UnregisterButtonEvents();
    }

    private void RegisterButtonEvents()
    {
        if (headerLoginButton != null) headerLoginButton.clicked += OpenLoginPage;
        if (loginButton != null) loginButton.clicked += OpenLoginPage;
        if (registerButton != null) registerButton.clicked += OpenRegisterPage;
        if (viewAllModelsButton != null) viewAllModelsButton.clicked += OpenModelList;
        if (privacyButton != null) privacyButton.clicked += OpenPrivacyPolicy;
    }

    private void UnregisterButtonEvents()
    {
        if (headerLoginButton != null) headerLoginButton.clicked -= OpenLoginPage;
        if (loginButton != null) loginButton.clicked -= OpenLoginPage;
        if (registerButton != null) registerButton.clicked -= OpenRegisterPage;
        if (viewAllModelsButton != null) viewAllModelsButton.clicked -= OpenModelList;
        if (privacyButton != null) privacyButton.clicked -= OpenPrivacyPolicy;
    }

    private void OnLanguageChanged(string language)
    {
        ApplyCurrentLanguage();
    }

    private void ApplyCurrentLanguage()
    {
        bool vi = AppLanguageManager.IsVietnamese;

        if (headerLoginButton != null)
            headerLoginButton.text = vi ? "Đăng nhập" : "Login";

        if (loginButton != null)
            loginButton.text = vi ? "Đăng nhập" : "Login";

        if (registerButton != null)
            registerButton.text = vi ? "Đăng ký" : "Register";

        if (viewAllModelsButton != null)
            viewAllModelsButton.text = vi ? "Xem tất cả" : "View All";

        if (privacyButton != null)
            privacyButton.text = vi ? "Chính sách quyền riêng tư" : "Privacy Policy";

        if (heroTitleLabel != null)
            heroTitleLabel.text = vi ? "Bắt đầu học" : "Start Learning";

        if (heroSubtitleLabel != null)
        {
            heroSubtitleLabel.text = vi
                ? "Khám phá mô hình 3D tương tác và trải nghiệm học tập trực quan."
                : "Explore interactive 3D models and immersive learning experiences.";
        }

        if (featuredModelsTitleLabel != null)
            featuredModelsTitleLabel.text = vi ? "Mô hình nổi bật" : "Featured Models";
    }

    private void OpenLoginPage() { Debug.Log("Mở trang đăng nhập");  SceneManager.LoadScene("AuthScene");  }
    private void OpenRegisterPage()
    {
        PlayerPrefs.SetString("open_auth_tab", "register");
        PlayerPrefs.Save();

        // SceneManager.LoadScene("LoginScene");
    }
    private void OpenModelList() { Debug.Log("Mở danh sách mô hình 3D"); /* SceneManager.LoadScene("ModelListScene"); */ }
    private void OpenPrivacyPolicy() { Debug.Log("Mở chính sách quyền riêng tư"); /* Application.OpenURL("URL của bạn"); */ }
}
