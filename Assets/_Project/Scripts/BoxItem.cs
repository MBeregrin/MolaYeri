using UnityEngine;
using Unity.Netcode;

public class BoxItem : NetworkBehaviour, IInteractable
{
    [Header("Kutu İçeriği")]
    [Tooltip("Bu koli ne kolisi? (Oluşturduğun Cips veya Kola verisini buraya sürükle)")]
    public ProductData boxContent; 
    public int itemsInside = 10; // Kutunun içinde kaç adet ürün var?

    private Rigidbody rb;
    private Collider col;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    // Arayüzden (IInteractable) gelen metin
    public string GetInteractionPrompt()
    {
        if (boxContent != null)
            return $"{boxContent.productName} Kolisi Al [E]";
        
        return "Boş Koli Al [E]";
    }

    // Oyuncu E'ye bastığında tetiklenir
    public void Interact(ulong playerID)
    {
        // Kutuyu yerden alma komutunu Sunucuya (Server) gönder
        RequestPickupServerRpc(playerID);
    }

    [Rpc(SendTo.Server)]
    public void RequestPickupServerRpc(ulong playerID)
    {
        // 1. Kutunun network sahipliğini, E'ye basan oyuncuya ver
        NetworkObject.ChangeOwnership(playerID);

        // 2. Kutuyu oyuncunun NetworkObject'inin (Karakterinin) içine sok
        NetworkObject playerNetObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(playerID);
        if (playerNetObj != null)
        {
            NetworkObject.TrySetParent(playerNetObj, false);
        }

        // 3. Fiziği kapat ki oyuncunun elindeyken sağa sola çarpıp adamı uçurmasın
        TogglePhysicsClientRpc(false);
    }

    // Sunucu bu komutu tüm oyunculara yollar
    [Rpc(SendTo.ClientsAndHost)]
    public void TogglePhysicsClientRpc(bool state)
    {
        if (rb != null) rb.isKinematic = !state;
        if (col != null) col.enabled = state;

        // Fiziği kapattıysak (yani elimize aldıysak) pozisyonunu kameranın önüne düzelt
        if (!state)
        {
            // Not: Bu değerler karakterinin elinin/kamerasının konumuna göre Unity'den ince ayar isteyebilir.
            transform.localPosition = new Vector3(0, 0.5f, 1f); 
            transform.localRotation = Quaternion.identity;
        }
    }
}