using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using System.Collections;
using WeBussedUp.Core.Managers;
using WeBussedUp.UI;

namespace WeBussedUp.Core
{
    public enum GameState
    {
        MainMenu,
        Loading,
        Playing,
        Paused,
        DayEnd,
        GameOver
    }

    /// <summary>
    /// Oyunun genel akışını yönetir.
    /// Sahne geçişleri, pause, game over ve gün sonu tetikleyicisi.
    /// Tüm Manager'ların başlatılma sırasını koordine eder.
    /// </summary>
    public class GameManager : NetworkBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────
        public static GameManager Instance { get; private set; }

        // ─── Inspector ───────────────────────────────────────────
        [Header("Sahneler")]
        [SerializeField] private string _mainMenuScene = "MainMenu";
        [SerializeField] private string _gameScene     = "GameScene";

        [Header("Oyun Ayarları")]
        [SerializeField] private float _gameStartDelay = 2f;  // Oyun başlamadan önce bekleme

        [Header("Game Over")]
        [SerializeField] private float _bankruptcyThreshold = -1000f; // Bu bakiyenin altında game over

        [Header("UI")]
        [SerializeField] private GameObject _pauseMenuPanel;
        [SerializeField] private GameObject _gameOverPanel;
        [SerializeField] private GameObject _loadingPanel;

        // ─── Network State ───────────────────────────────────────
        public NetworkVariable<int> CurrentState = new NetworkVariable<int>(
            (int)GameState.Loading,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ─── Runtime ─────────────────────────────────────────────
        public GameState State => (GameState)CurrentState.Value;
        public bool      IsPaused  => State == GameState.Paused;
        public bool      IsPlaying => State == GameState.Playing;

        // ─── Events ──────────────────────────────────────────────
        public event System.Action<GameState> OnGameStateChanged;
        public event System.Action            OnGamePaused;
        public event System.Action            OnGameResumed;
        public event System.Action            OnGameOver;

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
        }

        public override void OnNetworkSpawn()
        {
            CurrentState.OnValueChanged += OnStateChanged;

            if (IsServer)
                StartCoroutine(InitializeGame());
        }

        public override void OnNetworkDespawn()
        {
            CurrentState.OnValueChanged -= OnStateChanged;
        }

        // ─── Oyun Başlatma ───────────────────────────────────────
        private IEnumerator InitializeGame()
        {
            SetState(GameState.Loading);
            ShowLoadingPanel(true);

            // Manager'ların hazır olmasını bekle
            yield return new WaitForSeconds(_gameStartDelay);

            // Ekonomi kontrolü
            if (EconomyManager.Instance != null)
                EconomyManager.Instance.OnMoneyChanged += CheckBankruptcy;

            // Zaman sistemi başlat
            if (TimeManager.Instance != null)
                TimeManager.Instance.OnNewDay += HandleNewDay;

            ShowLoadingPanel(false);
            SetState(GameState.Playing);

            Debug.Log("[GameManager] Oyun başladı!");
        }

        // ─── State Yönetimi ──────────────────────────────────────
        private void SetState(GameState newState)
        {
            if (!IsServer) return;
            CurrentState.Value = (int)newState;
        }

        private void OnStateChanged(int oldVal, int newVal)
        {
            GameState newState = (GameState)newVal;
            OnGameStateChanged?.Invoke(newState);

            switch (newState)
            {
                case GameState.Playing:
                    Time.timeScale = 1f;
                    _pauseMenuPanel?.SetActive(false);
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible   = false;
                    OnGameResumed?.Invoke();
                    break;

                case GameState.Paused:
                    Time.timeScale = 0f;
                    _pauseMenuPanel?.SetActive(true);
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible   = true;
                    OnGamePaused?.Invoke();
                    break;

                case GameState.DayEnd:
                    Time.timeScale = 0f;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible   = true;
                    break;

                case GameState.GameOver:
                    Time.timeScale = 0f;
                    _gameOverPanel?.SetActive(true);
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible   = true;
                    OnGameOver?.Invoke();
                    break;
            }
        }

        // ─── Pause ───────────────────────────────────────────────
        private void Update()
        {
            if (!IsOwner) return;

            // ESC ile pause toggle — sadece playing veya paused'da
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (State == GameState.Playing)
                    RequestPauseServerRpc();
                else if (State == GameState.Paused)
                    RequestResumeServerRpc();
            }
        }

        [Rpc(SendTo.Server)]
        public void RequestPauseServerRpc()
        {
            if (State != GameState.Playing) return;
            SetState(GameState.Paused);
        }

        [Rpc(SendTo.Server)]
        public void RequestResumeServerRpc()
        {
            if (State != GameState.Paused) return;
            SetState(GameState.Playing);
        }

        // ─── Gün Sonu ────────────────────────────────────────────
        private void HandleNewDay(int day)
        {
            if (!IsServer) return;

            SetState(GameState.DayEnd);
            TriggerDayEndClientRpc(day);

            // Kısa süre sonra oyuna dön
            StartCoroutine(ReturnToPlayingAfterDelay(3f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TriggerDayEndClientRpc(int day)
        {
            // DailyReportUI'ı tetikle
            FindAnyObjectByType<DailyReportUI>()?.ShowReport(day - 1);
        }

        private IEnumerator ReturnToPlayingAfterDelay(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);

            if (State == GameState.DayEnd)
                SetState(GameState.Playing);
        }

        // ─── Game Over ───────────────────────────────────────────
        private void CheckBankruptcy(float newBalance)
        {
            if (!IsServer) return;
            if (newBalance <= _bankruptcyThreshold)
                TriggerGameOver("İflas ettin! Bakiye çok düştü.");
        }

        public void TriggerGameOver(string reason = "")
        {
            if (!IsServer) return;

            SetState(GameState.GameOver);
            GameOverClientRpc(reason);

            Debug.Log($"[GameManager] Game Over: {reason}");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GameOverClientRpc(string reason)
        {
            UIManager.Instance?.ShowNotification(
                $"Oyun Bitti! {reason}", Color.red);
        }

        // ─── Sahne Geçişleri ─────────────────────────────────────
        [Rpc(SendTo.Server)]
        public void RequestReturnToMenuServerRpc()
        {
            ReturnToMainMenu();
        }

        private void ReturnToMainMenu()
        {
            if (!IsServer) return;

            Time.timeScale = 1f;

            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();

            SceneManager.LoadScene(_mainMenuScene);
        }

        public void RestartGame()
        {
            if (!IsServer) return;

            Time.timeScale = 1f;
            SceneManager.LoadScene(_gameScene);
        }

        // ─── UI Yardımcıları ─────────────────────────────────────
        private void ShowLoadingPanel(bool show)
        {
            ShowLoadingClientRpc(show);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShowLoadingClientRpc(bool show)
        {
            _loadingPanel?.SetActive(show);
        }

        private void OnApplicationQuit()
        {
            Time.timeScale = 1f;
        }
    }
}