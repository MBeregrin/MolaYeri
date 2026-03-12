using UnityEngine;

public interface IInteractable
{
    // E tuşuna basınca ne olacak?
    void Interact(ulong playerID); 

    // Ekrana bakınca ne yazacak? (Örn: "Al [E]", "Konuş [E]")
    string GetInteractionPrompt();
}