using UnityEngine;
using Unity.Netcode;
using WeBussedUp.Core.Data;
using WeBussedUp.Interfaces;
using WeBussedUp.Player;

namespace WeBussedUp.Gameplay.Items
{
    /// <summary>
    /// Raf doldurmak için taşınan ambalaj.
    /// ProductCategory'ye göre görsel seçilir:
    /// Food → karton kutu, Drink → kasa, diğerleri → genel kutu.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
    public class BoxItem : NetworkBehaviour, IInteractable, ICarriable
    {
        public bool IsCarried => !IsEmpty;
        // ─── Inspector ───────────────────────────────────────────
        [Header("İçerik")]
        [SerializeField] private ProductData _boxContent;

        [Header("Ambalaj Görselleri")]
        [Tooltip("Food kategorisi için karton kutu görsel prefabı")]
        [SerializeField] private GameObject _cardboardBoxVisual;

        [Tooltip("Drink kategorisi için kasa görsel prefabı")]
        [SerializeField] private GameObject _crateVisual;

        [Tooltip("Diğer kategoriler için genel kutu görsel prefabı")]
        [SerializeField] private GameObject _genericBoxVisual;

        [Header("Etiket")]
        [Tooltip("Kutunun üzerindeki ürün ikonu için Renderer")]
        [SerializeField] private Renderer _labelRenderer;
        [SerializeField] private string   _labelTextureProp = "_BaseMap";

        // ─── Network State ───────────────────────────────────────
        private NetworkVariable<int> _itemsInside = new NetworkVariable<int>(
            10,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private Rigidbody _rb;
        private Collider  _col;

        // ─── Public API ──────────────────────────────────────────
        public ProductData BoxContent  => _boxContent;
        public int         ItemsInside => _itemsInside.Value;
        public bool        IsEmpty     => _itemsInside.Value <= 0;

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            _rb  = GetComponent<Rigidbody>();
            _col = GetComponent<Collider>();
        }

        public override void OnNetworkSpawn()
        {
            _itemsInside.OnValueChanged += OnItemsChanged;
            ApplyVisuals();
        }

        public override void OnNetworkDespawn()
        {
            _itemsInside.OnValueChanged -= OnItemsChanged;
        }

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            if (IsEmpty) return "Boş Koli [E]";
            string name = _boxContent != null ? _boxContent.productName : "Ürün";
            return $"{name} Kolisi Al ({_itemsInside.Value} adet) [E]";
        }

        public bool CanInteract(ulong playerId) => !IsEmpty;

        public InteractionType GetInteractionType() => InteractionType.PickUp;

        public void Interact(ulong playerId)
        {
            if (IsSpawned) RequestPickupServerRpc(playerId);
        }

        // ─── Raf Doldurma ────────────────────────────────────────
        public void ConsumeItem()
        {
            if (!IsServer) return;
            if (_itemsInside.Value > 0)
                _itemsInside.Value--;
        }

        // ─── Network RPC ─────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void RequestPickupServerRpc(ulong playerId)
        {
            NetworkObject playerNetObj = NetworkManager.Singleton
                .SpawnManager.GetPlayerNetworkObject(playerId);

            if (playerNetObj == null) return;

            bool parentOk = NetworkObject.TrySetParent(playerNetObj, worldPositionStays: false);
            if (!parentOk) return;

            NetworkObject.ChangeOwnership(playerId);
            SetPhysicsClientRpc(enablePhysics: false);
            NotifyCarrySystemClientRpc(playerId);
        }

        [Rpc(SendTo.Server)]
        public void RequestDropServerRpc(Vector3 worldPosition)
        {
            NetworkObject.TrySetParent((NetworkObject)null, worldPositionStays: true);
            NetworkObject.RemoveOwnership();
            DropClientRpc(worldPosition);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetPhysicsClientRpc(bool enablePhysics)
        {
            if (_rb  != null) _rb.isKinematic  = !enablePhysics;
            if (_col != null) _col.enabled     =  enablePhysics;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyCarrySystemClientRpc(ulong playerId)
{
    if (NetworkManager.Singleton.LocalClientId != playerId) return;

    PlayerCarrySystem carrySystem = NetworkManager.Singleton
        .SpawnManager.GetPlayerNetworkObject(playerId)
        ?.GetComponent<PlayerCarrySystem>();

    carrySystem?.SetCarriedItem(this); // artık ICarriable kabul ediyor
}

        [Rpc(SendTo.ClientsAndHost)]
        private void DropClientRpc(Vector3 dropPosition)
        {
            transform.position = dropPosition;
            SetPhysicsClientRpc(enablePhysics: true);
        }

        // ─── Görsel ──────────────────────────────────────────────
        private void ApplyVisuals()
        {
            if (_boxContent == null) return;

            // Tüm görselleri kapat
            _cardboardBoxVisual?.SetActive(false);
            _crateVisual?.SetActive(false);
            _genericBoxVisual?.SetActive(false);

            // Kategoriye göre doğru görseli aç
            GameObject activeVisual = _boxContent.productCategory switch
            {
                ProductCategory.Food    => _cardboardBoxVisual,
                ProductCategory.Snack   => _cardboardBoxVisual,
                ProductCategory.Drink   => _crateVisual,
                _                       => _genericBoxVisual
            };

            activeVisual?.SetActive(true);

            // Ürün ikonunu etikete uygula
            ApplyLabel(activeVisual);
        }

        private void ApplyLabel(GameObject visual)
        {
            if (visual == null || _boxContent?.icon == null) return;

            // Aktif görselin içindeki label renderer'ı bul
            Renderer labelRend = visual.GetComponentInChildren<Renderer>();
            if (labelRend == null && _labelRenderer != null)
                labelRend = _labelRenderer;

            if (labelRend == null) return;

            if (labelRend.material.HasProperty(_labelTextureProp))
                labelRend.material.SetTexture(
                    _labelTextureProp,
                    _boxContent.icon.texture);
        }

        private void OnItemsChanged(int oldVal, int newVal)
        {
            // Kutu boşaldıysa görsel değişimi
            if (newVal <= 0)
            {
                // Boş kutu efekti — hafif soluklaş
                foreach (var rend in GetComponentsInChildren<Renderer>())
                {
                    Color c = rend.material.color;
                    c.a = 0.5f;
                    rend.material.color = c;
                }
            }
        }
    }
}
