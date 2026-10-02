using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

/// <summary>
/// Shows the AI-generated structure of a 3D model (table model_parts) on top of
/// a UI Toolkit screen: one label per part, a connector line to the part on the
/// model and a detail sheet (description / structure / function) when a label
/// is tapped.
///
/// Used by Mode3DScene and ARScene. VRClassroomScene keeps its own
/// implementation (VRModelDetailService + VRModelDetailAnchorController).
///
/// Usage:
///     overlay = ModelStructureOverlay.Attach(this, root, () => modelRoot, () => camera);
///     overlay.SetModel(assetId, lessonId, fileName);
///     overlay.Toggle();               // from the "structure" button
/// </summary>
public class ModelStructureOverlay : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Static helpers used by gesture scripts
    // ------------------------------------------------------------------

    private static readonly List<ModelStructureOverlay> ActiveOverlays = new();

    /// <summary>True while a detail sheet is open (world gestures should pause).</summary>
    public static bool IsAnyDetailOpen
    {
        get
        {
            for (int i = 0; i < ActiveOverlays.Count; i++)
            {
                if (ActiveOverlays[i] != null && ActiveOverlays[i].IsDetailOpen)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// True when the screen position (pixels, origin bottom-left as in Input)
    /// is over a structure label or the detail sheet.
    /// </summary>
    public static bool IsScreenPointOverOverlay(Vector2 screenPosition)
    {
        for (int i = 0; i < ActiveOverlays.Count; i++)
        {
            if (ActiveOverlays[i] != null && ActiveOverlays[i].ContainsScreenPoint(screenPosition))
                return true;
        }
        return false;
    }

    public static bool ShouldBlockWorldTouch()
    {
        if (IsAnyDetailOpen)
            return true;

        for (int i = 0; i < Input.touchCount; i++)
        {
            if (IsScreenPointOverOverlay(Input.GetTouch(i).position))
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Data
    // ------------------------------------------------------------------

    [Serializable]
    public class PartData
    {
        public string id;
        public string asset_id;
        public string part_key;
        public string part_name;
        public string part_name_vi;
        public string node_name;
        public string description;
        public string description_vi;
        public string structure_description;
        public string structure_description_vi;
        public string function_description;
        public string function_description_vi;
        public int display_order;
        public bool is_active = true;
    }

    [Serializable] private class PartList { public PartData[] items; }

    [Serializable] private class AssetRow { public string id; public string file_name; }
    [Serializable] private class AssetList { public AssetRow[] items; }

    private sealed class PartView
    {
        public PartData Data;
        public Transform Node;
        public Vector3 NodeLocalCenter;
        public Button Label;
        public Vector2 AnchorPanel;
        public Vector2 LabelCenter;
        public bool Visible;
    }

    private sealed class LayoutEntry
    {
        public PartView View;
        public float Y;
    }

    // ------------------------------------------------------------------
    // Settings (can be changed by the host before first use)
    // ------------------------------------------------------------------

    /// <summary>Space reserved at the top of the screen (header), in panel pixels.</summary>
    public float TopInset = 120f;

    /// <summary>Space reserved at the bottom of the screen (toolbar), in panel pixels.</summary>
    public float BottomInset = 200f;

    public float SideMargin = 10f;
    public int MaxLabels = 16;

    private static readonly Color LineColor = new Color(0.08f, 0.42f, 0.95f, 1f);
    private static readonly Color SelectedColor = new Color(1f, 0.62f, 0.12f, 1f);
    private static readonly Color DotColor = new Color(1f, 0.85f, 0.2f, 1f);

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------

    private Func<Transform> modelRootProvider;
    private Func<Camera> cameraProvider;

    private VisualElement hostRoot;
    private VisualElement overlayRoot;
    private VisualElement connectorLayer;
    private VisualElement labelsLayer;
    private Label statusLabel;

    private VisualElement detailScrim;
    private VisualElement detailSheet;

    // Everything is designed for a ~420 px wide panel and scaled up for
    // high-resolution panels (Mode3DScene uses a large reference resolution).
    private float uiScale = 1f;
    private readonly List<Action<float>> scaleActions = new();
    private Label detailTitle;
    private Label detailSubtitle;
    private Label detailDescription;
    private Label detailStructure;
    private Label detailFunction;
    private VisualElement detailStructureSection;
    private VisualElement detailFunctionSection;
    private VisualElement detailDescriptionSection;

    private readonly List<PartView> views = new();
    private readonly List<PartData> loadedParts = new();

    private string assetId;
    private string lessonId;
    private string fileName;
    private string loadedForAssetId;
    private Transform boundModelRoot;

    private Coroutine loadRoutine;
    private bool isLoading;
    private PartView selected;

    public bool IsEnabled { get; private set; }
    public bool IsDetailOpen => detailScrim != null && detailScrim.style.display.value == DisplayStyle.Flex;
    public int PartCount => loadedParts.Count;

    public event Action<bool> EnabledChanged;

    // ------------------------------------------------------------------
    // Setup
    // ------------------------------------------------------------------

    public static ModelStructureOverlay Attach(
        MonoBehaviour host,
        VisualElement root,
        Func<Transform> modelRoot,
        Func<Camera> camera)
    {
        if (host == null || root == null)
            return null;

        ModelStructureOverlay overlay = host.GetComponent<ModelStructureOverlay>();
        if (overlay == null)
            overlay = host.gameObject.AddComponent<ModelStructureOverlay>();

        overlay.modelRootProvider = modelRoot;
        overlay.cameraProvider = camera;
        overlay.BuildUi(root);
        return overlay;
    }

    private void OnEnable()
    {
        if (!ActiveOverlays.Contains(this))
            ActiveOverlays.Add(this);
    }

    private void OnDisable()
    {
        ActiveOverlays.Remove(this);
    }

    private void OnDestroy()
    {
        ActiveOverlays.Remove(this);
        overlayRoot?.RemoveFromHierarchy();
    }

    /// <summary>
    /// Sets the model whose structure is shown. assetId (lesson_assets.id) is
    /// preferred; otherwise the model_3d asset is resolved from lessonId + fileName.
    /// </summary>
    public void SetModel(string newAssetId, string newLessonId = null, string newFileName = null)
    {
        string normalizedAsset = (newAssetId ?? string.Empty).Trim();
        bool changed =
            !string.Equals(normalizedAsset, assetId ?? string.Empty, StringComparison.Ordinal) ||
            !string.Equals(newLessonId ?? string.Empty, lessonId ?? string.Empty, StringComparison.Ordinal) ||
            !string.Equals(newFileName ?? string.Empty, fileName ?? string.Empty, StringComparison.Ordinal);

        assetId = normalizedAsset;
        lessonId = (newLessonId ?? string.Empty).Trim();
        fileName = (newFileName ?? string.Empty).Trim();

        if (!changed)
            return;

        loadedParts.Clear();
        loadedForAssetId = null;
        ClearViews();
        CloseDetail();

        if (IsEnabled)
            StartLoad();
    }

    /// <summary>Call when the model GameObject was replaced (new GLB, AR model switch).</summary>
    public void NotifyModelChanged()
    {
        boundModelRoot = null;
        ClearViews();
        CloseDetail();

        if (IsEnabled && loadedParts.Count > 0)
            StartCoroutine(BindWhenModelReady());
    }

    public void Toggle() => SetEnabled(!IsEnabled);

    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
            return;

        IsEnabled = enabled;

        if (overlayRoot != null)
            overlayRoot.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;

        if (!enabled)
        {
            CloseDetail();
            ClearViews();
            SetStatus(null);
        }
        else if (loadedParts.Count > 0 &&
                 string.Equals(loadedForAssetId, assetId, StringComparison.Ordinal))
        {
            StartCoroutine(BindWhenModelReady());
        }
        else
        {
            StartLoad();
        }

        EnabledChanged?.Invoke(enabled);
    }

    // ------------------------------------------------------------------
    // UI construction (inline styles, so no extra USS file is needed)
    // ------------------------------------------------------------------

    private void BuildUi(VisualElement root)
    {
        if (overlayRoot != null && hostRoot == root)
            return;

        overlayRoot?.RemoveFromHierarchy();
        hostRoot = root;

        overlayRoot = new VisualElement { name = "model-structure-overlay", pickingMode = PickingMode.Ignore };
        Stretch(overlayRoot);
        overlayRoot.style.display = DisplayStyle.None;

        connectorLayer = new VisualElement { name = "structure-connectors", pickingMode = PickingMode.Ignore };
        Stretch(connectorLayer);
        connectorLayer.generateVisualContent += DrawConnectors;

        labelsLayer = new VisualElement { name = "structure-labels", pickingMode = PickingMode.Ignore };
        Stretch(labelsLayer);

        statusLabel = new Label { name = "structure-status", pickingMode = PickingMode.Ignore };
        statusLabel.style.position = Position.Absolute;
        statusLabel.style.left = new Length(50, LengthUnit.Percent);
        statusLabel.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
        statusLabel.style.maxWidth = new Length(86, LengthUnit.Percent);
        scaleActions.Add(k =>
        {
            statusLabel.style.paddingLeft = 14 * k;
            statusLabel.style.paddingRight = 14 * k;
            statusLabel.style.paddingTop = 7 * k;
            statusLabel.style.paddingBottom = 7 * k;
            SetRadius(statusLabel, 14 * k);
        });
        statusLabel.style.backgroundColor = new Color(0.05f, 0.1f, 0.22f, 0.85f);
        statusLabel.style.color = Color.white;
        RegisterScaled(statusLabel, 13f);
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        statusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        SetRadius(statusLabel, 14);
        statusLabel.style.display = DisplayStyle.None;

        BuildDetailSheet();

        overlayRoot.Add(connectorLayer);
        overlayRoot.Add(labelsLayer);
        overlayRoot.Add(statusLabel);
        overlayRoot.Add(detailScrim);

        root.Add(overlayRoot);
    }

    private void BuildDetailSheet()
    {
        // Dimmed full-screen layer with the card in the middle (same as VR).
        // Tapping outside the card closes it.
        detailScrim = new VisualElement { name = "structure-detail-scrim", pickingMode = PickingMode.Position };
        Stretch(detailScrim);
        detailScrim.style.alignItems = Align.Center;
        detailScrim.style.justifyContent = Justify.Center;
        detailScrim.style.backgroundColor = new Color(0.01f, 0.04f, 0.12f, 0.45f);
        detailScrim.style.display = DisplayStyle.None;
        detailScrim.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
        detailScrim.RegisterCallback<PointerUpEvent>(e =>
        {
            if (e.target == detailScrim)
                CloseDetail();
            e.StopPropagation();
        });

        detailSheet = new VisualElement { name = "structure-detail-sheet", pickingMode = PickingMode.Position };
        detailSheet.style.width = new Length(86, LengthUnit.Percent);
        detailSheet.style.maxHeight = new Length(62, LengthUnit.Percent);
        detailSheet.style.backgroundColor = new Color(1f, 1f, 1f, 0.98f);
        SetBorder(detailSheet, new Color(0.85f, 0.89f, 0.95f, 1f), 1);
        scaleActions.Add(k =>
        {
            detailSheet.style.paddingLeft = 18 * k;
            detailSheet.style.paddingRight = 12 * k;
            detailSheet.style.paddingTop = 14 * k;
            detailSheet.style.paddingBottom = 16 * k;
            SetRadius(detailSheet, 20 * k);
        });

        // Stop taps on the card from closing it or reaching the model.
        detailSheet.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
        detailSheet.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
        detailScrim.Add(detailSheet);

        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.FlexStart;

        VisualElement titles = new VisualElement();
        titles.style.flexGrow = 1;
        titles.style.flexShrink = 1;
        titles.style.minWidth = 0;

        detailTitle = new Label();
        RegisterScaled(detailTitle, 18f);
        detailTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        detailTitle.style.color = new Color(0.07f, 0.15f, 0.32f, 1f);
        detailTitle.style.whiteSpace = WhiteSpace.Normal;

        detailSubtitle = new Label();
        RegisterScaled(detailSubtitle, 12f);
        detailSubtitle.style.color = new Color(0.42f, 0.5f, 0.62f, 1f);
        detailSubtitle.style.whiteSpace = WhiteSpace.Normal;
        detailSubtitle.style.marginTop = 2;

        titles.Add(detailTitle);
        titles.Add(detailSubtitle);

        Button close = new Button(CloseDetail) { text = "×" };
        close.style.flexShrink = 0;
        scaleActions.Add(k =>
        {
            close.style.width = 34 * k;
            close.style.height = 34 * k;
            close.style.minWidth = 34 * k;
            close.style.marginLeft = 8 * k;
            close.style.fontSize = 22 * k;
            SetRadius(close, 17 * k);
        });
        close.style.color = new Color(0.25f, 0.32f, 0.45f, 1f);
        close.style.backgroundColor = new Color(0.93f, 0.95f, 0.98f, 1f);
        SetBorder(close, Color.clear, 0);
        close.style.paddingLeft = 0;
        close.style.paddingRight = 0;
        close.style.paddingTop = 0;
        close.style.paddingBottom = 2;

        header.Add(titles);
        header.Add(close);

        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.marginTop = 8;
        scroll.style.flexShrink = 1;
        scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

        detailDescriptionSection = BuildSection(out detailDescription, null);
        detailStructureSection = BuildSection(out detailStructure, "structure");
        detailFunctionSection = BuildSection(out detailFunction, "function");

        scroll.Add(detailDescriptionSection);
        scroll.Add(detailStructureSection);
        scroll.Add(detailFunctionSection);

        detailSheet.Add(header);
        detailSheet.Add(scroll);
    }

    private VisualElement BuildSection(out Label body, string kind)
    {
        VisualElement section = new VisualElement();
        section.style.marginTop = 8;

        if (kind != null)
        {
            Label title = new Label { name = "section-title-" + kind };
            RegisterScaled(title, 12f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.08f, 0.42f, 0.95f, 1f);
            title.style.marginBottom = 2;
            section.Add(title);
        }

        body = new Label();
        RegisterScaled(body, 14f);
        body.style.color = new Color(0.12f, 0.17f, 0.26f, 1f);
        body.style.whiteSpace = WhiteSpace.Normal;
        section.Add(body);
        return section;
    }

    // ------------------------------------------------------------------
    // Loading model_parts from Supabase
    // ------------------------------------------------------------------

    private void StartLoad()
    {
        if (loadRoutine != null)
            StopCoroutine(loadRoutine);

        loadRoutine = StartCoroutine(LoadPartsRoutine());
    }

    private IEnumerator LoadPartsRoutine()
    {
        isLoading = true;
        SetStatus(T("Loading model structure...", "Đang tải cấu trúc mô hình..."));

        if (!SupabaseSession.IsLoggedIn && !SupabaseSession.HasStoredSession)
        {
            isLoading = false;
            SetStatus(T("Please sign in to view the model structure.", "Vui lòng đăng nhập để xem cấu trúc mô hình."));
            yield break;
        }

        yield return SupabaseTokenRefresher.EnsureFreshToken();

        string targetAssetId = assetId;

        if (!Guid.TryParse(targetAssetId, out _))
        {
            string resolved = null;
            yield return ResolveAssetIdRoutine(value => resolved = value);
            targetAssetId = resolved;
        }

        if (!Guid.TryParse(targetAssetId, out _))
        {
            isLoading = false;
            loadRoutine = null;
            SetStatus(T("This model has no structure data yet.", "Mô hình này chưa có dữ liệu cấu trúc."));
            yield break;
        }

        string url = SupabaseConfig.RestUrl.TrimEnd('/') +
                     "/model_parts?asset_id=eq." + UnityWebRequest.EscapeURL(targetAssetId) +
                     "&is_active=eq.true" +
                     "&select=id,asset_id,part_key,part_name,part_name_vi,node_name,description,description_vi," +
                     "structure_description,structure_description_vi,function_description,function_description_vi,display_order,is_active" +
                     "&order=display_order.asc";

        string json = null;
        string error = null;
        yield return GetJson(url, v => json = v, e => error = e);

        isLoading = false;
        loadRoutine = null;

        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogWarning("[ModelStructure] Cannot load model_parts: " + error);
            SetStatus(T("Could not load the model structure.", "Không tải được cấu trúc mô hình."));
            yield break;
        }

        loadedParts.Clear();

        try
        {
            PartList list = JsonUtility.FromJson<PartList>("{\"items\":" + (string.IsNullOrWhiteSpace(json) ? "[]" : json) + "}");
            if (list?.items != null)
            {
                foreach (PartData part in list.items)
                {
                    if (part != null && part.is_active && !string.IsNullOrWhiteSpace(part.part_key))
                        loadedParts.Add(part);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[ModelStructure] Cannot parse model_parts: " + exception.Message);
        }

        loadedForAssetId = assetId;

        if (loadedParts.Count == 0)
        {
            SetStatus(T("AI has not analysed this model's structure yet.",
                        "AI chưa phân tích cấu trúc cho mô hình này."));
            yield break;
        }

        Debug.Log($"[ModelStructure] Loaded {loadedParts.Count} part(s) for asset {targetAssetId}.");

        if (IsEnabled)
            yield return BindWhenModelReady();
    }

    private IEnumerator ResolveAssetIdRoutine(Action<string> onResolved)
    {
        if (!Guid.TryParse(lessonId, out _))
        {
            onResolved?.Invoke(null);
            yield break;
        }

        string url = SupabaseConfig.RestUrl.TrimEnd('/') +
                     "/lesson_assets?lesson_id=eq." + UnityWebRequest.EscapeURL(lessonId) +
                     "&asset_type=eq.model_3d&select=id,file_name&order=display_order.asc";

        string json = null;
        string error = null;
        yield return GetJson(url, v => json = v, e => error = e);

        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(json))
        {
            onResolved?.Invoke(null);
            yield break;
        }

        AssetList list = null;
        try { list = JsonUtility.FromJson<AssetList>("{\"items\":" + json + "}"); }
        catch { /* ignored */ }

        if (list?.items == null || list.items.Length == 0)
        {
            onResolved?.Invoke(null);
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            foreach (AssetRow row in list.items)
            {
                if (row != null && string.Equals(row.file_name, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    onResolved?.Invoke(row.id);
                    yield break;
                }
            }
        }

        onResolved?.Invoke(list.items[0].id);
    }

    private static IEnumerator GetJson(string url, Action<string> onSuccess, Action<string> onError)
    {
        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = 30;
        request.SetRequestHeader("apikey", SupabaseConfig.PublishableKey);
        request.SetRequestHeader("Authorization", "Bearer " + SupabaseSession.AccessToken);
        request.SetRequestHeader("Accept", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
            onSuccess?.Invoke(request.downloadHandler?.text);
        else
            onError?.Invoke($"HTTP {request.responseCode}: {request.error} {request.downloadHandler?.text}");
    }

    // ------------------------------------------------------------------
    // Binding parts to the loaded model
    // ------------------------------------------------------------------

    private IEnumerator BindWhenModelReady()
    {
        const float timeout = 20f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (!IsEnabled)
                yield break;

            Transform root = modelRootProvider?.Invoke();
            if (root != null && root.gameObject.activeInHierarchy &&
                root.GetComponentsInChildren<Renderer>(false).Length > 0)
            {
                BuildViews(root);
                yield break;
            }

            if (elapsed < 0.05f)
                SetStatus(T("Waiting for the model...", "Đang chờ mô hình hiển thị..."));

            elapsed += 0.25f;
            yield return new WaitForSecondsRealtime(0.25f);
        }

        SetStatus(T("Place or load the model to see its structure.",
                    "Hãy đặt/tải mô hình để xem cấu trúc."));
    }

    private void BuildViews(Transform root)
    {
        ClearViews();
        boundModelRoot = root;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        int count = 0;

        foreach (PartData part in loadedParts)
        {
            if (count >= MaxLabels)
                break;

            Transform node = FindNode(transforms, part.node_name);
            if (node == null)
                continue;

            PartView view = new PartView
            {
                Data = part,
                Node = node,
                NodeLocalCenter = node.InverseTransformPoint(ComputeCenter(node))
            };

            Button label = new Button(() => OpenDetail(view)) { text = GetPartName(part) };
            StyleLabel(label, false);
            label.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            view.Label = label;
            labelsLayer.Add(label);

            views.Add(view);
            count++;
        }

        if (views.Count == 0)
        {
            SetStatus(T("The structure data does not match this model's parts.",
                        "Dữ liệu cấu trúc không khớp với các bộ phận của mô hình."));
            return;
        }

        SetStatus(null);
        Debug.Log($"[ModelStructure] Showing {views.Count} structure label(s).");
    }

    private static Transform FindNode(Transform[] transforms, string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
            return null;

        string target = nodeName.Trim();

        foreach (Transform t in transforms)
            if (t != null && string.Equals(t.name, target, StringComparison.Ordinal))
                return t;

        foreach (Transform t in transforms)
            if (t != null && string.Equals(t.name, target, StringComparison.OrdinalIgnoreCase))
                return t;

        return null;
    }

    private static Vector3 ComputeCenter(Transform node)
    {
        Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
        bool has = false;
        Bounds bounds = default;

        foreach (Renderer r in renderers)
        {
            if (r == null || r is LineRenderer || r is TrailRenderer)
                continue;

            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }

        return has ? bounds.center : node.position;
    }

    private void ClearViews()
    {
        views.Clear();
        selected = null;
        labelsLayer?.Clear();
        connectorLayer?.MarkDirtyRepaint();
    }

    // ------------------------------------------------------------------
    // Per-frame layout
    // ------------------------------------------------------------------

    private void LateUpdate()
    {
        if (!IsEnabled || overlayRoot == null || views.Count == 0)
            return;

        Transform currentRoot = modelRootProvider?.Invoke();
        if (currentRoot != boundModelRoot)
        {
            // The host replaced the model (AR model switch) without notifying.
            NotifyModelChanged();
            return;
        }

        Camera cam = cameraProvider?.Invoke();
        if (cam == null) cam = Camera.main;
        IPanel panel = overlayRoot.panel;
        if (cam == null || panel == null)
            return;

        float width = overlayRoot.resolvedStyle.width;
        float height = overlayRoot.resolvedStyle.height;
        if (width < 2f || height < 2f)
            return;

        UpdateScaleFromLayout();

        bool modelVisible = boundModelRoot != null && boundModelRoot.gameObject.activeInHierarchy;

        List<LayoutEntry> entries = new List<LayoutEntry>(views.Count);

        foreach (PartView view in views)
        {
            view.Visible = false;

            if (!modelVisible || view.Node == null || !view.Node.gameObject.activeInHierarchy)
            {
                view.Label.style.display = DisplayStyle.None;
                continue;
            }

            Vector3 world = view.Node.TransformPoint(view.NodeLocalCenter);
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0.01f)
            {
                view.Label.style.display = DisplayStyle.None;
                continue;
            }

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(
                panel, new Vector2(screen.x, Screen.height - screen.y));

            view.AnchorPanel = panelPos;
            view.Visible = true;
            view.Label.style.display = DisplayStyle.Flex;
            entries.Add(new LayoutEntry { View = view, Y = panelPos.y });
        }

        // Alternate parts top-to-bottom between the left and right columns.
        entries.Sort((a, b) => a.Y.CompareTo(b.Y));
        List<LayoutEntry> left = new List<LayoutEntry>();
        List<LayoutEntry> right = new List<LayoutEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            if ((i & 1) == 0) left.Add(entries[i]);
            else right.Add(entries[i]);
        }

        LayoutColumn(left, false, width, height);
        LayoutColumn(right, true, width, height);

        connectorLayer.MarkDirtyRepaint();
    }

    private void LayoutColumn(List<LayoutEntry> column, bool rightSide, float width, float height)
    {
        if (column.Count == 0)
            return;

        float maxLabelWidth = Mathf.Max(90f, width * 0.32f);
        float top = TopInset;
        float bottom = Mathf.Max(top + 60f, height - BottomInset);
        float gap = 6f * uiScale;

        float[] heights = new float[column.Count];
        float total = 0f;
        for (int i = 0; i < column.Count; i++)
        {
            Button label = column[i].View.Label;
            label.style.maxWidth = maxLabelWidth;
            float h = label.resolvedStyle.height;
            heights[i] = float.IsNaN(h) || h < 10f ? 30f * uiScale : h;
            total += heights[i] + gap;
        }

        // Start each label at its anchor height, pushed down to avoid overlaps.
        float[] ys = new float[column.Count];
        float cursor = top;
        for (int i = 0; i < column.Count; i++)
        {
            float desired = column[i].Y - heights[i] * 0.5f;
            ys[i] = Mathf.Max(desired, cursor);
            cursor = ys[i] + heights[i] + gap;
        }

        // If the column runs past the bottom, shift the whole column upward.
        float overflow = cursor - gap - bottom;
        if (overflow > 0f)
        {
            for (int i = column.Count - 1; i >= 0; i--)
            {
                float limit = i == column.Count - 1 ? bottom - heights[i] : ys[i + 1] - gap - heights[i];
                ys[i] = Mathf.Min(ys[i], limit);
            }

            if (ys[0] < top)
            {
                // Too many labels: compress evenly between top and bottom.
                float step = (bottom - top) / column.Count;
                for (int i = 0; i < column.Count; i++)
                    ys[i] = top + step * i;
            }
        }

        for (int i = 0; i < column.Count; i++)
        {
            PartView view = column[i].View;
            Button label = view.Label;
            float w = label.resolvedStyle.width;
            if (float.IsNaN(w) || w < 10f) w = maxLabelWidth * 0.7f;

            // Keep a clear gap between the labels and the screen edges.
            float margin = Mathf.Max(SideMargin, width * 0.06f);
            float x = rightSide ? width - margin - w : margin;

            label.style.left = x;
            label.style.top = ys[i];

            view.LabelCenter = new Vector2(
                rightSide ? x : x + w,
                ys[i] + heights[i] * 0.5f);
        }
    }

    private void DrawConnectors(MeshGenerationContext context)
    {
        if (!IsEnabled || views.Count == 0)
            return;

        Painter2D painter = context.painter2D;

        foreach (PartView view in views)
        {
            if (!view.Visible)
                continue;

            bool isSelected = view == selected;

            painter.lineWidth = isSelected ? 3f : 2f;
            painter.strokeColor = isSelected ? SelectedColor : LineColor;
            painter.BeginPath();
            painter.MoveTo(view.AnchorPanel);
            painter.LineTo(view.LabelCenter);
            painter.Stroke();

            painter.fillColor = isSelected ? SelectedColor : DotColor;
            painter.BeginPath();
            painter.Arc(view.AnchorPanel, isSelected ? 6f : 4.5f, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
            painter.Fill();
        }
    }

    // ------------------------------------------------------------------
    // Detail sheet
    // ------------------------------------------------------------------

    private void OpenDetail(PartView view)
    {
        if (view == null)
            return;

        if (selected != null)
            StyleLabel(selected.Label, false);

        selected = view;
        StyleLabel(view.Label, true);

        PartData part = view.Data;
        bool vi = AppLanguageManager.IsVietnamese;

        detailTitle.text = GetPartName(part);
        detailSubtitle.text = vi
            ? (string.IsNullOrWhiteSpace(part.part_name) ? string.Empty : part.part_name)
            : (string.IsNullOrWhiteSpace(part.part_name_vi) ? string.Empty : part.part_name_vi);
        detailSubtitle.style.display = string.IsNullOrWhiteSpace(detailSubtitle.text)
            ? DisplayStyle.None : DisplayStyle.Flex;

        FillSection(detailDescriptionSection, detailDescription,
            Pick(part.description_vi, part.description), null);
        FillSection(detailStructureSection, detailStructure,
            Pick(part.structure_description_vi, part.structure_description), T("Structure", "Cấu tạo"));
        FillSection(detailFunctionSection, detailFunction,
            Pick(part.function_description_vi, part.function_description), T("Function", "Chức năng"));

        UpdateScaleFromLayout();
        detailScrim.style.display = DisplayStyle.Flex;
        detailScrim.BringToFront();
        connectorLayer.MarkDirtyRepaint();
    }

    public void CloseDetail()
    {
        if (detailScrim != null)
            detailScrim.style.display = DisplayStyle.None;

        if (selected != null && selected.Label != null)
            StyleLabel(selected.Label, false);

        selected = null;
        connectorLayer?.MarkDirtyRepaint();
    }

    private static void FillSection(VisualElement section, Label body, string text, string title)
    {
        bool has = !string.IsNullOrWhiteSpace(text);
        section.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
        body.text = has ? text.Trim() : string.Empty;

        if (title != null && section.childCount > 1 && section[0] is Label header)
            header.text = title;
    }

    private static string Pick(string vietnamese, string english)
    {
        if (AppLanguageManager.IsVietnamese)
            return !string.IsNullOrWhiteSpace(vietnamese) ? vietnamese : english;
        return !string.IsNullOrWhiteSpace(english) ? english : vietnamese;
    }

    private static string GetPartName(PartData part)
    {
        string name = Pick(part.part_name_vi, part.part_name);
        return string.IsNullOrWhiteSpace(name) ? part.part_key : name.Trim();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private bool ContainsScreenPoint(Vector2 screenPosition)
    {
        if (!IsEnabled || overlayRoot?.panel == null)
            return false;

        Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(
            overlayRoot.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));

        if (IsDetailOpen && detailScrim.worldBound.Contains(panelPos))
            return true;

        foreach (PartView view in views)
        {
            if (view.Visible && view.Label != null && view.Label.worldBound.Contains(panelPos))
                return true;
        }

        return false;
    }

    private void SetStatus(string message)
    {
        if (statusLabel == null)
            return;

        if (string.IsNullOrWhiteSpace(message))
        {
            statusLabel.style.display = DisplayStyle.None;
            return;
        }

        UpdateScaleFromLayout();
        statusLabel.text = message;
        statusLabel.style.top = TopInset;
        statusLabel.style.display = DisplayStyle.Flex;
    }

    private void StyleLabel(Button label, bool isSelected)
    {
        float k = uiScale;
        label.style.position = Position.Absolute;
        label.style.paddingLeft = 10 * k;
        label.style.paddingRight = 10 * k;
        label.style.paddingTop = 5 * k;
        label.style.paddingBottom = 5 * k;
        label.style.marginLeft = 0;
        label.style.marginRight = 0;
        label.style.fontSize = 12 * k;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = isSelected ? Color.white : new Color(0.07f, 0.15f, 0.32f, 1f);
        label.style.backgroundColor = isSelected ? SelectedColor : new Color(1f, 1f, 1f, 0.94f);
        SetRadius(label, 12 * k);
        SetBorder(label, isSelected ? SelectedColor : LineColor, 1.5f * k);
    }

    private void RegisterScaled(VisualElement element, float baseFontSize)
    {
        scaleActions.Add(k => element.style.fontSize = baseFontSize * k);
        element.style.fontSize = baseFontSize * uiScale;
    }

    private void UpdateScaleFromLayout()
    {
        if (overlayRoot == null)
            return;

        float width = overlayRoot.resolvedStyle.width;
        if (float.IsNaN(width) || width < 2f)
            return;

        float scale = Mathf.Clamp(width / 420f, 1f, 3f);
        if (Mathf.Abs(scale - uiScale) < 0.02f && scaleActionsApplied)
            return;

        uiScale = scale;
        scaleActionsApplied = true;

        foreach (Action<float> action in scaleActions)
            action(uiScale);

        foreach (PartView view in views)
        {
            if (view.Label != null)
                StyleLabel(view.Label, view == selected);
        }
    }

    private bool scaleActionsApplied;

    private static void Stretch(VisualElement element)
    {
        element.style.position = Position.Absolute;
        element.style.left = 0;
        element.style.right = 0;
        element.style.top = 0;
        element.style.bottom = 0;
    }

    private static void SetRadius(VisualElement element, float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }

    private static void SetBorder(VisualElement element, Color color, float width)
    {
        element.style.borderLeftWidth = width;
        element.style.borderRightWidth = width;
        element.style.borderTopWidth = width;
        element.style.borderBottomWidth = width;
        element.style.borderLeftColor = color;
        element.style.borderRightColor = color;
        element.style.borderTopColor = color;
        element.style.borderBottomColor = color;
    }

    private static string T(string english, string vietnamese) =>
        AppLanguageManager.T(english, vietnamese);
}
