using UnityEngine;

public class FurnitureBuilder
{
    private readonly ClassroomConfig config;
    private readonly GameObject skeletonPrefab;
    public FurnitureBuilder(
    ClassroomConfig config,
    GameObject skeletonPrefab)
    {
        this.config = config;
        this.skeletonPrefab = skeletonPrefab;
    }

    public void Build(Transform parent)
    {
        CreateTeacherDesk(parent);
        CreateWhiteboard(parent);
        CreateProjectorScreen(parent);
        CreatePedestal(parent);

        GenerateStudentArea(parent);
    }

    //---------------------------------------------------
    // Student Area
    //---------------------------------------------------

    private void GenerateStudentArea(Transform parent)
    {
        float startX = -(config.columns - 1) * config.deskSpacingX * 0.5f;
        float startZ = -2f;

        for (int row = 0; row < config.rows; row++)
        {
            for (int col = 0; col < config.columns; col++)
            {
                GameObject studentSet = new GameObject($"StudentSet_{row}_{col}");
                studentSet.transform.SetParent(parent);

                studentSet.transform.localPosition = new Vector3(
                    startX + col * config.deskSpacingX,
                    0f,
                    startZ + row * config.deskSpacingZ);

                CreateStudentDesk(studentSet.transform);
                CreateChair(studentSet.transform);
            }
        }
    }

    //---------------------------------------------------
    // Desk
    //---------------------------------------------------

    private void CreateStudentDesk(Transform parent)
    {
        GameObject desk = GameObject.CreatePrimitive(PrimitiveType.Cube);

        desk.name = "Desk";

        desk.transform.SetParent(parent);

        desk.transform.localPosition = new Vector3(0f, 0.35f, 0f);

        desk.transform.localScale = config.deskSize;

        ApplyColor(desk, config.deskColor);
    }

    //---------------------------------------------------
    // Chair
    //---------------------------------------------------

private void CreateChair(Transform parent)
{
    GameObject chair = new GameObject("Chair");

    chair.transform.SetParent(parent);

    // Ghế nằm phía sau bàn
    chair.transform.localPosition = new Vector3(0f, 0f, 0.75f);

    //---------------- Seat ----------------

    GameObject seat = GameObject.CreatePrimitive(PrimitiveType.Cube);

    seat.transform.SetParent(chair.transform);

    seat.transform.localPosition = new Vector3(0f, 0.35f, 0f);

    seat.transform.localScale = new Vector3(0.55f, 0.08f, 0.55f);

    ApplyColor(seat, config.chairColor);

    //---------------- Back ----------------

    GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube);

    back.transform.SetParent(chair.transform);

    // Lưng ghế ở phía sau người ngồi (+Z)
    back.transform.localPosition = new Vector3(0f, 0.7f, 0.23f);

    back.transform.localScale = new Vector3(0.55f, 0.7f, 0.08f);

    ApplyColor(back, config.chairColor);

    //---------------- Legs ----------------

    CreateChairLeg(chair.transform, new Vector3(-0.2f, 0.15f, -0.2f));
    CreateChairLeg(chair.transform, new Vector3( 0.2f, 0.15f, -0.2f));
    CreateChairLeg(chair.transform, new Vector3(-0.2f, 0.15f,  0.2f));
    CreateChairLeg(chair.transform, new Vector3( 0.2f, 0.15f,  0.2f));
}
    private void CreateChairLeg(
    Transform parent,
    Vector3 pos)
{
    GameObject leg =
        GameObject.CreatePrimitive(PrimitiveType.Cube);

    leg.transform.SetParent(parent);

    leg.transform.localPosition = pos;

    leg.transform.localScale =
        new Vector3(0.06f,0.35f,0.06f);

    ApplyColor(leg, config.chairColor);
}

    //---------------------------------------------------
    // Teacher Desk
    //---------------------------------------------------

    private void CreateTeacherDesk(Transform parent)
    {
        GameObject desk =
            GameObject.CreatePrimitive(PrimitiveType.Cube);

        desk.name = "TeacherDesk";

        desk.transform.SetParent(parent);

        desk.transform.localPosition =
            config.teacherDeskPosition;

        desk.transform.localScale =
            config.teacherDeskSize;

        ApplyColor(desk, config.deskColor);
    }

    //---------------------------------------------------
    // Whiteboard
    //---------------------------------------------------

    private void CreateWhiteboard(Transform parent)
    {
        GameObject board =
            GameObject.CreatePrimitive(PrimitiveType.Cube);

        board.name = "Whiteboard";

        board.transform.SetParent(parent);

        board.transform.localPosition =
            config.whiteboardPosition;

        board.transform.localScale =
            config.whiteboardSize;

        ApplyColor(board, config.boardColor);
    }

    
    //---------------------------------------------------
    // Screen
    //---------------------------------------------------

    private void CreateProjectorScreen(Transform parent)
    {
        GameObject screen =
            GameObject.CreatePrimitive(PrimitiveType.Cube);

        screen.name = "Screen";

        screen.transform.SetParent(parent);

        screen.transform.localPosition =
            config.screenPosition;

        screen.transform.localScale =
            config.screenSize;

        ApplyColor(screen, Color.white);
    }

    //---------------------------------------------------
    // Pedestal
    //---------------------------------------------------

    private void CreatePedestal(Transform parent)
    {
        // Bục
        GameObject pedestal =
            GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pedestal.name = "Pedestal";
        pedestal.transform.SetParent(parent);
        pedestal.transform.localPosition =
            config.pedestalPosition;
        pedestal.transform.localScale =
            new Vector3(
                config.pedestalRadius,
                config.pedestalHeight * 0.5f,
                config.pedestalRadius);
        ApplyColor(pedestal, Color.gray);

        // Kiểm tra xem skeletonPrefab đã được gán chưa rồi mới Instantiate
        if (skeletonPrefab != null)
        {
            GameObject skeleton = Object.Instantiate(skeletonPrefab, parent);
            // skeleton.transform.localScale = Vector3.one * 0.01f;
            skeleton.transform.localPosition = config.pedestalPosition + new Vector3(0f, config.pedestalHeight, 0f);
            Interactable interact = skeleton.AddComponent<Interactable>();
            interact.objectName = "Human Skeleton";
            interact.canHold = true;
            
            Rigidbody rb = skeleton.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = skeleton.AddComponent<Rigidbody>();
            }
            rb.mass = 5f;
            rb.useGravity = false;
        }
        else
        {
            Debug.LogWarning("Chưa gán Skeleton Prefab! Đã bỏ qua bước tạo bộ xương.");
        }
    }

    //---------------------------------------------------
    // Material
    //---------------------------------------------------

    private void ApplyColor(GameObject obj, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
            shader = Shader.Find("Standard");

        Material mat = new Material(shader);

        mat.color = color;

        obj.GetComponent<MeshRenderer>().material = mat;
    }
}