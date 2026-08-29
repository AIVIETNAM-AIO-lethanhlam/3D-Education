using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Builds the classroom and removes legacy world-space demo objects that are
/// no longer used by the runtime lesson-model VR flow.
/// </summary>
public class VRClassroomBuilder : MonoBehaviour
{
    private ClassroomConfig config;

    [Header("Legacy cleanup")]
    [SerializeField] private bool removeLegacySkeleton = true;
    [SerializeField] private bool removeLegacyPedestal = true;

    [Tooltip("Removes the long grey world-space object named 'Screen' shown in front of the player.")]
    [SerializeField] private bool removeLegacyScreen = true;

    private void Start()
    {
        config = new ClassroomConfig();

        // Runtime lesson models are loaded separately by VRRuntimeModelCatalog.
        // Do not create the old hard-coded Skeleton.
        config.skeletonPrefab = null;

        new FloorBuilder(config).Build(transform);
        new WallBuilder(config).Build(transform);

        FurnitureBuilder furniture =
            new FurnitureBuilder(config, null);

        furniture.Build(transform);

        // FurnitureBuilder creates its objects synchronously, so clean them now.
        CleanupLegacyObjects();

        // Also clean again after scene startup because some old scripts/builders
        // can create objects during their Start()/next frame.
        StartCoroutine(CleanupAfterSceneStartup());
    }

    private IEnumerator CleanupAfterSceneStartup()
    {
        yield return null;
        CleanupLegacyObjects();

        yield return new WaitForEndOfFrame();
        CleanupLegacyObjects();

        yield return new WaitForSecondsRealtime(0.25f);
        CleanupLegacyObjects();

        yield return new WaitForSecondsRealtime(0.75f);
        CleanupLegacyObjects();
    }

    private void CleanupLegacyObjects()
    {
        // First clean objects generated under this Environment hierarchy.
        Transform[] classroomObjects =
            GetComponentsInChildren<Transform>(true);

        for (int i = classroomObjects.Length - 1; i >= 0; i--)
        {
            Transform item = classroomObjects[i];

            if (item == null || item == transform)
                continue;

            TryRemoveLegacyObject(item.gameObject);
        }

        // Defensive scene-wide pass:
        // The screenshot/debug text shows "Hit: Screen", so the grey object can
        // also be outside this builder hierarchy depending on scene version.
#if UNITY_2023_1_OR_NEWER
        Transform[] allSceneTransforms =
            FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#else
        Transform[] allSceneTransforms =
            FindObjectsOfType<Transform>(true);
#endif

        for (int i = allSceneTransforms.Length - 1; i >= 0; i--)
        {
            Transform item = allSceneTransforms[i];

            if (item == null)
                continue;

            TryRemoveLegacyObject(item.gameObject);
        }
    }

    private void TryRemoveLegacyObject(GameObject target)
    {
        if (target == null)
            return;

        string objectName =
            target.name?.Trim() ?? string.Empty;

        bool shouldRemove = false;

        if (removeLegacySkeleton &&
            IsLegacySkeletonName(objectName))
        {
            shouldRemove = true;
        }

        if (removeLegacyPedestal &&
            IsLegacyPedestalName(objectName))
        {
            shouldRemove = true;
        }

        if (removeLegacyScreen &&
            IsLegacyScreenName(objectName) &&
            LooksLikeWorldGeometry(target))
        {
            shouldRemove = true;
        }

        if (!shouldRemove)
            return;

        Debug.Log(
            "[VRClassroomBuilder] Removing legacy world object: " +
            objectName);

        // Disable immediately so it disappears in the current frame.
        foreach (Renderer renderer
                 in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null)
                renderer.enabled = false;
        }

        foreach (Collider collider
                 in target.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null)
                collider.enabled = false;
        }

        target.SetActive(false);

        // Destroy after disabling to avoid a one-frame flash.
        Destroy(target);
    }

    private static bool IsLegacySkeletonName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return
            name.Equals(
                "Skeleton",
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(
                "Skeleton(",
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(
                "Skeleton ",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyPedestalName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return
            name.Equals(
                "Pedestal",
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(
                "Pedestal(",
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(
                "Pedestal ",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyScreenName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // IMPORTANT:
        // Only match the exact old 3D object name.
        // Do NOT broadly match "Screen" substrings because Unity/UI objects may
        // also contain that word.
        return
            name.Equals(
                "Screen",
                StringComparison.OrdinalIgnoreCase) ||
            name.Equals(
                "Screen(Clone)",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWorldGeometry(GameObject target)
    {
        if (target == null)
            return false;

        // Never touch Canvas/UI Toolkit related objects.
        if (target.GetComponent<Canvas>() != null ||
            target.GetComponent<RectTransform>() != null)
        {
            return false;
        }

        return
            target.GetComponentInChildren<MeshRenderer>(true) != null ||
            target.GetComponentInChildren<SkinnedMeshRenderer>(true) != null ||
            target.GetComponentInChildren<Collider>(true) != null;
    }
}
