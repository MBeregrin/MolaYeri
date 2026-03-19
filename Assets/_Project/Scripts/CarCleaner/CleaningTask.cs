using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;
using WeBussedUp.Interfaces;
using WeBussedUp.Stations.CarWash;
using WeBussedUp.Core.Managers;
using WeBussedUp.UI;

namespace WeBussedUp.Gameplay
{
    /// <summary>
    /// Temizleme görevi bileşeni. WC, araba yıkama sonrası köpük, dökülen içecek vb.
    /// Oyuncu E ile etkileşime geçer → her basışta temizlik ilerler → tamamlanınca kaybolur.
    /// SlipHazard varsa CleanServerRpc ile bildirir — memnuniyet bonusu verir.
    /// </summary>
    public class CleaningTask : NetworkBehaviour, IInteractable
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Temizlik Ayarları")]
        [SerializeField] private float _cleanAmountPerAction = 0.25f; // Her E basışında %25
        [SerializeField] private float _satisfactionBonus    = 10f;   // Memnuniyet artışı
        [SerializeField] private int   _actionsRequired      = 4;     // Kaç aksiyonda temiz

        [Header("Görsel")]
        [SerializeField] private GameObject  _dirtyVisual;   // Kirli görsel (leke, köpük vb.)
        [SerializeField] private ParticleSystem _cleanParticle;
        [SerializeField] private AudioSource    _cleanAudio;

        [Header("Bağlı SlipHazard")]
        [Tooltip("Bu temizlik görevi bir SlipHazard'a bağlıysa buraya sürükle")]
        [SerializeField] private SlipHazard _linkedSlipHazard;

        [Header("Olaylar")]
        public UnityEvent OnCleaningStarted;
        public UnityEvent OnCleaningCompleted;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<int> ActionsCompleted = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsCompleted = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private bool _cleaningStarted = false;

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            ActionsCompleted.OnValueChanged += OnActionsChanged;
            IsCompleted.OnValueChanged      += OnCompletedChanged;

            UpdateVisual(ActionsCompleted.Value);
        }

        public override void OnNetworkDespawn()
        {
            ActionsCompleted.OnValueChanged -= OnActionsChanged;
            IsCompleted.OnValueChanged      -= OnCompletedChanged;
        }

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            if (IsCompleted.Value) return string.Empty;

            int remaining = _actionsRequired - ActionsCompleted.Value;
            return $"Temizle ({remaining} hareket kaldı) [E]";
        }

        public bool CanInteract(ulong playerId) => !IsCompleted.Value;

        public InteractionType GetInteractionType() => InteractionType.Clean;

        public void Interact(ulong playerId)
        {
            if (!IsSpawned || IsCompleted.Value) return;
            PerformCleanActionServerRpc(playerId);
        }

        // ─── Server RPC ──────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void PerformCleanActionServerRpc(ulong playerId)
        {
            if (IsCompleted.Value) return;

            ActionsCompleted.Value++;

            // SlipHazard'a bildir
            if (_linkedSlipHazard != null)
                _linkedSlipHazard.CleanServerRpc(_cleanAmountPerAction, playerId);

            // Tamamlandı mı?
            if (ActionsCompleted.Value >= _actionsRequired)
            {
                IsCompleted.Value = true;
                AwardBonusClientRpc(playerId);
                StartCoroutine(DespawnAfterDelay(1.5f));
            }
            else
            {
                PlayCleanEffectClientRpc();
            }
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void PlayCleanEffectClientRpc()
        {
            _cleanParticle?.Play();
            _cleanAudio?.PlayOneShot(_cleanAudio.clip);

            if (!_cleaningStarted)
            {
                _cleaningStarted = true;
                OnCleaningStarted?.Invoke();
            }

            UpdateVisual(ActionsCompleted.Value);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AwardBonusClientRpc(ulong cleanerId)
        {
            _cleanParticle?.Play();
            _cleanAudio?.PlayOneShot(_cleanAudio.clip);
            OnCleaningCompleted?.Invoke();

            // Sadece temizleyen oyuncuya bildirim
            if (NetworkManager.Singleton.LocalClientId == cleanerId)
            {
                UIManager.Instance?.ShowNotification(
                    $"Temizlendi! +{_satisfactionBonus} Memnuniyet 🧹", Color.green);

                // Memnuniyet artışını UIManager'a ilet
                // RatingManager ileride bağlanacak
            }
        }

        // ─── Görsel ──────────────────────────────────────────────
        private void UpdateVisual(int actionsCompleted)
        {
            if (_dirtyVisual == null) return;

            // Temizlik ilerledikçe görsel solar
            float progress = (float)actionsCompleted / _actionsRequired;
            Renderer rend  = _dirtyVisual.GetComponent<Renderer>();

            if (rend != null)
            {
                Color c = rend.material.color;
                c.a     = 1f - progress;
                rend.material.color = c;
            }
        }

        private void OnActionsChanged(int oldVal, int newVal)
        {
            UpdateVisual(newVal);
        }

        private void OnCompletedChanged(bool oldVal, bool newVal)
        {
            if (newVal && _dirtyVisual != null)
                _dirtyVisual.SetActive(false);
        }

        private System.Collections.IEnumerator DespawnAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn();
        }

        // ─── Gizmos ──────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
#endif
    }
}