using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Unity.Netcode;
using WeBussedUp.Interfaces;
using WeBussedUp.Core.Managers;
using WeBussedUp.NPC;

namespace WeBussedUp.UI
{
    public enum TabletPage  { Dashboard, Rating, Prices, Stock, Finance }

    /// <summary>
    /// Tablet veya telefon cihazı — IInteractable ile etkileşime girilir.
    /// Tablet: sabit 3D obje (tezgah üstü), E ile açılır.
    /// Phone: oyuncunun cebinde, özel tuşla çıkarılır (ileride).
    /// İçerik: Rating, ürün fiyatları, stok, finans özeti.
    /// </summary>
    public class TabletDevice : NetworkBehaviour, IInteractable
    {
        // ─── Inspector: Cihaz ────────────────────────────────────
        [Header("Cihaz Tipi")]
        [SerializeField] private DeviceType _deviceType;

        [Header("3D Model")]
        [SerializeField] private GameObject  _deviceModel;   // Masadaki tablet modeli
        [SerializeField] private Renderer    _screenRenderer;
        [SerializeField] private Material    _screenOnMat;
        [SerializeField] private Material    _screenOffMat;

        // ─── Inspector: UI Panel ─────────────────────────────────
        [Header("Ana Panel")]
        [SerializeField] private GameObject   _uiRoot;        // Tüm tablet UI'ı
        [SerializeField] private CanvasGroup  _canvasGroup;
        [SerializeField] private RectTransform _panelRect;

        [Header("Navigasyon Butonları")]
        [SerializeField] private Button _dashboardBtn;
        [SerializeField] private Button _ratingBtn;
        [SerializeField] private Button _pricesBtn;
        [SerializeField] private Button _stockBtn;
        [SerializeField] private Button _financeBtn;
        [SerializeField] private Button _closeBtn;

        [Header("Sayfa Panelleri")]
        [SerializeField] private GameObject _dashboardPage;
        [SerializeField] private GameObject _ratingPage;
        [SerializeField] private GameObject _pricesPage;
        [SerializeField] private GameObject _stockPage;
        [SerializeField] private GameObject _financePage;

        // ─── Inspector: Dashboard ────────────────────────────────
        [Header("Dashboard")]
        [SerializeField] private TextMeshProUGUI _balanceText;
        [SerializeField] private TextMeshProUGUI _dayText;
        [SerializeField] private TextMeshProUGUI _timeText;
        [SerializeField] private TextMeshProUGUI _customerCountText;
        [SerializeField] private TextMeshProUGUI _popularityText;
        [SerializeField] private TextMeshProUGUI _overallRatingDashText;

        // ─── Inspector: Rating ───────────────────────────────────
        [Header("Rating Sayfası")]
        [SerializeField] private TextMeshProUGUI _overallRatingText;
        [SerializeField] private TextMeshProUGUI _serviceText;
        [SerializeField] private TextMeshProUGUI _cleanlinessText;
        [SerializeField] private TextMeshProUGUI _priceText;
        [SerializeField] private TextMeshProUGUI _speedText;
        [SerializeField] private Image[]         _starImages;
        [SerializeField] private Sprite          _starFilled;
        [SerializeField] private Sprite          _starEmpty;
        [SerializeField] private Slider          _serviceSlider;
        [SerializeField] private Slider          _cleanlinessSlider;
        [SerializeField] private Slider          _priceRatingSlider;
        [SerializeField] private Slider          _speedSlider;
        [SerializeField] private TextMeshProUGUI _totalRatingsText;

        // ─── Inspector: Fiyatlar ─────────────────────────────────
        [Header("Fiyat Sayfası")]
        [SerializeField] private Transform              _priceListContainer;
        [SerializeField] private GameObject             _priceListItemPrefab;
        [SerializeField] private TextMeshProUGUI        _fuelPriceText;
        [SerializeField] private Button                 _fuelPriceUpBtn;
        [SerializeField] private Button                 _fuelPriceDownBtn;
        [SerializeField] private float                  _fuelPriceStep = 1f;

        // ─── Inspector: Stok ─────────────────────────────────────
        [Header("Stok Sayfası")]
        [SerializeField] private Transform   _stockListContainer;
        [SerializeField] private GameObject  _stockListItemPrefab;
        [SerializeField] private Image       _stockStatusBar;
        [SerializeField] private Color       _stockFullColor  = Color.green;
        [SerializeField] private Color       _stockLowColor   = Color.red;

        // ─── Inspector: Finans ───────────────────────────────────
        [Header("Finans Sayfası")]
        [SerializeField] private TextMeshProUGUI _totalIncomeText;
        [SerializeField] private TextMeshProUGUI _totalExpenseText;
        [SerializeField] private TextMeshProUGUI _netProfitText;
        [SerializeField] private TextMeshProUGUI _transactionListText;

        // ─── Inspector: Animasyon ─────────────────────────────────
        [Header("Animasyon")]
        [SerializeField] private float _openDuration  = 0.4f;
        [SerializeField] private float _closeDuration = 0.25f;

        // ─── Runtime ─────────────────────────────────────────────
        private bool       _isOpen       = false;
        private TabletPage _currentPage  = TabletPage.Dashboard;
        private float      _currentFuelPrice = 42f;

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            return _isOpen ? "Tableti Kapat [E]" : "Tableti Aç [E]";
        }

        public bool CanInteract(ulong playerId) => true;

        public InteractionType GetInteractionType() => InteractionType.Use;

        public void Interact(ulong playerId)
        {
            if (_isOpen) CloseDevice();
            else         OpenDevice();
        }

        // ─── Unity ───────────────────────────────────────────────
        private void Start()
        {
            SetupButtons();
            _uiRoot?.SetActive(false);
            SetScreenState(false);
        }

        private void Update()
        {
            // Tablet açıkken verileri güncelle
            if (_isOpen && Time.frameCount % 30 == 0) // Her 30 frame'de bir
                RefreshCurrentPage();
        }

        // ─── Buton Kurulumu ──────────────────────────────────────
        private void SetupButtons()
        {
            _dashboardBtn?.onClick.AddListener(() => SwitchPage(TabletPage.Dashboard));
            _ratingBtn?.onClick.AddListener(()    => SwitchPage(TabletPage.Rating));
            _pricesBtn?.onClick.AddListener(()    => SwitchPage(TabletPage.Prices));
            _stockBtn?.onClick.AddListener(()     => SwitchPage(TabletPage.Stock));
            _financeBtn?.onClick.AddListener(()   => SwitchPage(TabletPage.Finance));
            _closeBtn?.onClick.AddListener(CloseDevice);

            // Yakıt fiyatı
            _fuelPriceUpBtn?.onClick.AddListener(()   => AdjustFuelPrice(_fuelPriceStep));
            _fuelPriceDownBtn?.onClick.AddListener(() => AdjustFuelPrice(-_fuelPriceStep));
        }

        // ─── Aç/Kapat ────────────────────────────────────────────
        private void OpenDevice()
        {
            _isOpen = true;
            _uiRoot?.SetActive(true);
            SetScreenState(true);

            // Cursor serbest bırak
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            // Animasyon
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.DOFade(1f, _openDuration).SetEase(Ease.OutQuad);
            }

            if (_panelRect != null)
            {
                _panelRect.localScale = new Vector3(0.9f, 0.9f, 1f);
                _panelRect.DOScale(Vector3.one, _openDuration).SetEase(Ease.OutBack);
            }

            SwitchPage(TabletPage.Dashboard);
        }

        private void CloseDevice()
        {
            _isOpen = false;

            // Cursor kilitle
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            Sequence seq = DOTween.Sequence();

            if (_canvasGroup != null)
                seq.Join(_canvasGroup.DOFade(0f, _closeDuration));

            if (_panelRect != null)
                seq.Join(_panelRect.DOScale(
                    new Vector3(0.9f, 0.9f, 1f), _closeDuration)
                    .SetEase(Ease.InBack));

            seq.OnComplete(() =>
            {
                _uiRoot?.SetActive(false);
                SetScreenState(false);
            });
        }

        // ─── Sayfa Geçişi ─────────────────────────────────────────
        private void SwitchPage(TabletPage page)
        {
            _currentPage = page;

            _dashboardPage?.SetActive(page == TabletPage.Dashboard);
            _ratingPage?.SetActive(page   == TabletPage.Rating);
            _pricesPage?.SetActive(page   == TabletPage.Prices);
            _stockPage?.SetActive(page    == TabletPage.Stock);
            _financePage?.SetActive(page  == TabletPage.Finance);

            RefreshCurrentPage();
        }

        private void RefreshCurrentPage()
        {
            switch (_currentPage)
            {
                case TabletPage.Dashboard: RefreshDashboard(); break;
                case TabletPage.Rating:    RefreshRating();    break;
                case TabletPage.Prices:    RefreshPrices();    break;
                case TabletPage.Stock:     RefreshStock();     break;
                case TabletPage.Finance:   RefreshFinance();   break;
            }
        }

        // ─── Dashboard ───────────────────────────────────────────
        private void RefreshDashboard()
        {
            if (EconomyManager.Instance != null)
            {
                float balance = EconomyManager.Instance.CompanyMoney.Value;
                if (_balanceText != null)
                    _balanceText.text = $"{balance:N0}₺";
            }

            if (TimeManager.Instance != null)
            {
                if (_dayText  != null) _dayText.text  = $"Gün {TimeManager.Instance.CurrentDay}";
                if (_timeText != null) _timeText.text = TimeManager.Instance.GetFormattedTime();
            }

            if (TrafficManager.Instance != null)
            {
                if (_customerCountText != null)
                    _customerCountText.text =
                        $"{TrafficManager.Instance.TotalCustomersToday.Value} Müşteri";

                if (_popularityText != null)
                    _popularityText.text =
                        $"%{TrafficManager.Instance.EntranceProbability.Value:F0} Popülerlik";
            }

            if (RatingManager.Instance != null)
            {
                if (_overallRatingDashText != null)
                    _overallRatingDashText.text =
                        $"{RatingManager.Instance.OverallRating.Value:F1} ⭐";
            }
        }

        // ─── Rating ──────────────────────────────────────────────
        private void RefreshRating()
        {
            if (RatingManager.Instance == null) return;

            float overall     = RatingManager.Instance.OverallRating.Value;
            float service     = RatingManager.Instance.ServiceAvg;
            float cleanliness = RatingManager.Instance.CleanlinessAvg;
            float price       = RatingManager.Instance.PriceAvg;
            float speed       = RatingManager.Instance.SpeedAvg;
            int   total       = RatingManager.Instance.TotalRatingsCount.Value;

            if (_overallRatingText != null)
                _overallRatingText.text = $"{overall:F1} / 5.0";

            if (_serviceText     != null) _serviceText.text     = $"{service:F1}";
            if (_cleanlinessText != null) _cleanlinessText.text = $"{cleanliness:F1}";
            if (_priceText       != null) _priceText.text       = $"{price:F1}";
            if (_speedText       != null) _speedText.text       = $"{speed:F1}";
            if (_totalRatingsText != null)
                _totalRatingsText.text = $"{total} değerlendirme";

            // Yıldızlar
            int stars = Mathf.RoundToInt(overall);
            if (_starImages != null)
                for (int i = 0; i < _starImages.Length; i++)
                    if (_starImages[i] != null)
                        _starImages[i].sprite = i < stars ? _starFilled : _starEmpty;

            // Slider'lar
            SetSlider(_serviceSlider,       service     / 5f);
            SetSlider(_cleanlinessSlider,   cleanliness / 5f);
            SetSlider(_priceRatingSlider,   price       / 5f);
            SetSlider(_speedSlider,         speed       / 5f);
        }

        // ─── Fiyatlar ────────────────────────────────────────────
        private void RefreshPrices()
        {
            // Yakıt fiyatı
            if (_fuelPriceText != null)
                _fuelPriceText.text = $"{_currentFuelPrice:F2}₺/Lt";

            // Raf ürün fiyatları — ShelfManager'lardan topla
            if (_priceListContainer == null || _priceListItemPrefab == null) return;

            // Mevcut listeyi temizle
            foreach (Transform child in _priceListContainer)
                Destroy(child.gameObject);

            // Tüm ShelfManager'ları bul
            var shelves = FindObjectsByType <WeBussedUp.Stations.Market.ShelfManager>(FindObjectsInactive.Exclude);

            foreach (var shelf in shelves)
            {
                if (string.IsNullOrEmpty(shelf.ProductID)) continue;

                var product = WeBussedUp.Core.ItemDatabase.Instance?
                    .GetProductByID(shelf.ProductID);

                if (product == null) continue;

                GameObject item = Instantiate(_priceListItemPrefab, _priceListContainer);
                var texts = item.GetComponentsInChildren<TextMeshProUGUI>();

                if (texts.Length > 0) texts[0].text = product.productName;
                if (texts.Length > 1) texts[1].text = $"{product.defaultSellPrice:F2}₺";
                if (texts.Length > 2) texts[2].text = $"{shelf.Stock}/{shelf.Capacity}";
            }
        }

        private void AdjustFuelPrice(float delta)
        {
            _currentFuelPrice = Mathf.Max(0.01f, _currentFuelPrice + delta);

            if (_fuelPriceText != null)
                _fuelPriceText.text = $"{_currentFuelPrice:F2}₺/Lt";

            // Tüm FuelPump'lara yeni fiyatı uygula
            var pumps = FindObjectsByType <WeBussedUp.Stations.GasStation.FuelPump>(FindObjectsInactive.Exclude);

            foreach (var pump in pumps)
                pump.SetPriceServerRpc(_currentFuelPrice);
        }

        // ─── Stok ────────────────────────────────────────────────
        private void RefreshStock()
        {
            if (_stockListContainer == null || _stockListItemPrefab == null) return;

            foreach (Transform child in _stockListContainer)
                Destroy(child.gameObject);

            var shelves = FindObjectsByType <WeBussedUp.Stations.Market.ShelfManager>(FindObjectsInactive.Exclude);

            int totalStock    = 0;
            int totalCapacity = 0;

            foreach (var shelf in shelves)
            {
                if (string.IsNullOrEmpty(shelf.ProductID)) continue;

                var product = WeBussedUp.Core.ItemDatabase.Instance?
                    .GetProductByID(shelf.ProductID);

                if (product == null) continue;

                totalStock    += shelf.Stock;
                totalCapacity += shelf.Capacity;

                GameObject item = Instantiate(_stockListItemPrefab, _stockListContainer);
                var texts = item.GetComponentsInChildren<TextMeshProUGUI>();
                var images = item.GetComponentsInChildren<Image>();

                if (texts.Length > 0) texts[0].text = product.productName;
                if (texts.Length > 1) texts[1].text = $"{shelf.Stock}/{shelf.Capacity}";

                // Stok bar rengi
                float ratio = shelf.Capacity > 0
                    ? (float)shelf.Stock / shelf.Capacity : 0f;

                if (images.Length > 0)
                    images[0].color = Color.Lerp(_stockLowColor, _stockFullColor, ratio);

                // Düşük stok uyarısı
                if (product != null && shelf.Stock <= product.lowStockThreshold)
                    if (texts.Length > 2)
                    {
                        texts[2].text  = "⚠️ Düşük";
                        texts[2].color = Color.red;
                    }
            }

            // Genel stok bar
            if (_stockStatusBar != null && totalCapacity > 0)
            {
                float ratio = (float)totalStock / totalCapacity;
                _stockStatusBar.fillAmount = ratio;
                _stockStatusBar.color = Color.Lerp(_stockLowColor, _stockFullColor, ratio);
            }
        }

        // ─── Finans ──────────────────────────────────────────────
        private void RefreshFinance()
        {
            if (EconomyManager.Instance == null) return;

            float balance = EconomyManager.Instance.CompanyMoney.Value;

            // Transaction geçmişinden günlük hesapla
            float income  = 0f;
            float expense = 0f;
            var   sb      = new System.Text.StringBuilder();

            foreach (var record in EconomyManager.Instance.TransactionHistory)
            {
                if (record.IsIncome) income  += record.Amount;
                else                 expense += record.Amount;

                sb.AppendLine(record.ToString());
            }

            float net = income - expense;

            if (_totalIncomeText  != null)
                _totalIncomeText.text  = $"+{income:N0}₺";

            if (_totalExpenseText != null)
                _totalExpenseText.text = $"-{expense:N0}₺";

            if (_netProfitText != null)
            {
                _netProfitText.text  = $"{(net >= 0 ? "+" : "")}{net:N0}₺";
                _netProfitText.color = net >= 0 ? Color.green : Color.red;
            }

            if (_transactionListText != null)
                _transactionListText.text = sb.ToString();
        }

        // ─── Util ─────────────────────────────────────────────────
        private void SetSlider(Slider slider, float value)
        {
            if (slider != null) slider.value = value;
        }

        private void SetScreenState(bool on)
        {
            if (_screenRenderer == null) return;
            _screenRenderer.material = on ? _screenOnMat : _screenOffMat;
        }
    }
}