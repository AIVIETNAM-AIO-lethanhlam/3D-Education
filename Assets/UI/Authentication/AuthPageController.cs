using System;
using System.Collections;
using System.Net.Mail;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class AuthPageController : MonoBehaviour
{
    private const string MainHomeSceneName = "MainHomeScene";

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

    private Label loginMessageLabel;
    private Label registerMessageLabel;

    private string loginRole = "student";
    private string registerRole = "student";

    private bool loginPasswordVisible;
    private bool registerPasswordVisible;
    private bool rememberLogin;
    private bool isSigningIn;
    private bool isRegistering;

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

        loginMessageLabel = root.Q<Label>("login-message-label");
        registerMessageLabel = root.Q<Label>("register-message-label");

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
        if (loginTabButton != null) loginTabButton.clicked += ShowLoginTab;
        if (registerTabButton != null) registerTabButton.clicked += ShowRegisterTab;

        if (loginTeacherButton != null) loginTeacherButton.clicked += SelectLoginTeacher;
        if (loginStudentButton != null) loginStudentButton.clicked += SelectLoginStudent;
        if (registerTeacherButton != null) registerTeacherButton.clicked += SelectRegisterTeacher;
        if (registerStudentButton != null) registerStudentButton.clicked += SelectRegisterStudent;

        if (loginTogglePasswordButton != null) loginTogglePasswordButton.clicked += ToggleLoginPassword;
        if (registerTogglePasswordButton != null) registerTogglePasswordButton.clicked += ToggleRegisterPassword;

        if (rememberButton != null) rememberButton.clicked += ToggleRemember;
        if (forgotPasswordButton != null) forgotPasswordButton.clicked += OpenForgotPassword;
        if (signInButton != null) signInButton.clicked += SignIn;
        if (createAccountButton != null) createAccountButton.clicked += CreateAccount;

        if (loginGoogleButton != null) loginGoogleButton.clicked += LoginWithGoogle;
        if (registerGoogleButton != null) registerGoogleButton.clicked += RegisterWithGoogle;
        if (loginPrivacyButton != null) loginPrivacyButton.clicked += OpenPrivacy;
        if (registerPrivacyButton != null) registerPrivacyButton.clicked += OpenPrivacy;

        RegisterFocus(loginEmailField, OnLoginEmailFocusIn, OnLoginEmailFocusOut);
        RegisterFocus(loginPasswordField, OnLoginPasswordFocusIn, OnLoginPasswordFocusOut);
        RegisterFocus(registerNameField, OnRegisterNameFocusIn, OnRegisterNameFocusOut);
        RegisterFocus(registerEmailField, OnRegisterEmailFocusIn, OnRegisterEmailFocusOut);
        RegisterFocus(registerPasswordField, OnRegisterPasswordFocusIn, OnRegisterPasswordFocusOut);
    }

    private void UnregisterEvents()
    {
        if (loginTabButton != null) loginTabButton.clicked -= ShowLoginTab;
        if (registerTabButton != null) registerTabButton.clicked -= ShowRegisterTab;

        if (loginTeacherButton != null) loginTeacherButton.clicked -= SelectLoginTeacher;
        if (loginStudentButton != null) loginStudentButton.clicked -= SelectLoginStudent;
        if (registerTeacherButton != null) registerTeacherButton.clicked -= SelectRegisterTeacher;
        if (registerStudentButton != null) registerStudentButton.clicked -= SelectRegisterStudent;

        if (loginTogglePasswordButton != null) loginTogglePasswordButton.clicked -= ToggleLoginPassword;
        if (registerTogglePasswordButton != null) registerTogglePasswordButton.clicked -= ToggleRegisterPassword;

        if (rememberButton != null) rememberButton.clicked -= ToggleRemember;
        if (forgotPasswordButton != null) forgotPasswordButton.clicked -= OpenForgotPassword;
        if (signInButton != null) signInButton.clicked -= SignIn;
        if (createAccountButton != null) createAccountButton.clicked -= CreateAccount;

        if (loginGoogleButton != null) loginGoogleButton.clicked -= LoginWithGoogle;
        if (registerGoogleButton != null) registerGoogleButton.clicked -= RegisterWithGoogle;
        if (loginPrivacyButton != null) loginPrivacyButton.clicked -= OpenPrivacy;
        if (registerPrivacyButton != null) registerPrivacyButton.clicked -= OpenPrivacy;

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
        if (field == null) return;
        field.RegisterCallback(focusIn);
        field.RegisterCallback(focusOut);
    }

    private static void UnregisterFocus(
        TextField field,
        EventCallback<FocusInEvent> focusIn,
        EventCallback<FocusOutEvent> focusOut)
    {
        if (field == null) return;
        field.UnregisterCallback(focusIn);
        field.UnregisterCallback(focusOut);
    }

    private void ShowLoginTab()
    {
        loginForm?.RemoveFromClassList("form-hidden");
        registerForm?.AddToClassList("form-hidden");

        loginTabButton?.AddToClassList("tab-button-active");
        registerTabButton?.RemoveFromClassList("tab-button-active");

        ClearRegisterMessage();
    }

    private void ShowRegisterTab()
    {
        loginForm?.AddToClassList("form-hidden");
        registerForm?.RemoveFromClassList("form-hidden");

        loginTabButton?.RemoveFromClassList("tab-button-active");
        registerTabButton?.AddToClassList("tab-button-active");

        ClearLoginMessage();
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
        UpdatePasswordVisibility(registerPasswordField, registerEyeIcon, registerPasswordVisible);
    }

    private static void UpdatePasswordVisibility(
        TextField field,
        VisualElement icon,
        bool visible)
    {
        if (field != null)
            field.isPasswordField = !visible;

        if (icon == null) return;

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
        rememberCheckbox?.EnableInClassList("remember-checkbox-checked", rememberLogin);
        rememberCheckmark?.EnableInClassList("remember-checkmark-hidden", !rememberLogin);
    }

    private void SignIn()
    {
        if (isSigningIn)
            return;

        string email = NormalizeEmail(loginEmailField?.value);
        string password = loginPasswordField?.value ?? string.Empty;

        ClearLoginMessage();

        if (!IsValidEmail(email))
        {
            ShowLoginMessage("Email đăng nhập không hợp lệ.", AuthMessageType.Error);
            loginEmailField?.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowLoginMessage("Vui lòng nhập mật khẩu.", AuthMessageType.Error);
            loginPasswordField?.Focus();
            return;
        }

        StartCoroutine(SignInCoroutine(email, password, loginRole));
    }

    private IEnumerator SignInCoroutine(
        string email,
        string password,
        string selectedRole)
    {
        SetLoginLoading(true);

        SupabaseAuthResponse signInResponse = null;
        string signInError = null;

        yield return SupabaseAuthService.SignIn(
            email,
            password,
            response => signInResponse = response,
            error => signInError = error
        );

        SetLoginLoading(false);

        if (!string.IsNullOrWhiteSpace(signInError))
        {
            Debug.LogError($"Supabase sign-in failed: {signInError}");

            ShowLoginMessage(
                TranslateSignInError(signInError),
                AuthMessageType.Error
            );

            yield break;
        }

        if (signInResponse == null ||
            signInResponse.user == null ||
            string.IsNullOrWhiteSpace(signInResponse.access_token))
        {
            ShowLoginMessage(
                "Không nhận được phiên đăng nhập từ Supabase.",
                AuthMessageType.Error
            );

            yield break;
        }

        string actualRole = selectedRole;

        string metadataRole =
            signInResponse.user.user_metadata?.role;

        if (!string.IsNullOrWhiteSpace(metadataRole))
        {
            actualRole =
                metadataRole.Trim().ToLowerInvariant();
        }

        if (actualRole != selectedRole)
        {
            ShowLoginMessage(
                $"Tài khoản này có role '{actualRole}', không phải '{selectedRole}'.",
                AuthMessageType.Error
            );

            yield break;
        }

        SupabaseSession.SaveAuthResponse(
            signInResponse,
            actualRole
        );

        /*
         * Giữ các key cũ để những scene chưa refactor vẫn hoạt động.
         * Khi toàn bộ project dùng SupabaseSession, có thể xóa khối này.
         */
        PlayerPrefs.SetString(
            "current_user_id",
            SupabaseSession.UserId
        );

        PlayerPrefs.SetString(
            "current_email",
            SupabaseSession.Email
        );

        PlayerPrefs.SetString(
            "current_full_name",
            SupabaseSession.FullName
        );

        PlayerPrefs.SetString(
            "current_role",
            SupabaseSession.Role
        );

        PlayerPrefs.SetString(
            "current_avatar_url",
            SupabaseSession.AvatarUrl
        );

        PlayerPrefs.SetInt(
            "remember_login",
            rememberLogin ? 1 : 0
        );

        PlayerPrefs.DeleteKey("current_password");
        PlayerPrefs.Save();

        Debug.Log(
            "Đăng nhập Supabase thành công\n" +
            $"User ID: {signInResponse.user.id}\n" +
            $"Email: {signInResponse.user.email}\n" +
            $"Role: {actualRole}"
        );

        if (!Application.CanStreamedLevelBeLoaded(MainHomeSceneName))
        {
            Debug.LogError(
                $"Không thể mở scene '{MainHomeSceneName}'. " +
                "Hãy thêm scene này vào File > Build Profiles > Scene List."
            );

            ShowLoginMessage(
                $"Scene {MainHomeSceneName} chưa được thêm vào Build Profiles.",
                AuthMessageType.Error
            );

            yield break;
        }

        SceneManager.LoadScene(MainHomeSceneName);
    }

    private void CreateAccount()
    {
        if (isRegistering)
            return;

        string fullName = NormalizePlainText(registerNameField?.value);
        string email = NormalizeEmail(registerEmailField?.value);
        string password = registerPasswordField?.value ?? string.Empty;

        ClearRegisterMessage();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            ShowRegisterMessage("Vui lòng nhập họ và tên.", AuthMessageType.Error);
            registerNameField?.Focus();
            return;
        }

        if (!IsValidEmail(email))
        {
            ShowRegisterMessage("Email không hợp lệ.", AuthMessageType.Error);
            registerEmailField?.Focus();
            return;
        }

        if (password.Length < 6)
        {
            ShowRegisterMessage(
                "Mật khẩu phải có ít nhất 6 ký tự.",
                AuthMessageType.Error
            );

            registerPasswordField?.Focus();
            return;
        }

        StartCoroutine(
            CreateAccountCoroutine(
                fullName,
                email,
                password,
                registerRole
            )
        );
    }

    private IEnumerator CreateAccountCoroutine(
        string fullName,
        string email,
        string password,
        string role)
    {
        SetRegisterLoading(true);

        SupabaseAuthResponse signUpResponse = null;
        string signUpError = null;

        yield return SupabaseAuthService.SignUp(
            fullName,
            email,
            password,
            role,
            response => signUpResponse = response,
            error => signUpError = error
        );

        SetRegisterLoading(false);

        if (!string.IsNullOrWhiteSpace(signUpError))
        {
            Debug.LogError($"Supabase sign-up failed: {signUpError}");

            ShowRegisterMessage(
                TranslateSignUpError(signUpError),
                AuthMessageType.Error
            );

            yield break;
        }

        if (signUpResponse?.user == null)
        {
            ShowRegisterMessage(
                "Không nhận được thông tin tài khoản từ Supabase.",
                AuthMessageType.Error
            );

            yield break;
        }

        Debug.Log(
            "Đăng ký thành công\n" +
            $"User ID: {signUpResponse.user.id}\n" +
            $"Email: {signUpResponse.user.email}\n" +
            $"Role: {role}"
        );

        // Sau đăng ký, không giữ session signup.
        SupabaseSession.Clear();


        if (loginEmailField != null)
            loginEmailField.value = email;

        if (loginPasswordField != null)
            loginPasswordField.value = string.Empty;

        if (role == "teacher")
            SelectLoginTeacher();
        else
            SelectLoginStudent();

        if (registerNameField != null) registerNameField.value = string.Empty;
        if (registerEmailField != null) registerEmailField.value = string.Empty;
        if (registerPasswordField != null) registerPasswordField.value = string.Empty;

        ShowLoginTab();

        ShowLoginMessage(
            "Đăng ký thành công. Vui lòng nhập mật khẩu để đăng nhập.",
            AuthMessageType.Success
        );

        loginPasswordField?.Focus();
    }

    private void SetLoginLoading(bool loading)
    {
        isSigningIn = loading;

        signInButton?.SetEnabled(!loading);
        loginTeacherButton?.SetEnabled(!loading);
        loginStudentButton?.SetEnabled(!loading);

        if (signInButton != null)
            signInButton.text = loading ? "Signing In..." : "Sign In";

        if (loading)
            ShowLoginMessage("Đang đăng nhập...", AuthMessageType.Loading);
    }

    private void SetRegisterLoading(bool loading)
    {
        isRegistering = loading;

        createAccountButton?.SetEnabled(!loading);
        registerTeacherButton?.SetEnabled(!loading);
        registerStudentButton?.SetEnabled(!loading);

        if (createAccountButton != null)
            createAccountButton.text =
                loading ? "Creating Account..." : "Create Account";

        if (loading)
            ShowRegisterMessage(
                "Đang tạo tài khoản...",
                AuthMessageType.Loading
            );
    }

    private void ShowLoginMessage(
        string message,
        AuthMessageType messageType)
    {
        ShowMessage(loginMessageLabel, message, messageType);
    }

    private void ShowRegisterMessage(
        string message,
        AuthMessageType messageType)
    {
        ShowMessage(registerMessageLabel, message, messageType);
    }

    private static void ShowMessage(
        Label label,
        string message,
        AuthMessageType messageType)
    {
        if (label == null)
        {
            if (messageType == AuthMessageType.Error)
                Debug.LogError(message);
            else
                Debug.Log(message);

            return;
        }

        label.text = message;

        RemoveMessageClasses(label);

        switch (messageType)
        {
            case AuthMessageType.Error:
                label.AddToClassList("auth-message--error");
                label.AddToClassList("register-message--error");
                break;

            case AuthMessageType.Success:
                label.AddToClassList("auth-message--success");
                label.AddToClassList("register-message--success");
                break;

            case AuthMessageType.Loading:
                label.AddToClassList("auth-message--loading");
                label.AddToClassList("register-message--loading");
                break;
        }
    }

    private void ClearLoginMessage()
    {
        ClearMessage(loginMessageLabel);
    }

    private void ClearRegisterMessage()
    {
        ClearMessage(registerMessageLabel);
    }

    private static void ClearMessage(Label label)
    {
        if (label == null) return;

        label.text = string.Empty;
        RemoveMessageClasses(label);
    }

    private static void RemoveMessageClasses(VisualElement element)
    {
        element.RemoveFromClassList("auth-message--error");
        element.RemoveFromClassList("auth-message--success");
        element.RemoveFromClassList("auth-message--loading");

        // Hỗ trợ USS cũ.
        element.RemoveFromClassList("register-message--error");
        element.RemoveFromClassList("register-message--success");
        element.RemoveFromClassList("register-message--loading");
    }

    private static string NormalizeEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Trim()
            .ToLowerInvariant()
            .Replace("\u200B", string.Empty)
            .Replace("\u200C", string.Empty)
            .Replace("\u200D", string.Empty)
            .Replace("\u2060", string.Empty)
            .Replace("\uFEFF", string.Empty);
    }

    private static string NormalizePlainText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Trim()
            .Replace("\u200B", string.Empty)
            .Replace("\u200C", string.Empty)
            .Replace("\u200D", string.Empty)
            .Replace("\u2060", string.Empty)
            .Replace("\uFEFF", string.Empty);
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            MailAddress address = new MailAddress(email);

            return string.Equals(
                address.Address,
                email,
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch
        {
            return false;
        }
    }

    private static string TranslateSignUpError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "Đăng ký thất bại.";

        string lowerError = error.ToLowerInvariant();

        if (lowerError.Contains("already registered") ||
            lowerError.Contains("already been registered") ||
            lowerError.Contains("user already exists"))
        {
            return "Email này đã được đăng ký.";
        }

        if (lowerError.Contains("invalid email") ||
            lowerError.Contains("email address"))
        {
            return "Email không hợp lệ.";
        }

        if (lowerError.Contains("password"))
            return "Mật khẩu không đáp ứng yêu cầu của hệ thống.";

        if (lowerError.Contains("rate limit") ||
            lowerError.Contains("too many requests"))
        {
            return "Bạn thao tác quá nhanh. Vui lòng thử lại sau.";
        }

        if (lowerError.Contains("network") ||
            lowerError.Contains("unable to resolve host") ||
            lowerError.Contains("cannot resolve destination host") ||
            lowerError.Contains("connection"))
        {
            return "Không thể kết nối đến máy chủ. Vui lòng kiểm tra Internet.";
        }

        return error;
    }

    private static string TranslateSignInError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "Đăng nhập thất bại.";

        string lowerError = error.ToLowerInvariant();

        if (lowerError.Contains("invalid login credentials") ||
            lowerError.Contains("invalid credentials"))
        {
            return "Email hoặc mật khẩu không chính xác.";
        }

        if (lowerError.Contains("email not confirmed"))
            return "Email chưa được xác nhận.";

        if (lowerError.Contains("rate limit") ||
            lowerError.Contains("too many requests"))
        {
            return "Bạn thao tác quá nhanh. Vui lòng thử lại sau.";
        }

        if (lowerError.Contains("network") ||
            lowerError.Contains("connection"))
        {
            return "Không thể kết nối đến máy chủ.";
        }

        return error;
    }

    private void OpenForgotPassword() =>
        Debug.Log("Mở trang quên mật khẩu.");

    private void LoginWithGoogle() =>
        Debug.Log("Đăng nhập bằng Google.");

    private void RegisterWithGoogle() =>
        Debug.Log($"Đăng ký bằng Google với role: {registerRole}");

    private void OpenPrivacy() =>
        Debug.Log("Mở Privacy Policy & Terms.");

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

    private enum AuthMessageType
    {
        Error,
        Success,
        Loading
    }
}