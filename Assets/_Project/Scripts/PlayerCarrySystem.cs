using UnityEngine;
using Unity.Netcode;

public class PlayerCarrySystem : NetworkBehaviour
{
    [Header("Taşıma Ayarları")]
    [Tooltip("Kameranın önünde boş bir obje oluşturup buraya sürükle")]
    [SerializeField] private Transform holdPoint; 
    [SerializeField] private float smoothSpeed = 15f; // Eşyanın ele gelme yumuşaklığı

    private PickableItem currentlyHeldItem;
    private PlayerInputs input;

    private void Awake() => input = new PlayerInputs();
    private void OnEnable() => input.Enable();
    private void OnDisable() => input.Disable();

    private void Update()
    {
        // Sadece kendi karakterimizi kontrol edelim
        if (!IsOwner) return;

        HandleDropInput();
        UpdateHeldItemPosition();
    }

    private void HandleDropInput()
    {
        // G tuşuna (veya ayarladığın bırakma tuşuna) basarsa ve elinde bir şey varsa bırak
        if (input.Player.Interact.WasPressedThisFrame() && currentlyHeldItem != null)
        {
            currentlyHeldItem.DropServerRpc();
            currentlyHeldItem = null;
        }
    }

    private void UpdateHeldItemPosition()
    {
        // Eğer elimizde bir eşya referansı yoksa, sahnede bizi (ID'mizi) takip eden bir koli var mı diye bak
        if (currentlyHeldItem == null)
        {
            FindItemAssignedToMe();
        }

        // Eğer eşyamız varsa, onu MIKNATIS gibi holdPoint'e çek
        if (currentlyHeldItem != null)
        {
            // Eşya başkası tarafından alındıysa/düştüyse elimizden sil
            if (currentlyHeldItem.carrierID.Value != NetworkManager.Singleton.LocalClientId)
            {
                currentlyHeldItem = null;
                return;
            }

            // Objeyi kameranın önüne (holdPoint'e) yumuşakça götür
            currentlyHeldItem.transform.position = Vector3.Lerp(currentlyHeldItem.transform.position, holdPoint.position, Time.deltaTime * smoothSpeed);
            currentlyHeldItem.transform.rotation = Quaternion.Lerp(currentlyHeldItem.transform.rotation, holdPoint.rotation, Time.deltaTime * smoothSpeed);
        }
    }

    private void FindItemAssignedToMe()
    {
        // Bu işlem her frame yapılmaz, sadece elimiz boşken bize atanmış bir obje var mı diye bakar.
        PickableItem[] allItems = FindObjectsByType<PickableItem>(FindObjectsSortMode.None);
        foreach (var item in allItems)
        {
            if (item.carrierID.Value == NetworkManager.Singleton.LocalClientId)
            {
                currentlyHeldItem = item;
                break;
            }
        }
    }
    // Bunu PlayerCarrySystem.cs'nin en altına ekle!
    public bool IsHoldingItem()
    {
        return currentlyHeldItem != null;
    }
}