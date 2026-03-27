using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;
using DG.Tweening;
using WeBussedUp.Interfaces;
using WeBussedUp.Core.Managers;
using WeBussedUp.Core.Data;
using WeBussedUp.NPC;
using WeBussedUp.UI;

namespace WeBussedUp.Stations.Market
{
    [System.Serializable]
    public class CartItem
    {
        public ProductData product;
        public int         quantity;
        public float       totalPrice => product.defaultSellPrice * quantity;
    }

    /// <summary>
    /// Kasa sistemi.
    /// Oyuncu E ile kasaya geçer → kasiyer moduna girer.
    /// Müşteri gelir → ürünleri tarat → ödeme al → müşteri gider.
    /// Kasiyer modu: kamera kilitlener, özel UI açılır.
    /// </summary>
    public class CashRegister : NetworkBehaviour, IInteractable
    {
        // ─── Inspector: Kasa ─────────────────────────────────────
        [Header("Kasa Ayarları")]
        [SerializeField] private Transform _cashierStandPoint;  // Oyuncunun duracağı yer
        [SerializeField] private Transform _customerStandPoint; // Müşterinin duracağı yer
        [SerializeField] private Transform _cameraLockPoint;    // Kasiyer kamera noktası

        [Header("Ekran (3D)")]
        [SerializeField] private Renderer    _screenRenderer;
        [SerializeField] private Material    _screenOnMat;
        [SerializeField] private Material    _screenOffMat;

        [Header("Ses")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip   _beepClip;         // Ürün tarama sesi
        [SerializeField] private AudioClip   _paymentClip;      // Ödeme sesi
        [SerializeField] private AudioClip   _errorClip;        // Hata sesi

        // ─── Inspector: UI ───────────────────────────────────────
        [Header("Kasiyer UI")]
        [SerializeField] private GameObject   _cashierUI;
        [SerializeField] private CanvasGroup  _uiCanvasGroup;

        [Header("Sepet Listesi")]
        [SerializeField] private Transform              _cartContainer;
        [SerializeField] private GameObject             _cartItemPrefab;

        [Header("Fiyat")]
        [SerializeField] private TextMeshProUGUI _subtotalText;
        [SerializeField] private TextMeshProUGUI _totalText;
        [SerializeField] private TextMeshProUGUI _changeText;
        [SerializeField] private TextMeshProUGUI _customerNameText;
        [SerializeField] private TextMeshProUGUI _statusText;

        [Header("Butonlar")]
        [SerializeField] private Button _chargeButton;     // Ödemeyi al
        [SerializeField] private Button _cancelButton;     // İptal
        [SerializeField] private Button _exitButton;       // Kasiyerden çık

        [Header("Animasyon")]
        [SerializeField] private float _uiOpenDuration = 0.3f;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<bool> IsOccupied = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> HasCustomer = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private ulong              _cashierPlayerId  = ulong.MaxValue;
        private ulong              _currentCustomerId = ulong.MaxValue;
        private List<CartItem>     _cart             = new();
        private float              _totalAmount      = 0f;
        private bool               _isCashierMode    = false;

        // Kamera kilitleme için
        private Transform          _playerCameraHolder;
        private Vector3            _originalCamPos;
        private Quaternion         _originalCamRot;

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            IsOccupied.OnValueChanged += OnOccupiedChanged;
            HasCustomer.OnValueChanged += OnCustomerChanged;

            SetScreenState(false);
            _cashierUI?.SetActive(false);
        }

        public override void OnNetworkDespawn()
        {
            IsOccupied.OnValueChanged  -= OnOccupiedChanged;
            HasCustomer.OnValueChanged -= OnCustomerChanged;
        }

        // ─── IInteractable ───────────────────────────────────────
        public string GetInteractionPrompt()
        {
            if (_isCashierMode) return "Kasadan Çık [E]";
            if (IsOccupied.Value) return "Kasa Meşgul";
            return "Kasiyere Geç [E]";
        }

        public bool CanInteract(ulong playerId)
        {
            if (_isCashierMode) return true; // Çıkmak için
            return !IsOccupied.Value;
        }

        public InteractionType GetInteractionType() => InteractionType.Use;

        public void Interact(ulong playerId)
        {
            if (!IsSpawned) return;

            if (_isCashierMode)
                ExitCashierMode(playerId);
            else
                EnterCashierMode(playerId);
        }

        // ─── Kasiyer Modu Gir/Çık ────────────────────────────────
        private void EnterCashierMode(ulong playerId)
        {
            EnterCashierServerRpc(playerId);
        }

        [Rpc(SendTo.Server)]
        private void EnterCashierServerRpc(ulong playerId)
        {
            if (IsOccupied.Value) return;

            IsOccupied.Value    = true;
            _cashierPlayerId    = playerId;

            EnterCashierClientRpc(playerId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EnterCashierClientRpc(ulong playerId)
        {
            if (NetworkManager.Singleton.LocalClientId != playerId) return;

            _isCashierMode = true;

            // Cursor serbest bırak
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            // Kamera kilitle
            LockCameraToRegister();

            // UI aç
            OpenCashierUI();

            SetScreenState(true);
            SetStatus("Müşteri bekleniyor...");

            Debug.Log("[CashRegister] Kasiyer moduna girildi.");
        }

        private void ExitCashierMode(ulong playerId)
        {
            if (_cart.Count > 0)
            {
                UIManager.Instance?.ShowNotification(
                    "Önce işlemi tamamla!", Color.red);
                return;
            }

            ExitCashierServerRpc(playerId);
        }

        [Rpc(SendTo.Server)]
        private void ExitCashierServerRpc(ulong playerId)
        {
            if (_cashierPlayerId != playerId) return;

            IsOccupied.Value  = false;
            _cashierPlayerId  = ulong.MaxValue;

            ExitCashierClientRpc(playerId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ExitCashierClientRpc(ulong playerId)
        {
            if (NetworkManager.Singleton.LocalClientId != playerId) return;

            _isCashierMode = false;

            // Cursor kilitle
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            // Kamerayı geri al
            UnlockCamera();

            // UI kapat
            CloseCashierUI();
            SetScreenState(false);

            Debug.Log("[CashRegister] Kasiyer modundan çıkıldı.");
        }

        // ─── Müşteri Geldi ────────────────────────────────────────
        /// <summary>
        /// CustomerAI kasaya gelince çağrılır.
        /// </summary>
        public void CustomerArrived(ulong customerId, List<CartItem> items)
        {
            if (!IsServer || !IsOccupied.Value) return;

            _currentCustomerId = customerId;
            _cart              = new List<CartItem>(items);
            _totalAmount       = 0f;

            foreach (var item in _cart)
                _totalAmount += item.totalPrice;

            HasCustomer.Value = true;
            ShowCustomerCartClientRpc(_cashierPlayerId, _totalAmount);
        }

        // ─── Ödeme Al ────────────────────────────────────────────
        [Rpc(SendTo.Server)]
        public void ChargeCustomerServerRpc(ulong playerId)
        {
            if (_cashierPlayerId != playerId) return;
            if (!HasCustomer.Value || _cart.Count == 0) return;

            // Para ekle
            EconomyManager.Instance?.AddMoneyServerRpc(
                _totalAmount, TransactionCategory.Sale);

            // CustomerAI'ya bildir
            NotifyCustomerPaidClientRpc(_currentCustomerId, _totalAmount);

            // Sepeti temizle
            _cart.Clear();
            _totalAmount       = 0f;
            _currentCustomerId = ulong.MaxValue;
            HasCustomer.Value  = false;

            PaymentSuccessClientRpc(_cashierPlayerId);
        }

        [Rpc(SendTo.Server)]
        public void CancelTransactionServerRpc(ulong playerId)
        {
            if (_cashierPlayerId != playerId) return;
            if (!HasCustomer.Value) return;

            _cart.Clear();
            _totalAmount       = 0f;
            _currentCustomerId = ulong.MaxValue;
            HasCustomer.Value  = false;

            CancelClientRpc(_cashierPlayerId);
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void ShowCustomerCartClientRpc(ulong targetId, float total)
        {
            if (NetworkManager.Singleton.LocalClientId != targetId) return;

            // Sepeti UI'a yaz
            UpdateCartUI();

            if (_totalText != null)
                _totalText.text = $"Toplam: {total:F2}₺";

            _audioSource?.PlayOneShot(_beepClip);
            SetStatus($"Müşteri geldi — {total:F2}₺");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PaymentSuccessClientRpc(ulong targetId)
        {
            if (NetworkManager.Singleton.LocalClientId != targetId) return;

            _audioSource?.PlayOneShot(_paymentClip);

            // UI temizle
            ClearCartUI();
            SetStatus("Ödeme alındı! ✓");
            UIManager.Instance?.ShowNotification("Ödeme alındı! 💰", Color.green);

            // DOTween ile yeşil flash
            if (_uiCanvasGroup != null)
            {
                _uiCanvasGroup.DOKill();
                DOTween.Sequence()
                    .Append(_uiCanvasGroup.DOFade(0.5f, 0.1f))
                    .Append(_uiCanvasGroup.DOFade(1f,   0.2f));
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CancelClientRpc(ulong targetId)
        {
            if (NetworkManager.Singleton.LocalClientId != targetId) return;

            _audioSource?.PlayOneShot(_errorClip);
            ClearCartUI();
            SetStatus("İşlem iptal edildi.");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyCustomerPaidClientRpc(ulong customerId, float amount)
        {
            foreach (var ai in FindObjectsByType<CustomerAI>(FindObjectsInactive.Exclude))
            {
                var netObj = ai.GetComponent<NetworkObject>();
                if (netObj != null && netObj.OwnerClientId == customerId)
                {
                    ai.OnServiceCompleted(CustomerNeed.Shopping, 1f);
                    break;
                }
            }
        }

        // ─── UI ──────────────────────────────────────────────────
        private void OpenCashierUI()
        {
            _cashierUI?.SetActive(true);

            if (_uiCanvasGroup != null)
            {
                _uiCanvasGroup.alpha = 0f;
                _uiCanvasGroup.DOFade(1f, _uiOpenDuration).SetEase(Ease.OutQuad);
            }

            // Buton bağlantıları
            _chargeButton?.onClick.RemoveAllListeners();
            _chargeButton?.onClick.AddListener(() =>
                ChargeCustomerServerRpc(NetworkManager.Singleton.LocalClientId));

            _cancelButton?.onClick.RemoveAllListeners();
            _cancelButton?.onClick.AddListener(() =>
                CancelTransactionServerRpc(NetworkManager.Singleton.LocalClientId));

            _exitButton?.onClick.RemoveAllListeners();
            _exitButton?.onClick.AddListener(() =>
                ExitCashierMode(NetworkManager.Singleton.LocalClientId));
        }

        private void CloseCashierUI()
        {
            if (_uiCanvasGroup != null)
            {
                _uiCanvasGroup.DOFade(0f, 0.2f)
                    .OnComplete(() => _cashierUI?.SetActive(false));
            }
            else
            {
                _cashierUI?.SetActive(false);
            }

            ClearCartUI();
        }

        private void UpdateCartUI()
        {
            ClearCartUI();

            if (_cartContainer == null || _cartItemPrefab == null) return;

            foreach (var item in _cart)
            {
                GameObject row = Instantiate(_cartItemPrefab, _cartContainer);
                var texts = row.GetComponentsInChildren<TextMeshProUGUI>();

                if (texts.Length > 0) texts[0].text = item.product.productName;
                if (texts.Length > 1) texts[1].text = $"x{item.quantity}";
                if (texts.Length > 2) texts[2].text = $"{item.totalPrice:F2}₺";

                // Ürün taranan ses
                _audioSource?.PlayOneShot(_beepClip);
            }
        }

        private void ClearCartUI()
        {
            if (_cartContainer == null) return;
            foreach (Transform child in _cartContainer)
                Destroy(child.gameObject);
        }

        private void SetStatus(string message)
        {
            if (_statusText != null) _statusText.text = message;
        }

        // ─── Kamera Kilitleme ─────────────────────────────────────
        private void LockCameraToRegister()
        {
            if (_cameraLockPoint == null) return;

            // Oyuncunun kamerasını bul
            var playerController = FindAnyObjectByType<WeBussedUp.Player.PlayerController>();

            if (playerController == null) return;

            Camera cam = playerController.GetComponentInChildren<Camera>();
            if (cam == null) return;

            _playerCameraHolder = cam.transform.parent;
            _originalCamPos     = _playerCameraHolder.position;
            _originalCamRot     = _playerCameraHolder.rotation;

            // Kamerayı kasa ekranına kilitle
            _playerCameraHolder
                .DOMove(_cameraLockPoint.position, 0.3f)
                .SetEase(Ease.OutCubic);

            _playerCameraHolder
                .DORotateQuaternion(_cameraLockPoint.rotation, 0.3f)
                .SetEase(Ease.OutCubic);

            // PlayerController'ı devre dışı bırak
            playerController.enabled = false;
        }

        private void UnlockCamera()
        {
            if (_playerCameraHolder == null) return;

            _playerCameraHolder
                .DOMove(_originalCamPos, 0.3f)
                .SetEase(Ease.OutCubic);

            _playerCameraHolder
                .DORotateQuaternion(_originalCamRot, 0.3f)
                .SetEase(Ease.OutCubic)
                .OnComplete(() =>
                {
                    // PlayerController'ı tekrar aç
                    var pc = FindAnyObjectByType<WeBussedUp.Player.PlayerController>();
                    if (pc != null) pc.enabled = true;
                });
        }

        // ─── Görsel Callback'ler ──────────────────────────────────
        private void OnOccupiedChanged(bool oldVal, bool newVal)
        {
            SetScreenState(newVal);
        }

        private void OnCustomerChanged(bool oldVal, bool newVal)
        {
            if (!_isCashierMode) return;
            SetStatus(newVal ? "Müşteri bekleniyor..." : "Müşteri bekleniyor...");
        }

        private void SetScreenState(bool on)
        {
            if (_screenRenderer == null) return;
            _screenRenderer.material = on ? _screenOnMat : _screenOffMat;
        }

        // ─── Gizmos ──────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_cashierStandPoint != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(_cashierStandPoint.position, 0.2f);
                UnityEditor.Handles.Label(
                    _cashierStandPoint.position + Vector3.up, "Kasiyer");
            }

            if (_customerStandPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(_customerStandPoint.position, 0.2f);
                UnityEditor.Handles.Label(
                    _customerStandPoint.position + Vector3.up, "Müşteri");
            }

            if (_cameraLockPoint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(_cameraLockPoint.position, 0.15f);
                UnityEditor.Handles.Label(
                    _cameraLockPoint.position + Vector3.up, "Kamera");
            }
        }
#endif
    }
}
