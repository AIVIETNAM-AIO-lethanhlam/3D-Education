using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ModelListUI : MonoBehaviour
{
    public ModelDatabase database;

    public ModelSpawner spawner;

    public Transform content;

    public GameObject itemPrefab;

    public TMP_InputField searchInput;

    private List<ModelItemUI> items =
        new List<ModelItemUI>();

    void Start()
    {
        foreach (ModelData model in database.models)
        {
            GameObject item =
                Instantiate(itemPrefab, content);

            ModelItemUI ui =
                item.GetComponent<ModelItemUI>();

            ui.Setup(model, spawner);

            items.Add(ui);
        }

        searchInput.onValueChanged.AddListener(Filter);
    }

    void Filter(string keyword)
    {
        keyword = keyword.ToLower().Trim();

        foreach (ModelItemUI item in items)
        {
            bool show =
                item.ModelName
                .ToLower()
                .Contains(keyword);

            item.gameObject.SetActive(show);
        }
    }
    public void ClearSearch()
    {
        searchInput.text = "";
    }
}