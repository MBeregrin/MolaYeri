using UnityEngine;
using Unity.Netcode;
using System;

public class TimeManager : NetworkBehaviour
{
    public static TimeManager Instance;

    [Header("Zaman Ayarları")]
    // Gerçek hayattaki kaç dakika, oyundaki 1 güne (24 saate) eşit olsun?
    // Örn: 24 yaparsan, gerçek hayattaki 1 dakika oyunda 1 saat olur.
    [SerializeField] private float realMinutesPerGameDay = 24f; 
    
    // Ağ üzerinde senkronize saat (0.00 ile 24.00 arası) - Oyuna sabah 08:00'da başlar.
    public NetworkVariable<float> currentTime = new NetworkVariable<float>(8f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    // Kaçıncı gündeyiz?
    public NetworkVariable<int> currentDay = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Gece/Gündüz Olayları")]
    [SerializeField] private float nightStartTime = 20f; // Akşam 8 (Işıklar yanar)
    [SerializeField] private float morningStartTime = 6f; // Sabah 6 (Işıklar söner)

    // Diğer scriptlere haber vermek için Event'ler (Sokak lambaları vb. bunları dinleyecek)
    public event Action OnNightStarted;
    public event Action OnMorningStarted;

    private bool isNight = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        // Zamanı SADECE SERVER (Kurucu) ilerletir. 
        // Client'lar (Katılanlar) sadece Server'dan gelen yeni saati okur.
        if (!IsServer) return;

        // Zamanı hesapla
        float timeToAdvance = (Time.deltaTime / (realMinutesPerGameDay * 60f)) * 24f;
        currentTime.Value += timeToAdvance;

        // 24 saati doldurduk mu? Yeni güne geç!
        if (currentTime.Value >= 24f)
        {
            currentTime.Value = 0f; // Gece yarısı sıfırla
            currentDay.Value++;
            Debug.Log($"[ZAMAN] YENİ GÜN! Gün: {currentDay.Value}");
        }

        CheckDayNightCycle();
    }

    private void CheckDayNightCycle()
    {
        // Gece mi oldu?
        if (!isNight && (currentTime.Value >= nightStartTime || currentTime.Value < morningStartTime))
        {
            isNight = true;
            OnNightStarted?.Invoke(); // Tüm ışıklara "YANIN" emri gider
            Debug.Log("[ZAMAN] Akşam oldu, ışıklar yandı.");
        }
        // Sabah mı oldu?
        else if (isNight && (currentTime.Value >= morningStartTime && currentTime.Value < nightStartTime))
        {
            isNight = false;
            OnMorningStarted?.Invoke(); // Tüm ışıklara "SÖNÜN" emri gider
            Debug.Log("[ZAMAN] Sabah oldu, ışıklar söndü.");
        }
    }

    // ==========================================
    // YARDIMCI FONKSİYONLAR (UI VEYA DİĞER SCRIPTLER İÇİN)
    // ==========================================

    // Saati ve dakikayı ekrana yazdırmak için (Örn: "14:30")
    public string GetFormattedTime()
    {
        int hours = Mathf.FloorToInt(currentTime.Value);
        int minutes = Mathf.FloorToInt((currentTime.Value - hours) * 60f);
        return string.Format("{0:00}:{1:00}", hours, minutes);
    }
}