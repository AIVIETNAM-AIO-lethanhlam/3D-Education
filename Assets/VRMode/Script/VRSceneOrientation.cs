using UnityEngine;

namespace VRMode.Script
{
    public class VRSceneOrientation : MonoBehaviour
    {
        private void Awake()
        {
            // Tự động ép màn hình xoay NGANG khi vừa mở VRScene
            Screen.orientation = ScreenOrientation.LandscapeLeft;
        }

        private void OnDestroy()
        {
            // Tự động trả màn hình về DỌC khi thoát khỏi VRScene (chuyển sang Scene khác)
            Screen.orientation = ScreenOrientation.Portrait;
        }
    }
}