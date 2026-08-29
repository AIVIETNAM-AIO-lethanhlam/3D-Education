using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class PlayerInteraction : MonoBehaviour
{
    public Button resetTransformButton;
    [Header("References")]
    public Camera playerCamera;

    [Header("Interaction")]
    public float interactDistance = 3f;

    public LayerMask interactLayer;

    private Interactable currentTarget;
    [Header("UI")]
    public Button interactButton;
    public Button releaseButton;
    public TMP_Text objectNameText;
    public CrosshairUI crosshair;
    public HoldSystem holdSystem;
    private void Start()
    {
        interactButton.gameObject.SetActive(false);

        releaseButton.gameObject.SetActive(false);
        resetTransformButton.gameObject.SetActive(false);

        interactButton.onClick.AddListener(OnInteractPressed);

        releaseButton.onClick.AddListener(OnReleasePressed);
        resetTransformButton.onClick.AddListener(ResetTransform);
    }
    private void OnReleasePressed()
    {
        holdSystem.Release();

        releaseButton.gameObject.SetActive(false);
    }
    private void OnInteractPressed()
    {
        if (currentTarget == null)
            return;

        if (currentTarget.canHold)
        {
            holdSystem.Hold(currentTarget.transform);

            releaseButton.gameObject.SetActive(true);

            interactButton.gameObject.SetActive(false);
        }
        else
        {
            currentTarget.Interact();
        }
    }
    private void Update()
    {
        DetectObject();

        if (currentTarget == null)
            return;

        if (Input.GetKeyDown(KeyCode.E))
        {
            currentTarget.Interact();
        }
        if(Input.GetKeyDown(KeyCode.R))
        {
            holdSystem.Release();
        }
    }

    //------------------------------------------------

    private void DetectObject()
    {
        if (holdSystem != null && holdSystem.IsHolding)
        {
            interactButton.gameObject.SetActive(false);
            resetTransformButton.gameObject.SetActive(true);
            return;
        }
        currentTarget = null;

        interactButton.gameObject.SetActive(false);
        resetTransformButton.gameObject.SetActive(false);
        if (crosshair != null)
            crosshair.SetTarget(false);
        Ray ray = new Ray(
            playerCamera.transform.position,
            playerCamera.transform.forward);

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            interactDistance,
            interactLayer))
        {
             Debug.Log("Hit : " + hit.collider.name);
            currentTarget =
            hit.collider.GetComponentInParent<Interactable>();

            if (currentTarget != null)
            {
                Debug.Log(currentTarget.objectName);
                interactButton.gameObject.SetActive(true);
                resetTransformButton.gameObject.SetActive(true);
                objectNameText.text = currentTarget.objectName;

                if (crosshair != null)
                    crosshair.SetTarget(true);
            }
        }
    }
    private void ResetTransform()
    {
        if(currentTarget == null)
            return;

        // PHẢI RELEASE TRƯỚC
        if (holdSystem != null && holdSystem.IsHolding)
        {
            holdSystem.Release();
        }

        // SAU ĐÓ MỚI RESET LẠI VỊ TRÍ
        TransformState state = currentTarget.GetComponent<TransformState>();
        if(state != null)
        {
            state.ResetTransform();
        }
    }
    //------------------------------------------------

    private void OnDrawGizmos()
    {
        if (playerCamera == null)
            return;

        Gizmos.color = Color.green;

        Gizmos.DrawRay(
            playerCamera.transform.position,
            playerCamera.transform.forward *
            interactDistance);
    }
}