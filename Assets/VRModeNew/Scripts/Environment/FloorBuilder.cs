using UnityEngine;

public class FloorBuilder
{
    private readonly ClassroomConfig config;

    public FloorBuilder(ClassroomConfig config)
    {
        this.config = config;
    }

    public GameObject Build(Transform parent)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);

        floor.name = "Floor";
        floor.transform.SetParent(parent);

        floor.transform.localScale = new Vector3(
            config.roomWidth,
            0.2f,
            config.roomLength);

        floor.transform.position = Vector3.zero;

        ApplyColor(floor, config.floorColor);

        return floor;
    }

    private void ApplyColor(GameObject obj, Color color)
    {
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;

        obj.GetComponent<MeshRenderer>().material = mat;
    }
}