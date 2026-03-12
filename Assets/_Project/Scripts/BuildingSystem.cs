using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class BuildingSystem : NetworkBehaviour
{
    public static BuildingSystem Instance;

    [Header("İnşaat Ayarları")]
    public float gridCellSize = 1f;      // Grid karelerinin boyutu (1 metre ideal)
    public float buildRange = 8f;        // Lazerin (Kameranın) yetişme mesafesi

    [Header("Katmanlar (Layers)")]
    public LayerMask floorLayer;         // Zemin Layer'ı
    public LayerMask wallLayer;          // Duvar Layer'ı
    public LayerMask obstacleLayer;      // Diğer eşyalar, duvarlar (Üst üste binmesin diye)

    [Header("Referanslar")]
    public Transform cameraTransform;    // Oyuncunun kamerası

    // --- ARKA PLAN DEĞİŞKENLERİ ---
    private ItemData currentItem;        // Şu an seçili olan eşyanın kimlik kartı
    private GameObject ghostObject;      // Yeşil/Kırmızı parlayan hayalet obje
    private Renderer[] ghostRenderers;
    
    private bool isBuildMode = false;
    private bool canPlace = false;
    private float currentYRotation = 0f; // Eşyayı R tuşuyla döndürmek için

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        if (!IsOwner) return;

        // --- YENİ INPUT SİSTEMİ: B TUŞU İLE GİR / ÇIK (TOGGLE) ---
        if (Keyboard.current.bKey.wasPressedThisFrame)
        {
            if (!isBuildMode)
            {
                // İnşaat Moduna GİR
                if (ItemDatabase.Instance != null && ItemDatabase.Instance.allItems.Count > 0)
                {
                    ItemData testItem = ItemDatabase.Instance.allItems[0]; 
                    StartBuilding(testItem);
                    Debug.Log($"<color=yellow>[İNŞAAT]</color> Mod Açıldı! Seçilen: {testItem.itemName}");
                }
            }
            else
            {
                // Zaten inşaat modundaysa ÇIK
                StopBuilding();
                Debug.Log("<color=yellow>[İNŞAAT]</color> Mod Kapatıldı!");
            }
        }

        // Eğer inşaat modunda değilsek aşağıdaki fiziksel hesaplamaları ÇALIŞTIRMA
        if (!isBuildMode || currentItem == null || ghostObject == null) return;

        UpdateGhostPositionAndSnapping();
        HandleRotation();
        HandlePlacement();
    }

    // ==========================================
    // İNŞAAT MODUNU BAŞLATMA VE BİTİRME
    // ==========================================

    /// <summary>
    /// UI (Tablet) üzerinden bir eşya seçildiğinde çağrılır.
    /// Örn: BuildingSystem.Instance.StartBuilding(rafItemData);
    /// </summary>
    public void StartBuilding(ItemData itemData)
    {
        if (!itemData.isBuildable) 
        {
            Debug.LogWarning("[İNŞAAT] Bu eşya inşa edilemez!");
            return;
        }

        currentItem = itemData;
        isBuildMode = true;

        // Varsa eski hayaleti sil, yeni eşyanın hayaletini yarat
        if (ghostObject != null) Destroy(ghostObject);
        
        ghostObject = Instantiate(currentItem.prefab);
        
        // Hayalet objenin fiziksel etkileşimlerini kapat (sadece görüntü olsun)
        if (ghostObject.TryGetComponent(out Collider col)) col.enabled = false;
        if (ghostObject.TryGetComponent(out Rigidbody rb)) Destroy(rb);
        if (ghostObject.TryGetComponent(out NetworkObject netObj)) Destroy(netObj); // Ghost senkronize edilmez

        ghostRenderers = ghostObject.GetComponentsInChildren<Renderer>();
        currentYRotation = 0f;
    }

    public void StopBuilding()
    {
        isBuildMode = false;
        currentItem = null;
        if (ghostObject != null) Destroy(ghostObject);
    }

    // ==========================================
    // HAYALET YÖNETİMİ (GRID VE YÜZEY KONTROLÜ)
    // ==========================================

    private void UpdateGhostPositionAndSnapping()
    {
        // 1. HANGİ YÜZEYE BAKIYORUZ? (ItemData'dan çekiyoruz)
        LayerMask targetLayer = currentItem.placementSurface == PlacementSurface.Wall ? wallLayer : floorLayer;

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        
        if (Physics.Raycast(ray, out RaycastHit hit, buildRange, targetLayer))
        {
            ghostObject.SetActive(true);

            // 2. GRID MATEMATİĞİ (Tık tık oturması için)
            // (ItemData'daki gridSize x ve y değerlerine göre merkezleme yapılabilir, şimdilik basit hücre oturtması)
            float snapX = Mathf.Round(hit.point.x / gridCellSize) * gridCellSize;
            float snapZ = Mathf.Round(hit.point.z / gridCellSize) * gridCellSize;
            float snapY = hit.point.y; // Yükseklik zeminin yüksekliği kalır (veya duvarın)

            Vector3 snappedPosition = new Vector3(snapX, snapY, snapZ);
            
            // Eğer duvara asılıyorsa, objeyi duvara yapıştır ve duvara doğru döndür
            if (currentItem.placementSurface == PlacementSurface.Wall)
            {
                snappedPosition = hit.point; // Duvarlarda serbest kaydırma daha iyi hissettirir
                ghostObject.transform.rotation = Quaternion.LookRotation(hit.normal);
            }
            else
            {
                ghostObject.transform.position = snappedPosition;
                ghostObject.transform.rotation = Quaternion.Euler(0, currentYRotation, 0);
            }

            // 3. ENGEL KONTROLÜ (Üst üste eşya koymayı engelle)
            CheckForObstacles();
        }
        else
        {
            // Lazer boşa bakıyorsa hayaleti gizle
            ghostObject.SetActive(false);
            canPlace = false;
        }
    }

    private void CheckForObstacles()
    {
        // Hayaletin kapladığı alanı ItemData'daki GridSize'a göre hesaplayıp BoxOverlap atıyoruz
        Vector3 boxExtents = new Vector3(currentItem.gridSize.x * gridCellSize / 2f, 0.5f, currentItem.gridSize.y * gridCellSize / 2f);
        
        // Etrafında bizim belirlediğimiz Layer'dan (Obstacle) bir şey var mı?
        Collider[] hitColliders = Physics.OverlapBox(ghostObject.transform.position + Vector3.up * 0.5f, boxExtents, ghostObject.transform.rotation, obstacleLayer);

        if (hitColliders.Length > 0)
        {
            canPlace = false;
            ChangeGhostColor(Color.red); // Hata rengi
        }
        else
        {
            canPlace = true;
            ChangeGhostColor(Color.green); // Müsait rengi
        }
    }

    // ==========================================
    // İNŞA ETME VE DÖNDÜRME GİRDİLERİ
    // ==========================================

    private void HandleRotation()
    {
        // YENİ INPUT SİSTEMİ İLE R TUŞU
        if (currentItem.placementSurface == PlacementSurface.Floor && Keyboard.current.rKey.wasPressedThisFrame)
        {
            currentYRotation += 90f; 
        }
    }

    private void HandlePlacement()
    {
        // YENİ INPUT SİSTEMİ İLE SOL TIK
        if (Mouse.current.leftButton.wasPressedThisFrame && canPlace && ghostObject.activeSelf)
        {
            if (EconomyManager.Instance != null && !EconomyManager.Instance.HasEnoughMoney(currentItem.buyPrice))
            {
                Debug.LogWarning("[İNŞAAT] Paran yetersiz Kral!");
                return;
            }

            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.SpendMoneyServerRpc(currentItem.buyPrice);
            }

            PlaceObjectServerRpc(currentItem.itemID, ghostObject.transform.position, ghostObject.transform.rotation);
        }

        // YENİ INPUT SİSTEMİ İLE SAĞ TIK VEYA ESC
        if (Mouse.current.rightButton.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            StopBuilding();
        }
    }

    // Multiplayer'da eşya yaratmak (Spawn) her zaman Server'ın işidir!
    [Rpc(SendTo.Server)]
    private void PlaceObjectServerRpc(string itemID, Vector3 position, Quaternion rotation)
    {
        // Database'den eşyayı bul
        ItemData itemToBuild = ItemDatabase.Instance.GetItemByID(itemID);
        if (itemToBuild == null) return;

        // Gerçek objeyi yarat
        GameObject newObj = Instantiate(itemToBuild.prefab, position, rotation);
        
        // Ağda herkesin ekranında var et
        if (newObj.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn();
        }
    }

    // Hayaletin rengini değiştiren ufak bir yardımcı
    private void ChangeGhostColor(Color color)
    {
        color.a = 0.5f; // Yarı saydam (Cam gibi) olsun
        foreach (var rend in ghostRenderers)
        {
            foreach (var mat in rend.materials)
            {
                // Material'in rendering modunu Transparent yapman gerekir ki bu işe yarasın
                mat.color = color; 
            }
        }
    }
}