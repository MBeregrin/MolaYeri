using UnityEngine;

namespace WeBussedUp.Core.Data
{
    public enum FacilityType  { General, Supermarket, Cafe, GasStation, CarWash, Restroom }
    public enum ItemCategory  { Consumable, Furniture, Equipment, CarCare, Decoration }
    public enum PlacementSurface { Floor, Wall, Ceiling }

    public enum ProductCategory
{
    Food,       // Karton kutu
    Drink,      // Kasa (plastik/metal)
    Snack,      // Küçük karton kutu
    Cleaning,   // Deterjan kutusu
    Automotive, // Yağ/sıvı kutusu
    Other
}

    /// <summary>
    /// Oyundaki her eşyanın kimlik kartı — ürünler, mobilyalar, ekipmanlar.
    /// BuildingSystem ve ShelfSystem bu SO'yu referans alır.
    /// </summary>
    [CreateAssetMenu(fileName = "NewItem", menuName = "We Bussed Up/Yeni Eşya (ItemData)")]
    public class ItemData : ScriptableObject
    {
        [Header("Kimlik")]
        public string      itemID;
        public string      itemName;

        [Header("Tesis & Kategori")]
        public FacilityType  facility;
        public ItemCategory  category;

        [Header("Ekonomi")]
        public float buyPrice;
        public float sellPrice;

        [Header("Görsel & Prefab")]
        public Sprite     icon;
        public GameObject prefab;

        // ─── İnşaat & Grid ───────────────────────────────────────
        [Header("İnşaat Ayarları")]
        [Tooltip("İnşaat modunda (B tuşu) yerleştirilebilir mi?")]
        public bool isBuildable = false;

        [Tooltip("Zemin mi, duvar mı, tavan mı?")]
        public PlacementSurface placementSurface = PlacementSurface.Floor;

        [Tooltip("Grid'de kapladığı alan (X: genişlik, Y: derinlik)")]
        public Vector2Int gridSize = new Vector2Int(1, 1);

        [Tooltip("Pivot merkezde ise ofseti buradan düzelt.\n" +
                 "Örn: prefab'ın pivot'u ortadaysa Y: -0.5 yazar, tabana oturur.\n" +
                 "Raf sistemi de bu değeri slot hesabında kullanır.")]
        public Vector3 pivotOffset = Vector3.zero;

        // ─── Raf Sistemi (ShelfManager için) ─────────────────────
        [Header("Raf Ayarları")]
        [Tooltip("Bu eşya rafa konulabilir mi? (Ürünler: true, Mobilyalar: false)")]
        public bool isShelfable = false;

        [Tooltip("Raf slotunda birbiriyle arasındaki boşluk (metre).\n" +
                 "Ürünler iç içe geçmesin diye kullanılır.")]
        public float shelfSpacing = 0.02f;

        [Tooltip("Raf slotunda ürünün local rotation offset'i.\n" +
                 "Örn: yatay durması gereken ürünler için (90,0,0)")]
        public Vector3 shelfRotationOffset = Vector3.zero;

        [Tooltip("Komşu eşyalar arası boşluk katsayısı (0=yapışık, 1=tam hücre).\n" +
         "Sandalye: 0.05 | Dekor: 0.2 | Raf: 0.6 | Masa: 0.85 | Standart: 0.75")]
        [Range(0f, 1f)]
public float gapMultiplier = 0.75f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // itemID boşsa itemName'den otomatik üret
            if (string.IsNullOrEmpty(itemID) && !string.IsNullOrEmpty(itemName))
                itemID = itemName.ToLower().Replace(" ", "_");

            gridSize.x = Mathf.Max(1, gridSize.x);
            gridSize.y = Mathf.Max(1, gridSize.y);

            shelfSpacing = Mathf.Max(0f, shelfSpacing);
        }
#endif
    }
}