using UnityEngine;

public enum FacilityType { General, Supermarket, Cafe, GasStation }
public enum ItemCategory { Consumable, Furniture, Equipment, CarCare, Decoration }

// Nereye inşa edilebilir? (Duvar rafı mı, yer masası mı?)
public enum PlacementSurface { Floor, Wall, Ceiling }

[CreateAssetMenu(fileName = "NewItem", menuName = "Tycoon Sistemi/Yeni Eşya (Item)")]
public class ItemData : ScriptableObject
{
    [Header("Kimlik Bilgileri")]
    public string itemID;      
    public string itemName;    
    
    [Header("Tesis & Kategori Ayarları")]
    public FacilityType facility; 
    public ItemCategory category; 

    [Header("Ekonomi")]
    public float buyPrice;     
    public float sellPrice;    

    [Header("Görsel & Obje")]
    public Sprite icon;        
    public GameObject prefab;  

    // ==========================================
    // İŞTE SENİN İSTEDİĞİ YENİ BÖLÜM (GRİD SİSTEMİ)
    // ==========================================
    [Header("İnşaat & Grid Ayarları")]
    [Tooltip("Bu eşya inşa modunda (B tuşu) kurulabilir mi? (Örn: Cips hayır, Raf evet)")]
    public bool isBuildable = false; 
    
    [Tooltip("Bu eşya nereye yapışacak?")]
    public PlacementSurface placementSurface = PlacementSurface.Floor;

    [Tooltip("Grid üzerinde kaç X kaç kare kaplayacak? (Örn: X:2, Y:1)")]
    public Vector2Int gridSize = new Vector2Int(1, 1); 
}