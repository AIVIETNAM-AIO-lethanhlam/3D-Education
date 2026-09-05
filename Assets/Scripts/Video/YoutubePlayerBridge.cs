using UnityEngine;

/// <summary>
/// Optional bridge for an embedded YouTube/WebView plugin.
/// Add a MonoBehaviour implementing this interface to ShowLessonUIDocument.
/// Without a bridge, the controller falls back to Application.OpenURL.
/// </summary>
public interface IYouTubePlayerBridge
{
    bool IsReady { get; }
    bool IsPlaying { get; }
    float CurrentTimeSeconds { get; }
    float DurationSeconds { get; }

    void Load(string youtubeUrl);
    void Play();
    void Pause();
    void Replay();
    void SetMuted(bool muted);
    void SetFullscreen(bool fullscreen);

    // Temporarily hide/show the embedded native YouTube WebView.
    // This is needed when UI Toolkit popups such as Lecture Slides
    // or Exercises are open, because a native WebView can otherwise
    // stay above the popup and continue receiving touches.
    void SetEmbeddedWebViewSuppressed(bool suppressed);
}