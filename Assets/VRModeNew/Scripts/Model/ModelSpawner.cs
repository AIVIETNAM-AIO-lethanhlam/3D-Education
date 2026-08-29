using UnityEngine;
using System.Collections.Generic;
using System;
public class ModelSpawner : MonoBehaviour
{
    private List<GameObject> spawnedModels = new List<GameObject>();

    private GameObject selectedModel;
    public Action OnModeChanged;
    [Header("Rotation")]
    public bool RotateMode => rotateMode;
    
    private bool rotateMode = false;
    public float rotateSpeed = 90f;

    private float currentRotationY = 0;
    public Camera cam;

    // public List<ModelData> models =
    // new List<ModelData>();
    public Action<GameObject> OnModelSelected;

    private GameObject selectedPrefab;

    public LayerMask floorLayer;

  
    public PlacementIndicator indicator;
    private GameObject previewModel;
    private bool scaleMode = false;

    public bool ScaleMode => scaleMode;

    private float currentScale = 1f;

    public float scaleStep = 0.1f;

    public float minScale = 0.2f;

    public float maxScale = 3f;
    public void ToggleScaleMode()
    {
        scaleMode = !scaleMode;
        if (scaleMode)
            rotateMode = false;
        OnModeChanged?.Invoke();
    }
    public void ToggleRotateMode()
    {
        rotateMode = !rotateMode;
        if (rotateMode)
            scaleMode = false;
        OnModeChanged?.Invoke();
    }
    public void ScaleUp()
    {
        if (!scaleMode) return;

        currentScale =
            Mathf.Clamp(
                currentScale + scaleStep,
                minScale,
                maxScale);

        ApplyScale();
    }
    public void ScaleDown()
    {
        if (!scaleMode) return;

        currentScale =
            Mathf.Clamp(
                currentScale - scaleStep,
                minScale,
                maxScale);

        ApplyScale();
    }
    private void ApplyScale()
    {
        Vector3 s = Vector3.one * currentScale;


        if(previewModel != null)
            previewModel.transform.localScale = s;


        if(selectedModel != null)
            selectedModel.transform.localScale = s;
    }
    public void RotateLeft()
    {
        if(!rotateMode) return;
        currentRotationY -= 45;
    }

    public void RotateRight()
    {
        if(!rotateMode) return;
        currentRotationY += 45;
    }
    void Update()
    {
        if(selectedModel != null)
        {
            selectedModel.transform.rotation =
                Quaternion.Euler(
                    0,
                    currentRotationY,
                    0);
        }
        if(previewModel != null)
        {
            previewModel.transform.position = indicator.transform.position;
            previewModel.transform.rotation =
            Quaternion.Euler(0,
                            currentRotationY,
                            0);
        }
    }
    public void SelectPrefab(GameObject prefab)
    {
        currentRotationY = 0;
        currentScale = 1;
        selectedPrefab = prefab;
        OnModelSelected?.Invoke(prefab);
        CreatePreview();
        Debug.Log("Selected: " + prefab.name);
    }
    private void CreatePreview()
    {
        if(previewModel != null)
            Destroy(previewModel);

        if(selectedPrefab == null)
            return;

        previewModel = Instantiate(selectedPrefab);

        previewModel.transform.position = indicator.transform.position;
        previewModel.transform.rotation = Quaternion.identity;

        DestroyPhysics(previewModel);

        SetTransparent(previewModel);
    }
    private void PlaceOnFloor(GameObject obj)
    {
        Renderer[] renders = obj.GetComponentsInChildren<Renderer>();

        if (renders.Length == 0)
            return;

        Bounds bounds = renders[0].bounds;

        foreach (Renderer r in renders)
            bounds.Encapsulate(r.bounds);

        float bottom = bounds.min.y;

        float offset = obj.transform.position.y - bottom;

        obj.transform.position += Vector3.up * offset;
    }
    private void DestroyPhysics(GameObject obj)
    {
        foreach(Collider c in obj.GetComponentsInChildren<Collider>())
            Destroy(c);

        foreach(Rigidbody rb in obj.GetComponentsInChildren<Rigidbody>())
            Destroy(rb);
    }
    private void SetTransparent(GameObject obj)
    {
        foreach(Renderer r in obj.GetComponentsInChildren<Renderer>())
        {
            foreach(Material m in r.materials)
            {
                Color c = m.color;

                c.a = 0.5f;

                m.color = c;
            }
        }
    }
    // public void SelectModel(int index)
    // {
    //     if(index < 0 || index >= models.Count)
    //         return;

    //     selectedPrefab = models[index].prefab;

    //     Debug.Log("Selected : " + models[index].modelName);
    // }
    public void SelectPlacedModel(GameObject obj)
    {
        selectedModel = obj;


        // lấy rotation hiện tại
        currentRotationY =
        selectedModel.transform.eulerAngles.y;


        // lấy scale hiện tại
        currentScale =
        selectedModel.transform.localScale.x;


        Debug.Log(
            "Selected model: "
            + selectedModel.name
        );
    }
    public void SpawnModel()
    {
        Ray ray = cam.ScreenPointToRay(
            new Vector3(Screen.width / 2f,
                        Screen.height / 2f));

        if (Physics.Raycast(ray,
            out RaycastHit hit,
            100f,
            floorLayer))
        {

            if(selectedPrefab == null)
            {
                Debug.Log("Chưa chọn model");
                return;
            }


            GameObject newModel =
            Instantiate(
                selectedPrefab,
                previewModel.transform.position,
                previewModel.transform.rotation
            );
            Collider col =
            newModel.GetComponentInChildren<Collider>();

            if(col == null)
            {
                MeshCollider mesh =
                newModel.AddComponent<MeshCollider>();

                mesh.convex = true;
            }

            newModel.transform.localScale =
                Vector3.one * currentScale;


            PlaceOnFloor(newModel);


            Rigidbody rb = newModel.AddComponent<Rigidbody>();
            if(rb == null)
            {
                rb = newModel.AddComponent<Rigidbody>();
            }
            rb.useGravity = false;


            Interactable interact =
            newModel.AddComponent<Interactable>();
            if(interact == null)
            {
                interact = newModel.AddComponent<Interactable>();
            }

            interact.objectName = newModel.name;


            TransformState state =
            newModel.AddComponent<TransformState>();

            state.SaveTransform();


            spawnedModels.Add(newModel);


            Destroy(previewModel);
            previewModel = null;


            selectedModel = newModel;
            currentRotationY = 
            newModel.transform.eulerAngles.y;

            currentScale = 
            newModel.transform.localScale.x;

            Debug.Log(
                "Spawned: " 
                + newModel.name
                + " Total: "
                + spawnedModels.Count
            );
        }
    }
    public void DeleteModel()
    {
        if(selectedModel == null)
        {
            Debug.Log("Chưa chọn model");
            return;
        }


        if(spawnedModels.Contains(selectedModel))
        {
            spawnedModels.Remove(selectedModel);
        }


        Destroy(selectedModel);


        selectedModel = null;


        Debug.Log("Deleted");
    }
}