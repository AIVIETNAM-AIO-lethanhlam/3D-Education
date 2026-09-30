#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

/// <summary>
/// PC / Meta Quest Link only (Editor Play Mode and Windows builds).
/// XR is not started automatically ("Initialize XR on Startup" is off), so the
/// 2D screens (login, classes, lessons) stay on the monitor. When VRClassroomScene
/// is loaded, this component starts the OpenXR loader so the classroom is rendered
/// in the headset through Quest Link, and stops it again when the user leaves.
/// Android builds (phone / standalone headset) are not affected.
/// </summary>
public class VRSessionBootstrap : MonoBehaviour
{
    private const string VrSceneName = "VRClassroomScene";
    private const string HeadsetVrSceneName = "VRClassroomXRScene";

    private static VRSessionBootstrap instance;
    private bool xrStartedHere;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (instance != null)
            return;

        GameObject host = new GameObject("[VRSessionBootstrap]");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<VRSessionBootstrap>();
        SceneManager.sceneLoaded += instance.OnSceneLoaded;
        instance.OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;

        if (scene.name == VrSceneName || scene.name == HeadsetVrSceneName)
            StartCoroutine(StartXR());
        else
            StopXR();
    }

    private IEnumerator StartXR()
    {
        XRGeneralSettings settings = XRGeneralSettings.Instance;
        if (settings == null || settings.Manager == null)
        {
            Debug.LogWarning("[VRSessionBootstrap] XR Plug-in Management is not configured for Windows/Standalone.");
            yield break;
        }

        if (settings.Manager.activeLoader == null)
            yield return settings.Manager.InitializeLoader();

        if (settings.Manager.activeLoader == null)
        {
            Debug.LogWarning("[VRSessionBootstrap] No XR loader started. Check that Meta Quest Link is running, the headset is connected, and Meta Quest Link is the active OpenXR runtime.");
            yield break;
        }

        settings.Manager.StartSubsystems();
        xrStartedHere = true;
        Debug.Log("[VRSessionBootstrap] XR started with " + settings.Manager.activeLoader.name);
    }

    private void StopXR()
    {
        if (!xrStartedHere)
            return;

        XRGeneralSettings settings = XRGeneralSettings.Instance;
        if (settings != null && settings.Manager != null && settings.Manager.activeLoader != null)
        {
            settings.Manager.StopSubsystems();
            settings.Manager.DeinitializeLoader();
        }

        xrStartedHere = false;
        Debug.Log("[VRSessionBootstrap] XR stopped.");
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        StopXR();
    }
}
#endif
