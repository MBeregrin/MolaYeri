// TestBootstrap.cs — sadece test için
using UnityEngine;
using Unity.Netcode;

public class TestBootstrap : MonoBehaviour
{
    private void Start()
    {
        NetworkManager.Singleton.StartHost();
    }
}
