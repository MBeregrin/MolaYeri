using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float rotationSpeed = 15f; 

    [Header("References")]
    [SerializeField] private Transform cameraHolder; 
    [SerializeField] private GameObject playerVirtualCamera; 

    [Header("Animation")]
    [SerializeField] private Animator animator; 

    [Header("Interaction")]
    [SerializeField] private float interactDistance = 3f; 
    [SerializeField] private LayerMask interactLayer;     
    
    // NOT: handPoint'i artık PlayerCarrySystem scripti kullanacak ama referans kırılmasın diye burada tutabiliriz 
    // veya PlayerCarrySystem'in içindeki "Hold Point" kutusuna Unity'den bu handPoint objesini sürükleyebilirsin.
    public Transform handPoint; 

    // Bileşenler
    private CharacterController characterController;
    private PlayerInputs input; 
    private PlayerCarrySystem carrySystem; // YENİ: Mıknatıs taşıma sistemimiz

    // Değişkenler
    private Vector2 moveInput;
    private Vector2 lookInput;
    private float cameraPitch = 0f; 
    private Vector3 velocity;
    private float gravity = -9.81f;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        carrySystem = GetComponent<PlayerCarrySystem>(); // Taşıma sistemini bul
        input = new PlayerInputs(); 
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            if (playerVirtualCamera != null) playerVirtualCamera.SetActive(false);
            enabled = false;
            return;
        }
        
        if (playerVirtualCamera != null) playerVirtualCamera.SetActive(true);
        
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        var renderers = GetComponentsInChildren<SkinnedMeshRenderer>();
        int invisibleLayer = LayerMask.NameToLayer("InvisibleToOwner");

        if (invisibleLayer != -1) 
        {
            foreach (var renderer in renderers) renderer.gameObject.layer = invisibleLayer;
        }
        else
        {
            Debug.LogWarning("UYARI: 'InvisibleToOwner' adında bir Layer bulamadım! Unity'den eklemeyi unutma.");
        }
    }

    private void OnEnable() => input.Enable();
    private void OnDisable() => input.Disable();

    private void Update()
    {
        if (!IsOwner) return;

        HandleInput();
        HandleMovement();
        HandleRotation();
        CheckInteraction();
    }

    private void HandleInput()
    {
        moveInput = input.Player.Move.ReadValue<Vector2>();
        lookInput = input.Player.Look.ReadValue<Vector2>();
    }

    private void HandleMovement()
    {
        float targetSpeed = input.Player.Sprint.IsPressed() ? sprintSpeed : moveSpeed;
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        
        if (characterController.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; 
        }
        velocity.y += gravity * Time.deltaTime;

        Vector3 finalMove = (move * targetSpeed) + velocity;
        characterController.Move(finalMove * Time.deltaTime);

        if (animator != null)
        {
            Vector3 horizontalVelocity = new Vector3(characterController.velocity.x, 0, characterController.velocity.z);
            float actualSpeed = horizontalVelocity.magnitude;
            float animationSpeedPercent = 0f;
            
            if (actualSpeed > 0.05f) animationSpeedPercent = 0.5f; 
            if (actualSpeed > 5.5f) animationSpeedPercent = 1f;   

            animator.SetFloat("Speed", animationSpeedPercent, 0.1f, Time.deltaTime);
        }
    }

    private void HandleRotation()
    {
        float mouseSensitivityMultiplier = 0.1f; 

        float mouseX = lookInput.x * rotationSpeed * mouseSensitivityMultiplier;
        transform.Rotate(Vector3.up * mouseX);

        float mouseY = lookInput.y * rotationSpeed * mouseSensitivityMultiplier;
        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -85f, 85f);

        if (cameraHolder != null)
        {
            cameraHolder.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }
    }

    private void CheckInteraction()
    {
        if (input.Player.Interact.WasPressedThisFrame())
        {
            // --- YENİ MANTIK ---
            // Eğer elimizde halihazırda bir eşya Varsa (Mıknatıs sistemi taşıyorsa), lazer atma!
            // Çünkü bırakma (Drop) işlemini PlayerCarrySystem halledecek.
            if (carrySystem != null && carrySystem.IsHoldingItem()) return; 

            // Elimiz boşsa, E'ye basınca ileriye lazer (Raycast) at.
            Ray ray = new Ray(cameraHolder.position, cameraHolder.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, interactDistance, interactLayer))
            {
                if (hit.collider.TryGetComponent(out IInteractable interactable))
                {
                    interactable.Interact(NetworkManager.Singleton.LocalClientId);
                }
            }
        }
    }

    public void SetCarrying(bool state)
    {
        if (animator != null) animator.SetBool("IsCarrying", state);
    }
}