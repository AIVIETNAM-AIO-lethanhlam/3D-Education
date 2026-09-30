using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class UserInfoPageController : MonoBehaviour
{
    private const string SettingSceneName = "SettingScene";

    private UIDocument uiDocument;
    private GeneralHeaderController generalHeaderController;

    private Button changeAvatarButton;
    private Button passwordVisibilityButton;
    private Button saveChangesButton;

    private TextField usernameField;
    private TextField dateOfBirthField;
    private TextField emailField;
    private TextField passwordField;

    private Label profileNameLabel;
    private Label profileRoleLabel;
    private Label avatarInitialLabel;

    private VisualElement passwordVisibilityIcon;

    private bool isPasswordVisible;

    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogError(
                "Không tìm thấy UIDocument trên GameObject UserInfoUIDocument.");

            return;
        }

        VisualElement root = uiDocument.rootVisualElement;

        ConfigureHeader(root);
        FindVisualElements(root);
        ConfigureFields();
        RegisterCallbacks();
        LoadUserInformation();
    }

    private void OnDisable()
    {
        UnregisterCallbacks();

        if (generalHeaderController != null)
        {
            generalHeaderController.BackClicked -=
                ReturnToSettingScene;

            generalHeaderController.Dispose();
            generalHeaderController = null;
        }
    }

    private void ConfigureHeader(VisualElement root)
    {
        generalHeaderController =
            new GeneralHeaderController(root);

        generalHeaderController.ConfigurePage(
            title: "User Information",
            subtitle: null,
            showBackButton: true,
            showSubtitleIcon: false);

        generalHeaderController.SetBottomBorderVisible(true);

        generalHeaderController.BackClicked +=
            ReturnToSettingScene;
    }

    private void FindVisualElements(VisualElement root)
    {
        changeAvatarButton =
            root.Q<Button>("change-avatar-button");

        passwordVisibilityButton =
            root.Q<Button>("password-visibility-button");

        saveChangesButton =
            root.Q<Button>("save-changes-button");

        usernameField =
            root.Q<TextField>("username-field");

        dateOfBirthField =
            root.Q<TextField>("date-of-birth-field");

        emailField =
            root.Q<TextField>("email-field");

        passwordField =
            root.Q<TextField>("password-field");

        profileNameLabel =
            root.Q<Label>("profile-name-label");

        profileRoleLabel =
            root.Q<Label>("profile-role-label");

        avatarInitialLabel =
            root.Q<Label>("avatar-initial-label");

        passwordVisibilityIcon =
            root.Q<VisualElement>("password-visibility-icon");
    }

    private void ConfigureFields()
    {
        if (passwordField == null)
        {
            return;
        }

        isPasswordVisible = false;
        passwordField.isPasswordField = true;
    }

    private void RegisterCallbacks()
    {
        if (changeAvatarButton != null)
        {
            changeAvatarButton.clicked +=
                OnChangeAvatarClicked;
        }

        if (passwordVisibilityButton != null)
        {
            passwordVisibilityButton.clicked +=
                TogglePasswordVisibility;
        }

        if (saveChangesButton != null)
        {
            saveChangesButton.clicked +=
                SaveChanges;
        }

        if (usernameField != null)
        {
            usernameField.RegisterValueChangedCallback(
                OnUsernameChanged);
        }
    }

    private void UnregisterCallbacks()
    {
        if (changeAvatarButton != null)
        {
            changeAvatarButton.clicked -=
                OnChangeAvatarClicked;
        }

        if (passwordVisibilityButton != null)
        {
            passwordVisibilityButton.clicked -=
                TogglePasswordVisibility;
        }

        if (saveChangesButton != null)
        {
            saveChangesButton.clicked -=
                SaveChanges;
        }

        if (usernameField != null)
        {
            usernameField.UnregisterValueChangedCallback(
                OnUsernameChanged);
        }
    }

    // =========================================================
    // LOAD / SAVE (2026-09: data comes from and goes to Supabase)
    // =========================================================

    private Label statusLabel;
    private bool isSaving;

    private void LoadUserInformation()
    {
        // The plain-text password must never be stored on the device.
        PlayerPrefs.DeleteKey("current_password");

        string username = SupabaseSession.FullName;
        string email = SupabaseSession.Email;
        string role = SupabaseSession.Role;

        usernameField?.SetValueWithoutNotify(username);
        dateOfBirthField?.SetValueWithoutNotify(string.Empty);

        // BUG-014: an empty date must not look like a broken field.
        if (dateOfBirthField != null)
        {
            dateOfBirthField.textEdition.placeholder =
                AppLanguageManager.IsVietnamese
                    ? "Chưa cập nhật (MM/dd/yyyy)"
                    : "Not updated (MM/dd/yyyy)";
        }

        if (emailField != null)
        {
            emailField.SetValueWithoutNotify(email);
            // Changing the login email needs e-mail confirmation, so it is read-only here.
            emailField.isReadOnly = true;
        }

        if (passwordField != null)
        {
            // Empty = keep the current password.
            passwordField.SetValueWithoutNotify(string.Empty);
            passwordField.textEdition.placeholder = "New password (leave empty to keep)";
        }

        if (profileRoleLabel != null)
        {
            profileRoleLabel.text =
                string.Equals(role, "teacher", StringComparison.OrdinalIgnoreCase)
                    ? "Teacher · HCMUT"
                    : "Student · HCMUT";
        }

        UpdateProfileDisplay(username);
        EnsureStatusLabel();

        if (SupabaseSession.IsLoggedIn)
            StartCoroutine(LoadProfileFromSupabase());
    }

    private IEnumerator LoadProfileFromSupabase()
    {
        SupabaseProfile profile = null;
        string error = null;

        yield return SupabaseProfileService.GetCurrentProfile(
            value => profile = value,
            message => error = message);

        if (profile == null)
        {
            ShowStatus("Cannot load profile: " + error, true);
            yield break;
        }

        string fullName = profile.full_name ?? string.Empty;
        usernameField?.SetValueWithoutNotify(fullName);
        UpdateProfileDisplay(fullName);

        // profiles.date_of_birth is yyyy-MM-dd; the form uses MM/dd/yyyy.
        // Only the first 10 characters are used so a timestamp value
        // ("yyyy-MM-ddTHH:mm:ss...") is also accepted.
        string rawBirthDate = (profile.date_of_birth ?? string.Empty).Trim();
        if (rawBirthDate.Length > 10)
            rawBirthDate = rawBirthDate.Substring(0, 10);

        if (dateOfBirthField != null &&
            DateTime.TryParseExact(
                rawBirthDate,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateTime birthDate))
        {
            dateOfBirthField.SetValueWithoutNotify(
                birthDate.ToString("MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (profileRoleLabel != null)
        {
            profileRoleLabel.text =
                string.Equals(profile.role, "teacher", StringComparison.OrdinalIgnoreCase)
                    ? "Teacher · HCMUT"
                    : "Student · HCMUT";
        }
    }

    private void SaveChanges()
    {
        if (isSaving)
            return;

        string username = usernameField != null ? usernameField.value.Trim() : string.Empty;
        string dateOfBirth = dateOfBirthField != null ? dateOfBirthField.value.Trim() : string.Empty;
        string password = passwordField != null ? passwordField.value : string.Empty;

        if (!ValidateInput(username, dateOfBirth, password))
            return;

        StartCoroutine(SaveChangesRoutine(username, dateOfBirth, password));
    }

    private IEnumerator SaveChangesRoutine(string username, string dateOfBirth, string password)
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            ShowStatus("Your session has expired. Please log in again.", true);
            yield break;
        }

        isSaving = true;
        saveChangesButton?.SetEnabled(false);
        ShowStatus("Saving...", false);

        string isoDate = string.Empty;
        if (!string.IsNullOrWhiteSpace(dateOfBirth) &&
            DateTime.TryParseExact(
                dateOfBirth,
                "MM/dd/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateTime parsedDate))
        {
            isoDate = parsedDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        // 1. Profile (full_name, date_of_birth) -> public.profiles
        string profileError = null;
        yield return SupabaseProfileService.UpdateCurrentProfile(
            username,
            isoDate,
            _ => { },
            message => profileError = message);

        if (!string.IsNullOrWhiteSpace(profileError))
        {
            FinishSaving("Cannot save profile: " + profileError, true);
            yield break;
        }

        // Keep legacy keys used by older scenes in sync.
        PlayerPrefs.SetString("current_full_name", username);
        PlayerPrefs.SetString("current_date_of_birth", dateOfBirth);
        PlayerPrefs.Save();

        // 2. Password -> Supabase Auth (PUT /auth/v1/user), only when a new one was typed.
        if (!string.IsNullOrEmpty(password))
        {
            string passwordError = null;
            yield return SupabaseAuthService.UpdatePassword(
                password,
                () => { },
                message => passwordError = message);

            if (!string.IsNullOrWhiteSpace(passwordError))
            {
                FinishSaving("Profile saved, but the password was not changed: " + passwordError, true);
                yield break;
            }

            passwordField?.SetValueWithoutNotify(string.Empty);
        }

        FinishSaving("Saved successfully.", false);
        yield return new WaitForSeconds(0.8f);
        ReturnToSettingScene();
    }

    private void FinishSaving(string message, bool isError)
    {
        isSaving = false;
        saveChangesButton?.SetEnabled(true);
        ShowStatus(message, isError);
    }

    private void EnsureStatusLabel()
    {
        if (statusLabel != null || saveChangesButton?.parent == null)
            return;

        statusLabel = new Label(string.Empty);
        statusLabel.name = "user-info-status-label";
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        statusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        statusLabel.style.fontSize = 12;
        statusLabel.style.marginBottom = 6;
        statusLabel.style.display = DisplayStyle.None;
        saveChangesButton.parent.Insert(0, statusLabel);
    }

    private void ShowStatus(string message, bool isError)
    {
        if (isError)
            Debug.LogWarning("[UserInfo] " + message);

        EnsureStatusLabel();
        if (statusLabel == null)
            return;

        statusLabel.text = message ?? string.Empty;
        statusLabel.style.color = isError
            ? new Color(0.75f, 0.19f, 0.27f)
            : new Color(0.13f, 0.45f, 0.25f);
        statusLabel.style.display = string.IsNullOrWhiteSpace(message)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

    private bool ValidateInput(
        string username,
        string dateOfBirth,
        string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            ShowStatus("Username cannot be empty.", true);
            usernameField?.Focus();
            return false;
        }

        // Date of birth is optional; when present it must be MM/dd/yyyy and in the past.
        if (!string.IsNullOrWhiteSpace(dateOfBirth))
        {
            if (!IsValidDate(dateOfBirth))
            {
                ShowStatus("Date of birth must use the MM/dd/yyyy format.", true);
                dateOfBirthField?.Focus();
                return false;
            }
        }

        // Same rules as sign-up: 8+ characters, 1 uppercase letter, letters and numbers (2026-09).
        if (!string.IsNullOrEmpty(password) && !PasswordPolicy.IsValid(password))
        {
            ShowStatus(PasswordPolicy.ErrorMessage(password), true);
            passwordField?.Focus();
            return false;
        }

        return true;
    }

    private bool IsValidDate(string dateText)
    {
        return DateTime.TryParseExact(
                   dateText,
                   "MM/dd/yyyy",
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None,
                   out DateTime value) &&
               value.Date < DateTime.Today &&
               value.Year >= 1900;
    }

    private void TogglePasswordVisibility()
    {
        if (passwordField == null)
        {
            return;
        }

        isPasswordVisible = !isPasswordVisible;
        passwordField.isPasswordField = !isPasswordVisible;

        if (passwordVisibilityIcon == null)
        {
            return;
        }

        passwordVisibilityIcon.EnableInClassList(
            "icon-eye",
            !isPasswordVisible);

        passwordVisibilityIcon.EnableInClassList(
            "icon-eye-off",
            isPasswordVisible);
    }

    private void OnUsernameChanged(
        ChangeEvent<string> changeEvent)
    {
        UpdateProfileDisplay(changeEvent.newValue);
    }

    private void UpdateProfileDisplay(string username)
    {
        string cleanedUsername =
            string.IsNullOrWhiteSpace(username)
                ? "User"
                : username.Trim();

        if (profileNameLabel != null)
        {
            profileNameLabel.text =
                cleanedUsername;
        }

        if (avatarInitialLabel != null)
        {
            avatarInitialLabel.text =
                CreateInitials(cleanedUsername);
        }
    }

    private string CreateInitials(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "U";
        }

        string[] words = fullName.Split(
            new[] { ' ' },
            StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 1)
        {
            return words[0]
                .Substring(0, 1)
                .ToUpper();
        }

        string firstInitial =
            words[0].Substring(0, 1);

        string lastInitial =
            words[words.Length - 1].Substring(0, 1);

        return (firstInitial + lastInitial)
            .ToUpper();
    }

    private void OnChangeAvatarClicked()
    {
        Debug.Log("Đã nhấn nút thay đổi avatar.");
    }

    private void ReturnToSettingScene()
    {
        if (!Application.CanStreamedLevelBeLoaded(
                SettingSceneName))
        {
            Debug.LogError(
                $"Không tìm thấy scene {SettingSceneName}. " +
                "Hãy thêm scene vào Build Profiles.");

            return;
        }

        SceneManager.LoadScene(SettingSceneName);
    }
}