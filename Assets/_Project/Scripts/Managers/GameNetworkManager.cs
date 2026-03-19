using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WeBussedUp.Network
{
    public class GameNetworkManager : MonoBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────
        public static GameNetworkManager Instance { get; private set; }

        // ─── Inspector ───────────────────────────────────────────
        [Header("Steam Ayarları")]
        [SerializeField] private uint _steamAppId = 480; // Test AppID, yayında değiştirilecek

        [Header("Lobby Ayarları")]
        [SerializeField] private int    _maxPlayers     = 4;
        [SerializeField] private string _gameVersionKey = "version";
        [SerializeField] private string _gameVersion    = "0.1.0";
        [SerializeField] private string _lobbyNameKey   = "name";

        [Header("Sahne")]
        [SerializeField] private string _gameSceneName = "GameScene";

        // ─── Runtime ─────────────────────────────────────────────
        public Lobby?        CurrentLobby     { get; private set; }
        public bool          IsHost           => NetworkManager.Singleton.IsHost;
        public bool          IsConnected      => NetworkManager.Singleton.IsClient ||
                                                 NetworkManager.Singleton.IsHost;
        public bool          IsSteamReady     { get; private set; }
        public List<SteamId> ConnectedPlayers { get; private set; } = new();

        // ─── Events ──────────────────────────────────────────────
        public event System.Action         OnLobbyCreated;
        public event System.Action<Lobby>  OnLobbyJoined;
        public event System.Action         OnLobbyLeft;
        public event System.Action<Friend> OnPlayerJoined;
        public event System.Action<Friend> OnPlayerLeft;
        public event System.Action<string> OnConnectionFailed;

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitSteam();
        }

        private void OnDestroy()
        {
            if (IsSteamReady)
            {
                SteamClient.Shutdown();
                Debug.Log("[GameNetworkManager] Steam kapatıldı.");
            }
        }

        private void Update()
        {
            // Facepunch Steamworks her frame callback işlemesi gerekiyor
            if (IsSteamReady) SteamClient.RunCallbacks();
        }

        private void InitSteam()
        {
            try
            {
                SteamClient.Init(_steamAppId, asyncCallbacks: false);
                IsSteamReady = true;
                Debug.Log($"[GameNetworkManager] Steam bağlandı: {SteamClient.Name} (AppID: {_steamAppId})");
            }
            catch (System.Exception e)
            {
                IsSteamReady = false;
                Debug.LogError($"[GameNetworkManager] Steam başlatılamadı: {e.Message}\n" +
                               "Steam açık mı? steam_appid.txt doğru mu?");
            }
        }

        private void OnEnable()
        {
            if (!IsSteamReady) return;

            SteamMatchmaking.OnLobbyCreated       += HandleLobbyCreated;
            SteamMatchmaking.OnLobbyEntered       += HandleLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined  += HandleMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave   += HandleMemberLeft;
            SteamMatchmaking.OnLobbyInvite        += HandleLobbyInvite;
            SteamMatchmaking.OnLobbyGameCreated   += HandleLobbyGameCreated;
            SteamFriends.OnGameLobbyJoinRequested += HandleJoinRequested;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback  += HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            }
        }

        private void OnDisable()
        {
            if (!IsSteamReady) return;

            SteamMatchmaking.OnLobbyCreated       -= HandleLobbyCreated;
            SteamMatchmaking.OnLobbyEntered       -= HandleLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined  -= HandleMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave   -= HandleMemberLeft;
            SteamMatchmaking.OnLobbyInvite        -= HandleLobbyInvite;
            SteamMatchmaking.OnLobbyGameCreated   -= HandleLobbyGameCreated;
            SteamFriends.OnGameLobbyJoinRequested -= HandleJoinRequested;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback  -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        // ─── Lobby Oluşturma ─────────────────────────────────────
        public async Task CreateLobbyAsync(string lobbyName = "")
        {
            if (!IsSteamReady)
            {
                Notify("Steam bağlı değil!", UnityEngine.Color.red);
                return;
            }

            try
            {
                var result = await SteamMatchmaking.CreateLobbyAsync(_maxPlayers);

                if (!result.HasValue)
                {
                    OnConnectionFailed?.Invoke("Lobi oluşturulamadı.");
                    Notify("Lobi oluşturulamadı!", UnityEngine.Color.red);
                    return;
                }

                CurrentLobby = result.Value;
                CurrentLobby.Value.SetPublic();
                CurrentLobby.Value.SetJoinable(true);
                CurrentLobby.Value.SetData(_gameVersionKey, _gameVersion);
                CurrentLobby.Value.SetData(_lobbyNameKey,
                    string.IsNullOrEmpty(lobbyName)
                        ? $"{SteamClient.Name}'in Mola Yeri"
                        : lobbyName);

                NetworkManager.Singleton.StartHost();

                OnLobbyCreated?.Invoke();
                Notify("Lobi oluşturuldu! Arkadaşlarını davet et.", UnityEngine.Color.green);
                Debug.Log($"[GameNetworkManager] Lobi: {CurrentLobby.Value.Id}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameNetworkManager] Lobi hatası: {e.Message}");
                OnConnectionFailed?.Invoke(e.Message);
            }
        }

        public async Task JoinLobbyAsync(SteamId lobbyId)
        {
            if (!IsSteamReady)
            {
                Notify("Steam bağlı değil!", UnityEngine.Color.red);
                return;
            }

            try
            {
                var result = await SteamMatchmaking.JoinLobbyAsync(lobbyId);

                if (!result.HasValue)
                {
                    OnConnectionFailed?.Invoke("Lobiye katılınamadı.");
                    Notify("Lobiye katılınamadı!", UnityEngine.Color.red);
                    return;
                }

                CurrentLobby = result.Value;

                string hostVersion = CurrentLobby.Value.GetData(_gameVersionKey);
                if (hostVersion != _gameVersion)
                {
                    Notify($"Versiyon uyumsuz! Host: {hostVersion}", UnityEngine.Color.red);
                    await LeaveLobbyAsync();
                    return;
                }

                Debug.Log($"[GameNetworkManager] Lobiye katılındı: {lobbyId}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameNetworkManager] Katılım hatası: {e.Message}");
                OnConnectionFailed?.Invoke(e.Message);
            }
        }

        public async Task LeaveLobbyAsync()
        {
            if (CurrentLobby.HasValue)
            {
                CurrentLobby.Value.Leave();
                CurrentLobby = null;
            }

            NetworkManager.Singleton?.Shutdown();

            ConnectedPlayers.Clear();
            OnLobbyLeft?.Invoke();
            Notify("Lobiden çıkıldı.", UnityEngine.Color.white);

            await Task.CompletedTask;
        }

        public void StartGame()
        {
            if (!IsHost || !CurrentLobby.HasValue) return;

            CurrentLobby.Value.SetJoinable(false);

            NetworkManager.Singleton.SceneManager.LoadScene(
                _gameSceneName,
                UnityEngine.SceneManagement.LoadSceneMode.Single
            );
        }

        public async Task<Lobby[]> FetchLobbiesAsync()
        {
            if (!IsSteamReady) return new Lobby[0];

            try
            {
                var lobbies = await SteamMatchmaking.LobbyList
                    .FilterDistanceWorldwide()
                    .WithKeyValue(_gameVersionKey, _gameVersion)
                    .RequestAsync();

                return lobbies ?? new Lobby[0];
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameNetworkManager] Lobi listesi hatası: {e.Message}");
                return new Lobby[0];
            }
        }

        // ─── Steam Callbacks ─────────────────────────────────────
        private void HandleLobbyCreated(Result result, Lobby lobby)
        {
            if (result != Result.OK)
                OnConnectionFailed?.Invoke($"Steam hatası: {result}");
        }

        private void HandleLobbyEntered(Lobby lobby)
        {
            if (NetworkManager.Singleton.IsHost) return;

            NetworkManager.Singleton.StartClient();
            OnLobbyJoined?.Invoke(lobby);
            Notify($"{lobby.GetData(_lobbyNameKey)} lobisine katıldın!", UnityEngine.Color.green);
        }

        private void HandleMemberJoined(Lobby lobby, Friend friend)
        {
            ConnectedPlayers.Add(friend.Id);
            OnPlayerJoined?.Invoke(friend);
            Notify($"{friend.Name} katıldı!", UnityEngine.Color.cyan);
        }

        private void HandleMemberLeft(Lobby lobby, Friend friend)
        {
            ConnectedPlayers.Remove(friend.Id);
            OnPlayerLeft?.Invoke(friend);
            Notify($"{friend.Name} ayrıldı.", UnityEngine.Color.yellow);
        }

        private void HandleLobbyInvite(Friend friend, Lobby lobby)
        {
            Notify($"{friend.Name} seni lobiye davet etti!", UnityEngine.Color.cyan);
        }

        private void HandleLobbyGameCreated(Lobby lobby, uint ip, ushort port, SteamId steamId) { }

        private async void HandleJoinRequested(Lobby lobby, SteamId steamId)
        {
            await JoinLobbyAsync(lobby.Id);
        }

        // ─── Netcode Callbacks ────────────────────────────────────
        private void HandleClientConnected(ulong clientId)
        {
            Debug.Log($"[GameNetworkManager] Client bağlandı: {clientId}");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            Debug.Log($"[GameNetworkManager] Client ayrıldı: {clientId}");

            if (IsHost) return;

            if (clientId == NetworkManager.ServerClientId)
            {
                Notify("Host ayrıldı!", UnityEngine.Color.red);
                _ = LeaveLobbyAsync();
            }
        }

        // ─── Util ────────────────────────────────────────────────
        private void Notify(string message, UnityEngine.Color color)
        {
            WeBussedUp.UI.UIManager.Instance?.ShowNotification(message, color);
        }
    }
}