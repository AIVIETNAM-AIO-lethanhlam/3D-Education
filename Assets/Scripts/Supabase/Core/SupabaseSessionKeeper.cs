using UnityEngine;

/// <summary>Checks the Supabase token periodically and when the app is resumed.</summary>
public class SupabaseSessionKeeper : MonoBehaviour
{
    private const float CheckIntervalSeconds = 30f;
    private float nextCheckTime;

    private void Update()
    {
        if (Time.unscaledTime < nextCheckTime) return;
        nextCheckTime = Time.unscaledTime + CheckIntervalSeconds;
        CheckNow();
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused) CheckNow();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) CheckNow();
    }

    private void CheckNow()
    {
        if (SupabaseTokenRefresher.NeedsRefresh)
            StartCoroutine(SupabaseTokenRefresher.EnsureFreshToken());
    }
}
