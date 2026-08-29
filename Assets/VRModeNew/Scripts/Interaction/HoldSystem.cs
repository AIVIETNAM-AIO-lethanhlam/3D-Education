using UnityEngine;

public class HoldSystem : MonoBehaviour
{
    [Header("Hold Point")]
    public Transform holdPoint;

    [Header("Speed")]
    public float moveSpeed = 10f;

    private Transform heldObject;
    private Rigidbody heldRb;

    // Các biến dùng để tính toán lực ném chuẩn VR
    private Vector3 previousPosition;
    private Vector3 currentVelocity;

    public bool IsHolding => heldObject != null;

    void Update()
    {
        if (heldObject == null)
            return;

        // Nội suy vị trí bám theo điểm cầm nắm
        heldObject.position = Vector3.Lerp(
            heldObject.position,
            holdPoint.position,
            Time.deltaTime * moveSpeed);

        heldObject.rotation = Quaternion.Lerp(
            heldObject.rotation,
            holdPoint.rotation,
            Time.deltaTime * moveSpeed);

        // Tính toán vận tốc di chuyển tay (tạo cảm giác ném tự nhiên)
        currentVelocity = (heldObject.position - previousPosition) / Time.deltaTime;
        previousPosition = heldObject.position;
    }

    public void Hold(Transform target)
    {
        if (heldObject != null)
            return;

        heldObject = target;
        heldRb = target.GetComponent<Rigidbody>();

        if (heldRb != null)
        {
            heldRb.isKinematic = true;
            heldRb.useGravity = false;
        }

        // Tắt toàn bộ Collider để không vướng vào người chơi hoặc sàn khi đang cầm
        Collider[] colliders = target.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = false;
        }

        heldObject.SetParent(holdPoint, false);
        heldObject.localPosition = Vector3.zero;
        heldObject.localRotation = Quaternion.identity;
        
        previousPosition = heldObject.position;
    }

    public void Release()
    {
        if (heldObject == null)
            return;

        heldObject.SetParent(null);

        // 1. KÉO VẬT THỂ VỀ VÙNG AN TOÀN TRƯỚC KHI BẬT VẬT LÝ (ANTI-CLIPPING)
        // Lấy hướng từ vật thể trỏ ngược về Camera
        Vector3 safeDirection = (Camera.main.transform.position - heldObject.position).normalized;
        // Kéo giật vật thể về phía người chơi 0.2f và nâng lên 0.1f để dứt điểm việc bị kẹt dưới sàn
        heldObject.position += (safeDirection * 0.2f) + (Vector3.up * 0.1f);

        // 2. Bật lại toàn bộ Collider và ép chuẩn MeshCollider
        Collider[] colliders = heldObject.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = true;
            if (col is MeshCollider meshCol)
            {
                meshCol.convex = true;
            }
        }

        // Ép Unity đồng bộ không gian lập tức
        Physics.SyncTransforms();

        // 3. Xử lý Rigidbody
        if (heldRb != null)
        {
            heldRb.isKinematic = false;
            heldRb.useGravity = true;
            
            // Cài đặt dò va chạm mức tối đa dành cho các vật thể ném đi
            heldRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Xử lý lực ném
            Vector3 throwForce = currentVelocity;
            
            // QUAN TRỌNG: Nếu đang ném cắm xuống đất (y < 0), triệt tiêu ngay lực cắm xuống đó
            if (throwForce.y < 0)
            {
                throwForce.y = 0;
            }

            // Nếu người chơi đứng im và bấm thả (vận tốc < 1), cấp một lực đẩy nhẹ ra trước
            if (throwForce.magnitude < 1f)
            {
                throwForce = Camera.main.transform.forward * 1.5f;
            }

            // Gán lực và thêm một chút độ xoáy (Torque) để vật rơi tự nhiên hơn
            heldRb.linearVelocity = throwForce;
            heldRb.angularVelocity = UnityEngine.Random.insideUnitSphere * 2f;
        }

        // Dọn dẹp biến
        heldObject = null;
        heldRb = null;
    }
}