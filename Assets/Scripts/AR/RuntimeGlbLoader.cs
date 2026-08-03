using System;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

public class RuntimeGlbLoader : MonoBehaviour
{
    [Header("Model container")]
    [SerializeField] private Transform modelRoot;

    [Header("Default model settings")]
    [SerializeField] private float initialScale = 0.25f;
    [SerializeField] private bool normalizeModelSize = true;
    [SerializeField] private float normalizedSize = 0.35f;

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

        string correctedUrl = NormalizeSupabaseStorageUrl(modelUrl);

        Debug.Log(
            "[RuntimeGlbLoader] Original model URL:\n" +
            modelUrl);

        Debug.Log(
            "[RuntimeGlbLoader] Final model URL:\n" +
            correctedUrl);

        if (!Uri.TryCreate(
                correctedUrl,
                UriKind.Absolute,
                out Uri modelUri))
        {
            Debug.LogError(
                "[RuntimeGlbLoader] The final model URL is invalid:\n" +
                correctedUrl);
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
                    "Check that the Final model URL contains " +
                    "'/storage/v1/object/sign/' and that the signed token " +
                    "has not expired."
                );

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
                Vector3.one * initialScale;

            DisableImportedCamerasAndLights(loadedModel);

            if (normalizeModelSize)
            {
                NormalizeSize(
                    loadedModel,
                    Mathf.Max(0.01f, normalizedSize));
            }

            // Keep the model hidden until the user taps a detected plane.
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
    /// Supabase createSignedUrl can return an absolute URL whose path starts
    /// with /object/sign/... instead of /storage/v1/object/sign/...
    /// This method repairs that URL while preserving the query token.
    /// </summary>
    private static string NormalizeSupabaseStorageUrl(
        string rawUrl)
    {
        string value = rawUrl?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out Uri uri))
        {
            return value;
        }

        string path = uri.AbsolutePath;

        if (path.StartsWith(
                "/object/",
                StringComparison.OrdinalIgnoreCase))
        {
            path = "/storage/v1" + path;
        }
        else if (path.StartsWith(
                     "/storage/object/",
                     StringComparison.OrdinalIgnoreCase))
        {
            path =
                "/storage/v1" +
                path.Substring("/storage".Length);
        }

        UriBuilder builder = new(uri)
        {
            Path = path
        };

        return builder.Uri.AbsoluteUri;
    }

    private void NormalizeSize(
        GameObject target,
        float targetSize)
    {
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
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