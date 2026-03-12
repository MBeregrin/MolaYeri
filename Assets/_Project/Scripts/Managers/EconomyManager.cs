using UnityEngine;
using Unity.Netcode;
using System;

public class EconomyManager : NetworkBehaviour
{
    // Her scriptin bu kasaya kolayca ulaşması için Singleton (Tekil) yapıyoruz.
    public static EconomyManager Instance;

    [Header("Şirket Kasası")]
    // NetworkVariable: Para değiştiği an Server'dan tüm oyunculara otomatik ve anında iletilir.
    // Başlangıç parası olarak 1000$ verdik.
    public NetworkVariable<float> companyMoney = new NetworkVariable<float>(1000f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Ekranda (UI) parayı yazdıracağımız zaman bu Event'i tetikleyeceğiz.
    public event Action<float> OnMoneyChanged;

    private void Awake()
    {
        // Singleton Kurulumu
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        // Para her değiştiğinde bu fonksiyon otomatik çalışır (Hem Server'da hem Client'larda)
        companyMoney.OnValueChanged += (oldValue, newValue) =>
        {
            // Ekranda UI varsa, parayı güncellemesi için ona haber ver.
            OnMoneyChanged?.Invoke(newValue);
            Debug.Log($"[KASA] Yeni Bakiye: {newValue}$");
        };
    }

    // ==========================================
    // PARA EKLEME VE HARCAMA İŞLEMLERİ
    // (Sadece Server parayı değiştirebilir, hileyi önlemek için)
    // ==========================================

    /// <summary>
    /// Kasaya para ekler (Satış yapıldığında vb. çağrılır)
    /// Örnek Kullanım: EconomyManager.Instance.AddMoneyServerRpc(50f);
    /// </summary>
    [Rpc(SendTo.Server)]
    public void AddMoneyServerRpc(float amount)
    {
        if (amount <= 0) return;
        companyMoney.Value += amount;
    }

    /// <summary>
    /// Kasadan para harcar (Toptancıdan mal alınca, raf kurunca vb. çağrılır)
    /// Örnek Kullanım: EconomyManager.Instance.SpendMoneyServerRpc(150f);
    /// </summary>
    [Rpc(SendTo.Server)]
    public void SpendMoneyServerRpc(float amount)
    {
        if (amount <= 0) return;
        
        // Kasada yeterli para var mı kontrolü
        if (companyMoney.Value >= amount)
        {
            companyMoney.Value -= amount;
        }
        else
        {
            Debug.LogWarning("[KASA] Yetersiz Bakiye! Bu işlem yapılamaz.");
            // İstersen burada ekrana "YETERSİZ BAKİYE" uyarısı çıkartacak bir ClientRpc tetikleyebilirsin.
        }
    }

    /// <summary>
    /// Herhangi bir scriptin "Kasada bu kadar paramız var mı?" diye sorması için.
    /// (Satın alma butonunu gri/aktif yapmak için kullanılır).
    /// </summary>
    public bool HasEnoughMoney(float amount)
    {
        return companyMoney.Value >= amount;
    }
}