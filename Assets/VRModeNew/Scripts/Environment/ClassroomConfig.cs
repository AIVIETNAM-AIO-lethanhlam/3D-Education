using UnityEngine;

[System.Serializable]
public class ClassroomConfig
{
    [Header("Room")]
    public float roomWidth = 18f;
    public float roomLength = 24f;
    public float roomHeight = 4f;
    public float wallThickness = 0.2f;

    [Header("Student Area")]
    public int rows = 5;
    public int columns = 4;

    public float deskSpacingX = 2.4f;
    public float deskSpacingZ = 2.6f;

    [Header("Furniture")]
    public GameObject skeletonPrefab;
    public Vector3 teacherDeskPosition = new(0f, 0.4f, -8f);
    public Vector3 whiteboardPosition = new(0f, 2f, -11.8f);
    public Vector3 screenPosition = new(0f, 2f, -11.7f);
    public Vector3 pedestalPosition = new(0f, 0.5f, -3f);
    [Header("Desk Size")]
    public Vector3 deskSize = new(1.2f, 0.7f, 0.8f);

    [Header("Chair Size")]
    public Vector3 chairSeatSize = new(0.45f, 0.08f, 0.45f);

    [Header("Teacher Desk")]
    public Vector3 teacherDeskSize = new(2f, 0.8f, 0.8f);

    [Header("Whiteboard")]
    public Vector3 whiteboardSize = new(4f, 2f, 0.08f);

    [Header("Projector Screen")]
    public Vector3 screenSize = new(3.5f, 2f, 0.05f);

[Header("Pedestal")]
public float pedestalRadius = 0.5f;
public float pedestalHeight = 1f;

   [Header("Colors")]

// Floor
public Color floorColor = new Color(0.72f, 0.74f, 0.78f);

// Wall
public Color wallColor = new Color(0.95f, 0.93f, 0.88f);

// Ceiling
public Color ceilingColor = Color.white;

// Furniture
public Color deskColor = new Color(0.46f, 0.30f, 0.18f);
public Color chairColor = new Color(0.18f, 0.18f, 0.20f);
public Color boardColor = new Color(0.97f, 0.97f, 0.97f);

// Window
public Color windowColor = new Color(0.75f, 0.90f, 1f);
public Color frameColor = new Color(0.30f, 0.30f, 0.30f);

// Decoration
public Color skirtingColor = new Color(0.55f, 0.55f, 0.55f);
}