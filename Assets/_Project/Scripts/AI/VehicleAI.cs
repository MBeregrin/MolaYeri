using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using WeBussedUp.NPC;

namespace WeBussedUp.NPC
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class VehicleAI : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Araç Ayarları")]
        [SerializeField] private VehicleType _vehicleType    = VehicleType.Car;
        [SerializeField] private float       _driveSpeed     = 8f;
        [SerializeField] private float       _parkSpeed      = 3f;
        [SerializeField] private float       _stoppingDist   = 0.5f;

        [Header("NPC Ayarları")]
        [SerializeField] private Transform[] _passengerSeats;
        [SerializeField] private Transform   _exitPoint;

        [Header("Yolculuk")]
        [SerializeField] private float _parkDuration     = 30f;
        [SerializeField] private float _exitWaitDuration =  5f;

        [Header("Görsel")]
        [SerializeField] private Renderer[]  _bodyRenderers;
        [SerializeField] private Color[]     _colorOptions;
        [SerializeField] private GameObject  _brakeLightObject;
        [SerializeField] private AudioSource _engineAudio;
        [SerializeField] private AudioClip   _hornClip;

        [Header("Egzoz")]
        [SerializeField] private ParticleSystem _exhaustParticle;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<int> CurrentState = new NetworkVariable<int>(
            (int)VehicleState.OnHighway,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> PassengerCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private NavMeshAgent     _agent;
        private Vector3          _parkingSpot;
        private Vector3          _exitSpot;
        private Vector3          _despawnPoint;
        private Coroutine        _stateCoroutine;
        private List<CustomerAI> _spawnedPassengers = new();

        // ─── Public API ──────────────────────────────────────────
        public VehicleType  Type      => _vehicleType;
        public Transform    ExitPoint => _exitPoint;
        public VehicleState State     => (VehicleState)CurrentState.Value;

        // ─── Initialize ──────────────────────────────────────────
        public void Initialize(Vector3 spawnPoint, Vector3 parkingSpot,
                               Vector3 exitSpot,   Vector3 despawnPoint,
                               int passengerCount,  VehicleType vehicleType = VehicleType.Car)
        {
            if (!IsServer) return;

            transform.position   = spawnPoint;
            _vehicleType         = vehicleType;
            _parkingSpot         = parkingSpot;
            _exitSpot            = exitSpot;
            _despawnPoint        = despawnPoint;
            PassengerCount.Value = Mathf.Clamp(passengerCount, 1,
                _passengerSeats != null ? _passengerSeats.Length : 4);

            ApplyRandomColorClientRpc(Random.Range(0, _colorOptions.Length));

            if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
            _stateCoroutine = StartCoroutine(DriveRoutine());
        }

        public void InitializePassthrough(Vector3 from, Vector3 to)
        {
            if (!IsServer) return;

            transform.position = from;
            _despawnPoint      = to;
            _agent.speed       = _driveSpeed;
            _agent.SetDestination(to);

            CurrentState.Value = (int)VehicleState.OnHighway;
            StartCoroutine(PassthroughRoutine());
        }

        public void OnPassengerBoarded()
        {
            if (!IsServer) return;

            PassengerCount.Value--;

            if (PassengerCount.Value <= 0)
                StartCoroutine(ExitRoutine());
        }

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            _agent                  = GetComponent<NavMeshAgent>();
            _agent.stoppingDistance = _stoppingDist;
        }

        public override void OnNetworkSpawn()
        {
            CurrentState.OnValueChanged   += OnStateChanged;
            PassengerCount.OnValueChanged += OnPassengerCountChanged;

            _exhaustParticle?.Play();
            _engineAudio?.Play();
        }

        public override void OnNetworkDespawn()
        {
            CurrentState.OnValueChanged   -= OnStateChanged;
            PassengerCount.OnValueChanged -= OnPassengerCountChanged;
        }

        // ─── Sürüş Rutinleri ─────────────────────────────────────
        private IEnumerator DriveRoutine()
        {
            CurrentState.Value = (int)VehicleState.EnteringLot;
            _agent.speed       = _driveSpeed;
            _agent.SetDestination(_parkingSpot);

            yield return WaitForArrival(_parkingSpot);

            CurrentState.Value = (int)VehicleState.Parking;
            _agent.speed       = _parkSpeed;

            yield return new WaitForSeconds(0.5f);

            CurrentState.Value = (int)VehicleState.Parked;
            _agent.isStopped   = true;

            SpawnPassengersServerRpc();

            yield return new WaitForSeconds(_parkDuration);

            if (State == VehicleState.Parked)
                StartCoroutine(ExitRoutine());
        }

        private IEnumerator ExitRoutine()
        {
            yield return new WaitForSeconds(_exitWaitDuration);

            CurrentState.Value = (int)VehicleState.Exiting;
            _agent.isStopped   = false;
            _agent.speed       = _driveSpeed;
            _agent.SetDestination(_exitSpot);

            yield return WaitForArrival(_exitSpot);

            _agent.SetDestination(_despawnPoint);
            CurrentState.Value = (int)VehicleState.Despawning;

            yield return WaitForArrival(_despawnPoint);

            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn();
        }

        private IEnumerator PassthroughRoutine()
        {
            yield return WaitForArrival(_despawnPoint);

            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn();
        }

        private IEnumerator WaitForArrival(Vector3 target, float timeout = 30f)
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

        // ─── NPC Spawn ───────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void SpawnPassengersServerRpc()
        {
            CustomerSpawner spawner = GetComponent<CustomerSpawner>();
            if (spawner == null) spawner = gameObject.AddComponent<CustomerSpawner>();

            spawner.SpawnPassengers(this, PassengerCount.Value);
        }

        // ─── State Callbacks ─────────────────────────────────────
        private void OnStateChanged(int oldVal, int newVal)
        {
            VehicleState state = (VehicleState)newVal;

            if (_brakeLightObject != null)
                _brakeLightObject.SetActive(
                    state == VehicleState.Parking ||
                    state == VehicleState.Parked);

            if (_engineAudio != null)
                _engineAudio.pitch = state == VehicleState.Parked ? 0.5f : 1f;

            if (_exhaustParticle != null)
            {
                var emission = _exhaustParticle.emission;
                emission.rateOverTime = state == VehicleState.Parked ? 2f : 8f;
            }
        }

        private void OnPassengerCountChanged(int oldVal, int newVal)
        {
            if (newVal <= 0 && State == VehicleState.Parked)
                StartCoroutine(ExitRoutine());
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void ApplyRandomColorClientRpc(int colorIndex)
        {
            if (_colorOptions == null || _colorOptions.Length == 0) return;
            Color color = _colorOptions[Mathf.Clamp(colorIndex, 0, _colorOptions.Length - 1)];

            foreach (var rend in _bodyRenderers)
                if (rend != null) rend.material.color = color;
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void PlayHornClientRpc()
        {
            if (_engineAudio != null && _hornClip != null)
                _engineAudio.PlayOneShot(_hornClip);
        }

        // ─── Gizmos ──────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(_parkingSpot, 0.3f);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(_despawnPoint, 0.3f);

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(_exitSpot, 0.3f);

            if (_passengerSeats != null)
            {
                Gizmos.color = Color.cyan;
                foreach (var seat in _passengerSeats)
                    if (seat != null) Gizmos.DrawSphere(seat.position, 0.15f);
            }
        }
#endif
    }
}