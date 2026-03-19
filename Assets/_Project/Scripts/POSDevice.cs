using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;
using System.Collections;
using WeBussedUp.Interfaces;
using WeBussedUp.Core.Managers;
using WeBussedUp.UI;

namespace WeBussedUp.Stations.GasStation
{
    public enum PaymentMethod { Cash, Card, Contactless }

    /// <summary>
    /// POS cihazı — FuelPump, WashStation veya kasa yanına yerleştirilir.
    /// Oyuncu E ile etkileşime geçer → ödeme animasyonu → para EconomyManager'a aktarılır.
    /// Birden fazla istasyona bağlanabilir (array ile).
    /// </summary>
    public class POSDevice : NetworkBehaviour, IInteractable
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("POS Ayarları")]
        [SerializeField] private PaymentMethod _acceptedMethod  = PaymentMethod.Card;
        [SerializeField] private float         _transactionTime = 2f; // Ödeme animasyon süresi

        [Header("Bağlı İstasyonlar")]
        [Tooltip("Bu POS hangi istasyonların ödemesini alır? FuelPump, WashStation vb.")]
        [SerializeField] private MonoBehaviour[] _linkedStations; // IPayable interface'i implemente eder

        [Header("Görsel")]
        [SerializeField] private Renderer         _screenRenderer;
        [SerializeField] private string           _emissionProp   = "_EmissionColor";
        [SerializeField] private Color            _colorIdle      = Color.blue;
        [SerializeField] private Color            _colorProcessing = Color.yellow;
        [SerializeField] private Color            _colorSuccess   = Color.green;
        [SerializeField] private Color            _colorFail      = Color.red;
        [SerializeField] private Animator         _posAnimator;

        [Header("Ses")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip   _beepClip;
        [SerializeField] private AudioClip   _successClip;
        [SerializeField] private AudioClip   _failClip;

        [Header("Olaylar")]
        public UnityEvent<float>         OnPaymentSuccess; // float = tutar
        public UnityEvent                OnPaymentFailed;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<bool> IsProcessing = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<float> PendingAmount = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private ulong     _payingPlayerId = ulong.MaxValue;
        private Coroutine _transactionCoroutine;

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            IsProcessing.OnValueChanged += OnProcessingChanged;
            UpdateScreen(_colorIdle);
        }

        public override void OnNetworkDespawn()
        {
            IsProcessing.OnValueChanged -= OnProcessingChanged;
        }

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            if (IsProcessing.Value)
                return $"İşleniyor... {PendingAmount.Value:F2}₺";

            if (PendingAmount.Value > 0f)
                return $"Ödeme Yap — {PendingAmount.Value:F2}₺ [E]";

            return "POS Cihazı [E]";
        }

        public bool CanInteract(ulong playerId)
        {
            return !IsProcessing.Value;
        }

        public InteractionType GetInteractionType() => InteractionType.Pay;

        public void Interact(ulong playerId)
        {
            if (!IsSpawned || !CanInteract(playerId)) return;
            RequestPaymentServerRpc(playerId);
        }

        // ─── Public API ──────────────────────────────────────────
        /// <summary>
        /// FuelPump veya WashStation ödeme tutarını buraya iletir.
        /// Oyuncu POS'a gelince bu tutar gösterilir.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void SetPendingAmountServerRpc(float amount)
        {
            PendingAmount.Value = amount;
        }

        // ─── Server RPC ──────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void RequestPaymentServerRpc(ulong playerId)
        {
            if (IsProcessing.Value) return;

            float amount = PendingAmount.Value > 0f
                ? PendingAmount.Value
                : CollectAmountFromStations();

            if (amount <= 0f)
            {
                NotifyClientRpc(playerId, "Ödenecek tutar yok.", false);
                return;
            }

            if (EconomyManager.Instance != null &&
                !EconomyManager.Instance.HasEnoughMoney(amount))
            {
                PaymentResultClientRpc(playerId, 0f, false);
                return;
            }

            _payingPlayerId    = playerId;
            IsProcessing.Value = true;

            if (_transactionCoroutine != null) StopCoroutine(_transactionCoroutine);
            _transactionCoroutine = StartCoroutine(ProcessTransaction(playerId, amount));
        }

        // ─── Transaction ─────────────────────────────────────────
        private IEnumerator ProcessTransaction(ulong playerId, float amount)
        {
            ProcessingClientRpc(amount);

            yield return new WaitForSeconds(_transactionTime);

            // Son kontrol — para hâlâ yeterli mi?
            bool success = EconomyManager.Instance == null ||
                           EconomyManager.Instance.HasEnoughMoney(amount);

            if (success)
            {
                EconomyManager.Instance?.AddMoneyServerRpc(amount, TransactionCategory.Sale);
                PendingAmount.Value = 0f;
            }

            IsProcessing.Value = false;
            _payingPlayerId    = ulong.MaxValue;

            PaymentResultClientRpc(playerId, success ? amount : 0f, success);
        }

        /// <summary>
        /// Bağlı istasyonlardan bekleyen tutarı toplar.
        /// Ileride IPayable interface'i ile genişletilir.
        /// </summary>
        private float CollectAmountFromStations()
        {
            float total = 0f;

            foreach (var station in _linkedStations)
            {
                if (station == null) continue;

                // FuelPump kontrolü
                if (station is FuelPump pump)
                    total += pump.CurrentFuel.Value * pump.PricePerLiter.Value;
            }

            return total;
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void ProcessingClientRpc(float amount)
        {
            UpdateScreen(_colorProcessing);
            _posAnimator?.SetTrigger("Process");
            _audioSource?.PlayOneShot(_beepClip);

            UIManager.Instance?.ShowNotification(
                $"Ödeme işleniyor... {amount:F2}₺", Color.yellow);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PaymentResultClientRpc(ulong targetId, float amount, bool success)
        {
            UpdateScreen(success ? _colorSuccess : _colorFail);
            StartCoroutine(ResetScreenAfterDelay(2f));

            if (success)
            {
                _audioSource?.PlayOneShot(_successClip);
                OnPaymentSuccess?.Invoke(amount);

                if (NetworkManager.Singleton.LocalClientId == targetId)
                    UIManager.Instance?.ShowNotification(
                        $"Ödeme başarılı! {amount:F2}₺ 💳", Color.green);
            }
            else
            {
                _audioSource?.PlayOneShot(_failClip);
                OnPaymentFailed?.Invoke();

                if (NetworkManager.Singleton.LocalClientId == targetId)
                    UIManager.Instance?.ShowNotification(
                        "Ödeme başarısız! Yetersiz bakiye.", Color.red);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyClientRpc(ulong targetId, string message, bool success)
        {
            if (NetworkManager.Singleton.LocalClientId != targetId) return;
            UIManager.Instance?.ShowNotification(message, success ? Color.green : Color.red);
        }

        // ─── Görsel ──────────────────────────────────────────────
        private void OnProcessingChanged(bool oldVal, bool newVal)
        {
            UpdateScreen(newVal ? _colorProcessing : _colorIdle);
        }

        private void UpdateScreen(Color color)
        {
            if (_screenRenderer == null) return;
            if (_screenRenderer.material.HasProperty(_emissionProp))
                _screenRenderer.material.SetColor(_emissionProp, color);
        }

        private IEnumerator ResetScreenAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            UpdateScreen(_colorIdle);
        }
    }
}