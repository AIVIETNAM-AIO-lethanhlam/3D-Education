using UnityEngine;

public class WallBuilder
{
    private readonly ClassroomConfig config;

    public WallBuilder(ClassroomConfig config)
    {
        this.config = config;
    }

    public void Build(Transform parent)
    {
        CreateFrontWall(parent);
        CreateBackWall(parent);
        CreateLeftWall(parent);
        CreateRightWall(parent);
        CreateCeiling(parent);
        CreateWindows(parent);
        CreateSkirting(parent);
    }

    private void CreateFrontWall(Transform parent)
    {
        CreateWall(
            "FrontWall",
            new Vector3(0,
                        config.roomHeight / 2f,
                        config.roomLength / 2f),

            new Vector3(config.roomWidth,
                        config.roomHeight,
                        config.wallThickness),

            parent);
    }

    private void CreateBackWall(Transform parent)
    {
        CreateWall(
            "BackWall",
            new Vector3(0,
                        config.roomHeight / 2f,
                        -config.roomLength / 2f),

            new Vector3(config.roomWidth,
                        config.roomHeight,
                        config.wallThickness),

            parent);
    }

    private void CreateLeftWall(Transform parent)
    {
        CreateWall(
            "LeftWall",
            new Vector3(-config.roomWidth / 2f,
                        config.roomHeight / 2f,
                        0),

            new Vector3(config.wallThickness,
                        config.roomHeight,
                        config.roomLength),

            parent);
    }

    private void CreateRightWall(Transform parent)
    {
        CreateWall(
            "RightWall",
            new Vector3(config.roomWidth / 2f,
                        config.roomHeight / 2f,
                        0),

            new Vector3(config.wallThickness,
                        config.roomHeight,
                        config.roomLength),

            parent);
    }
    private void CreateWindows(Transform parent)
{
    float y = 2f;

    for(int i = -2; i <= 2; i++)
    {
        CreateWindow(
            new Vector3(-config.roomWidth/2f - 0.05f,
            y,
            i*3f),
            parent);

        CreateWindow(
            new Vector3(config.roomWidth/2f + 0.05f,
            y,
            i*3f),
            parent);
    }
}
private void CreateWindow(Vector3 pos, Transform parent)
{
    GameObject window =
        GameObject.CreatePrimitive(PrimitiveType.Cube);

    window.name = "Window";

    window.transform.SetParent(parent);

    window.transform.localPosition = pos;

    window.transform.localScale =
        new Vector3(
            0.28f,
            1.5f,
            2f);

    ApplyColor(window, config.windowColor);

    CreateWindowFrame(window.transform);
}
private void CreateWindowFrame(Transform parent)
{
    CreateFrame(parent,new Vector3(0,0.8f,0),new Vector3(0.08f,0.08f,2.1f));
    CreateFrame(parent,new Vector3(0,-0.8f,0),new Vector3(0.08f,0.08f,2.1f));

    CreateFrame(parent,new Vector3(0,0,1.02f),new Vector3(0.08f,1.6f,0.08f));
    CreateFrame(parent,new Vector3(0,0,-1.02f),new Vector3(0.08f,1.6f,0.08f));

    CreateFrame(parent,new Vector3(0,0,0),new Vector3(0.08f,1.6f,0.05f));
}
private void CreateFrame(
    Transform parent,
    Vector3 pos,
    Vector3 scale)
{
    GameObject frame =
        GameObject.CreatePrimitive(PrimitiveType.Cube);

    frame.transform.SetParent(parent);

    frame.transform.localPosition = pos;

    frame.transform.localScale = scale;

    ApplyColor(frame, config.frameColor);
}
private void CreateSkirting(Transform parent)
{
    CreateStrip(
        new Vector3(0,0.15f,-config.roomLength/2f),
        new Vector3(config.roomWidth,0.15f,0.05f),
        parent);

    CreateStrip(
        new Vector3(0,0.15f,config.roomLength/2f),
        new Vector3(config.roomWidth,0.15f,0.05f),
        parent);

    CreateStrip(
        new Vector3(-config.roomWidth/2f,0.15f,0),
        new Vector3(0.05f,0.15f,config.roomLength),
        parent);

    CreateStrip(
        new Vector3(config.roomWidth/2f,0.15f,0),
        new Vector3(0.05f,0.15f,config.roomLength),
        parent);
}
private void CreateStrip(
    Vector3 pos,
    Vector3 scale,
    Transform parent)
{
    GameObject strip =
        GameObject.CreatePrimitive(PrimitiveType.Cube);

    strip.transform.SetParent(parent);

    strip.transform.localPosition = pos;

    strip.transform.localScale = scale;

    ApplyColor(strip, config.skirtingColor);
}
    private void CreateCeiling(Transform parent)
    {
        GameObject ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);

        ceiling.name = "Ceiling";

        ceiling.transform.SetParent(parent);

        ceiling.transform.localPosition =
            new Vector3(0,
                        config.roomHeight,
                        0);

        ceiling.transform.localScale =
            new Vector3(config.roomWidth,
                        config.wallThickness,
                        config.roomLength);

        ApplyColor(ceiling, config.ceilingColor);
    }

    private void CreateWall(
        string objectName,
        Vector3 position,
        Vector3 scale,
        Transform parent)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);

        wall.name = objectName;

        wall.transform.SetParent(parent);

        wall.transform.localPosition = position;

        wall.transform.localScale = scale;

        ApplyColor(wall, config.wallColor);
    }

    private void ApplyColor(GameObject obj, Color color)
{
    Shader shader = Shader.Find("Universal Render Pipeline/Lit");

    if(shader == null)
        shader = Shader.Find("Standard");

    Material mat = new Material(shader);

    mat.color = color;

    obj.GetComponent<MeshRenderer>().material = mat;
}
}