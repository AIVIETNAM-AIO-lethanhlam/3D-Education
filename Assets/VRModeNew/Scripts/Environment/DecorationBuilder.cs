using UnityEngine;

public class DecorationBuilder
{
    private readonly ClassroomConfig config;

    public DecorationBuilder(ClassroomConfig config)
    {
        this.config = config;
    }

    public void Build(Transform parent)
    {
        CreateClock(parent);
    }

    private void CreateClock(Transform parent)
    {
        GameObject clock = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        clock.name = "Clock";
        clock.transform.SetParent(parent);
        
        // Treo đồng hồ ở giữa tường trên bảng
        clock.transform.localPosition = new Vector3(0f, 3.2f, -11.9f);
        clock.transform.localScale = new Vector3(0.4f, 0.05f, 0.4f);
        clock.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        ApplyColor(clock, Color.white);
    }

    private void ApplyColor(GameObject obj, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.color = color;
        obj.GetComponent<MeshRenderer>().material = mat;
    }
}