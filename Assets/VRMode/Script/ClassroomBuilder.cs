using UnityEngine;

namespace VRMode.Script
{
    public class ClassroomBuilder : MonoBehaviour
    {
        [Header("Cấu hình Màu sắc Lớp Học")]
        [SerializeField] private Color wallColor = new Color(0.9f, 0.9f, 0.85f);
        [SerializeField] private Color floorColor = new Color(0.5f, 0.5f, 0.5f);
        [SerializeField] private Color blackboardColor = new Color(0.1f, 0.35f, 0.2f);
        [SerializeField] private Color deskColor = new Color(0.6f, 0.4f, 0.2f);

        private void Start()
        {
            // Tự động reset vị trí về gốc (0,0,0)
            transform.position = Vector3.zero;

            // Tự động dựng lớp học nếu chưa có
            if (transform.childCount == 0)
            {
                BuildClassroom();
            }
        }

        [ContextMenu("Build Classroom Now")]
        public void BuildClassroom()
        {
            // Xóa lớp học cũ nếu có
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(transform.GetChild(i).gameObject);
            }

            GameObject room = new GameObject("Classroom_3D");
            room.transform.SetParent(transform);
            room.transform.localPosition = Vector3.zero;

            // Tạo Material
            Material floorMat = CreateMaterial(floorColor);
            Material wallMat = CreateMaterial(wallColor);
            Material boardMat = CreateMaterial(blackboardColor);
            Material deskMat = CreateMaterial(deskColor);

            // 1. Sàn nhà (12m x 14m)
            CreateCube("Floor", room.transform, new Vector3(0, -0.1f, 5), new Vector3(12, 0.2f, 14), floorMat);

            // 2. Trần nhà
            CreateCube("Ceiling", room.transform, new Vector3(0, 3.6f, 5), new Vector3(12, 0.2f, 14), wallMat);

            // 3. Tường trước (Nơi gắn Bảng đen)
            CreateCube("FrontWall", room.transform, new Vector3(0, 1.75f, 12f), new Vector3(12, 3.5f, 0.2f), wallMat);

            // 4. Tường sau
            CreateCube("BackWall", room.transform, new Vector3(0, 1.75f, -2f), new Vector3(12, 3.5f, 0.2f), wallMat);

            // 5. Tường trái & Tường phải
            CreateCube("LeftWall", room.transform, new Vector3(-6f, 1.75f, 5), new Vector3(0.2f, 3.5f, 14), wallMat);
            CreateCube("RightWall", room.transform, new Vector3(6f, 1.75f, 5), new Vector3(0.2f, 3.5f, 14), wallMat);

            // 6. Bảng xanh dạy học
            CreateCube("Blackboard", room.transform, new Vector3(0, 1.8f, 11.88f), new Vector3(5f, 2f, 0.05f), boardMat);

            // 7. Bàn Bục Giảng (Giáo viên)
            CreateCube("TeacherDesk", room.transform, new Vector3(0, 0.45f, 9.5f), new Vector3(2f, 0.9f, 1f), deskMat);

            // 8. Dãy Bàn Học Sinh (3 dãy x 3 hàng)
            for (int row = 0; row < 3; row++)
            {
                for (int col = -1; col <= 1; col++)
                {
                    Vector3 deskPos = new Vector3(col * 3f, 0.35f, 2.5f + (row * 2.3f));
                    CreateCube($"StudentDesk_{row}_{col}", room.transform, deskPos, new Vector3(1.5f, 0.7f, 0.8f), deskMat);
                }
            }

            Debug.Log("Đã tạo thành công Lớp Học 3D!");
        }

        private GameObject CreateCube(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.localPosition = pos;
            cube.transform.localScale = scale;
            if (mat != null) cube.GetComponent<Renderer>().material = mat;
            return cube;
        }

        private Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            Material mat = new Material(shader);
            mat.color = color;
            return mat;
        }
    }
}