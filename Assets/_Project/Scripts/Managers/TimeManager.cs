using UnityEngine;
using Unity.Netcode;
using System;

namespace WeBussedUp.Core.Managers
{
    /// <summary>
    /// Oyun içi zamanı yönetir. Server yetkili — client'lar NetworkVariable ile senkronize olur.
    /// Gece/gündüz döngüsü event sistemiyle diğer sistemlere bildirilir (ışıklar, NPC rutinleri).
    /// SaveManager SetDay/SetTime üzerinden zaman verisi yükler.
    /// </summary>
    public class TimeManager : NetworkBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────
        public static TimeManager Instance { get; private set; }

        // ─── Inspector ───────────────────────────────────────────
        [Header("Zaman Hızı")]
        [Tooltip("Gerçek kaç dakika = 1 oyun günü? (24 → 1 gerçek dk = 1 oyun saati)")]
        [SerializeField] private float _realMinutesPerGameDay = 24f;

        [Tooltip("Oyun başlangıç saati")]
        [SerializeField] private float _startTime = 8f;

        [Header("Gece/Gündüz Eşikleri")]
        [SerializeField] private float _nightStartTime   = 20f;
        [SerializeField] private float _morningStartTime =  6f;

        [Header("Debug")]
        [SerializeField] private bool _logDayChange = true;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<float> CurrentTimeVar = new NetworkVariable<float>(
            8f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> CurrentDayVar = new NetworkVariable<int>(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Events ──────────────────────────────────────────────
        public event Action         OnNightStarted;
        public event Action         OnMorningStarted;
        public event Action<int>    OnNewDay;       // Yeni gün — gün numarası parametre

        // ─── Public API (SaveManager için) ───────────────────────
        public int   CurrentDay  => CurrentDayVar.Value;
        public float CurrentTime => CurrentTimeVar.Value;
        public bool  IsNight     => _isNight;

        public void SetDay(int day)
        {
            if (!IsServer) return;
            CurrentDayVar.Value = Mathf.Max(1, day);
        }

        public void SetTime(float time)
        {
            if (!IsServer) return;
            CurrentTimeVar.Value = Mathf.Clamp(time, 0f, 24f);
            // Yüklenen saate göre gece/gündüz durumunu güncelle
            _isNight = IsNightHour(CurrentTimeVar.Value);
        }

        public string GetFormattedTime()
        {
            int hours   = Mathf.FloorToInt(CurrentTimeVar.Value);
            int minutes = Mathf.FloorToInt((CurrentTimeVar.Value - hours) * 60f);
            return $"{hours:00}:{minutes:00}";
        }

        // ─── Runtime ─────────────────────────────────────────────
        private bool  _isNight;
        private float _timePerSecond; // Cache — Update'de hesaplamamak için

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
            // 1 saniyede ilerleyen oyun saati miktarı
            _timePerSecond = 24f / (_realMinutesPerGameDay * 60f);

            if (IsServer)
            {
                CurrentTimeVar.Value = _startTime;
                _isNight = IsNightHour(_startTime);
            }

            CurrentTimeVar.OnValueChanged += OnTimeChanged;
        }

        public override void OnNetworkDespawn()
        {
            CurrentTimeVar.OnValueChanged -= OnTimeChanged;
        }

        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (!IsServer) return;

            float newTime = CurrentTimeVar.Value + _timePerSecond * Time.deltaTime;

            if (newTime >= 24f)
            {
                newTime -= 24f;
                int newDay = CurrentDayVar.Value + 1;
                CurrentDayVar.Value = newDay;

                // Gece yarısı geçişi: saat sıfırlandı, gece durumunu
                // doğru hesapla — 0.x sabah eşiğinden küçük → hâlâ gece
                _isNight = IsNightHour(newTime);

                if (_logDayChange)
                    Debug.Log($"[TimeManager] Yeni Gün: {newDay}");

                OnNewDay?.Invoke(newDay);
            }

            // Değişim yoksa NetworkVariable'a yazma — gereksiz trafik önlenir
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (CurrentTimeVar.Value != newTime)
                CurrentTimeVar.Value = newTime;

            CheckDayNightCycle(newTime);
        }

        // ─── Gece/Gündüz ─────────────────────────────────────────
        private void CheckDayNightCycle(float time)
        {
            bool shouldBeNight = IsNightHour(time);

            if (shouldBeNight == _isNight) return; // Değişim yok

            _isNight = shouldBeNight;

            if (_isNight)
            {
                OnNightStarted?.Invoke();
                if (_logDayChange) Debug.Log("[TimeManager] Gece başladı.");
            }
            else
            {
                OnMorningStarted?.Invoke();
                if (_logDayChange) Debug.Log("[TimeManager] Sabah oldu.");
            }
        }

        /// <summary>
        /// Verilen saat gece saati mi?
        /// Gece: nightStart(20) ≤ t < 24 veya 0 ≤ t < morningStart(6)
        /// </summary>
        private bool IsNightHour(float time)
        {
            return time >= _nightStartTime || time < _morningStartTime;
        }

        private void OnTimeChanged(float oldTime, float newTime)
        {
            // Client tarafında UI güncelleme için — TimeManager'ı dinleyen UI buraya bağlanır
        }

#if UNITY_EDITOR
        [ContextMenu("Geceye Atla")]
        private void DebugJumpToNight() => SetTime(_nightStartTime);

        [ContextMenu("Sabaha Atla")]
        private void DebugJumpToMorning() => SetTime(_morningStartTime);
#endif
    }
}