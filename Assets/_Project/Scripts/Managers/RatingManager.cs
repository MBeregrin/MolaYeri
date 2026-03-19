using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System;
using WeBussedUp.UI;
using WeBussedUp.Core.Managers;
using WeBussedUp.Core;
using WeBussedUp.NPC;

namespace WeBussedUp.Core.Managers
{
    [Serializable]
    public struct RatingEntry
    {
        public float  Score;        // 1-5 arası
        public string Category;     // "Temizlik", "Hız", "Fiyat" vb.
        public float  Timestamp;    // Time.time

        public RatingEntry(float score, string category)
        {
            Score     = Mathf.Clamp(score, 1f, 5f);
            Category  = category;
            Timestamp = Time.time;
        }
    }

    /// <summary>
    /// Tesis genelindeki müşteri memnuniyeti ve yıldız sistemini yönetir.
    /// CustomerAI → ReportRating() → ortalama hesaplanır → UI güncellenir.
    /// Temizlik, fiyat, hiz ve hizmet kalitesi ayrı ayrı izlenir.
    /// </summary>
    public class RatingManager : NetworkBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────
        public static RatingManager Instance { get; private set; }

        // ─── Inspector ───────────────────────────────────────────
        [Header("Ağırlıklar (Toplam 1 olmalı)")]
        [SerializeField] private float _serviceWeight     = 0.35f;
        [SerializeField] private float _cleanlinessWeight = 0.25f;
        [SerializeField] private float _priceWeight       = 0.20f;
        [SerializeField] private float _speedWeight       = 0.20f;

        [Header("Günlük Sıfırlama")]
        [SerializeField] private bool _resetDailyRatings = true;

        [Header("Debug")]
        [SerializeField] private bool _logRatings = true;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<float> OverallRating = new NetworkVariable<float>(
            5f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> TotalRatingsCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        private List<float> _serviceRatings     = new();
        private List<float> _cleanlinessRatings = new();
        private List<float> _priceRatings       = new();
        private List<float> _speedRatings       = new();

        // Kategori ortalamaları
        public float ServiceAvg     => Average(_serviceRatings);
        public float CleanlinessAvg => Average(_cleanlinessRatings);
        public float PriceAvg       => Average(_priceRatings);
        public float SpeedAvg       => Average(_speedRatings);

        // ─── Events ──────────────────────────────────────────────
        public event Action<float> OnRatingUpdated;  // Yeni ortalama

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

        public override void OnNetworkSpawn()
        {
            OverallRating.OnValueChanged     += OnOverallRatingChanged;
            TotalRatingsCount.OnValueChanged += OnRatingsCountChanged;

            // TimeManager gün değişimine abone ol
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnNewDay += HandleNewDay;
        }

        public override void OnNetworkDespawn()
        {
            OverallRating.OnValueChanged     -= OnOverallRatingChanged;
            TotalRatingsCount.OnValueChanged -= OnRatingsCountChanged;

            if (TimeManager.Instance != null)
                TimeManager.Instance.OnNewDay -= HandleNewDay;
        }

        // ─── Public API ──────────────────────────────────────────
        /// <summary>
        /// CustomerAI ayrılırken çağırır.
        /// satisfaction: 0-100 arası müşteri memnuniyeti
        /// </summary>
        [Rpc(SendTo.Server)]
        public void ReportRatingServerRpc(float satisfaction,
                                          float serviceQuality,
                                          float cleanlinessScore,
                                          float priceScore,
                                          float speedScore)
        {
            // 0-100 → 1-5
            float stars = Mathf.Lerp(1f, 5f, satisfaction / 100f);

            _serviceRatings.Add(    Mathf.Lerp(1f, 5f, serviceQuality));
            _cleanlinessRatings.Add(Mathf.Lerp(1f, 5f, cleanlinessScore));
            _priceRatings.Add(      Mathf.Lerp(1f, 5f, priceScore));
            _speedRatings.Add(      Mathf.Lerp(1f, 5f, speedScore));

            TotalRatingsCount.Value++;

            RecalculateOverall();

            if (_logRatings)
                Debug.Log($"[RatingManager] Yeni değerlendirme: {stars:F1}⭐ " +
                          $"(Hizmet:{serviceQuality:F1} Temizlik:{cleanlinessScore:F1} " +
                          $"Fiyat:{priceScore:F1} Hız:{speedScore:F1})");
        }

        /// <summary>
        /// Sadece genel memnuniyetle hızlı değerlendirme.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void ReportSimpleRatingServerRpc(float satisfaction)
        {
            float normalized = satisfaction / 100f;
            _serviceRatings.Add(    Mathf.Lerp(1f, 5f, normalized));
            _cleanlinessRatings.Add(Mathf.Lerp(1f, 5f, normalized));
            _priceRatings.Add(      Mathf.Lerp(1f, 5f, normalized));
            _speedRatings.Add(      Mathf.Lerp(1f, 5f, normalized));

            TotalRatingsCount.Value++;
            RecalculateOverall();
        }

        /// <summary>
        /// Temizlik bonusu — CleaningTask tamamlandığında çağrılır.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void AddCleanlinessBoostServerRpc(float boost)
        {
            // Mevcut temizlik ortalamasına bonus ekle (max 5)
            float current = CleanlinessAvg;
            _cleanlinessRatings.Add(Mathf.Min(current + boost, 5f));
            RecalculateOverall();
        }

        public int GetStars() => Mathf.RoundToInt(OverallRating.Value);

        // ─── Hesaplama ───────────────────────────────────────────
        private void RecalculateOverall()
        {
            float overall = ServiceAvg     * _serviceWeight
                          + CleanlinessAvg * _cleanlinessWeight
                          + PriceAvg       * _priceWeight
                          + SpeedAvg       * _speedWeight;

            OverallRating.Value = Mathf.Clamp(overall, 1f, 5f);
        }

        private float Average(List<float> list)
        {
            if (list == null || list.Count == 0) return 5f; // Başlangıçta mükemmel
            float sum = 0f;
            foreach (var v in list) sum += v;
            return sum / list.Count;
        }

        // ─── Günlük Sıfırlama ────────────────────────────────────
        private void HandleNewDay(int day)
        {
            if (!IsServer || !_resetDailyRatings) return;

            // Günlük puanları sıfırla ama genel ortalamanın %50'sini koru
            float carryOver = OverallRating.Value;

            _serviceRatings.Clear();
            _cleanlinessRatings.Clear();
            _priceRatings.Clear();
            _speedRatings.Clear();

            // Carry over — yeni güne sıfırdan başlamak yerine önceki puanı taşı
            _serviceRatings.Add(carryOver);
            _cleanlinessRatings.Add(carryOver);
            _priceRatings.Add(carryOver);
            _speedRatings.Add(carryOver);

            TotalRatingsCount.Value = 0;
            RecalculateOverall();

            Debug.Log($"[RatingManager] Gün {day} — Puanlar sıfırlandı. Carry over: {carryOver:F1}⭐");
        }

        // ─── Callbacks ───────────────────────────────────────────
        private void OnOverallRatingChanged(float oldVal, float newVal)
        {
            OnRatingUpdated?.Invoke(newVal);

            // UIManager'ı güncelle
            UIManager.Instance?.UpdateSatisfaction(newVal / 5f * 100f);

            // Popülerlik etkisi — rating yükseldikçe daha fazla müşteri gelir
            float popularityBoost = (newVal - 3f) * 2f; // 3 yıldız nötr
            TrafficManager.Instance?.IncreasePopularityServerRpc(popularityBoost);
        }

        private void OnRatingsCountChanged(int oldVal, int newVal)
        {
            if (_logRatings)
                Debug.Log($"[RatingManager] Toplam değerlendirme: {newVal} | Ortalama: {OverallRating.Value:F2}⭐");
        }

        // ─── Save/Load ───────────────────────────────────────────
        /// <summary>
        /// SaveManager tarafından çağrılır.
        /// </summary>
        public float GetSaveableRating() => OverallRating.Value;

        public void LoadRating(float savedRating)
        {
            if (!IsServer) return;
            OverallRating.Value = Mathf.Clamp(savedRating, 1f, 5f);

            // Kayıtlı puanla listeleri başlat
            _serviceRatings.Add(savedRating);
            _cleanlinessRatings.Add(savedRating);
            _priceRatings.Add(savedRating);
            _speedRatings.Add(savedRating);
        }
    }
}