using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ModelStructureScanner : MonoBehaviour
{
    [Header("Model Root")]
    [SerializeField]
    private Transform modelRoot;

    [Header("Runtime Auto Find")]
    [SerializeField]
    private bool autoFindRuntimeModel = true;

    [SerializeField]
    private string runtimeAnchorName = "VRRuntimeModelAnchor";

    [SerializeField]
    private float findTimeout = 20f;

    [Header("Wait For GLB")]
    [Tooltip("Số renderer tối thiểu trước khi xem model đã load.")]
    [SerializeField]
    private int minimumRendererCount = 1;

    [Tooltip("Hierarchy phải ổn định trong khoảng thời gian này trước khi scan.")]
    [SerializeField]
    private float hierarchyStableTime = 0.5f;

    [Header("Options")]
    [SerializeField]
    private bool includeInactive = true;

    [SerializeField]
    private bool printMeshInfo = true;

    private readonly List<ModelNodeInfo> scannedNodes =
        new List<ModelNodeInfo>();


    [System.Serializable]
    public class ModelNodeInfo
    {
        public string nodeName;
        public string hierarchyPath;

        public bool hasMesh;
        public string meshName;

        public string rendererType;

        public Vector3 localPosition;
        public Vector3 localScale;
    }


    private void Start()
    {
        if (autoFindRuntimeModel)
        {
            StartCoroutine(
                FindAndWaitForRuntimeModel()
            );
        }
    }


    // =========================================================
    // FIND RUNTIME MODEL
    // =========================================================

    private IEnumerator FindAndWaitForRuntimeModel()
    {
        float elapsed = 0f;

        Debug.Log(
            "[ModelStructureScanner] Waiting for VRRuntimeModelAnchor..."
        );

        Transform runtimeModel = null;

        while (elapsed < findTimeout)
        {
            GameObject anchor =
                GameObject.Find(runtimeAnchorName);

            if (anchor != null)
            {
                runtimeModel =
                    FindRuntimeModelUnderAnchor(
                        anchor.transform
                    );

                if (runtimeModel != null)
                {
                    modelRoot =
                        runtimeModel;

                    Debug.Log(
                        "[ModelStructureScanner] Runtime model wrapper found: "
                        + modelRoot.name
                    );

                    break;
                }
            }

            elapsed +=
                Time.unscaledDeltaTime;

            yield return null;
        }


        if (modelRoot == null)
        {
            Debug.LogError(
                "[ModelStructureScanner] Runtime model was not found."
            );

            yield break;
        }


        // -----------------------------------------------------
        // IMPORTANT:
        // Wrapper exists before GLB finishes importing.
        // Wait until actual Renderers / Meshes appear.
        // -----------------------------------------------------

        Debug.Log(
            "[ModelStructureScanner] Waiting for GLB hierarchy / meshes..."
        );


        elapsed = 0f;

        int previousTransformCount = -1;
        int previousRendererCount = -1;

        float stableTimer = 0f;


        while (elapsed < findTimeout)
        {
            int transformCount =
                modelRoot
                    .GetComponentsInChildren<Transform>(true)
                    .Length;

            Renderer[] renderers =
                modelRoot
                    .GetComponentsInChildren<Renderer>(true);

            int rendererCount =
                renderers != null
                    ? renderers.Length
                    : 0;


            bool enoughRenderers =
                rendererCount >=
                minimumRendererCount;


            bool hierarchyUnchanged =
                transformCount ==
                previousTransformCount &&
                rendererCount ==
                previousRendererCount;


            if (enoughRenderers &&
                hierarchyUnchanged)
            {
                stableTimer +=
                    Time.unscaledDeltaTime;
            }
            else
            {
                stableTimer = 0f;
            }


            previousTransformCount =
                transformCount;

            previousRendererCount =
                rendererCount;


            if (enoughRenderers &&
                stableTimer >= hierarchyStableTime)
            {
                Debug.Log(
                    "[ModelStructureScanner] GLB ready. "
                    + "Transforms = "
                    + transformCount
                    + ", Renderers = "
                    + rendererCount
                );

                // Wait one extra frame.
                yield return null;

                ScanModel();

                yield break;
            }


            elapsed +=
                Time.unscaledDeltaTime;

            yield return null;
        }


        Debug.LogWarning(
            "[ModelStructureScanner] Timed out waiting for GLB. "
            + "Scanning current hierarchy anyway."
        );

        ScanModel();
    }


    private Transform FindRuntimeModelUnderAnchor(
        Transform anchor
    )
    {
        if (anchor == null)
            return null;


        // Preferred:
        // VRRuntimeModelAnchor
        // └── VRLessonModel_human_heart

        for (int i = 0;
             i < anchor.childCount;
             i++)
        {
            Transform child =
                anchor.GetChild(i);

            if (child.name.StartsWith(
                    "VRLessonModel_"
                ))
            {
                return child;
            }
        }


        // Fallback:
        // Use first child.
        if (anchor.childCount > 0)
        {
            return anchor.GetChild(0);
        }


        return null;
    }


    // =========================================================
    // SCAN
    // =========================================================

    public List<ModelNodeInfo> ScanModel()
    {
        scannedNodes.Clear();


        if (modelRoot == null)
        {
            Debug.LogError(
                "[ModelStructureScanner] Model Root is null."
            );

            return scannedNodes;
        }


        Debug.Log(
            "[ModelStructureScanner] "
            + "===== START SCAN: "
            + modelRoot.name
            + " ====="
        );


        ScanRecursive(
            modelRoot,
            modelRoot.name
        );


        int meshNodeCount = 0;

        foreach (ModelNodeInfo info
                 in scannedNodes)
        {
            if (info.hasMesh)
            {
                meshNodeCount++;
            }
        }


        Debug.Log(
            "[ModelStructureScanner] "
            + "===== FINISHED ===== "
            + "TOTAL NODES: "
            + scannedNodes.Count
            + " | MESH NODES: "
            + meshNodeCount
        );


        return scannedNodes;
    }


    private void ScanRecursive(
        Transform current,
        string currentPath
    )
    {
        if (current == null)
            return;


        if (!includeInactive &&
            !current.gameObject.activeInHierarchy)
        {
            return;
        }


        MeshFilter meshFilter =
            current.GetComponent<MeshFilter>();


        SkinnedMeshRenderer skinnedRenderer =
            current.GetComponent<SkinnedMeshRenderer>();


        MeshRenderer meshRenderer =
            current.GetComponent<MeshRenderer>();


        bool hasMesh = false;

        string meshName = "";

        string rendererType = "None";


        // -----------------------------------------------------
        // MeshFilter + MeshRenderer
        // -----------------------------------------------------

        if (meshFilter != null &&
            meshFilter.sharedMesh != null)
        {
            hasMesh = true;

            meshName =
                meshFilter.sharedMesh.name;

            rendererType =
                meshRenderer != null
                    ? "MeshRenderer"
                    : "MeshFilter";
        }


        // -----------------------------------------------------
        // SkinnedMeshRenderer
        // -----------------------------------------------------

        if (skinnedRenderer != null &&
            skinnedRenderer.sharedMesh != null)
        {
            hasMesh = true;

            meshName =
                skinnedRenderer.sharedMesh.name;

            rendererType =
                "SkinnedMeshRenderer";
        }


        ModelNodeInfo info =
            new ModelNodeInfo
            {
                nodeName =
                    current.name,

                hierarchyPath =
                    currentPath,

                hasMesh =
                    hasMesh,

                meshName =
                    meshName,

                rendererType =
                    rendererType,

                localPosition =
                    current.localPosition,

                localScale =
                    current.localScale
            };


        scannedNodes.Add(info);


        // -----------------------------------------------------
        // LOG
        // -----------------------------------------------------

        if (printMeshInfo)
        {
            Debug.Log(
                "[ModelNode] "
                + "Path = "
                + currentPath
                + " | Mesh = "
                + (
                    hasMesh
                        ? meshName
                        : "NONE"
                )
                + " | Renderer = "
                + rendererType
                + " | LocalPos = "
                + current.localPosition
                + " | Scale = "
                + current.localScale
            );
        }
        else
        {
            Debug.Log(
                "[ModelNode] Path = "
                + currentPath
            );
        }


        // -----------------------------------------------------
        // CHILDREN
        // -----------------------------------------------------

        for (int i = 0;
             i < current.childCount;
             i++)
        {
            Transform child =
                current.GetChild(i);


            ScanRecursive(
                child,
                currentPath
                + "/"
                + child.name
            );
        }
    }


    // =========================================================
    // MANUAL SCAN
    // =========================================================

    [ContextMenu("Scan Model Structure")]
    private void ScanFromInspector()
    {
        ScanModel();
    }
}