using UnityEngine;
using System.Collections.Generic;

public class ItemDatabase : MonoBehaviour
{
    // Singleton mantığı: Oyundaki her script buraya kolayca ulaşabilsin diye
    public static ItemDatabase Instance;

    [Header("OYUNDAKİ TÜM EŞYALAR")]
    // İşte aradığın List mantığı burada!
    public List<ItemData> allItems = new List<ItemData>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // İLERİSİ İÇİN SİHİRLİ FONKSİYON:
    // Örneğin tablet ekranından "cips_acili" siparişi verildi.
    // Bu fonksiyon gidip listeyi tarar ve o cipsin tüm bilgilerini (fiyat, prefab) sana getirir.
    public ItemData GetItemByID(string id)
    {
        foreach (var item in allItems)
        {
            if (item.itemID == id) return item;
        }
        Debug.LogWarning("Eşya bulunamadı ID: " + id);
        return null;
    }
}