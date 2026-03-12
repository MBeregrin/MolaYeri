using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[System.Serializable]
public struct ShelfPlank
{
    [Header("Temel Ayarlar")]
    public Transform startPoint;
    public float width;
    public float depth;
    
    [Header("Dizilim Yönü")]
    public bool flipX; 
    public bool flipZ;

    [Header("Ürün Duruşu")]
    public float itemRotationY; 
}

public class ShelfManager : NetworkBehaviour, IInteractable
{
    [Header("Raf Konfigürasyonu")]
    public ShelfPlank[] planks; 
    
    [Header("TEST - Oyuncunun Elindeki Ürün")]
    [Tooltip("Daha envanter sistemimiz olmadığı için, rafa dizeceğimiz ürünü şimdilik buraya sürükle bırak yapıp test edeceğiz.")]
    public ProductData testProductToPlace;

    [Header("Veritabanı (Şimdilik)")]
    [Tooltip("Oyundaki tüm ürünleri (ProductData) buraya ekle ki raf ID'den ürünü bulabilsin.")]
    public List<ProductData> allAvailableProducts;

    // --- NETWORK VARIABLES ---
    // Artık string değil, INT tutuyoruz (-1 boş raf demek)
    public NetworkVariable<int> currentProductID = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> currentStock = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> maxCapacity = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private List<GameObject> spawnedDummies = new List<GameObject>();

    public override void OnNetworkSpawn()
    {
        currentStock.OnValueChanged += (oldStock, newStock) => UpdateVisuals();
        currentProductID.OnValueChanged += (oldID, newID) => UpdateVisuals();
        UpdateVisuals();
    }

    public string GetInteractionPrompt()
    {
        if (currentStock.Value == 0) return "Raf Boş - Ürün Diz [E]";
        if (maxCapacity.Value > 0 && currentStock.Value >= maxCapacity.Value) return "Raf Dolu!";
        return $"Ürün Diz ({currentStock.Value}/{maxCapacity.Value}) [E]";
    }

    public void Interact(ulong playerID)
    {
        if (!IsSpawned) return;

        if (testProductToPlace == null)
        {
            Debug.LogError("Test ürünü atanmamış! Lütfen ShelfManager'daki testProductToPlace kısmına bir ProductData sürükle.");
            return;
        }

        TryAddProductServerRpc(testProductToPlace.productID);
    }

    [Rpc(SendTo.Server)]
    public void TryAddProductServerRpc(int productID)
    {
        if (planks == null || planks.Length == 0 || planks[0].startPoint == null) return;

        ProductData productData = GetProductByID(productID);
        if (productData == null) return;

        // Raf boşsa, ürünü tanımla ve kapasiteyi hesapla
        if (currentStock.Value == 0)
        {
            currentProductID.Value = productID;
            maxCapacity.Value = CalculateMaxCapacity(productData);
        }
        // Raf doluysa ama farklı ürün konmaya çalışılıyorsa reddet
        else if (currentProductID.Value != productID)
        {
            return; 
        }

        // Kapasite dolmadıysa stoğu artır
        if (currentStock.Value < maxCapacity.Value)
        {
            currentStock.Value++;
        }
    }

    // ID'ye göre ürünü bulma
    private ProductData GetProductByID(int id)
    {
        foreach (var product in allAvailableProducts)
        {
            if (product.productID == id) return product;
        }
        return null;
    }

    // Kapasite Hesaplama (Artık çok kolay çünkü boyutlar ScriptableObject'te var!)
    private int CalculateMaxCapacity(ProductData productData)
    {
        int total = 0;
        foreach (var plank in planks)
        {
            int c = Mathf.FloorToInt(plank.width / productData.itemWidth);
            int r = Mathf.FloorToInt(plank.depth / productData.itemDepth);
            total += (c > 0 ? c : 0) * (r > 0 ? r : 0);
        }
        return total;
    }

    // ==========================================
    // GÖRSEL DİZİLİM
    // ==========================================
    private void UpdateVisuals()
    {
        foreach (var dummy in spawnedDummies) if (dummy != null) Destroy(dummy);
        spawnedDummies.Clear();

        if (currentStock.Value == 0 || currentProductID.Value == -1) return;

        ProductData productData = GetProductByID(currentProductID.Value);
        if (productData == null) return;

        int itemsLeft = currentStock.Value;

        foreach (var plank in planks)
        {
            int cols = Mathf.FloorToInt(plank.width / productData.itemWidth);
            int rows = Mathf.FloorToInt(plank.depth / productData.itemDepth);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (itemsLeft <= 0) return;

                    float xPos = plank.flipX ? -(c * productData.itemWidth) - (productData.itemWidth/2) : (c * productData.itemWidth) + (productData.itemWidth/2);
                    float zPos = plank.flipZ ? -(r * productData.itemDepth) - (productData.itemDepth/2) : (r * productData.itemDepth) + (productData.itemDepth/2);

                    Vector3 localPos = new Vector3(xPos, 0, zPos);
                    Vector3 worldPos = plank.startPoint.TransformPoint(localPos);
                    Quaternion finalRotation = plank.startPoint.rotation * Quaternion.Euler(0, plank.itemRotationY, 0);

                    // Prefab'ı üret (Artık productData'nın içinden çekiyoruz)
                    GameObject visualDummy = Instantiate(productData.singleItemPrefab, worldPos, finalRotation);
                    visualDummy.transform.SetParent(plank.startPoint);

                    // Fiziği Kapat
                    if (visualDummy.TryGetComponent(out Collider col)) col.enabled = false;
                    if (visualDummy.TryGetComponent(out Rigidbody rb)) Destroy(rb);
                    if (visualDummy.TryGetComponent(out NetworkObject netObj)) Destroy(netObj);

                    spawnedDummies.Add(visualDummy);
                    itemsLeft--;
                }
            }
        }
    }
}