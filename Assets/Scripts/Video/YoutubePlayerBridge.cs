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
}