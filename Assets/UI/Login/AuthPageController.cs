using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class AuthPageController : MonoBehaviour
{
    private Button loginTabButton;
    private Button registerTabButton;

    private VisualElement loginForm;
    private VisualElement registerForm;

    private Button loginTeacherButton;
    private Button loginStudentButton;
    private Button registerTeacherButton;
    private Button registerStudentButton;

    private VisualElement loginEmailContainer;
    private VisualElement loginPasswordContainer;
    private VisualElement registerNameContainer;
    private VisualElement registerEmailContainer;
    private VisualElement registerPasswordContainer;

    private TextField loginEmailField;
    private TextField loginPasswordField;
    private TextField registerNameField;
    private TextField registerEmailField;
    private TextField registerPasswordField;

    private Button loginTogglePasswordButton;
    private Button registerTogglePasswordButton;
    private VisualElement loginEyeIcon;
    private VisualElement registerEyeIcon;

    private Button rememberButton;
    private VisualElement rememberCheckbox;
    private Label rememberCheckmark;

    private Button forgotPasswordButton;
    private Button signInButton;
    private Button createAccountButton;
    private Button loginGoogleButton;
    private Button registerGoogleButton;
    private Button loginPrivacyButton;
    private Button registerPrivacyButton;

    private string loginRole = "student";
    private string registerRole = "student";

    private bool loginPasswordVisible;
    private bool registerPasswordVisible;
    private bool rememberLogin;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
        {
            Debug.LogError("Không tìm thấy UIDocument.");
            return;
        }

        VisualElement root = document.rootVisualElement;

        loginTabButton = root.Q<Button>("login-tab-button");
        registerTabButton = root.Q<Button>("register-tab-button");

        loginForm = root.Q<VisualElement>("login-form");
        registerForm = root.Q<VisualElement>("register-form");

        loginTeacherButton = root.Q<Button>("login-teacher-role-button");
        loginStudentButton = root.Q<Button>("login-student-role-button");
        registerTeacherButton = root.Q<Button>("register-teacher-role-button");
        registerStudentButton = root.Q<Button>("register-student-role-button");

        loginEmailContainer = root.Q<VisualElement>("login-email-container");
        loginPasswordContainer = root.Q<VisualElement>("login-password-container");
        registerNameContainer = root.Q<VisualElement>("register-name-container");
        registerEmailContainer = root.Q<VisualElement>("register-email-container");
        registerPasswordContainer = root.Q<VisualElement>("register-password-container");

        loginEmailField = root.Q<TextField>("login-email-field");
        loginPasswordField = root.Q<TextField>("login-password-field");
        registerNameField = root.Q<TextField>("register-name-field");
        registerEmailField = root.Q<TextField>("register-email-field");
        registerPasswordField = root.Q<TextField>("register-password-field");

        loginTogglePasswordButton = root.Q<Button>("login-toggle-password-button");
        registerTogglePasswordButton = root.Q<Button>("register-toggle-password-button");
        loginEyeIcon = root.Q<VisualElement>("login-eye-icon");
        registerEyeIcon = root.Q<VisualElement>("register-eye-icon");

        rememberButton = root.Q<Button>("remember-button");
        rememberCheckbox = root.Q<VisualElement>("remember-checkbox");
        rememberCheckmark = root.Q<Label>("remember-checkmark");

        forgotPasswordButton = root.Q<Button>("forgot-password-button");
        signInButton = root.Q<Button>("sign-in-button");
        createAccountButton = root.Q<Button>("create-account-button");
        loginGoogleButton = root.Q<Button>("login-google-button");
        registerGoogleButton = root.Q<Button>("register-google-button");
        loginPrivacyButton = root.Q<Button>("login-privacy-button");
        registerPrivacyButton = root.Q<Button>("register-privacy-button");

        RegisterEvents();

        SelectLoginStudent();
        SelectRegisterStudent();
        UpdateRememberVisual();

        string requestedTab = PlayerPrefs.GetString("open_auth_tab", "login");

        if (requestedTab == "register")
            ShowRegisterTab();
        else
            ShowLoginTab();

        PlayerPrefs.DeleteKey("open_auth_tab");
    }

    private void OnDisable()
    {
        UnregisterEvents();
    }

    private void RegisterEvents()
    {
        if (loginTabButton != null)
            loginTabButton.clicked += ShowLoginTab;

        if (registerTabButton != null)
            registerTabButton.clicked += ShowRegisterTab;

        if (loginTeacherButton != null)
            loginTeacherButton.clicked += SelectLoginTeacher;

        if (loginStudentButton != null)
            loginStudentButton.clicked += SelectLoginStudent;

        if (registerTeacherButton != null)
            registerTeacherButton.clicked += SelectRegisterTeacher;

        if (registerStudentButton != null)
            registerStudentButton.clicked += SelectRegisterStudent;

        if (loginTogglePasswordButton != null)
            loginTogglePasswordButton.clicked += ToggleLoginPassword;

        if (registerTogglePasswordButton != null)
            registerTogglePasswordButton.clicked += ToggleRegisterPassword;

        if (rememberButton != null)
            rememberButton.clicked += ToggleRemember;

        if (forgotPasswordButton != null)
            forgotPasswordButton.clicked += OpenForgotPassword;

        if (signInButton != null)
            signInButton.clicked += SignIn;

        if (createAccountButton != null)
            createAccountButton.clicked += CreateAccount;

        if (loginGoogleButton != null)
            loginGoogleButton.clicked += LoginWithGoogle;

        if (registerGoogleButton != null)
            registerGoogleButton.clicked += RegisterWithGoogle;

        if (loginPrivacyButton != null)
            loginPrivacyButton.clicked += OpenPrivacy;

        if (registerPrivacyButton != null)
            registerPrivacyButton.clicked += OpenPrivacy;

        RegisterFocus(loginEmailField, OnLoginEmailFocusIn, OnLoginEmailFocusOut);
        RegisterFocus(loginPasswordField, OnLoginPasswordFocusIn, OnLoginPasswordFocusOut);
        RegisterFocus(registerNameField, OnRegisterNameFocusIn, OnRegisterNameFocusOut);
        RegisterFocus(registerEmailField, OnRegisterEmailFocusIn, OnRegisterEmailFocusOut);
        RegisterFocus(registerPasswordField, OnRegisterPasswordFocusIn, OnRegisterPasswordFocusOut);
    }

    private void UnregisterEvents()
    {
        if (loginTabButton != null)
            loginTabButton.clicked -= ShowLoginTab;

        if (registerTabButton != null)
            registerTabButton.clicked -= ShowRegisterTab;

        if (loginTeacherButton != null)
            loginTeacherButton.clicked -= SelectLoginTeacher;

        if (loginStudentButton != null)
            loginStudentButton.clicked -= SelectLoginStudent;

        if (registerTeacherButton != null)
            registerTeacherButton.clicked -= SelectRegisterTeacher;

        if (registerStudentButton != null)
            registerStudentButton.clicked -= SelectRegisterStudent;

        if (loginTogglePasswordButton != null)
            loginTogglePasswordButton.clicked -= ToggleLoginPassword;

        if (registerTogglePasswordButton != null)
            registerTogglePasswordButton.clicked -= ToggleRegisterPassword;

        if (rememberButton != null)
            rememberButton.clicked -= ToggleRemember;

        if (forgotPasswordButton != null)
            forgotPasswordButton.clicked -= OpenForgotPassword;

        if (signInButton != null)
            signInButton.clicked -= SignIn;

        if (createAccountButton != null)
            createAccountButton.clicked -= CreateAccount;

        if (loginGoogleButton != null)
            loginGoogleButton.clicked -= LoginWithGoogle;

        if (registerGoogleButton != null)
            registerGoogleButton.clicked -= RegisterWithGoogle;

        if (loginPrivacyButton != null)
            loginPrivacyButton.clicked -= OpenPrivacy;

        if (registerPrivacyButton != null)
            registerPrivacyButton.clicked -= OpenPrivacy;

        UnregisterFocus(loginEmailField, OnLoginEmailFocusIn, OnLoginEmailFocusOut);
        UnregisterFocus(loginPasswordField, OnLoginPasswordFocusIn, OnLoginPasswordFocusOut);
        UnregisterFocus(registerNameField, OnRegisterNameFocusIn, OnRegisterNameFocusOut);
        UnregisterFocus(registerEmailField, OnRegisterEmailFocusIn, OnRegisterEmailFocusOut);
        UnregisterFocus(registerPasswordField, OnRegisterPasswordFocusIn, OnRegisterPasswordFocusOut);
    }

    private static void RegisterFocus(
        TextField field,
        EventCallback<FocusInEvent> focusIn,
        EventCallback<FocusOutEvent> focusOut)
    {
        if (field == null)
            return;

        field.RegisterCallback(focusIn);
        field.RegisterCallback(focusOut);
    }

    private static void UnregisterFocus(
        TextField field,
        EventCallback<FocusInEvent> focusIn,
        EventCallback<FocusOutEvent> focusOut)
    {
        if (field == null)
            return;

        field.UnregisterCallback(focusIn);
        field.UnregisterCallback(focusOut);
    }

    private void ShowLoginTab()
    {
        loginForm?.RemoveFromClassList("form-hidden");
        registerForm?.AddToClassList("form-hidden");

        loginTabButton?.AddToClassList("tab-button-active");
        registerTabButton?.RemoveFromClassList("tab-button-active");
    }

    private void ShowRegisterTab()
    {
        loginForm?.AddToClassList("form-hidden");
        registerForm?.RemoveFromClassList("form-hidden");

        loginTabButton?.RemoveFromClassList("tab-button-active");
        registerTabButton?.AddToClassList("tab-button-active");
    }

    private void SelectLoginTeacher()
    {
        loginRole = "teacher";
        loginTeacherButton?.AddToClassList("role-button-active");
        loginStudentButton?.RemoveFromClassList("role-button-active");
    }

    private void SelectLoginStudent()
    {
        loginRole = "student";
        loginStudentButton?.AddToClassList("role-button-active");
        loginTeacherButton?.RemoveFromClassList("role-button-active");
    }

    private void SelectRegisterTeacher()
    {
        registerRole = "teacher";
        registerTeacherButton?.AddToClassList("role-button-active");
        registerStudentButton?.RemoveFromClassList("role-button-active");
    }

    private void SelectRegisterStudent()
    {
        registerRole = "student";
        registerStudentButton?.AddToClassList("role-button-active");
        registerTeacherButton?.RemoveFromClassList("role-button-active");
    }

    private void ToggleLoginPassword()
    {
        loginPasswordVisible = !loginPasswordVisible;
        UpdatePasswordVisibility(loginPasswordField, loginEyeIcon, loginPasswordVisible);
    }

    private void ToggleRegisterPassword()
    {
        registerPasswordVisible = !registerPasswordVisible;
        UpdatePasswordVisibility(
            registerPasswordField,
            registerEyeIcon,
            registerPasswordVisible
        );
    }

    private static void UpdatePasswordVisibility(
        TextField field,
        VisualElement icon,
        bool visible)
    {
        if (field != null)
            field.isPasswordField = !visible;

        if (icon == null)
            return;

        icon.EnableInClassList("icon-eye-off", visible);
        icon.EnableInClassList("icon-eye", !visible);
    }

    private void ToggleRemember()
    {
        rememberLogin = !rememberLogin;
        UpdateRememberVisual();
    }

    private void UpdateRememberVisual()
    {
        rememberCheckbox?.EnableInClassList(
            "remember-checkbox-checked",
            rememberLogin
        );

        rememberCheckmark?.EnableInClassList(
            "remember-checkmark-hidden",
            !rememberLogin
        );
    }

    private void SignIn()
    {
        string email = loginEmailField?.value.Trim() ?? string.Empty;
        string password = loginPasswordField?.value ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email))
        {
            Debug.LogWarning("Vui lòng nhập email.");
            loginEmailField?.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            Debug.LogWarning("Vui lòng nhập mật khẩu.");
            loginPasswordField?.Focus();
            return;
        }

        PlayerPrefs.SetString("current_role", loginRole);
        PlayerPrefs.SetString("current_email", email);
        PlayerPrefs.SetInt("remember_login", rememberLogin ? 1 : 0);
        PlayerPrefs.Save();

        Debug.Log($"Đăng nhập: role={loginRole}, email={email}");
    }

    private void CreateAccount()
    {
        string fullName = registerNameField?.value.Trim() ?? string.Empty;
        string email = registerEmailField?.value.Trim() ?? string.Empty;
        string password = registerPasswordField?.value ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fullName))
        {
            Debug.LogWarning("Vui lòng nhập họ tên.");
            registerNameField?.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            Debug.LogWarning("Vui lòng nhập email.");
            registerEmailField?.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            Debug.LogWarning("Vui lòng nhập mật khẩu.");
            registerPasswordField?.Focus();
            return;
        }

        PlayerPrefs.SetString("pending_role", registerRole);
        PlayerPrefs.SetString("pending_name", fullName);
        PlayerPrefs.SetString("pending_email", email);
        PlayerPrefs.Save();

        Debug.Log(
            $"Tạo tài khoản: role={registerRole}, name={fullName}, email={email}"
        );

        // Kết nối API/Firebase đăng ký tại đây.
    }

    private void OpenForgotPassword()
    {
        Debug.Log("Mở trang quên mật khẩu.");
    }

    private void LoginWithGoogle()
    {
        Debug.Log("Đăng nhập bằng Google.");
    }

    private void RegisterWithGoogle()
    {
        Debug.Log($"Đăng ký bằng Google với role: {registerRole}");
    }

    private void OpenPrivacy()
    {
        Debug.Log("Mở Privacy Policy & Terms.");
    }

    private void OnLoginEmailFocusIn(FocusInEvent evt) =>
        loginEmailContainer?.AddToClassList("input-focused");

    private void OnLoginEmailFocusOut(FocusOutEvent evt) =>
        loginEmailContainer?.RemoveFromClassList("input-focused");

    private void OnLoginPasswordFocusIn(FocusInEvent evt) =>
        loginPasswordContainer?.AddToClassList("input-focused");

    private void OnLoginPasswordFocusOut(FocusOutEvent evt) =>
        loginPasswordContainer?.RemoveFromClassList("input-focused");

    private void OnRegisterNameFocusIn(FocusInEvent evt) =>
        registerNameContainer?.AddToClassList("input-focused");

    private void OnRegisterNameFocusOut(FocusOutEvent evt) =>
        registerNameContainer?.RemoveFromClassList("input-focused");

    private void OnRegisterEmailFocusIn(FocusInEvent evt) =>
        registerEmailContainer?.AddToClassList("input-focused");

    private void OnRegisterEmailFocusOut(FocusOutEvent evt) =>
        registerEmailContainer?.RemoveFromClassList("input-focused");

    private void OnRegisterPasswordFocusIn(FocusInEvent evt) =>
        registerPasswordContainer?.AddToClassList("input-focused");

    private void OnRegisterPasswordFocusOut(FocusOutEvent evt) =>
        registerPasswordContainer?.RemoveFromClassList("input-focused");
}
