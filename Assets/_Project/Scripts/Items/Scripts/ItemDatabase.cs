using UnityEngine;
using System.Collections.Generic;
using WeBussedUp.Core.Data;

namespace WeBussedUp.Core
{
    /// <summary>
    /// Tüm ItemData ve ProductData SO'larının merkezi kayıt defteri.
    /// Sahneye bir kez eklenir, DontDestroyOnLoad ile yaşar.
    /// BuildingSystem, ShelfManager, BoxItem bu database'i referans alır.
    /// </summary>
    public class ItemDatabase : MonoBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────
        public static ItemDatabase Instance { get; private set; }

        // ─── Inspector ───────────────────────────────────────────
        [Header("Eşyalar (ItemData)")]
        [SerializeField] private List<ItemData> _allItems = new List<ItemData>();

        [Header("Ürünler (ProductData)")]
        [SerializeField] private List<ProductData> _allProducts = new List<ProductData>();

        // ─── Runtime Lookup (O(1)) ────────────────────────────────
        private Dictionary<string, ItemData>    _itemLookup    = new();
        private Dictionary<string, ProductData> _productLookup = new();

        // ─── Public Read-only ─────────────────────────────────────
        public IReadOnlyList<ItemData>    allItems    => _allItems;
        public IReadOnlyList<ProductData> allProducts => _allProducts;

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

            BuildLookups();
        }

        // ─── Lookup Build ─────────────────────────────────────────
        private void BuildLookups()
        {
            _itemLookup.Clear();
            foreach (var item in _allItems)
            {
                if (item == null) continue;

                if (_itemLookup.ContainsKey(item.itemID))
                {
                    Debug.LogWarning($"[ItemDatabase] Duplicate itemID: '{item.itemID}' — '{item.itemName}' atlandı.", item);
                    continue;
                }
                _itemLookup[item.itemID] = item;
            }

            _productLookup.Clear();
            foreach (var product in _allProducts)
            {
                if (product == null) continue;

                if (_productLookup.ContainsKey(product.productID))
                {
                    Debug.LogWarning($"[ItemDatabase] Duplicate productID: '{product.productID}' — '{product.productName}' atlandı.", product);
                    continue;
                }
                _productLookup[product.productID] = product;
            }

            Debug.Log($"[ItemDatabase] {_itemLookup.Count} eşya, {_productLookup.Count} ürün yüklendi.");
        }

        // ─── Public API ───────────────────────────────────────────
        /// <summary>O(1) eşya arama.</summary>
        public ItemData GetItemByID(string id)
        {
            if (_itemLookup.TryGetValue(id, out var item)) return item;
            Debug.LogWarning($"[ItemDatabase] ItemID bulunamadı: '{id}'");
            return null;
        }

        /// <summary>O(1) ürün arama.</summary>
        public ProductData GetProductByID(string id)
        {
            if (_productLookup.TryGetValue(id, out var product)) return product;
            Debug.LogWarning($"[ItemDatabase] ProductID bulunamadı: '{id}'");
            return null;
        }

        /// <summary>Belirli bir tesise ait eşyaları filtrele. UI tablet için.</summary>
        public List<ItemData> GetItemsByFacility(FacilityType facility)
        {
            var result = new List<ItemData>();
            foreach (var item in _allItems)
                if (item != null && item.facility == facility)
                    result.Add(item);
            return result;
        }

        /// <summary>Belirli kategorideki eşyaları filtrele.</summary>
        public List<ItemData> GetItemsByCategory(ItemCategory category)
        {
            var result = new List<ItemData>();
            foreach (var item in _allItems)
                if (item != null && item.category == category)
                    result.Add(item);
            return result;
        }

        /// <summary>Rafa konulabilir tüm ürünleri getir. ShelfManager için.</summary>
        public List<ProductData> GetAllShelfableProducts()
        {
            var result = new List<ProductData>();
            foreach (var product in _allProducts)
                if (product != null)
                    result.Add(product);
            return result;
        }

#if UNITY_EDITOR
        [ContextMenu("Lookup Tablosunu Yeniden Oluştur")]
        private void RebuildInEditor() => BuildLookups();
#endif
    }
}