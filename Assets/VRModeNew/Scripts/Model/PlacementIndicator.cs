using UnityEngine;

public class PlacementIndicator : MonoBehaviour
{
    public Camera cam;
    public LayerMask floorLayer;

    void Update()
    {
        
        Ray ray = cam.ScreenPointToRay(
            new Vector3(Screen.width / 2,
                        Screen.height / 2));

        if (Physics.Raycast(ray, out RaycastHit hit, 100, floorLayer))
        {
            transform.position =
            hit.point ;
            transform.rotation = Quaternion.identity;
        }
    }
}