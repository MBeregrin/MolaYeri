using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;
using WeBussedUp.Core.Managers;
using WeBussedUp.UI;

namespace WeBussedUp.Gameplay
{
    /// <summary>
    /// Temizlenebilir kirli yüzey — duvar lekesi, zemin kiri, dökülen sıvı vb.
    /// CleaningTool raycast'i bu bileşene çarpar → CleanServerRpc çağrılır.
    /// CleaningTask'tan farkı: görev değil, sürekli temizlenebilen statik yüzey.
    /// </summary>
    public class DirtSurface : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Kir Ayarları")]
        [SerializeField] private float _maxDirt          = 1f;   // Maksimum kir seviyesi
        [SerializeField] private float _cleanThreshold   = 0.05f; // Bu altında tamamen temiz

        [Header("Görsel")]
        [SerializeField] private Renderer    _dirtRenderer;  // Kir görseli
        [SerializeField] private string      _dirtProperty   = "_DirtAmount"; // Shader property
        [SerializeField] private Color       _cleanColor     = Color.white;
        [SerializeField] private Color       _dirtyColor     = new Color(0.4f, 0.3f, 0.2f);

        [Header("Efektler")]
        [SerializeField] private ParticleSystem _cleanParticle;
        [SerializeField] private AudioSource    _cleanAudio;

        [Header("Olaylar")]
        public UnityEvent OnFullyCleaned;
        public UnityEvent OnDirtAdded;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<float> DirtLevel = new NetworkVariable<float>(
            1f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            DirtLevel.OnValueChanged += OnDirtLevelChanged;
            UpdateVisual(DirtLevel.Value);
        }

        public override void OnNetworkDespawn()
        {
            DirtLevel.OnValueChanged -= OnDirtLevelChanged;
        }

        // ─── Public API ──────────────────────────────────────────
        public bool IsClean => DirtLevel.Value <= _cleanThreshold;

        /// <summary>
        /// CleaningTool tarafından çağrılır.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void CleanServerRpc(float amount, ulong cleanerId)
        {
            if (IsClean) return;

            DirtLevel.Value = Mathf.Max(0f, DirtLevel.Value - amount);

            if (IsClean)
            {
                // Temizlik bonusu — RatingManager'a bildir
                RatingManager.Instance?.AddCleanlinessBoostServerRpc(0.1f);
                NotifyCleanedClientRpc(cleanerId);
            }
        }

        /// <summary>
        /// Yeni kir ekle — WashStation köpük döktüğünde vb.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void AddDirtServerRpc(float amount)
        {
            DirtLevel.Value = Mathf.Min(_maxDirt, DirtLevel.Value + amount);
            OnDirtAdded?.Invoke();
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyCleanedClientRpc(ulong cleanerId)
        {
            _cleanParticle?.Play();
            _cleanAudio?.PlayOneShot(_cleanAudio.clip);
            OnFullyCleaned?.Invoke();

            if (NetworkManager.Singleton.LocalClientId == cleanerId)
                UIManager.Instance?.ShowNotification("Yüzey temizlendi! 🧹", Color.green);
        }

        // ─── Görsel ──────────────────────────────────────────────
        private void OnDirtLevelChanged(float oldVal, float newVal)
        {
            UpdateVisual(newVal);
        }

        private void UpdateVisual(float dirtLevel)
        {
            if (_dirtRenderer == null) return;

            float t = dirtLevel / _maxDirt;

            // Shader property varsa kullan
            if (_dirtRenderer.material.HasProperty(_dirtProperty))
            {
                _dirtRenderer.material.SetFloat(_dirtProperty, t);
            }
            else
            {
                // Fallback: renk interpolasyonu
                _dirtRenderer.material.color = Color.Lerp(_cleanColor, _dirtyColor, t);
            }

            // Tamamen temizlendiyse renderer'ı kapat
            if (dirtLevel <= _cleanThreshold)
                _dirtRenderer.gameObject.SetActive(false);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.4f, 0.2f, 0.5f);
            Gizmos.DrawCube(transform.position, Vector3.one * 0.3f);
        }
#endif
    }
}
