using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;
using System.Collections.Generic;

namespace WeBussedUp.Gameplay.Building
{
    /// <summary>
    /// Yerleştirilen objenin geçerli bir yüzey üzerinde durup durmadığını kontrol eder.
    /// Server yetkili — sonuç NetworkVariable ile tüm clientlara yayılır.
    /// BuildingSystem.PlaceObjectServerRpc → CanPlace() ile yerleşim öncesi doğrulama yapar.
    /// </summary>
    public class PlacementDependency : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Yüzey Gereksinimleri")]
        [Tooltip("Hangi tag'li yüzeyler geçerli? (Örn: ShelfSurface, Table, Counter)")]
        [SerializeField] private List<string> _allowedSurfaceTags = new List<string>();

        [Tooltip("Raycast başlangıç noktası offset'i (obje merkezine göre)")]
        [SerializeField] private Vector3 _checkOffset = new Vector3(0f, 0.05f, 0f);

        [Tooltip("Aşağı raycast mesafesi — obje yüksekliğine göre ayarla")]
        [SerializeField] private float _checkDistance = 0.3f;

        [Tooltip("Kaç saniyede bir kontrol yapılsın? (0 = sadece yerleşimde bir kez)")]
        [SerializeField] private float _checkInterval = 0f;

        [Header("Olaylar")]
        public UnityEvent OnValidPlacement;
        public UnityEvent OnInvalidPlacement;

        // PlacementDependency içine public getter'lar ekle
public List<string> AllowedSurfaceTags => _allowedSurfaceTags;
public Vector3      CheckOffset        => _checkOffset;
public float        CheckDistance      => _checkDistance;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<bool> IsPlacedCorrectly = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private float _nextCheckTime = 0f;
        private bool  _initialCheckDone = false;

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            IsPlacedCorrectly.OnValueChanged += HandlePlacementChanged;

            // Spawn anında mevcut durumu uygula
            ApplyPlacementState(IsPlacedCorrectly.Value);

            // Server: ilk kontrolü hemen yap
            if (IsServer) RunCheck();
        }

        public override void OnNetworkDespawn()
        {
            IsPlacedCorrectly.OnValueChanged -= HandlePlacementChanged;
        }

        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (!IsServer)             return;
            if (_checkInterval <= 0f)  return; // Sadece spawn'da kontrol — her frame değil
            if (Time.time < _nextCheckTime) return;

            _nextCheckTime = Time.time + _checkInterval;
            RunCheck();
        }

        // ─── Public API ──────────────────────────────────────────
        /// <summary>
        /// BuildingSystem.PlaceObjectServerRpc tarafından çağrılır.
        /// Spawn öncesi pozisyon bazlı ön doğrulama.
        /// </summary>
        public static bool CanPlace(Vector3 position, Vector3 checkOffset, float checkDistance, List<string> allowedTags)
        {
            Vector3 origin = position + checkOffset;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, checkDistance))
                return false;

            if (allowedTags == null || allowedTags.Count == 0)
                return true; // Tag kısıtı yoksa her yüzey geçerli

            return allowedTags.Contains(hit.collider.tag);
        }

        /// <summary>
        /// Spawn sonrası instance bazlı kontrol — server tarafından çağrılır.
        /// </summary>
        public void ForceCheck()
        {
            if (!IsServer) return;
            RunCheck();
        }

        // ─── Private ─────────────────────────────────────────────
        private void RunCheck()
        {
            bool valid = PerformRaycast(transform.position);

            if (IsPlacedCorrectly.Value == valid && _initialCheckDone) return;

            _initialCheckDone       = true;
            IsPlacedCorrectly.Value = valid;

            Debug.Log($"[PlacementDependency] {gameObject.name} → {(valid ? "GEÇERLİ" : "GEÇERSİZ")}");
        }

        private bool PerformRaycast(Vector3 position)
        {
            Vector3 origin = position + _checkOffset;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _checkDistance))
                return false;

            if (_allowedSurfaceTags == null || _allowedSurfaceTags.Count == 0)
                return true;

            return _allowedSurfaceTags.Contains(hit.collider.tag);
        }

        private void HandlePlacementChanged(bool previous, bool current)
        {
            ApplyPlacementState(current);
        }

        private void ApplyPlacementState(bool isValid)
        {
            if (isValid) OnValidPlacement?.Invoke();
            else         OnInvalidPlacement?.Invoke();
        }

        // ─── Gizmos ──────────────────────────────────────────────
        private void OnDrawGizmosSelected()
        {
            bool isValid = Application.isPlaying && IsPlacedCorrectly.Value;
            Gizmos.color = isValid ? Color.green : Color.red;

            Vector3 origin = transform.position + _checkOffset;
            Gizmos.DrawRay(origin, Vector3.down * _checkDistance);
            Gizmos.DrawWireSphere(origin, 0.03f);
        }
    }
}