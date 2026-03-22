using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using WeBussedUp.Core.Managers;

namespace WeBussedUp.UI
{
    /// <summary>
    /// Oyun içi pause menüsü.
    /// ESC → GameManager.RequestPauseServerRpc() → bu panel açılır.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Panel")]
        [SerializeField] private GameObject   _panel;
        [SerializeField] private CanvasGroup  _canvasGroup;
        [SerializeField] private RectTransform _menuRect;

        [Header("Butonlar")]
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _saveButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _mainMenuButton;
        [SerializeField] private Button _quitButton;

        [Header("Bilgi")]
        [SerializeField] private TextMeshProUGUI _balanceText;
        [SerializeField] private TextMeshProUGUI _dayText;
        [SerializeField] private TextMeshProUGUI _timeText;
        [SerializeField] private TextMeshProUGUI _ratingText;

        // ─── Unity ───────────────────────────────────────────────
        private void Start()
        {
            SetupButtons();

            if (WeBussedUp.Core.GameManager.Instance != null)
            {
                WeBussedUp.Core.GameManager.Instance.OnGamePaused  += OnPaused;
                WeBussedUp.Core.GameManager.Instance.OnGameResumed += OnResumed;
            }

            if (EconomyManager.Instance != null)
                EconomyManager.Instance.OnMoneyChanged += UpdateBalance;

            _panel?.SetActive(false);
        }

        private void OnDestroy()
        {
            if (WeBussedUp.Core.GameManager.Instance != null)
            {
                WeBussedUp.Core.GameManager.Instance.OnGamePaused  -= OnPaused;
                WeBussedUp.Core.GameManager.Instance.OnGameResumed -= OnResumed;
            }

            if (EconomyManager.Instance != null)
                EconomyManager.Instance.OnMoneyChanged -= UpdateBalance;
        }

        // ─── Buton Kurulumu ──────────────────────────────────────
        private void SetupButtons()
        {
            _resumeButton?.onClick.AddListener(OnResumeClicked);
            _saveButton?.onClick.AddListener(OnSaveClicked);
            _settingsButton?.onClick.AddListener(OnSettingsClicked);
            _mainMenuButton?.onClick.AddListener(OnMainMenuClicked);
            _quitButton?.onClick.AddListener(OnQuitClicked);
        }

        // ─── Aç/Kapat ────────────────────────────────────────────
        private void OnPaused()
        {
            _panel?.SetActive(true);
            UpdateInfo();

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.DOFade(1f, 0.3f).SetUpdate(true);
            }

            if (_menuRect != null)
            {
                _menuRect.localScale = Vector3.one * 0.9f;
                _menuRect.DOScale(Vector3.one, 0.3f)
                    .SetEase(Ease.OutBack)
                    .SetUpdate(true);
            }
        }

        private void OnResumed()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.DOFade(0f, 0.2f)
                    .SetUpdate(true)
                    .OnComplete(() => _panel?.SetActive(false));
            }
            else
            {
                _panel?.SetActive(false);
            }
        }

        // ─── Bilgi Güncelleme ─────────────────────────────────────
        private void UpdateInfo()
        {
            if (_balanceText != null && EconomyManager.Instance != null)
                _balanceText.text = $"{EconomyManager.Instance.CompanyMoney.Value:N0}₺";

            if (_dayText != null && TimeManager.Instance != null)
                _dayText.text = $"Gün {TimeManager.Instance.CurrentDay}";

            if (_timeText != null && TimeManager.Instance != null)
                _timeText.text = TimeManager.Instance.GetFormattedTime();

            if (_ratingText != null && RatingManager.Instance != null)
                _ratingText.text = $"{RatingManager.Instance.OverallRating.Value:F1} ⭐";
        }

        private void UpdateBalance(float amount)
        {
            if (_balanceText != null)
                _balanceText.text = $"{amount:N0}₺";
        }

        // ─── Buton Aksiyonları ────────────────────────────────────
        private void OnResumeClicked()
        {
            WeBussedUp.Core.GameManager.Instance?.RequestResumeServerRpc();
        }

        private void OnSaveClicked()
        {
            SaveManager.Instance?.ManualSave();
            UIManager.Instance?.ShowNotification("Kaydedildi! 💾", Color.cyan);
        }

        private void OnSettingsClicked()
        {
            // SettingsManager açılacak
            UIManager.Instance?.ShowNotification("Ayarlar yakında! ⚙️", Color.white);
        }

        private void OnMainMenuClicked()
        {
            WeBussedUp.Core.GameManager.Instance?.RequestReturnToMenuServerRpc();
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}