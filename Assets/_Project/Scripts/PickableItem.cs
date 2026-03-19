using UnityEngine;
using Unity.Netcode;
using WeBussedUp.Core.Data;
using WeBussedUp.Player;
using WeBussedUp.Interfaces;

namespace WeBussedUp.Gameplay.Items
{
    /// <summary>
    /// Yerden alınabilen, taşınabilen ve bırakılabilen her eşyanın temel bileşeni.
    /// Taşıma pozisyonlaması PlayerCarrySystem'e aittir — bu script sadece
    /// network state ve fizik yönetir.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
    public class PickableItem : NetworkBehaviour, IInteractable, ICarriable
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Eşya Verisi")]
        [SerializeField] private ItemData _itemData;

        [Tooltip("ItemData atanmadıysa fallback isim")]
        [SerializeField] private string _fallbackName = "Eşya";

        // ─── Network State ───────────────────────────────────────
        /// <summary>ulong.MaxValue = kimse taşımıyor</summary>
        public NetworkVariable<ulong> CarrierID = new NetworkVariable<ulong>(
            ulong.MaxValue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private Rigidbody _rb;
        private Collider  _col;

        // ─── Public API ──────────────────────────────────────────
        public bool     IsCarried  => CarrierID.Value != ulong.MaxValue;
        public ItemData ItemData   => _itemData;
        public string   DisplayName => _itemData != null ? _itemData.itemName : _fallbackName;

        public bool CanInteract(ulong playerId) => !IsCarried;
        public InteractionType GetInteractionType() => InteractionType.PickUp;

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            _rb  = GetComponent<Rigidbody>();
            _col = GetComponent<Collider>();
        }

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            CarrierID.OnValueChanged += OnCarrierChanged;
            ApplyPhysicsState(CarrierID.Value);
        }

        public override void OnNetworkDespawn()
        {
            CarrierID.OnValueChanged -= OnCarrierChanged;
        }

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            if (IsCarried) return string.Empty;
            return $"{DisplayName} Al [E]";
        }

        public void Interact(ulong playerId)
        {
            if (!IsCarried) RequestPickUpServerRpc(playerId);
        }

        // ─── Network RPC ─────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void RequestPickUpServerRpc(ulong playerId)
        {
            // Race condition: biri daha önce kapmış olabilir
            if (IsCarried) return;

            NetworkObject playerNetObj = NetworkManager.Singleton
                .SpawnManager.GetPlayerNetworkObject(playerId);

            if (playerNetObj == null) return;

            CarrierID.Value = playerId;
            NetworkObject.ChangeOwnership(playerId);

            // Parent ata — pozisyonlamayı PlayerCarrySystem üstlenir
            NetworkObject.TrySetParent(playerNetObj, worldPositionStays: false);

            NotifyCarrySystemClientRpc(playerId);
        }

        [Rpc(SendTo.Server)]
        public void RequestDropServerRpc(Vector3 dropPosition)
        {
            if (!IsCarried) return;

            CarrierID.Value = ulong.MaxValue;
            NetworkObject.TrySetParent((NetworkObject)null, worldPositionStays: true);
            NetworkObject.RemoveOwnership();

            DropClientRpc(dropPosition);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyCarrySystemClientRpc(ulong playerId)
        {
            // Sadece taşıyan oyuncunun CarrySystem'i kendini set eder
            if (NetworkManager.Singleton.LocalClientId != playerId) return;

            PlayerCarrySystem carrySystem = NetworkManager.Singleton
                .SpawnManager.GetPlayerNetworkObject(playerId)
                ?.GetComponent<PlayerCarrySystem>();

            carrySystem?.SetCarriedItem(this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DropClientRpc(Vector3 dropPosition)
        {
            transform.position = dropPosition;
        }

        // ─── Fizik ───────────────────────────────────────────────
        private void OnCarrierChanged(ulong oldId, ulong newId)
        {
            ApplyPhysicsState(newId);
        }

        private void ApplyPhysicsState(ulong currentCarrierId)
        {
            bool carried = currentCarrierId != ulong.MaxValue;

            _rb.isKinematic = carried;
            _col.enabled    = !carried;
        }
    }
}