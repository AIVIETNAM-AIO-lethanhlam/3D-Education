using UnityEngine;
using System.Collections.Generic;

public class ModelDatabase : MonoBehaviour
{
    public List<ModelData> models = new();

    void Awake()
    {
        GameObject[] prefabs =
            Resources.LoadAll<GameObject>("Models");

        foreach(GameObject p in prefabs)
        {
            models.Add(new ModelData()
            {
                modelName = ModelNameFormatter.Format(p.name),
                prefab = p
            });
        }

        Debug.Log(models.Count + " models loaded");
    }
}