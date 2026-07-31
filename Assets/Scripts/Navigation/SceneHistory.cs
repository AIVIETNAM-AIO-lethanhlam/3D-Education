using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lưu lịch sử các Scene trong phiên chạy ứng dụng và hỗ trợ quay lại Scene trước.
/// Thêm file này một lần vào thư mục Scripts dùng chung.
/// </summary>
public static class SceneHistory
{
    private static readonly Stack<string> SceneStack =
        new Stack<string>();

    private static string currentSceneName;
    private static bool isNavigatingBack;
    private static bool isInitialized;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (isInitialized)
        {
            return;
        }

        isInitialized = true;
        SceneStack.Clear();

        Scene activeScene = SceneManager.GetActiveScene();

        currentSceneName =
            activeScene.IsValid()
                ? activeScene.name
                : string.Empty;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(
        Scene loadedScene,
        LoadSceneMode loadMode)
    {
        string loadedSceneName = loadedScene.name;

        if (string.IsNullOrWhiteSpace(loadedSceneName))
        {
            return;
        }

        if (isNavigatingBack)
        {
            currentSceneName = loadedSceneName;
            isNavigatingBack = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(currentSceneName))
        {
            currentSceneName = loadedSceneName;
            return;
        }

        if (!string.Equals(
                currentSceneName,
                loadedSceneName,
                StringComparison.Ordinal))
        {
            SceneStack.Push(currentSceneName);
            currentSceneName = loadedSceneName;
        }
    }

    public static void GoBack(
        string fallbackSceneName = "MainHomeScene")
    {
        string activeSceneName =
            SceneManager.GetActiveScene().name;

        while (SceneStack.Count > 0)
        {
            string previousSceneName =
                SceneStack.Pop();

            if (string.IsNullOrWhiteSpace(previousSceneName))
            {
                continue;
            }

            if (string.Equals(
                    previousSceneName,
                    activeSceneName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!Application.CanStreamedLevelBeLoaded(
                    previousSceneName))
            {
                Debug.LogWarning(
                    $"Scene '{previousSceneName}' chưa được thêm vào Build Profiles.");
                continue;
            }

            isNavigatingBack = true;

            Debug.Log(
                $"SceneHistory: {activeSceneName} -> {previousSceneName}");

            SceneManager.LoadScene(previousSceneName);
            return;
        }

        LoadFallbackScene(
            activeSceneName,
            fallbackSceneName);
    }

    public static void Clear()
    {
        SceneStack.Clear();
        currentSceneName =
            SceneManager.GetActiveScene().name;
        isNavigatingBack = false;
    }

    private static void LoadFallbackScene(
        string activeSceneName,
        string fallbackSceneName)
    {
        if (string.IsNullOrWhiteSpace(fallbackSceneName))
        {
            Debug.LogWarning(
                "Không có Scene trước đó và fallback Scene đang rỗng.");
            return;
        }

        if (string.Equals(
                activeSceneName,
                fallbackSceneName,
                StringComparison.Ordinal))
        {
            Debug.LogWarning(
                "Không có Scene trước đó để quay lại.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(
                fallbackSceneName))
        {
            Debug.LogError(
                $"Fallback Scene '{fallbackSceneName}' chưa được thêm vào Build Profiles.");
            return;
        }

        isNavigatingBack = true;

        Debug.Log(
            $"Không có lịch sử Scene. Quay về fallback: {fallbackSceneName}");

        SceneManager.LoadScene(fallbackSceneName);
    }
}