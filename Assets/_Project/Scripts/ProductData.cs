using UnityEngine;

// Bu satır, Unity'de sağ tıkladığımızda menüde çıkmasını sağlar
[CreateAssetMenu(fileName = "New_Product", menuName = "We Bussed Up/Yeni Ürün Oluştur")]
public class ProductData : ScriptableObject
{
    [Header("Ürün Kimliği")]
    [Tooltip("Multiplayer'da ürünleri bu ID ile tanıyacağız. Her ürüne KESİNLİKLE farklı bir sayı ver! (Örn: Cips=1, Kola=2)")]
    public int productID; 
    public string productName; // "Acılı Pringles"

    [Header("Modeller (Prefablar)")]
    [Tooltip("Rafa dizilecek tekli ürünün 3D Modeli")]
    public GameObject singleItemPrefab; 
    
    [Tooltip("Oyuncunun kucaklayıp taşıyacağı toptan kutunun 3D Modeli (İleride kullanacağız)")]
    public GameObject boxPrefab; 

    [Header("Fiziksel Boyutlar (Raf Dizilimi İçin)")]
    [Tooltip("Bu ürün rafta sağa/sola doğru ne kadar yer kaplıyor?")]
    public float itemWidth = 0.1f;
    [Tooltip("Bu ürün rafta arkaya/öne doğru ne kadar yer kaplıyor?")]
    public float itemDepth = 0.1f;

    [Header("Ekonomi (Tycoon Kısmı)")]
    public float buyPrice; // Biz toptancıdan kaça alıyoruz? (Örn: 2.5$)
    public float defaultSellPrice; // Marketteki varsayılan satış fiyatı (Örn: 4.0$)
}