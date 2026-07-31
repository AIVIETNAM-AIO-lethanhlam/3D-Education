using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class SettingPageController : MonoBehaviour
{
    private const string HomeSceneName = "HomeScene";
    private const string PrivacySceneName = "PrivacyScene";
    private const string UserInfoSceneName = "UserInfoScene";

    [Header("Optional Audio Mixer")]
    [SerializeField] private AudioMixer audioMixer;

    private GeneralHeaderController headerController;
    private BottomNavigationController bottomNavigationController;

    private Label profileNameLabel;
    private Label profileEmailLabel;
    private Label profileRoleLabel;
    private Label sfxValueLabel;
    private Label bgmValueLabel;

    private Button editProfileButton;
    private Button userInformationButton;
    private Button englishButton;
    private Button vietnameseButton;
    private Button restoreButton;
    private Button dismissButton;
    private Button privacyButton;
    private Button logoutButton;

    private Toggle darkModeToggle;
    private Toggle tutorialToggle;

    private Slider sfxSlider;
    private Slider bgmSlider;

    private VisualElement sfxSliderFill;
    private VisualElement bgmSliderFill;
    private VisualElement sessionRecoveryCard;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        if (document == null)
        {
            Debug.LogError(
                "SettingPageController: Không tìm thấy UIDocument.");
            return;
        }

        VisualElement root = document.rootVisualElement;

        if (root == null)
        {
            Debug.LogError(
                "SettingPageController: rootVisualElement đang null.");
            return;
        }

        InitializeHeader(root);
        InitializeBottomNavigation(root);
        FindElements(root);

        LoadProfileInformation();
        LoadSavedSettings();
        UpdateRecoveryCardVisibility();
        RegisterEvents();
    }

    private void OnDisable()
    {
        UnregisterEvents();
        DisposeHeader();
        DisposeBottomNavigation();
    }

    private void InitializeHeader(VisualElement root)
    {
        headerController =
            new GeneralHeaderController(root);

        headerController.ConfigurePage(
            title: "Settings",
            subtitle: null,
            showBackButton: true,
            showSubtitleIcon: false);

        headerController.SetCompact(false);
        headerController.SetLargeSafeArea(false);
        headerController.SetBottomBorderVisible(true);

    }

    private void DisposeHeader()
    {
        if (headerController == null)
        {
            return;
        }

        headerController.Dispose();
        headerController = null;
    }

    private void InitializeBottomNavigation(
        VisualElement root)
    {
        bottomNavigationController =
            new BottomNavigationController(
                root,
                BottomNavigationTab.Settings);
    }

    private void DisposeBottomNavigation()
    {
        if (bottomNavigationController == null)
        {
            return;
        }

        bottomNavigationController.Dispose();
        bottomNavigationController = null;
    }

    private void FindElements(VisualElement root)
    {
        profileNameLabel =
            root.Q<Label>("profile-name-label");

        profileEmailLabel =
            root.Q<Label>("profile-email-label");

        profileRoleLabel =
            root.Q<Label>("profile-role-label");

        sfxValueLabel =
            root.Q<Label>("sfx-value-label");

        bgmValueLabel =
            root.Q<Label>("bgm-value-label");

        editProfileButton =
            root.Q<Button>("edit-profile-button");

        userInformationButton =
            root.Q<Button>("user-information-button");

        englishButton =
            root.Q<Button>("english-button");

        vietnameseButton =
            root.Q<Button>("vietnamese-button");

        restoreButton =
            root.Q<Button>("restore-button");

        dismissButton =
            root.Q<Button>("dismiss-button");

        privacyButton =
            root.Q<Button>("privacy-button");

        logoutButton =
            root.Q<Button>("logout-button");

        darkModeToggle =
            root.Q<Toggle>("dark-mode-toggle");

        tutorialToggle =
            root.Q<Toggle>("tutorial-toggle");

        sfxSlider =
            root.Q<Slider>("sfx-slider");

        bgmSlider =
            root.Q<Slider>("bgm-slider");

        sfxSliderFill =
            root.Q<VisualElement>("sfx-slider-fill");

        bgmSliderFill =
            root.Q<VisualElement>("bgm-slider-fill");

        sessionRecoveryCard =
            root.Q<VisualElement>("session-recovery-card");
    }

    private void RegisterEvents()
    {
        if (editProfileButton != null)
        {
            editProfileButton.clicked +=
                OpenUserInformation;
        }

        if (userInformationButton != null)
        {
            userInformationButton.clicked +=
                OpenUserInformation;
        }

        if (englishButton != null)
        {
            englishButton.clicked +=
                SelectEnglish;
        }

        if (vietnameseButton != null)
        {
            vietnameseButton.clicked +=
                SelectVietnamese;
        }

        if (restoreButton != null)
        {
            restoreButton.clicked +=
                RestoreSession;
        }

        if (dismissButton != null)
        {
            dismissButton.clicked +=
                DismissRecovery;
        }

        if (privacyButton != null)
        {
            privacyButton.clicked +=
                OpenPrivacyPage;
        }

        if (logoutButton != null)
        {
            logoutButton.clicked +=
                Logout;
        }

        if (darkModeToggle != null)
        {
            darkModeToggle.RegisterValueChangedCallback(
                OnDarkModeChanged);
        }

        if (tutorialToggle != null)
        {
            tutorialToggle.RegisterValueChangedCallback(
                OnTutorialChanged);
        }

        if (sfxSlider != null)
        {
            sfxSlider.RegisterValueChangedCallback(
                OnSfxVolumeChanged);
        }

        if (bgmSlider != null)
        {
            bgmSlider.RegisterValueChangedCallback(
                OnBgmVolumeChanged);
        }
    }

    private void UnregisterEvents()
    {
        if (editProfileButton != null)
        {
            editProfileButton.clicked -=
                OpenUserInformation;
        }

        if (userInformationButton != null)
        {
            userInformationButton.clicked -=
                OpenUserInformation;
        }

        if (englishButton != null)
        {
            englishButton.clicked -=
                SelectEnglish;
        }

        if (vietnameseButton != null)
        {
            vietnameseButton.clicked -=
                SelectVietnamese;
        }

        if (restoreButton != null)
        {
            restoreButton.clicked -=
                RestoreSession;
        }

        if (dismissButton != null)
        {
            dismissButton.clicked -=
                DismissRecovery;
        }

        if (privacyButton != null)
        {
            privacyButton.clicked -=
                OpenPrivacyPage;
        }

        if (logoutButton != null)
        {
            logoutButton.clicked -=
                Logout;
        }

        if (darkModeToggle != null)
        {
            darkModeToggle.UnregisterValueChangedCallback(
                OnDarkModeChanged);
        }

        if (tutorialToggle != null)
        {
            tutorialToggle.UnregisterValueChangedCallback(
                OnTutorialChanged);
        }

        if (sfxSlider != null)
        {
            sfxSlider.UnregisterValueChangedCallback(
                OnSfxVolumeChanged);
        }

        if (bgmSlider != null)
        {
            bgmSlider.UnregisterValueChangedCallback(
                OnBgmVolumeChanged);
        }
    }

    private void LoadProfileInformation()
    {
        ApplyProfileToLabels(
            SupabaseSession.FullName,
            SupabaseSession.Email,
            SupabaseSession.Role
        );

        if (!SupabaseSession.IsLoggedIn)
        {
            Debug.LogWarning(
                "SettingsScene không tìm thấy phiên đăng nhập Supabase."
            );

            return;
        }

        StartCoroutine(
            SupabaseProfileService.GetCurrentProfile(
                profile =>
                {
                    ApplyProfileToLabels(
                        profile.full_name,
                        profile.email,
                        profile.role
                    );
                },
                error =>
                {
                    Debug.LogWarning(
                        "Không tải được profile mới nhất: " + error
                    );
                }
            )
        );
    }

    private void ApplyProfileToLabels(
        string fullName,
        string email,
        string role)
    {
        if (profileNameLabel != null)
        {
            profileNameLabel.text =
                string.IsNullOrWhiteSpace(fullName)
                    ? "User"
                    : fullName;
        }

        if (profileEmailLabel != null)
        {
            profileEmailLabel.text =
                string.IsNullOrWhiteSpace(email)
                    ? "user@hcmut.edu.vn"
                    : email;
        }

        if (profileRoleLabel != null)
        {
            profileRoleLabel.text =
                string.Equals(
                    role,
                    "teacher",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Teacher"
                    : "Student";
        }
    }

    private void LoadSavedSettings()
    {
        bool darkMode =
            PlayerPrefs.GetInt("dark_mode", 0) == 1;

        bool tutorialEnabled =
            PlayerPrefs.GetInt(
                "tutorial_enabled",
                1) == 1;

        float sfxVolume =
            Mathf.Clamp(
                PlayerPrefs.GetFloat(
                    "sfx_volume",
                    70f),
                0f,
                100f);

        float bgmVolume =
            Mathf.Clamp(
                PlayerPrefs.GetFloat(
                    "bgm_volume",
                    50f),
                0f,
                100f);

        string language =
            PlayerPrefs.GetString(
                "app_language",
                "EN");

        darkModeToggle?.SetValueWithoutNotify(
            darkMode);

        tutorialToggle?.SetValueWithoutNotify(
            tutorialEnabled);

        sfxSlider?.SetValueWithoutNotify(
            sfxVolume);

        bgmSlider?.SetValueWithoutNotify(
            bgmVolume);

        UpdateVolumeLabel(
            sfxValueLabel,
            sfxVolume);

        UpdateVolumeLabel(
            bgmValueLabel,
            bgmVolume);

        UpdateSliderFill(
            sfxSliderFill,
            sfxVolume);

        UpdateSliderFill(
            bgmSliderFill,
            bgmVolume);

        UpdateLanguageButtons(language);

        ApplyAudioMixerVolume(
            "SFXVolume",
            sfxVolume);

        ApplyAudioMixerVolume(
            "BGMVolume",
            bgmVolume);
    }

    private void OnDarkModeChanged(
        ChangeEvent<bool> evt)
    {
        PlayerPrefs.SetInt(
            "dark_mode",
            evt.newValue ? 1 : 0);

        PlayerPrefs.Save();
    }

    private void OnTutorialChanged(
        ChangeEvent<bool> evt)
    {
        PlayerPrefs.SetInt(
            "tutorial_enabled",
            evt.newValue ? 1 : 0);

        PlayerPrefs.Save();
    }

    private void OnSfxVolumeChanged(
        ChangeEvent<float> evt)
    {
        SaveVolume(
            "sfx_volume",
            "SFXVolume",
            sfxValueLabel,
            sfxSliderFill,
            evt.newValue);
    }

    private void OnBgmVolumeChanged(
        ChangeEvent<float> evt)
    {
        SaveVolume(
            "bgm_volume",
            "BGMVolume",
            bgmValueLabel,
            bgmSliderFill,
            evt.newValue);
    }

    private void SaveVolume(
        string playerPrefsKey,
        string mixerParameter,
        Label valueLabel,
        VisualElement sliderFill,
        float volume)
    {
        float safeVolume =
            Mathf.Clamp(
                volume,
                0f,
                100f);

        UpdateVolumeLabel(
            valueLabel,
            safeVolume);

        UpdateSliderFill(
            sliderFill,
            safeVolume);

        PlayerPrefs.SetFloat(
            playerPrefsKey,
            safeVolume);

        PlayerPrefs.Save();

        ApplyAudioMixerVolume(
            mixerParameter,
            safeVolume);
    }

    private static void UpdateVolumeLabel(
        Label label,
        float value)
    {
        if (label == null)
        {
            return;
        }

        label.text =
            $"{Mathf.RoundToInt(value)}%";
    }

    private static void UpdateSliderFill(
        VisualElement fill,
        float value)
    {
        if (fill == null)
        {
            return;
        }

        fill.style.width =
            Length.Percent(
                Mathf.Clamp(
                    value,
                    0f,
                    100f));
    }

    private void ApplyAudioMixerVolume(
        string exposedParameter,
        float percentage)
    {
        if (audioMixer == null)
        {
            return;
        }

        float normalizedValue =
            Mathf.Clamp(
                percentage / 100f,
                0.0001f,
                1f);

        float decibelValue =
            Mathf.Log10(
                normalizedValue) * 20f;

        audioMixer.SetFloat(
            exposedParameter,
            decibelValue);
    }

    private void SelectEnglish()
    {
        SetLanguage("EN");
    }

    private void SelectVietnamese()
    {
        SetLanguage("VI");
    }

    private void SetLanguage(string language)
    {
        string normalizedLanguage =
            string.Equals(
                language,
                "VI",
                StringComparison.OrdinalIgnoreCase)
                ? "VI"
                : "EN";

        PlayerPrefs.SetString(
            "app_language",
            normalizedLanguage);

        PlayerPrefs.Save();

        UpdateLanguageButtons(
            normalizedLanguage);
    }

    private void UpdateLanguageButtons(
        string language)
    {
        bool isEnglish =
            !string.Equals(
                language,
                "VI",
                StringComparison.OrdinalIgnoreCase);

        englishButton?.EnableInClassList(
            "language-button-active",
            isEnglish);

        vietnameseButton?.EnableInClassList(
            "language-button-active",
            !isEnglish);
    }

    private void UpdateRecoveryCardVisibility()
    {
        if (sessionRecoveryCard == null)
        {
            return;
        }

        bool hasRecoverySession =
            PlayerPrefs.GetInt(
                "has_recovery_session",
                1) == 1;

        sessionRecoveryCard.style.display =
            hasRecoverySession
                ? DisplayStyle.Flex
                : DisplayStyle.None;
    }

    private void RestoreSession()
    {
        PlayerPrefs.SetInt(
            "has_recovery_session",
            0);

        PlayerPrefs.Save();

        HideRecoveryCard();

        Debug.Log(
            "Đã khôi phục phiên học.");
    }

    private void DismissRecovery()
    {
        PlayerPrefs.SetInt(
            "has_recovery_session",
            0);

        PlayerPrefs.Save();

        HideRecoveryCard();
    }

    private void HideRecoveryCard()
    {
        if (sessionRecoveryCard != null)
        {
            sessionRecoveryCard.style.display =
                DisplayStyle.None;
        }
    }

    private void Logout()
    {
        SupabaseAuthService.SignOutLocally();

        /*
         * Xóa thêm các key cũ trong thời gian project
         * vẫn còn controller chưa chuyển sang SupabaseSession.
         */
        ClearLegacyAuthenticationKeys();

        Debug.Log(
            "Đã đăng xuất và xóa thông tin phiên người dùng."
        );

        LoadSceneSafely(HomeSceneName);
    }

    private static void ClearLegacyAuthenticationKeys()
    {
        string[] legacyKeys =
        {
            "current_user_id",
            "current_profile_id",
            "current_full_name",
            "current_username",
            "current_email",
            "current_role",
            "current_avatar_url",
            "auth_token",
            "supabase_access_token",
            "supabase_refresh_token",
            "pending_role",
            "exercise_role",
            "current_password"
        };

        foreach (string key in legacyKeys)
        {
            PlayerPrefs.DeleteKey(key);
        }

        PlayerPrefs.Save();
    }

    private void OpenUserInformation()
    {
        Debug.Log(
            "Mở trang User Information.");

        LoadSceneSafely(
            UserInfoSceneName);
    }

    private void OpenPrivacyPage()
    {
        LoadSceneSafely(
            PrivacySceneName);
    }

    private static void LoadSceneSafely(
        string sceneName)
    {
        if (Application.CanStreamedLevelBeLoaded(
                sceneName))
        {
            SceneManager.LoadScene(
                sceneName);
            return;
        }

        Debug.LogError(
            $"Không thể mở Scene '{sceneName}'. " +
            "Hãy kiểm tra tên Scene và thêm Scene vào Build Profiles.");
    }
}