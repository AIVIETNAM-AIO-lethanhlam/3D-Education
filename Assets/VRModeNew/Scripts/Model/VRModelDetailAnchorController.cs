using System;
using System.Collections;
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
    private bool allowAnyLoggedInUserForTesting = true;


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
        }
    }
}
