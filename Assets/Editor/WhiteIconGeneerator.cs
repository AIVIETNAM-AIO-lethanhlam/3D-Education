#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class WhiteIconGeneratorFixed
{
    private const string IconFolder = "Assets/UI/Images/Icons";

    private static readonly string[] SourceFiles =
    {
        "graduation-cap.png",
        "cube.png",
        "vr-glasses.png",
        "ai-sparkle.png",
        "trophy.png",
        "lock.png"
    };

    [MenuItem("Tools/Virtual Education/Generate White UI Icons (Fixed)")]
    public static void GenerateWhiteIcons()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;

        foreach (string fileName in SourceFiles)
        {
            string sourceAssetPath = $"{IconFolder}/{fileName}";
            string sourceAbsolutePath = Path.Combine(projectRoot, sourceAssetPath);

            if (!File.Exists(sourceAbsolutePath))
            {
                Debug.LogError($"Không tìm thấy icon nguồn: {sourceAbsolutePath}");
                continue;
            }

            byte[] sourceBytes = File.ReadAllBytes(sourceAbsolutePath);
            Texture2D sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!sourceTexture.LoadImage(sourceBytes, false))
            {
                Object.DestroyImmediate(sourceTexture);
                Debug.LogError($"Không thể đọc icon: {sourceAssetPath}");
                continue;
            }

            Color32[] sourcePixels = sourceTexture.GetPixels32();
            Color32[] whitePixels = new Color32[sourcePixels.Length];

            for (int i = 0; i < sourcePixels.Length; i++)
            {
                // Giữ nguyên alpha của ảnh nguồn, chỉ đổi phần màu thành trắng.
                whitePixels[i] = new Color32(255, 255, 255, sourcePixels[i].a);
            }

            Texture2D outputTexture = new Texture2D(
                sourceTexture.width,
                sourceTexture.height,
                TextureFormat.RGBA32,
                false);

            outputTexture.SetPixels32(whitePixels);
            outputTexture.Apply(false, false);

            string outputName = Path.GetFileNameWithoutExtension(fileName) + "-white.png";
            string outputAssetPath = $"{IconFolder}/{outputName}";
            string outputAbsolutePath = Path.Combine(projectRoot, outputAssetPath);

            File.WriteAllBytes(outputAbsolutePath, outputTexture.EncodeToPNG());

            Object.DestroyImmediate(sourceTexture);
            Object.DestroyImmediate(outputTexture);

            AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(outputAssetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            Debug.Log($"Đã tạo và import: {outputAssetPath}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("Đã tạo xong các icon trắng. Hãy Reimport HomePage.uss và HomePage.uxml.");
    }
}
#endif
