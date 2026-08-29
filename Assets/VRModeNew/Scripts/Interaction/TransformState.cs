using UnityEngine;

public class TransformState : MonoBehaviour
{
    private Vector3 startPosition;
    private Quaternion startRotation;
    private Vector3 startScale;
    
    private Rigidbody rb;
    private bool startGravity; // Biến lưu trạng thái gravity ban đầu

    public void SaveTransform()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
        startScale = transform.localScale;

        rb = GetComponent<Rigidbody>();
        
        if(rb != null)
        {
            startGravity = rb.useGravity;
        }
    }

    public void ResetTransform()
    {
        if(rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero; 
        }

        // CỘNG THÊM 0.05f VÀO TRỤC Y: Tránh lỗi sai số tọa độ khiến model cấn vạch mặt sàn
        transform.position = startPosition + (Vector3.up * 0.05f); 
        transform.rotation = startRotation;
        transform.localScale = startScale;

        Physics.SyncTransforms();

        if(rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = startGravity; 
        }
    }
}