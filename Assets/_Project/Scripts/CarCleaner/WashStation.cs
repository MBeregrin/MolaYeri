using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;
using System.Collections;
using WeBussedUp.Interfaces;
using WeBussedUp.Core.Managers;
using WeBussedUp.NPC;
using WeBussedUp.UI;
using WeBussedUp.Gameplay;
namespace WeBussedUp.Stations.CarWash
{
    public enum WashType { Basic, Foam, Full }

    /// <summary>
    /// Araba ve otobüs yıkama istasyonu.
    /// Araç slota girer → oyuncu veya NPC ödeme yapar → yıkama başlar.
    /// Yıkama sırasında SlipHazard spawn edilir — köpük yere düşer.
    /// Yıkama kalitesi müşteri memnuniyetini etkiler.
    /// </summary>
    public class WashStation : NetworkBehaviour, IInteractable
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("İstasyon Ayarları")]
        [SerializeField] private WashType _washType         = WashType.Foam;
        [SerializeField] private float    _washDuration     = 15f;  // Saniye
        [SerializeField] private float    _basicPrice       = 50f;
        [SerializeField] private float    _foamPrice        = 80f;
        [SerializeField] private float    _fullPrice        = 120f;

        [Header("Araç Slotu")]
        [SerializeField] private Transform _vehicleSlot;
        [SerializeField] private float     _vehicleDetectRadius = 3f;
        [SerializeField] private LayerMask _vehicleLayer;

        [Header("Köpük Sistemi")]
        [SerializeField] private GameObject   _slipHazardPrefab;
        [SerializeField] private Transform[]  _foamSpawnPoints;  // Köpüğün döküleceği noktalar
        [SerializeField] private float        _foamSpawnInterval = 3f;
        [SerializeField] private int          _maxFoamPatches    = 4;

        [Header("Görsel & Efekt")]
        [SerializeField] private ParticleSystem[] _washParticles;
        [SerializeField] private AudioSource      _washAudio;
        [SerializeField] private AudioClip        _startClip;
        [SerializeField] private AudioClip        _finishClip;
        [SerializeField] private Animator         _brushAnimator;  // Fırça animasyonu

        [Header("Işıklar")]
        [SerializeField] private Light _statusLight;
        [SerializeField] private Color _colorIdle    = Color.green;
        [SerializeField] private Color _colorActive  = Color.yellow;
        [SerializeField] private Color _colorBlocked = Color.red;

        [Header("Olaylar")]
        public UnityEvent         OnWashStarted;
        public UnityEvent<float>  OnWashCompleted;  // float = kalite skoru (0-1)
        public UnityEvent         OnVehicleEntered;
        public UnityEvent         OnVehicleExited;

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

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<bool> IsOccupied = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsWashing = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<float> WashProgress = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<float> CurrentPrice = new NetworkVariable<float>(
            50f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private ulong     _customerClientId  = ulong.MaxValue;
        private Coroutine _washCoroutine;
        private Coroutine _foamCoroutine;
        private int       _activeFoamCount   = 0;
        private float     _washQuality       = 1f; // Temizlik skoru (0-1)

        // ─── Public API ──────────────────────────────────────────
        public float GetPrice() => _washType switch
        {
            WashType.Basic => _basicPrice,
            WashType.Foam  => _foamPrice,
            WashType.Full  => _fullPrice,
            _              => _basicPrice
        };

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            if (IsServer)
                CurrentPrice.Value = GetPrice();

            IsOccupied.OnValueChanged  += OnOccupiedChanged;
            IsWashing.OnValueChanged   += OnWashingChanged;
            WashProgress.OnValueChanged += OnProgressChanged;

            UpdateStatusLight(_colorIdle);
        }

        public override void OnNetworkDespawn()
        {
            IsOccupied.OnValueChanged  -= OnOccupiedChanged;
            IsWashing.OnValueChanged   -= OnWashingChanged;
            WashProgress.OnValueChanged -= OnProgressChanged;
        }

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            if (IsWashing.Value)  return $"Yıkaniyor... %{WashProgress.Value * 100f:F0}";
            if (IsOccupied.Value) return "İstasyon Dolu";

            string typeName = _washType switch
            {
                WashType.Basic => "Basit Yıkama",
                WashType.Foam  => "Köpüklü Yıkama",
                WashType.Full  => "Tam Yıkama",
                _              => "Yıkama"
            };

            return $"{typeName} — {CurrentPrice.Value:F0}₺ [E]";
        }

        public bool CanInteract(ulong playerId)
        {
            if (IsOccupied.Value || IsWashing.Value) return false;
            return HasVehicleNearby();
        }

        public InteractionType GetInteractionType() => InteractionType.Use;

        public void Interact(ulong playerId)
        {
            if (!IsSpawned || !CanInteract(playerId)) return;
            RequestWashServerRpc(playerId);
        }

        // ─── Server RPC ──────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void RequestWashServerRpc(ulong playerId)
        {
            if (IsOccupied.Value || IsWashing.Value) return;

            float price = CurrentPrice.Value;

            if (EconomyManager.Instance != null &&
                !EconomyManager.Instance.HasEnoughMoney(price))
            {
                NotifyPlayerClientRpc(playerId, "Yetersiz bakiye!", false);
                return;
            }

            EconomyManager.Instance?.AddMoneyServerRpc(price, TransactionCategory.Sale);

            _customerClientId = playerId;
            IsOccupied.Value  = true;
            IsWashing.Value   = true;
            WashProgress.Value = 0f;
            _washQuality      = 1f;

            if (_washCoroutine != null) StopCoroutine(_washCoroutine);
            if (_foamCoroutine != null) StopCoroutine(_foamCoroutine);

            _washCoroutine = StartCoroutine(WashRoutine());

            if (_washType == WashType.Foam || _washType == WashType.Full)
                _foamCoroutine = StartCoroutine(FoamSpawnRoutine());

            StartWashClientRpc();
        }

        // ─── Yıkama Rutini ───────────────────────────────────────
        private IEnumerator WashRoutine()
        {
            float elapsed = 0f;

            while (elapsed < _washDuration)
            {
                elapsed            += Time.deltaTime;
                WashProgress.Value  = elapsed / _washDuration;
                yield return null;
            }

            WashProgress.Value = 1f;
            CompleteWash();
        }

        private IEnumerator FoamSpawnRoutine()
        {
            var wait = new WaitForSeconds(_foamSpawnInterval);

            while (IsWashing.Value && _activeFoamCount < _maxFoamPatches)
            {
                SpawnFoamPatch();
                yield return wait;
            }
        }

        private void SpawnFoamPatch()
        {
            if (_slipHazardPrefab == null || _foamSpawnPoints == null ||
                _foamSpawnPoints.Length == 0) return;

            // Rastgele spawn noktası seç
            Transform spawnPoint = _foamSpawnPoints[
                Random.Range(0, _foamSpawnPoints.Length)];

            GameObject foam = Instantiate(_slipHazardPrefab,
                spawnPoint.position, Quaternion.identity);

            if (foam.TryGetComponent(out NetworkObject netObj))
                netObj.Spawn();

            _activeFoamCount++;

            // Köpük despawn olunca sayacı azalt
            if (foam.TryGetComponent(out SlipHazard slipHazard))
                StartCoroutine(TrackFoamDespawn(foam));
        }

        private IEnumerator TrackFoamDespawn(GameObject foam)
        {
            while (foam != null) yield return null;
            _activeFoamCount = Mathf.Max(0, _activeFoamCount - 1);
        }

        private void CompleteWash()
        {
            IsWashing.Value  = false;
            IsOccupied.Value = false;

            // Müşteri memnuniyetine bildir
            float quality = _washQuality;
            CompleteWashClientRpc(_customerClientId, quality);

            // CustomerAI'ya hizmet tamamlandı bildirimi
            NotifyCustomerAI(_customerClientId, quality);

            OnWashCompleted?.Invoke(quality);
            _customerClientId = ulong.MaxValue;
        }

        private void NotifyCustomerAI(ulong customerId, float quality)
        {
            // Sahnedeki CustomerAI'ları tara, ilgili müşteriyi bul
            foreach (var ai in FindObjectsByType<CustomerAI>(FindObjectsInactive.Exclude))
            {
                var netObj = ai.GetComponent<NetworkObject>();
                if (netObj != null && netObj.OwnerClientId == customerId)
                {
                    ai.OnServiceCompleted(CustomerNeed.CarWash, quality);
                    break;
                }
            }
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void StartWashClientRpc()
        {
            foreach (var p in _washParticles) p?.Play();

            if (_washAudio != null && _startClip != null)
                _washAudio.PlayOneShot(_startClip);

            _brushAnimator?.SetBool("IsWashing", true);
            OnWashStarted?.Invoke();
            UpdateStatusLight(_colorActive);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CompleteWashClientRpc(ulong customerId, float quality)
        {
            foreach (var p in _washParticles) p?.Stop();

            if (_washAudio != null && _finishClip != null)
                _washAudio.PlayOneShot(_finishClip);

            _brushAnimator?.SetBool("IsWashing", false);
            UpdateStatusLight(_colorIdle);

            // Sadece ödeme yapan oyuncuya bildir
            if (NetworkManager.Singleton.LocalClientId == customerId)
            {
                int stars = Mathf.RoundToInt(Mathf.Lerp(1f, 5f, quality));
                UIManager.Instance?.ShowNotification(
                    $"Yıkama tamamlandı! {new string('⭐', stars)}", Color.cyan);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyPlayerClientRpc(ulong targetId, string message, bool success)
        {
            if (NetworkManager.Singleton.LocalClientId != targetId) return;
            UIManager.Instance?.ShowNotification(message, success ? Color.green : Color.red);
        }

        // ─── Araç Algılama ───────────────────────────────────────
        private bool HasVehicleNearby()
        {
            if (_vehicleSlot == null) return true;

            Collider[] hits = Physics.OverlapSphere(
                _vehicleSlot.position, _vehicleDetectRadius, _vehicleLayer);

            return hits.Length > 0;
        }

        // ─── Görsel ──────────────────────────────────────────────
        private void OnOccupiedChanged(bool oldVal, bool newVal)
        {
            UpdateStatusLight(newVal ? _colorBlocked : _colorIdle);
            if (newVal) OnVehicleEntered?.Invoke();
            else        OnVehicleExited?.Invoke();
        }

        private void OnWashingChanged(bool oldVal, bool newVal)
        {
            UpdateStatusLight(newVal ? _colorActive : _colorIdle);
        }

        private void OnProgressChanged(float oldVal, float newVal)
        {
            // Progress bar UI — ileride WashStation UI paneli bağlanır
        }

        private void UpdateStatusLight(Color color)
        {
            if (_statusLight != null) _statusLight.color = color;
        }

        // ─── Gizmos ──────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_vehicleSlot != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(_vehicleSlot.position, _vehicleDetectRadius);
            }

            if (_foamSpawnPoints != null)
            {
                Gizmos.color = Color.blue;
                foreach (var p in _foamSpawnPoints)
                    if (p != null) Gizmos.DrawSphere(p.position, 0.2f);
            }
        }
#endif
    }
}


