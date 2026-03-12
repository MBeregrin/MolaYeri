using UnityEngine;
using Unity.Netcode;

public class TrafficManager : NetworkBehaviour
{
    public static TrafficManager Instance;

    [Header("Otoban Ayarları")]
    [SerializeField] private float baseSpawnInterval = 5f; // Otobandan kaç saniyede bir araba geçsin?
    
    [Header("Müşteri Ayarları")]
    [Range(0f, 100f)] 
    [SerializeField] private float entranceProbability = 20f; // Arabanın tesise girme ihtimali (%20)

    // İleride araba prefablarını buralara atacağız
    // public GameObject[] sivilArabaPrefabları;
    // public GameObject[] otobusPrefabları;
    // public Transform spawnPoint; // Arabanın doğacağı yer

    private float spawnTimer;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        // Trafiği SADECE SERVER yönetir. (Senin ekranında araba giriyorsa, arkadaşında da aynı araba girmeli)
        if (!IsServer) return;

        spawnTimer += Time.deltaTime;

        // Belirli bir süre geçtiyse yeni bir araba yolla
        if (spawnTimer >= baseSpawnInterval)
        {
            spawnTimer = 0f;
            CalculateTraffic();
        }
    }

    private void CalculateTraffic()
    {
        // 1. ZAMAN ŞEFİ İLE İLETİŞİM (Gece mi, gündüz mü?)
        float currentTime = 8f; // Eğer TimeManager sahnede yoksa çökmesin diye varsayılan
        if (TimeManager.Instance != null)
        {
            currentTime = TimeManager.Instance.currentTime.Value;
        }

        // ÖRNEK: Gece 02:00 ile 05:00 arası trafik azalır (Spawm süresi uzar)
        if (currentTime > 2f && currentTime < 5f)
        {
            if (Random.value > 0.3f) return; // %70 ihtimalle araba yollama (Gece sakinliği)
        }

        // 2. MÜŞTERİ KARARI: Bu araç tesise girecek mi?
        bool willEnterFacility = Random.Range(0f, 100f) <= entranceProbability;

        if (willEnterFacility)
        {
            SpawnCustomerVehicle();
        }
        else
        {
            SpawnPassingVehicle();
        }
    }

    private void SpawnCustomerVehicle()
    {
        // TODO: İleride Instantiate ile araba yaratıp, NavMesh (veya Waypoint) ile park alanına süreceğiz.
        // İçinden inen NPC'yi markete/kafeye yönlendireceğiz.
        
        Debug.Log("<color=green>[TRAFİK] Bir araç sağa sinyal verdi, Mola Yerine giriyor!</color>");
    }

    private void SpawnPassingVehicle()
    {
        // TODO: Araba otobanda dümdüz devam edip harita sonunda yok olacak (Despawn).
        
        Debug.Log("<color=gray>[TRAFİK] Bir araç transit geçti gitti...</color>");
    }

    // ==========================================
    // GELİŞTİRME (UPGRADE) FONKSİYONLARI
    // ==========================================

    /// <summary>
    /// Tesisine tabela reklamı veya upgrade aldığında bu fonksiyonu çağır.
    /// Gelen müşteri ihtimali artar!
    /// </summary>
    public void IncreasePopularity(float amount)
    {
        entranceProbability += amount;
        entranceProbability = Mathf.Clamp(entranceProbability, 0f, 100f);
        Debug.Log($"[TRAFİK] Tesis popülerliği arttı! Yeni müşteri ihtimali: %{entranceProbability}");
    }
}