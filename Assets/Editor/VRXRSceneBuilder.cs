using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds VRClassroomXRScene (headset version) from VRClassroomScene (phone version).
/// The phone scene is copied, never modified.
///
/// Menu: 3D Education > VR > Build Headset VR Scene
///       3D Education > VR > Use Headset VR Scene In Editor (toggle)
/// </summary>
public static class VRXRSceneBuilder
{
    private const string SourceScenePath = "Assets/VRModeNew/Scene/VRClassroomScene.unity";
    private const string XRScenePath = "Assets/VRModeNew/Scene/VRClassroomXRScene.unity";
    private const string XRIPackageName = "com.unity.xr.interaction.toolkit";
    private const string SimulatorSampleName = "XR Interaction Simulator";
    private const string StarterAssetsSampleName = "Starter Assets";

    // Read by ShowLessonPageController (Editor only) to open the headset scene.
    public const string UseXRScenePrefKey = "3DEducation.UseXRSceneInEditor";
    private const string ToggleMenuPath = "3D Education/VR/Use Headset VR Scene In Editor";

    // Phone-only objects of VRClassroomScene that are disabled in the headset scene.
    private static readonly string[] PhoneOnlyObjects =
    {
        "Player",       // CharacterController + joystick + touch camera
        "Main Camera",  // phone camera (replaced by the XR Origin camera)
        "Canvas",       // legacy screen-space toolbar / joystick
        "UIDocument",   // screen-space UI Toolkit HUD (VRPageController)
        "GameManager",  // legacy ModelSpawner / ModelListUI
        "UIManager",    // legacy toolbar manager
        "Cylinder",     // legacy placement indicator
    };

    [MenuItem("3D Education/VR/Build Headset VR Scene", priority = 1)]
    private static void BuildHeadsetScene()
    {
        if (!File.Exists(SourceScenePath))
        {
            EditorUtility.DisplayDialog("VR XR Scene", "Không tìm thấy " + SourceScenePath, "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        bool importedStarter = EnsureSampleImported(StarterAssetsSampleName);
        bool importedSimulator = EnsureSampleImported(SimulatorSampleName);

        if (importedStarter || importedSimulator)
        {
            // Sample scripts must compile before their prefabs can be used.
            EditorUtility.DisplayDialog(
                "VR XR Scene",
                "Đã import sample của XR Interaction Toolkit.\n\n" +
                "Đợi Unity biên dịch xong (thanh tiến trình góc dưới biến mất), " +
                "rồi chạy lại menu 3D Education > VR > Build Headset VR Scene.",
                "OK");
            return;
        }

        GameObject xrOriginPrefab = FindPrefab("XR Origin (XR Rig)", "Starter Assets");
        if (xrOriginPrefab == null)
        {
            EditorUtility.DisplayDialog(
                "VR XR Scene",
                "Chưa có prefab 'XR Origin (XR Rig)'.\n\n" +
                "Mở Window > Package Manager > XR Interaction Toolkit > Samples, " +
                "import 'Starter Assets', đợi Unity biên dịch xong rồi chạy lại menu này.",
                "OK");
            return;
        }

        if (File.Exists(XRScenePath))
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "VR XR Scene",
                "VRClassroomXRScene đã tồn tại. Tạo lại từ VRClassroomScene?\n" +
                "(Mọi chỉnh sửa tay trong VRClassroomXRScene sẽ mất.)",
                "Tạo lại", "Hủy");

            if (!overwrite)
                return;

            AssetDatabase.DeleteAsset(XRScenePath);
        }

        if (!AssetDatabase.CopyAsset(SourceScenePath, XRScenePath))
        {
            EditorUtility.DisplayDialog("VR XR Scene", "Không copy được scene.", "OK");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(XRScenePath, OpenSceneMode.Single);

        // 1) Remember where the phone player stands, then disable phone-only objects.
        Vector3 spawnPosition = Vector3.zero;
        Quaternion spawnRotation = Quaternion.identity;

        GameObject player = FindInScene(scene, "Player");
        if (player != null)
        {
            spawnPosition = player.transform.position;
            spawnRotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
        }

        foreach (string objectName in PhoneOnlyObjects)
        {
            GameObject target = FindInScene(scene, objectName);
            if (target != null)
                target.SetActive(false);
            else
                Debug.Log($"[VRXRSceneBuilder] '{objectName}' not found (skipped).");
        }

        // 2) XR Origin: head tracking + controllers (+ locomotion from Starter Assets).
        GameObject origin = (GameObject)PrefabUtility.InstantiatePrefab(xrOriginPrefab, scene);
        origin.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

        // 3) Headset scene controller: model catalog, grab, world-space menu, simulator.
        GameObject controller = new GameObject("VR XR Scene Controller");
        SceneManager.MoveGameObjectToScene(controller, scene);
        controller.AddComponent<VRXRSceneController>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        UpdateBuildScenes();
        EditorPrefs.SetBool(UseXRScenePrefKey, true);

        EditorUtility.DisplayDialog(
            "VR XR Scene",
            "Đã tạo " + XRScenePath + " và thêm vào Build Profiles.\n\n" +
            "Chế độ 'Use Headset VR Scene In Editor' đã được BẬT: khi bấm VR Mode " +
            "trong ShowLessonScene, Editor sẽ mở bản cho kính.\n\n" +
            "Nếu Unity vừa import sample, hãy đợi biên dịch xong rồi mới bấm Play.",
            "OK");
    }

    [MenuItem(ToggleMenuPath, priority = 2)]
    private static void ToggleUseXRScene()
    {
        bool value = !EditorPrefs.GetBool(UseXRScenePrefKey, false);
        EditorPrefs.SetBool(UseXRScenePrefKey, value);
        Debug.Log("[VRXRSceneBuilder] Use headset VR scene in Editor: " + value);
    }

    [MenuItem(ToggleMenuPath, true)]
    private static bool ToggleUseXRSceneValidate()
    {
        Menu.SetChecked(ToggleMenuPath, EditorPrefs.GetBool(UseXRScenePrefKey, false));
        return true;
    }

    // =========================================================

    /// <returns>true when the sample was imported just now.</returns>
    private static bool EnsureSampleImported(string displayName)
    {
        string version =
            UnityEditor.PackageManager.PackageInfo.FindForPackageName(XRIPackageName)?.version;

        Sample sample = Sample.FindByPackage(XRIPackageName, version)
            .FirstOrDefault(s => s.displayName == displayName);

        if (string.IsNullOrEmpty(sample.displayName))
        {
            Debug.LogWarning($"[VRXRSceneBuilder] Sample '{displayName}' was not found in {XRIPackageName}.");
            return false;
        }

        if (sample.isImported)
            return false;

        bool imported = sample.Import(Sample.ImportOptions.OverridePreviousImports);
        Debug.Log($"[VRXRSceneBuilder] Import sample '{displayName}': {imported}");
        AssetDatabase.Refresh();
        return imported;
    }

    private static GameObject FindPrefab(string prefabName, string pathMustContain)
    {
        foreach (string guid in AssetDatabase.FindAssets($"\"{prefabName}\" t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == prefabName &&
                path.Contains(pathMustContain))
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
        }

        return null;
    }

    private static GameObject FindInScene(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.Trim() == objectName)
                    return child.gameObject;
            }
        }

        return null;
    }

    private static void UpdateBuildScenes()
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();

        // Drop entries whose scene file no longer exists (e.g. old Assets/VRMode/Scene/VRScene.unity).
        int removed = scenes.RemoveAll(s => !File.Exists(s.path));
        if (removed > 0)
            Debug.Log($"[VRXRSceneBuilder] Removed {removed} missing scene(s) from Build Profiles.");

        if (!scenes.Any(s => s.path == XRScenePath))
            scenes.Add(new EditorBuildSettingsScene(XRScenePath, true));

        EditorBuildSettings.scenes = scenes.ToArray();

        // Write ProjectSettings/EditorBuildSettings.asset to disk right away
        // (otherwise it is only saved when Unity saves the project).
        AssetDatabase.SaveAssets();
    }

    [MenuItem("3D Education/VR/Add Headset VR Scene To Build Profiles", priority = 3)]
    private static void AddHeadsetSceneToBuild()
    {
        if (!File.Exists(XRScenePath))
        {
            EditorUtility.DisplayDialog(
                "VR XR Scene",
                "Chưa có VRClassroomXRScene. Hãy chạy 3D Education > VR > Build Headset VR Scene trước.",
                "OK");
            return;
        }

        UpdateBuildScenes();
        EditorPrefs.SetBool(UseXRScenePrefKey, true);
        EditorUtility.DisplayDialog(
            "VR XR Scene",
            "Đã thêm VRClassroomXRScene vào Build Profiles và bật 'Use Headset VR Scene In Editor'.",
            "OK");
    }
}
