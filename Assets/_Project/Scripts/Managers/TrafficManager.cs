using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using WeBussedUp.Core.Managers;
using WeBussedUp.NPC;

namespace WeBussedUp.NPC
{
    /// <summary>
    /// Otobandaki trafiği ve tesise giren araçları yönetir. Server yetkili.
    /// Zaman dilimine göre trafik yoğunluğu değişir.
    /// VehicleAI spawn eder — CustomerSpawner üzerinden NPC çıkarır.
    /// </summary>
    public class TrafficManager : NetworkBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────
        public static TrafficManager Instance { get; private set; }

        // ─── Inspector ───────────────────────────────────────────
        [Header("Spawn Noktaları")]
        [SerializeField] private Transform _highwaySpawnPoint;
        [SerializeField] private Transform _highwayDespawnPoint;
        [SerializeField] private Transform _parkingEntrance;
        [SerializeField] private Transform _parkingExit;

        [Header("Araç Prefabları")]
        [SerializeField] private GameObject[] _carPrefabs;
        [SerializeField] private GameObject[] _busPrefabs;
        [SerializeField] private GameObject[] _truckPrefabs;

        [Header("Trafik Ayarları")]
        [SerializeField] private float _baseSpawnInterval    = 5f;
        [SerializeField] private float _nightSpawnMultiplier = 3f;
        [SerializeField] private float _peakSpawnMultiplier  = 0.5f;

        [Header("Müşteri Olasılıkları")]
        [SerializeField, Range(0f, 100f)] private float _baseEntranceProbability = 20f;
        [SerializeField, Range(0f, 100f)] private float _busProbability          = 10f;
        [SerializeField, Range(0f, 100f)] private float _truckProbability        =  5f;

        [Header("Yolcu Sayısı")]
        [SerializeField] private int _minCarPassengers = 1;
        [SerializeField] private int _maxCarPassengers = 4;
        [SerializeField] private int _minBusPassengers = 5;
        [SerializeField] private int _maxBusPassengers = 15;

        [Header("Yoğun Saat Dilimleri")]
        [SerializeField] private Vector2 _morningPeak = new Vector2(7f,  9f);
        [SerializeField] private Vector2 _eveningPeak = new Vector2(17f, 19f);
        [SerializeField] private Vector2 _quietHours  = new Vector2(2f,  5f);

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<float> EntranceProbability = new NetworkVariable<float>(
            20f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> TotalCustomersToday = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private float _spawnTimer;
        private float _currentSpawnInterval;

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            EntranceProbability.Value = _baseEntranceProbability;
            _currentSpawnInterval     = _baseSpawnInterval;
        }

        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (!IsServer) return;

            _spawnTimer += Time.deltaTime;

            if (_spawnTimer >= _currentSpawnInterval)
            {
                _spawnTimer = 0f;
                UpdateSpawnInterval();
                ProcessTraffic();
            }
        }

        // ─── Trafik Hesabı ───────────────────────────────────────
        private void UpdateSpawnInterval()
        {
            float time = TimeManager.Instance?.CurrentTime ?? 8f;

            if (IsInRange(time, _quietHours))
                _currentSpawnInterval = _baseSpawnInterval * _nightSpawnMultiplier;
            else if (IsInRange(time, _morningPeak) || IsInRange(time, _eveningPeak))
                _currentSpawnInterval = _baseSpawnInterval * _peakSpawnMultiplier;
            else
                _currentSpawnInterval = _baseSpawnInterval;
        }

        private void ProcessTraffic()
        {
            float time = TimeManager.Instance?.CurrentTime ?? 8f;

            // Sakin saatlerde %70 ihtimalle spawn etme
            if (IsInRange(time, _quietHours) && Random.value > 0.3f) return;

            VehicleType type      = DetermineVehicleType();
            bool        willEnter = Random.Range(0f, 100f) <= EntranceProbability.Value;

            if (willEnter)
                SpawnCustomerVehicle(type);
            else
                SpawnPassingVehicle(type);
        }

        private VehicleType DetermineVehicleType()
        {
            float roll = Random.Range(0f, 100f);
            if (roll < _truckProbability) return VehicleType.Truck;
            if (roll < _busProbability)   return VehicleType.Bus;
            return VehicleType.Car;
        }

        // ─── Spawn ───────────────────────────────────────────────
        private void SpawnCustomerVehicle(VehicleType type)
        {
            GameObject prefab = GetRandomPrefab(type);

            if (prefab == null || _highwaySpawnPoint == null || _parkingEntrance == null)
            {
                Debug.Log($"[TrafficManager] Müşteri aracı ({type}) — prefab veya nokta atanmamış.");
                TotalCustomersToday.Value++;
                return;
            }

            GameObject vehicle = Instantiate(
                prefab,
                _highwaySpawnPoint.position,
                _highwaySpawnPoint.rotation
            );

            if (!vehicle.TryGetComponent(out NetworkObject netObj))
            {
                Destroy(vehicle);
                return;
            }

            netObj.Spawn();

            if (vehicle.TryGetComponent(out VehicleAI vehicleAI))
            {
                int passengerCount = type switch
                {
                    VehicleType.Bus => Random.Range(_minBusPassengers, _maxBusPassengers + 1),
                    VehicleType.Car => Random.Range(_minCarPassengers, _maxCarPassengers + 1),
                    _               => 1
                };

                vehicleAI.Initialize(
                    _highwaySpawnPoint.position,
                    _parkingEntrance.position,
                    _parkingExit != null ? _parkingExit.position : _highwaySpawnPoint.position,
                    _highwayDespawnPoint != null ? _highwayDespawnPoint.position : Vector3.zero,
                    passengerCount
                );
            }

            TotalCustomersToday.Value++;
            Debug.Log($"[TrafficManager] {type} tesise giriyor. Bugünkü müşteri: {TotalCustomersToday.Value}");
        }

        private void SpawnPassingVehicle(VehicleType type)
        {
            GameObject prefab = GetRandomPrefab(type);

            if (prefab == null || _highwaySpawnPoint == null)
            {
                Debug.Log($"[TrafficManager] {type} transit geçti.");
                return;
            }

            GameObject vehicle = Instantiate(
                prefab,
                _highwaySpawnPoint.position,
                _highwaySpawnPoint.rotation
            );

            if (!vehicle.TryGetComponent(out NetworkObject netObj))
            {
                Destroy(vehicle);
                return;
            }

            netObj.Spawn();

            if (vehicle.TryGetComponent(out VehicleAI vehicleAI))
            {
                vehicleAI.InitializePassthrough(
                    _highwaySpawnPoint.position,
                    _highwayDespawnPoint != null ? _highwayDespawnPoint.position : Vector3.zero
                );
            }
        }

        private GameObject GetRandomPrefab(VehicleType type)
        {
            GameObject[] pool = type switch
            {
                VehicleType.Bus   => _busPrefabs,
                VehicleType.Truck => _truckPrefabs,
                _                 => _carPrefabs
            };

            if (pool == null || pool.Length == 0) return null;
            return pool[Random.Range(0, pool.Length)];
        }

        // ─── Public API ──────────────────────────────────────────
        [Rpc(SendTo.Server)]
        public void IncreasePopularityServerRpc(float amount)
        {
            EntranceProbability.Value = Mathf.Clamp(
                EntranceProbability.Value + amount, 0f, 100f);

            Debug.Log($"[TrafficManager] Popülerlik → %{EntranceProbability.Value:F1}");
        }

        public void ResetDailyStats()
        {
            if (!IsServer) return;
            TotalCustomersToday.Value = 0;
        }

        // ─── Util ────────────────────────────────────────────────
        private bool IsInRange(float time, Vector2 range) =>
            time >= range.x && time < range.y;

        // ─── Gizmos ──────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_highwaySpawnPoint != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(_highwaySpawnPoint.position, 0.3f);
                UnityEditor.Handles.Label(_highwaySpawnPoint.position + Vector3.up, "Spawn");
            }

            if (_parkingEntrance != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(_parkingEntrance.position, 0.3f);
                UnityEditor.Handles.Label(_parkingEntrance.position + Vector3.up, "Park Giriş");
            }

            if (_parkingExit != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(_parkingExit.position, 0.3f);
                UnityEditor.Handles.Label(_parkingExit.position + Vector3.up, "Park Çıkış");
            }

            if (_highwayDespawnPoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(_highwayDespawnPoint.position, 0.3f);
                UnityEditor.Handles.Label(_highwayDespawnPoint.position + Vector3.up, "Despawn");
            }
        }
#endif
    }
}