using UnityEngine;

public class Interactable : MonoBehaviour
{
    public string objectName;

    public bool canHold = true;

    public virtual void Interact()
    {
        Debug.Log(objectName);
    }
}