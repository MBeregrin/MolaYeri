using UnityEngine;
using Unity.Netcode;
using System.Collections;
using WeBussedUp.UI;
using WeBussedUp.Gameplay.Ragdoll;

namespace WeBussedUp.Stations.CarWash
{
    /// <summary>
    /// Araba/otobüs yıkama sırasında yere dökülen köpük alanı.
    /// Oyuncu veya NPC üzerinden geçerse ragdoll tetiklenir.
    /// Belirli süre sonra köpük kurur ve tehlike kalkar.
    /// Temizlik görevi tamamlanınca (CleaningTask) alan temizlenir.
    /// </summary>
    public class SlipHazard : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Köpük Ayarları")]
        [SerializeField] private float _lifetime         = 30f;  // Köpük kaç saniye kalır
        [SerializeField] private float _slipRadius       = 1.2f; // Tehlike alanı yarıçapı
        [SerializeField] private float _cleanThreshold   = 0.3f; // Bu kadar temizlenince yok olur

        [Header("Görsel")]
        [SerializeField] private ParticleSystem _foamParticle;   // Köpük efekti
        [SerializeField] private GameObject     _foamDecal;      // Zemin decal'i
        [SerializeField] private Renderer       _foamRenderer;
        [SerializeField] private AnimationCurve _fadeCurve;      // Kuruma animasyonu

        [Header("Ses")]
        [SerializeField] private AudioSource _slipSound;
        [SerializeField] private AudioSource _cleanSound;

        [Header("Algılama")]
        [SerializeField] private LayerMask _playerLayer;
        [SerializeField] private LayerMask _npcLayer;
        [SerializeField] private float     _checkInterval = 0.1f; // Kaç saniyede bir kontrol

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<float> Cleanliness = new NetworkVariable<float>(
            0f,   // 0 = tamamen kirli/köpüklü, 1 = temiz
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsActive = new NetworkVariable<bool>(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private float _lifetimeTimer  = 0f;
        private float _checkTimer     = 0f;
        

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            Cleanliness.OnValueChanged += OnCleanlinessChanged;
            IsActive.OnValueChanged    += OnActiveChanged;

            if (IsServer)
                StartCoroutine(LifetimeRoutine());

            // Köpük efektini başlat
            SpawnFoamClientRpc();
        }

        public override void OnNetworkDespawn()
        {
            Cleanliness.OnValueChanged -= OnCleanlinessChanged;
            IsActive.OnValueChanged    -= OnActiveChanged;
        }

        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (!IsServer || !IsActive.Value) return;

            _checkTimer += Time.deltaTime;
            if (_checkTimer >= _checkInterval)
            {
                _checkTimer = 0f;
                CheckForVictims();
            }
        }

        // ─── Kuruma ──────────────────────────────────────────────
        private IEnumerator LifetimeRoutine()
        {
            while (_lifetimeTimer < _lifetime && IsActive.Value)
            {
                _lifetimeTimer += Time.deltaTime;

                // Zamanla köpük azalır — ama tamamen temizlenmez
                float naturalClean = _lifetimeTimer / _lifetime * 0.5f; // Max %50 doğal kuruma
                if (Cleanliness.Value < naturalClean)
                    Cleanliness.Value = naturalClean;

                yield return null;
            }

            // Süre doldu — temizlenmemişse yine de yok ol
            if (IsActive.Value)
                DeactivateServerRpc();
        }

        // ─── Kurban Algılama ─────────────────────────────────────
        private void CheckForVictims()
        {
            // Oyuncuları kontrol et
            Collider[] playerHits = Physics.OverlapSphere(
                transform.position, _slipRadius, _playerLayer);

            foreach (var hit in playerHits)
            {
                if (hit.TryGetComponent(out RagdollController ragdoll))
                    TriggerSlip(ragdoll, isPlayer: true);
            }

            // NPC'leri kontrol et
            Collider[] npcHits = Physics.OverlapSphere(
                transform.position, _slipRadius, _npcLayer);

            foreach (var hit in npcHits)
            {
                if (hit.TryGetComponent(out RagdollController ragdoll))
                    TriggerSlip(ragdoll, isPlayer: false);
            }
        }

        private void TriggerSlip(RagdollController ragdoll, bool isPlayer)
        {
            if (ragdoll == null || ragdoll.IsRagdolling) return;

            // Kayma yönü — köpükten uzağa doğru
            Vector3 slipDirection = (ragdoll.transform.position - transform.position).normalized;
            slipDirection.y = 0.3f; // Hafif yukarı fırlatma

            ragdoll.TriggerRagdoll(slipDirection * 4f);

            // Ses ve UI
            PlaySlipSoundClientRpc();

            if (isPlayer)
                UIManager.Instance?.ShowNotification("Kaydın! 🧼", Color.yellow);
        }

        // ─── Temizleme (CleaningTask tarafından çağrılır) ────────
        /// <summary>
        /// CleaningTask her temizlik aksiyonunda bu metodu çağırır.
        /// amount: 0-1 arası temizlik miktarı
        /// </summary>
        [Rpc(SendTo.Server)]
        public void CleanServerRpc(float amount, ulong cleanerId)
        {
            if (!IsActive.Value) return;

            Cleanliness.Value += amount;

            if (Cleanliness.Value >= _cleanThreshold)
            {
                // Yeterince temizlendi
                AwardCleaningBonusClientRpc(cleanerId);
                DeactivateServerRpc();
            }
        }

        [Rpc(SendTo.Server)]
        private void DeactivateServerRpc()
        {
            if (!IsActive.Value) return;
            IsActive.Value = false;

            StartCoroutine(DespawnAfterDelay(1.5f));
        }

        private IEnumerator DespawnAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn();
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void SpawnFoamClientRpc()
        {
            _foamParticle?.Play();
            if (_foamDecal != null) _foamDecal.SetActive(true);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlaySlipSoundClientRpc()
        {
            _slipSound?.PlayOneShot(_slipSound.clip);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AwardCleaningBonusClientRpc(ulong cleanerId)
        {
            if (Unity.Netcode.NetworkManager.Singleton.LocalClientId != cleanerId) return;

            _cleanSound?.Play();
            UIManager.Instance?.ShowNotification("Temizlendi! +Memnuniyet 🧹", Color.green);
        }

        // ─── Görsel Callbacks ────────────────────────────────────
        private void OnCleanlinessChanged(float oldVal, float newVal)
        {
            if (_foamRenderer == null) return;

            // Köpük görselini temizliğe göre soldur
            float alpha = 1f - newVal;
            Color c     = _foamRenderer.material.color;
            c.a         = _fadeCurve != null ? _fadeCurve.Evaluate(alpha) : alpha;
            _foamRenderer.material.color = c;
        }

        private void OnActiveChanged(bool oldVal, bool newVal)
        {
            if (newVal) return;

            // Deaktif — efektleri durdur
            _foamParticle?.Stop();
            if (_foamDecal != null) _foamDecal.SetActive(false);
        }

        // ─── Gizmos ──────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.3f);
            Gizmos.DrawSphere(transform.position, _slipRadius);
        }
#endif
    }
}