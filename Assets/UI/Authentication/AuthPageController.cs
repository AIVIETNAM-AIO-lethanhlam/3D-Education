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

    private VisualElement root;

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

    private PasswordRecoveryFlow passwordRecoveryFlow;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
        {
            Debug.LogError("Không tìm thấy UIDocument.");
            return;
        }

        root = document.rootVisualElement;

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

        passwordRecoveryFlow = new PasswordRecoveryFlow(
            this,
            root,
            ReturnFromPasswordRecovery);

        AppLanguageManager.LanguageChanged += OnLanguageChanged;

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

        ApplyCurrentLanguage();
    }

    private void OnDisable()
    {
        AppLanguageManager.LanguageChanged -= OnLanguageChanged;

        passwordRecoveryFlow?.Dispose();
        passwordRecoveryFlow = null;
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

    private void OnLanguageChanged(string language)
    {
        ApplyCurrentLanguage();
    }

    private void ApplyCurrentLanguage()
    {
        if (root == null)
            return;

        LocalizeElementTree(root);

        if (loginEmailField != null)
            loginEmailField.textEdition.placeholder =
                T("Email address", "Địa chỉ email");

        if (loginPasswordField != null)
            loginPasswordField.textEdition.placeholder =
                T("Password", "Mật khẩu");

        if (registerNameField != null)
            registerNameField.textEdition.placeholder =
                T("Full name", "Họ và tên");

        if (registerEmailField != null)
            registerEmailField.textEdition.placeholder =
                T("Email address", "Địa chỉ email");

        if (registerPasswordField != null)
            registerPasswordField.textEdition.placeholder =
                T("Password", "Mật khẩu");

        passwordRecoveryFlow?.ApplyLanguage();

        if (!isSigningIn && signInButton != null)
            signInButton.text = T("Sign In", "Đăng nhập");

        if (!isRegistering && createAccountButton != null)
            createAccountButton.text = T("Create Account", "Tạo tài khoản");
    }

    private void LocalizeElementTree(VisualElement element)
    {
        if (element == null)
            return;

        if (element is Label label)
            label.text = TranslateKnownUiText(label.text);

        if (element is Button button &&
            !string.IsNullOrWhiteSpace(button.text))
        {
            button.text = TranslateKnownUiText(button.text);
        }

        foreach (VisualElement child in element.Children())
            LocalizeElementTree(child);
    }

    private static string TranslateKnownUiText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        bool vi = AppLanguageManager.IsVietnamese;

        switch (text.Trim())
        {
            case "Login":
            case "Đăng nhập":
                return vi ? "Đăng nhập" : "Login";
            case "Register":
            case "Đăng ký":
                return vi ? "Đăng ký" : "Register";
            case "I AM A":
            case "TÔI LÀ":
                return vi ? "TÔI LÀ" : "I AM A";
            case "Teacher":
            case "Giáo viên":
                return vi ? "Giáo viên" : "Teacher";
            case "Student":
            case "Học sinh":
                return vi ? "Học sinh" : "Student";
            case "Remember me":
            case "Ghi nhớ đăng nhập":
                return vi ? "Ghi nhớ đăng nhập" : "Remember me";
            case "Forgot password?":
            case "Quên mật khẩu?":
                return vi ? "Quên mật khẩu?" : "Forgot password?";
            case "Sign In":
                return vi ? "Đăng nhập" : "Sign In";
            case "or continue with":
            case "hoặc tiếp tục với":
                return vi ? "hoặc tiếp tục với" : "or continue with";
            case "Continue with Google":
            case "Tiếp tục với Google":
                return vi ? "Tiếp tục với Google" : "Continue with Google";
            case "Privacy Policy & Terms":
            case "Chính sách quyền riêng tư & Điều khoản":
                return vi
                    ? "Chính sách quyền riêng tư & Điều khoản"
                    : "Privacy Policy & Terms";
            case "Create Account":
            case "Tạo tài khoản":
                return vi ? "Tạo tài khoản" : "Create Account";
            case "Forgot your password?":
                return vi ? "Quên mật khẩu?" : "Forgot your password?";
            case "EMAIL ADDRESS":
            case "ĐỊA CHỈ EMAIL":
                return vi ? "ĐỊA CHỈ EMAIL" : "EMAIL ADDRESS";
            case "Send verification code":
            case "Gửi mã xác minh":
                return vi ? "Gửi mã xác minh" : "Send verification code";
            case "Remember your password?":
            case "Bạn nhớ mật khẩu rồi?":
                return vi ? "Bạn nhớ mật khẩu rồi?" : "Remember your password?";
            case "Check your email":
            case "Kiểm tra email":
                return vi ? "Kiểm tra email" : "Check your email";
            case "Verify code":
            case "Xác minh mã":
                return vi ? "Xác minh mã" : "Verify code";
            case "Didn't receive the code?":
            case "Chưa nhận được mã?":
                return vi ? "Chưa nhận được mã?" : "Didn't receive the code?";
            case "Use a different email":
            case "Dùng email khác":
                return vi ? "Dùng email khác" : "Use a different email";
            case "Create a new password":
            case "Tạo mật khẩu mới":
                return vi ? "Tạo mật khẩu mới" : "Create a new password";
            case "NEW PASSWORD":
            case "MẬT KHẨU MỚI":
                return vi ? "MẬT KHẨU MỚI" : "NEW PASSWORD";
            case "8+ characters":
            case "Từ 8 ký tự":
                return vi ? "Từ 8 ký tự" : "8+ characters";
            case "Uppercase letter":
            case "Chữ hoa":
                return vi ? "Chữ hoa" : "Uppercase letter";
            case "Lowercase letter":
            case "Chữ thường":
                return vi ? "Chữ thường" : "Lowercase letter";
            case "Number":
            case "Chữ số":
                return vi ? "Chữ số" : "Number";
            case "Special character":
            case "Ký tự đặc biệt":
                return vi ? "Ký tự đặc biệt" : "Special character";
            case "CONFIRM NEW PASSWORD":
            case "XÁC NHẬN MẬT KHẨU MỚI":
                return vi ? "XÁC NHẬN MẬT KHẨU MỚI" : "CONFIRM NEW PASSWORD";
            case "Reset password":
            case "Đặt lại mật khẩu":
                return vi ? "Đặt lại mật khẩu" : "Reset password";
            case "Password reset successfully":
            case "Đặt lại mật khẩu thành công":
                return vi
                    ? "Đặt lại mật khẩu thành công"
                    : "Password reset successfully";
            case "Back to Sign In":
            case "Quay lại đăng nhập":
                return vi ? "Quay lại đăng nhập" : "Back to Sign In";
        }

        if (text == "Enter the email associated with your account and we'll send you a verification code to reset your password." ||
            text == "Nhập email liên kết với tài khoản. Chúng tôi sẽ gửi mã xác minh để đặt lại mật khẩu.")
        {
            return vi
                ? "Nhập email liên kết với tài khoản. Chúng tôi sẽ gửi mã xác minh để đặt lại mật khẩu."
                : "Enter the email associated with your account and we'll send you a verification code to reset your password.";
        }

        if (text == "We sent a 6-digit verification code to" ||
            text == "Chúng tôi đã gửi mã xác minh 6 chữ số đến")
        {
            return vi
                ? "Chúng tôi đã gửi mã xác minh 6 chữ số đến"
                : "We sent a 6-digit verification code to";
        }

        if (text == "Choose a strong password that you haven't used before." ||
            text == "Hãy chọn mật khẩu mạnh mà bạn chưa từng sử dụng trước đây.")
        {
            return vi
                ? "Hãy chọn mật khẩu mạnh mà bạn chưa từng sử dụng trước đây."
                : "Choose a strong password that you haven't used before.";
        }

        if (text == "Your password has been updated. You can now sign in using your new password." ||
            text == "Mật khẩu đã được cập nhật. Bạn có thể đăng nhập bằng mật khẩu mới.")
        {
            return vi
                ? "Mật khẩu đã được cập nhật. Bạn có thể đăng nhập bằng mật khẩu mới."
                : "Your password has been updated. You can now sign in using your new password.";
        }

        if (text == "For your security, other active sessions may need to sign in again." ||
            text == "Để bảo mật, các phiên đang hoạt động khác có thể cần đăng nhập lại.")
        {
            return vi
                ? "Để bảo mật, các phiên đang hoạt động khác có thể cần đăng nhập lại."
                : "For your security, other active sessions may need to sign in again.";
        }

        return text;
    }

    private static string T(string english, string vietnamese)
    {
        return AppLanguageManager.IsVietnamese
            ? vietnamese
            : english;
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
            ShowLoginMessage(T("Invalid login email.", "Email đăng nhập không hợp lệ."), AuthMessageType.Error);
            loginEmailField?.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowLoginMessage(T("Please enter your password.", "Vui lòng nhập mật khẩu."), AuthMessageType.Error);
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
                T("No login session was returned by Supabase.", "Không nhận được phiên đăng nhập từ Supabase."),
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
                T(
                    $"This account has role '{actualRole}', not '{selectedRole}'.",
                    $"Tài khoản này có role '{actualRole}', không phải '{selectedRole}'."),
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
                T(
                    $"Scene {MainHomeSceneName} has not been added to Build Profiles.",
                    $"Scene {MainHomeSceneName} chưa được thêm vào Build Profiles."),
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
            ShowRegisterMessage(T("Please enter your full name.", "Vui lòng nhập họ và tên."), AuthMessageType.Error);
            registerNameField?.Focus();
            return;
        }

        if (!IsValidEmail(email))
        {
            ShowRegisterMessage(T("Invalid email.", "Email không hợp lệ."), AuthMessageType.Error);
            registerEmailField?.Focus();
            return;
        }

        if (password.Length < 6)
        {
            ShowRegisterMessage(
                T("Password must contain at least 6 characters.", "Mật khẩu phải có ít nhất 6 ký tự."),
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
                T("No account information was returned by Supabase.", "Không nhận được thông tin tài khoản từ Supabase."),
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
            T(
                "Account created successfully. Please enter your password to sign in.",
                "Đăng ký thành công. Vui lòng nhập mật khẩu để đăng nhập."),
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
            signInButton.text = loading
                ? T("Signing In...", "Đang đăng nhập...")
                : T("Sign In", "Đăng nhập");

        if (loading)
            ShowLoginMessage(
                T("Signing in...", "Đang đăng nhập..."),
                AuthMessageType.Loading);
    }

    private void SetRegisterLoading(bool loading)
    {
        isRegistering = loading;

        createAccountButton?.SetEnabled(!loading);
        registerTeacherButton?.SetEnabled(!loading);
        registerStudentButton?.SetEnabled(!loading);

        if (createAccountButton != null)
            createAccountButton.text =
                loading
                    ? T("Creating Account...", T("Creating account...", "Đang tạo tài khoản..."))
                    : T("Create Account", "Tạo tài khoản");

        if (loading)
            ShowRegisterMessage(
                T("Creating account...", "Đang tạo tài khoản..."),
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
            return T("Sign up failed.", "Đăng ký thất bại.");

        string lowerError = error.ToLowerInvariant();

        if (lowerError.Contains("already registered") ||
            lowerError.Contains("already been registered") ||
            lowerError.Contains("user already exists"))
        {
            return T("This email is already registered.", "Email này đã được đăng ký.");
        }

        if (lowerError.Contains("invalid email") ||
            lowerError.Contains("email address"))
        {
            return T("Invalid email.", "Email không hợp lệ.");
        }

        if (lowerError.Contains("password"))
            return T("The password does not meet the system requirements.", "Mật khẩu không đáp ứng yêu cầu của hệ thống.");

        if (lowerError.Contains("rate limit") ||
            lowerError.Contains("too many requests"))
        {
            return T("Too many attempts. Please try again later.", "Bạn thao tác quá nhanh. Vui lòng thử lại sau.");
        }

        if (lowerError.Contains("network") ||
            lowerError.Contains("unable to resolve host") ||
            lowerError.Contains("cannot resolve destination host") ||
            lowerError.Contains("connection"))
        {
            return T("Unable to connect to the server. Please check your internet connection.", "Không thể kết nối đến máy chủ. Vui lòng kiểm tra Internet.");
        }

        return error;
    }

    private static string TranslateSignInError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return T("Sign in failed.", "Đăng nhập thất bại.");

        string lowerError = error.ToLowerInvariant();

        if (lowerError.Contains("invalid login credentials") ||
            lowerError.Contains("invalid credentials"))
        {
            return T("Incorrect email or password.", "Email hoặc mật khẩu không chính xác.");
        }

        if (lowerError.Contains("email not confirmed"))
            return T("Email has not been confirmed.", "Email chưa được xác nhận.");

        if (lowerError.Contains("rate limit") ||
            lowerError.Contains("too many requests"))
        {
            return T("Too many attempts. Please try again later.", "Bạn thao tác quá nhanh. Vui lòng thử lại sau.");
        }

        if (lowerError.Contains("network") ||
            lowerError.Contains("connection"))
        {
            return T("Unable to connect to the server.", "Không thể kết nối đến máy chủ.");
        }

        return error;
    }

    private void OpenForgotPassword()
    {
        passwordRecoveryFlow?.Open(loginEmailField?.value ?? string.Empty);
    }

    private void ReturnFromPasswordRecovery(string recoveredEmail)
    {
        if (loginEmailField != null && !string.IsNullOrWhiteSpace(recoveredEmail))
            loginEmailField.value = recoveredEmail;

        if (loginPasswordField != null)
            loginPasswordField.value = string.Empty;

        ShowLoginTab();
        ClearLoginMessage();
        loginPasswordField?.Focus();
    }

    private void LoginWithGoogle()
    {
        if (isSigningIn || isRegistering)
            return;

        ClearLoginMessage();

        bool opened = SupabaseAuthService.OpenGoogleOAuth(
            "login",
            loginRole,
            error =>
            {
                Debug.LogError($"Không thể mở Google OAuth: {error}");
                ShowLoginMessage(
                    string.IsNullOrWhiteSpace(error)
                        ? T("Unable to open Google sign-in.", "Không thể mở đăng nhập Google.")
                        : error,
                    AuthMessageType.Error
                );
            }
        );

        if (opened)
        {
            ShowLoginMessage(
                T("Opening Google sign-in...", "Đang mở Google để đăng nhập..."),
                AuthMessageType.Loading
            );
        }
    }

    private void RegisterWithGoogle()
    {
        if (isSigningIn || isRegistering)
            return;

        ClearRegisterMessage();

        bool opened = SupabaseAuthService.OpenGoogleOAuth(
            "register",
            registerRole,
            error =>
            {
                Debug.LogError($"Không thể mở Google OAuth: {error}");
                ShowRegisterMessage(
                    string.IsNullOrWhiteSpace(error)
                        ? T("Unable to open Google registration.", "Không thể mở đăng ký bằng Google.")
                        : error,
                    AuthMessageType.Error
                );
            }
        );

        if (opened)
        {
            ShowRegisterMessage(
                T(
                    $"Opening Google registration with role '{registerRole}'...",
                    $"Đang mở Google để đăng ký với role '{registerRole}'..."),
                AuthMessageType.Loading
            );
        }
    }

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