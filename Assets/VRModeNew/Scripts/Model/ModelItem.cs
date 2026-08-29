using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ModelItemUI : MonoBehaviour
{
    public TMP_Text modelName;

    public Button selectButton;
    public TMP_Text buttonText;
    

    private GameObject prefab;
    
    public string ModelName
    {
        get
        {
            return modelName.text;
        }
    }
    public void Setup(
    ModelData model,
    ModelSpawner spawner)
    {
        prefab = model.prefab;

        modelName.text = model.modelName;
    

        SetSelected(false);

        selectButton.onClick.RemoveAllListeners();

        selectButton.onClick.AddListener(() =>
        {
            spawner.SelectPrefab(prefab);
        });

        spawner.OnModelSelected += UpdateState;
    }
    private void UpdateState(GameObject selected)
    {
        SetSelected(selected == prefab);
    }
    private void SetSelected(bool selected)
    {
        Image img = selectButton.GetComponent<Image>();

        if(selected)
        {
            buttonText.text = "Selected";

            img.color = new Color(
                0.25f,
                0.75f,
                0.35f);
        }
        else
        {
            buttonText.text = "Select";

            img.color = Color.white;
        }
    }
}