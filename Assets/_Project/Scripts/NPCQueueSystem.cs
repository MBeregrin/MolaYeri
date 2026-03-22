using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using WeBussedUp.NPC;

namespace WeBussedUp.Gameplay
{
    /// <summary>
    /// Herhangi bir istasyonun önünde sıra yönetir.
    /// CustomerAI istasyona yaklaştığında sıraya girer.
    /// Sıra doluysa müşteri başka istasyona yönlenir veya ayrılır.
    /// </summary>
    public class NPCQueueSystem : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Sıra Ayarları")]
        [SerializeField] private int     _maxQueueSize   = 5;
        [SerializeField] private float   _queueSpacing   = 0.8f;  // Aralar arası mesafe
        [SerializeField] private Transform _queueStartPoint;       // Sıranın başlangıç noktası
        [SerializeField] private Vector3   _queueDirection = Vector3.back; // Sıra yönü

        [Header("Bekleme")]
        [SerializeField] private float _maxWaitTime     = 60f;    // Max bekleme süresi
        [SerializeField] private float _satisfactionLossPerSec = 0.5f; // Beklerken memnuniyet kaybı

        [Header("Debug")]
        [SerializeField] private bool _showQueueGizmos = true;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<int> QueueCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private List<CustomerAI>  _queue          = new();
        private List<float>       _waitTimers     = new();
        private bool              _isServing      = false;

        // ─── Public API ──────────────────────────────────────────
        public bool  IsFull      => _queue.Count >= _maxQueueSize;
        public bool  IsEmpty     => _queue.Count == 0;
        public int   Count       => _queue.Count;

        /// <summary>
        /// CustomerAI sıraya girmek istediğinde çağırır.
        /// </summary>
        public bool TryEnqueue(CustomerAI customer)
        {
            if (!IsServer) return false;
            if (IsFull)    return false;
            if (_queue.Contains(customer)) return false;

            _queue.Add(customer);
            _waitTimers.Add(0f);
            QueueCount.Value = _queue.Count;

            // Müşteriyi sıra pozisyonuna yönlendir
            UpdateQueuePositions();

            customer.StartWaiting();

            Debug.Log($"[NPCQueueSystem] {gameObject.name} — " +
                      $"Sıraya girdi. Sıra: {_queue.Count}/{_maxQueueSize}");
            return true;
        }

        /// <summary>
        /// İstasyon hazır olduğunda ilk müşteriyi sıradan çıkar.
        /// </summary>
        public CustomerAI Dequeue()
        {
            if (!IsServer || IsEmpty) return null;

            CustomerAI customer = _queue[0];
            _queue.RemoveAt(0);
            _waitTimers.RemoveAt(0);
            QueueCount.Value = _queue.Count;

            customer.StopWaiting();

            // Kalan müşterileri öne kaydır
            UpdateQueuePositions();

            Debug.Log($"[NPCQueueSystem] {gameObject.name} — " +
                      $"Sıradan çıktı. Kalan: {_queue.Count}");
            return customer;
        }

        /// <summary>
        /// Müşteri sıradan ayrıldığında (sinirlenme vb.) çağrılır.
        /// </summary>
        public void RemoveFromQueue(CustomerAI customer)
        {
            if (!IsServer) return;

            int index = _queue.IndexOf(customer);
            if (index < 0) return;

            _queue.RemoveAt(index);
            _waitTimers.RemoveAt(index);
            QueueCount.Value = _queue.Count;

            UpdateQueuePositions();
        }

        /// <summary>
        /// Sıranın başındaki müşteriyi döner — Dequeue etmez.
        /// </summary>
        public CustomerAI Peek() => IsEmpty ? null : _queue[0];

        /// <summary>
        /// Servis başladığında çağrılır.
        /// </summary>
        public void SetServing(bool serving)
        {
            if (!IsServer) return;
            _isServing = serving;
        }

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            QueueCount.OnValueChanged += OnQueueCountChanged;
        }

        public override void OnNetworkDespawn()
        {
            QueueCount.OnValueChanged -= OnQueueCountChanged;
        }

        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (!IsServer || IsEmpty) return;

            UpdateWaitTimers();
        }

        // ─── Bekleme Yönetimi ─────────────────────────────────────
        private void UpdateWaitTimers()
        {
            for (int i = _waitTimers.Count - 1; i >= 0; i--)
            {
                _waitTimers[i] += Time.deltaTime;

                // Memnuniyet düşür
                if (i < _queue.Count)
                    _queue[i].ModifySatisfactionPublic(-_satisfactionLossPerSec * Time.deltaTime);

                // Max bekleme aşıldıysa sıradan çıkar
                if (_waitTimers[i] >= _maxWaitTime)
                {
                    CustomerAI impatientCustomer = _queue[i];
                    RemoveFromQueue(impatientCustomer);

                    Debug.Log($"[NPCQueueSystem] Müşteri çok bekledi, ayrılıyor!");
                }
            }
        }

        // ─── Pozisyon Güncelleme ─────────────────────────────────
        private void UpdateQueuePositions()
        {
            if (_queueStartPoint == null) return;

            for (int i = 0; i < _queue.Count; i++)
            {
                if (_queue[i] == null) continue;

                Vector3 targetPos = _queueStartPoint.position +
                                    _queueDirection.normalized * (i * _queueSpacing);

                // NavMesh agent'ı yeni pozisyona yönlendir
                var agent = _queue[i].GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null && agent.isActiveAndEnabled)
                    agent.SetDestination(targetPos);
            }
        }

        // ─── Callbacks ───────────────────────────────────────────
        private void OnQueueCountChanged(int oldVal, int newVal)
        {
            // UI güncelleme — ileride sıra göstergesi eklenebilir
        }

        // ─── Gizmos ──────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!_showQueueGizmos || _queueStartPoint == null) return;

            for (int i = 0; i < _maxQueueSize; i++)
            {
                Vector3 pos = _queueStartPoint.position +
                              _queueDirection.normalized * (i * _queueSpacing);

                // Dolu slot
                Gizmos.color = i < (_queue?.Count ?? 0)
                    ? new Color(1f, 0.5f, 0f, 0.8f)
                    : new Color(0f, 1f, 0f, 0.3f);

                Gizmos.DrawSphere(pos, 0.2f);

                // Slotlar arası çizgi
                if (i > 0)
                {
                    Vector3 prevPos = _queueStartPoint.position +
                                      _queueDirection.normalized * ((i - 1) * _queueSpacing);
                    Gizmos.color = Color.white;
                    Gizmos.DrawLine(prevPos, pos);
                }
            }

            // Sıra yönü oku
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(_queueStartPoint.position,
                _queueDirection.normalized * (_maxQueueSize * _queueSpacing));
        }
#endif
    }
}