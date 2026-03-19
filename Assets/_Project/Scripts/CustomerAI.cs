using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using WeBussedUp.Core.Managers;

namespace WeBussedUp.NPC
{
    [System.Serializable]
    public class CustomerAppearance
    {
        public Mesh       bodyMesh;
        public Material[] outfitMaterials;
        public Color      skinColor;
    }

    /// <summary>
    /// Tek bir müşteri NPC'sinin davranışını yönetir.
    /// Araçtan iner → ihtiyaç listesini gezir → öder → araca biner.
    /// Memnuniyet: bekleme süresi, temizlik, fiyat, hizmet hızına göre hesaplanır.
    /// Server yetkili — görsel senkronizasyon ClientRpc ile yapılır.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class CustomerAI : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Hareket")]
        [SerializeField] private float _moveSpeed      = 3.5f;
        [SerializeField] private float _stoppingDist   = 0.5f;
        [SerializeField] private float _arrivalTimeout = 30f;

        [Header("Memnuniyet")]
        [SerializeField] private float _angerPerSecond = 1f;
        [SerializeField] private float _tipThreshold   = 80f;
        [SerializeField] private float _tipAmount      = 5f;
        [SerializeField] private float _leaveThreshold = 20f;

        [Header("Görsel")]
        [SerializeField] private SkinnedMeshRenderer  _bodyRenderer;
        [SerializeField] private CustomerAppearance[] _maleAppearances;
        [SerializeField] private CustomerAppearance[] _femaleAppearances;
        [SerializeField] private CustomerAppearance[] _childAppearances;

        [Header("Animasyon")]
        [SerializeField] private Animator _animator;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<float> Satisfaction = new NetworkVariable<float>(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> State = new NetworkVariable<int>(
            (int)CustomerState.SpawningFromVehicle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private NavMeshAgent       _agent;
        private CustomerGender     _gender;
        private List<CustomerNeed> _needQueue    = new();
        private CustomerNeed       _currentNeed  = CustomerNeed.None;
        private Transform          _exitPoint;
        private VehicleType        _vehicleType;

        private float _waitTimer         = 0f;
        private bool  _isWaiting         = false;
        private bool  _initialized       = false;
        private float _cleanlinessBonus  = 0f;
        private float _priceModifier     = 0f;

        // ─── Public API ──────────────────────────────────────────
        public void Initialize(CustomerGender gender, List<CustomerNeed> needs,
                               Transform exitPoint, VehicleType vehicleType)
        {
            if (!IsServer) return;

            _gender      = gender;
            _needQueue   = new List<CustomerNeed>(needs);
            _exitPoint   = exitPoint;
            _vehicleType = vehicleType;
            _initialized = true;

            ApplyAppearanceClientRpc((int)gender, Random.Range(0, GetAppearanceCount(gender)));
            ProcessNextNeed();
        }

        public void OnServiceCompleted(CustomerNeed completedNeed, float serviceQuality)
        {
            if (!IsServer) return;
            ModifySatisfaction((serviceQuality - 0.5f) * 20f);
            ProcessNextNeed();
        }

        public void StartWaiting()
        {
            if (!IsServer) return;
            _isWaiting  = true;
            _waitTimer  = 0f;
            State.Value = (int)CustomerState.WaitingInQueue;
        }

        public void StopWaiting()
        {
            if (!IsServer) return;
            _isWaiting  = false;
            State.Value = (int)CustomerState.BeingServed;
        }

        public void AddCleanlinessBonus(float bonus) => _cleanlinessBonus += bonus;

        public void SetPassthrough(Vector3 from, Vector3 to)
        {
            if (!IsServer) return;
            transform.position = from;
            _agent.SetDestination(to);
        }

        public void SetDestination(Vector3 parkingPosition, VehicleType type)
        {
            if (!IsServer) return;
            _vehicleType = type;
            _agent.SetDestination(parkingPosition);
            State.Value  = (int)CustomerState.SpawningFromVehicle;
        }

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            _agent                  = GetComponent<NavMeshAgent>();
            _agent.speed            = _moveSpeed;
            _agent.stoppingDistance = _stoppingDist;
        }

        public override void OnNetworkSpawn()
        {
            State.OnValueChanged        += OnStateChanged;
            Satisfaction.OnValueChanged += OnSatisfactionChanged;
        }

        public override void OnNetworkDespawn()
        {
            State.OnValueChanged        -= OnStateChanged;
            Satisfaction.OnValueChanged -= OnSatisfactionChanged;
        }

        private void Update()
        {
            if (!IsServer || !_initialized) return;
            HandleWaiting();
            UpdateAnimator();
        }

        // ─── State Machine ───────────────────────────────────────
        private void ProcessNextNeed()
        {
            if (_needQueue.Count == 0)
            {
                StartCoroutine(ExitRoutine());
                return;
            }

            _currentNeed = _needQueue[0];
            _needQueue.RemoveAt(0);

            Transform station = FindNearestStation(_currentNeed);

            if (station == null)
            {
                ModifySatisfaction(-10f);
                ProcessNextNeed();
                return;
            }

            State.Value = (int)CustomerState.MovingToStation;
            _agent.SetDestination(station.position);
            StartCoroutine(WaitForArrival(station));
        }

        private IEnumerator WaitForArrival(Transform target)
        {
            float timer = 0f;

            while (timer < _arrivalTimeout)
            {
                timer += Time.deltaTime;

                if (!_agent.pathPending && _agent.remainingDistance <= _stoppingDist)
                {
                    NotifyStationArrivalClientRpc(target.position);
                    yield break;
                }

                yield return null;
            }

            ModifySatisfaction(-15f);
            ProcessNextNeed();
        }

        private IEnumerator ExitRoutine()
        {
            if (_exitPoint == null) { Despawn(); yield break; }

            State.Value = (int)CustomerState.MovingToExit;
            _agent.SetDestination(_exitPoint.position);

            float timer = 0f;
            while (timer < _arrivalTimeout)
            {
                timer += Time.deltaTime;

                if (!_agent.pathPending && _agent.remainingDistance <= _stoppingDist)
                {
                    ResolvePaymentAndLeave();
                    yield break;
                }
                yield return null;
            }

            Despawn();
        }

        // ─── Ödeme & Ayrılış ─────────────────────────────────────
        private void ResolvePaymentAndLeave()
        {
            State.Value = (int)CustomerState.EnteringVehicle;

            float finalSatisfaction = Mathf.Clamp(
                Satisfaction.Value + _cleanlinessBonus - _priceModifier,
                0f, 100f);

            // Bahşiş
            if (finalSatisfaction >= _tipThreshold)
                EconomyManager.Instance?.AddMoneyServerRpc(_tipAmount, TransactionCategory.Bonus);

            int stars = Mathf.RoundToInt(Mathf.Lerp(1f, 5f, finalSatisfaction / 100f));
            ShowReactionClientRpc(finalSatisfaction >= _tipThreshold, stars);

            // RatingManager'a bildir
            RatingManager.Instance?.ReportSimpleRatingServerRpc(finalSatisfaction);

            Debug.Log($"[CustomerAI] Müşteri ayrıldı. Memnuniyet: {finalSatisfaction:F0} | {stars}⭐");

            Despawn();
        }

        private void Despawn()
        {
            if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn();
        }

        // ─── Memnuniyet ──────────────────────────────────────────
        private void HandleWaiting()
        {
            if (!_isWaiting) return;

            _waitTimer += Time.deltaTime;
            ModifySatisfaction(-_angerPerSecond * Time.deltaTime);

            if (Satisfaction.Value <= _leaveThreshold)
            {
                _isWaiting = false;
                _needQueue.Clear();
                StartCoroutine(ExitRoutine());
            }
        }

        private void ModifySatisfaction(float delta)
        {
            Satisfaction.Value = Mathf.Clamp(Satisfaction.Value + delta, 0f, 100f);
        }

        // ─── İstasyon Arama ──────────────────────────────────────
        private Transform FindNearestStation(CustomerNeed need)
        {
            string tag = need switch
            {
                CustomerNeed.Shopping => "StationMarket",
                CustomerNeed.Cafe     => "StationCafe",
                CustomerNeed.Fuel     => "StationFuel",
                CustomerNeed.CarWash  => "StationCarWash",
                CustomerNeed.Restroom => "StationRestroom",
                _                     => string.Empty
            };

            if (string.IsNullOrEmpty(tag)) return null;

            GameObject[] stations = GameObject.FindGameObjectsWithTag(tag);
            if (stations.Length == 0) return null;

            Transform nearest = null;
            float     minDist = float.MaxValue;

            foreach (var s in stations)
            {
                float dist = Vector3.Distance(transform.position, s.transform.position);
                if (dist < minDist) { minDist = dist; nearest = s.transform; }
            }

            return nearest;
        }

        // ─── Görsel ──────────────────────────────────────────────
        private int GetAppearanceCount(CustomerGender gender) => gender switch
        {
            CustomerGender.Male   => _maleAppearances  != null ? _maleAppearances.Length   : 0,
            CustomerGender.Female => _femaleAppearances != null ? _femaleAppearances.Length : 0,
            _                     => _childAppearances  != null ? _childAppearances.Length  : 0
        };

        private void UpdateAnimator()
        {
            if (_animator == null) return;
            _animator.SetFloat("Speed",     _agent.velocity.magnitude, 0.1f, Time.deltaTime);
            _animator.SetBool ("IsWaiting", _isWaiting);
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void ApplyAppearanceClientRpc(int genderIndex, int appearanceIndex)
        {
            if (_bodyRenderer == null) return;

            CustomerGender     gender = (CustomerGender)genderIndex;
            CustomerAppearance[] pool = gender switch
            {
                CustomerGender.Male   => _maleAppearances,
                CustomerGender.Female => _femaleAppearances,
                _                     => _childAppearances
            };

            if (pool == null || pool.Length == 0) return;

            var appearance = pool[Mathf.Clamp(appearanceIndex, 0, pool.Length - 1)];
            if (appearance.outfitMaterials != null)
                _bodyRenderer.materials = appearance.outfitMaterials;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyStationArrivalClientRpc(Vector3 stationPos) { }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShowReactionClientRpc(bool positive, int stars)
        {
            Debug.Log($"[CustomerAI] {(positive ? "😊" : "😠")} {stars}⭐");
        }

        // ─── Callbacks ───────────────────────────────────────────
        private void OnStateChanged(int oldState, int newState)
        {
            _animator?.SetInteger("State", newState);
        }

        private void OnSatisfactionChanged(float oldVal, float newVal) { }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_agent == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, _agent.destination);
            Gizmos.DrawWireSphere(_agent.destination, 0.3f);
        }
#endif
    }
}