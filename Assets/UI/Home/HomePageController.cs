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
        RegisterButtonEvents();
    }

    private void OnDisable() => UnregisterButtonEvents();

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

    private void OpenLoginPage() { Debug.Log("Mở trang đăng nhập");  SceneManager.LoadScene("LoginScene");  }
    private void OpenRegisterPage()
    {
        PlayerPrefs.SetString("open_auth_tab", "register");
        PlayerPrefs.Save();

        // SceneManager.LoadScene("LoginScene");
    }
    private void OpenModelList() { Debug.Log("Mở danh sách mô hình 3D"); /* SceneManager.LoadScene("ModelListScene"); */ }
    private void OpenPrivacyPolicy() { Debug.Log("Mở chính sách quyền riêng tư"); /* Application.OpenURL("URL của bạn"); */ }
}
