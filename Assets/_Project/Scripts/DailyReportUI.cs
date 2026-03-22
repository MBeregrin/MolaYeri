using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using DG.Tweening;
using WeBussedUp.Core.Managers;
using WeBussedUp.NPC;

namespace WeBussedUp.UI
{
    /// <summary>
    /// Gün sonu raporu — TimeManager.OnNewDay event'ini dinler.
    /// Gelir, gider, müşteri sayısı, rating ve temizlik skoru gösterir.
    /// DOTween ile animasyonlu açılış.
    /// </summary>
    public class DailyReportUI : MonoBehaviour
    {
        // ─── Inspector: Panel ────────────────────────────────────
        [Header("Panel")]
        [SerializeField] private GameObject  _reportPanel;
        [SerializeField] private CanvasGroup _panelCanvasGroup;
        [SerializeField] private RectTransform _panelRect;

        [Header("Animasyon")]
        [SerializeField] private float _openDuration  = 0.5f;
        [SerializeField] private float _closeDuration = 0.3f;
        [SerializeField] private float _autoCloseTime = 10f; // Otomatik kapanma süresi

        // ─── Inspector: Gün Bilgisi ───────────────────────────────
        [Header("Gün Bilgisi")]
        [SerializeField] private TextMeshProUGUI _dayText;
        [SerializeField] private TextMeshProUGUI _dateText;

        // ─── Inspector: Finans ───────────────────────────────────
        [Header("Finans")]
        [SerializeField] private TextMeshProUGUI _totalIncomeText;
        [SerializeField] private TextMeshProUGUI _totalExpenseText;
        [SerializeField] private TextMeshProUGUI _netProfitText;
        [SerializeField] private TextMeshProUGUI _balanceText;
        [SerializeField] private Image           _profitIndicator; // Yeşil/kırmızı ok

        // ─── Inspector: Müşteri ──────────────────────────────────
        [Header("Müşteri")]
        [SerializeField] private TextMeshProUGUI _totalCustomersText;
        [SerializeField] private TextMeshProUGUI _satisfiedCustomersText;
        [SerializeField] private TextMeshProUGUI _leftAngryText;

        // ─── Inspector: Rating ───────────────────────────────────
        [Header("Rating")]
        [SerializeField] private TextMeshProUGUI _overallRatingText;
        [SerializeField] private TextMeshProUGUI _serviceRatingText;
        [SerializeField] private TextMeshProUGUI _cleanlinessRatingText;
        [SerializeField] private TextMeshProUGUI _priceRatingText;
        [SerializeField] private TextMeshProUGUI _speedRatingText;
        [SerializeField] private Image[]         _starImages;      // 5 yıldız
        [SerializeField] private Sprite          _starFilled;
        [SerializeField] private Sprite          _starEmpty;

        // ─── Inspector: Progress Barlar ──────────────────────────
        [Header("Progress Barlar")]
        [SerializeField] private Slider _serviceSlider;
        [SerializeField] private Slider _cleanlinessSlider;
        [SerializeField] private Slider _priceSlider;
        [SerializeField] private Slider _speedSlider;

        // ─── Inspector: Butonlar ─────────────────────────────────
        [Header("Butonlar")]
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _saveButton;

        // ─── Inspector: Renk ─────────────────────────────────────
        [Header("Renkler")]
        [SerializeField] private Color _profitColor = Color.green;
        [SerializeField] private Color _lossColor   = Color.red;
        [SerializeField] private Color _neutralColor = Color.white;

        // ─── Runtime ─────────────────────────────────────────────
        private bool      _isOpen        = false;
        private Coroutine _autoCloseCoroutine;

        // Gün verileri
        private float _dayIncome;
        private float _dayExpense;
        private int   _dayCustomers;

        // ─── Unity ───────────────────────────────────────────────
        private void Start()
        {
            // Event bağlantıları
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnNewDay += HandleNewDay;

            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnMoneyChanged      += TrackMoney;
                EconomyManager.Instance.OnTransactionRecorded += TrackTransaction;
            }

            if (TrafficManager.Instance != null)
                TrafficManager.Instance.TotalCustomersToday.OnValueChanged += TrackCustomers;

            // Buton bağlantıları
            _continueButton?.onClick.AddListener(CloseReport);
            _saveButton?.onClick.AddListener(OnSaveClicked);

            // Başlangıçta kapalı
            _reportPanel?.SetActive(false);
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnNewDay -= HandleNewDay;

            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnMoneyChanged       -= TrackMoney;
                EconomyManager.Instance.OnTransactionRecorded -= TrackTransaction;
            }
        }

        // ─── Gün Sonu Tetikleyici ─────────────────────────────────
        private void HandleNewDay(int newDay)
        {
            // Bir önceki günün raporunu göster
            ShowReport(newDay - 1);

            // Günlük istatistikleri sıfırla
            _dayIncome   = 0f;
            _dayExpense  = 0f;
            _dayCustomers = 0;
        }

        // ─── Rapor Göster ────────────────────────────────────────
        public void ShowReport(int day)
        {
            PopulateReport(day);
            OpenPanel();
        }

        private void PopulateReport(int day)
        {
            // Gün bilgisi
            if (_dayText  != null) _dayText.text  = $"Gün {day} Raporu";
            if (_dateText != null) _dateText.text  = System.DateTime.Now.ToString("dd/MM/yyyy");

            // Finans
            float netProfit = _dayIncome - _dayExpense;
            float balance   = EconomyManager.Instance?.CompanyMoney.Value ?? 0f;

            if (_totalIncomeText  != null)
                _totalIncomeText.text  = $"+{_dayIncome:N0}₺";

            if (_totalExpenseText != null)
                _totalExpenseText.text = $"-{_dayExpense:N0}₺";

            if (_netProfitText != null)
            {
                _netProfitText.text  = $"{(netProfit >= 0 ? "+" : "")}{netProfit:N0}₺";
                _netProfitText.color = netProfit >= 0 ? _profitColor : _lossColor;
            }

            if (_balanceText != null)
                _balanceText.text = $"{balance:N0}₺";

            if (_profitIndicator != null)
                _profitIndicator.color = netProfit >= 0 ? _profitColor : _lossColor;

            // Müşteri
            if (_totalCustomersText != null)
                _totalCustomersText.text = $"{_dayCustomers}";

            // Rating
            if (RatingManager.Instance != null)
            {
                float overall     = RatingManager.Instance.OverallRating.Value;
                float service     = RatingManager.Instance.ServiceAvg;
                float cleanliness = RatingManager.Instance.CleanlinessAvg;
                float price       = RatingManager.Instance.PriceAvg;
                float speed       = RatingManager.Instance.SpeedAvg;

                if (_overallRatingText     != null)
                    _overallRatingText.text     = $"{overall:F1} ⭐";

                if (_serviceRatingText     != null)
                    _serviceRatingText.text     = $"{service:F1}";

                if (_cleanlinessRatingText != null)
                    _cleanlinessRatingText.text = $"{cleanliness:F1}";

                if (_priceRatingText       != null)
                    _priceRatingText.text       = $"{price:F1}";

                if (_speedRatingText       != null)
                    _speedRatingText.text       = $"{speed:F1}";

                // Yıldızlar
                UpdateStars(overall);

                // Slider'lar — DOTween animasyonlu
                AnimateSlider(_serviceSlider,     service     / 5f);
                AnimateSlider(_cleanlinessSlider, cleanliness / 5f);
                AnimateSlider(_priceSlider,       price       / 5f);
                AnimateSlider(_speedSlider,       speed       / 5f);
            }
        }

        // ─── Panel Aç/Kapat ──────────────────────────────────────
        private void OpenPanel()
        {
            if (_isOpen) return;
            _isOpen = true;

            _reportPanel?.SetActive(true);

            if (_panelCanvasGroup != null)
            {
                _panelCanvasGroup.alpha = 0f;
                _panelCanvasGroup.DOFade(1f, _openDuration).SetEase(Ease.OutQuad);
            }

            if (_panelRect != null)
            {
                _panelRect.localScale = Vector3.one * 0.8f;
                _panelRect.DOScale(Vector3.one, _openDuration).SetEase(Ease.OutBack);
            }

            // Otomatik kapanma
            if (_autoCloseCoroutine != null) StopCoroutine(_autoCloseCoroutine);
            _autoCloseCoroutine = StartCoroutine(AutoCloseRoutine());
        }

        private void CloseReport()
        {
            if (!_isOpen) return;

            if (_autoCloseCoroutine != null)
                StopCoroutine(_autoCloseCoroutine);

            Sequence seq = DOTween.Sequence();

            if (_panelCanvasGroup != null)
                seq.Join(_panelCanvasGroup.DOFade(0f, _closeDuration));

            if (_panelRect != null)
                seq.Join(_panelRect.DOScale(0.8f, _closeDuration).SetEase(Ease.InBack));

            seq.OnComplete(() =>
            {
                _reportPanel?.SetActive(false);
                _isOpen = false;
            });
        }

        private IEnumerator AutoCloseRoutine()
        {
            yield return new WaitForSeconds(_autoCloseTime);
            CloseReport();
        }

        // ─── Görsel Güncellemeler ─────────────────────────────────
        private void UpdateStars(float rating)
        {
            if (_starImages == null) return;

            int filledCount = Mathf.RoundToInt(rating);

            for (int i = 0; i < _starImages.Length; i++)
            {
                if (_starImages[i] == null) continue;

                bool filled = i < filledCount;
                _starImages[i].sprite = filled ? _starFilled : _starEmpty;

                // DOTween yıldız animasyonu
                if (filled)
                {
                    _starImages[i].transform.localScale = Vector3.zero;
                    _starImages[i].transform
                        .DOScale(Vector3.one, 0.3f)
                        .SetDelay(i * 0.1f)
                        .SetEase(Ease.OutBack);
                }
            }
        }

        private void AnimateSlider(Slider slider, float targetValue)
        {
            if (slider == null) return;

            slider.value = 0f;
            DOTween.To(
                () => slider.value,
                x  => slider.value = x,
                targetValue,
                0.8f
            ).SetEase(Ease.OutCubic).SetDelay(0.3f);
        }

        // ─── Veri Takibi ─────────────────────────────────────────
        private void TrackMoney(float newAmount) { }

        private void TrackTransaction(TransactionRecord record)
        {
            if (record.IsIncome)
                _dayIncome  += record.Amount;
            else
                _dayExpense += record.Amount;
        }

        private void TrackCustomers(int oldVal, int newVal)
        {
            _dayCustomers = newVal;
        }

        // ─── Buton Aksiyonları ────────────────────────────────────
        private void OnSaveClicked()
        {
            SaveManager.Instance?.ManualSave();
            UIManager.Instance?.ShowNotification("Oyun kaydedildi! 💾", Color.cyan);
        }
    }
}