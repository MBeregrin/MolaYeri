using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using System.Collections;
using WeBussedUp.Stations.GasStation;
using WeBussedUp.Core.Managers;
using WeBussedUp.UI;

namespace WeBussedUp.NPC
{
    /// <summary>
    /// Tedarik kamyonu — FuelPump boşaldığında otomatik gelir, doldurur, gider.
    /// TrafficManager Truck tipinde spawn eder.
    /// </summary>
    public class FuelTanker : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Tanker Ayarları")]
        [SerializeField] private float _fuelCapacity    = 500f;  // Taşıdığı yakıt miktarı
        [SerializeField] private float _fillRate        = 10f;   // Saniyede kaç litre doldurur
        [SerializeField] private float _arrivalTimeout  = 60f;

        [Header("Hareket")]
        [SerializeField] private float _driveSpeed      = 6f;
        [SerializeField] private float _stoppingDist    = 1f;

        [Header("Görsel")]
        [SerializeField] private ParticleSystem _exhaustParticle;
        [SerializeField] private AudioSource    _engineAudio;
        [SerializeField] private AudioSource    _fillAudio;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<float> CurrentFuel = new NetworkVariable<float>(
            500f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsFillingPump = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private NavMeshAgent _agent;
        private FuelPump     _targetPump;
        private Vector3      _despawnPoint;

        // ─── Public API ──────────────────────────────────────────
        /// <summary>
        /// TrafficManager tarafından çağrılır.
        /// </summary>
        public void Initialize(Vector3 spawnPoint, Vector3 despawnPoint, FuelPump targetPump)
        {
            if (!IsServer) return;

            transform.position = spawnPoint;
            _despawnPoint      = despawnPoint;
            _targetPump        = targetPump;
            CurrentFuel.Value  = _fuelCapacity;

            StartCoroutine(TankerRoutine());
        }

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            _agent                  = GetComponent<NavMeshAgent>();
            _agent.speed            = _driveSpeed;
            _agent.stoppingDistance = _stoppingDist;
        }

        public override void OnNetworkSpawn()
        {
            IsFillingPump.OnValueChanged += OnFillingChanged;
            _exhaustParticle?.Play();
            _engineAudio?.Play();
        }

        public override void OnNetworkDespawn()
        {
            IsFillingPump.OnValueChanged -= OnFillingChanged;
        }

        // ─── Tanker Rutini ───────────────────────────────────────
        private IEnumerator TankerRoutine()
        {
            if (_targetPump == null) { Despawn(); yield break; }

            // Pompaya git
            _agent.SetDestination(_targetPump.transform.position);
            yield return WaitForArrival(_targetPump.transform.position);

            // Doldur
            yield return StartCoroutine(FillPumpRoutine());

            // Çık
            _agent.SetDestination(_despawnPoint);
            yield return WaitForArrival(_despawnPoint);

            Despawn();
        }

        private IEnumerator FillPumpRoutine()
        {
            IsFillingPump.Value = true;
            FillStartClientRpc();

            while (CurrentFuel.Value > 0f && _targetPump != null &&
                   _targetPump.CurrentFuel.Value < 60f)
            {
                float fillAmount = Mathf.Min(_fillRate * Time.deltaTime, CurrentFuel.Value);

                _targetPump.RefillPumpServerRpc(fillAmount);
                CurrentFuel.Value -= fillAmount;

                yield return null;
            }

            IsFillingPump.Value = false;
            FillCompleteClientRpc();
        }

        private IEnumerator WaitForArrival(Vector3 target, float timeout = 60f)
        {
            float timer = 0f;

            while (timer < timeout)
            {
                timer += Time.deltaTime;

                if (!_agent.pathPending &&
                    _agent.remainingDistance <= _stoppingDist)
                    yield break;

                yield return null;
            }

            transform.position = target;
        }

        private void Despawn()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn();
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void FillStartClientRpc()
        {
            _fillAudio?.Play();
            UIManager.Instance?.ShowNotification(
                "Yakıt ikmali başladı! ⛽", Color.cyan);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FillCompleteClientRpc()
        {
            _fillAudio?.Stop();
            UIManager.Instance?.ShowNotification(
                "Yakıt ikmali tamamlandı! ⛽", Color.green);
        }

        private void OnFillingChanged(bool oldVal, bool newVal)
        {
            if (!newVal) _fillAudio?.Stop();
        }
    }
}