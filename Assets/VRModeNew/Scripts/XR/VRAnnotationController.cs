using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Một Canvas duy nhất cố định bên trái màn hình, chứa danh sách LabelCard xếp dọc (VerticalLayoutGroup).
/// Mỗi card có một LineRenderer nối từ cạnh phải của card tới marker 3D tương ứng trên mô hình.
/// </summary>
public class VRAnnotationController : MonoBehaviour
{
    [Header("Canvas")]
    [Tooltip("true = ScreenSpaceCamera (Mobile/AR). false = WorldSpace panel cố định trước mặt người dùng (VR).")]
    [SerializeField] private bool useScreenSpace = true;
    [Tooltip("Khoảng cách từ camera tới mặt phẳng UI (mét). Đặt nhỏ để mô hình không che UI.")]
    [SerializeField] private float screenPlaneDistance = 0.3f;
    [SerializeField] private Vector2 referenceResolution = new Vector2(1080f, 1920f);
    [SerializeField] private float worldPanelDistance = 1.2f;   // chỉ dùng khi useScreenSpace = false
    [SerializeField] private float worldPanelScale = 0.001f;    // chỉ dùng khi useScreenSpace = false

    [Header("Danh sách bên trái")]
    [SerializeField] private float panelWidth = 400f;
    [SerializeField] private Vector2 panelAnchoredPosition = new Vector2(24f, -120f); // cách mép trái, lệch xuống dưới giữa
    [SerializeField] private float cardSpacing = 14f;
    [SerializeField] private float cardMinHeight = 64f;
    [SerializeField] private float cardFontSize = 26f;

    [Header("Đường nối")]
    [SerializeField] private float lineStartWidth = 0.002f;
    [SerializeField] private float lineEndWidth = 0.002f;
    [SerializeField] private Color lineColor = new Color(0.15f, 0.4f, 1f, 0.95f);

    private VRXRSceneController sceneController;
    private VRModelDetailAnchorController anchorController;
    private Transform currentModelTransform;
    private List<VRModelDetailService.ModelPartData> activeParts = new List<VRModelDetailService.ModelPartData>();
    private List<GameObject> activeMarkers = new List<GameObject>();

    private bool annotationsVisible = false;

    private class AnnotationPair
    {
        public VRModelDetailService.ModelPartData partData;
        public GameObject cardObject;
        public RectTransform cardRect;
        public Transform markerTransform;
        public Vector3 localAnchorPos;
        public LineRenderer lineRenderer;
        public Image cardBackground;
    }

    private readonly List<AnnotationPair> activePairs = new List<AnnotationPair>();
    private readonly Vector3[] cornerBuffer = new Vector3[4];
    private Material lineMaterial;

    // Canvas chung
    private Canvas panelCanvas;
    private RectTransform panelRect;   // có VerticalLayoutGroup
    private Transform linesRoot;

    public bool IsVisible => annotationsVisible;

    public void Initialize(VRXRSceneController controller, VRModelDetailAnchorController detailAnchorController)
    {
        sceneController = controller;
        anchorController = detailAnchorController;
        Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        lineMaterial = new Material(sh);
    }

    public void SetModelAndParts(Transform modelTransform, List<VRModelDetailService.ModelPartData> parts, List<GameObject> markers = null)
    {
        Clear();

        currentModelTransform = modelTransform;
        if (parts != null) activeParts = new List<VRModelDetailService.ModelPartData>(parts);
        if (markers != null) activeMarkers = new List<GameObject>(markers);

        if (annotationsVisible)
        {
            BuildAnnotationUI();
        }
    }

    public void ToggleAnnotations()
    {
        SetAnnotationsEnabled(!annotationsVisible);
    }

    public void SetAnnotationsEnabled(bool enabled)
    {
        annotationsVisible = enabled;

        if (enabled && activePairs.Count == 0 && currentModelTransform != null)
        {
            BuildAnnotationUI();
        }

        ApplyVisibility();
        if (enabled && !useScreenSpace) PlacePanelInFrontOfCamera();
    }

    private void ApplyVisibility()
    {
        if (panelCanvas != null) panelCanvas.gameObject.SetActive(annotationsVisible);
        if (linesRoot != null) linesRoot.gameObject.SetActive(annotationsVisible);
    }

    // ------------------------------------------------------------------
    // Canvas chung + VerticalLayoutGroup
    // ------------------------------------------------------------------
    private void EnsurePanel()
    {
        if (panelCanvas != null) return;

        GameObject canvasGo = new GameObject("XR_AnnotationPanelCanvas", typeof(RectTransform));
        panelCanvas = canvasGo.AddComponent<Canvas>();
        RectTransform canvasRect = (RectTransform)canvasGo.transform;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();

        if (useScreenSpace)
        {
            panelCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            panelCanvas.worldCamera = Camera.main;
            panelCanvas.planeDistance = screenPlaneDistance;
            panelCanvas.sortingOrder = 10;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }
        else
        {
            panelCanvas.renderMode = RenderMode.WorldSpace;
            panelCanvas.worldCamera = Camera.main;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 3f;
            scaler.referencePixelsPerUnit = 100f;

            canvasRect.sizeDelta = referenceResolution;
            canvasRect.localScale = Vector3.one * worldPanelScale;
        }

        canvasGo.AddComponent<GraphicRaycaster>();              // touch / chuột
        canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>(); // controller XR

        // Panel chứa danh sách, neo vào cạnh trái, giữa theo chiều dọc
        GameObject panelGo = new GameObject("AnnotationListPanel", typeof(RectTransform));
        panelGo.transform.SetParent(canvasRect, false);
        panelRect = (RectTransform)panelGo.transform;
        panelRect.anchorMin = new Vector2(0f, 0.5f);
        panelRect.anchorMax = new Vector2(0f, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = panelAnchoredPosition;
        panelRect.sizeDelta = new Vector2(panelWidth, 0f);

        VerticalLayoutGroup vlg = panelGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = cardSpacing;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter fitter = panelGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        linesRoot = new GameObject("AnnotationLines").transform;
    }

    private void BuildAnnotationUI()
    {
        ClearUIOnly();
        if (currentModelTransform == null || activeParts.Count == 0) return;

        EnsurePanel();

        // Tất cả LabelCard là con của VerticalLayoutGroup, tự xếp chồng theo chiều dọc
        foreach (var part in activeParts)
        {
            if (part == null || !part.is_active) continue;

            GameObject marker = null;
            if (activeMarkers != null)
                marker = activeMarkers.Find(m => m != null && m.name == "VRInfoMarker_" + part.part_key);

            AnnotationPair pair = CreateAnnotationCardAndLine(part, marker);
            if (pair != null) activePairs.Add(pair);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        ApplyVisibility();
        if (annotationsVisible && !useScreenSpace) PlacePanelInFrontOfCamera();
    }

    private AnnotationPair CreateAnnotationCardAndLine(VRModelDetailService.ModelPartData part, GameObject markerGo)
    {
        if (part == null) return null;

        // Neo dự phòng khi không có marker
        Vector3 worldAnchorPos = markerGo != null ? markerGo.transform.position
            : (anchorController != null ? anchorController.GetPartWorldPosition(part) : Vector3.zero);
        if (worldAnchorPos == Vector3.zero && currentModelTransform != null)
            worldAnchorPos = currentModelTransform.position;
        Vector3 localAnchorPos = currentModelTransform != null
            ? currentModelTransform.InverseTransformPoint(worldAnchorPos) : Vector3.zero;

        // 1. LabelCard (con của VerticalLayoutGroup)
        GameObject cardGo = new GameObject("LabelCard_" + part.part_key, typeof(RectTransform));
        cardGo.transform.SetParent(panelRect, false);
        RectTransform cardRect = (RectTransform)cardGo.transform;

        Image cardBg = cardGo.AddComponent<Image>();
        cardBg.color = new Color32(255, 255, 255, 240);

        Outline border = cardGo.AddComponent<Outline>();
        border.effectColor = new Color(0.62f, 0.75f, 0.95f, 1f);
        border.effectDistance = new Vector2(2f, -2f);

        LayoutElement cardLayoutElement = cardGo.AddComponent<LayoutElement>();
        cardLayoutElement.minHeight = cardMinHeight;

        HorizontalLayoutGroup cardLayout = cardGo.AddComponent<HorizontalLayoutGroup>();
        cardLayout.padding = new RectOffset(16, 16, 10, 10);
        cardLayout.childAlignment = TextAnchor.MiddleCenter;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = true;

        // 2. Text: căn giữa, tự xuống dòng như trong ảnh
        GameObject textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(cardGo.transform, false);
        TextMeshProUGUI labelText = textGo.AddComponent<TextMeshProUGUI>();

        bool isVi = AppLanguageManager.IsVietnamese;
        labelText.text = isVi
            ? (string.IsNullOrWhiteSpace(part.part_name_vi) ? part.part_name : part.part_name_vi)
            : (string.IsNullOrWhiteSpace(part.part_name) ? part.part_name_vi : part.part_name);
        labelText.fontSize = cardFontSize;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = new Color32(15, 23, 42, 255);
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.textWrappingMode = TextWrappingModes.Normal;
        labelText.overflowMode = TextOverflowModes.Overflow;
        labelText.raycastTarget = false;

        // 3. Tương tác: Button (click) + EventTrigger (hover)
        VRModelDetailService.ModelPartData partRef = part;
        GameObject markerTarget = markerGo;

        Button btn = cardGo.AddComponent<Button>();
        btn.targetGraphic = cardBg;
        btn.onClick.AddListener(() =>
        {
            if (sceneController != null) sceneController.OpenInfoPanelForPart(partRef);
            if (markerTarget != null)
            {
                markerTarget.transform.localScale = Vector3.one * 0.065f;
                Renderer r = markerTarget.GetComponent<Renderer>();
                if (r != null) r.material.color = new Color(0.2f, 1f, 0.4f, 1f);
            }
        });

        EventTrigger trigger = cardGo.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerEnter, () => OnCardHoverEnter(cardBg, markerTarget));
        AddTrigger(trigger, EventTriggerType.PointerExit, () => OnCardHoverExit(cardBg, markerTarget));

        // 4. LineRenderer (world space) nằm ngoài Canvas
        GameObject lineGo = new GameObject("AnnotationLine_" + part.part_key);
        lineGo.transform.SetParent(linesRoot, false);
        LineRenderer line = lineGo.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = lineStartWidth;
        line.endWidth = lineEndWidth;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.material = lineMaterial;
        line.startColor = lineColor;
        line.endColor = lineColor;

        return new AnnotationPair
        {
            partData = part,
            cardObject = cardGo,
            cardRect = cardRect,
            markerTransform = markerGo != null ? markerGo.transform : null,
            localAnchorPos = localAnchorPos,
            lineRenderer = line,
            cardBackground = cardBg
        };
    }

    private static void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    private static void OnCardHoverEnter(Image bg, GameObject marker)
    {
        if (bg != null) bg.color = new Color32(224, 242, 254, 255);
        if (marker != null)
        {
            marker.transform.localScale = Vector3.one * 0.055f;
            Renderer r = marker.GetComponent<Renderer>();
            if (r != null && r.material != null)
                r.material.color = new Color(0f, 0.92f, 1f, 1f);
        }
    }

    private static void OnCardHoverExit(Image bg, GameObject marker)
    {
        if (bg != null) bg.color = new Color32(255, 255, 255, 240);
        if (marker != null)
        {
            marker.transform.localScale = Vector3.one * 0.04f;
            Renderer r = marker.GetComponent<Renderer>();
            if (r != null && r.material != null)
                r.material.color = new Color(1f, 0.84f, 0f, 0.95f);
        }
    }

    /// <summary>Chế độ VR: đặt panel một lần trước mặt người dùng, không bám theo marker.</summary>
    private void PlacePanelInFrontOfCamera()
    {
        Camera cam = Camera.main;
        if (panelCanvas == null || cam == null) return;

        Vector3 forward = cam.transform.forward; forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        forward.Normalize();

        Transform t = panelCanvas.transform;
        t.position = cam.transform.position + forward * worldPanelDistance;
        t.rotation = Quaternion.LookRotation(forward); // mặt trước hướng về người dùng
    }

    // ------------------------------------------------------------------
    // LineRenderer: cạnh phải của card -> vị trí marker
    // ------------------------------------------------------------------
    private void LateUpdate()
    {
        if (!annotationsVisible || activePairs.Count == 0) return;

        // ScreenSpaceCamera cần worldCamera hợp lệ, nếu không Canvas sẽ hoạt động như Overlay
        if (panelCanvas != null && panelCanvas.worldCamera == null)
            panelCanvas.worldCamera = Camera.main;

        for (int i = 0; i < activePairs.Count; i++)
        {
            AnnotationPair pair = activePairs[i];
            if (pair == null || pair.cardObject == null || pair.lineRenderer == null) continue;

            Vector3 target;
            if (pair.markerTransform != null && pair.markerTransform.gameObject.activeInHierarchy)
                target = pair.markerTransform.position;
            else if (currentModelTransform != null)
                target = currentModelTransform.TransformPoint(pair.localAnchorPos);
            else
            {
                pair.lineRenderer.enabled = false;
                continue;
            }

            pair.lineRenderer.enabled = true;

            // Corners: 0 = dưới-trái, 1 = trên-trái, 2 = trên-phải, 3 = dưới-phải
            pair.cardRect.GetWorldCorners(cornerBuffer);
            Vector3 rightEdgeMid = (cornerBuffer[2] + cornerBuffer[3]) * 0.5f;

            pair.lineRenderer.SetPosition(0, rightEdgeMid);
            pair.lineRenderer.SetPosition(1, target);
        }
    }

    private void ClearUIOnly()
    {
        foreach (var pair in activePairs)
        {
            if (pair == null) continue;
            if (pair.cardObject != null) Destroy(pair.cardObject);
            if (pair.lineRenderer != null) Destroy(pair.lineRenderer.gameObject);
        }
        activePairs.Clear();
    }

    public void Clear()
    {
        ClearUIOnly();
        currentModelTransform = null;
        activeParts.Clear();
        activeMarkers.Clear();
    }

    private void OnDestroy()
    {
        Clear();
        if (panelCanvas != null) Destroy(panelCanvas.gameObject);
        if (linesRoot != null) Destroy(linesRoot.gameObject);
        if (lineMaterial != null) Destroy(lineMaterial);
    }
}