using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine.Events;

public class PlacementDependency : NetworkBehaviour
{
    [Header("Gereksinim Ayarları")]
    [Tooltip("Bu eşya hangi Tag'e sahip objelerin üzerinde çalışır? (Örn: KitchenShelf, Table, Desk)")]
    public List<string> allowedSurfaceTags = new List<string>(); 

    [Tooltip("Lazerin çıkış noktası ve uzunluğu")]
    public float checkDistance = 0.5f;
    public Vector3 checkOffset = new Vector3(0, 0.1f, 0);

    // Durumu herkes bilsin (UI veya başka scriptler buna bakabilir)
    public NetworkVariable<bool> isPlacedCorrectly = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Olaylar (Events)")]
    [Tooltip("Yerleşim doğru olduğunda ne olsun? (Örn: Hata ikonunu gizle, Sesi aç)")]
    public UnityEvent OnValidPlacement;

    [Tooltip("Yerleşim yanlış olduğunda ne olsun? (Örn: Hata ikonunu göster, Elektriği kes)")]
    public UnityEvent OnInvalidPlacement;

    public override void OnNetworkSpawn()
    {
        // İlk açılışta durumu kontrol et ve eventleri tetikle
        isPlacedCorrectly.OnValueChanged += (prev, current) => 
        {
            if (current) OnValidPlacement?.Invoke();
            else OnInvalidPlacement?.Invoke();
        };

        // Mevcut durumu uygula
        if (isPlacedCorrectly.Value) OnValidPlacement?.Invoke();
        else OnInvalidPlacement?.Invoke();
    }

    private void Update()
    {
        // Sadece SERVER kontrolü yapar (Güvenlik)
        if (!IsServer) return;

        CheckSurface();
    }

    private void CheckSurface()
    {
        Vector3 origin = transform.position + checkOffset;
        RaycastHit hit;
        bool foundValidSurface = false;

        // Aşağıya doğru lazer at
        if (Physics.Raycast(origin, Vector3.down, out hit, checkDistance))
        {
            // Lazerin çarptığı şeyin Tag'i, bizim izin verdiğimiz listede var mı?
            if (allowedSurfaceTags.Contains(hit.collider.tag))
            {
                foundValidSurface = true;
            }
        }

        // Durum değiştiyse NetworkVariable'ı güncelle
        if (isPlacedCorrectly.Value != foundValidSurface)
        {
            isPlacedCorrectly.Value = foundValidSurface;
            Debug.Log($"[YERLEŞİM] {gameObject.name} durumu değişti: {(foundValidSurface ? "DOĞRU" : "YANLIŞ")}");
        }
    }

    // Editörde Lazerin nereye gittiğini görmek için (Gizmos)
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isPlacedCorrectly.Value ? Color.green : Color.red;
        Gizmos.DrawRay(transform.position + checkOffset, Vector3.down * checkDistance);
    }
}