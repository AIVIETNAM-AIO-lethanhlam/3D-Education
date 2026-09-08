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

    [Header("Android Media Volume")]
    [SerializeField, Min(0.1f)]
    private float systemVolumePollInterval = 0.25f;

    private float nextSystemVolumePollTime;
    private bool updatingVolumeUiFromSystem;

    private GeneralHeaderController headerController;
    private BottomNavigationController bottomNavigationController;

    private Label profileNameLabel;
    private Label profileEmailLabel;
    private Label profileRoleLabel;
    private Label sfxValueLabel;
    private Label bgmValueLabel;

    // Localizable labels
    private Label accountTitleLabel;
    private Label userInformationTitleLabel;
    private Label userInformationDescriptionLabel;
    private Label appearanceTitleLabel;
    private Label languageLabel;
    private Label audioTitleLabel;
    private Label sfxTitleLabel;
    private Label bgmTitleLabel;
    private Label recoveryTitleLabel;
    private Label recoveryDescriptionLabel;
    private Label privacyTitleLabel;
    private Label logoutLabel;
    private Label homeNavLabel;
    private Label myNavLabel;
    private Label aiNavLabel;
    private Label settingsNavLabel;

    private Button editProfileButton;
    private Button userInformationButton;
    private Button englishButton;
    private Button vietnameseButton;
    private Button restoreButton;
    private Button dismissButton;
    private Button privacyButton;
    private Button logoutButton;


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

        AppLanguageManager.LanguageChanged += OnLanguageChanged;

        LoadProfileInformation();
        LoadSavedSettings();
        UpdateRecoveryCardVisibility();
        RegisterEvents();
        ApplyCurrentLanguage();
    }

    private void OnDisable()
    {
        AppLanguageManager.LanguageChanged -= OnLanguageChanged;

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

        accountTitleLabel =
            root.Q<Label>("account-title-label");

        userInformationTitleLabel =
            root.Q<Label>("user-information-title-label");

        userInformationDescriptionLabel =
            root.Q<Label>("user-information-description-label");

        appearanceTitleLabel =
            root.Q<Label>("appearance-title-label");

        languageLabel =
            root.Q<Label>("language-label");

        audioTitleLabel =
            root.Q<Label>("audio-title-label");

        sfxTitleLabel =
            root.Q<Label>("sfx-title-label");

        bgmTitleLabel =
            root.Q<Label>("bgm-title-label");
recoveryTitleLabel =
            root.Q<Label>("recovery-title-label");

        recoveryDescriptionLabel =
            root.Q<Label>("recovery-description-label");

        privacyTitleLabel =
            root.Q<Label>("privacy-title-label");

        logoutLabel =
            root.Q<Label>("logout-label");

        homeNavLabel =
            root.Q<Label>("home-nav-label");

        myNavLabel =
            root.Q<Label>("my-nav-label");

        aiNavLabel =
            root.Q<Label>("ai-nav-label");

        settingsNavLabel =
            root.Q<Label>("settings-nav-label");

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
            bool isTeacher =
                string.Equals(
                    role,
                    "teacher",
                    StringComparison.OrdinalIgnoreCase);

            if (AppLanguageManager.IsVietnamese)
            {
                profileRoleLabel.text =
                    isTeacher
                        ? "Giáo viên"
                        : "Học sinh";
            }
            else
            {
                profileRoleLabel.text =
                    isTeacher
                        ? "Teacher"
                        : "Student";
            }
        }
    }

    private void LoadSavedSettings()
    {
// Slider 1 = Android media/video volume.
        // On a real Android device it starts from the CURRENT phone
        // STREAM_MUSIC volume, so it matches the hardware volume keys.
        float sfxVolume =
            GetSystemMediaVolumePercent();

        // Slider 2 = Unity/app audio multiplier.
        // Actual output is still affected by the phone media volume above.
        float bgmVolume =
            Mathf.Clamp(
                PlayerPrefs.GetFloat(
                    "app_audio_volume",
                    100f),
                0f,
                100f);

        string language =
            PlayerPrefs.GetString(
                "app_language",
                "EN");
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

        ApplyAppAudioVolume(bgmVolume);

        nextSystemVolumePollTime =
            Time.unscaledTime + systemVolumePollInterval;
    }
private void OnSfxVolumeChanged(
        ChangeEvent<float> evt)
    {
        if (updatingVolumeUiFromSystem)
            return;

        float safeVolume =
            Mathf.Clamp(evt.newValue, 0f, 100f);

        UpdateVolumeLabel(
            sfxValueLabel,
            safeVolume);

        UpdateSliderFill(
            sfxSliderFill,
            safeVolume);

        SetSystemMediaVolumePercent(
            safeVolume);

        // Keep a fallback value for Unity Editor / non-Android testing.
        PlayerPrefs.SetFloat(
            "video_volume_fallback",
            safeVolume);

        PlayerPrefs.Save();
    }

    private void OnBgmVolumeChanged(
        ChangeEvent<float> evt)
    {
        float safeVolume =
            Mathf.Clamp(evt.newValue, 0f, 100f);

        UpdateVolumeLabel(
            bgmValueLabel,
            safeVolume);

        UpdateSliderFill(
            bgmSliderFill,
            safeVolume);

        PlayerPrefs.SetFloat(
            "app_audio_volume",
            safeVolume);

        PlayerPrefs.Save();

        ApplyAppAudioVolume(
            safeVolume);
    }

    private void Update()
    {
        // Keep the Video Volume slider synchronized when the user presses
        // the phone's physical volume buttons while SettingsScene is open.
        if (Time.unscaledTime < nextSystemVolumePollTime)
            return;

        nextSystemVolumePollTime =
            Time.unscaledTime + systemVolumePollInterval;

#if UNITY_ANDROID && !UNITY_EDITOR
        SyncVideoVolumeFromPhone();
#endif
    }

    private void SyncVideoVolumeFromPhone()
    {
        float phoneVolume =
            GetSystemMediaVolumePercent();

        if (sfxSlider == null)
            return;

        if (Mathf.Abs(
                sfxSlider.value - phoneVolume) < 0.5f)
        {
            return;
        }

        updatingVolumeUiFromSystem = true;

        sfxSlider.SetValueWithoutNotify(
            phoneVolume);

        UpdateVolumeLabel(
            sfxValueLabel,
            phoneVolume);

        UpdateSliderFill(
            sfxSliderFill,
            phoneVolume);

        updatingVolumeUiFromSystem = false;
    }

    private float GetSystemMediaVolumePercent()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using AndroidJavaClass unityPlayer =
                new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer"
                );

            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>(
                    "currentActivity"
                );

            using AndroidJavaObject audioManager =
                activity.Call<AndroidJavaObject>(
                    "getSystemService",
                    "audio"
                );

            using AndroidJavaClass audioManagerClass =
                new AndroidJavaClass(
                    "android.media.AudioManager"
                );

            int streamMusic =
                audioManagerClass.GetStatic<int>(
                    "STREAM_MUSIC"
                );

            int current =
                audioManager.Call<int>(
                    "getStreamVolume",
                    streamMusic
                );

            int maximum =
                audioManager.Call<int>(
                    "getStreamMaxVolume",
                    streamMusic
                );

            if (maximum <= 0)
                return 0f;

            return Mathf.Clamp(
                current * 100f / maximum,
                0f,
                100f
            );
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Không đọc được âm lượng media của Android: " +
                exception.Message
            );
        }
#endif

        return Mathf.Clamp(
            PlayerPrefs.GetFloat(
                "video_volume_fallback",
                70f
            ),
            0f,
            100f
        );
    }

    private void SetSystemMediaVolumePercent(
        float percentage)
    {
        float safePercentage =
            Mathf.Clamp(
                percentage,
                0f,
                100f
            );

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using AndroidJavaClass unityPlayer =
                new AndroidJavaClass(
                    "com.unity3d.player.UnityPlayer"
                );

            using AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>(
                    "currentActivity"
                );

            using AndroidJavaObject audioManager =
                activity.Call<AndroidJavaObject>(
                    "getSystemService",
                    "audio"
                );

            using AndroidJavaClass audioManagerClass =
                new AndroidJavaClass(
                    "android.media.AudioManager"
                );

            int streamMusic =
                audioManagerClass.GetStatic<int>(
                    "STREAM_MUSIC"
                );

            int maximum =
                audioManager.Call<int>(
                    "getStreamMaxVolume",
                    streamMusic
                );

            int target =
                Mathf.RoundToInt(
                    maximum *
                    safePercentage /
                    100f
                );

            audioManager.Call(
                "setStreamVolume",
                streamMusic,
                target,
                0
            );

            return;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Không chỉnh được âm lượng media Android: " +
                exception.Message
            );
        }
#endif

        PlayerPrefs.SetFloat(
            "video_volume_fallback",
            safePercentage
        );
    }

    private void ApplyAppAudioVolume(
        float percentage)
    {
        float safePercentage =
            Mathf.Clamp(
                percentage,
                0f,
                100f
            );

        // Controls ordinary Unity AudioSources.
        AudioListener.volume =
            safePercentage / 100f;

        // If the project has the optional mixer parameters configured,
        // keep them synchronized too.
        ApplyAudioMixerVolume(
            "SFXVolume",
            safePercentage
        );

        ApplyAudioMixerVolume(
            "BGMVolume",
            safePercentage
        );
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
        AppLanguageManager.SetLanguage(language);
    }

    private void OnLanguageChanged(string language)
    {
        ApplyCurrentLanguage();
    }

    private void ApplyCurrentLanguage()
    {
        bool vi =
            AppLanguageManager.IsVietnamese;

        // Header
        headerController?.ConfigurePage(
            title: vi ? "Cài đặt" : "Settings",
            subtitle: null,
            showBackButton: true,
            showSubtitleIcon: false);

        SetLabelText(
            accountTitleLabel,
            vi ? "TÀI KHOẢN" : "ACCOUNT");

        SetLabelText(
            userInformationTitleLabel,
            vi ? "Thông tin người dùng" : "User Information");

        SetLabelText(
            userInformationDescriptionLabel,
            vi ? "Chỉnh sửa tên, email, mật khẩu" : "Edit name, email, password");

        SetLabelText(
            appearanceTitleLabel,
            vi ? "NGÔN NGỮ" : "LANGUAGE");

        SetLabelText(
            languageLabel,
            vi ? "Ngôn ngữ" : "Language");

        SetLabelText(
            audioTitleLabel,
            vi ? "ÂM THANH" : "AUDIO");

        SetLabelText(
            sfxTitleLabel,
            vi ? "Âm lượng video" : "Video Volume");

        SetLabelText(
            bgmTitleLabel,
            vi ? "Âm lượng ứng dụng" : "App Volume");
SetLabelText(
            recoveryTitleLabel,
            vi ? "Khôi phục phiên" : "Session Recovery");

        SetLabelText(
            recoveryDescriptionLabel,
            vi
                ? "Phát hiện phiên chưa lưu từ 2 giờ trước. Khôi phục?"
                : "Unsaved session from 2h ago detected. Restore it?");

        if (restoreButton != null)
            restoreButton.text =
                vi ? "Khôi phục" : "Restore";

        if (dismissButton != null)
            dismissButton.text =
                vi ? "Bỏ qua" : "Dismiss";

        SetLabelText(
            privacyTitleLabel,
            vi ? "Quyền riêng tư & Điều khoản" : "Privacy & Terms");

        SetLabelText(
            logoutLabel,
            vi ? "Đăng xuất" : "Logout");

        SetLabelText(
            homeNavLabel,
            vi ? "Trang chủ" : "Home");

        SetLabelText(
            myNavLabel,
            vi ? "Của tôi" : "My");

        SetLabelText(
            aiNavLabel,
            "AI");

        SetLabelText(
            settingsNavLabel,
            vi ? "Cài đặt" : "Settings");

        UpdateLanguageButtons(
            AppLanguageManager.CurrentLanguage);

        // Re-render role label in the selected language.
        ApplyProfileToLabels(
            SupabaseSession.FullName,
            SupabaseSession.Email,
            SupabaseSession.Role);
    }

    private static void SetLabelText(
        Label label,
        string value)
    {
        if (label != null)
            label.text = value;
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