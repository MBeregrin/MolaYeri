using UnityEngine;
using WeBussedUp.Core.Data;

namespace WeBussedUp.Core.Data
{
    /// <summary>
    /// Markette satılan her ürünün verisi.
    /// ItemData ile ilişkili — singleItemPrefab'daki ItemData bu SO'ya referans verir.
    /// ShelfManager ürünleri rafta dizmek için bu SO'yu okur.
    /// BoxItem koli içeriği olarak bu SO'yu taşır.
    /// </summary>
    [CreateAssetMenu(fileName = "New_Product", menuName = "We Bussed Up/Yeni Ürün (ProductData)")]
    public class ProductData : ScriptableObject
    {
        // Mevcut alanların altına ekle
[Header("Ambalaj")]
[Tooltip("Ürün tipi — kutu görselini belirler")]
public ProductCategory productCategory = ProductCategory.Food;
        // ─── Kimlik ──────────────────────────────────────────────
        [Header("Kimlik")]
        [Tooltip("Her ürüne unique string ver. Örn: 'chips_spicy', 'cola_330ml'")]
        public string productID;
        public string productName;
        public Sprite icon;

        // ─── Prefablar ───────────────────────────────────────────
        [Header("Prefablar")]
        [Tooltip("Rafa dizilecek tekli ürün prefabı")]
        public GameObject singleItemPrefab;

        [Tooltip("Oyuncunun taşıyacağı koli prefabı (BoxItem bileşeni içermeli)")]
        public GameObject boxPrefab;

        // ─── Raf Dizilimi ────────────────────────────────────────
        [Header("Raf Dizilimi")]
        [Tooltip("Rafta kapladığı genişlik (metre). ShelfManager slot hesabında kullanır.")]
        public float itemWidth = 0.1f;

        [Tooltip("Rafta kapladığı derinlik (metre).")]
        public float itemDepth = 0.1f;

        [Tooltip("Ürünler arası boşluk (metre). Üst üste geçmeyi önler.\n" +
                 "ItemData.shelfSpacing ile aynı mantık — ürün bazında override.")]
        public float shelfSpacing = 0.02f;

        [Tooltip("Rafta ürünün local rotation offset'i.\n" +
                 "Örn: yatık durması gereken ürünler için (90, 0, 0)")]
        public Vector3 shelfRotationOffset = Vector3.zero;

        [Tooltip("Raf slotunda kaç adet üst üste istiflenir? (0 = istif yok)")]
        public int maxStackCount = 0;

        // ─── Ekonomi ─────────────────────────────────────────────
        [Header("Ekonomi")]
        [Tooltip("Toptancıdan alış fiyatı")]
        public float buyPrice;

        [Tooltip("Marketteki varsayılan satış fiyatı")]
        public float defaultSellPrice;

        [Tooltip("Koli başına kaç adet ürün gelir?")]
        public int unitsPerBox = 10;

        // ─── Stok ────────────────────────────────────────────────
        [Header("Stok Ayarları")]
        [Tooltip("Başlangıç stok miktarı")]
        public int initialStock = 0;

        [Tooltip("Bu seviyenin altına düşünce uyarı ver (UI için)")]
        public int lowStockThreshold = 5;

        // ─── Hesaplamalar ─────────────────────────────────────────
        /// <summary>Kar marjı yüzdesi</summary>
        public float ProfitMargin => buyPrice > 0
            ? (defaultSellPrice - buyPrice) / buyPrice * 100f
            : 0f;

        /// <summary>Koli toplam maliyeti</summary>
        public float BoxCost => buyPrice * unitsPerBox;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(productID) && !string.IsNullOrEmpty(productName))
                productID = productName.ToLower().Replace(" ", "_");

            itemWidth        = Mathf.Max(0.01f, itemWidth);
            itemDepth        = Mathf.Max(0.01f, itemDepth);
            shelfSpacing     = Mathf.Max(0f,    shelfSpacing);
            unitsPerBox      = Mathf.Max(1,      unitsPerBox);
            lowStockThreshold = Mathf.Max(0,     lowStockThreshold);

            if (defaultSellPrice < buyPrice)
                Debug.LogWarning($"[ProductData] '{productName}' satış fiyatı alış fiyatından düşük!", this);
        }
#endif
    }
}