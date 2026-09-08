using System;
using System.Collections.Concurrent;
using UnityEngine;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Android SpeechRecognizer bridge for Unity.
/// Converts live microphone speech to text without storing an audio file.
/// Supports partial and final recognition results.
/// </summary>
public class AndroidSpeechRecognizer : MonoBehaviour
{
    public event Action<string> PartialResultReceived;
    public event Action<string> FinalResultReceived;
    public event Action<bool> ListeningStateChanged;
    public event Action<string> ErrorReceived;

    public bool IsListening { get; private set; }

    private readonly ConcurrentQueue<Action> mainThreadActions = new();

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject speechRecognizer;
    private AndroidJavaObject recognizerIntent;
    private AndroidRecognitionListener recognitionListener;
    private PermissionCallbacks permissionCallbacks;
    private string pendingLanguageCode = "vi-VN";
#endif

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out Action action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    public void StartListening(string languageCode)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        pendingLanguageCode = string.IsNullOrWhiteSpace(languageCode)
            ? "vi-VN"
            : languageCode.Trim();

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            RequestMicrophonePermission();
            return;
        }

        StartAndroidRecognizer();
#else
        QueueError(
            AppLanguageManager.IsVietnamese
                ? "Nhận diện giọng nói hiện chỉ được bật trên bản Android."
                : "Speech recognition is currently enabled only in the Android build."
        );
#endif
    }

    public void StopListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer == null || !IsListening)
            return;

        try
        {
            speechRecognizer.Call("stopListening");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Android SpeechRecognizer stopListening failed: " +
                exception.Message
            );

            SetListening(false);
        }
#else
        SetListening(false);
#endif
    }

    public void CancelListening()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (speechRecognizer != null)
        {
            try
            {
                speechRecognizer.Call("cancel");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Android SpeechRecognizer cancel failed: " +
                    exception.Message
                );
            }
        }
#endif

        SetListening(false);
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void RequestMicrophonePermission()
    {
        permissionCallbacks = new PermissionCallbacks();

        permissionCallbacks.PermissionGranted += permissionName =>
        {
            if (permissionName == Permission.Microphone)
                mainThreadActions.Enqueue(StartAndroidRecognizer);
        };

        permissionCallbacks.PermissionDenied += permissionName =>
        {
            if (permissionName == Permission.Microphone)
            {
                QueueError(
                    AppLanguageManager.IsVietnamese
                        ? "Bạn cần cấp quyền microphone để nhập mô tả bằng giọng nói."
                        : "Microphone permission is required for voice input."
                );
            }
        };

        permissionCallbacks.PermissionDeniedAndDontAskAgain += permissionName =>
        {
            if (permissionName == Permission.Microphone)
            {
                QueueError(
                    AppLanguageManager.IsVietnamese
                        ? "Quyền microphone đã bị chặn. Hãy bật Microphone trong Cài đặt ứng dụng."
                        : "Microphone permission is blocked. Enable it in the app settings."
                );
            }
        };

        Permission.RequestUserPermission(
            Permission.Microphone,
            permissionCallbacks
        );
    }

    private void StartAndroidRecognizer()
    {
        try
        {
            using AndroidJavaClass unityPlayer =
                new AndroidJavaClass("com.unity3d.player.UnityPlayer");

            AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            if (activity == null)
            {
                QueueError(
                    AppLanguageManager.IsVietnamese
                        ? "Không tìm thấy Android Activity để mở nhận diện giọng nói."
                        : "Android activity is unavailable for speech recognition."
                );
                return;
            }

            activity.Call(
                "runOnUiThread",
                new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using AndroidJavaClass recognizerClass =
                            new AndroidJavaClass("android.speech.SpeechRecognizer");

                        bool recognitionAvailable =
                            recognizerClass.CallStatic<bool>(
                                "isRecognitionAvailable",
                                activity
                            );

                        if (!recognitionAvailable)
                        {
                            QueueError(
                                AppLanguageManager.IsVietnamese
                                    ? "Thiết bị không có dịch vụ nhận diện giọng nói."
                                    : "Speech recognition service is not available on this device."
                            );
                            return;
                        }

                        DestroyAndroidRecognizerOnUiThread();

                        speechRecognizer =
                            recognizerClass.CallStatic<AndroidJavaObject>(
                                "createSpeechRecognizer",
                                activity
                            );

                        recognitionListener =
                            new AndroidRecognitionListener(this);

                        speechRecognizer.Call(
                            "setRecognitionListener",
                            recognitionListener
                        );

                        using AndroidJavaClass intentClass =
                            new AndroidJavaClass("android.content.Intent");

                        using AndroidJavaClass recognizerIntentClass =
                            new AndroidJavaClass("android.speech.RecognizerIntent");

                        string actionRecognizeSpeech =
                            recognizerIntentClass.GetStatic<string>(
                                "ACTION_RECOGNIZE_SPEECH"
                            );

                        recognizerIntent =
                            new AndroidJavaObject(
                                "android.content.Intent",
                                actionRecognizeSpeech
                            );

                        string languageModelKey =
                            recognizerIntentClass.GetStatic<string>(
                                "EXTRA_LANGUAGE_MODEL"
                            );

                        string freeFormModel =
                            recognizerIntentClass.GetStatic<string>(
                                "LANGUAGE_MODEL_FREE_FORM"
                            );

                        string languageKey =
                            recognizerIntentClass.GetStatic<string>(
                                "EXTRA_LANGUAGE"
                            );

                        string partialKey =
                            recognizerIntentClass.GetStatic<string>(
                                "EXTRA_PARTIAL_RESULTS"
                            );

                        string maxResultsKey =
                            recognizerIntentClass.GetStatic<string>(
                                "EXTRA_MAX_RESULTS"
                            );

                        recognizerIntent.Call<AndroidJavaObject>(
                            "putExtra",
                            languageModelKey,
                            freeFormModel
                        );

                        recognizerIntent.Call<AndroidJavaObject>(
                            "putExtra",
                            languageKey,
                            pendingLanguageCode
                        );

                        recognizerIntent.Call<AndroidJavaObject>(
                            "putExtra",
                            partialKey,
                            true
                        );

                        recognizerIntent.Call<AndroidJavaObject>(
                            "putExtra",
                            maxResultsKey,
                            3
                        );

                        speechRecognizer.Call(
                            "startListening",
                            recognizerIntent
                        );

                        SetListening(true);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            "Android SpeechRecognizer start failed: " +
                            exception
                        );

                        QueueError(
                            AppLanguageManager.IsVietnamese
                                ? "Không thể bắt đầu nhận diện giọng nói."
                                : "Unable to start speech recognition."
                        );
                    }
                })
            );
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Android SpeechRecognizer initialization failed: " +
                exception
            );

            QueueError(
                AppLanguageManager.IsVietnamese
                    ? "Không thể khởi tạo nhận diện giọng nói."
                    : "Unable to initialize speech recognition."
            );
        }
    }

    private void DestroyAndroidRecognizerOnUiThread()
    {
        if (speechRecognizer != null)
        {
            try
            {
                speechRecognizer.Call("destroy");
            }
            catch
            {
                // Ignore cleanup errors.
            }

            speechRecognizer.Dispose();
            speechRecognizer = null;
        }

        recognizerIntent?.Dispose();
        recognizerIntent = null;
        recognitionListener = null;
    }

    private string ExtractBestResult(AndroidJavaObject bundle)
    {
        if (bundle == null)
            return string.Empty;

        try
        {
            using AndroidJavaClass recognizerClass =
                new AndroidJavaClass("android.speech.SpeechRecognizer");

            string resultsKey =
                recognizerClass.GetStatic<string>(
                    "RESULTS_RECOGNITION"
                );

            using AndroidJavaObject results =
                bundle.Call<AndroidJavaObject>(
                    "getStringArrayList",
                    resultsKey
                );

            if (results == null)
                return string.Empty;

            int count = results.Call<int>("size");

            if (count <= 0)
                return string.Empty;

            return results.Call<string>("get", 0) ?? string.Empty;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Could not extract speech result: " +
                exception.Message
            );

            return string.Empty;
        }
    }

    private string TranslateAndroidError(int errorCode)
    {
        bool vi = AppLanguageManager.IsVietnamese;

        return errorCode switch
        {
            1 => vi ? "Lỗi mạng khi nhận diện giọng nói." : "Speech recognition network timeout.",
            2 => vi ? "Không thể kết nối mạng để nhận diện giọng nói." : "Speech recognition network error.",
            3 => vi ? "Lỗi microphone." : "Audio recording error.",
            4 => vi ? "Dịch vụ nhận diện giọng nói đang bận." : "Speech recognition server error.",
            5 => vi ? "Dịch vụ nhận diện giọng nói gặp lỗi." : "Speech recognition client error.",
            6 => vi ? "Không nghe thấy giọng nói. Hãy thử lại." : "No speech detected. Please try again.",
            7 => vi ? "Không nhận diện được nội dung. Hãy nói rõ hơn." : "No speech match. Please speak more clearly.",
            8 => vi ? "Dịch vụ nhận diện giọng nói đang bận." : "Speech recognizer is busy.",
            9 => vi ? "Ứng dụng chưa được cấp quyền microphone." : "Microphone permission is missing.",
            _ => vi ? $"Nhận diện giọng nói thất bại (mã {errorCode})." : $"Speech recognition failed (code {errorCode})."
        };
    }

    private sealed class AndroidRecognitionListener : AndroidJavaProxy
    {
        private readonly AndroidSpeechRecognizer owner;

        public AndroidRecognitionListener(
            AndroidSpeechRecognizer owner)
            : base("android.speech.RecognitionListener")
        {
            this.owner = owner;
        }

        public void onReadyForSpeech(AndroidJavaObject parameters)
        {
            owner.SetListening(true);
        }

        public void onBeginningOfSpeech()
        {
            owner.SetListening(true);
        }

        public void onRmsChanged(float rmsDb)
        {
        }

        public void onBufferReceived(byte[] buffer)
        {
        }

        public void onEndOfSpeech()
        {
            // Keep the UI in processing state until results/error arrives.
        }

        public void onError(int error)
        {
            owner.SetListening(false);
            owner.QueueError(owner.TranslateAndroidError(error));
        }

        public void onResults(AndroidJavaObject results)
        {
            string text = owner.ExtractBestResult(results);
            owner.SetListening(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                owner.QueueError(
                    AppLanguageManager.IsVietnamese
                        ? "Không nhận diện được nội dung. Hãy thử lại."
                        : "No speech was recognized. Please try again."
                );
                return;
            }

            owner.mainThreadActions.Enqueue(
                () => owner.FinalResultReceived?.Invoke(text)
            );
        }

        public void onPartialResults(AndroidJavaObject partialResults)
        {
            string text = owner.ExtractBestResult(partialResults);

            if (!string.IsNullOrWhiteSpace(text))
            {
                owner.mainThreadActions.Enqueue(
                    () => owner.PartialResultReceived?.Invoke(text)
                );
            }
        }

        public void onEvent(
            int eventType,
            AndroidJavaObject parameters)
        {
        }
    }
#endif

    private void SetListening(bool value)
    {
        if (IsListening == value)
            return;

        IsListening = value;

        mainThreadActions.Enqueue(
            () => ListeningStateChanged?.Invoke(value)
        );
    }

    private void QueueError(string message)
    {
        SetListening(false);

        mainThreadActions.Enqueue(
            () => ErrorReceived?.Invoke(message)
        );
    }

    private void OnDestroy()
    {
        CancelListening();

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using AndroidJavaClass unityPlayer =
                new AndroidJavaClass("com.unity3d.player.UnityPlayer");

            AndroidJavaObject activity =
                unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            activity?.Call(
                "runOnUiThread",
                new AndroidJavaRunnable(DestroyAndroidRecognizerOnUiThread)
            );
        }
        catch
        {
            // Ignore cleanup errors during shutdown.
        }
#endif
    }
}
