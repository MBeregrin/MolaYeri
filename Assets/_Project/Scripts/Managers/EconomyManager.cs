using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections.Generic;

namespace WeBussedUp.Core.Managers
{
    /// <summary>
    /// Şirket kasasını yönetir. Server yetkili — para sadece server'da değişir.
    /// Tüm işlemler TransactionRecord ile loglanır (UI ve debug için).
    /// </summary>
    public class EconomyManager : NetworkBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────
        public static EconomyManager Instance { get; private set; }

        // ─── Inspector ───────────────────────────────────────────
        [Header("Başlangıç Ayarları")]
        [SerializeField] private float _startingMoney = 1000f;

        [Header("Debug")]
        [SerializeField] private bool _logTransactions = true;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<float> CompanyMoney = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Events ──────────────────────────────────────────────
        /// <summary>Para değiştiğinde — UI binding için</summary>
        public event Action<float> OnMoneyChanged;

        /// <summary>Harcama başarısız olduğunda — UI uyarısı için</summary>
        public event Action<float> OnInsufficientFunds;

        /// <summary>Yeni transaction kaydedildiğinde — finans paneli için</summary>
        public event Action<TransactionRecord> OnTransactionRecorded;

        // ─── Transaction History ─────────────────────────────────
        private readonly List<TransactionRecord> _transactionHistory = new();
        public IReadOnlyList<TransactionRecord> TransactionHistory => _transactionHistory;

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            CompanyMoney.OnValueChanged += HandleMoneyChanged;

            if (IsServer)
                CompanyMoney.Value = _startingMoney;
        }

        public override void OnNetworkDespawn()
        {
            CompanyMoney.OnValueChanged -= HandleMoneyChanged;
        }

        // ─── Public API ──────────────────────────────────────────
        public bool HasEnoughMoney(float amount) => CompanyMoney.Value >= amount;

        // ─── Server RPC ──────────────────────────────────────────
        /// <summary>Kasaya para ekle — satış, bonus vb.</summary>
        [Rpc(SendTo.Server)]
        public void AddMoneyServerRpc(float amount, TransactionCategory category = TransactionCategory.Sale)
        {
            if (amount <= 0) return;

            CompanyMoney.Value += amount;
            RecordTransaction(amount, category, isIncome: true);
        }

        /// <summary>Kasadan para harca — alım, inşaat vb.</summary>
        [Rpc(SendTo.Server)]
        public void SpendMoneyServerRpc(float amount, TransactionCategory category = TransactionCategory.Purchase)
        {
            if (amount <= 0) return;

            if (CompanyMoney.Value < amount)
            {
                NotifyInsufficientFundsClientRpc(amount);
                return;
            }

            CompanyMoney.Value -= amount;
            RecordTransaction(amount, category, isIncome: false);
        }

        // ─── Client RPC ──────────────────────────────────────────
        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyInsufficientFundsClientRpc(float attemptedAmount)
        {
            OnInsufficientFunds?.Invoke(attemptedAmount);
            Debug.LogWarning($"[EconomyManager] Yetersiz bakiye! Gereken: {attemptedAmount:F2}₺, Mevcut: {CompanyMoney.Value:F2}₺");
        }

        // ─── Private ─────────────────────────────────────────────
        private void HandleMoneyChanged(float oldValue, float newValue)
        {
            OnMoneyChanged?.Invoke(newValue);

            if (_logTransactions)
                Debug.Log($"[EconomyManager] Bakiye: {oldValue:F2}₺ → {newValue:F2}₺");
        }

        private void RecordTransaction(float amount, TransactionCategory category, bool isIncome)
        {
            var record = new TransactionRecord(amount, category, isIncome);
            _transactionHistory.Add(record);
            OnTransactionRecorded?.Invoke(record);
        }
    }

    // ─── Veri Yapıları ───────────────────────────────────────────
    public enum TransactionCategory
    {
        Sale,        // Müşteriye satış
        Purchase,    // Toptancıdan alım
        Construction,// İnşaat/eşya yerleştirme
        Wage,        // Personel maaşı
        Utility,     // Fatura (elektrik, su)
        Fuel,        // Benzin alımı
        Bonus,       // Bonus/ödül
        Other
    }

    [Serializable]
    public struct TransactionRecord
    {
        public float               Amount;
        public TransactionCategory Category;
        public bool                IsIncome;
        public DateTime            Timestamp;

        public TransactionRecord(float amount, TransactionCategory category, bool isIncome)
        {
            Amount    = amount;
            Category  = category;
            IsIncome  = isIncome;
            Timestamp = DateTime.Now;
        }

        public override string ToString() =>
            $"[{Timestamp:HH:mm:ss}] {(IsIncome ? "+" : "-")}{Amount:F2}₺ ({Category})";
    }
}