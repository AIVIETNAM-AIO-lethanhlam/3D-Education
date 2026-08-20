using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneHistory
{
    private const int MaxHistorySize = 40;

    private static readonly Stack<string> backStack = new();
    private static bool initialized;
    private static string currentSceneName = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RuntimeInitialize()
    {
        Initialize();
    }

    private static void Initialize()
    {
        if (initialized)
            return;

        initialized = true;

        Scene activeScene = SceneManager.GetActiveScene();
        currentSceneName = activeScene.IsValid()
            ? activeScene.name
            : string.Empty;

        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    public static bool LoadScene(string sceneName)
    {
        Initialize();

        sceneName = Normalize(sceneName);

        if (!CanLoad(sceneName))
            return false;

        string activeScene = SceneManager.GetActiveScene().name;

        if (string.Equals(
                activeScene,
                sceneName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Push(activeScene);
        currentSceneName = sceneName;

        SceneManager.LoadScene(sceneName);
        return true;
    }

    public static bool GoBack(string fallbackScene = "MainHomeScene")
    {
        Initialize();

        string activeScene = SceneManager.GetActiveScene().name;

        while (backStack.Count > 0)
        {
            string targetScene = backStack.Pop();

            if (string.IsNullOrWhiteSpace(targetScene))
                continue;

            if (string.Equals(
                    targetScene,
                    activeScene,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Application.CanStreamedLevelBeLoaded(targetScene))
            {
                Debug.LogWarning(
                    $"[SceneHistory] Skip {targetScene}: Scene chưa có trong Build Profiles."
                );
                continue;
            }

            currentSceneName = targetScene;

            Debug.Log(
                $"[SceneHistory] Back {activeScene} -> {targetScene}. " +
                $"Remaining: {DebugStack()}"
            );

            SceneManager.LoadScene(targetScene);
            return true;
        }

        fallbackScene = Normalize(fallbackScene);

        if (string.IsNullOrWhiteSpace(fallbackScene) ||
            string.Equals(
                fallbackScene,
                activeScene,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(fallbackScene))
        {
            Debug.LogWarning(
                $"[SceneHistory] Fallback {fallbackScene} chưa có trong Build Profiles."
            );
            return false;
        }

        currentSceneName = fallbackScene;
        SceneManager.LoadScene(fallbackScene);
        return true;
    }

    public static void ResetToCurrentScene()
    {
        Initialize();

        backStack.Clear();

        Scene activeScene = SceneManager.GetActiveScene();
        currentSceneName = activeScene.IsValid()
            ? activeScene.name
            : string.Empty;

        Debug.Log($"[SceneHistory] Reset root: {currentSceneName}");
    }

    public static bool ResetAndLoad(string sceneName)
    {
        Initialize();

        sceneName = Normalize(sceneName);

        if (!CanLoad(sceneName))
            return false;

        backStack.Clear();
        currentSceneName = sceneName;

        SceneManager.LoadScene(sceneName);
        return true;
    }

    public static bool CanGoBack
    {
        get
        {
            Initialize();
            return backStack.Count > 0;
        }
    }

    public static int Count
    {
        get
        {
            Initialize();
            return backStack.Count;
        }
    }

    public static string DebugStack()
    {
        return backStack.Count == 0
            ? "<empty>"
            : string.Join(" <- ", backStack.ToArray());
    }

    private static void Push(string sceneName)
    {
        sceneName = Normalize(sceneName);

        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        if (backStack.Count > 0 &&
            string.Equals(
                backStack.Peek(),
                sceneName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        backStack.Push(sceneName);
        TrimToMaxSize();

        Debug.Log(
            $"[SceneHistory] Push: {sceneName}. Stack: {DebugStack()}"
        );
    }

    private static void HandleActiveSceneChanged(
        Scene previousScene,
        Scene nextScene)
    {
        if (nextScene.IsValid())
            currentSceneName = nextScene.name;

        /*
         * Không tự push previousScene tại đây.
         * Push chỉ được thực hiện bởi LoadScene().
         * Nhờ vậy GoBack không đẩy Scene hiện tại trở lại stack
         * và không tạo vòng lặp A <-> B.
         */
    }

    private static bool CanLoad(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[SceneHistory] sceneName đang trống.");
            return false;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError(
                $"[SceneHistory] {sceneName} chưa có trong Build Profiles / Scene List."
            );
            return false;
        }

        return true;
    }

    private static string Normalize(string sceneName)
    {
        return sceneName?.Trim() ?? string.Empty;
    }

    private static void TrimToMaxSize()
    {
        if (backStack.Count <= MaxHistorySize)
            return;

        string[] newestFirst = backStack.ToArray();
        backStack.Clear();

        int keepCount = Mathf.Min(MaxHistorySize, newestFirst.Length);

        for (int i = keepCount - 1; i >= 0; i--)
            backStack.Push(newestFirst[i]);
    }
}