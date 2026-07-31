using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class SplashPageController : MonoBehaviour
{
    [Header("Scene Configuration")]
    [SerializeField]
    private string nextSceneName = "HomeScene";

    [Header("Splash Configuration")]
    [SerializeField]
    [Min(0.1f)]
    private float splashDuration = 4f;

    private VisualElement progressTrack;
    private VisualElement progressFill;

    private VisualElement loadingDot1;
    private VisualElement loadingDot2;
    private VisualElement loadingDot3;

    private Coroutine splashCoroutine;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();

        VisualElement root = document.rootVisualElement;

        progressTrack =
            root.Q<VisualElement>("progress-track");

        progressFill =
            root.Q<VisualElement>("progress-fill");

        loadingDot1 =
            root.Q<VisualElement>("loading-dot-1");

        loadingDot2 =
            root.Q<VisualElement>("loading-dot-2");

        loadingDot3 =
            root.Q<VisualElement>("loading-dot-3");

        if (progressTrack == null ||
            progressFill == null)
        {
            Debug.LogError(
                "SplashPageController: Không tìm thấy " +
                "progress-track hoặc progress-fill."
            );

            return;
        }

        /*
         * Đặt thanh loading về 0 ngay khi Scene được bật.
         */
        progressFill.style.width = 0f;

        splashCoroutine =
            StartCoroutine(RunSplashScreen());
    }

    private void OnDisable()
    {
        if (splashCoroutine != null)
        {
            StopCoroutine(splashCoroutine);
            splashCoroutine = null;
        }
    }

    private IEnumerator RunSplashScreen()
    {
        if (string.IsNullOrWhiteSpace(nextSceneName))
        {
            Debug.LogError(
                "SplashPageController: nextSceneName đang trống."
            );

            yield break;
        }

        /*
         * Chờ UI Toolkit tính xong chiều rộng của progress-track.
         */
        yield return new WaitUntil(
            () => progressTrack.resolvedStyle.width > 0f
        );

        /*
         * Ép thanh về 0 thêm lần nữa sau khi layout hoàn tất.
         */
        UpdateProgressBar(0f);

        /*
         * Giữ một vài frame ở trạng thái 0%.
         * Nhờ vậy người dùng nhìn thấy thanh bắt đầu từ đầu.
         */
        yield return null;
        yield return null;

        /*
         * Bắt đầu tải HomeScene sau khi Splash đã hiển thị.
         */
        AsyncOperation loadOperation =
            SceneManager.LoadSceneAsync(nextSceneName);

        if (loadOperation == null)
        {
            Debug.LogError(
                $"Không thể tải Scene '{nextSceneName}'. " +
                "Hãy kiểm tra Build Profiles."
            );

            yield break;
        }

        loadOperation.allowSceneActivation = false;

        float elapsedTime = 0f;

        while (elapsedTime < splashDuration)
        {
            /*
             * Giới hạn deltaTime để frame đầu không làm thanh
             * nhảy thẳng lên 30–40%.
             */
            float safeDeltaTime =
                Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            elapsedTime += safeDeltaTime;

            float progress =
                Mathf.Clamp01(
                    elapsedTime / splashDuration
                );

            UpdateProgressBar(progress);
            UpdateLoadingDots(elapsedTime);

            yield return null;
        }

        UpdateProgressBar(1f);

        /*
         * Chờ Scene tải xong trước khi chuyển.
         */
        while (loadOperation.progress < 0.9f)
        {
            yield return null;
        }

        loadOperation.allowSceneActivation = true;
    }

    private void UpdateProgressBar(float progress)
    {
        if (progressTrack == null ||
            progressFill == null)
        {
            return;
        }

        float trackWidth =
            progressTrack.resolvedStyle.width;

        if (trackWidth <= 0f)
        {
            return;
        }

        progress = Mathf.Clamp01(progress);

        float fillWidth =
            trackWidth * progress;

        progressFill.style.width = fillWidth;
    }

    private void UpdateLoadingDots(float elapsedTime)
    {
        int activeIndex =
            Mathf.FloorToInt(elapsedTime * 2.5f) % 3;

        SetDotActive(loadingDot1, activeIndex == 0);
        SetDotActive(loadingDot2, activeIndex == 1);
        SetDotActive(loadingDot3, activeIndex == 2);
    }

    private static void SetDotActive(
        VisualElement dot,
        bool isActive
    )
    {
        if (dot == null)
        {
            return;
        }

        if (isActive)
        {
            dot.AddToClassList("loading-dot-active");
        }
        else
        {
            dot.RemoveFromClassList("loading-dot-active");
        }
    }
}