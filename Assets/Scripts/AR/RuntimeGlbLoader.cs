using System;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

/// <summary>
/// Runtime loader cho GLB/GLTF.
/// 
/// Hỗ trợ:
/// - Cloudflare R2 presigned URL (AWS SigV4) - GIỮ NGUYÊN URL, không rebuild.
/// - Supabase Storage signed URL.
/// - Public HTTP/HTTPS URL.
/// 
/// Lưu ý:
/// - URL /storage/v1/object/authenticated/... của Supabase cần Authorization header.
///   glTFast tự tải URL nên loader này không thể tự gắn JWT vào request đó.
///   Vì vậy ShowLessonScene nên truyền signed URL, không truyền authenticated URL.
/// </summary>
public class RuntimeGlbLoader : MonoBehaviour
{
    [Header("Model container")]
    [SerializeField] private Transform modelRoot;

    [Header("Default model settings")]
    [SerializeField] private float initialScale = 1.0f;
    [SerializeField] private bool normalizeModelSize = true;
    [SerializeField] private float normalizedSize = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool logUrls = true;

    private GltfImport gltfImport;
    private GameObject loadedModel;

    public GameObject LoadedModel => loadedModel;
    public bool IsLoaded => loadedModel != null;

    public async Task<GameObject> LoadModelAsync(string modelUrl)
    {
        if (string.IsNullOrWhiteSpace(modelUrl))
        {
            Debug.LogError("[RuntimeGlbLoader] Model URL is empty.");
            return null;
        }

        if (modelRoot == null)
        {
            Debug.LogError(
                "[RuntimeGlbLoader] Model Root is not assigned in the Inspector.");
            return null;
        }

        ClearModel();

        // Chỉ sửa URL Supabase legacy khi thực sự cần.
        // R2/AWS presigned URL phải được dùng nguyên trạng.
        string finalUrl = PrepareModelUrl(modelUrl);

        if (logUrls)
        {
            Debug.Log(
                "[RuntimeGlbLoader] Original model URL:\n" +
                modelUrl);

            Debug.Log(
                "[RuntimeGlbLoader] Final model URL:\n" +
                finalUrl);

            Debug.Log(
                "[RuntimeGlbLoader] URL type: " +
                GetUrlTypeDescription(finalUrl));
        }

        if (IsSupabaseAuthenticatedObjectUrl(finalUrl))
        {
            Debug.LogError(
                "[RuntimeGlbLoader] This is a Supabase authenticated object URL.\n" +
                "glTFast cannot attach the user's Supabase JWT automatically.\n" +
                "Create a signed URL in ShowLessonScene / backend and pass that signed URL instead.\n" +
                "URL: " + finalUrl);
            return null;
        }

        if (!Uri.TryCreate(
                finalUrl,
                UriKind.Absolute,
                out Uri modelUri))
        {
            Debug.LogError(
                "[RuntimeGlbLoader] The final model URL is invalid:\n" +
                finalUrl);
            return null;
        }

        try
        {
            gltfImport = new GltfImport();

            bool loaded = await gltfImport.Load(modelUri);

            if (!loaded)
            {
                Debug.LogError(
                    "[RuntimeGlbLoader] glTFast could not load the GLB file.\n" +
                    "Possible causes:\n" +
                    "- R2/Supabase signed URL expired.\n" +
                    "- Signed URL was generated for the wrong object key.\n" +
                    "- R2 token does not have Object Read permission.\n" +
                    "- The GLB object does not exist.\n" +
                    "- The server returned HTML/XML instead of a GLB file.");

                DisposeImporter();
                return null;
            }

            GameObject container = new GameObject("LoadedGLB");
            container.transform.SetParent(modelRoot, false);

            bool instantiated =
                await gltfImport.InstantiateMainSceneAsync(
                    container.transform);

            if (!instantiated)
            {
                Debug.LogError(
                    "[RuntimeGlbLoader] Could not instantiate the GLB scene.");

                Destroy(container);
                DisposeImporter();
                return null;
            }

            loadedModel = container;

            loadedModel.transform.localPosition = Vector3.zero;
            loadedModel.transform.localRotation = Quaternion.identity;
            loadedModel.transform.localScale =
                Vector3.one * Mathf.Max(0.0001f, initialScale);

            DisableImportedCamerasAndLights(loadedModel);

            if (normalizeModelSize)
            {
                NormalizeSize(
                    loadedModel,
                    Mathf.Max(0.01f, normalizedSize));
            }

            // ARModelSceneController sẽ bật model sau khi user đặt lên plane.
            loadedModel.SetActive(false);

            Debug.Log(
                "[RuntimeGlbLoader] Model loaded successfully.");

            return loadedModel;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "[RuntimeGlbLoader] Error loading model:\n" +
                exception);

            ClearModel();
            return null;
        }
    }

    /// <summary>
    /// Không được thay đổi AWS/R2 presigned URL.
    /// Chỉ sửa các Supabase legacy URL bị thiếu /storage/v1.
    /// </summary>
    private static string PrepareModelUrl(string rawUrl)
    {
        string value = rawUrl?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        // CRITICAL:
        // AWS SigV4/R2 presigned URL phụ thuộc vào exact canonical URI + query.
        // Không dùng UriBuilder để rebuild URL này.
        if (IsAwsPresignedUrl(value))
            return value;

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out Uri uri))
        {
            return value;
        }

        // Supabase signed URL chuẩn đã có /storage/v1/object/sign/.
        // Không rebuild nếu không cần.
        if (uri.AbsolutePath.StartsWith(
                "/storage/v1/object/sign/",
                StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        // Public/authenticated Supabase URL chuẩn cũng giữ nguyên.
        if (uri.AbsolutePath.StartsWith(
                "/storage/v1/object/public/",
                StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.StartsWith(
                "/storage/v1/object/authenticated/",
                StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        // Chỉ compatibility cho URL Supabase legacy:
        // /object/sign/... -> /storage/v1/object/sign/...
        // /storage/object/... -> /storage/v1/object/...
        string path = uri.AbsolutePath;
        bool needsRepair = false;

        if (path.StartsWith(
                "/object/",
                StringComparison.OrdinalIgnoreCase))
        {
            path = "/storage/v1" + path;
            needsRepair = true;
        }
        else if (path.StartsWith(
                     "/storage/object/",
                     StringComparison.OrdinalIgnoreCase))
        {
            path =
                "/storage/v1" +
                path.Substring("/storage".Length);

            needsRepair = true;
        }

        if (!needsRepair)
            return value;

        try
        {
            UriBuilder builder = new UriBuilder(uri)
            {
                Path = path
            };

            return builder.Uri.AbsoluteUri;
        }
        catch
        {
            return value;
        }
    }

    private static bool IsAwsPresignedUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return
            url.IndexOf(
                "X-Amz-Signature=",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
            url.IndexOf(
                "X-Amz-Credential=",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
            url.IndexOf(
                "X-Amz-Algorithm=AWS4-HMAC-SHA256",
                StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsSupabaseAuthenticatedObjectUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return url.IndexOf(
            "/storage/v1/object/authenticated/",
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetUrlTypeDescription(string url)
    {
        if (IsAwsPresignedUrl(url))
            return "Cloudflare R2 / AWS SigV4 presigned URL";

        if (url.IndexOf(
                "/storage/v1/object/sign/",
                StringComparison.OrdinalIgnoreCase) >= 0)
            return "Supabase signed Storage URL";

        if (url.IndexOf(
                "/storage/v1/object/public/",
                StringComparison.OrdinalIgnoreCase) >= 0)
            return "Supabase public Storage URL";

        if (IsSupabaseAuthenticatedObjectUrl(url))
            return "Supabase authenticated Storage URL";

        return "Normal HTTP/HTTPS URL";
    }

    private void NormalizeSize(
        GameObject target,
        float targetSize)
    {
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);

        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning(
                "[RuntimeGlbLoader] Model has no Renderer. " +
                "Size normalization skipped.");
            return;
        }

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float largestDimension = Mathf.Max(
            bounds.size.x,
            bounds.size.y,
            bounds.size.z);

        if (largestDimension <= 0.0001f)
            return;

        float scaleMultiplier =
            targetSize / largestDimension;

        target.transform.localScale *= scaleMultiplier;

        bounds = CalculateBounds(target);

        Vector3 localCenter =
            target.transform.InverseTransformPoint(
                bounds.center);

        float localHalfHeight =
            bounds.extents.y /
            Mathf.Max(
                Mathf.Abs(target.transform.lossyScale.y),
                0.0001f);

        target.transform.localPosition = new Vector3(
            -localCenter.x,
            -localCenter.y + localHalfHeight,
            -localCenter.z);
    }

    private static Bounds CalculateBounds(
        GameObject target)
    {
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);

        if (renderers == null || renderers.Length == 0)
            return new Bounds(
                target.transform.position,
                Vector3.zero);

        Bounds result = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            result.Encapsulate(renderers[i].bounds);
        }

        return result;
    }

    private static void DisableImportedCamerasAndLights(
        GameObject target)
    {
        Camera[] cameras =
            target.GetComponentsInChildren<Camera>(true);

        foreach (Camera importedCamera in cameras)
        {
            importedCamera.enabled = false;
        }

        Light[] lights =
            target.GetComponentsInChildren<Light>(true);

        foreach (Light importedLight in lights)
        {
            importedLight.enabled = false;
        }
    }

    public void ClearModel()
    {
        if (loadedModel != null)
        {
            Destroy(loadedModel);
            loadedModel = null;
        }

        DisposeImporter();
    }

    private void DisposeImporter()
    {
        if (gltfImport == null)
            return;

        gltfImport.Dispose();
        gltfImport = null;
    }

    private void OnDestroy()
    {
        ClearModel();
    }
}
