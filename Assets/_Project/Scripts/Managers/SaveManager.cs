using UnityEngine;
using Unity.Netcode;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using WeBussedUp.Core.Managers;
using WeBussedUp.Core.Data;
using WeBussedUp.Gameplay.Building;

namespace WeBussedUp.Core.Managers
{
    [Serializable]
    public class PlacedItemSaveData
    {
        public string itemID;
        public float  posX, posY, posZ;
        public float  rotX, rotY, rotZ, rotW;

        public Vector3    Position => new Vector3(posX, posY, posZ);
        public Quaternion Rotation => new Quaternion(rotX, rotY, rotZ, rotW);

        public PlacedItemSaveData() { }

        public PlacedItemSaveData(string id, Vector3 pos, Quaternion rot)
        {
            itemID = id;
            posX = pos.x; posY = pos.y; posZ = pos.z;
            rotX = rot.x; rotY = rot.y; rotZ = rot.z; rotW = rot.w;
        }
    }

    [Serializable]
    public class ShelfSaveData
    {
        public string networkObjectID;
        public string productID;
        public int    currentStock;
    }

    [Serializable]
    public class GameSaveData
    {
        public int    saveVersion  = 1;
        public string saveDate;

        public float money;
        public int   currentDay;
        public float currentTime;
        public float overallRating = 5f;

        public List<PlacedItemSaveData> placedItems = new();
        public List<ShelfSaveData>      shelves     = new();
    }

    /// <summary>
    /// Oyun verilerini JSON olarak kaydeder/yükler.
    /// Sadece Server okur/yazar — client'lar NetworkVariable senkronizasyonuyla güncellenir.
    /// Otosave desteği vardır.
    /// </summary>
    public class SaveManager : NetworkBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────
        public static SaveManager Instance { get; private set; }

        // ─── Inspector ───────────────────────────────────────────
        [Header("Kayıt Ayarları")]
        [SerializeField] private string _saveFileName     = "WeBussedUp_Save.json";
        [SerializeField] private bool   _autoSave         = true;
        [SerializeField] private float  _autoSaveInterval = 120f;

        [Header("Debug")]
        [SerializeField] private bool _logSaveLoad = true;

        // ─── Runtime ─────────────────────────────────────────────
        private string    _savePath;
        private Coroutine _autoSaveCoroutine;

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance  = this;
            _savePath = Path.Combine(Application.persistentDataPath, _saveFileName);
            DontDestroyOnLoad(gameObject);
        }

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            LoadGame();

            if (_autoSave)
                _autoSaveCoroutine = StartCoroutine(AutoSaveRoutine());
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;

            if (_autoSaveCoroutine != null)
                StopCoroutine(_autoSaveCoroutine);

            SaveGame();
        }

        private void OnApplicationQuit()
        {
            if (IsServer) SaveGame();
        }

        // ─── Public API ──────────────────────────────────────────
        public void ManualSave()
        {
            if (!IsServer) { RequestSaveServerRpc(); return; }
            SaveGame();
        }

        public bool HasSaveFile() => File.Exists(_savePath);

        public void DeleteSave()
        {
            if (!IsServer) return;
            if (File.Exists(_savePath))
            {
                File.Delete(_savePath);
                Log("Kayıt dosyası silindi.");
            }
        }

        // ─── Server RPC ──────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void RequestSaveServerRpc() => SaveGame();

        // ─── Save ────────────────────────────────────────────────
        private void SaveGame()
        {
            if (!IsServer) return;

            try
            {
                var data = new GameSaveData
                {
                    saveDate      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    money         = EconomyManager.Instance?.CompanyMoney.Value ?? 0f,
                    currentDay    = TimeManager.Instance?.CurrentDay            ?? 0,
                    currentTime   = TimeManager.Instance?.CurrentTime           ?? 0f,
                    overallRating = RatingManager.Instance?.GetSaveableRating() ?? 5f,
                    placedItems   = CollectPlacedItems(),
                    shelves       = CollectShelfData(),
                };

                string json = JsonUtility.ToJson(data, prettyPrint: true);
                File.WriteAllText(_savePath, json);

                Log($"Kaydedildi → {_savePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Kayıt hatası: {e.Message}");
            }
        }

        // ─── Load ────────────────────────────────────────────────
        private void LoadGame()
        {
            if (!IsServer) return;

            if (!File.Exists(_savePath))
            {
                Log("Kayıt bulunamadı — yeni oyun başlatılıyor.");
                return;
            }

            try
            {
                string       json = File.ReadAllText(_savePath);
                GameSaveData data = JsonUtility.FromJson<GameSaveData>(json);

                if (EconomyManager.Instance != null)
                    EconomyManager.Instance.CompanyMoney.Value = data.money;

                if (TimeManager.Instance != null)
                {
                    TimeManager.Instance.SetDay(data.currentDay);
                    TimeManager.Instance.SetTime(data.currentTime);
                }

                RatingManager.Instance?.LoadRating(data.overallRating);

                RestorePlacedItems(data.placedItems);

                Log($"Yüklendi — Gün {data.currentDay}, Bakiye {data.money:F2}₺, Rating {data.overallRating:F1}⭐");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Yükleme hatası: {e.Message}");
            }
        }

        // ─── Collect & Restore ───────────────────────────────────
        private List<PlacedItemSaveData> CollectPlacedItems()
        {
            var result = new List<PlacedItemSaveData>();

            var buildingItems = FindObjectsByType<BuildingItem>(FindObjectsInactive.Exclude);

            foreach (var item in buildingItems)
            {
                if (item.ItemData == null) continue;
                result.Add(new PlacedItemSaveData(
                    item.ItemData.itemID,
                    item.transform.position,
                    item.transform.rotation
                ));
            }

            return result;
        }

        private List<ShelfSaveData> CollectShelfData()
        {
            // ShelfManager kayıt sistemi ileride eklenecek
            return new List<ShelfSaveData>();
        }

        private void RestorePlacedItems(List<PlacedItemSaveData> items)
        {
            if (items == null || items.Count == 0) return;

            foreach (var saved in items)
            {
                ItemData itemData = ItemDatabase.Instance?.GetItemByID(saved.itemID);
                if (itemData == null || itemData.prefab == null) continue;

                GameObject placed = Instantiate(itemData.prefab, saved.Position, saved.Rotation);

                if (placed.TryGetComponent(out BuildingItem buildingItem))
                    buildingItem.Initialize(itemData);

                if (placed.TryGetComponent(out NetworkObject netObj))
                    netObj.Spawn();
            }
        }

        // ─── Autosave ────────────────────────────────────────────
        private IEnumerator AutoSaveRoutine()
        {
            var wait = new WaitForSeconds(_autoSaveInterval);
            while (true)
            {
                yield return wait;
                SaveGame();
                Log("Otomatik kayıt tamamlandı.");
            }
        }

        // ─── Util ────────────────────────────────────────────────
        private void Log(string msg)
        {
            if (_logSaveLoad)
                Debug.Log($"[SaveManager] {msg}");
        }
    }
}