using UnityEngine;
using Unity.Netcode;
using System.Collections;

namespace WeBussedUp.Gameplay.Ragdoll
{
    /// <summary>
    /// Oyuncu ve NPC ragdoll sistemi.
    /// SlipHazard, patlama, itme gibi dış kuvvetler TriggerRagdoll() ile tetikler.
    /// Belirli süre sonra karakter ayağa kalkar.
    /// </summary>
    public class RagdollController : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Ragdoll Ayarları")]
        [SerializeField] private float _ragdollDuration  = 2.5f;  // Kaç saniye yerde kalır
        [SerializeField] private float _getUpDuration    = 1.0f;  // Ayağa kalkma animasyon süresi
        [SerializeField] private float _minForce         = 1f;    // Altındaki kuvvetler ragdoll tetiklemez

        [Header("Bileşenler")]
        [SerializeField] private Animator          _animator;
        [SerializeField] private CharacterController _characterController;

        // Ragdoll kemikleri — Inspector'da otomatik doldurulur
        [SerializeField] private Rigidbody[] _ragdollBodies;
        [SerializeField] private Collider[]  _ragdollColliders;

        // Ana collider ve rigidbody (karakter hareketi için)
        [SerializeField] private Collider  _mainCollider;
        [SerializeField] private Rigidbody _mainRigidbody;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<bool> IsRagdollingVar = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private bool      _isRagdolling = false;
        private Coroutine _ragdollCoroutine;

        public bool IsRagdolling => _isRagdolling;

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            IsRagdollingVar.OnValueChanged += OnRagdollStateChanged;
            SetRagdollEnabled(false); // Başlangıçta kapalı
        }

        public override void OnNetworkDespawn()
        {
            IsRagdollingVar.OnValueChanged -= OnRagdollStateChanged;
        }

        // ─── Public API ──────────────────────────────────────────
        /// <summary>
        /// SlipHazard, patlama vb. dış sistemler bu metodu çağırır.
        /// </summary>
        public void TriggerRagdoll(Vector3 force)
        {
            if (!IsServer)              return;
            if (_isRagdolling)          return;
            if (force.magnitude < _minForce) return;

            TriggerRagdollServerRpc(force);
        }

        // ─── Server RPC ──────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void TriggerRagdollServerRpc(Vector3 force)
        {
            if (_isRagdolling) return;

            IsRagdollingVar.Value = true;

            if (_ragdollCoroutine != null)
                StopCoroutine(_ragdollCoroutine);

            _ragdollCoroutine = StartCoroutine(RagdollRoutine(force));
        }

        // ─── Ragdoll Routine ─────────────────────────────────────
        private IEnumerator RagdollRoutine(Vector3 force)
        {
            // 1. Ragdoll'u aç
            EnableRagdollClientRpc(force);

            // 2. Süre bekle
            yield return new WaitForSeconds(_ragdollDuration);

            // 3. Ayağa kalk
            GetUpClientRpc();

            yield return new WaitForSeconds(_getUpDuration);

            // 4. Normal harekete dön
            IsRagdollingVar.Value = false;
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void EnableRagdollClientRpc(Vector3 force)
        {
            SetRagdollEnabled(true);

            // Kuvveti uygula — hangi kemiğe en yakınsa ona vur
            if (_ragdollBodies != null && _ragdollBodies.Length > 0)
            {
                // Pelvis veya merkeze en yakın kemiği bul
                Rigidbody target = FindNearestBone(transform.position);
                target?.AddForce(force, ForceMode.Impulse);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GetUpClientRpc()
        {
            SetRagdollEnabled(false);

            // Ayağa kalkma animasyonu
            _animator?.SetTrigger("GetUp");
        }

        // ─── Ragdoll Aktif/Pasif ─────────────────────────────────
        private void SetRagdollEnabled(bool enabled)
        {
            _isRagdolling = enabled;

            // Animator
            if (_animator != null) _animator.enabled = !enabled;

            // CharacterController
            if (_characterController != null) _characterController.enabled = !enabled;

            // Ana collider ve rigidbody
            if (_mainCollider  != null) _mainCollider.enabled      = !enabled;
            if (_mainRigidbody != null) _mainRigidbody.isKinematic =  enabled;

            // Ragdoll kemikleri
            if (_ragdollBodies != null)
                foreach (var rb in _ragdollBodies)
                    if (rb != null) rb.isKinematic = !enabled;

            if (_ragdollColliders != null)
                foreach (var col in _ragdollColliders)
                    if (col != null) col.enabled = enabled;
        }

        private Rigidbody FindNearestBone(Vector3 point)
        {
            Rigidbody nearest = null;
            float     minDist = float.MaxValue;

            foreach (var rb in _ragdollBodies)
            {
                if (rb == null) continue;
                float dist = Vector3.Distance(rb.position, point);
                if (dist < minDist) { minDist = dist; nearest = rb; }
            }

            return nearest;
        }

        // ─── State Callback ──────────────────────────────────────
        private void OnRagdollStateChanged(bool oldVal, bool newVal)
        {
            _isRagdolling = newVal;
        }

        // ─── Editor Yardımcı ─────────────────────────────────────
#if UNITY_EDITOR
        [ContextMenu("Ragdoll Kemiklerini Otomatik Doldur")]
        private void AutoFillRagdollBones()
        {
            _ragdollBodies    = GetComponentsInChildren<Rigidbody>();
            _ragdollColliders = GetComponentsInChildren<Collider>();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}