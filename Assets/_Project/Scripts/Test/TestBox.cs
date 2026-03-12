using UnityEngine;

public class TestBox : MonoBehaviour, IInteractable
{
    public string GetInteractionPrompt()
    {
        return "Patlat [E]";
    }

    public void Interact(ulong playerID)
    {
        Debug.Log("BOOM! " + playerID + " numaralı oyuncu beni patlattı!");
        // Rengini rastgele değiştir
        GetComponent<Renderer>().material.color = Random.ColorHSV();
    }
}