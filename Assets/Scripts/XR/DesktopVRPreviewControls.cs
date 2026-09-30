#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR;

/// <summary>
/// Desktop preview of VRClassroomScene without a headset (Unity Editor / PC build only).
/// Active only when no XR device is running, so it never interferes with Quest Link.
///
/// Controls:
///   W A S D ........... walk (uses the Player's CharacterController)
///   Arrow keys ........ look around
///   Middle mouse drag . look around
/// Model interaction stays in VRModelInteractionController (left drag = move,
/// right drag = rotate, wheel = scale, R = reset).
/// Movement and look pause automatically while the scene disables the player
/// (startup, part-detail panel) and while a text field has focus.
/// </summary>
public class DesktopVRPreviewControls : MonoBehaviour
{
    private const string VrSceneName = "VRClassroomScene";

    [SerializeField] private float moveSpeed = 1.6f;
    [SerializeField] private float keyLookSpeed = 70f;
    [SerializeField] private float mouseLookSpeed = 2.2f;
    [SerializeField] private float minPitch = -60f;
    [SerializeField] private float maxPitch = 60f;

    private CharacterController characterController;
    private Behaviour playerController;
    private Behaviour cameraLook;
    private Transform playerBody;
    private Transform cameraPivot;
    private UIDocument[] documents;
    private float pitch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != VrSceneName || FindAnyObjectByType<DesktopVRPreviewControls>() != null)
            return;

        GameObject host = new GameObject("[DesktopVRPreviewControls]");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<DesktopVRPreviewControls>();
    }

    private void Start()
    {
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == null)
                continue;

            string typeName = behaviour.GetType().Name;
            if (typeName == "PlayerController" && playerController == null)
            {
                playerController = behaviour;
                characterController = behaviour.GetComponent<CharacterController>();
            }
            else if (typeName == "CameraLook" && cameraLook == null)
            {
                cameraLook = behaviour;
                var look = behaviour as CameraLook;
                if (look != null)
                {
                    playerBody = look.playerBody;
                    cameraPivot = look.cameraPivot;
                }
            }
        }

        if (playerBody == null && characterController != null)
            playerBody = characterController.transform;
        if (cameraPivot == null && Camera.main != null)
            cameraPivot = Camera.main.transform.parent != null ? Camera.main.transform.parent : Camera.main.transform;

        if (cameraPivot != null)
        {
            pitch = cameraPivot.localEulerAngles.x;
            if (pitch > 180f) pitch -= 360f;
        }

        documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log("[DesktopVRPreviewControls] Desktop VR preview: WASD walk, arrow keys / middle mouse look.");
    }

    private void Update()
    {
        if (XRSettings.isDeviceActive || IsTyping())
            return;

        bool lookAllowed = cameraLook == null || cameraLook.enabled;
        bool moveAllowed = playerController == null || playerController.enabled;

        if (lookAllowed)
            Look();

        if (moveAllowed)
            Walk();
    }

    private void Look()
    {
        float yaw = 0f;
        float pitchDelta = 0f;

        if (Input.GetKey(KeyCode.LeftArrow)) yaw -= keyLookSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.RightArrow)) yaw += keyLookSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.UpArrow)) pitchDelta -= keyLookSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.DownArrow)) pitchDelta += keyLookSpeed * Time.deltaTime;

        if (Input.GetMouseButton(2))
        {
            yaw += Input.GetAxis("Mouse X") * mouseLookSpeed;
            pitchDelta -= Input.GetAxis("Mouse Y") * mouseLookSpeed;
        }

        if (playerBody != null && Mathf.Abs(yaw) > 0f)
            playerBody.Rotate(Vector3.up * yaw, Space.World);

        if (cameraPivot != null && Mathf.Abs(pitchDelta) > 0f)
        {
            pitch = Mathf.Clamp(pitch + pitchDelta, minPitch, maxPitch);
            Vector3 euler = cameraPivot.localEulerAngles;
            cameraPivot.localEulerAngles = new Vector3(pitch, euler.y, euler.z);
        }
    }

    private void Walk()
    {
        if (characterController == null || !characterController.enabled || Camera.main == null)
            return;

        float h = 0f, v = 0f;
        if (Input.GetKey(KeyCode.A)) h -= 1f;
        if (Input.GetKey(KeyCode.D)) h += 1f;
        if (Input.GetKey(KeyCode.W)) v += 1f;
        if (Input.GetKey(KeyCode.S)) v -= 1f;
        if (h == 0f && v == 0f)
            return;

        Vector3 forward = Camera.main.transform.forward; forward.y = 0f; forward.Normalize();
        Vector3 right = Camera.main.transform.right; right.y = 0f; right.Normalize();
        Vector3 move = Vector3.ClampMagnitude(right * h + forward * v, 1f);
        characterController.Move(move * moveSpeed * Time.deltaTime);
    }

    private bool IsTyping()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null &&
            (selected.GetComponent<UnityEngine.UI.InputField>() != null ||
             selected.GetComponent<TMPro.TMP_InputField>() != null))
            return true;

        if (documents != null)
        {
            foreach (UIDocument doc in documents)
            {
                if (doc == null || doc.rootVisualElement == null || doc.rootVisualElement.panel == null)
                    continue;
                if (doc.rootVisualElement.panel.focusController?.focusedElement is TextField)
                    return true;
            }
        }
        return false;
    }
}
#endif
