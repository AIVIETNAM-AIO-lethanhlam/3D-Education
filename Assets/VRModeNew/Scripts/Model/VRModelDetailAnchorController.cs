using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

public class VRModelDetailAnchorController : MonoBehaviour
{
    // =========================================================
    // REFERENCES
    // =========================================================

    [Header("References")]

    [SerializeField]
    private VRModelDetailService detailService;

    [SerializeField]
    private Camera targetCamera;

    [SerializeField]
    private Transform modelRoot;


    // =========================================================
    // RUNTIME MODEL AUTO FIND
    // =========================================================

    [Header("Runtime Model Auto Find")]

    [SerializeField]
    private bool autoFindRuntimeModel = true;

    [SerializeField]
    private string runtimeAnchorName =
        "VRRuntimeModelAnchor";

    [SerializeField]
    private float findModelTimeout =
        20f;


    // =========================================================
    // AUTOMATIC AI ANCHORS
    // =========================================================

    [Header("Automatic AI Anchors")]

    [SerializeField]
    private bool buildAutomaticAnchors = true;

    [Tooltip(
        "Most Unity glTF loaders convert glTF right-handed coordinates to Unity "
        + "by flipping X. If the markers appear mirrored, toggle this off."
    )]
    [SerializeField]
    private bool flipAnchorX = true;

    [SerializeField]
    private bool flipAnchorY = false;

    [SerializeField]
    private bool flipAnchorZ = false;

    [SerializeField]
    private float anchorCoordinateScale = 1f;

    [SerializeField]
    private Vector3 anchorCoordinateOffset = Vector3.zero;


    // =========================================================
    // AUTOMATIC AI ANCHOR SURFACE SNAP
    // =========================================================

    [Header("Automatic Anchor Surface Snap")]

    [Tooltip(
        "Snap each AI anchor to the nearest vertex on the runtime model. "
        + "This refines the approximate backend 3D point so the final anchor "
        + "lies on the visible model surface."
    )]
    [SerializeField]
    private bool snapAutomaticAnchorsToMesh = true;

    [Tooltip(
        "Maximum distance, in model-root local units, that an AI anchor may move "
        + "when snapping to the nearest model vertex. Set <= 0 to allow any distance."
    )]
    [SerializeField]
    private float maxSnapDistance = 0.05f;

    [Tooltip(
        "When enabled, prints the original AI point, snapped point and snap distance."
    )]
    [SerializeField]
    private bool printSnapDebug = true;

    [Tooltip(
        "How long the controller waits for the asynchronously loaded GLB meshes "
        + "before building/snapping automatic anchors."
    )]
    [SerializeField]
    private float waitForMeshTimeout = 8f;

    [Tooltip(
        "Polling interval while waiting for MeshFilter/SkinnedMeshRenderer data."
    )]
    [SerializeField]
    private float waitForMeshPollInterval = 0.1f;

    private Coroutine automaticAnchorBuildCoroutine;

    [Header("Automatic Anchor Debug")]

    [SerializeField]
    private bool showAutomaticAnchorMarkers = true;

    [Tooltip(
        "Base local size of the yellow automatic anchor marker. " +
        "Smaller than before to avoid overlap when the camera/model gets close."
    )]
    [SerializeField]
    private float automaticAnchorMarkerSize = 0.0035f;

    [Tooltip(
        "Shrink markers automatically as the camera gets closer, so the yellow dots " +
        "do not grow excessively on screen."
    )]
    [SerializeField]
    private bool adaptAutomaticMarkerSizeToCamera = true;

    [Tooltip(
        "Camera distance at which Automatic Anchor Marker Size is used as-is."
    )]
    [SerializeField]
    private float automaticMarkerReferenceDistance = 2.0f;

    [Tooltip(
        "Minimum multiplier applied to the marker when very close to the camera."
    )]
    [SerializeField, Range(0.15f, 1f)]
    private float automaticMarkerMinScaleMultiplier = 0.30f;

    [Tooltip(
        "Maximum multiplier applied to the marker when far away."
    )]
    [SerializeField, Range(0.5f, 2f)]
    private float automaticMarkerMaxScaleMultiplier = 1.0f;

    [SerializeField]
    private Color automaticAnchorMarkerColor = Color.yellow;

    private readonly List<Transform>
        automaticAnchorDebugMarkers =
            new List<Transform>();

    [Tooltip(
        "Optional: show the original unsnapped backend point in addition to the final snapped point."
    )]
    [SerializeField]
    private bool showOriginalAnchorMarkers = false;

    [SerializeField]
    private float originalAnchorMarkerSize = 0.004f;

    [SerializeField]
    private Color originalAnchorMarkerColor = Color.magenta;

    private readonly Dictionary<string, Transform>
        automaticAnchors =
            new Dictionary<string, Transform>();

    private Transform automaticAnchorContainer;

    private readonly List<Vector3>
        cachedModelSurfaceVertices =
            new List<Vector3>();


    // =========================================================
    // PLACEMENT
    // =========================================================

    [Header("Placement")]

    [SerializeField]
    private bool placementMode = false;

    [SerializeField]
    private string selectedPartId = "";

    [SerializeField]
    private string selectedPartName = "";


    // =========================================================
    // DEBUG
    // =========================================================

    [Header("Debug")]

    [SerializeField]
    private bool printDebug = true;

    [Tooltip(
        "TEST ONLY: when enabled, both student and teacher accounts can place/edit anchors. "
        + "Disable this before production if only teachers should edit anchors."
    )]
    [SerializeField]
    private bool allowAnyLoggedInUserForTesting = false;


    // =========================================================
    // PUBLIC STATE
    // =========================================================

    public bool IsPlacementMode =>
        placementMode;

    public Transform ModelRoot =>
        modelRoot;

    public string SelectedPartId =>
        selectedPartId;

    public string SelectedPartName =>
        selectedPartName;


    // =========================================================
    // UNITY
    // =========================================================

    private void Start()
    {
        if (targetCamera == null)
        {
            targetCamera =
                Camera.main;
        }

        if (detailService == null)
        {
            detailService =
                FindFirstObjectByType<
                    VRModelDetailService
                >();
        }

        if (detailService != null)
        {
            detailService.OnModelPartsLoaded -=
                HandleAutomaticPartsLoaded;

            detailService.OnModelPartsLoaded +=
                HandleAutomaticPartsLoaded;
        }

        if (
            autoFindRuntimeModel &&
            modelRoot == null
        )
        {
            StartCoroutine(
                FindRuntimeModelCoroutine()
            );
        }
    }


    private void Update()
    {
        if (!placementMode)
        {
            return;
        }

        if (targetCamera == null)
        {
            targetCamera =
                Camera.main;
        }

        if (targetCamera == null)
        {
            return;
        }

        // Do not place an anchor while the teacher is pressing a UI control.
        if (
            EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject()
        )
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            TryPlaceAnchorFromMouse();
        }
    }


    private void LateUpdate()
    {
        SyncAutomaticAnchorContainerTransform();
        UpdateAutomaticAnchorMarkerSizes();
    }


    // =========================================================
    // RUNTIME MODEL
    // =========================================================

    private IEnumerator FindRuntimeModelCoroutine()
    {
        float elapsed =
            0f;

        if (printDebug)
        {
            Debug.Log(
                "[VRModelDetailAnchorController] "
                + "Waiting for runtime model..."
            );
        }

        while (
            elapsed <
            findModelTimeout
        )
        {
            GameObject anchor =
                GameObject.Find(
                    runtimeAnchorName
                );

            if (anchor != null)
            {
                Transform runtimeModel =
                    FindLessonModel(
                        anchor.transform
                    );

                if (runtimeModel != null)
                {
                    SetModelRoot(
                        runtimeModel
                    );

                    yield break;
                }
            }

            elapsed +=
                Time.unscaledDeltaTime;

            yield return null;
        }

        Debug.LogWarning(
            "[VRModelDetailAnchorController] "
            + "Could not find runtime model within "
            + findModelTimeout
            + " seconds."
        );
    }


    private Transform FindLessonModel(
        Transform anchor
    )
    {
        if (anchor == null)
        {
            return null;
        }

        for (
            int i = 0;
            i < anchor.childCount;
            i++
        )
        {
            Transform child =
                anchor.GetChild(i);

            if (
                child.name.StartsWith(
                    "VRLessonModel_",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return child;
            }
        }

        if (anchor.childCount > 0)
        {
            return anchor.GetChild(0);
        }

        return null;
    }


    public void SetModelRoot(
        Transform newModelRoot
    )
    {
        modelRoot =
            newModelRoot;

        if (printDebug)
        {
            Debug.Log(
                "[VRModelDetailAnchorController] "
                + "Runtime model assigned: "
                + (
                    modelRoot != null
                        ? modelRoot.name
                        : "NULL"
                )
            );
        }

        TryBuildAutomaticAnchors();
    }


    // =========================================================
    // AUTOMATIC AI ANCHOR BUILD
    // =========================================================

    private void HandleAutomaticPartsLoaded(
        List<VRModelDetailService.ModelPartData> parts
    )
    {
        TryBuildAutomaticAnchors();
    }


    public void RebuildAutomaticAnchors()
    {
        ClearAutomaticAnchors();
        TryBuildAutomaticAnchors();
    }


    public bool TryGetAutomaticAnchor(
        string partKey,
        out Transform anchorTransform
    )
    {
        anchorTransform = null;

        if (string.IsNullOrWhiteSpace(partKey))
        {
            return false;
        }

        return automaticAnchors.TryGetValue(
            partKey.Trim(),
            out anchorTransform
        ) &&
        anchorTransform != null;
    }


    public Vector3 GetPartWorldPosition(
        VRModelDetailService.ModelPartData part
    )
    {
        if (
            part == null ||
            modelRoot == null ||
            detailService == null ||
            !detailService.HasAnchor(part)
        )
        {
            return Vector3.zero;
        }

        if (
            !string.IsNullOrWhiteSpace(part.part_key) &&
            TryGetAutomaticAnchor(
                part.part_key,
                out Transform finalAnchor
            ) &&
            finalAnchor != null
        )
        {
            return finalAnchor.position;
        }

        Vector3 localPoint =
            ConvertBackendAnchorToUnityLocal(
                detailService.GetAnchorPosition(part)
            );

        return modelRoot.TransformPoint(
            localPoint
        );
    }


    public Vector3 ConvertBackendAnchorToUnityLocal(
        Vector3 backendAnchor
    )
    {
        Vector3 converted =
            backendAnchor;

        if (flipAnchorX)
        {
            converted.x = -converted.x;
        }

        if (flipAnchorY)
        {
            converted.y = -converted.y;
        }

        if (flipAnchorZ)
        {
            converted.z = -converted.z;
        }

        converted *=
            anchorCoordinateScale;

        converted +=
            anchorCoordinateOffset;

        return converted;
    }


    private void TryBuildAutomaticAnchors()
    {
        if (
            !buildAutomaticAnchors ||
            modelRoot == null ||
            detailService == null ||
            detailService.CurrentParts == null ||
            detailService.CurrentParts.Count == 0
        )
        {
            return;
        }

        if (automaticAnchorBuildCoroutine != null)
        {
            StopCoroutine(
                automaticAnchorBuildCoroutine
            );
        }

        automaticAnchorBuildCoroutine =
            StartCoroutine(
                WaitForMeshThenBuildAutomaticAnchors()
            );
    }


    private IEnumerator WaitForMeshThenBuildAutomaticAnchors()
    {
        float elapsed =
            0f;

        if (printSnapDebug)
        {
            Debug.Log(
                "[VRModelDetailAnchorController] "
                + "Waiting for runtime mesh data before snapping anchors..."
            );
        }

        while (
            elapsed <
            waitForMeshTimeout
        )
        {
            if (HasRuntimeMeshData())
            {
                if (printSnapDebug)
                {
                    Debug.Log(
                        "[VRModelDetailAnchorController] "
                        + "Runtime mesh data is ready after "
                        + elapsed.ToString("F2")
                        + "s."
                    );
                }

                automaticAnchorBuildCoroutine =
                    null;

                BuildAutomaticAnchorsNow();

                yield break;
            }

            float delay =
                Mathf.Max(
                    0.02f,
                    waitForMeshPollInterval
                );

            yield return new WaitForSecondsRealtime(
                delay
            );

            elapsed +=
                delay;
        }

        Debug.LogWarning(
            "[VRModelDetailAnchorController] "
            + "Timed out waiting for runtime mesh data after "
            + waitForMeshTimeout.ToString("F2")
            + "s. Anchors will still be created, but surface snap may be unavailable."
        );

        automaticAnchorBuildCoroutine =
            null;

        BuildAutomaticAnchorsNow();
    }


    private bool HasRuntimeMeshData()
    {
        if (modelRoot == null)
        {
            return false;
        }

        MeshFilter[] meshFilters =
            modelRoot.GetComponentsInChildren<MeshFilter>(
                true
            );

        for (
            int i = 0;
            i < meshFilters.Length;
            i++
        )
        {
            if (
                meshFilters[i] != null &&
                meshFilters[i].sharedMesh != null &&
                meshFilters[i].sharedMesh.vertexCount > 0
            )
            {
                return true;
            }
        }

        SkinnedMeshRenderer[] skinnedRenderers =
            modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(
                true
            );

        for (
            int i = 0;
            i < skinnedRenderers.Length;
            i++
        )
        {
            if (
                skinnedRenderers[i] != null &&
                skinnedRenderers[i].sharedMesh != null &&
                skinnedRenderers[i].sharedMesh.vertexCount > 0
            )
            {
                return true;
            }
        }

        return false;
    }


    private void BuildAutomaticAnchorsNow()
    {
        if (
            !buildAutomaticAnchors ||
            modelRoot == null ||
            detailService == null ||
            detailService.CurrentParts == null ||
            detailService.CurrentParts.Count == 0
        )
        {
            return;
        }

        ClearAutomaticAnchors();

        // Build a one-time vertex cache before creating anchors.
        // The cache is expressed in modelRoot local coordinates.
        BuildModelSurfaceVertexCache();

        GameObject containerObject =
            new GameObject(
                "AIModelDetailAnchors"
            );

        automaticAnchorContainer =
            containerObject.transform;

        // IMPORTANT:
        // Keep AI anchor/debug objects OUTSIDE the runtime model hierarchy.
        // ModelStructureScanner recursively scans modelRoot, so parenting debug
        // spheres under the model incorrectly increases its node/mesh counts.
        Transform externalParent =
            modelRoot.parent;

        automaticAnchorContainer.SetParent(
            externalParent,
            false
        );

        CopyModelRootLocalTransformToAnchorContainer();

        int createdCount = 0;

        for (
            int i = 0;
            i < detailService.CurrentParts.Count;
            i++
        )
        {
            VRModelDetailService.ModelPartData part =
                detailService.CurrentParts[i];

            if (
                part == null ||
                !part.is_active ||
                !detailService.HasAnchor(part)
            )
            {
                continue;
            }

            Vector3 backendPoint =
                detailService.GetAnchorPosition(
                    part
                );

            Vector3 originalLocalPoint =
                ConvertBackendAnchorToUnityLocal(
                    backendPoint
                );

            Vector3 finalLocalPoint =
                originalLocalPoint;

            float snapDistance =
                0f;

            bool snapped =
                false;

            if (
                snapAutomaticAnchorsToMesh &&
                cachedModelSurfaceVertices.Count > 0
            )
            {
                snapped =
                    TrySnapToNearestModelVertex(
                        originalLocalPoint,
                        out finalLocalPoint,
                        out snapDistance
                    );
            }

            string safePartKey =
                string.IsNullOrWhiteSpace(
                    part.part_key
                )
                    ? i.ToString()
                    : part.part_key.Trim();

            GameObject anchorObject =
                new GameObject(
                    "AIAnchor_"
                    + safePartKey
                );

            Transform anchorTransform =
                anchorObject.transform;

            anchorTransform.SetParent(
                automaticAnchorContainer,
                false
            );

            anchorTransform.localPosition =
                finalLocalPoint;

            automaticAnchors[
                safePartKey
            ] =
                anchorTransform;

            if (showAutomaticAnchorMarkers)
            {
                CreateDebugMarker(
                    anchorTransform
                );
            }

            if (
                showOriginalAnchorMarkers &&
                snapped
            )
            {
                CreateOriginalDebugMarker(
                    originalLocalPoint,
                    safePartKey
                );
            }

            createdCount++;

            if (printDebug)
            {
                Debug.Log(
                    "[VRModelDetailAnchorController] "
                    + "AI anchor created: "
                    + part.part_name
                    + " | backend="
                    + backendPoint.ToString("F4")
                    + " | originalLocal="
                    + originalLocalPoint.ToString("F4")
                    + " | finalLocal="
                    + finalLocalPoint.ToString("F4")
                    + " | snapped="
                    + snapped
                    + " | snapDistance="
                    + snapDistance.ToString("F5")
                    + " | world="
                    + anchorTransform.position.ToString("F4")
                );
            }
        }

        Debug.Log(
            "[VRModelDetailAnchorController] "
            + "Automatic AI anchors ready. Count = "
            + createdCount
            + " | SurfaceVertices = "
            + cachedModelSurfaceVertices.Count
        );
    }


    private void BuildModelSurfaceVertexCache()
    {
        cachedModelSurfaceVertices.Clear();

        if (modelRoot == null)
        {
            return;
        }

        MeshFilter[] meshFilters =
            modelRoot.GetComponentsInChildren<MeshFilter>(
                true
            );

        for (
            int i = 0;
            i < meshFilters.Length;
            i++
        )
        {
            MeshFilter filter =
                meshFilters[i];

            if (
                filter == null ||
                filter.sharedMesh == null
            )
            {
                continue;
            }

            Vector3[] vertices =
                filter.sharedMesh.vertices;

            Transform meshTransform =
                filter.transform;

            for (
                int v = 0;
                v < vertices.Length;
                v++
            )
            {
                Vector3 worldPoint =
                    meshTransform.TransformPoint(
                        vertices[v]
                    );

                cachedModelSurfaceVertices.Add(
                    modelRoot.InverseTransformPoint(
                        worldPoint
                    )
                );
            }
        }

        // Support skinned models too. If a renderer shares the same mesh as a
        // MeshFilter this may add duplicate candidate points, which is harmless
        // for nearest-point selection.
        SkinnedMeshRenderer[] skinnedRenderers =
            modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(
                true
            );

        for (
            int i = 0;
            i < skinnedRenderers.Length;
            i++
        )
        {
            SkinnedMeshRenderer renderer =
                skinnedRenderers[i];

            if (
                renderer == null ||
                renderer.sharedMesh == null
            )
            {
                continue;
            }

            Mesh bakedMesh =
                new Mesh();

            renderer.BakeMesh(
                bakedMesh
            );

            Vector3[] vertices =
                bakedMesh.vertices;

            for (
                int v = 0;
                v < vertices.Length;
                v++
            )
            {
                Vector3 worldPoint =
                    renderer.transform.TransformPoint(
                        vertices[v]
                    );

                cachedModelSurfaceVertices.Add(
                    modelRoot.InverseTransformPoint(
                        worldPoint
                    )
                );
            }

            Destroy(
                bakedMesh
            );
        }

        if (printSnapDebug)
        {
            Debug.Log(
                "[VRModelDetailAnchorController] "
                + "Surface vertex cache built. Count = "
                + cachedModelSurfaceVertices.Count
            );
        }
    }


    private bool TrySnapToNearestModelVertex(
        Vector3 originalLocalPoint,
        out Vector3 snappedLocalPoint,
        out float snapDistance
    )
    {
        snappedLocalPoint =
            originalLocalPoint;

        snapDistance =
            0f;

        if (
            cachedModelSurfaceVertices == null ||
            cachedModelSurfaceVertices.Count == 0
        )
        {
            return false;
        }

        float bestSqrDistance =
            float.PositiveInfinity;

        Vector3 bestPoint =
            originalLocalPoint;

        for (
            int i = 0;
            i < cachedModelSurfaceVertices.Count;
            i++
        )
        {
            Vector3 candidate =
                cachedModelSurfaceVertices[i];

            float sqrDistance =
                (
                    candidate -
                    originalLocalPoint
                ).sqrMagnitude;

            if (
                sqrDistance <
                bestSqrDistance
            )
            {
                bestSqrDistance =
                    sqrDistance;

                bestPoint =
                    candidate;
            }
        }

        if (
            float.IsInfinity(
                bestSqrDistance
            )
        )
        {
            return false;
        }

        snapDistance =
            Mathf.Sqrt(
                bestSqrDistance
            );

        if (
            maxSnapDistance > 0f &&
            snapDistance > maxSnapDistance
        )
        {
            if (printSnapDebug)
            {
                Debug.LogWarning(
                    "[VRModelDetailAnchorController] "
                    + "Nearest model vertex is farther than Max Snap Distance. "
                    + "Original point kept. Distance = "
                    + snapDistance.ToString("F5")
                    + " | Max = "
                    + maxSnapDistance.ToString("F5")
                );
            }

            snappedLocalPoint =
                originalLocalPoint;

            return false;
        }

        snappedLocalPoint =
            bestPoint;

        return true;
    }


    private void CopyModelRootLocalTransformToAnchorContainer()
    {
        if (
            modelRoot == null ||
            automaticAnchorContainer == null
        )
        {
            return;
        }

        automaticAnchorContainer.localPosition =
            modelRoot.localPosition;

        automaticAnchorContainer.localRotation =
            modelRoot.localRotation;

        automaticAnchorContainer.localScale =
            modelRoot.localScale;
    }


    private void SyncAutomaticAnchorContainerTransform()
    {
        if (
            modelRoot == null ||
            automaticAnchorContainer == null
        )
        {
            return;
        }

        // Since the anchor container is a sibling of modelRoot, mirror the
        // runtime model's local transform so anchors continue following any
        // model movement, rotation or scale without becoming scanner children.
        CopyModelRootLocalTransformToAnchorContainer();
    }


    private void CreateOriginalDebugMarker(
        Vector3 originalLocalPoint,
        string partKey
    )
    {
        if (automaticAnchorContainer == null)
        {
            return;
        }

        GameObject marker =
            GameObject.CreatePrimitive(
                PrimitiveType.Sphere
            );

        marker.name =
            "OriginalAIAnchor_"
            + partKey;

        marker.transform.SetParent(
            automaticAnchorContainer,
            false
        );

        marker.transform.localPosition =
            originalLocalPoint;

        marker.transform.localScale =
            Vector3.one
            * originalAnchorMarkerSize;

        Collider markerCollider =
            marker.GetComponent<Collider>();

        if (markerCollider != null)
        {
            Destroy(
                markerCollider
            );
        }

        Renderer renderer =
            marker.GetComponent<Renderer>();

        if (renderer != null)
        {
            Shader shader =
                Shader.Find(
                    "Universal Render Pipeline/Unlit"
                )
                ??
                Shader.Find(
                    "Unlit/Color"
                );

            if (shader != null)
            {
                Material material =
                    new Material(
                        shader
                    );

                material.color =
                    originalAnchorMarkerColor;

                renderer.material =
                    material;
            }
        }
    }


    private void CreateDebugMarker(
        Transform anchorTransform
    )
    {
        GameObject marker =
            GameObject.CreatePrimitive(
                PrimitiveType.Sphere
            );

        marker.name =
            "DebugMarker";

        marker.transform.SetParent(
            anchorTransform,
            false
        );

        marker.transform.localPosition =
            Vector3.zero;

        marker.transform.localScale =
            Vector3.one
            * automaticAnchorMarkerSize;

        automaticAnchorDebugMarkers.Add(
            marker.transform
        );

        Collider markerCollider =
            marker.GetComponent<Collider>();

        if (markerCollider != null)
        {
            Destroy(
                markerCollider
            );
        }

        Renderer renderer =
            marker.GetComponent<Renderer>();

        if (renderer != null)
        {
            Shader shader =
                Shader.Find(
                    "Universal Render Pipeline/Unlit"
                )
                ??
                Shader.Find(
                    "Unlit/Color"
                );

            if (shader != null)
            {
                Material material =
                    new Material(
                        shader
                    );

                material.color =
                    automaticAnchorMarkerColor;

                renderer.material =
                    material;
            }
        }
    }


    private void UpdateAutomaticAnchorMarkerSizes()
    {
        if (
            automaticAnchorDebugMarkers.Count == 0
        )
        {
            return;
        }

        if (targetCamera == null)
        {
            targetCamera =
                Camera.main;
        }

        float referenceDistance =
            Mathf.Max(
                0.05f,
                automaticMarkerReferenceDistance
            );

        for (
            int i = automaticAnchorDebugMarkers.Count - 1;
            i >= 0;
            i--
        )
        {
            Transform marker =
                automaticAnchorDebugMarkers[i];

            if (marker == null)
            {
                automaticAnchorDebugMarkers.RemoveAt(i);
                continue;
            }

            float multiplier =
                1f;

            if (
                adaptAutomaticMarkerSizeToCamera &&
                targetCamera != null
            )
            {
                float distance =
                    Vector3.Distance(
                        targetCamera.transform.position,
                        marker.position
                    );

                multiplier =
                    Mathf.Clamp(
                        distance / referenceDistance,
                        automaticMarkerMinScaleMultiplier,
                        automaticMarkerMaxScaleMultiplier
                    );
            }

            marker.localScale =
                Vector3.one
                * automaticAnchorMarkerSize
                * multiplier;
        }
    }


    private void ClearAutomaticAnchors()
    {
        automaticAnchors.Clear();
        cachedModelSurfaceVertices.Clear();
        automaticAnchorDebugMarkers.Clear();

        if (automaticAnchorContainer != null)
        {
            Destroy(
                automaticAnchorContainer.gameObject
            );

            automaticAnchorContainer =
                null;
        }
    }


    // =========================================================
    // PLACEMENT MODE
    // =========================================================

    public void BeginPlacement(
        string partId,
        string partName
    )
    {
        // The UPDATE RLS policy is based on auth.uid(), so the request must
        // come from a real authenticated Supabase session.
        if (!SupabaseSession.IsLoggedIn)
        {
            Debug.LogError(
                "[VRModelDetailAnchorController] "
                + "Cannot place anchor because the Supabase session "
                + "is not logged in."
            );

            return;
        }

        if (
            !allowAnyLoggedInUserForTesting &&
            !SupabaseSession.IsTeacher
        )
        {
            Debug.LogWarning(
                "[VRModelDetailAnchorController] "
                + "Only teachers can place or edit model anchors."
            );

            return;
        }

        if (
            string.IsNullOrWhiteSpace(
                partId
            )
        )
        {
            Debug.LogError(
                "[VRModelDetailAnchorController] "
                + "BeginPlacement received an invalid partId."
            );

            return;
        }

        if (modelRoot == null)
        {
            Debug.LogWarning(
                "[VRModelDetailAnchorController] "
                + "The runtime model is not ready yet."
            );

            return;
        }

        selectedPartId =
            partId.Trim();

        selectedPartName =
            string.IsNullOrWhiteSpace(
                partName
            )
                ? selectedPartId
                : partName.Trim();

        placementMode =
            true;

        if (printDebug)
        {
            Debug.Log(
                "[VRModelDetailAnchorController] "
                + "Placement mode ON for: "
                + selectedPartName
                + " | User = "
                + SupabaseSession.UserId
                + " | Role = "
                + SupabaseSession.Role
                + " | AnyUserTest = "
                + allowAnyLoggedInUserForTesting
            );
        }
    }


    public void BeginPlacementForPart(
        VRModelDetailService.ModelPartData part
    )
    {
        if (part == null)
        {
            Debug.LogError(
                "[VRModelDetailAnchorController] "
                + "BeginPlacementForPart received NULL."
            );

            return;
        }

        BeginPlacement(
            part.id,
            part.part_name
        );
    }


    public void CancelPlacement()
    {
        placementMode =
            false;

        selectedPartId =
            "";

        selectedPartName =
            "";

        if (printDebug)
        {
            Debug.Log(
                "[VRModelDetailAnchorController] "
                + "Placement cancelled."
            );
        }
    }


    // =========================================================
    // MOUSE RAYCAST
    // =========================================================

    private void TryPlaceAnchorFromMouse()
    {
        if (modelRoot == null)
        {
            Debug.LogWarning(
                "[VRModelDetailAnchorController] "
                + "modelRoot is NULL."
            );

            return;
        }

        if (
            string.IsNullOrWhiteSpace(
                selectedPartId
            )
        )
        {
            Debug.LogWarning(
                "[VRModelDetailAnchorController] "
                + "No model part is selected."
            );

            return;
        }

        if (!TryGetValidMouseRay(out Ray ray))
        {
            return;
        }

        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                100f,
                ~0,
                QueryTriggerInteraction.Ignore
            );

        Array.Sort(
            hits,
            (a, b) =>
                a.distance.CompareTo(
                    b.distance
                )
        );

        foreach (
            RaycastHit hit
            in hits
        )
        {
            if (hit.collider == null)
            {
                continue;
            }

            Transform hitTransform =
                hit.collider.transform;

            bool belongsToModel =
                hitTransform ==
                    modelRoot
                ||
                hitTransform.IsChildOf(
                    modelRoot
                );

            if (!belongsToModel)
            {
                continue;
            }

            Vector3 localPoint =
                modelRoot.InverseTransformPoint(
                    hit.point
                );

            if (printDebug)
            {
                Debug.Log(
                    "[VRModelDetailAnchorController] "
                    + "Anchor selected for "
                    + selectedPartName
                    + " at local position: "
                    + localPoint
                );
            }

            string partIdToSave =
                selectedPartId;

            string partNameToSave =
                selectedPartName;

            // Disable placement immediately to prevent multiple PATCH requests.
            placementMode =
                false;

            StartCoroutine(
                SaveAnchorCoroutine(
                    partIdToSave,
                    partNameToSave,
                    localPoint
                )
            );

            return;
        }

        Debug.LogWarning(
            "[VRModelDetailAnchorController] "
            + "The click did not hit the current model."
        );
    }


    private bool TryGetValidMouseRay(
        out Ray ray
    )
    {
        ray =
            default;

        if (targetCamera == null)
        {
            return false;
        }

        Vector3 mousePosition =
            Input.mousePosition;

        if (
            !IsFinite(mousePosition.x) ||
            !IsFinite(mousePosition.y) ||
            !IsFinite(mousePosition.z)
        )
        {
            return false;
        }

        Rect pixelRect =
            targetCamera.pixelRect;

        if (
            pixelRect.width <= 0f ||
            pixelRect.height <= 0f
        )
        {
            return false;
        }

        if (
            !pixelRect.Contains(
                new Vector2(
                    mousePosition.x,
                    mousePosition.y
                )
            )
        )
        {
            return false;
        }

        ray =
            targetCamera.ScreenPointToRay(
                mousePosition
            );

        return
            IsFinite(ray.origin.x) &&
            IsFinite(ray.origin.y) &&
            IsFinite(ray.origin.z) &&
            IsFinite(ray.direction.x) &&
            IsFinite(ray.direction.y) &&
            IsFinite(ray.direction.z);
    }


    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(value) &&
            !float.IsInfinity(value);
    }


    // =========================================================
    // SAVE TO SUPABASE
    // =========================================================

    private IEnumerator SaveAnchorCoroutine(
        string partId,
        string partName,
        Vector3 localPoint
    )
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            Debug.LogError(
                "[VRModelDetailAnchorController] "
                + "Session expired or user is no longer logged in."
            );

            yield break;
        }

        if (
            !allowAnyLoggedInUserForTesting &&
            !SupabaseSession.IsTeacher
        )
        {
            Debug.LogWarning(
                "[VRModelDetailAnchorController] "
                + "Only teachers can update model anchors."
            );

            yield break;
        }

        AnchorUpdatePayload payload =
            new AnchorUpdatePayload
            {
                anchor_x =
                    localPoint.x,

                anchor_y =
                    localPoint.y,

                anchor_z =
                    localPoint.z,

                is_verified =
                    true
            };

        string json =
            JsonUtility.ToJson(
                payload
            );

        string tableAndQuery =
            "model_parts"
            + "?id=eq."
            + UnityWebRequest.EscapeURL(
                partId
            );

        bool succeeded =
            false;

        string responseText =
            "";

        string errorText =
            "";

        // IMPORTANT:
        // SupabaseRestService automatically sends:
        //
        // apikey: SupabaseConfig.PublishableKey
        // Authorization: Bearer SupabaseSession.AccessToken
        //
        // so Supabase RLS can correctly evaluate auth.uid().
        yield return SupabaseRestService.Patch(
            tableAndQuery,
            json,

            onSuccess:
                response =>
                {
                    succeeded =
                        true;

                    responseText =
                        response;
                },

            onError:
                error =>
                {
                    errorText =
                        error;
                },

            returnRepresentation:
                true
        );

        if (!succeeded)
        {
            Debug.LogError(
                "[VRModelDetailAnchorController] "
                + "Failed to save anchor for "
                + partName
                + "."
                + "\n"
                + errorText
            );

            yield break;
        }

        Debug.Log(
            "[VRModelDetailAnchorController] "
            + "Anchor saved successfully for "
            + partName
            + "."
            + "\nResponse: "
            + responseText
        );

        selectedPartId =
            "";

        selectedPartName =
            "";

        if (detailService != null)
        {
            detailService
                .LoadCurrentModelParts();
        }
    }


    // =========================================================
    // PAYLOAD
    // =========================================================

    [Serializable]
    private class AnchorUpdatePayload
    {
        public float anchor_x;

        public float anchor_y;

        public float anchor_z;

        public bool is_verified;
    }


    // =========================================================
    // DEBUG
    // =========================================================

    [ContextMenu("Cancel Placement")]
    private void DebugCancelPlacement()
    {
        CancelPlacement();
    }


    // =========================================================
    // DEBUG - TEST AORTA
    // =========================================================

    [ContextMenu("TEST - Place Aorta")]
    private void DebugPlaceAorta()
    {
        // ContextMenu can also be pressed while the Editor is not playing.
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "[VRModelDetailAnchorController] "
                + "TEST - Place Aorta only works in Play Mode."
            );

            return;
        }


        if (detailService == null)
        {
            detailService =
                FindFirstObjectByType<
                    VRModelDetailService
                >();
        }


        if (detailService == null)
        {
            Debug.LogError(
                "[VRModelDetailAnchorController] "
                + "VRModelDetailService not found."
            );

            return;
        }


        // If model parts are not ready yet, subscribe to the service event
        // and trigger the normal resolve/load pipeline.
        if (
            detailService.CurrentParts == null ||
            detailService.CurrentParts.Count == 0
        )
        {
            Debug.Log(
                "[VRModelDetailAnchorController] "
                + "Model parts are not loaded yet. "
                + "Waiting for VRModelDetailService..."
            );


            detailService.OnModelPartsLoaded -=
                HandlePartsLoadedForAortaTest;


            detailService.OnModelPartsLoaded +=
                HandlePartsLoadedForAortaTest;


            if (
                !string.IsNullOrWhiteSpace(
                    detailService.CurrentAssetId
                )
            )
            {
                detailService
                    .LoadCurrentModelParts();
            }
            else
            {
                detailService
                    .ResolveCurrentModelAsset();
            }


            return;
        }


        TryBeginAortaPlacement();
    }


    private void HandlePartsLoadedForAortaTest(
        System.Collections.Generic.List<
            VRModelDetailService.ModelPartData
        > parts
    )
    {
        if (detailService != null)
        {
            detailService.OnModelPartsLoaded -=
                HandlePartsLoadedForAortaTest;
        }


        Debug.Log(
            "[VRModelDetailAnchorController] "
            + "Model parts finished loading. Count = "
            + (
                parts != null
                    ? parts.Count
                    : 0
            )
        );


        TryBeginAortaPlacement();
    }


    private void TryBeginAortaPlacement()
    {
        if (detailService == null)
        {
            Debug.LogError(
                "[VRModelDetailAnchorController] "
                + "Detail service is NULL."
            );

            return;
        }


        // If runtime model has not been assigned yet, try to resolve it now.
        if (modelRoot == null)
        {
            GameObject anchor =
                GameObject.Find(
                    runtimeAnchorName
                );


            if (anchor != null)
            {
                Transform runtimeModel =
                    FindLessonModel(
                        anchor.transform
                    );


                if (runtimeModel != null)
                {
                    SetModelRoot(
                        runtimeModel
                    );
                }
            }
        }


        if (modelRoot == null)
        {
            Debug.LogWarning(
                "[VRModelDetailAnchorController] "
                + "Aorta is ready, but the runtime model is not ready yet."
            );

            return;
        }


        VRModelDetailService.ModelPartData aorta =
            detailService.GetPartByKey(
                "aorta"
            );


        if (aorta == null)
        {
            Debug.LogError(
                "[VRModelDetailAnchorController] "
                + "Aorta was not found."
                + "\nCurrentParts Count = "
                + detailService.CurrentParts.Count
            );


            for (
                int i = 0;
                i < detailService.CurrentParts.Count;
                i++
            )
            {
                VRModelDetailService.ModelPartData part =
                    detailService.CurrentParts[i];


                if (part == null)
                {
                    continue;
                }


                Debug.Log(
                    "[VRModelDetailAnchorController] "
                    + "Available part: "
                    + part.part_name
                    + " | key = "
                    + part.part_key
                );
            }


            return;
        }


        Debug.Log(
            "[VRModelDetailAnchorController] "
            + "TEST selecting Aorta."
            + "\nPart ID = "
            + aorta.id
        );


        BeginPlacementForPart(
            aorta
        );
    }


    private void OnDestroy()
    {
        if (detailService != null)
        {
            detailService.OnModelPartsLoaded -=
                HandlePartsLoadedForAortaTest;

            detailService.OnModelPartsLoaded -=
                HandleAutomaticPartsLoaded;
        }

        if (automaticAnchorBuildCoroutine != null)
        {
            StopCoroutine(
                automaticAnchorBuildCoroutine
            );

            automaticAnchorBuildCoroutine =
                null;
        }

        ClearAutomaticAnchors();
    }
}
