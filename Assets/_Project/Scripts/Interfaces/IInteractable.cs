namespace WeBussedUp.Interfaces
{
    /// <summary>
    /// Oyuncunun E tuşuyla etkileşime geçebildiği her objenin uyguladığı arayüz.
    /// Uygulayan: BoxItem, PickableItem, FuelPump, POSDevice, ShelfSlot...
    /// </summary>
    public interface IInteractable
    {
        /// <summary>
        /// Ekranda gösterilecek prompt metni.
        /// Örn: "Kola Al [E]", "Benzin Doldur [E]", "Ödeme Yap [E]"
        /// Boş string dönerse UI hiçbir şey göstermez.
        /// </summary>
        string GetInteractionPrompt();

        /// <summary>
        /// Bu an etkileşim mümkün mü?
        /// False dönerse Interact() çağrılmaz, prompt soluk gösterilir.
        /// Örn: para yok, slot dolu, zaten taşınıyor...
        /// </summary>
        bool CanInteract(ulong playerId);

        /// <summary>
        /// Oyuncu E tuşuna bastığında tetiklenir.
        /// CanInteract() true döndüğünde çağrılır.
        /// </summary>
        void Interact(ulong playerId);

        /// <summary>
        /// UI'da gösterilecek ikon tipi.
        /// HUD sistemi buna göre farklı ikon seçer.
        /// </summary>
        InteractionType GetInteractionType();
    }

    public enum InteractionType
    {
        PickUp,     // Eşya alma (BoxItem, PickableItem)
        Use,        // Kullan (Benzin pompası, kahve makinesi)
        Pay,        // Ödeme (POS cihazı, kasa)
        Talk,       // Konuş (NPC)
        Clean,      // Temizle (WC, araba yıkama)
        Refuel,     // Yakıt doldur (Benzin pompası araç tarafı)
        Deposit,    // Rafa koy / depoya bırak
    }
}