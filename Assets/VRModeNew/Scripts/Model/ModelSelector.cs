using UnityEngine;

public class ModelSelector : MonoBehaviour
{
    public Camera cam;
    public ModelSpawner spawner;


    void Update()
    {
        if(Input.GetMouseButtonDown(0))
        {
            Ray ray = cam.ScreenPointToRay(
                Input.mousePosition
            );


            if(Physics.Raycast(ray, out RaycastHit hit, 100))
            {
                // Lấy object cha chứa Interactable
                Interactable interact =
                hit.collider.GetComponentInParent<Interactable>();


                if(interact != null)
                {
                    spawner.SelectPlacedModel(
                        interact.gameObject
                    );
                }
            }
        }
    }
}