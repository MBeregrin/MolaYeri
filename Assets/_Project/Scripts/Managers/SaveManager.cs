using UnityEngine;
using Unity.Netcode;
using System.IO;

// 1. KAYIT DOSYASI ŞABLONU (Neleri kaydedeceğiz?)
[System.Serializable]
public class GameSaveData
{
    public float money;
    public int currentDay;
    public float currentTime;
    
    // TODO: İleride buraya "List<PlacedItemData> placedItems" ekleyeceğiz.
    // Böylece oyuncunun koyduğu sandalyelerin, rafların yerini de hatırlayacağız.
}

public class SaveManager : NetworkBehaviour
{
    public static SaveManager Instance;
    
    // Kayıt dosyasının bilgisayarda duracağı yer
    private string savePath;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // persistentDataPath: Unity'nin her bilgisayarda garanti olarak yazma izni olan gizli klasördür.
        savePath = Application.persistentDataPath + "/MolaYeriSave.json";
    }

    public override void OnNetworkSpawn()
    {
        // SADECE SERVER (Kurucu) eski kaydı okur ve dünyayı inşa eder.
        // Client'lar (Katılanlar) oyuna girdiğinde Server zaten her şeyi yüklemiş olur.
        if (IsServer)
        {
            LoadGame(); 
        }
    }

    // Oyun kapanırken (veya kurucu çıkarken) otomatik kaydet
    private void OnApplicationQuit()
    {
        if (IsServer) SaveGame();
    }

    // ==========================================
    // KAYDETME VE YÜKLEME FONKSİYONLARI
    // ==========================================

    public void SaveGame()
    {
        if (!IsServer) return;

        // 1. Şablonu oluştur
        GameSaveData data = new GameSaveData();
        
        // 2. Şeflerden güncel verileri topla
        if (EconomyManager.Instance != null) 
            data.money = EconomyManager.Instance.companyMoney.Value;
            
        if (TimeManager.Instance != null)
        {
            data.currentDay = TimeManager.Instance.currentDay.Value;
            data.currentTime = TimeManager.Instance.currentTime.Value;
        }

        // 3. Veriyi JSON (Metin) formatına çevir ve dosyaya yaz
        string json = JsonUtility.ToJson(data, true); // 'true' yazısı JSON'u okunaklı formatlar
        File.WriteAllText(savePath, json);
        
        Debug.Log($"<color=cyan>[KAYIT] Oyun başarıyla kaydedildi! Dosya Yolu: {savePath}</color>");
    }

    public void LoadGame()
    {
        if (!IsServer) return;

        // Kayıt dosyası var mı diye bak
        if (File.Exists(savePath))
        {
            // 1. Metin dosyasını oku ve JSON'dan bizim Şablona (GameSaveData) çevir
            string json = File.ReadAllText(savePath);
            GameSaveData data = JsonUtility.FromJson<GameSaveData>(json);

            // 2. Okunan verileri Şeflere geri dağıt
            if (EconomyManager.Instance != null) 
                EconomyManager.Instance.companyMoney.Value = data.money;
                
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.currentDay.Value = data.currentDay;
                TimeManager.Instance.currentTime.Value = data.currentTime;
            }

            Debug.Log("<color=cyan>[KAYIT] Eski kayıt başarıyla yüklendi!</color>");
        }
        else
        {
            // İlk defa oynanıyorsa
            Debug.LogWarning("[KAYIT] Kayıt dosyası bulunamadı. Yeni bir tesise başlanıyor!");
            
            // Eğer ilk oyunsa başlangıç parasını falan burada ayarlayabilirsin.
            // Örn: EconomyManager.Instance.companyMoney.Value = 1500f;
        }
    }

    // İstediğin zaman manuel kaydetmek için (Örn: Menüdeki "Oyunu Kaydet" butonu)
    public void ManualSave()
    {
        SaveGame();
    }
}