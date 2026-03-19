using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using WeBussedUp.Network;

namespace WeBussedUp.UI
{
    /// <summary>
    /// Steam lobby UI — lobi oluşturma, listeleme, katılma, oyuncu listesi.
    /// GameNetworkManager event'lerini dinler.
    /// Panel sistemi: MainMenu → LobbyBrowser → LobbyRoom
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        // ─── Inspector: Paneller ─────────────────────────────────
        [Header("Paneller")]
        [SerializeField] private GameObject _mainMenuPanel;
        [SerializeField] private GameObject _lobbyBrowserPanel;
        [SerializeField] private GameObject _lobbyRoomPanel;
        [SerializeField] private GameObject _loadingPanel;

        // ─── Inspector: Ana Menü ─────────────────────────────────
        [Header("Ana Menü")]
        [SerializeField] private Button          _createLobbyButton;
        [SerializeField] private Button          _browseLobbyButton;
        [SerializeField] private Button          _quitButton;
        [SerializeField] private TextMeshProUGUI _playerNameText;
        [SerializeField] private TextMeshProUGUI _versionText;

        // ─── Inspector: Lobi Tarayıcı ────────────────────────────
        [Header("Lobi Tarayıcı")]
        [SerializeField] private Transform       _lobbyListContainer;
        [SerializeField] private GameObject      _lobbyListItemPrefab;
        [SerializeField] private Button          _refreshButton;
        [SerializeField] private Button          _backFromBrowserButton;
        [SerializeField] private TextMeshProUGUI _lobbyCountText;
        [SerializeField] private TMP_InputField  _lobbyNameInput;

        // ─── Inspector: Lobi Odası ───────────────────────────────
        [Header("Lobi Odası")]
        [SerializeField] private Transform       _playerListContainer;
        [SerializeField] private GameObject      _playerListItemPrefab;
        [SerializeField] private Button          _startGameButton;
        [SerializeField] private Button          _leaveLobbyButton;
        [SerializeField] private TextMeshProUGUI _lobbyNameText;
        [SerializeField] private TextMeshProUGUI _playerCountText;
        [SerializeField] private TextMeshProUGUI _lobbyCodeText;

        // ─── Inspector: Loading ──────────────────────────────────
        [Header("Loading")]
        [SerializeField] private TextMeshProUGUI _loadingText;

        // ─── Runtime ─────────────────────────────────────────────
        private List<Lobby>      _foundLobbies    = new();
        private List<GameObject> _lobbyListItems  = new();
        private List<GameObject> _playerListItems = new();

        // ─── Unity ───────────────────────────────────────────────
        private void Start()
        {
            SubscribeToEvents();
            SetupButtons();

            // Steam ismi göster
            if (_playerNameText != null)
                _playerNameText.text = SteamClient.IsValid ? SteamClient.Name : "Oyuncu";

            // Versiyon göster
            if (_versionText != null)
                _versionText.text = $"v{Application.version}";

            ShowPanel(_mainMenuPanel);
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        // ─── Event Bağlantıları ──────────────────────────────────
        private void SubscribeToEvents()
        {
            if (GameNetworkManager.Instance == null) return;

            GameNetworkManager.Instance.OnLobbyCreated     += HandleLobbyCreated;
            GameNetworkManager.Instance.OnLobbyJoined      += HandleLobbyJoined;
            GameNetworkManager.Instance.OnLobbyLeft        += HandleLobbyLeft;
            GameNetworkManager.Instance.OnPlayerJoined     += HandlePlayerJoined;
            GameNetworkManager.Instance.OnPlayerLeft       += HandlePlayerLeft;
            GameNetworkManager.Instance.OnConnectionFailed += HandleConnectionFailed;
        }

        private void UnsubscribeFromEvents()
        {
            if (GameNetworkManager.Instance == null) return;

            GameNetworkManager.Instance.OnLobbyCreated     -= HandleLobbyCreated;
            GameNetworkManager.Instance.OnLobbyJoined      -= HandleLobbyJoined;
            GameNetworkManager.Instance.OnLobbyLeft        -= HandleLobbyLeft;
            GameNetworkManager.Instance.OnPlayerJoined     -= HandlePlayerJoined;
            GameNetworkManager.Instance.OnPlayerLeft       -= HandlePlayerLeft;
            GameNetworkManager.Instance.OnConnectionFailed -= HandleConnectionFailed;
        }

        // ─── Buton Kurulumu ──────────────────────────────────────
        private void SetupButtons()
        {
            _createLobbyButton?.onClick.AddListener(OnCreateLobbyClicked);
            _browseLobbyButton?.onClick.AddListener(OnBrowseLobbyClicked);
            _quitButton?.onClick.AddListener(OnQuitClicked);

            _refreshButton?.onClick.AddListener(OnRefreshClicked);
            _backFromBrowserButton?.onClick.AddListener(OnBackFromBrowserClicked);

            _startGameButton?.onClick.AddListener(OnStartGameClicked);
            _leaveLobbyButton?.onClick.AddListener(OnLeaveLobbyClicked);

            // Başlangıçta start butonu sadece host'a görünür
            _startGameButton?.gameObject.SetActive(false);
        }

        // ─── Ana Menü Butonları ───────────────────────────────────
        private async void OnCreateLobbyClicked()
        {
            if (GameNetworkManager.Instance == null) return;

            ShowLoading("Lobi oluşturuluyor...");

            string lobbyName = _lobbyNameInput != null && !string.IsNullOrEmpty(_lobbyNameInput.text)
                ? _lobbyNameInput.text
                : string.Empty;

            await GameNetworkManager.Instance.CreateLobbyAsync(lobbyName);
        }

        private async void OnBrowseLobbyClicked()
        {
            ShowPanel(_lobbyBrowserPanel);
            await RefreshLobbyList();
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ─── Lobi Tarayıcı Butonları ─────────────────────────────
        private async void OnRefreshClicked()
        {
            await RefreshLobbyList();
        }

        private void OnBackFromBrowserClicked()
        {
            ShowPanel(_mainMenuPanel);
        }

        // ─── Lobi Odası Butonları ─────────────────────────────────
        private void OnStartGameClicked()
        {
            GameNetworkManager.Instance?.StartGame();
        }

        private async void OnLeaveLobbyClicked()
        {
            if (GameNetworkManager.Instance == null) return;
            ShowLoading("Lobiden çıkılıyor...");
            await GameNetworkManager.Instance.LeaveLobbyAsync();
        }

        // ─── Lobi Listesi ────────────────────────────────────────
        private async System.Threading.Tasks.Task RefreshLobbyList()
        {
            ShowLoading("Lobiler aranıyor...");
            ClearLobbyList();

            if (GameNetworkManager.Instance == null)
            {
                HideLoading();
                return;
            }

            Lobby[] lobbies = await GameNetworkManager.Instance.FetchLobbiesAsync();
            _foundLobbies   = new List<Lobby>(lobbies);

            HideLoading();
            PopulateLobbyList();
        }

        private void PopulateLobbyList()
        {
            if (_lobbyListContainer == null || _lobbyListItemPrefab == null) return;

            foreach (var lobby in _foundLobbies)
            {
                GameObject item = Instantiate(_lobbyListItemPrefab, _lobbyListContainer);
                _lobbyListItems.Add(item);

                // İsim
                string name      = lobby.GetData("name");
                int    memberCnt = lobby.MemberCount;
                int    maxCnt    = lobby.MaxMembers;

                if (item.TryGetComponent(out LobbyListItem listItem))
                    listItem.Setup(name, memberCnt, maxCnt, () => OnJoinLobbyClicked(lobby));
                else
                {
                    // LobbyListItem scripti yoksa manuel text atama
                    var texts = item.GetComponentsInChildren<TextMeshProUGUI>();
                    if (texts.Length > 0) texts[0].text = $"{name} ({memberCnt}/{maxCnt})";

                    var btn = item.GetComponentInChildren<Button>();
                    if (btn != null)
                    {
                        var capturedLobby = lobby;
                        btn.onClick.AddListener(() => OnJoinLobbyClicked(capturedLobby));
                    }
                }
            }

            if (_lobbyCountText != null)
                _lobbyCountText.text = $"{_foundLobbies.Count} lobi bulundu";
        }

        private void ClearLobbyList()
        {
            foreach (var item in _lobbyListItems)
                if (item != null) Destroy(item);
            _lobbyListItems.Clear();
        }

        private async void OnJoinLobbyClicked(Lobby lobby)
        {
            if (GameNetworkManager.Instance == null) return;
            ShowLoading("Lobiye katılınıyor...");
            await GameNetworkManager.Instance.JoinLobbyAsync(lobby.Id);
        }

        // ─── Oyuncu Listesi ──────────────────────────────────────
        private void RefreshPlayerList()
        {
            if (_playerListContainer == null || _playerListItemPrefab == null) return;

            foreach (var item in _playerListItems)
                if (item != null) Destroy(item);
            _playerListItems.Clear();

            if (GameNetworkManager.Instance?.CurrentLobby == null) return;

            foreach (var member in GameNetworkManager.Instance.CurrentLobby.Value.Members)
            {
                GameObject item = Instantiate(_playerListItemPrefab, _playerListContainer);
                _playerListItems.Add(item);

                var texts = item.GetComponentsInChildren<TextMeshProUGUI>();
                if (texts.Length > 0)
                    texts[0].text = member.Name;

                // Host rozeti
                bool isHost = member.Id == GameNetworkManager.Instance.CurrentLobby.Value.Owner.Id;
                if (texts.Length > 1)
                    texts[1].text = isHost ? "👑 Host" : "";
            }

            UpdatePlayerCount();
        }

        private void UpdatePlayerCount()
        {
            if (_playerCountText == null || GameNetworkManager.Instance?.CurrentLobby == null) return;

            var lobby = GameNetworkManager.Instance.CurrentLobby.Value;
            _playerCountText.text = $"{lobby.MemberCount}/{lobby.MaxMembers} Oyuncu";
        }

        // ─── GameNetworkManager Callbacks ────────────────────────
        private void HandleLobbyCreated()
        {
            HideLoading();
            ShowPanel(_lobbyRoomPanel);

            if (GameNetworkManager.Instance?.CurrentLobby != null)
            {
                var lobby = GameNetworkManager.Instance.CurrentLobby.Value;

                if (_lobbyNameText != null)
                    _lobbyNameText.text = lobby.GetData("name");

                if (_lobbyCodeText != null)
                    _lobbyCodeText.text = $"ID: {lobby.Id}";
            }

            // Host: start butonu görünür
            _startGameButton?.gameObject.SetActive(true);

            RefreshPlayerList();
        }

        private void HandleLobbyJoined(Lobby lobby)
        {
            HideLoading();
            ShowPanel(_lobbyRoomPanel);

            if (_lobbyNameText != null)
                _lobbyNameText.text = lobby.GetData("name");

            if (_lobbyCodeText != null)
                _lobbyCodeText.text = $"ID: {lobby.Id}";

            // Client: start butonu gizli
            _startGameButton?.gameObject.SetActive(false);

            RefreshPlayerList();
        }

        private void HandleLobbyLeft()
        {
            HideLoading();
            ShowPanel(_mainMenuPanel);
            ClearLobbyList();

            foreach (var item in _playerListItems)
                if (item != null) Destroy(item);
            _playerListItems.Clear();
        }

        private void HandlePlayerJoined(Friend friend)
        {
            RefreshPlayerList();
        }

        private void HandlePlayerLeft(Friend friend)
        {
            RefreshPlayerList();
        }

        private void HandleConnectionFailed(string reason)
        {
            HideLoading();
            UIManager.Instance?.ShowNotification($"Bağlantı hatası: {reason}", UnityEngine.Color.red);
        }

        // ─── Panel Yönetimi ──────────────────────────────────────
        private void ShowPanel(GameObject panel)
        {
            _mainMenuPanel?.SetActive(false);
            _lobbyBrowserPanel?.SetActive(false);
            _lobbyRoomPanel?.SetActive(false);

            panel?.SetActive(true);
        }

        private void ShowLoading(string message = "Yükleniyor...")
        {
            _loadingPanel?.SetActive(true);
            if (_loadingText != null) _loadingText.text = message;
        }

        private void HideLoading()
        {
            _loadingPanel?.SetActive(false);
        }
    }

    /// <summary>
    /// Lobi listesindeki tek bir satır.
    /// Prefab'a ekle — LobbyUI otomatik bulur.
    /// </summary>
    public class LobbyListItem : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _playerCountText;
        [SerializeField] private Button          _joinButton;

        public void Setup(string lobbyName, int current, int max, System.Action onJoin)
        {
            if (_nameText        != null) _nameText.text        = lobbyName;
            if (_playerCountText != null) _playerCountText.text = $"{current}/{max}";
            if (_joinButton      != null) _joinButton.onClick.AddListener(() => onJoin?.Invoke());
        }
    }
}
