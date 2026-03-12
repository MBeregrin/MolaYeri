using UnityEngine;

// Eşya Türleri
public enum BuildingCategory
{
    Standard, // Normal eşyalar (Standart boşluk)
    Shelf,    // Raflar (NPC geçebilsin diye aralıklı)
    Chair,    // Sandalyeler (Dip dibe girebilsin)
    Decor,    // Çiçek/Böcek (Sıkışık olabilir)
    Table     // Masalar (Ferah olsun)
}

public class BuildingItem : MonoBehaviour
{
    [Header("Eşya Kimliği")]
    public BuildingCategory category = BuildingCategory.Standard;

    [Header("Özel Ayar (İstersen elle gir)")]
    public bool useRefinedGap = false;
    [Range(0.01f, 1f)] public float customGap = 0.75f;

    // Sistemin okuyacağı boşluk değeri (Daha küçük = Daha sıkışık)
    public float GetGapMultiplier()
    {
        if (useRefinedGap) return customGap;

        switch (category)
        {
            case BuildingCategory.Chair: return 0.05f; // %5 boşluk (Yapışık)
            case BuildingCategory.Decor: return 0.2f;  // %20 boşluk
            case BuildingCategory.Shelf: return 0.6f;  // %60 boşluk (NPC geçer)
            case BuildingCategory.Table: return 0.85f; // %85 boşluk
            default: return 0.75f; // Standart
        }
    }
}