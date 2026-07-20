#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class WhiteIconGeneratorFixed
{
    private const string IconFolder = "Assets/UI/Images/Icons";
    private const string WhiteSuffix = "-white";

    [MenuItem("Tools/Virtual Education/Generate All White UI Icons")]
    public static void GenerateAllWhiteIcons()
    {
        if (!AssetDatabase.IsValidFolder(IconFolder))
        {
            Debug.LogError(
                $"Không tìm thấy thư mục icon: {IconFolder}\n" +
                "Hãy kiểm tra lại đường dẫn IconFolder."
            );

            return;
        }

        string projectRoot = Directory
            .GetParent(Application.dataPath)!
            .FullName;

        string iconFolderAbsolutePath = Path.Combine(
            projectRoot,
            IconFolder
        );

        string[] sourceFiles = Directory.GetFiles(
            iconFolderAbsolutePath,
            "*.png",
            SearchOption.AllDirectories
        );

        if (sourceFiles.Length == 0)
        {
            Debug.LogWarning(
                $"Không tìm thấy file PNG nào trong thư mục: {IconFolder}"
            );

            return;
        }

        int generatedCount = 0;
        int skippedCount = 0;
        int failedCount = 0;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (string sourceAbsolutePath in sourceFiles)
            {
                string normalizedAbsolutePath =
                    sourceAbsolutePath.Replace("\\", "/");

                string sourceFileName =
                    Path.GetFileNameWithoutExtension(normalizedAbsolutePath);

                /*
                 * Không xử lý lại các file đã là icon trắng.
                 *
                 * Ví dụ:
                 * profile-white.png
                 * book-white.png
                 */
                if (sourceFileName.EndsWith(
                        WhiteSuffix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    skippedCount++;
                    continue;
                }

                string sourceAssetPath =
                    ConvertAbsolutePathToAssetPath(
                        normalizedAbsolutePath,
                        projectRoot
                    );

                bool generated = GenerateWhiteIcon(
                    sourceAssetPath,
                    normalizedAbsolutePath,
                    projectRoot
                );

                if (generated)
                {
                    generatedCount++;
                }
                else
                {
                    failedCount++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();

        AssetDatabase.Refresh(
            ImportAssetOptions.ForceSynchronousImport |
            ImportAssetOptions.ForceUpdate
        );

        Debug.Log(
            "Hoàn thành tạo icon trắng.\n" +
            $"Đã tạo hoặc cập nhật: {generatedCount} icon.\n" +
            $"Đã bỏ qua: {skippedCount} icon trắng có sẵn.\n" +
            $"Thất bại: {failedCount} icon.\n\n" +
            $"Thư mục đầu ra: {IconFolder}"
        );
    }

    private static bool GenerateWhiteIcon(
        string sourceAssetPath,
        string sourceAbsolutePath,
        string projectRoot)
    {
        Texture2D sourceTexture = null;
        Texture2D outputTexture = null;

        try
        {
            if (!File.Exists(sourceAbsolutePath))
            {
                Debug.LogError(
                    $"Không tìm thấy icon nguồn: {sourceAbsolutePath}"
                );

                return false;
            }

            byte[] sourceBytes = File.ReadAllBytes(sourceAbsolutePath);

            sourceTexture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false
            );

            if (!sourceTexture.LoadImage(sourceBytes, false))
            {
                Debug.LogError(
                    $"Không thể đọc icon: {sourceAssetPath}"
                );

                return false;
            }

            Color32[] sourcePixels = sourceTexture.GetPixels32();
            Color32[] whitePixels = new Color32[sourcePixels.Length];

            for (int i = 0; i < sourcePixels.Length; i++)
            {
                Color32 sourcePixel = sourcePixels[i];

                /*
                 * Giữ nguyên alpha của pixel gốc.
                 * Toàn bộ phần nhìn thấy được chuyển sang màu trắng.
                 */
                whitePixels[i] = new Color32(
                    255,
                    255,
                    255,
                    sourcePixel.a
                );
            }

            outputTexture = new Texture2D(
                sourceTexture.width,
                sourceTexture.height,
                TextureFormat.RGBA32,
                false
            );

            outputTexture.SetPixels32(whitePixels);
            outputTexture.Apply(false, false);

            string sourceDirectory =
                Path.GetDirectoryName(sourceAbsolutePath)!
                    .Replace("\\", "/");

            string outputFileName =
                Path.GetFileNameWithoutExtension(sourceAbsolutePath) +
                WhiteSuffix +
                ".png";

            string outputAbsolutePath = Path.Combine(
                    sourceDirectory,
                    outputFileName
                )
                .Replace("\\", "/");

            File.WriteAllBytes(
                outputAbsolutePath,
                outputTexture.EncodeToPNG()
            );

            string outputAssetPath =
                ConvertAbsolutePathToAssetPath(
                    outputAbsolutePath,
                    projectRoot
                );

            /*
             * Không import ngay ở đây vì đang dùng
             * AssetDatabase.StartAssetEditing().
             *
             * AssetDatabase.Refresh() ở cuối hàm chính
             * sẽ import toàn bộ file đã tạo.
             */
            Debug.Log(
                $"Đã tạo icon trắng: {outputAssetPath}"
            );

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Không thể tạo icon trắng từ: {sourceAssetPath}\n" +
                exception
            );

            return false;
        }
        finally
        {
            if (sourceTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceTexture);
            }

            if (outputTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(outputTexture);
            }
        }
    }

    private static string ConvertAbsolutePathToAssetPath(
        string absolutePath,
        string projectRoot)
    {
        string normalizedProjectRoot =
            projectRoot.Replace("\\", "/").TrimEnd('/');

        string normalizedAbsolutePath =
            absolutePath.Replace("\\", "/");

        string relativePath = normalizedAbsolutePath
            .Substring(normalizedProjectRoot.Length)
            .TrimStart('/');

        return relativePath;
    }

    [MenuItem("Tools/Virtual Education/Configure Generated White Icons")]
    public static void ConfigureGeneratedWhiteIcons()
    {
        if (!AssetDatabase.IsValidFolder(IconFolder))
        {
            Debug.LogError(
                $"Không tìm thấy thư mục icon: {IconFolder}"
            );

            return;
        }

        string[] textureGuids = AssetDatabase.FindAssets(
            "t:Texture2D",
            new[] { IconFolder }
        );

        int configuredCount = 0;

        foreach (string guid in textureGuids)
        {
            string assetPath =
                AssetDatabase.GUIDToAssetPath(guid);

            string fileNameWithoutExtension =
                Path.GetFileNameWithoutExtension(assetPath);

            if (!fileNameWithoutExtension.EndsWith(
                    WhiteSuffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (importer == null)
            {
                continue;
            }

            bool hasChanged = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                hasChanged = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                hasChanged = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                hasChanged = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                hasChanged = true;
            }

            if (importer.filterMode != FilterMode.Bilinear)
            {
                importer.filterMode = FilterMode.Bilinear;
                hasChanged = true;
            }

            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                hasChanged = true;
            }

            if (hasChanged)
            {
                importer.SaveAndReimport();
            }

            configuredCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"Đã cấu hình {configuredCount} icon trắng thành Sprite."
        );
    }

    [MenuItem("Tools/Virtual Education/Generate And Configure White UI Icons")]
    public static void GenerateAndConfigureWhiteIcons()
    {
        GenerateAllWhiteIcons();
        ConfigureGeneratedWhiteIcons();

        Debug.Log(
            "Đã tạo và cấu hình xong toàn bộ icon trắng.\n" +
            "Bạn có thể sử dụng các file có hậu tố '-white.png' " +
            "trong bất kỳ file USS hoặc UXML nào."
        );
    }
}

#endif