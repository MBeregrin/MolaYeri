using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using WeBussedUp.Interfaces;
using WeBussedUp.Core.Managers;
using WeBussedUp.NPC;
using WeBussedUp.UI;
using WeBussedUp.Gameplay;

namespace WeBussedUp.Stations.Restaurant
{
    public enum FoodItem
    {
        Coffee,     // Kahve
        Tea,        // Çay
        Sandwich,   // Sandviç
        Burger,     // Burger
        Soup,       // Çorba
        Water       // Su
    }

    [System.Serializable]
    public class MenuItem
    {
        public FoodItem foodItem;
        public string   displayName;
        public float    price;
        public float    prepTime;    // Hazırlama süresi (saniye)
        public Sprite   icon;
    }

    [System.Serializable]
    public class Order
    {
        public ulong      customerId;
        public FoodItem   foodItem;
        public float      orderTime;   // Ne zaman sipariş verildi
        public bool       isReady;
    }

    /// <summary>
    /// Kafe/restoran istasyonu.
    /// Müşteri gelir → sipariş verir → oyuncu hazırlar → servis eder.
    /// Hız ve kalite memnuniyeti etkiler.
    /// Overcooked tarzı — birden fazla sipariş aynı anda gelebilir.
    /// </summary>
    public class RestaurantStation : NetworkBehaviour, IInteractable
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Menü")]
        [SerializeField] private List<MenuItem> _menuItems = new();
        [SerializeField] private int            _maxOrders = 4;

        [Header("Servis Noktaları")]
        [SerializeField] private Transform[] _customerSeats;   // Müşterilerin oturacağı yerler
        [SerializeField] private Transform   _counterPoint;    // Tezgah — oyuncu buraya gelir
        [SerializeField] private Transform   _kitchenPoint;    // Mutfak — hazırlama noktası

        [Header("Görsel")]
        [SerializeField] private Renderer    _statusLight;
        [SerializeField] private Color       _colorIdle   = Color.green;
        [SerializeField] private Color       _colorBusy   = Color.yellow;
        [SerializeField] private Color       _colorFull   = Color.red;

        [Header("Ses")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip   _orderReadyClip;
        [SerializeField] private AudioClip   _serveClip;

        [Header("Olaylar")]
        public UnityEvent<Order>  OnOrderPlaced;
        public UnityEvent<Order>  OnOrderServed;
        public UnityEvent         OnAllOrdersServed;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<int> ActiveOrderCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsPreparingOrder = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private NPCQueueSystem _queue;

private void Start()
{
    _queue = GetComponent<NPCQueueSystem>();
}

// CustomerAI gelince
public void OnCustomerArrived(CustomerAI customer)
{
    if (_queue != null && !_queue.IsFull)
        _queue.TryEnqueue(customer);
}

        // ─── Runtime ─────────────────────────────────────────────
        private List<Order>   _activeOrders    = new();
        private List<Order>   _readyOrders     = new();
        private Coroutine     _prepCoroutine;

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            ActiveOrderCount.OnValueChanged  += OnOrderCountChanged;
            IsPreparingOrder.OnValueChanged  += OnPreparingChanged;

            UpdateStatusLight();
        }

        public override void OnNetworkDespawn()
        {
            ActiveOrderCount.OnValueChanged  -= OnOrderCountChanged;
            IsPreparingOrder.OnValueChanged  -= OnPreparingChanged;
        }

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            if (_readyOrders.Count > 0)
                return $"Siparişi Servis Et ({_readyOrders.Count} hazır) [E]";

            if (ActiveOrderCount.Value >= _maxOrders)
                return "Tüm Masalar Dolu!";

            if (IsPreparingOrder.Value)
                return "Sipariş Hazırlanıyor...";

            return $"Sipariş Hazırla [E]";
        }

        public bool CanInteract(ulong playerId)
        {
            return _readyOrders.Count > 0 || 
                   (ActiveOrderCount.Value < _maxOrders && !IsPreparingOrder.Value);
        }

        public InteractionType GetInteractionType() => InteractionType.Use;

        public void Interact(ulong playerId)
        {
            if (!IsSpawned) return;

            // Hazır sipariş varsa servis et
            if (_readyOrders.Count > 0)
            {
                ServeOrderServerRpc(playerId);
                return;
            }

            // Değilse hazırlamaya başla
            if (!IsPreparingOrder.Value && ActiveOrderCount.Value < _maxOrders)
                StartPreparingServerRpc(playerId);
        }

        // ─── Sipariş Alma (CustomerAI tarafından) ────────────────
        /// <summary>
        /// CustomerAI istasyona gelince çağırır.
        /// </summary>
        public void PlaceOrderFromNPC(ulong customerId)
        {
            if (!IsServer) return;
            if (ActiveOrderCount.Value >= _maxOrders) return;
            if (_menuItems.Count == 0) return;

            // Rastgele menü öğesi seç
            MenuItem menuItem = _menuItems[Random.Range(0, _menuItems.Count)];

            Order order = new Order
            {
                customerId = customerId,
                foodItem   = menuItem.foodItem,
                orderTime  = Time.time,
                isReady    = false
            };

            _activeOrders.Add(order);
            ActiveOrderCount.Value++;

            NotifyNewOrderClientRpc(customerId, (int)menuItem.foodItem, menuItem.displayName);
            OnOrderPlaced?.Invoke(order);

            // Otomatik hazırlamayı başlat
            if (!IsPreparingOrder.Value)
                StartCoroutine(PrepareOrderRoutine(order, menuItem.prepTime));
        }

        // ─── Server RPC ──────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void StartPreparingServerRpc(ulong playerId)
        {
            if (IsPreparingOrder.Value || _activeOrders.Count == 0) return;

            Order order = _activeOrders[0];
            MenuItem menuItem = GetMenuItem(order.foodItem);

            if (menuItem == null) return;

            if (_prepCoroutine != null) StopCoroutine(_prepCoroutine);
            _prepCoroutine = StartCoroutine(PrepareOrderRoutine(order, menuItem.prepTime));
        }

        [Rpc(SendTo.Server)]
        private void ServeOrderServerRpc(ulong playerId)
        {
            if (_readyOrders.Count == 0) return;

            Order order = _readyOrders[0];
            _readyOrders.RemoveAt(0);
            _activeOrders.Remove(order);
            ActiveOrderCount.Value--;

            // Ödeme al
            MenuItem menuItem = GetMenuItem(order.foodItem);
            if (menuItem != null)
                EconomyManager.Instance?.AddMoneyServerRpc(menuItem.price, TransactionCategory.Sale);

            // Hız bonusu — ne kadar hızlı servis edildi
            float waitTime     = Time.time - order.orderTime;
            float speedScore   = Mathf.Clamp01(1f - (waitTime / 60f)); // 60sn max bekleme
            float serviceScore = speedScore;

            // CustomerAI'ya bildir
            NotifyCustomerServedClientRpc(order.customerId, serviceScore);
            ServeEffectClientRpc();

            OnOrderServed?.Invoke(order);

            if (_activeOrders.Count == 0 && _readyOrders.Count == 0)
                OnAllOrdersServed?.Invoke();

            UpdateStatusLight();
        }

        // ─── Hazırlama Rutini ─────────────────────────────────────
        private IEnumerator PrepareOrderRoutine(Order order, float prepTime)
        {
            IsPreparingOrder.Value = true;
            PrepareStartClientRpc();

            yield return new WaitForSeconds(prepTime);

            order.isReady = true;
            _readyOrders.Add(order);

            IsPreparingOrder.Value = false;
            OrderReadyClientRpc(order.customerId);

            // Sırada başka sipariş var mı?
            foreach (var nextOrder in _activeOrders)
            {
                if (!nextOrder.isReady)
                {
                    MenuItem nextItem = GetMenuItem(nextOrder.foodItem);
                    if (nextItem != null)
                    {
                        _prepCoroutine = StartCoroutine(
                            PrepareOrderRoutine(nextOrder, nextItem.prepTime));
                    }
                    break;
                }
            }
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyNewOrderClientRpc(ulong customerId, int foodItemIndex, string itemName)
        {
            UIManager.Instance?.ShowNotification(
                $"Yeni Sipariş: {itemName} 🍽️", Color.yellow);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OrderReadyClientRpc(ulong customerId)
        {
            _audioSource?.PlayOneShot(_orderReadyClip);
            UIManager.Instance?.ShowNotification(
                "Sipariş Hazır! Servis Et [E] 🔔", Color.green);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PrepareStartClientRpc()
        {
            UpdateStatusLight();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ServeEffectClientRpc()
        {
            _audioSource?.PlayOneShot(_serveClip);
            UpdateStatusLight();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyCustomerServedClientRpc(ulong customerId, float serviceScore)
        {
            // CustomerAI'ya hizmet tamamlandı bildir
            foreach (var ai in FindObjectsByType<CustomerAI>(FindObjectsInactive.Exclude))
            {
                var netObj = ai.GetComponent<NetworkObject>();
                if (netObj != null && netObj.OwnerClientId == customerId)
                {
                    ai.OnServiceCompleted(CustomerNeed.Cafe, serviceScore);
                    break;
                }
            }
        }

        // ─── Util ─────────────────────────────────────────────────
        private MenuItem GetMenuItem(FoodItem foodItem)
        {
            foreach (var item in _menuItems)
                if (item.foodItem == foodItem) return item;
            return null;
        }

        private void UpdateStatusLight()
        {
            if (_statusLight == null) return;

            Color color = ActiveOrderCount.Value == 0  ? _colorIdle
                        : ActiveOrderCount.Value >= _maxOrders ? _colorFull
                        : _colorBusy;

            _statusLight.material.color = color;
        }

        private void OnOrderCountChanged(int oldVal, int newVal) => UpdateStatusLight();
        private void OnPreparingChanged(bool oldVal, bool newVal) => UpdateStatusLight();

        // ─── Gizmos ──────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_counterPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(_counterPoint.position, 0.2f);
                UnityEditor.Handles.Label(_counterPoint.position + Vector3.up, "Tezgah");
            }

            if (_kitchenPoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(_kitchenPoint.position, 0.2f);
                UnityEditor.Handles.Label(_kitchenPoint.position + Vector3.up, "Mutfak");
            }

            if (_customerSeats != null)
            {
                Gizmos.color = Color.cyan;
                foreach (var seat in _customerSeats)
                    if (seat != null)
                    {
                        Gizmos.DrawSphere(seat.position, 0.15f);
                        UnityEditor.Handles.Label(seat.position + Vector3.up, "Koltuk");
                    }
            }
        }
#endif
    }
    
}