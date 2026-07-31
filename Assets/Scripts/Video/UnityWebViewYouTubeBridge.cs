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
        "Public HTTPS URL of youtube-player.html, for example: " +
        "https://YOUR_PROJECT.supabase.co/storage/v1/object/public/web-player/youtube-player.html"
    )]
    [SerializeField] private string hostedPlayerPageUrl = string.Empty;

    [Header("UI Toolkit")]
    [SerializeField] private string videoElementName = "video-wrapper";
    [SerializeField] private float bottomControlInset = 40f;
    [SerializeField] private float horizontalInset = 0f;
    [SerializeField] private float topInset = 0f;

    [Header("YouTube")]
    [SerializeField] private bool showNativeYouTubeControls = true;
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

        videoElement = root.Q<VisualElement>(videoElementName);

        if (videoElement == null)
        {
            Debug.LogError(
                $"[UnityWebViewYouTubeBridge] Cannot find '{videoElementName}' in UXML."
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

        UpdateWebViewMargins();

        string origin = GetOrigin(hostedPlayerPageUrl);
        string separator = hostedPlayerPageUrl.Contains("?") ? "&" : "?";
        string playerUrl =
            hostedPlayerPageUrl.Trim() + separator +
            "v=" + Uri.EscapeDataString(videoId) +
            "&controls=" + (showNativeYouTubeControls ? "1" : "0") +
            "&origin=" + Uri.EscapeDataString(origin) +
            "&widget_referrer=" + Uri.EscapeDataString(hostedPlayerPageUrl.Trim()) +
            "&cache=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Debug.Log("[UnityWebViewYouTubeBridge] Loading HTTPS player: " + playerUrl);
        webViewObject.LoadURL(playerUrl);
        webViewObject.SetVisibility(true);
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

    public void SetFullscreen(bool fullscreen)
    {
        if (webViewObject == null)
            return;

        isFullscreen = fullscreen;

        if (fullscreen)
            webViewObject.SetMargins(0, 0, 0, 0);
        else
            UpdateWebViewMargins();
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
            (showNativeYouTubeControls ? "1" : "0") + "," +
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

        float rootWidth = root.resolvedStyle.width;
        float rootHeight = root.resolvedStyle.height;

        if (rootWidth <= 0f || rootHeight <= 0f)
            return;

        Rect bounds = videoElement.worldBound;

        float scaleX = Screen.width / rootWidth;
        float scaleY = Screen.height / rootHeight;

        int left = Mathf.RoundToInt(
            Mathf.Max(
                0f,
                (bounds.xMin + horizontalInset) * scaleX
            )
        );

        int top = Mathf.RoundToInt(
            Mathf.Max(
                0f,
                (bounds.yMin + topInset) * scaleY
            )
        );

        float visibleWidth = Mathf.Max(
            1f,
            bounds.width - horizontalInset * 2f
        );

        float visibleHeight = Mathf.Max(
            1f,
            bounds.height - topInset - bottomControlInset
        );

        int right = Mathf.RoundToInt(
            Mathf.Max(
                0f,
                Screen.width - left - visibleWidth * scaleX
            )
        );

        int bottom = Mathf.RoundToInt(
            Mathf.Max(
                0f,
                Screen.height - top - visibleHeight * scaleY
            )
        );

        webViewObject.SetMargins(left, top, right, bottom);
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
    }
}