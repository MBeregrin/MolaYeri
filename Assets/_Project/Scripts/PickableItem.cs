using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
public class PickableItem : NetworkBehaviour, IInteractable
{
    [Header("Eşya Bilgileri")]
    public string itemName = "Karton Koli";

    // İŞTE HATAYI ÇÖZECEK OLAN YENİ DEĞİŞKEN (Mıknatıs Sistemi İçin)
    // Bu eşyayı kim taşıyor? (ulong.MaxValue = Kimse taşımıyor, yerde duruyor)
    public NetworkVariable<ulong> carrierID = new NetworkVariable<ulong>(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Rigidbody rb;
    private Collider col;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        // Eşya ilk doğduğunda veya biri server'a katıldığında durumu senkronize et
        carrierID.OnValueChanged += OnCarrierChanged;
        UpdatePhysicalState(carrierID.Value);
    }

    // --- IINTERACTABLE ARAYÜZÜ (Ekrana bakınca çalışacak kısım) ---
    public string GetInteractionPrompt()
    {
        return carrierID.Value == ulong.MaxValue ? $"{itemName} Al [E]" : "";
    }

    public void Interact(ulong playerID)
    {
        // Sadece yerdeyse alınabilsin
        if (carrierID.Value == ulong.MaxValue)
        {
            PickUpServerRpc(playerID);
        }
    }

    // ==========================================
    // MULTIPLAYER KONTROLLERİ (SERVER RPC)
    // ==========================================

    [Rpc(SendTo.Server)]
    private void PickUpServerRpc(ulong playerID)
    {
        // Biri benden önce kapmadıysa, ID'yi benim ID'm yap
        if (carrierID.Value == ulong.MaxValue)
        {
            carrierID.Value = playerID;
            // Objenin ağ mülkiyetini de o oyuncuya ver ki rahat hareket ettirsin
            GetComponent<NetworkObject>().ChangeOwnership(playerID);
        }
    }

    // İŞTE DİĞER HATAYI ÇÖZECEK FONKSİYON
    [Rpc(SendTo.Server)]
    public void DropServerRpc()
    {
        // Yere bırak
        carrierID.Value = ulong.MaxValue;
        GetComponent<NetworkObject>().RemoveOwnership();
    }

    // ==========================================
    // MIKNATIS (FİZİK) DURUMU
    // ==========================================

    private void OnCarrierChanged(ulong oldID, ulong newID)
    {
        UpdatePhysicalState(newID);
    }

    private void UpdatePhysicalState(ulong currentCarrierID)
    {
        if (currentCarrierID == ulong.MaxValue)
        {
            // YERDE DURUYOR (Fizik Açık)
            rb.isKinematic = false;
            col.enabled = true;
        }
        else
        {
            // BİRİ TAŞIYOR (Fizik Kapalı, Lazer (Raycast) buna çarpmasın diye collider kapalı)
            rb.isKinematic = true;
            col.enabled = false;
        }
    }
}