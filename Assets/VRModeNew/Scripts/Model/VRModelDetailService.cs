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

        // The query only returns rows whose anchor coordinates are non-null,
        // so plain floats are safer with Unity JsonUtility than Nullable<float>.
        public float anchor_x;

        public float anchor_y;

        public float anchor_z;

        public string anchor_source;

        public float anchor_confidence;

        public string anchor_view;

        public string anchor_metadata;


        // -----------------------------------------------------
        // Label offset
        // -----------------------------------------------------

        public float label_offset_x = 0f;

        public float label_offset_y = 0.15f;

        public float label_offset_z = 0f;


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


    [Serializable]
    private class RefreshTokenPayload
    {
        public string refresh_token;
    }


    [Serializable]
    private class RefreshTokenResponse
    {
        public string access_token;

        public string refresh_token;

        public int expires_in;

        public string token_type;
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


        bool requestSucceeded = false;

        long responseCode = 0;

        string responseText = "";

        string requestError = "";


        yield return SendGetWithSessionRefresh(
            requestUrl,
            (success, code, body, error) =>
            {
                requestSucceeded = success;
                responseCode = code;
                responseText = body;
                requestError = error;
            }
        );


        if (!requestSucceeded)
        {
            IsLoading = false;


            string message =
                "[VRModelDetailService] "
                + "Could not resolve model asset."
                + "\nHTTP: "
                + responseCode
                + "\nError: "
                + requestError
                + "\nResponse: "
                + responseText;


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
            responseText;


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
            + "&anchor_x=not.is.null"
            + "&anchor_y=not.is.null"
            + "&anchor_z=not.is.null"
            + "&select="
            + "id,"
            + "asset_id,"
            + "part_key,"
            + "part_name,"
            + "node_name,"
            + "description,"
            + "structure_description,"
            + "function_description,"
            + "anchor_x,"
            + "anchor_y,"
            + "anchor_z,"
            + "label_offset_x,"
            + "label_offset_y,"
            + "label_offset_z,"
            + "display_order,"
            + "source,"
            + "is_verified,"
            + "is_active,"
            + "ai_confidence,"
            + "anchor_source,"
            + "anchor_confidence,"
            + "anchor_view,"
            + "anchor_metadata"
            + "&order=display_order.asc";


        bool requestSucceeded = false;

        long responseCode = 0;

        string responseText = "";

        string requestError = "";


        yield return SendGetWithSessionRefresh(
            requestUrl,
            (success, code, body, error) =>
            {
                requestSucceeded = success;
                responseCode = code;
                responseText = body;
                requestError = error;
            }
        );


        IsLoading =
            false;


        if (!requestSucceeded)
        {
            string message =
                "[VRModelDetailService] "
                + "Could not load model parts."
                + "\nHTTP: "
                + responseCode
                + "\nError: "
                + requestError
                + "\nResponse: "
                + responseText;


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
            responseText;


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
            part.is_active &&
            IsFinite(part.anchor_x) &&
            IsFinite(part.anchor_y) &&
            IsFinite(part.anchor_z);
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
            part.anchor_x,
            part.anchor_y,
            part.anchor_z
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


        return new Vector3(
            part.label_offset_x,
            part.label_offset_y,
            part.label_offset_z
        );
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
            + part.anchor_x.ToString(
                "F3"
            )
            + ", "
            + part.anchor_y.ToString(
                "F3"
            )
            + ", "
            + part.anchor_z.ToString(
                "F3"
            )
            + ")";
    }


    // =========================================================
    // SUPABASE SESSION / HEADERS
    // =========================================================

    private IEnumerator SendGetWithSessionRefresh(
        string requestUrl,
        Action<bool, long, string, string> onCompleted
    )
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using UnityWebRequest request =
                UnityWebRequest.Get(
                    requestUrl
                );


            AddSupabaseHeaders(
                request
            );


            yield return
                request.SendWebRequest();


            string responseText =
                request.downloadHandler != null
                    ? request.downloadHandler.text
                    : "";


            if (
                request.result ==
                UnityWebRequest.Result.Success
            )
            {
                onCompleted?.Invoke(
                    true,
                    request.responseCode,
                    responseText,
                    ""
                );

                yield break;
            }


            bool canRefresh =
                attempt == 0 &&
                IsJwtExpiredResponse(
                    request.responseCode,
                    responseText
                ) &&
                !string.IsNullOrWhiteSpace(
                    SupabaseSession.RefreshToken
                );


            if (!canRefresh)
            {
                onCompleted?.Invoke(
                    false,
                    request.responseCode,
                    responseText,
                    request.error
                );

                yield break;
            }


            Debug.LogWarning(
                "[VRModelDetailService] "
                + "Supabase access token expired. Refreshing session..."
            );


            bool refreshSucceeded = false;

            string refreshError = "";


            yield return RefreshSupabaseSessionCoroutine(
                success =>
                    refreshSucceeded = success,
                error =>
                    refreshError = error
            );


            if (!refreshSucceeded)
            {
                onCompleted?.Invoke(
                    false,
                    request.responseCode,
                    responseText,
                    "Session refresh failed: "
                    + refreshError
                );

                yield break;
            }


            Debug.Log(
                "[VRModelDetailService] "
                + "Supabase session refreshed. Retrying request..."
            );
        }
    }


    private IEnumerator RefreshSupabaseSessionCoroutine(
        Action<bool> onCompleted,
        Action<string> onError
    )
    {
        string refreshToken =
            SupabaseSession.RefreshToken;


        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            onError?.Invoke(
                "refresh_token is missing."
            );

            onCompleted?.Invoke(false);
            yield break;
        }


        string requestUrl =
            NormalizeSupabaseUrl(
                supabaseUrl
            )
            + "/auth/v1/token?grant_type=refresh_token";


        RefreshTokenPayload payload =
            new RefreshTokenPayload
            {
                refresh_token = refreshToken
            };


        string json =
            JsonUtility.ToJson(
                payload
            );


        using UnityWebRequest request =
            new UnityWebRequest(
                requestUrl,
                UnityWebRequest.kHttpVerbPOST
            );


        request.uploadHandler =
            new UploadHandlerRaw(
                System.Text.Encoding.UTF8.GetBytes(
                    json
                )
            );


        request.downloadHandler =
            new DownloadHandlerBuffer();


        request.SetRequestHeader(
            "apikey",
            supabaseAnonKey
        );


        request.SetRequestHeader(
            "Content-Type",
            "application/json"
        );


        request.SetRequestHeader(
            "Accept",
            "application/json"
        );


        yield return
            request.SendWebRequest();


        string responseText =
            request.downloadHandler != null
                ? request.downloadHandler.text
                : "";


        if (
            request.result !=
            UnityWebRequest.Result.Success
        )
        {
            onError?.Invoke(
                "HTTP "
                + request.responseCode
                + ": "
                + responseText
            );

            onCompleted?.Invoke(false);
            yield break;
        }


        RefreshTokenResponse response =
            JsonUtility.FromJson<RefreshTokenResponse>(
                responseText
            );


        if (
            response == null ||
            string.IsNullOrWhiteSpace(
                response.access_token
            )
        )
        {
            onError?.Invoke(
                "Supabase refresh response did not contain access_token."
            );

            onCompleted?.Invoke(false);
            yield break;
        }


        PlayerPrefs.SetString(
            "access_token",
            response.access_token
        );


        if (
            !string.IsNullOrWhiteSpace(
                response.refresh_token
            )
        )
        {
            PlayerPrefs.SetString(
                "refresh_token",
                response.refresh_token
            );
        }


        PlayerPrefs.Save();

        onCompleted?.Invoke(true);
    }


    private static bool IsJwtExpiredResponse(
        long responseCode,
        string responseText
    )
    {
        if (responseCode != 401)
        {
            return false;
        }


        string normalized =
            responseText == null
                ? ""
                : responseText.ToLowerInvariant();


        return
            normalized.Contains(
                "jwt expired"
            ) ||
            normalized.Contains(
                "pgrst303"
            );
    }


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


        // Only send Authorization when a real user access token exists.
        // Do not put the sb_publishable key in the Bearer header.
        if (
            !string.IsNullOrWhiteSpace(
                SupabaseSession.AccessToken
            )
        )
        {
            request.SetRequestHeader(
                "Authorization",
                "Bearer "
                + SupabaseSession.AccessToken
            );
        }


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