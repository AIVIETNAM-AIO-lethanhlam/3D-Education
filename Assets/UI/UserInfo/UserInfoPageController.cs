using System;
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

    private void LoadUserInformation()
    {
        string username = PlayerPrefs.GetString(
            "current_full_name",
            "Trần Văn Bình");

        string dateOfBirth = PlayerPrefs.GetString(
            "current_date_of_birth",
            "03/15/1999");

        string email = PlayerPrefs.GetString(
            "current_email",
            "binh.tran@hcmut.edu.vn");

        string password = PlayerPrefs.GetString(
            "current_password",
            "123456789");

        string role = PlayerPrefs.GetString(
            "current_role",
            "student");

        if (usernameField != null)
        {
            usernameField.SetValueWithoutNotify(username);
        }

        if (dateOfBirthField != null)
        {
            dateOfBirthField.SetValueWithoutNotify(dateOfBirth);
        }

        if (emailField != null)
        {
            emailField.SetValueWithoutNotify(email);
        }

        if (passwordField != null)
        {
            passwordField.SetValueWithoutNotify(password);
        }

        if (profileRoleLabel != null)
        {
            profileRoleLabel.text =
                string.Equals(
                    role,
                    "teacher",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Teacher · HCMUT"
                    : "Student · HCMUT";
        }

        UpdateProfileDisplay(username);
    }

    private void SaveChanges()
    {
        string username =
            usernameField != null
                ? usernameField.value.Trim()
                : string.Empty;

        string dateOfBirth =
            dateOfBirthField != null
                ? dateOfBirthField.value.Trim()
                : string.Empty;

        string email =
            emailField != null
                ? emailField.value.Trim()
                : string.Empty;

        string password =
            passwordField != null
                ? passwordField.value
                : string.Empty;

        if (!ValidateInput(
                username,
                dateOfBirth,
                email,
                password))
        {
            return;
        }

        PlayerPrefs.SetString(
            "current_full_name",
            username);

        PlayerPrefs.SetString(
            "current_date_of_birth",
            dateOfBirth);

        PlayerPrefs.SetString(
            "current_email",
            email);

        PlayerPrefs.SetString(
            "current_password",
            password);

        PlayerPrefs.Save();

        Debug.Log("Đã lưu thông tin người dùng.");

        ReturnToSettingScene();
    }

    private bool ValidateInput(
        string username,
        string dateOfBirth,
        string email,
        string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            Debug.LogWarning(
                "Tên người dùng không được để trống.");

            usernameField?.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(dateOfBirth))
        {
            Debug.LogWarning(
                "Ngày sinh không được để trống.");

            dateOfBirthField?.Focus();
            return false;
        }

        if (!IsValidDate(dateOfBirth))
        {
            Debug.LogWarning(
                "Ngày sinh không đúng định dạng MM/dd/yyyy.");

            dateOfBirthField?.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(email) ||
            !email.Contains("@") ||
            !email.Contains("."))
        {
            Debug.LogWarning("Email không hợp lệ.");

            emailField?.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(password) ||
            password.Length < 6)
        {
            Debug.LogWarning(
                "Mật khẩu phải có ít nhất 6 ký tự.");

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
            null,
            System.Globalization.DateTimeStyles.None,
            out _);
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