#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor helper: saves the current Game / Simulator view (Play Mode) as a PNG
/// in the project's "Screenshots" folder. Menu: 3D Education > Capture Screenshot (Ctrl+Shift+F12).
/// </summary>
public static class GameViewScreenshotCapture
{
    [MenuItem("3D Education/Capture Screenshot %#F12")]
    public static void Capture()
    {
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        ScreenCapture.CaptureScreenshot(file);
        Debug.Log("[Screenshot] Saved: " + file);
    }
}
#endif
