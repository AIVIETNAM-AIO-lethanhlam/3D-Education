using System;
using System.Globalization;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Networking;
using Gree.UnityWebView;

[RequireComponent(typeof(UIDocument))]
public sealed class UnityWebViewYouTubeBridge : MonoBehaviour, IYouTubePlayerBridge
{
    [Header("Hosted player page")]
    [Tooltip(
        "Public HTTPS URL of the hosted YouTube player page, for example: " +
        "https://YOUR-WORKER.workers.dev/youtube-player"
    )]
    [SerializeField] private string hostedPlayerPageUrl = string.Empty;

    [Header("UI Toolkit")]
    [Tooltip("The native WebView must match only the actual video box, not the wrapper that also contains Unity controls.")]
    [SerializeField] private string videoElementName = "video-section";

    [Header("YouTube")]
    // IMPORTANT:
    // YouTube controls are rendered inside a cross-origin iframe. On a narrow
    // mobile-sized player YouTube may move its native fullscreen button upward,
    // and Unity/USS cannot reposition that button. Disable native controls and
    // use ShowLessonScene's own replay / volume / fullscreen row instead.
    private const bool UseNativeYouTubeControls = true;

    [SerializeField] private float statePollingInterval = 0.4f;

    private UIDocument uiDocument;
    private VisualElement root;
    private VisualElement videoElement;
    private WebViewObject webViewObject;

    private bool isReady;
    private bool isPlaying;
    private bool isMuted;
    private bool isFullscreen;
    private bool playRequested;

    private float currentTimeSeconds;
    private float durationSeconds;
    private float nextPollingTime;
    private Coroutine loadPlayerCoroutine;
    private string pendingVideoId = string.Empty;

    // Prevent the native WebView from appearing full-screen before UI Toolkit
    // has finished laying out video-section.
    private bool allowEmbeddedWebViewVisibility;

    // UI Toolkit popups cannot render above a native Android WebView.
    // When a lesson modal (Lecture Slides / Exercises) is open, keep the
    // embedded YouTube WebView completely hidden so it cannot receive taps.
    private bool suppressEmbeddedWebView;

    public bool IsReady => isReady;
    public bool IsPlaying => isPlaying;
    public float CurrentTimeSeconds => currentTimeSeconds;
    public float DurationSeconds => durationSeconds;

    private void OnEnable()
    {
        uiDocument = GetComponent<UIDocument>();
        root = uiDocument != null ? uiDocument.rootVisualElement : null;

        if (root == null)
        {
            Debug.LogError("[UnityWebViewYouTubeBridge] UIDocument/rootVisualElement is missing.");
            return;
        }

        // IMPORTANT: target the actual 205px video box only.
        // Older scene instances may still have the serialized value "video-wrapper".
        // video-wrapper also contains the Unity control row below the video, which made
        // YouTube's own bottom controls/fullscreen icon appear vertically displaced.
        videoElement = root.Q<VisualElement>("video-section");

        if (videoElement == null && !string.IsNullOrWhiteSpace(videoElementName))
            videoElement = root.Q<VisualElement>(videoElementName);

        if (videoElement == null)
        {
            Debug.LogError(
                "[UnityWebViewYouTubeBridge] Cannot find 'video-section' in UXML."
            );
            return;
        }

        videoElement.RegisterCallback<GeometryChangedEvent>(
            HandleVideoGeometryChanged
        );
    }

    private void OnDisable()
    {
        if (videoElement != null)
        {
            videoElement.UnregisterCallback<GeometryChangedEvent>(
                HandleVideoGeometryChanged
            );
        }

        if (loadPlayerCoroutine != null)
        {
            StopCoroutine(loadPlayerCoroutine);
            loadPlayerCoroutine = null;
        }

        DestroyWebView();
    }

    private void Update()
    {
        if (webViewObject == null)
            return;

        if (!isFullscreen)
            UpdateWebViewMargins();

        if (!isReady || Time.unscaledTime < nextPollingTime)
            return;

        nextPollingTime =
            Time.unscaledTime + Mathf.Max(0.1f, statePollingInterval);

        EvaluateJavaScript(
            "if(window.sendPlayerInfo){window.sendPlayerInfo();}"
        );
    }

    public void Load(string youtubeUrl)
    {
        string videoId = ExtractYouTubeVideoId(youtubeUrl);

        if (string.IsNullOrWhiteSpace(videoId))
        {
            Debug.LogError("[UnityWebViewYouTubeBridge] Invalid YouTube URL: " + youtubeUrl);
            return;
        }

        if (!IsValidHostedPlayerUrl(hostedPlayerPageUrl))
        {
            Debug.LogError(
                "[UnityWebViewYouTubeBridge] Hosted Player Page URL is missing or invalid."
            );
            return;
        }

        ResetPlayerState();
        pendingVideoId = videoId;
        CreateWebViewIfNeeded();

        if (webViewObject == null)
            return;

        allowEmbeddedWebViewVisibility = false;
        webViewObject.SetVisibility(false);

        string origin = GetOrigin(hostedPlayerPageUrl);
        string separator = hostedPlayerPageUrl.Contains("?") ? "&" : "?";
        string playerUrl =
            hostedPlayerPageUrl.Trim() + separator +
            "v=" + Uri.EscapeDataString(videoId) +
            "&controls=" + (UseNativeYouTubeControls ? "1" : "0") +
            "&origin=" + Uri.EscapeDataString(origin) +
            "&widget_referrer=" + Uri.EscapeDataString(hostedPlayerPageUrl.Trim()) +
            "&cache=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Debug.Log("[UnityWebViewYouTubeBridge] Native YouTube controls enabled.");
        Debug.Log("[UnityWebViewYouTubeBridge] Loading HTTPS player: " + playerUrl);

        // Loading while hidden is safe. We only reveal the native WebView after
        // UI Toolkit has produced a real video-section rectangle.
        webViewObject.LoadURL(playerUrl);

        if (loadPlayerCoroutine != null)
            StopCoroutine(loadPlayerCoroutine);

        loadPlayerCoroutine = StartCoroutine(
            RevealEmbeddedWebViewWhenLayoutIsReady()
        );
    }

    public void Play()
    {
        playRequested = true;

        if (isReady)
            EvaluateJavaScript("window.unityPlay && window.unityPlay();");
    }

    public void Pause()
    {
        playRequested = false;

        if (isReady)
            EvaluateJavaScript("window.unityPause && window.unityPause();");
    }

    public void Replay()
    {
        currentTimeSeconds = 0f;
        playRequested = true;

        if (isReady)
            EvaluateJavaScript("window.unityReplay && window.unityReplay();");
    }

    public void SetMuted(bool muted)
    {
        isMuted = muted;

        if (isReady)
        {
            EvaluateJavaScript(
                muted
                    ? "window.unitySetMuted && window.unitySetMuted(true);"
                    : "window.unitySetMuted && window.unitySetMuted(false);"
            );
        }
    }

    /// <summary>
    /// Temporarily hides the embedded YouTube native WebView.
    /// Use this while a UI Toolkit modal is open because native Android
    /// WebViews render above UI Toolkit and would otherwise still receive taps.
    /// </summary>
    public void SetEmbeddedWebViewSuppressed(bool suppressed)
    {
        suppressEmbeddedWebView = suppressed;

        if (webViewObject == null)
            return;

        if (suppressed)
        {
            webViewObject.SetVisibility(false);
            return;
        }

        // Restore only the embedded lesson-video placement.
        // PDF/fullscreen mode manages its own native WebView rectangle.
        if (!isFullscreen)
            UpdateWebViewMargins();
    }

    public void SetFullscreen(bool fullscreen)
    {
        if (webViewObject == null)
            return;

        isFullscreen = fullscreen;

        if (fullscreen)
            webViewObject.SetMargins(0, 0, 0, 0);
        else
            UpdateWebViewMargins();

        EvaluateJavaScript(
            "window.unitySetWebFullscreenState && window.unitySetWebFullscreenState(" +
            (isFullscreen ? "true" : "false") +
            ");"
        );
    }

    private IEnumerator RevealEmbeddedWebViewWhenLayoutIsReady()
    {
        // UI Toolkit can need several frames after a scene change before worldBound
        // is trustworthy. Showing a native WebView earlier leaves its default
        // margins at 0,0,0,0, which covers the whole phone screen.
        const int maxFrames = 30;

        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null;

            if (webViewObject == null || videoElement == null || root == null)
                continue;

            Rect bounds = videoElement.worldBound;

            if (bounds.width <= 10f || bounds.height <= 10f)
                continue;

            float panelScale = 1f;
            if (root.panel != null)
                panelScale = Mathf.Max(0.01f, root.panel.scaledPixelsPerPoint);

            float pixelWidth = bounds.width * panelScale;
            float pixelHeight = bounds.height * panelScale;

            // Reject the temporary "whole panel" geometry that can occur while
            // UI Toolkit is still rebuilding the scene.
            if (pixelWidth >= Screen.width * 0.98f &&
                pixelHeight >= Screen.height * 0.90f)
            {
                continue;
            }

            allowEmbeddedWebViewVisibility = true;
            UpdateWebViewMargins();

            Debug.Log(
                "[UnityWebViewYouTubeBridge] Embedded video layout ready: " +
                bounds
            );

            loadPlayerCoroutine = null;
            yield break;
        }

        Debug.LogError(
            "[UnityWebViewYouTubeBridge] video-section never received a valid " +
            "embedded layout. WebView stays hidden instead of covering the screen."
        );

        allowEmbeddedWebViewVisibility = false;

        if (webViewObject != null)
            webViewObject.SetVisibility(false);

        loadPlayerCoroutine = null;
    }

    private void CreateWebViewIfNeeded()
    {
        if (webViewObject != null)
            return;

        GameObject webViewGameObject = new("YouTubeWebView");
        webViewGameObject.transform.SetParent(transform, false);

        webViewObject =
            webViewGameObject.AddComponent<WebViewObject>();

        webViewObject.Init(
            cb: HandleMessageFromJavaScript,
            err: message =>
                Debug.LogError(
                    "[UnityWebViewYouTubeBridge] WebView error: " +
                    message
                ),
            httpErr: message =>
                Debug.LogError(
                    "[UnityWebViewYouTubeBridge] HTTP error: " +
                    message
                ),
            ld: message =>
            {
                Debug.Log("[UnityWebViewYouTubeBridge] Hosted page loaded: " + message);
                InjectVideoIntoHostedPage();
            },
            started: message =>
                Debug.Log(
                    "[UnityWebViewYouTubeBridge] Loading started: " +
                    message
                ),
            transparent: false,
            zoom: false
        );

        webViewObject.SetScrollbarsVisibility(false);
        webViewObject.SetTextZoom(100);
        webViewObject.SetVisibility(false);
    }

    private void HandleMessageFromJavaScript(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (message == "youtube-ready")
        {
            isReady = true;
            nextPollingTime = 0f;

            Debug.Log(
                "[UnityWebViewYouTubeBridge] YouTube player is ready."
            );

            if (isMuted)
                SetMuted(true);

            if (playRequested)
                Play();

            return;
        }

        if (message.StartsWith(
            "youtube-state:",
            StringComparison.Ordinal
        ))
        {
            string value =
                message.Substring("youtube-state:".Length);

            if (int.TryParse(value, out int state))
            {
                isPlaying = state == 1;

                if (state == 0)
                    currentTimeSeconds = durationSeconds;
            }

            return;
        }

        if (message.StartsWith(
            "youtube-info:",
            StringComparison.Ordinal
        ))
        {
            ParsePlayerInformation(
                message.Substring("youtube-info:".Length)
            );
            return;
        }

        if (message == "youtube-autoplay-blocked")
        {
            playRequested = false;

            Debug.LogWarning(
                "[UnityWebViewYouTubeBridge] Autoplay was blocked. " +
                "Tap Play again."
            );
            return;
        }

        if (message == "youtube-toggle-fullscreen")
        {
            SetFullscreen(!isFullscreen);
            EvaluateJavaScript(
                "window.unitySetWebFullscreenState && window.unitySetWebFullscreenState(" +
                (isFullscreen ? "true" : "false") +
                ");"
            );
            return;
        }

        if (message.StartsWith(
            "youtube-error:",
            StringComparison.Ordinal
        ))
        {
            string errorCode = message.Substring("youtube-error:".Length);

            if (errorCode == "153")
            {
                Debug.LogError(
                    "[UnityWebViewYouTubeBridge] YouTube error 153: the player page " +
                    "was loaded without an HTTPS Referer. Do not use LoadHTML/data: or " +
                    "about:blank. Make sure youtube-player.html is served directly from " +
                    "the Hosted Player Page URL with Content-Type: text/html."
                );
            }
            else
            {
                Debug.LogError(
                    "[UnityWebViewYouTubeBridge] YouTube player error: " + errorCode
                );
            }
        }
    }

    private void ParsePlayerInformation(string rawValue)
    {
        string[] parts = rawValue.Split('|');

        if (parts.Length < 3)
            return;

        if (float.TryParse(
            parts[0],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float current
        ))
        {
            currentTimeSeconds = Mathf.Max(0f, current);
        }

        if (float.TryParse(
            parts[1],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float duration
        ))
        {
            durationSeconds = Mathf.Max(0f, duration);
        }

        if (int.TryParse(parts[2], out int state))
            isPlaying = state == 1;
    }

    private void EvaluateJavaScript(string javascript)
    {
        if (
            webViewObject == null ||
            string.IsNullOrWhiteSpace(javascript)
        )
        {
            return;
        }

        webViewObject.EvaluateJS(javascript);
    }

    private void InjectVideoIntoHostedPage()
    {
        if (webViewObject == null || string.IsNullOrWhiteSpace(pendingVideoId))
            return;

        string script =
            "window.unityLoadVideo && window.unityLoadVideo(" +
            ToJavaScriptString(pendingVideoId) + "," +
            (UseNativeYouTubeControls ? "1" : "0") + "," +
            ToJavaScriptString(GetOrigin(hostedPlayerPageUrl)) + "," +
            ToJavaScriptString(hostedPlayerPageUrl.Trim()) +
            ");";

        EvaluateJavaScript(script);
    }

    private static string ToJavaScriptString(string value)
    {
        value ??= string.Empty;
        return "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n") + "\"";
    }

    private static string GetOrigin(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            return string.Empty;

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private void DestroyWebView()
    {
        if (webViewObject == null)
            return;

        webViewObject.SetVisibility(false);
        Destroy(webViewObject.gameObject);
        webViewObject = null;

        ResetPlayerState();
    }

    private void HandleVideoGeometryChanged(
        GeometryChangedEvent evt
    )
    {
        UpdateWebViewMargins();
    }

    private void UpdateWebViewMargins()
    {
        if (
            webViewObject == null ||
            videoElement == null ||
            root == null ||
            isFullscreen
        )
        {
            return;
        }

        if (!allowEmbeddedWebViewVisibility)
        {
            webViewObject.SetVisibility(false);
            return;
        }

        if (suppressEmbeddedWebView)
        {
            webViewObject.SetVisibility(false);
            return;
        }

        Rect bounds = videoElement.worldBound;

        if (bounds.width <= 1f || bounds.height <= 1f)
        {
            webViewObject.SetVisibility(false);
            return;
        }

        // UI Toolkit worldBound is expressed in panel points. Convert it to native
        // screen pixels using the panel scale so the WebView exactly covers
        // video-section. Do NOT calculate from video-wrapper and subtract a guessed
        // control-row height; that caused YouTube's fullscreen/control bar to shift.
        float panelScale = 1f;
        if (root.panel != null)
            panelScale = Mathf.Max(0.01f, root.panel.scaledPixelsPerPoint);

        int left = Mathf.RoundToInt(bounds.xMin * panelScale);
        int top = Mathf.RoundToInt(bounds.yMin * panelScale);
        int right = Mathf.RoundToInt(Screen.width - bounds.xMax * panelScale);
        int bottom = Mathf.RoundToInt(Screen.height - bounds.yMax * panelScale);

        // Hide the native view when the video is completely outside the screen.
        if (bounds.xMax <= 0f || bounds.yMax <= 0f ||
            left >= Screen.width || top >= Screen.height ||
            right >= Screen.width || bottom >= Screen.height)
        {
            webViewObject.SetVisibility(false);
            return;
        }

        // Clamp partially visible edges while scrolling.
        left = Mathf.Clamp(left, 0, Screen.width);
        top = Mathf.Clamp(top, 0, Screen.height);
        right = Mathf.Clamp(right, 0, Screen.width);
        bottom = Mathf.Clamp(bottom, 0, Screen.height);

        webViewObject.SetMargins(left, top, right, bottom);
        webViewObject.SetVisibility(true);
    }

    private static string GetHostedOrigin(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri uri))
            return string.Empty;

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static bool IsValidHostedPlayerUrl(string value)
    {
        return
            !string.IsNullOrWhiteSpace(value) &&
            Uri.TryCreate(
                value.Trim(),
                UriKind.Absolute,
                out Uri uri
            ) &&
            string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static string ExtractYouTubeVideoId(
        string youtubeUrl
    )
    {
        if (string.IsNullOrWhiteSpace(youtubeUrl))
            return string.Empty;

        Match match = Regex.Match(
            youtubeUrl,
            @"(?:youtube\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/)|youtu\.be/)([A-Za-z0-9_-]{11})",
            RegexOptions.IgnoreCase
        );

        return
            match.Success
                ? match.Groups[1].Value
                : string.Empty;
    }

    private void ResetPlayerState()
    {
        isReady = false;
        isPlaying = false;
        isMuted = false;
        isFullscreen = false;
        playRequested = false;
        currentTimeSeconds = 0f;
        durationSeconds = 0f;
        nextPollingTime = 0f;
        pendingVideoId = string.Empty;
        allowEmbeddedWebViewVisibility = false;
        suppressEmbeddedWebView = false;
    }
}