using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
    public Image image;

    public Color normalColor = Color.white;
    public Color targetColor = Color.yellow;

    public void SetTarget(bool target)
    {
        image.color = target
            ? targetColor
            : normalColor;
    }
}