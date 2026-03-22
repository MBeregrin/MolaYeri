using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;
using WeBussedUp.Interfaces;
using WeBussedUp.Core.Managers;
using WeBussedUp.UI;
using WeBussedUp.Gameplay;
using WeBussedUp.NPC;

namespace WeBussedUp.Stations.GasStation
{
    public enum FuelType { Gasoline, Diesel, LPG }

    /// <summary>
    /// Benzin pompası istasyonu.
    /// Oyuncu aracı pompaya sürer → E ile etkileşim → POS cihazı ödeme alır → yakıt dolar.
    /// Alternatif: oyuncu pompa başına geçer, aracın yanında durur, E'ye basar → doldurma başlar.
    /// Dolum süresi yakıt miktarına göre hesaplanır.
    /// </summary>
    public class FuelPump : NetworkBehaviour, IInteractable
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Pompa Ayarları")]
        [SerializeField] private FuelType _fuelType        = FuelType.Gasoline;
        [SerializeField] private float    _pricePerLiter   = 42f;
        [SerializeField] private float    _flowRatePerSec  = 2f;   // Litre/saniye
        [SerializeField] private float    _maxCapacity     = 60f;  // Pompanın tankı (litre)

        [Header("Araç Algılama")]
        [SerializeField] private Transform  _vehicleSlot;          // Aracın durması gereken nokta
        [SerializeField] private float      _vehicleDetectRadius = 2.5f;
        [SerializeField] private LayerMask  _vehicleLayer;

        [Header("Görsel")]
        [SerializeField] private Renderer   _pumpRenderer;
        [SerializeField] private string     _emissionProperty = "_EmissionColor";
        [SerializeField] private Color      _colorIdle        = Color.white;
        [SerializeField] private Color      _colorActive      = Color.green;
        [SerializeField] private Color      _colorEmpty       = Color.red;

        [Header("Efektler")]
        [SerializeField] private ParticleSystem _fuelParticle;
        [SerializeField] private AudioSource    _pumpAudio;

        [Header("Olaylar")]
        public UnityEvent         OnFuelingStarted;
        public UnityEvent         OnFuelingCompleted;
        public UnityEvent         OnPumpEmpty;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<float> CurrentFuel = new NetworkVariable<float>(
            60f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsActive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<float> PricePerLiter = new NetworkVariable<float>(
            42f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private ulong   _currentCustomerId = ulong.MaxValue;
        private float   _targetFuelAmount  = 0f;  // Müşterinin istediği litre
        private float   _fueledAmount      = 0f;  // Bu seferlik doldurulan litre
        private bool    _isFueling         = false;

        // ─── Public API ──────────────────────────────────────────
        public FuelType FuelKind       => _fuelType;
        public bool     IsEmpty        => CurrentFuel.Value <= 0f;
        public bool     IsOccupied     => _currentCustomerId != ulong.MaxValue;

        // ─── NetworkBehaviour ────────────────────────────────────

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
        public override void OnNetworkSpawn()
        {
            PricePerLiter.Value = _pricePerLiter;
            CurrentFuel.Value   = _maxCapacity;

            CurrentFuel.OnValueChanged += OnFuelChanged;
            IsActive.OnValueChanged    += OnActiveChanged;

            UpdatePumpVisual(false);
        }

        public override void OnNetworkDespawn()
        {
            CurrentFuel.OnValueChanged -= OnFuelChanged;
            IsActive.OnValueChanged    -= OnActiveChanged;
        }

        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (!IsServer || !_isFueling) return;

            ProcessFueling();
        }

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            if (IsEmpty)    return $"{_fuelType} Bitti!";
            if (IsOccupied) return "Pompa Meşgul";

            return $"{_fuelType} Doldur — {PricePerLiter.Value:F2}₺/Lt [E]";
        }

        public bool CanInteract(ulong playerId)
        {
            if (IsEmpty)    return false;
            if (IsOccupied) return false;

            // Yakınında araç var mı?
            return HasVehicleNearby();
        }

        public InteractionType GetInteractionType() => InteractionType.Refuel;

        public void Interact(ulong playerId)
        {
            if (!IsSpawned || !CanInteract(playerId)) return;

            // Dolum miktarını sor — şimdilik max doldur, ileride POS UI ile miktar seçilecek
            RequestFuelingServerRpc(playerId, _maxCapacity);
        }

        // ─── Server RPC ──────────────────────────────────────────
        [Rpc(SendTo.Server)]
        public void RequestFuelingServerRpc(ulong playerId, float requestedLiters)
        {
            if (IsEmpty || IsOccupied) return;

            // Pompada ne kadar varsa o kadar doldur
            float actualLiters = Mathf.Min(requestedLiters, CurrentFuel.Value);
            float totalCost    = actualLiters * PricePerLiter.Value;

            if (EconomyManager.Instance != null &&
                !EconomyManager.Instance.HasEnoughMoney(totalCost))
            {
                NotifyClientRpc(playerId, "Yetersiz bakiye!", false);
                return;
            }

            // Parayı al
            EconomyManager.Instance?.SpendMoneyServerRpc(totalCost, TransactionCategory.Fuel);

            // Dolumu başlat
            _currentCustomerId = playerId;
            _targetFuelAmount  = actualLiters;
            _fueledAmount      = 0f;
            _isFueling         = true;

            IsActive.Value = true;
            StartFuelingClientRpc(actualLiters, totalCost);
        }

        [Rpc(SendTo.Server)]
        public void CancelFuelingServerRpc(ulong playerId)
        {
            if (_currentCustomerId != playerId) return;
            StopFueling(completed: false);
        }

        // ─── Dolum Mantığı ───────────────────────────────────────
        private void ProcessFueling()
        {
            float delta = _flowRatePerSec * Time.deltaTime;
            delta = Mathf.Min(delta, _targetFuelAmount - _fueledAmount);
            delta = Mathf.Min(delta, CurrentFuel.Value);

            _fueledAmount      += delta;
            CurrentFuel.Value  -= delta;

            bool done = _fueledAmount >= _targetFuelAmount || CurrentFuel.Value <= 0f;

            if (done) StopFueling(completed: true);
        }

        private void StopFueling(bool completed)
        {
            _isFueling         = false;
            IsActive.Value     = false;

            if (completed)
            {
                NotifyClientRpc(_currentCustomerId,
                    $"{_fueledAmount:F1}Lt doldu — {_fueledAmount * PricePerLiter.Value:F2}₺", true);
                OnFuelingCompleted?.Invoke();
            }

            _currentCustomerId = ulong.MaxValue;
            _targetFuelAmount  = 0f;
            _fueledAmount      = 0f;

            if (IsEmpty)
            {
                OnPumpEmpty?.Invoke();
                NotifyAllClientsEmptyClientRpc();
            }
        }

        // ─── Pompa Yenileme (Tedarik Kamyonu) ───────────────────
        /// <summary>
        /// Tedarik kamyonu veya oyuncu dolum yaptığında çağrılır.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RefillPumpServerRpc(float amount)
        {
            CurrentFuel.Value = Mathf.Min(CurrentFuel.Value + amount, _maxCapacity);
            Debug.Log($"[FuelPump] Pompa yenilendi: {CurrentFuel.Value:F0}Lt");
        }

        /// <summary>
        /// Fiyat güncelleme — EconomyManager veya oyuncu UI'dan çağırır.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void SetPriceServerRpc(float newPrice)
        {
            PricePerLiter.Value = Mathf.Max(0.01f, newPrice);
            Debug.Log($"[FuelPump] Yeni fiyat: {PricePerLiter.Value:F2}₺/Lt");
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void StartFuelingClientRpc(float liters, float cost)
        {
            _fuelParticle?.Play();
            _pumpAudio?.Play();
            OnFuelingStarted?.Invoke();

            UIManager.Instance?.ShowNotification(
                $"{liters:F1}Lt dolduruluyor — {cost:F2}₺", Color.cyan);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyClientRpc(ulong targetId, string message, bool success)
        {
            // Sadece hedef oyuncuya göster
            if (Unity.Netcode.NetworkManager.Singleton.LocalClientId != targetId) return;

            UIManager.Instance?.ShowNotification(message, success ? Color.green : Color.red);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyAllClientsEmptyClientRpc()
        {
            _fuelParticle?.Stop();
            _pumpAudio?.Stop();
            UIManager.Instance?.ShowNotification($"{_fuelType} bitti! Tedarik gerekiyor.", Color.red);
        }

        // ─── Araç Algılama ───────────────────────────────────────
        private bool HasVehicleNearby()
        {
            if (_vehicleSlot == null) return true; // Slot tanımsızsa her zaman izin ver

            Collider[] hits = Physics.OverlapSphere(
                _vehicleSlot.position, _vehicleDetectRadius, _vehicleLayer);

            return hits.Length > 0;
        }

        // ─── Görsel ──────────────────────────────────────────────
        private void OnFuelChanged(float oldVal, float newVal)
        {
            if (newVal <= 0f) UpdatePumpVisual(false, empty: true);
        }

        private void OnActiveChanged(bool oldVal, bool newVal)
        {
            UpdatePumpVisual(newVal);
            if (!newVal) { _fuelParticle?.Stop(); _pumpAudio?.Stop(); }
        }

        private void UpdatePumpVisual(bool active, bool empty = false)
        {
            if (_pumpRenderer == null) return;

            Color target = empty  ? _colorEmpty :
                           active ? _colorActive : _colorIdle;

            if (_pumpRenderer.material.HasProperty(_emissionProperty))
                _pumpRenderer.material.SetColor(_emissionProperty, target);
        }

        // ─── Gizmos ──────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_vehicleSlot == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_vehicleSlot.position, _vehicleDetectRadius);
        }
#endif
    }
}
