using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class VRModelDetailService : MonoBehaviour
{
    // =========================================================
    // SUPABASE
    // =========================================================

    [Header("Supabase")]

    [SerializeField]
    private string supabaseUrl =
        "https://nfribubvehdzjyguxejq.supabase.co";

    [SerializeField]
    private string supabaseAnonKey =
        "sb_publishable_1QTg4NVH-lYBBAt1qOQHYw_th1pWtIf";


    // =========================================================
    // CURRENT MODEL
    // =========================================================

    [Header("Current Model")]

    [Tooltip(
        "Normally leave this empty. " +
        "It will be resolved automatically from selected_lesson_id."
    )]
    [SerializeField]
    private string assetId = "";


    [Tooltip(
        "Automatically find the model_3d asset " +
        "belonging to selected_lesson_id."
    )]
    [SerializeField]
    private bool autoResolveAssetFromLesson = true;


    [SerializeField]
    private string lessonIdPlayerPrefsKey =
        "selected_lesson_id";


    // =========================================================
    // DEBUG
    // =========================================================

    [Header("Debug")]

    [SerializeField]
    private bool loadOnStart = true;


    [SerializeField]
    private bool printPartsToConsole = true;


    // =========================================================
    // STATE
    // =========================================================

    public List<ModelPartData> CurrentParts
    {
        get;
        private set;
    }
    =
    new List<ModelPartData>();


    public string CurrentAssetId
    {
        get
        {
            return assetId;
        }
    }


    public bool IsLoading
    {
        get;
        private set;
    }


    // =========================================================
    // EVENTS
    // =========================================================

    /// <summary>
    /// Called after model_parts are loaded successfully.
    /// </summary>
    public event Action<List<ModelPartData>>
        OnModelPartsLoaded;


    /// <summary>
    /// Called when loading fails.
    /// </summary>
    public event Action<string>
        OnModelPartsLoadFailed;


    // =========================================================
    // DATA CLASSES
    // =========================================================

    [Serializable]
    public class ModelPartData
    {
        public string id;

        public string asset_id;

        public string part_key;

        public string part_name;

        public string node_name;

        public string description;

        public string structure_description;

        public string function_description;


        // -----------------------------------------------------
        // Anchor
        // -----------------------------------------------------

        public float? anchor_x;

        public float? anchor_y;

        public float? anchor_z;


        // -----------------------------------------------------
        // Label offset
        // -----------------------------------------------------

        public float? label_offset_x;

        public float? label_offset_y;

        public float? label_offset_z;


        public int display_order;

        public string source;

        public bool is_verified;

        public bool is_active;

        public float? ai_confidence;
    }


    [Serializable]
    private class ModelAssetData
    {
        public string id;

        public string lesson_id;

        public string asset_type;

        public string file_name;

        public string file_extension;

        public int display_order;
    }


    [Serializable]
    private class JsonArrayWrapper<T>
    {
        public T[] items;
    }


    // =========================================================
    // UNITY
    // =========================================================

    private void Start()
    {
        if (!loadOnStart)
        {
            return;
        }


        if (autoResolveAssetFromLesson)
        {
            ResolveCurrentModelAsset();
        }
        else
        {
            LoadCurrentModelParts();
        }
    }


    // =========================================================
    // PUBLIC - RESOLVE MODEL
    // =========================================================

    /// <summary>
    /// Resolve lesson_assets.id automatically
    /// from PlayerPrefs selected_lesson_id.
    /// </summary>
    public void ResolveCurrentModelAsset()
    {
        string lessonId =
            PlayerPrefs.GetString(
                lessonIdPlayerPrefsKey,
                ""
            );


        if (string.IsNullOrWhiteSpace(lessonId))
        {
            string message =
                "[VRModelDetailService] "
                + lessonIdPlayerPrefsKey
                + " is missing from PlayerPrefs.";


            Debug.LogError(
                message
            );


            OnModelPartsLoadFailed
                ?.Invoke(
                    message
                );


            return;
        }


        Debug.Log(
            "[VRModelDetailService] "
            + "Resolving model asset for lesson: "
            + lessonId
        );


        StartCoroutine(
            ResolveAssetCoroutine(
                lessonId
            )
        );
    }


    /// <summary>
    /// Resolve model asset using a specific lesson ID.
    /// Useful if we already know the lesson ID.
    /// </summary>
    public void ResolveModelAssetForLesson(
        string lessonId
    )
    {
        if (string.IsNullOrWhiteSpace(lessonId))
        {
            Debug.LogError(
                "[VRModelDetailService] "
                + "ResolveModelAssetForLesson received empty lessonId."
            );

            return;
        }


        StartCoroutine(
            ResolveAssetCoroutine(
                lessonId.Trim()
            )
        );
    }


    // =========================================================
    // RESOLVE LESSON -> MODEL ASSET
    // =========================================================

    private IEnumerator ResolveAssetCoroutine(
        string lessonId
    )
    {
        if (IsLoading)
        {
            Debug.LogWarning(
                "[VRModelDetailService] "
                + "A request is already running."
            );

            yield break;
        }


        IsLoading = true;


        string requestUrl =
            NormalizeSupabaseUrl(
                supabaseUrl
            )
            + "/rest/v1/lesson_assets"
            + "?lesson_id=eq."
            + UnityWebRequest.EscapeURL(
                lessonId
            )
            + "&asset_type=eq.model_3d"
            + "&select="
            + "id,"
            + "lesson_id,"
            + "asset_type,"
            + "file_name,"
            + "file_extension,"
            + "display_order"
            + "&order=display_order.asc"
            + "&limit=1";


        Debug.Log(
            "[VRModelDetailService] "
            + "Looking for model_3d in lesson_assets..."
        );


        using UnityWebRequest request =
            UnityWebRequest.Get(
                requestUrl
            );


        AddSupabaseHeaders(
            request
        );


        yield return
            request.SendWebRequest();


        if (
            request.result !=
            UnityWebRequest.Result.Success
        )
        {
            IsLoading = false;


            string message =
                "[VRModelDetailService] "
                + "Could not resolve model asset."
                + "\nHTTP: "
                + request.responseCode
                + "\nError: "
                + request.error
                + "\nResponse: "
                + request.downloadHandler.text;


            Debug.LogError(
                message
            );


            OnModelPartsLoadFailed
                ?.Invoke(
                    message
                );


            yield break;
        }


        string json =
            request.downloadHandler.text;


        Debug.Log(
            "[VRModelDetailService] "
            + "Model asset response: "
            + json
        );


        ModelAssetData[] assets =
            ParseJsonArray<ModelAssetData>(
                json
            );


        if (
            assets == null ||
            assets.Length == 0
        )
        {
            IsLoading = false;


            string message =
                "[VRModelDetailService] "
                + "No model_3d asset found for lesson: "
                + lessonId;


            Debug.LogWarning(
                message
            );


            OnModelPartsLoadFailed
                ?.Invoke(
                    message
                );


            yield break;
        }


        ModelAssetData model =
            assets[0];


        if (
            model == null ||
            string.IsNullOrWhiteSpace(
                model.id
            )
        )
        {
            IsLoading = false;


            string message =
                "[VRModelDetailService] "
                + "Resolved model asset has no valid id.";


            Debug.LogError(
                message
            );


            OnModelPartsLoadFailed
                ?.Invoke(
                    message
                );


            yield break;
        }


        assetId =
            model.id;


        IsLoading = false;


        Debug.Log(
            "[VRModelDetailService] "
            + "Current model resolved:"
            + "\nFile = "
            + model.file_name
            + "\nAsset ID = "
            + assetId
        );


        // Load model_parts immediately.
        LoadCurrentModelParts();
    }


    // =========================================================
    // PUBLIC - LOAD MODEL PARTS
    // =========================================================

    /// <summary>
    /// Load parts using the currently resolved assetId.
    /// </summary>
    public void LoadCurrentModelParts()
    {
        if (
            string.IsNullOrWhiteSpace(
                assetId
            )
        )
        {
            Debug.LogWarning(
                "[VRModelDetailService] "
                + "assetId is empty."
            );

            return;
        }


        StartCoroutine(
            FetchModelPartsCoroutine(
                assetId
            )
        );
    }


    /// <summary>
    /// Load model parts directly from a specific asset ID.
    ///
    /// This will later be called whenever the user
    /// switches to another 3D model in VR.
    /// </summary>
    public void LoadModelParts(
        string newAssetId
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                newAssetId
            )
        )
        {
            Debug.LogError(
                "[VRModelDetailService] "
                + "LoadModelParts received invalid asset ID."
            );

            return;
        }


        assetId =
            newAssetId.Trim();


        Debug.Log(
            "[VRModelDetailService] "
            + "Switching current asset to: "
            + assetId
        );


        StartCoroutine(
            FetchModelPartsCoroutine(
                assetId
            )
        );
    }


    // =========================================================
    // FETCH MODEL PARTS
    // =========================================================

    private IEnumerator FetchModelPartsCoroutine(
        string targetAssetId
    )
    {
        if (IsLoading)
        {
            Debug.LogWarning(
                "[VRModelDetailService] "
                + "A request is already running."
            );

            yield break;
        }


        IsLoading =
            true;


        Debug.Log(
            "[VRModelDetailService] "
            + "Loading model parts for asset: "
            + targetAssetId
        );


        string requestUrl =
            NormalizeSupabaseUrl(
                supabaseUrl
            )
            + "/rest/v1/model_parts"
            + "?asset_id=eq."
            + UnityWebRequest.EscapeURL(
                targetAssetId
            )
            + "&is_active=eq.true"
            + "&order=display_order.asc";


        using UnityWebRequest request =
            UnityWebRequest.Get(
                requestUrl
            );


        AddSupabaseHeaders(
            request
        );


        yield return
            request.SendWebRequest();


        IsLoading =
            false;


        if (
            request.result !=
            UnityWebRequest.Result.Success
        )
        {
            string message =
                "[VRModelDetailService] "
                + "Could not load model parts."
                + "\nHTTP: "
                + request.responseCode
                + "\nError: "
                + request.error
                + "\nResponse: "
                + request.downloadHandler.text;


            Debug.LogError(
                message
            );


            OnModelPartsLoadFailed
                ?.Invoke(
                    message
                );


            yield break;
        }


        string json =
            request.downloadHandler.text;


        Debug.Log(
            "[VRModelDetailService] "
            + "Raw response: "
            + json
        );


        ModelPartData[] parsedParts =
            ParseJsonArray<ModelPartData>(
                json
            );


        CurrentParts.Clear();


        if (parsedParts != null)
        {
            CurrentParts.AddRange(
                parsedParts
            );
        }


        Debug.Log(
            "[VRModelDetailService] "
            + "Loaded "
            + CurrentParts.Count
            + " model parts."
        );


        if (printPartsToConsole)
        {
            PrintParts();
        }


        OnModelPartsLoaded
            ?.Invoke(
                CurrentParts
            );
    }


    // =========================================================
    // GET PART
    // =========================================================

    public ModelPartData GetPartById(
        string partId
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                partId
            )
        )
        {
            return null;
        }


        return CurrentParts.Find(
            part =>
                part != null &&
                part.id == partId
        );
    }


    public ModelPartData GetPartByKey(
        string partKey
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                partKey
            )
        )
        {
            return null;
        }


        return CurrentParts.Find(
            part =>
                part != null &&
                part.part_key == partKey
        );
    }


    // =========================================================
    // ANCHOR HELPERS
    // =========================================================

    public bool HasAnchor(
        ModelPartData part
    )
    {
        if (part == null)
        {
            return false;
        }


        return
            part.anchor_x.HasValue &&
            part.anchor_y.HasValue &&
            part.anchor_z.HasValue;
    }


    public Vector3 GetAnchorPosition(
        ModelPartData part
    )
    {
        if (
            part == null ||
            !HasAnchor(
                part
            )
        )
        {
            return Vector3.zero;
        }


        return new Vector3(
            part.anchor_x.Value,
            part.anchor_y.Value,
            part.anchor_z.Value
        );
    }


    public Vector3 GetLabelOffset(
        ModelPartData part
    )
    {
        if (part == null)
        {
            return Vector3.zero;
        }


        float x =
            part.label_offset_x
                ?? 0f;


        float y =
            part.label_offset_y
                ?? 0.15f;


        float z =
            part.label_offset_z
                ?? 0f;


        return new Vector3(
            x,
            y,
            z
        );
    }


    // =========================================================
    // PRINT DEBUG
    // =========================================================

    private void PrintParts()
    {
        for (
            int i = 0;
            i < CurrentParts.Count;
            i++
        )
        {
            ModelPartData part =
                CurrentParts[i];


            if (part == null)
            {
                continue;
            }


            Debug.Log(
                "[VRModelPart] "
                + (i + 1)
                + ". "
                + part.part_name
                + " | key = "
                + part.part_key
                + " | verified = "
                + part.is_verified
                + " | anchor = "
                + GetAnchorDebugText(
                    part
                )
            );
        }
    }


    private string GetAnchorDebugText(
        ModelPartData part
    )
    {
        if (
            part == null ||
            !HasAnchor(
                part
            )
        )
        {
            return "NULL";
        }


        return
            "("
            + part.anchor_x.Value.ToString(
                "F3"
            )
            + ", "
            + part.anchor_y.Value.ToString(
                "F3"
            )
            + ", "
            + part.anchor_z.Value.ToString(
                "F3"
            )
            + ")";
    }


    // =========================================================
    // SUPABASE HEADERS
    // =========================================================

    private void AddSupabaseHeaders(
        UnityWebRequest request
    )
    {
        if (request == null)
        {
            return;
        }


        request.SetRequestHeader(
            "apikey",
            supabaseAnonKey
        );


        request.SetRequestHeader(
            "Authorization",
            "Bearer "
            + supabaseAnonKey
        );


        request.SetRequestHeader(
            "Accept",
            "application/json"
        );
    }


    // =========================================================
    // JSON ARRAY PARSER
    // =========================================================

    private T[] ParseJsonArray<T>(
        string json
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                json
            )
        )
        {
            return Array.Empty<T>();
        }


        try
        {
            string wrappedJson =
                "{\"items\":"
                + json
                + "}";


            JsonArrayWrapper<T> wrapper =
                JsonUtility.FromJson<
                    JsonArrayWrapper<T>
                >(
                    wrappedJson
                );


            if (
                wrapper == null ||
                wrapper.items == null
            )
            {
                return Array.Empty<T>();
            }


            return wrapper.items;
        }
        catch (
            Exception exception
        )
        {
            Debug.LogError(
                "[VRModelDetailService] "
                + "JSON parsing failed."
                + "\n"
                + exception.Message
                + "\nJSON:"
                + json
            );


            return Array.Empty<T>();
        }
    }


    // =========================================================
    // URL HELPER
    // =========================================================

    private string NormalizeSupabaseUrl(
        string url
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                url
            )
        )
        {
            return "";
        }


        return url.Trim()
            .TrimEnd('/');
    }


    // =========================================================
    // MANUAL DEBUG BUTTONS
    // =========================================================

    [ContextMenu(
        "Resolve Current Model Asset"
    )]
    private void DebugResolveModel()
    {
        ResolveCurrentModelAsset();
    }


    [ContextMenu(
        "Reload Current Model Parts"
    )]
    private void DebugReloadParts()
    {
        LoadCurrentModelParts();
    }


    [ContextMenu(
        "Print Current Model Parts"
    )]
    private void DebugPrintParts()
    {
        PrintParts();
    }
}