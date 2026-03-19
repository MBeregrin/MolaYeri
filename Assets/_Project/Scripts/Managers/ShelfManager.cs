using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;
using System.Collections.Generic;
using WeBussedUp.Core.Data;
using WeBussedUp.Core;
using WeBussedUp.Interfaces;
using WeBussedUp.Player;
using WeBussedUp.Gameplay.Items;
using DG.Tweening;

namespace WeBussedUp.Stations.Market
{
    /// <summary>
    /// Tek bir raf tahtasının ürün dizilimini yönetir.
    /// Grid tabanlı slot sistemi — pivot offset, spacing ve rotation SO'dan okunur.
    /// Görsel sadece delta güncellemesiyle rebuild edilir (full clear yok).
    /// </summary>
    [System.Serializable]
    public struct ShelfPlank
    {
        [Tooltip("Raf tahtasının sol-ön köşesi (boş GameObject)")]
        public Transform startPoint;

        [Tooltip("Tahtanın genişliği (metre, X ekseni)")]
        public float width;

        [Tooltip("Tahtanın derinliği (metre, Z ekseni)")]
        public float depth;

        [Tooltip("X eksenini ters çevir (sağdan sola diz)")]
        public bool flipX;

        [Tooltip("Z eksenini ters çevir (arkadan öne diz)")]
        public bool flipZ;
    }

    /// <summary>
    /// Bir rafın tüm tahtalarını yöneten ana bileşen.
    /// BoxItem → Interact() → TryAddProductServerRpc() → slot hesabı → görsel spawn.
    /// </summary>
    public class ShelfManager : NetworkBehaviour, IInteractable
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Raf Tahtaları")]
        [SerializeField] private ShelfPlank[] _planks;

        [Header("Olaylar")]
        public UnityEvent OnShelfFilled;
        public UnityEvent OnShelfEmptied;

        // ─── Network State ───────────────────────────────────────
        // string ID — ProductData.productID ile tutarlı
        private NetworkVariable<string> _productID = new NetworkVariable<string>(
            string.Empty,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private NetworkVariable<int> _currentStock = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private NetworkVariable<int> _maxCapacity = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        // Slot bazlı liste — index = slot pozisyonu, null = boş slot
        private List<GameObject> _slotDummies = new List<GameObject>();
        private ProductData      _cachedProduct;

        // ─── Public API ──────────────────────────────────────────
        public bool   IsEmpty    => _currentStock.Value == 0;
        public bool   IsFull     => _maxCapacity.Value > 0 && _currentStock.Value >= _maxCapacity.Value;
        public int    Stock      => _currentStock.Value;
        public int    Capacity   => _maxCapacity.Value;
        public string ProductID  => _productID.Value;

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            _currentStock.OnValueChanged += OnStockChanged;
            _productID.OnValueChanged    += OnProductChanged;

            RebuildAllVisuals();
        }

        public override void OnNetworkDespawn()
        {
            _currentStock.OnValueChanged -= OnStockChanged;
            _productID.OnValueChanged    -= OnProductChanged;
        }

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            if (IsFull)    return "Raf Dolu";
            if (IsEmpty)   return "Rafa Ürün Koy [E]";
            return $"Ürün Ekle ({_currentStock.Value}/{_maxCapacity.Value}) [E]";
        }

        public bool CanInteract(ulong playerId)
        {
            if (IsFull) return false;

            // Oyuncunun elinde BoxItem var mı?
            NetworkObject playerNetObj = NetworkManager.Singleton
                .SpawnManager.GetPlayerNetworkObject(playerId);

            if (playerNetObj == null) return false;

            PlayerCarrySystem carry = playerNetObj.GetComponent<PlayerCarrySystem>();
            if (carry == null || !carry.IsHoldingItem) return false;

            // Elindeki BoxItem bu rafa uygun mu?
            BoxItem box = playerNetObj.GetComponentInChildren<BoxItem>();
            if (box == null || box.IsEmpty) return false;

            // Raf doluysa farklı ürün kabul etme
            if (!IsEmpty && box.BoxContent != null &&
                box.BoxContent.productID != _productID.Value) return false;

            return true;
        }

        public InteractionType GetInteractionType() => InteractionType.Deposit;

        public void Interact(ulong playerId)
        {
            if (!IsSpawned || !CanInteract(playerId)) return;

            NetworkObject playerNetObj = NetworkManager.Singleton
                .SpawnManager.GetPlayerNetworkObject(playerId);

            BoxItem box = playerNetObj?.GetComponentInChildren<BoxItem>();
            if (box == null || box.BoxContent == null) return;

            TryAddProductServerRpc(box.BoxContent.productID, playerId);
        }

        // ─── Server RPC ──────────────────────────────────────────
        [Rpc(SendTo.Server)]
        public void TryAddProductServerRpc(string productID, ulong playerId)
        {
            if (_planks == null || _planks.Length == 0) return;

            ProductData product = ItemDatabase.Instance?.GetProductByID(productID);
            if (product == null) return;

            // Raf boşsa initialize et
            if (IsEmpty)
            {
                _productID.Value    = productID;
                _maxCapacity.Value  = CalculateMaxCapacity(product);
                _cachedProduct      = product;
            }
            else if (_productID.Value != productID) return; // Farklı ürün reddedilir

            if (IsFull) return;

            _currentStock.Value++;

            // BoxItem'dan bir ürün tüket
            NetworkObject playerNetObj = NetworkManager.Singleton
                .SpawnManager.GetPlayerNetworkObject(playerId);
            playerNetObj?.GetComponentInChildren<BoxItem>()?.ConsumeItem();
        }

        [Rpc(SendTo.Server)]
        public void TakeProductServerRpc(ulong playerId)
        {
            if (IsEmpty) return;

            _currentStock.Value--;

            if (_currentStock.Value == 0)
            {
                _productID.Value   = string.Empty;
                _maxCapacity.Value = 0;
                _cachedProduct     = null;
                NotifyEmptiedClientRpc();
            }
        }

        // ─── Network Callbacks ────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyEmptiedClientRpc() => OnShelfEmptied?.Invoke();

        private void OnStockChanged(int oldStock, int newStock)
        {
            // Delta güncelleme — sadece değişen slot'u işle
            if (newStock > oldStock)
                SpawnDummyAtSlot(newStock - 1);
            else if (newStock < oldStock)
                RemoveDummyAtSlot(newStock);

            if (IsFull) OnShelfFilled?.Invoke();
        }

        private void OnProductChanged(string oldID, string newID)
        {
            if (string.IsNullOrEmpty(newID)) ClearAllDummies();
            else
            {
                _cachedProduct = ItemDatabase.Instance?.GetProductByID(newID);
                RebuildAllVisuals();
            }
        }

        // ─── Kapasite Hesabı ─────────────────────────────────────
        private int CalculateMaxCapacity(ProductData product)
        {
            int total = 0;
            foreach (var plank in _planks)
            {
                float slotW = product.itemWidth + product.shelfSpacing;
                float slotD = product.itemDepth + product.shelfSpacing;

                int cols = slotW > 0 ? Mathf.FloorToInt(plank.width  / slotW) : 0;
                int rows = slotD > 0 ? Mathf.FloorToInt(plank.depth  / slotD) : 0;
                total += cols * rows;
            }
            return Mathf.Max(0, total);
        }

        // ─── Görsel Yönetim ──────────────────────────────────────
        /// <summary>
        /// Sıfırdan tüm dummy'leri oluşturur.
        /// Sadece spawn ve ürün değişiminde çağrılır.
        /// </summary>
        private void RebuildAllVisuals()
        {
            ClearAllDummies();

            if (_cachedProduct == null && !string.IsNullOrEmpty(_productID.Value))
                _cachedProduct = ItemDatabase.Instance?.GetProductByID(_productID.Value);

            if (_cachedProduct == null) return;

            int capacity = CalculateMaxCapacity(_cachedProduct);
            for (int i = 0; i < capacity; i++)
                _slotDummies.Add(null); // Slot rezerve et

            for (int i = 0; i < _currentStock.Value; i++)
                SpawnDummyAtSlot(i);
        }

        private void ClearAllDummies()
        {
            foreach (var dummy in _slotDummies)
                if (dummy != null) Destroy(dummy);
            _slotDummies.Clear();
        }

        /// <summary>
        /// Belirli bir slot index'ine dummy spawn eder.
        /// Pivot offset + shelfSpacing + shelfRotationOffset SO'dan okunur.
        /// </summary>
        private void SpawnDummyAtSlot(int slotIndex)
{
    if (_cachedProduct == null)                            return;
    if (slotIndex < 0 || slotIndex >= _slotDummies.Count) return;
    if (_slotDummies[slotIndex] != null)                   return;

    Vector3    worldPos;
    Quaternion worldRot;

    if (!GetSlotTransform(slotIndex, out worldPos, out worldRot)) return;

    GameObject dummy = Instantiate(
        _cachedProduct.singleItemPrefab, worldPos, worldRot);

    // Pivot offset uygula
    dummy.transform.position += worldRot * _cachedProduct.shelfRotationOffset;
    dummy.transform.SetParent(_planks[GetPlankIndex(slotIndex)].startPoint);

    // Fizik kapat
    foreach (var col in dummy.GetComponentsInChildren<Collider>())
        col.enabled = false;
    foreach (var rb in dummy.GetComponentsInChildren<Rigidbody>())
        Destroy(rb);
    if (dummy.TryGetComponent(out NetworkObject netObj))
        Destroy(netObj);

    _slotDummies[slotIndex] = dummy;

    // ─── DOTween Animasyon ───────────────────────────────────
    // Yukarıdan düşerek yerine otur
    Vector3 finalPos    = dummy.transform.localPosition;
    Vector3 startPos    = finalPos + Vector3.up * 0.3f;

    dummy.transform.localPosition = startPos;
    dummy.transform.localScale    = Vector3.zero;

    Sequence seq = DOTween.Sequence();

    // Önce scale aç
    seq.Append(dummy.transform
        .DOScale(Vector3.one, 0.2f)
        .SetEase(Ease.OutBack));

    // Aynı anda yerine in
    seq.Join(dummy.transform
        .DOLocalMove(finalPos, 0.25f)
        .SetEase(Ease.OutBounce));

    // Hafif zıplama efekti
    seq.Append(dummy.transform
        .DOLocalMoveY(finalPos.y + 0.02f, 0.08f)
        .SetEase(Ease.OutQuad));

    seq.Append(dummy.transform
        .DOLocalMoveY(finalPos.y, 0.08f)
        .SetEase(Ease.InQuad));
}

        private void RemoveDummyAtSlot(int slotIndex)
{
    if (slotIndex < 0 || slotIndex >= _slotDummies.Count) return;

    GameObject dummy = _slotDummies[slotIndex];
    if (dummy == null) return;

    _slotDummies[slotIndex] = null;

    // ─── DOTween Kaldırma Animasyonu ─────────────────────────
    Sequence seq = DOTween.Sequence();

    seq.Append(dummy.transform
        .DOScale(Vector3.zero, 0.15f)
        .SetEase(Ease.InBack));

    seq.Join(dummy.transform
        .DOLocalMoveY(dummy.transform.localPosition.y + 0.15f, 0.15f)
        .SetEase(Ease.OutQuad));

    seq.OnComplete(() =>
    {
        if (dummy != null) Destroy(dummy);
    });
}


        /// <summary>
        /// Global slot index'inden dünya pozisyonu ve rotasyonunu hesaplar.
        /// shelfSpacing dahil slot genişliği kullanılır — iç içe geçme olmaz.
        /// </summary>
        private bool GetSlotTransform(int slotIndex, out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;

            if (_cachedProduct == null) return false;

            float slotW = _cachedProduct.itemWidth  + _cachedProduct.shelfSpacing;
            float slotD = _cachedProduct.itemDepth  + _cachedProduct.shelfSpacing;

            int remaining = slotIndex;

            foreach (var plank in _planks)
            {
                if (plank.startPoint == null) continue;

                int cols = slotW > 0 ? Mathf.FloorToInt(plank.width / slotW) : 0;
                int rows = slotD > 0 ? Mathf.FloorToInt(plank.depth / slotD) : 0;
                int plankCapacity = cols * rows;

                if (remaining >= plankCapacity)
                {
                    remaining -= plankCapacity;
                    continue;
                }

                int col = remaining % cols;
                int row = remaining / cols;

                // Pivot ortada — yarım slot kaydır ki kenardan başlasın
                float xOffset = (col + 0.5f) * slotW;
                float zOffset = (row + 0.5f) * slotD;

                if (plank.flipX) xOffset = -xOffset;
                if (plank.flipZ) zOffset = -zOffset;

                // ItemData.pivotOffset ile prefab pivot düzeltmesi
                Vector3 localPos = new Vector3(xOffset, 0f, zOffset);

                worldPos = plank.startPoint.TransformPoint(localPos);
                worldRot = plank.startPoint.rotation *
                           Quaternion.Euler(_cachedProduct.shelfRotationOffset);

                return true;
            }

            return false;
        }

        /// <summary>Global slot index'inin hangi tahta'ya ait olduğunu döner.</summary>
        private int GetPlankIndex(int slotIndex)
        {
            if (_cachedProduct == null) return 0;

            float slotW = _cachedProduct.itemWidth  + _cachedProduct.shelfSpacing;
            float slotD = _cachedProduct.itemDepth  + _cachedProduct.shelfSpacing;

            int remaining = slotIndex;
            for (int i = 0; i < _planks.Length; i++)
            {
                int cols = slotW > 0 ? Mathf.FloorToInt(_planks[i].width / slotW) : 0;
                int rows = slotD > 0 ? Mathf.FloorToInt(_planks[i].depth / slotD) : 0;
                int cap  = cols * rows;

                if (remaining < cap) return i;
                remaining -= cap;
            }
            return _planks.Length - 1;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_planks == null) return;

            foreach (var plank in _planks)
            {
                if (plank.startPoint == null) continue;

                // Raf alanını göster
                Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
                Vector3 center = plank.startPoint.TransformPoint(
                    new Vector3(plank.width * 0.5f, 0f, plank.depth * 0.5f));
                Gizmos.DrawCube(center, new Vector3(plank.width, 0.01f, plank.depth));

                // StartPoint'i göster
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(plank.startPoint.position, 0.03f);
            }
        }
#endif
    }
}