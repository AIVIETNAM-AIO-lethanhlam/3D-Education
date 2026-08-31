using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Owns the entire password-recovery UI flow inside AuthPage.uxml:
/// Forgot Password -> 6-digit OTP -> New Password -> Success.
///
/// This class deliberately keeps the recovery access token in memory only.
/// A successful recovery never becomes a normal app login session.
/// </summary>
public sealed class PasswordRecoveryFlow : IDisposable
{
    private const int OtpLength = 6;
    private const int ResendCooldownSeconds = 60;
    private const int VerifyLockSeconds = 30;
    private const int MaxFailedVerifyAttempts = 5;
    private const float DefaultBottomSpaceHeight = 36f;
    private const float KeyboardSafetyPadding = 24f;
    private const float KeyboardFallbackHeightRatio = 0.42f;
    private const float KeyboardExtraScrollSpaceRatio = 0.34f;
    private const int KeyboardHiddenTicksBeforeRestore = 5;

    private enum Step
    {
        None,
        ForgotPassword,
        VerifyCode,
        ResetPassword,
        Success
    }

    private enum MessageType
    {
        Error,
        Success,
        Info
    }

    private readonly MonoBehaviour host;
    private readonly VisualElement root;
    private readonly Action<string> returnToLogin;
    private readonly ScrollView authScroll;
    private readonly VisualElement pageBottomSpace;

    private readonly Button backButton;
    private readonly VisualElement authLoginForm;
    private readonly VisualElement authRegisterForm;

    private readonly VisualElement forgotForm;
    private readonly VisualElement forgotEmailContainer;
    private readonly TextField forgotEmailField;
    private readonly Label forgotMessageLabel;
    private readonly Button sendCodeButton;
    private readonly Button forgotSignInButton;

    private readonly VisualElement verifyForm;
    private readonly Label maskedEmailLabel;
    private readonly Label verifyMessageLabel;
    private readonly Button verifyCodeButton;
    private readonly Button resendCodeButton;
    private readonly Button differentEmailButton;
    private readonly TextField[] otpFields = new TextField[OtpLength];
    private readonly EventCallback<ChangeEvent<string>>[] otpChangeCallbacks = new EventCallback<ChangeEvent<string>>[OtpLength];
    private readonly EventCallback<KeyDownEvent>[] otpKeyCallbacks = new EventCallback<KeyDownEvent>[OtpLength];

    private readonly VisualElement resetForm;
    private readonly VisualElement newPasswordContainer;
    private readonly VisualElement confirmPasswordContainer;
    private readonly TextField newPasswordField;
    private readonly TextField confirmPasswordField;
    private readonly Button toggleNewPasswordButton;
    private readonly Button toggleConfirmPasswordButton;
    private readonly VisualElement newPasswordEyeIcon;
    private readonly VisualElement confirmPasswordEyeIcon;
    private readonly Label resetMessageLabel;
    private readonly Label passwordMatchLabel;
    private readonly Button resetPasswordButton;
    private readonly VisualElement ruleLength;
    private readonly VisualElement ruleUppercase;
    private readonly VisualElement ruleLowercase;
    private readonly VisualElement ruleNumber;
    private readonly VisualElement ruleSpecial;

    private readonly VisualElement successForm;
    private readonly Button successBackToLoginButton;

    private readonly IVisualElementScheduledItem timer;
    private readonly IVisualElementScheduledItem keyboardTimer;

    private Step currentStep = Step.None;
    private string recoveryEmail = string.Empty;
    private string recoveryAccessToken = string.Empty;

    private bool isSendingCode;
    private bool isVerifyingCode;
    private bool isResettingPassword;
    private bool isUpdatingOtpFields;
    private bool newPasswordVisible;
    private bool confirmPasswordVisible;
    private bool keyboardWasVisible;
    private bool savedPreKeyboardScrollOffset;
    private VisualElement keyboardAvoidTarget;
    private Vector2 preKeyboardScrollOffset;
    private float keyboardBaselineRootHeight;
    private int keyboardHiddenTicks;

    private int resendSecondsRemaining;
    private int verifyLockSecondsRemaining;
    private int failedVerifyAttempts;

    public PasswordRecoveryFlow(
        MonoBehaviour host,
        VisualElement root,
        Action<string> returnToLogin)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.root = root ?? throw new ArgumentNullException(nameof(root));
        this.returnToLogin = returnToLogin;

        authScroll = root.Q<ScrollView>("auth-scroll");
        pageBottomSpace = root.Q<VisualElement>(className: "page-bottom-space");

        backButton = root.Q<Button>("recovery-back-button");
        authLoginForm = root.Q<VisualElement>("login-form");
        authRegisterForm = root.Q<VisualElement>("register-form");

        forgotForm = root.Q<VisualElement>("forgot-password-form");
        forgotEmailContainer = root.Q<VisualElement>("forgot-email-container");
        forgotEmailField = root.Q<TextField>("forgot-email-field");
        forgotMessageLabel = root.Q<Label>("forgot-message-label");
        sendCodeButton = root.Q<Button>("send-code-button");
        forgotSignInButton = root.Q<Button>("forgot-sign-in-button");

        verifyForm = root.Q<VisualElement>("verify-code-form");
        maskedEmailLabel = root.Q<Label>("masked-email-label");
        verifyMessageLabel = root.Q<Label>("verify-message-label");
        verifyCodeButton = root.Q<Button>("verify-code-button");
        resendCodeButton = root.Q<Button>("resend-code-button");
        differentEmailButton = root.Q<Button>("different-email-button");

        for (int i = 0; i < OtpLength; i++)
            otpFields[i] = root.Q<TextField>($"otp-field-{i}");

        resetForm = root.Q<VisualElement>("reset-password-form");
        newPasswordContainer = root.Q<VisualElement>("new-password-container");
        confirmPasswordContainer = root.Q<VisualElement>("confirm-password-container");
        newPasswordField = root.Q<TextField>("new-password-field");
        confirmPasswordField = root.Q<TextField>("confirm-password-field");
        toggleNewPasswordButton = root.Q<Button>("toggle-new-password-button");
        toggleConfirmPasswordButton = root.Q<Button>("toggle-confirm-password-button");
        newPasswordEyeIcon = root.Q<VisualElement>("new-password-eye-icon");
        confirmPasswordEyeIcon = root.Q<VisualElement>("confirm-password-eye-icon");
        resetMessageLabel = root.Q<Label>("reset-message-label");
        passwordMatchLabel = root.Q<Label>("password-match-label");
        resetPasswordButton = root.Q<Button>("reset-password-button");
        ruleLength = root.Q<VisualElement>("password-rule-length");
        ruleUppercase = root.Q<VisualElement>("password-rule-uppercase");
        ruleLowercase = root.Q<VisualElement>("password-rule-lowercase");
        ruleNumber = root.Q<VisualElement>("password-rule-number");
        ruleSpecial = root.Q<VisualElement>("password-rule-special");

        successForm = root.Q<VisualElement>("reset-success-form");
        successBackToLoginButton = root.Q<Button>("success-back-to-login-button");

        ConfigureRecoveryPlaceholders();
        RegisterEvents();
        SetStep(Step.None);

        timer = root.schedule.Execute(TickTimers).Every(1000);

        // Keep the focused recovery area above the native keyboard.
        // On some Android keyboards TouchScreenKeyboard.area becomes valid late, so the
        // routine also has a geometry/fallback path and does not depend on area alone.
        keyboardTimer = root.schedule.Execute(UpdateKeyboardAvoidance).Every(50);
        keyboardTimer.Pause();

        root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
    }

    public void Open(string suggestedEmail)
    {
        if (!HasRequiredUi())
        {
            Debug.LogError(
                "Password recovery UI is incomplete. Ensure AuthPage.uxml contains all recovery elements.");
            return;
        }

        ClearRecoveryState();

        string normalizedEmail = NormalizeEmail(suggestedEmail);
        if (forgotEmailField != null)
            forgotEmailField.value = normalizedEmail;

        authLoginForm?.AddToClassList("form-hidden");
        authRegisterForm?.AddToClassList("form-hidden");
        root.AddToClassList("recovery-active");
        SetStep(Step.ForgotPassword);
        forgotEmailField?.Focus();
    }

    public void Dispose()
    {
        timer?.Pause();
        keyboardTimer?.Pause();
        RestoreKeyboardLayout(true);
        root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        UnregisterEvents();
    }

    private void ConfigureRecoveryPlaceholders()
    {
        // Keep placeholder copy visible (and faint via USS) until the user actually types.
        if (newPasswordField != null)
        {
            newPasswordField.textEdition.placeholder = "Enter new password";
            newPasswordField.textEdition.hidePlaceholderOnFocus = false;
        }

        if (confirmPasswordField != null)
        {
            confirmPasswordField.textEdition.placeholder = "Re-enter new password";
            confirmPasswordField.textEdition.hidePlaceholderOnFocus = false;
        }
    }

    private bool HasRequiredUi()
    {
        return forgotForm != null &&
               verifyForm != null &&
               resetForm != null &&
               successForm != null &&
               sendCodeButton != null &&
               verifyCodeButton != null &&
               resetPasswordButton != null &&
               otpFields.All(field => field != null);
    }

    private void RegisterEvents()
    {
        if (backButton != null) backButton.clicked += HandleBack;
        if (sendCodeButton != null) sendCodeButton.clicked += SendRecoveryCode;
        if (forgotSignInButton != null) forgotSignInButton.clicked += ReturnToLogin;
        if (verifyCodeButton != null) verifyCodeButton.clicked += VerifyCode;
        if (resendCodeButton != null) resendCodeButton.clicked += ResendCode;
        if (differentEmailButton != null) differentEmailButton.clicked += UseDifferentEmail;
        if (toggleNewPasswordButton != null) toggleNewPasswordButton.clicked += ToggleNewPassword;
        if (toggleConfirmPasswordButton != null) toggleConfirmPasswordButton.clicked += ToggleConfirmPassword;
        if (resetPasswordButton != null) resetPasswordButton.clicked += ResetPassword;
        if (successBackToLoginButton != null) successBackToLoginButton.clicked += ReturnToLogin;

        forgotEmailField?.RegisterCallback<FocusInEvent>(OnForgotEmailFocusIn);
        forgotEmailField?.RegisterCallback<FocusOutEvent>(OnForgotEmailFocusOut);

        newPasswordField?.RegisterCallback<FocusInEvent>(OnNewPasswordFocusIn);
        newPasswordField?.RegisterCallback<FocusOutEvent>(OnNewPasswordFocusOut);
        confirmPasswordField?.RegisterCallback<FocusInEvent>(OnConfirmPasswordFocusIn);
        confirmPasswordField?.RegisterCallback<FocusOutEvent>(OnConfirmPasswordFocusOut);

        newPasswordField?.RegisterValueChangedCallback(OnNewPasswordChanged);
        confirmPasswordField?.RegisterValueChangedCallback(OnConfirmPasswordChanged);

        for (int i = 0; i < OtpLength; i++)
        {
            int index = i;

            otpChangeCallbacks[i] = evt => OnOtpChanged(index, evt);
            otpKeyCallbacks[i] = evt => OnOtpKeyDown(index, evt);

            otpFields[i]?.RegisterValueChangedCallback(otpChangeCallbacks[i]);
            otpFields[i]?.RegisterCallback(otpKeyCallbacks[i]);
        }
    }

    private void UnregisterEvents()
    {
        if (backButton != null) backButton.clicked -= HandleBack;
        if (sendCodeButton != null) sendCodeButton.clicked -= SendRecoveryCode;
        if (forgotSignInButton != null) forgotSignInButton.clicked -= ReturnToLogin;
        if (verifyCodeButton != null) verifyCodeButton.clicked -= VerifyCode;
        if (resendCodeButton != null) resendCodeButton.clicked -= ResendCode;
        if (differentEmailButton != null) differentEmailButton.clicked -= UseDifferentEmail;
        if (toggleNewPasswordButton != null) toggleNewPasswordButton.clicked -= ToggleNewPassword;
        if (toggleConfirmPasswordButton != null) toggleConfirmPasswordButton.clicked -= ToggleConfirmPassword;
        if (resetPasswordButton != null) resetPasswordButton.clicked -= ResetPassword;
        if (successBackToLoginButton != null) successBackToLoginButton.clicked -= ReturnToLogin;

        forgotEmailField?.UnregisterCallback<FocusInEvent>(OnForgotEmailFocusIn);
        forgotEmailField?.UnregisterCallback<FocusOutEvent>(OnForgotEmailFocusOut);

        newPasswordField?.UnregisterCallback<FocusInEvent>(OnNewPasswordFocusIn);
        newPasswordField?.UnregisterCallback<FocusOutEvent>(OnNewPasswordFocusOut);
        confirmPasswordField?.UnregisterCallback<FocusInEvent>(OnConfirmPasswordFocusIn);
        confirmPasswordField?.UnregisterCallback<FocusOutEvent>(OnConfirmPasswordFocusOut);

        newPasswordField?.UnregisterValueChangedCallback(OnNewPasswordChanged);
        confirmPasswordField?.UnregisterValueChangedCallback(OnConfirmPasswordChanged);

        for (int i = 0; i < OtpLength; i++)
        {
            if (otpFields[i] == null) continue;

            if (otpChangeCallbacks[i] != null)
                otpFields[i].UnregisterValueChangedCallback(otpChangeCallbacks[i]);

            if (otpKeyCallbacks[i] != null)
                otpFields[i].UnregisterCallback(otpKeyCallbacks[i]);
        }
    }

    private void HandleBack()
    {
        switch (currentStep)
        {
            case Step.ForgotPassword:
                ReturnToLogin();
                break;

            case Step.VerifyCode:
                SetStep(Step.ForgotPassword);
                forgotEmailField?.Focus();
                break;

            case Step.ResetPassword:
                // A recovery OTP is single-use. Going back starts a fresh recovery
                // request instead of pretending the already-consumed OTP can be reused.
                recoveryAccessToken = string.Empty;
                resendSecondsRemaining = 0;
                ClearOtpFields();
                SetStep(Step.ForgotPassword);
                ShowMessage(
                    forgotMessageLabel,
                    "Request a new code to restart password recovery.",
                    MessageType.Info);
                forgotEmailField?.Focus();
                break;

            case Step.Success:
                ReturnToLogin();
                break;
        }
    }

    private void SendRecoveryCode()
    {
        if (isSendingCode) return;

        ClearMessage(forgotMessageLabel);
        SetInputState(forgotEmailContainer, false, false);

        string email = NormalizeEmail(forgotEmailField?.value);

        if (string.IsNullOrWhiteSpace(email))
        {
            ShowMessage(forgotMessageLabel, "Please enter your email address.", MessageType.Error);
            SetInputState(forgotEmailContainer, true, false);
            forgotEmailField?.Focus();
            return;
        }

        if (!IsValidEmail(email))
        {
            ShowMessage(forgotMessageLabel, "Enter a valid email address.", MessageType.Error);
            SetInputState(forgotEmailContainer, true, false);
            forgotEmailField?.Focus();
            return;
        }

        recoveryEmail = email;
        host.StartCoroutine(SendRecoveryCodeCoroutine(email, false));
    }

    private IEnumerator SendRecoveryCodeCoroutine(string email, bool isResend)
    {
        isSendingCode = true;

        if (isResend)
        {
            resendCodeButton?.SetEnabled(false);
        }
        else
        {
            SetButtonLoading(sendCodeButton, true, "Sending code...");
            forgotEmailField?.SetEnabled(false);
        }

        string error = null;
        bool success = false;

        yield return SupabaseAuthService.SendPasswordRecoveryCode(
            email,
            () => success = true,
            message => error = message);

        isSendingCode = false;

        if (isResend)
        {
            resendCodeButton?.SetEnabled(true);
        }
        else
        {
            SetButtonLoading(sendCodeButton, false, "Send verification code");
            forgotEmailField?.SetEnabled(true);
        }

        if (!success || !string.IsNullOrWhiteSpace(error))
        {
            string friendly = TranslateRecoverySendError(error);

            if (isResend)
                ShowMessage(verifyMessageLabel, friendly, MessageType.Error);
            else
                ShowMessage(forgotMessageLabel, friendly, MessageType.Error);

            yield break;
        }

        recoveryEmail = email;
        failedVerifyAttempts = 0;
        verifyLockSecondsRemaining = 0;

        if (maskedEmailLabel != null)
            maskedEmailLabel.text = MaskEmail(email);

        ClearOtpFields();
        StartResendCooldown();
        SetStep(Step.VerifyCode);

        ShowMessage(
            verifyMessageLabel,
            isResend
                ? "If an account exists for this email, a new verification code has been sent."
                : "If an account exists for this email, we've sent a verification code.",
            MessageType.Success);

        otpFields[0]?.Focus();
    }

    private void ResendCode()
    {
        if (isSendingCode || resendSecondsRemaining > 0 || string.IsNullOrWhiteSpace(recoveryEmail))
            return;

        host.StartCoroutine(SendRecoveryCodeCoroutine(recoveryEmail, true));
    }

    private void UseDifferentEmail()
    {
        recoveryAccessToken = string.Empty;
        ClearOtpFields();
        ClearMessage(verifyMessageLabel);
        SetStep(Step.ForgotPassword);
        forgotEmailField?.Focus();
    }

    private void VerifyCode()
    {
        if (isVerifyingCode || verifyLockSecondsRemaining > 0)
            return;

        string code = GetOtpCode();
        if (code.Length != OtpLength)
        {
            ShowMessage(verifyMessageLabel, "Enter the 6-digit verification code.", MessageType.Error);
            return;
        }

        host.StartCoroutine(VerifyCodeCoroutine(code));
    }

    private IEnumerator VerifyCodeCoroutine(string code)
    {
        isVerifyingCode = true;
        SetButtonLoading(verifyCodeButton, true, "Verifying...");
        SetOtpEnabled(false);
        ClearMessage(verifyMessageLabel);

        SupabaseAuthResponse response = null;
        string error = null;

        yield return SupabaseAuthService.VerifyRecoveryCode(
            recoveryEmail,
            code,
            value => response = value,
            message => error = message);

        isVerifyingCode = false;
        SetButtonLoading(verifyCodeButton, false, "Verify code");
        SetOtpEnabled(true);

        if (!string.IsNullOrWhiteSpace(error) ||
            response == null ||
            string.IsNullOrWhiteSpace(response.access_token))
        {
            failedVerifyAttempts++;

            if (failedVerifyAttempts >= MaxFailedVerifyAttempts)
            {
                verifyLockSecondsRemaining = VerifyLockSeconds;
                ShowMessage(
                    verifyMessageLabel,
                    "Too many attempts. Please wait before trying again.",
                    MessageType.Error);
            }
            else
            {
                ShowMessage(
                    verifyMessageLabel,
                    TranslateVerifyError(error),
                    MessageType.Error);
            }

            UpdateVerifyButtonState();
            otpFields[0]?.Focus();
            yield break;
        }

        recoveryAccessToken = response.access_token;
        failedVerifyAttempts = 0;
        verifyLockSecondsRemaining = 0;

        if (newPasswordField != null)
            newPasswordField.value = string.Empty;

        if (confirmPasswordField != null)
            confirmPasswordField.value = string.Empty;

        SetStep(Step.ResetPassword);
        UpdatePasswordValidation();
        newPasswordField?.Focus();
    }

    private void ResetPassword()
    {
        if (isResettingPassword)
            return;

        PasswordRules rules = EvaluatePassword(newPasswordField?.value ?? string.Empty);
        bool passwordsMatch = PasswordsMatch();

        if (!rules.AllValid || !passwordsMatch)
        {
            UpdatePasswordValidation();
            return;
        }

        host.StartCoroutine(ResetPasswordCoroutine(newPasswordField.value));
    }

    private IEnumerator ResetPasswordCoroutine(string newPassword)
    {
        isResettingPassword = true;
        SetButtonLoading(resetPasswordButton, true, "Resetting password...");
        SetResetInputsEnabled(false);
        ClearMessage(resetMessageLabel);

        string error = null;
        bool success = false;

        yield return SupabaseAuthService.UpdatePasswordWithAccessToken(
            newPassword,
            recoveryAccessToken,
            () => success = true,
            message => error = message);

        isResettingPassword = false;
        SetButtonLoading(resetPasswordButton, false, "Reset password");
        SetResetInputsEnabled(true);

        if (!success || !string.IsNullOrWhiteSpace(error))
        {
            ShowMessage(resetMessageLabel, TranslateResetError(error), MessageType.Error);
            UpdatePasswordValidation();
            yield break;
        }

        // Recovery is complete. Never persist the temporary recovery session.
        recoveryAccessToken = string.Empty;
        SupabaseSession.Clear();

        SetStep(Step.Success);
    }

    private void ToggleNewPassword()
    {
        newPasswordVisible = !newPasswordVisible;
        UpdatePasswordVisibility(newPasswordField, newPasswordEyeIcon, newPasswordVisible);
    }

    private void ToggleConfirmPassword()
    {
        confirmPasswordVisible = !confirmPasswordVisible;
        UpdatePasswordVisibility(confirmPasswordField, confirmPasswordEyeIcon, confirmPasswordVisible);
    }

    private static void UpdatePasswordVisibility(TextField field, VisualElement eyeIcon, bool visible)
    {
        if (field != null)
            field.isPasswordField = !visible;

        if (eyeIcon == null) return;

        eyeIcon.EnableInClassList("icon-eye-off", visible);
        eyeIcon.EnableInClassList("icon-eye", !visible);
    }

    private void OnNewPasswordChanged(ChangeEvent<string> evt)
    {
        UpdatePasswordValidation();
    }

    private void OnConfirmPasswordChanged(ChangeEvent<string> evt)
    {
        UpdatePasswordValidation();
    }

    private void UpdatePasswordValidation()
    {
        string password = newPasswordField?.value ?? string.Empty;
        string confirm = confirmPasswordField?.value ?? string.Empty;

        PasswordRules rules = EvaluatePassword(password);

        SetRuleState(ruleLength, rules.HasLength);
        SetRuleState(ruleUppercase, rules.HasUppercase);
        SetRuleState(ruleLowercase, rules.HasLowercase);
        SetRuleState(ruleNumber, rules.HasNumber);
        SetRuleState(ruleSpecial, rules.HasSpecial);

        bool hasConfirm = !string.IsNullOrEmpty(confirm);
        bool matches = hasConfirm && string.Equals(password, confirm, StringComparison.Ordinal);

        SetInputState(confirmPasswordContainer, hasConfirm && !matches, matches);

        if (passwordMatchLabel != null)
        {
            if (!hasConfirm)
            {
                passwordMatchLabel.text = string.Empty;
                passwordMatchLabel.RemoveFromClassList("password-match--success");
                passwordMatchLabel.RemoveFromClassList("password-match--error");
            }
            else if (matches)
            {
                passwordMatchLabel.text = "✓ Passwords match";
                passwordMatchLabel.AddToClassList("password-match--success");
                passwordMatchLabel.RemoveFromClassList("password-match--error");
            }
            else
            {
                passwordMatchLabel.text = "ⓘ Passwords do not match.";
                passwordMatchLabel.AddToClassList("password-match--error");
                passwordMatchLabel.RemoveFromClassList("password-match--success");
            }
        }

        resetPasswordButton?.SetEnabled(
            !isResettingPassword &&
            rules.AllValid &&
            matches &&
            !string.IsNullOrWhiteSpace(recoveryAccessToken));
    }

    private static void SetRuleState(VisualElement ruleElement, bool valid)
    {
        if (ruleElement == null) return;

        ruleElement.EnableInClassList("password-rule--valid", valid);

        Label icon = ruleElement.Q<Label>(className: "password-rule-icon");
        if (icon != null)
            icon.text = valid ? "✓" : "○";
    }

    private void OnOtpChanged(int index, ChangeEvent<string> evt)
    {
        if (isUpdatingOtpFields || otpFields[index] == null)
            return;

        string digits = DigitsOnly(evt.newValue);

        if (digits.Length > 1)
        {
            FillOtpFrom(index, digits);
            return;
        }

        isUpdatingOtpFields = true;
        otpFields[index].SetValueWithoutNotify(digits.Length == 0 ? string.Empty : digits[digits.Length - 1].ToString());
        isUpdatingOtpFields = false;

        otpFields[index].EnableInClassList("otp-input--filled", !string.IsNullOrEmpty(otpFields[index].value));

        if (!string.IsNullOrEmpty(otpFields[index].value) && index < OtpLength - 1)
            otpFields[index + 1]?.Focus();

        ClearMessage(verifyMessageLabel);
        UpdateVerifyButtonState();
    }

    private void OnOtpKeyDown(int index, KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Backspace &&
            string.IsNullOrEmpty(otpFields[index]?.value) &&
            index > 0)
        {
            otpFields[index - 1]?.Focus();
        }
    }

    private void FillOtpFrom(int startIndex, string rawDigits)
    {
        string digits = DigitsOnly(rawDigits);
        if (digits.Length == 0) return;

        isUpdatingOtpFields = true;

        int target = startIndex;
        foreach (char digit in digits)
        {
            if (target >= OtpLength) break;

            otpFields[target]?.SetValueWithoutNotify(digit.ToString());
            otpFields[target]?.EnableInClassList("otp-input--filled", true);
            target++;
        }

        isUpdatingOtpFields = false;

        int focusIndex = Mathf.Clamp(target, 0, OtpLength - 1);
        otpFields[focusIndex]?.Focus();

        ClearMessage(verifyMessageLabel);
        UpdateVerifyButtonState();
    }

    private string GetOtpCode()
    {
        return string.Concat(otpFields.Select(field => DigitsOnly(field?.value)));
    }

    private void ClearOtpFields()
    {
        isUpdatingOtpFields = true;

        foreach (TextField field in otpFields)
        {
            if (field == null) continue;
            field.SetValueWithoutNotify(string.Empty);
            field.RemoveFromClassList("otp-input--filled");
        }

        isUpdatingOtpFields = false;
        UpdateVerifyButtonState();
    }

    private void SetOtpEnabled(bool enabled)
    {
        foreach (TextField field in otpFields)
            field?.SetEnabled(enabled);
    }

    private void UpdateVerifyButtonState()
    {
        bool complete = GetOtpCode().Length == OtpLength;
        bool unlocked = verifyLockSecondsRemaining <= 0;

        verifyCodeButton?.SetEnabled(complete && unlocked && !isVerifyingCode);
    }

    private void StartResendCooldown()
    {
        resendSecondsRemaining = ResendCooldownSeconds;
        UpdateResendButton();
    }

    private void TickTimers()
    {
        if (resendSecondsRemaining > 0)
        {
            resendSecondsRemaining--;
            UpdateResendButton();
        }

        if (verifyLockSecondsRemaining > 0)
        {
            verifyLockSecondsRemaining--;

            if (verifyLockSecondsRemaining == 0)
            {
                failedVerifyAttempts = 0;
                ClearMessage(verifyMessageLabel);
                UpdateVerifyButtonState();
            }
        }
    }

    private void UpdateResendButton()
    {
        if (resendCodeButton == null) return;

        if (resendSecondsRemaining > 0)
        {
            resendCodeButton.text = $"Resend in 00:{resendSecondsRemaining:00}";
            resendCodeButton.SetEnabled(false);
        }
        else
        {
            resendCodeButton.text = "Resend code";
            resendCodeButton.SetEnabled(!isSendingCode);
        }
    }

    private void SetStep(Step step)
    {
        currentStep = step;

        if (step != Step.ResetPassword)
            StopKeyboardAvoidance();

        forgotForm?.EnableInClassList("form-hidden", step != Step.ForgotPassword);
        verifyForm?.EnableInClassList("form-hidden", step != Step.VerifyCode);
        resetForm?.EnableInClassList("form-hidden", step != Step.ResetPassword);
        successForm?.EnableInClassList("form-hidden", step != Step.Success);

        if (backButton != null)
            backButton.style.display = step == Step.None ? DisplayStyle.None : DisplayStyle.Flex;

        if (step == Step.VerifyCode)
        {
            UpdateVerifyButtonState();
            UpdateResendButton();
        }
        else if (step == Step.ResetPassword)
        {
            UpdatePasswordValidation();
        }
    }

    private void ReturnToLogin()
    {
        string emailToRestore = recoveryEmail;

        root.RemoveFromClassList("recovery-active");
        SetStep(Step.None);
        ClearRecoveryState();

        returnToLogin?.Invoke(emailToRestore);
    }

    private void ClearRecoveryState()
    {
        isSendingCode = false;
        isVerifyingCode = false;
        isResettingPassword = false;
        recoveryAccessToken = string.Empty;
        recoveryEmail = string.Empty;
        failedVerifyAttempts = 0;
        verifyLockSecondsRemaining = 0;
        resendSecondsRemaining = 0;

        ClearMessage(forgotMessageLabel);
        ClearMessage(verifyMessageLabel);
        ClearMessage(resetMessageLabel);

        SetInputState(forgotEmailContainer, false, false);
        SetInputState(newPasswordContainer, false, false);
        SetInputState(confirmPasswordContainer, false, false);

        ClearOtpFields();

        if (newPasswordField != null)
        {
            newPasswordField.SetValueWithoutNotify(string.Empty);
            newPasswordField.isPasswordField = true;
        }

        if (confirmPasswordField != null)
        {
            confirmPasswordField.SetValueWithoutNotify(string.Empty);
            confirmPasswordField.isPasswordField = true;
        }

        newPasswordVisible = false;
        confirmPasswordVisible = false;
        UpdatePasswordVisibility(newPasswordField, newPasswordEyeIcon, false);
        UpdatePasswordVisibility(confirmPasswordField, confirmPasswordEyeIcon, false);

        if (passwordMatchLabel != null)
        {
            passwordMatchLabel.text = string.Empty;
            passwordMatchLabel.RemoveFromClassList("password-match--success");
            passwordMatchLabel.RemoveFromClassList("password-match--error");
        }

        UpdatePasswordValidation();
        UpdateResendButton();
    }

    private static void SetButtonLoading(Button button, bool loading, string text)
    {
        if (button == null) return;
        button.text = text;
        button.SetEnabled(!loading);
    }

    private void SetResetInputsEnabled(bool enabled)
    {
        newPasswordField?.SetEnabled(enabled);
        confirmPasswordField?.SetEnabled(enabled);
        toggleNewPasswordButton?.SetEnabled(enabled);
        toggleConfirmPasswordButton?.SetEnabled(enabled);
    }

    private static void SetInputState(VisualElement container, bool error, bool success)
    {
        if (container == null) return;
        container.EnableInClassList("input-error", error);
        container.EnableInClassList("input-success", success);
    }

    private static void ShowMessage(Label label, string message, MessageType type)
    {
        if (label == null) return;

        label.text = message ?? string.Empty;
        label.RemoveFromClassList("recovery-message--error");
        label.RemoveFromClassList("recovery-message--success");
        label.RemoveFromClassList("recovery-message--info");

        if (string.IsNullOrWhiteSpace(message))
            return;

        switch (type)
        {
            case MessageType.Error:
                label.AddToClassList("recovery-message--error");
                break;
            case MessageType.Success:
                label.AddToClassList("recovery-message--success");
                break;
            case MessageType.Info:
                label.AddToClassList("recovery-message--info");
                break;
        }
    }

    private static void ClearMessage(Label label)
    {
        if (label == null) return;
        label.text = string.Empty;
        label.RemoveFromClassList("recovery-message--error");
        label.RemoveFromClassList("recovery-message--success");
        label.RemoveFromClassList("recovery-message--info");
    }

    private void OnForgotEmailFocusIn(FocusInEvent evt) =>
        forgotEmailContainer?.AddToClassList("input-focused");

    private void OnForgotEmailFocusOut(FocusOutEvent evt) =>
        forgotEmailContainer?.RemoveFromClassList("input-focused");

    private void OnNewPasswordFocusIn(FocusInEvent evt)
    {
        newPasswordContainer?.AddToClassList("input-focused");

        // When the user starts the first password, proactively make the confirm field
        // visible too. This avoids the common mobile UX trap where the keyboard opens
        // and the next field is completely hidden below it.
        StartKeyboardAvoidance(confirmPasswordContainer ?? newPasswordContainer);
    }

    private void OnNewPasswordFocusOut(FocusOutEvent evt) =>
        newPasswordContainer?.RemoveFromClassList("input-focused");

    private void OnConfirmPasswordFocusIn(FocusInEvent evt)
    {
        confirmPasswordContainer?.AddToClassList("input-focused");
        StartKeyboardAvoidance(confirmPasswordContainer);
    }

    private void OnConfirmPasswordFocusOut(FocusOutEvent evt) =>
        confirmPasswordContainer?.RemoveFromClassList("input-focused");

    private void OnRootGeometryChanged(GeometryChangedEvent evt)
    {
        // Remember the largest layout height we have seen. On Android with adjustResize,
        // the root becomes shorter while the keyboard is open. Keeping the full-height
        // baseline lets us avoid subtracting the keyboard twice.
        if (evt.newRect.height > keyboardBaselineRootHeight)
            keyboardBaselineRootHeight = evt.newRect.height;
    }

    private void StartKeyboardAvoidance(VisualElement target)
    {
        if (target == null)
            return;

        if (!savedPreKeyboardScrollOffset && authScroll != null)
        {
            preKeyboardScrollOffset = authScroll.scrollOffset;
            savedPreKeyboardScrollOffset = true;
        }

        float currentRootHeight = root.resolvedStyle.height;
        if (!float.IsNaN(currentRootHeight) && currentRootHeight > keyboardBaselineRootHeight)
            keyboardBaselineRootHeight = currentRootHeight;

        keyboardAvoidTarget = target;
        keyboardHiddenTicks = 0;
        keyboardTimer?.Resume();

        // Do one immediate pass and several scheduled passes while the keyboard animates.
        // This is intentionally independent of TouchScreenKeyboard.area because some
        // Android keyboards report area = 0 for a few frames.
        ApplyKeyboardAvoidance(true);
        root.schedule.Execute(() =>
        {
            if (keyboardAvoidTarget == target && currentStep == Step.ResetPassword)
                ApplyKeyboardAvoidance(true);
        }).ExecuteLater(120);
        root.schedule.Execute(() =>
        {
            if (keyboardAvoidTarget == target && currentStep == Step.ResetPassword)
                ApplyKeyboardAvoidance(true);
        }).ExecuteLater(300);
        root.schedule.Execute(() =>
        {
            if (keyboardAvoidTarget == target && currentStep == Step.ResetPassword)
                ApplyKeyboardAvoidance(true);
        }).ExecuteLater(550);
    }

    private void StopKeyboardAvoidance()
    {
        keyboardAvoidTarget = null;
        keyboardWasVisible = false;
        keyboardHiddenTicks = 0;
        keyboardTimer?.Pause();
        RestoreKeyboardLayout(true);
    }

    private void RestoreKeyboardLayout(bool restoreScroll)
    {
        if (pageBottomSpace != null)
            pageBottomSpace.style.height = DefaultBottomSpaceHeight;

        if (restoreScroll && savedPreKeyboardScrollOffset && authScroll != null)
        {
            Vector2 offset = preKeyboardScrollOffset;
            root.schedule.Execute(() =>
            {
                if (authScroll != null)
                    authScroll.scrollOffset = offset;
            }).ExecuteLater(1);
        }

        savedPreKeyboardScrollOffset = false;
    }

    private void UpdateKeyboardAvoidance()
    {
#if UNITY_ANDROID || UNITY_IOS
        if (currentStep != Step.ResetPassword || keyboardAvoidTarget == null)
        {
            StopKeyboardAvoidance();
            return;
        }

        bool keyboardVisible = TouchScreenKeyboard.isSupported && TouchScreenKeyboard.visible;

        if (keyboardVisible)
        {
            keyboardWasVisible = true;
            keyboardHiddenTicks = 0;
            ApplyKeyboardAvoidance(false);
            return;
        }

        // While the keyboard is sliding in, visible can remain false for a short time.
        // Keep applying the fallback instead of doing nothing. Once a keyboard was fully
        // visible, require several hidden ticks before restoring the original scroll.
        if (!keyboardWasVisible)
        {
            ApplyKeyboardAvoidance(true);
            return;
        }

        keyboardHiddenTicks++;
        if (keyboardHiddenTicks >= KeyboardHiddenTicksBeforeRestore)
            StopKeyboardAvoidance();
        else
            ApplyKeyboardAvoidance(true);
#endif
    }

    private void ApplyKeyboardAvoidance(bool allowFallback)
    {
#if UNITY_ANDROID || UNITY_IOS
        if (authScroll == null || keyboardAvoidTarget == null)
            return;

        float rootWidth = root.resolvedStyle.width;
        float rootHeight = root.resolvedStyle.height;

        if (float.IsNaN(rootWidth) || rootWidth <= 0f ||
            float.IsNaN(rootHeight) || rootHeight <= 0f)
            return;

        if (rootHeight > keyboardBaselineRootHeight)
            keyboardBaselineRootHeight = rootHeight;

        float keyboardHeight = 0f;

        if (TouchScreenKeyboard.isSupported && Screen.width > 0)
        {
            Rect keyboardArea = TouchScreenKeyboard.area;
            if (keyboardArea.height > 0f)
            {
                float pixelsToPanel = rootWidth / Screen.width;
                keyboardHeight = keyboardArea.height * pixelsToPanel;
            }
        }

        // Fallback for keyboards/devices that temporarily report an empty area.
        if (keyboardHeight <= 0f && allowFallback)
            keyboardHeight = keyboardBaselineRootHeight * KeyboardFallbackHeightRatio;

        float resizeDelta = Mathf.Max(0f, keyboardBaselineRootHeight - rootHeight);
        bool windowAlreadyResized = keyboardHeight > 0f &&
                                    resizeDelta >= keyboardHeight * 0.45f;

        // If Android already resized the Unity surface, the ScrollView viewport is already
        // above the keyboard, so do not subtract the keyboard height a second time.
        float keyboardOverlay = windowAlreadyResized ? 0f : keyboardHeight;

        // Always leave enough trailing scroll space. Even on adjustResize devices this is
        // needed so the lower confirm field can be moved above the keyboard.
        float extraScrollSpace = Mathf.Max(120f, rootHeight * KeyboardExtraScrollSpaceRatio);
        if (pageBottomSpace != null)
        {
            pageBottomSpace.style.height =
                DefaultBottomSpaceHeight +
                Mathf.Max(keyboardOverlay + KeyboardSafetyPadding, extraScrollSpace);
        }

        VisualElement viewport = authScroll.contentViewport;
        Rect viewportBounds = viewport != null ? viewport.worldBound : authScroll.worldBound;

        float visibleTop = viewportBounds.yMin + KeyboardSafetyPadding;
        float visibleBottom = viewportBounds.yMax - keyboardOverlay - KeyboardSafetyPadding;

        Rect targetBounds = keyboardAvoidTarget.worldBound;
        float targetBottom = targetBounds.yMax;
        float targetTop = targetBounds.yMin;

        // Extra room below the confirm field keeps its validation message from sitting
        // exactly on the keyboard edge.
        float desiredBottom = visibleBottom - 36f;
        float delta = 0f;

        if (targetBottom > desiredBottom)
            delta = targetBottom - desiredBottom;
        else if (targetTop < visibleTop)
            delta = targetTop - visibleTop;

        if (Mathf.Abs(delta) > 0.5f)
        {
            Vector2 offset = authScroll.scrollOffset;
            authScroll.scrollOffset = new Vector2(
                offset.x,
                Mathf.Max(0f, offset.y + delta));
        }
#endif
    }

    private static PasswordRules EvaluatePassword(string password)
    {
        if (password == null) password = string.Empty;

        return new PasswordRules
        {
            HasLength = password.Length >= 8,
            HasUppercase = password.Any(char.IsUpper),
            HasLowercase = password.Any(char.IsLower),
            HasNumber = password.Any(char.IsDigit),
            HasSpecial = password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c))
        };
    }

    private bool PasswordsMatch()
    {
        string password = newPasswordField?.value ?? string.Empty;
        string confirm = confirmPasswordField?.value ?? string.Empty;

        return !string.IsNullOrEmpty(password) &&
               string.Equals(password, confirm, StringComparison.Ordinal);
    }

    private static string TranslateRecoverySendError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "We couldn't send the verification code. Please try again.";

        string lower = error.ToLowerInvariant();

        if (lower.Contains("rate limit") ||
            lower.Contains("too many requests") ||
            lower.Contains("seconds"))
        {
            return "Please wait a moment before requesting another code.";
        }

        if (lower.Contains("network") ||
            lower.Contains("connection") ||
            lower.Contains("resolve host"))
        {
            return "Unable to connect to the server. Please check your internet connection.";
        }

        return "We couldn't send the verification code. Please try again.";
    }

    private static string TranslateVerifyError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "The verification code is incorrect. Please try again.";

        string lower = error.ToLowerInvariant();

        if (lower.Contains("expired") || lower.Contains("otp_expired"))
            return "This code has expired. Request a new code to continue.";

        if (lower.Contains("rate limit") || lower.Contains("too many requests"))
            return "Too many attempts. Please wait before trying again.";

        if (lower.Contains("network") || lower.Contains("connection"))
            return "Unable to connect to the server. Please check your internet connection.";

        return "The verification code is incorrect. Please try again.";
    }

    private static string TranslateResetError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "We couldn't reset your password. Please try again.";

        string lower = error.ToLowerInvariant();

        if (lower.Contains("expired") ||
            lower.Contains("invalid token") ||
            lower.Contains("jwt"))
        {
            return "Your recovery session has expired. Please request a new verification code.";
        }

        if (lower.Contains("password"))
            return "The new password does not meet the account security requirements.";

        if (lower.Contains("network") || lower.Contains("connection"))
            return "Unable to connect to the server. Please check your internet connection.";

        return "We couldn't reset your password. Please try again.";
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

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            MailAddress address = new MailAddress(email);
            return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        int atIndex = email.IndexOf('@');
        if (atIndex <= 0)
            return email;

        string local = email.Substring(0, atIndex);
        string domain = email.Substring(atIndex + 1);

        int visibleCount = local.Length >= 3 ? 3 : 1;
        string visible = local.Substring(0, Math.Min(visibleCount, local.Length));

        return $"{visible}***@{domain}";
    }

    private static string DigitsOnly(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return new string(value.Where(char.IsDigit).ToArray());
    }

    private struct PasswordRules
    {
        public bool HasLength;
        public bool HasUppercase;
        public bool HasLowercase;
        public bool HasNumber;
        public bool HasSpecial;

        public bool AllValid =>
            HasLength &&
            HasUppercase &&
            HasLowercase &&
            HasNumber &&
            HasSpecial;
    }
}
