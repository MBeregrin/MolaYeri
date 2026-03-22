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

namespace WeBussedUp.Stations.Restroom
{
    public enum RestroomStall
    {
        Stall1,
        Stall2,
        Stall3,
        Stall4
    }

    /// <summary>
    /// WC istasyonu.
    /// Müşteri gelir → boş kabin bulur → kullanır → kirli olur.
    /// Oyuncu temizlik modunda bez ile siler veya CleaningTask ile temizler.
    /// Temizlik memnuniyeti ve rating'i etkiler.
    /// </summary>
    public class RestroomStation : NetworkBehaviour, IInteractable
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Kabin Ayarları")]
        [SerializeField] private int   _stallCount      = 3;
        [SerializeField] private float _useTime         = 10f;  // Müşteri kaç saniye kullanır
        [SerializeField] private float _dirtyAmount     = 0.8f; // Kullanım sonrası kir miktarı

        [Header("Temizlik")]
        [SerializeField] private float _cleanThreshold  = 0.2f; // Bu altında temiz sayılır
        [SerializeField] private GameObject _cleaningTaskPrefab; // Spawn edilecek CleaningTask

        [Header("Servis Noktaları")]
        [SerializeField] private Transform[] _stallPoints;      // Kabin noktaları
        [SerializeField] private Transform   _entrancePoint;    // Giriş noktası

        [Header("Görsel")]
        [SerializeField] private Renderer[]  _stallLights;      // Kabin ışıkları
        [SerializeField] private Color       _colorFree   = Color.green;
        [SerializeField] private Color       _colorBusy   = Color.red;
        [SerializeField] private Color       _colorDirty  = new Color(0.6f, 0.4f, 0f);

        [Header("Ses")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip   _doorClip;
        [SerializeField] private AudioClip   _flushClip;

        [Header("Olaylar")]
        public UnityEvent OnRestroomCleaned;
        public UnityEvent OnRestroomDirty;

        // ─── Network State ───────────────────────────────────────
        // Her kabin için doluluk durumu
        public NetworkVariable<int> OccupiedStalls = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<float> CleanlinessLevel = new NetworkVariable<float>(
            1f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────

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
        private bool[]           _stallOccupied;
        private List<Coroutine>  _stallCoroutines = new();

        // ─── Public API ──────────────────────────────────────────
        public bool HasFreeStall    => OccupiedStalls.Value < _stallCount;
        public bool IsDirty         => CleanlinessLevel.Value < _cleanThreshold;
        public bool IsFullyCleaned  => CleanlinessLevel.Value >= 1f;

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            _stallOccupied = new bool[_stallCount];

            OccupiedStalls.OnValueChanged    += OnOccupiedChanged;
            CleanlinessLevel.OnValueChanged  += OnCleanlinessChanged;

            UpdateStallLights();
        }

        public override void OnNetworkDespawn()
        {
            OccupiedStalls.OnValueChanged    -= OnOccupiedChanged;
            CleanlinessLevel.OnValueChanged  -= OnCleanlinessChanged;
        }

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            if (IsDirty)
                return $"WC Kirli — Temizle! (Temizlik Modu: T) 🧹";

            if (!HasFreeStall)
                return "Tüm Kabinler Dolu!";

            return $"WC ({OccupiedStalls.Value}/{_stallCount} Dolu) [E]";
        }

        public bool CanInteract(ulong playerId) => HasFreeStall && !IsDirty;

        public InteractionType GetInteractionType() => InteractionType.Use;

        public void Interact(ulong playerId)
        {
            if (!IsSpawned) return;
            // Oyuncu direkt etkileşim kurmaz — sadece NPC kullanır
            // Oyuncunun görevi temizlemek
        }

        // ─── NPC Kullanımı ───────────────────────────────────────
        /// <summary>
        /// CustomerAI tarafından çağrılır.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RequestStallServerRpc(ulong customerId)
        {
            if (!IsServer || !HasFreeStall) return;

            // Boş kabin bul
            int stallIndex = FindFreeStall();
            if (stallIndex < 0) return;

            _stallOccupied[stallIndex] = true;
            OccupiedStalls.Value++;

            // Kabin kullanım rutinini başlat
            StartCoroutine(StallUseRoutine(stallIndex, customerId));

            StallDoorClientRpc(stallIndex, isOpening: true);
        }

        private IEnumerator StallUseRoutine(int stallIndex, ulong customerId)
        {
            // Müşteri kabini kullanıyor
            yield return new WaitForSeconds(_useTime);

            // Kabin kirli oldu
            _stallOccupied[stallIndex] = false;
            OccupiedStalls.Value       = Mathf.Max(0, OccupiedStalls.Value - 1);

            // Kir ekle
            float newCleanliness = Mathf.Max(0f, CleanlinessLevel.Value - _dirtyAmount);
            CleanlinessLevel.Value = newCleanliness;

            StallDoorClientRpc(stallIndex, isOpening: false);
            FlushClientRpc(stallIndex);

            // Kirli oldu — CleaningTask spawn et
            if (IsDirty && _cleaningTaskPrefab != null)
                SpawnCleaningTask(stallIndex);

            // CustomerAI'ya bildir
            NotifyCustomerDoneClientRpc(customerId);

            // Rating'e temizlik etkisi
            RatingManager.Instance?.AddCleanlinessBoostServerRpc(
                IsDirty ? -0.2f : 0f);

            OnRestroomDirty?.Invoke();

            Debug.Log($"[RestroomStation] Kabin {stallIndex} kullanıldı. " +
                      $"Temizlik: {CleanlinessLevel.Value:F2}");
        }

        private void SpawnCleaningTask(int stallIndex)
        {
            if (_cleaningTaskPrefab == null) return;

            Vector3 spawnPos = _stallPoints != null && stallIndex < _stallPoints.Length
                ? _stallPoints[stallIndex].position
                : transform.position;

            GameObject task = Instantiate(_cleaningTaskPrefab, spawnPos, Quaternion.identity);

            if (task.TryGetComponent(out NetworkObject netObj))
                netObj.Spawn();
        }

        // ─── Temizlik ────────────────────────────────────────────
        /// <summary>
        /// CleaningTool veya CleaningTask tamamlandığında çağrılır.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void CleanRestroomServerRpc(float cleanAmount, ulong cleanerId)
        {
            CleanlinessLevel.Value = Mathf.Min(1f, CleanlinessLevel.Value + cleanAmount);

            if (IsFullyCleaned)
            {
                RatingManager.Instance?.AddCleanlinessBoostServerRpc(0.3f);
                CleanCompleteClientRpc(cleanerId);
                OnRestroomCleaned?.Invoke();
            }
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void StallDoorClientRpc(int stallIndex, bool isOpening)
        {
            _audioSource?.PlayOneShot(_doorClip);
            UpdateStallLights();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FlushClientRpc(int stallIndex)
        {
            _audioSource?.PlayOneShot(_flushClip);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyCustomerDoneClientRpc(ulong customerId)
        {
            foreach (var ai in FindObjectsByType<CustomerAI>(FindObjectsInactive.Exclude))
            {
                var netObj = ai.GetComponent<NetworkObject>();
                if (netObj != null && netObj.OwnerClientId == customerId)
                {
                    ai.OnServiceCompleted(CustomerNeed.Restroom, 1f);
                    break;
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CleanCompleteClientRpc(ulong cleanerId)
        {
            if (NetworkManager.Singleton.LocalClientId == cleanerId)
                UIManager.Instance?.ShowNotification("WC Temizlendi! +Memnuniyet 🧹", Color.green);

            UpdateStallLights();
        }

        // ─── Görsel ──────────────────────────────────────────────
        private void UpdateStallLights()
        {
            if (_stallLights == null) return;

            for (int i = 0; i < _stallLights.Length; i++)
            {
                if (_stallLights[i] == null) continue;

                Color color;
                if (IsDirty)
                    color = _colorDirty;
                else if (i < _stallOccupied?.Length && _stallOccupied[i])
                    color = _colorBusy;
                else
                    color = _colorFree;

                _stallLights[i].material.color = color;
            }
        }

        private void OnOccupiedChanged(int oldVal, int newVal)  => UpdateStallLights();
        private void OnCleanlinessChanged(float oldVal, float newVal) => UpdateStallLights();

        // ─── Util ─────────────────────────────────────────────────
        private int FindFreeStall()
        {
            if (_stallOccupied == null) return -1;

            for (int i = 0; i < _stallCount; i++)
                if (!_stallOccupied[i]) return i;

            return -1;
        }

        // ─── Gizmos ──────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_entrancePoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(_entrancePoint.position, 0.2f);
                UnityEditor.Handles.Label(_entrancePoint.position + Vector3.up, "Giriş");
            }

            if (_stallPoints != null)
            {
                for (int i = 0; i < _stallPoints.Length; i++)
                {
                    if (_stallPoints[i] == null) continue;
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawSphere(_stallPoints[i].position, 0.15f);
                    UnityEditor.Handles.Label(
                        _stallPoints[i].position + Vector3.up, $"Kabin {i + 1}");
                }
            }
        }
#endif
    }
    
}