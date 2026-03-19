using UnityEngine;
using Unity.Netcode;
using WeBussedUp.Interfaces;
using WeBussedUp.Player;
using WeBussedUp.UI;
using WeBussedUp.Network;

namespace WeBussedUp.Player
{
    /// <summary>
    /// FPS oyuncu hareketi, kamera rotasyonu ve etkileşim raycasti.
    /// Taşıma: PlayerCarrySystem
    /// Input: PlayerInputs (Input System)
    /// Interaction prompt: CheckHover() → UI sistemi OnInteractableChanged event'ini dinler
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Hareket")]
        [SerializeField] private float _moveSpeed    = 5f;
        [SerializeField] private float _sprintSpeed  = 8f;

        [Header("Kamera")]
        [SerializeField] private Transform  _cameraHolder;
        [SerializeField] private GameObject _playerVirtualCamera;
        [SerializeField] private float      _rotationSpeed        = 15f;
        [SerializeField] private float      _mouseSensitivity     = 0.1f;
        [SerializeField] private float      _maxPitchAngle        = 85f;

        [Header("Etkileşim")]
        [SerializeField] private float     _interactDistance = 3f;
        [SerializeField] private LayerMask _interactLayer;

        [Header("El Noktası")]
        [SerializeField] private Transform _handPoint;

        [Header("Animasyon")]
        [SerializeField] private Animator _animator;

        // ─── Runtime ─────────────────────────────────────────────
        private CharacterController _cc;
        private PlayerInputs        _input;
        private PlayerCarrySystem   _carrySystem;

        private Vector2 _moveInput;
        private Vector2 _lookInput;
        private float   _cameraPitch;
        private Vector3 _verticalVelocity;

        private IInteractable _currentInteractable; // Şu an bakılan obje
        private IInteractable _lastInteractable;    // Prompt değişimini algılamak için

        // ─── Events (UI bağlantısı için) ─────────────────────────
        public event System.Action<IInteractable> OnInteractableChanged;

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            _cc          = GetComponent<CharacterController>();
            _carrySystem = GetComponent<PlayerCarrySystem>();
            _input       = new PlayerInputs();
        }

        private void OnEnable()  => _input.Enable();
        private void OnDisable() => _input.Disable();

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
{
    if (!IsOwner)
    {
        _playerVirtualCamera?.SetActive(false);
        enabled = false;
        return;
    }

    _playerVirtualCamera?.SetActive(true);

    Cursor.lockState = CursorLockMode.Locked;
    Cursor.visible   = false;

    // FPS'de kendi modelini görmemek için layer
    int invisibleLayer = LayerMask.NameToLayer("InvisibleToOwner");
    if (invisibleLayer != -1)
    {
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>())
            r.gameObject.layer = invisibleLayer;
    }
    else
    {
        Debug.LogWarning("[PlayerController] 'InvisibleToOwner' layer bulunamadı!", this);
    }

    // UIManager'a kendini kaydet
    WeBussedUp.UI.UIManager.Instance?.RegisterLocalPlayer(this);

    // PlayerNetworkSync yoksa ekle
    if (!TryGetComponent(out WeBussedUp.Network.PlayerNetworkSync _))
        gameObject.AddComponent<WeBussedUp.Network.PlayerNetworkSync>();
}
public override void OnNetworkDespawn()
{
    UIManager.Instance?.UnregisterLocalPlayer();
}


        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (!IsOwner) return;

            ReadInput();
            HandleMovement();
            HandleRotation();
            CheckHover();
            CheckInteractInput();
        }

        // ─── Input ───────────────────────────────────────────────
        private void ReadInput()
        {
            _moveInput = _input.Player.Move.ReadValue<Vector2>();
            _lookInput = _input.Player.Look.ReadValue<Vector2>();
        }

        // ─── Hareket ─────────────────────────────────────────────
        private void HandleMovement()
        {
            float speed = _input.Player.Sprint.IsPressed() ? _sprintSpeed : _moveSpeed;
            Vector3 move = transform.right   * _moveInput.x
                         + transform.forward * _moveInput.y;

            // Gravity — Physics.gravity kullan, hardcode değil
            if (_cc.isGrounded && _verticalVelocity.y < 0f)
                _verticalVelocity.y = -2f;

            _verticalVelocity.y += Physics.gravity.y * Time.deltaTime;

            _cc.Move((move * speed + _verticalVelocity) * Time.deltaTime);

            // Animasyon
            if (_animator != null)
            {
                float hSpeed = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude;
                float animVal = hSpeed > 5.5f ? 1f : hSpeed > 0.05f ? 0.5f : 0f;
                _animator.SetFloat("Speed", animVal, 0.1f, Time.deltaTime);
            }
        }

        // ─── Kamera Rotasyonu ─────────────────────────────────────
        private void HandleRotation()
        {
            float mouseX = _lookInput.x * _rotationSpeed * _mouseSensitivity;
            transform.Rotate(Vector3.up * mouseX);

            _cameraPitch -= _lookInput.y * _rotationSpeed * _mouseSensitivity;
            _cameraPitch  = Mathf.Clamp(_cameraPitch, -_maxPitchAngle, _maxPitchAngle);

            if (_cameraHolder != null)
                _cameraHolder.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
        }

        // ─── Etkileşim: Hover (Sürekli) ──────────────────────────
        /// <summary>
        /// Her frame bakılan objeyi kontrol eder.
        /// Değişim varsa OnInteractableChanged event'i tetiklenir → UI prompt güncellenir.
        /// </summary>
        private void CheckHover()
        {
            if (_cameraHolder == null) return;

            Ray ray = new Ray(_cameraHolder.position, _cameraHolder.forward);
            IInteractable found = null;

            if (Physics.Raycast(ray, out RaycastHit hit, _interactDistance, _interactLayer))
            {
                hit.collider.TryGetComponent(out found);

                // CanInteract false ise prompt gösterme ama objeyi kaydet (soluk gösterim için)
                if (found != null && !found.CanInteract(NetworkManager.Singleton.LocalClientId))
                    found = null;
            }

            _currentInteractable = found;

            // Değişim varsa UI'ya bildir
            if (_currentInteractable != _lastInteractable)
            {
                _lastInteractable = _currentInteractable;
                OnInteractableChanged?.Invoke(_currentInteractable);
            }
        }

        // ─── Etkileşim: E Tuşu ───────────────────────────────────
        private void CheckInteractInput()
        {
            if (!_input.Player.Interact.WasPressedThisFrame()) return;

            // Elimizde eşya varsa → drop PlayerCarrySystem'e bırakılmış, burada işlem yok
            if (_carrySystem != null && _carrySystem.IsHoldingItem) return;

            if (_currentInteractable == null) return;

            ulong localId = NetworkManager.Singleton.LocalClientId;

            if (_currentInteractable.CanInteract(localId))
                _currentInteractable.Interact(localId);
        }

        // ─── Public API ──────────────────────────────────────────
        public void SetCarrying(bool state)
        {
            _animator?.SetBool("IsCarrying", state);
        }

        public Transform HandPoint => _handPoint;
    
    
}}