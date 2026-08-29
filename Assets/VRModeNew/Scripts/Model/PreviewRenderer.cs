using UnityEngine;
using UnityEngine.UI;

public class PreviewRenderer : MonoBehaviour
{
    public Camera previewCamera;

    public RenderTexture renderTexture;

    public RawImage targetImage;

    private GameObject currentPreview;

    public void Show(GameObject prefab)
    {
        if(currentPreview != null)
            Destroy(currentPreview);

        currentPreview =
            Instantiate(prefab);

        currentPreview.layer =
            LayerMask.NameToLayer("Preview");

        SetLayerRecursively(
            currentPreview,
            LayerMask.NameToLayer("Preview"));

        currentPreview.transform.position =
            new Vector3(1000,1000,1000);

        currentPreview.transform.rotation =
            Quaternion.identity;

        previewCamera.targetTexture =
            renderTexture;

        targetImage.texture =
            renderTexture;
    }
    
    void SetLayerRecursively(GameObject obj,int layer)
    {
        obj.layer = layer;

        foreach(Transform t in obj.transform)
            SetLayerRecursively(t.gameObject,layer);
    }
}