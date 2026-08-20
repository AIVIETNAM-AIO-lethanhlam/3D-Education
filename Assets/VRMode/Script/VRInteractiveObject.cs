using UnityEngine;
using UnityEngine.Events;

namespace VRMode.Script
{
    [RequireComponent(typeof(Collider))] // Bắt buộc có Collider để Raycast nhận diện
    public class VRInteractiveObject : MonoBehaviour
    {
        [Header("Sự kiện khi nhìn đủ thời gian")]
        public UnityEvent onGazeClick;

        public void OnGazeTrigger()
        {
            Debug.Log($"Đã tương tác với: {gameObject.name}");
            onGazeClick?.Invoke(); // Kích hoạt sự kiện (mở UI, xoay hình, phát âm thanh...)
        }
    }
}