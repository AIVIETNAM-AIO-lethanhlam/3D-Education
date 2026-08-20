using UnityEngine;
using UnityEngine.UI;

namespace VRMode.Script
{
    public class VRGazePointer : MonoBehaviour
    {
        [Header("Gaze Settings")]
        [SerializeField] private float rayDistance = 20f;
        [SerializeField] private float gazeHoldTime = 2f; // Thời gian nhìn 2s để kích hoạt
        [SerializeField] private Image gazeProgressImage; // Kéo GazeImage vào đây

        private float timer = 0f;
        private VRInteractiveObject currentTarget;

        void Update()
        {
            Ray ray = new Ray(transform.position, transform.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, rayDistance))
            {
                VRInteractiveObject interactive = hit.collider.GetComponent<VRInteractiveObject>();

                if (interactive != null)
                {
                    if (currentTarget != interactive)
                    {
                        ResetGaze();
                        currentTarget = interactive;
                    }

                    timer += Time.deltaTime;
                    if (gazeProgressImage != null)
                    {
                        gazeProgressImage.fillAmount = timer / gazeHoldTime;
                    }

                    if (timer >= gazeHoldTime)
                    {
                        currentTarget.OnGazeTrigger();
                        ResetGaze();
                    }
                    return;
                }
            }

            // Nếu không nhìn vào vật thể nào
            ResetGaze();
        }

        private void ResetGaze()
        {
            currentTarget = null;
            timer = 0f;
            if (gazeProgressImage != null)
            {
                gazeProgressImage.fillAmount = 0f;
            }
        }
    }
}