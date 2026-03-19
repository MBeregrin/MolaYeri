using UnityEngine;
using Unity.Netcode;
using WeBussedUp.Gameplay.Building;
using WeBussedUp.Interfaces;

namespace WeBussedUp.Player
{
    public class PlayerCarrySystem : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Taşıma Noktası")]
        [SerializeField] private Transform _holdPoint;

        [Header("Ayarlar")]
        [SerializeField] private float _smoothSpeed  = 15f;
        [SerializeField] private float _dropDistance = 1.5f;
        [SerializeField] private float _throwForce   = 8f;

        [Header("Rotate Ayarları")]
        [SerializeField] private float _rotateStep = 90f;

        // ─── Runtime ─────────────────────────────────────────────
        private ICarriable   _heldItem;
        private PlayerInputs _input;
        private float        _heldItemYRotation = 0f;

        // ─── Cached cast'ler (her frame cast yapmamak için) ──────
        private Transform      _heldTransform;
        private NetworkObject  _heldNetObj;

        // ─── Public API ──────────────────────────────────────────
        public void SetCarriedItem(ICarriable item)
        {
            _heldItem          = item;
            _heldItemYRotation = 0f;

            // Cast'leri cache'le
            MonoBehaviour mb = item as MonoBehaviour;
            _heldTransform = mb != null ? mb.transform : null;
            _heldNetObj    = mb != null ? mb.GetComponent<NetworkObject>() : null;
        }

        public void ClearCarriedItem()
        {
            _heldItem      = null;
            _heldTransform = null;
            _heldNetObj    = null;
        }

        public bool IsHoldingItem => _heldItem != null;

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            _input = new PlayerInputs();
        }

        private void OnEnable()  => _input.Enable();
        private void OnDisable() => _input.Disable();

        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (!IsOwner) return;
            if (IsAnyModeActive()) return;

            HandleDropInput();
            HandleThrowInput();
            HandleRotateInput();
            UpdateHeldItemPosition();
            ValidateHeldItem();
        }

        // ─── Input ───────────────────────────────────────────────
        private void HandleDropInput()
        {
            if (!_input.Player.Drop.WasPressedThisFrame()) return;
            if (_heldItem == null) return;

            _heldItem.RequestDropServerRpc(CalculateDropPosition());
            ClearCarriedItem();
        }

        private void HandleThrowInput()
        {
            if (!_input.Player.Throw.WasPressedThisFrame()) return;
            if (_heldItem == null || _heldNetObj == null)   return;

            Vector3 throwDir = Camera.main != null
                ? Camera.main.transform.forward
                : transform.forward;

            _heldItem.RequestDropServerRpc(CalculateDropPosition());
            ThrowItemServerRpc(_heldNetObj, throwDir * _throwForce);

            ClearCarriedItem();
        }

        private void HandleRotateInput()
        {
            if (!_input.Player.Rotate.WasPressedThisFrame()) return;
            if (_heldItem == null) return;

            _heldItemYRotation += _rotateStep;
            if (_heldItemYRotation >= 360f) _heldItemYRotation = 0f;
        }

        // ─── Pozisyon Güncelleme ─────────────────────────────────
        private void UpdateHeldItemPosition()
        {
            if (_heldItem == null || _heldTransform == null) return;

            if (_holdPoint == null)
            {
                Debug.LogWarning("[CarrySystem] HoldPoint atanmamış!", this);
                return;
            }

            _heldTransform.localPosition = Vector3.Lerp(
                _heldTransform.localPosition,
                _holdPoint.localPosition,
                Time.deltaTime * _smoothSpeed
            );

            Quaternion targetRot = _holdPoint.localRotation *
                                   Quaternion.Euler(0f, _heldItemYRotation, 0f);

            _heldTransform.localRotation = Quaternion.Lerp(
                _heldTransform.localRotation,
                targetRot,
                Time.deltaTime * _smoothSpeed
            );
        }

        // ─── Validasyon ──────────────────────────────────────────
        private void ValidateHeldItem()
        {
            if (_heldItem == null) return;
            if (!_heldItem.IsCarried) ClearCarriedItem();
        }

        // ─── Mod Kontrolü ────────────────────────────────────────
        private bool IsAnyModeActive()
        {
            if (TryGetComponent(out BuildingSystem buildSys) &&
                buildSys.IsBuildMode) return true;

            if (TryGetComponent(out CleaningTool cleanTool) &&
                cleanTool.IsCleanMode) return true;

            return false;
        }

        // ─── Util ────────────────────────────────────────────────
        private Vector3 CalculateDropPosition()
        {
            if (_holdPoint != null)
                return _holdPoint.position + transform.forward * _dropDistance;
            return transform.position + transform.forward * _dropDistance;
        }

        // ─── Network ─────────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void ThrowItemServerRpc(NetworkObjectReference itemRef, Vector3 force)
        {
            if (!itemRef.TryGet(out NetworkObject netObj)) return;
            if (!netObj.TryGetComponent(out Rigidbody rb))  return;

            rb.isKinematic = false;
            rb.AddForce(force, ForceMode.Impulse);
        }
    }
}