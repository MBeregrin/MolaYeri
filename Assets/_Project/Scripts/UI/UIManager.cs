using UnityEngine;
using UnityEngine.UI;
using TMPro;
using WeBussedUp.Interfaces;
using WeBussedUp.Player;
using WeBussedUp.Core.Managers;

namespace WeBussedUp.UI
{
    /// <summary>
    /// Tüm UI sistemlerinin merkezi. Singleton.
    /// PlayerController, EconomyManager, TimeManager event'lerini dinler
    /// ve ilgili UI bileşenlerini günceller.
    /// Online co-op: sadece local owner'ın PlayerController'ına bağlanır.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────
        public static UIManager Instance { get; private set; }

        // ─── Inspector: Prompt ───────────────────────────────────
        [Header("Etkileşim Prompt")]
        [SerializeField] private GameObject      _promptRoot;
        [SerializeField] private TextMeshProUGUI _promptText;
        [SerializeField] private Image           _interactionIcon;
        [SerializeField] private CanvasGroup     _promptCanvasGroup;

        [Header("Prompt İkonları")]
        [SerializeField] private Sprite _iconPickUp;
        [SerializeField] private Sprite _iconUse;
        [SerializeField] private Sprite _iconPay;
        [SerializeField] private Sprite _iconTalk;
        [SerializeField] private Sprite _iconClean;
        [SerializeField] private Sprite _iconRefuel;
        [SerializeField] private Sprite _iconDeposit;

        // ─── Inspector: HUD ──────────────────────────────────────
        [Header("HUD")]
        [SerializeField] private TextMeshProUGUI _moneyText;
        [SerializeField] private TextMeshProUGUI _timeText;
        [SerializeField] private TextMeshProUGUI _dayText;
        [SerializeField] private Slider          _satisfactionSlider;
        [SerializeField] private Image           _satisfactionFill;
        [SerializeField] private Color           _colorHigh = Color.green;
        [SerializeField] private Color           _colorMid  = Color.yellow;
        [SerializeField] private Color           _colorLow  = Color.red;

        // ─── Inspector: Bildirim ─────────────────────────────────
        [Header("Bildirim")]
        [SerializeField] private GameObject      _notificationRoot;
        [SerializeField] private TextMeshProUGUI _notificationText;
        [SerializeField] private float           _notificationDuration = 2.5f;

        [Header("Yetersiz Bakiye Efekti")]
        [SerializeField] private Animator _moneyShakeAnimator; // Para yazısı sallansın

        // ─── Inspector: Ayarlar ──────────────────────────────────
        [Header("Ayarlar")]
        [SerializeField] private float _promptFadeSpeed = 8f;

        // ─── Runtime ─────────────────────────────────────────────
        private PlayerController _localPlayer;
        private float            _targetPromptAlpha = 0f;
        private float            _notifTimer        = 0f;
        private bool             _notifActive       = false;

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Başlangıç durumu
            _promptRoot?.SetActive(false);
            _notificationRoot?.SetActive(false);
        }

        private void Start()
        {
            SubscribeToManagers();
        }

        private void OnDestroy()
        {
            UnsubscribeFromManagers();
            UnsubscribeFromPlayer();
        }

        private void Update()
        {
            UpdatePromptFade();
            UpdateTimeUI();
            UpdateNotificationTimer();
        }

        // ─── Player Bağlantısı ───────────────────────────────────
        /// <summary>
        /// NetworkBehaviour.OnNetworkSpawn'dan sonra local PlayerController
        /// kendini buraya kaydeder. Online co-op'ta doğru oyuncuya bağlanır.
        /// </summary>
        public void RegisterLocalPlayer(PlayerController player)
        {
            UnsubscribeFromPlayer();

            _localPlayer = player;
            _localPlayer.OnInteractableChanged += HandleInteractableChanged;

            Debug.Log("[UIManager] Local player kaydedildi.");
        }

        public void UnregisterLocalPlayer()
        {
            UnsubscribeFromPlayer();
            _localPlayer = null;
        }

        private void UnsubscribeFromPlayer()
        {
            if (_localPlayer != null)
                _localPlayer.OnInteractableChanged -= HandleInteractableChanged;
        }

        // ─── Manager Bağlantıları ─────────────────────────────────
        private void SubscribeToManagers()
        {
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnMoneyChanged      += UpdateMoneyUI;
                EconomyManager.Instance.OnInsufficientFunds += OnInsufficientFunds;
                // İlk değeri hemen yaz
                UpdateMoneyUI(EconomyManager.Instance.CompanyMoney.Value);
            }
        }

        private void UnsubscribeFromManagers()
        {
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnMoneyChanged      -= UpdateMoneyUI;
                EconomyManager.Instance.OnInsufficientFunds -= OnInsufficientFunds;
            }
        }

        // ─── Prompt ──────────────────────────────────────────────
        private void HandleInteractableChanged(IInteractable interactable)
        {
            if (interactable == null)
            {
                _targetPromptAlpha = 0f;
                return;
            }

            string prompt = interactable.GetInteractionPrompt();
            if (string.IsNullOrEmpty(prompt))
            {
                _targetPromptAlpha = 0f;
                return;
            }

            if (_promptText     != null) _promptText.text      = prompt;
            if (_interactionIcon != null) _interactionIcon.sprite = GetIcon(interactable.GetInteractionType());

            _promptRoot?.SetActive(true);
            _targetPromptAlpha = 1f;
        }

        private void UpdatePromptFade()
        {
            if (_promptCanvasGroup == null) return;

            _promptCanvasGroup.alpha = Mathf.Lerp(
                _promptCanvasGroup.alpha,
                _targetPromptAlpha,
                Time.deltaTime * _promptFadeSpeed
            );

            if (_promptCanvasGroup.alpha < 0.01f)
                _promptRoot?.SetActive(false);
        }

        private Sprite GetIcon(InteractionType type) => type switch
        {
            InteractionType.PickUp  => _iconPickUp,
            InteractionType.Pay     => _iconPay,
            InteractionType.Use     => _iconUse,
            InteractionType.Talk    => _iconTalk,
            InteractionType.Clean   => _iconClean,
            InteractionType.Refuel  => _iconRefuel,
            InteractionType.Deposit => _iconDeposit,
            _                       => _iconUse
        };

        // ─── Para UI ─────────────────────────────────────────────
        private void UpdateMoneyUI(float amount)
        {
            if (_moneyText != null)
                _moneyText.text = $"{amount:N0}₺";
        }

        private void OnInsufficientFunds(float attempted)
        {
            ShowNotification("Yetersiz Bakiye!", Color.red);
            _moneyShakeAnimator?.SetTrigger("Shake");
        }

        // ─── Saat UI ─────────────────────────────────────────────
        private void UpdateTimeUI()
        {
            var tm = TimeManager.Instance;
            if (tm == null) return;

            if (_timeText != null) _timeText.text = tm.GetFormattedTime();
            if (_dayText  != null) _dayText.text  = $"Gün {tm.CurrentDay}";
        }

        // ─── Memnuniyet UI ───────────────────────────────────────
        /// <summary>
        /// Ortalama müşteri memnuniyeti — CustomerAI veya RatingManager çağırır.
        /// </summary>
        public void UpdateSatisfaction(float satisfaction)
        {
            if (_satisfactionSlider != null)
                _satisfactionSlider.value = satisfaction / 100f;

            if (_satisfactionFill != null)
            {
                _satisfactionFill.color = satisfaction > 66f ? _colorHigh
                                        : satisfaction > 33f ? _colorMid
                                        : _colorLow;
            }
        }

        // ─── Bildirim ────────────────────────────────────────────
        public void ShowNotification(string message, Color color = default)
        {
            if (_notificationRoot == null || _notificationText == null) return;

            _notificationText.text  = message;
            _notificationText.color = color == default ? Color.white : color;
            _notificationRoot.SetActive(true);

            _notifTimer  = _notificationDuration;
            _notifActive = true;
        }

        private void UpdateNotificationTimer()
        {
            if (!_notifActive) return;

            _notifTimer -= Time.deltaTime;
            if (_notifTimer > 0f) return;

            _notifActive = false;
            _notificationRoot?.SetActive(false);
        }
    }
}