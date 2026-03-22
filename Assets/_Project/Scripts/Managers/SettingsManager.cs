using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Audio;

namespace WeBussedUp.Core.Managers
{
    /// <summary>
    /// Ses, grafik ve hassasiyet ayarlarını yönetir.
    /// PlayerPrefs ile kalıcı kaydeder.
    /// </summary>
    public class SettingsManager : MonoBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────
        public static SettingsManager Instance { get; private set; }

        // ─── Inspector ───────────────────────────────────────────
        [Header("Audio Mixer")]
        [SerializeField] private AudioMixer _audioMixer;
        [SerializeField] private string     _masterVolumeParam  = "MasterVolume";
        [SerializeField] private string     _musicVolumeParam   = "MusicVolume";
        [SerializeField] private string     _sfxVolumeParam     = "SFXVolume";

        [Header("UI — Ses")]
        [SerializeField] private Slider          _masterSlider;
        [SerializeField] private Slider          _musicSlider;
        [SerializeField] private Slider          _sfxSlider;
        [SerializeField] private TextMeshProUGUI _masterValueText;
        [SerializeField] private TextMeshProUGUI _musicValueText;
        [SerializeField] private TextMeshProUGUI _sfxValueText;

        [Header("UI — Grafik")]
        [SerializeField] private TMP_Dropdown _qualityDropdown;
        [SerializeField] private Toggle       _fullscreenToggle;
        [SerializeField] private Toggle       _vsyncToggle;
        [SerializeField] private TMP_Dropdown _resolutionDropdown;

        [Header("UI — Oyuncu")]
        [SerializeField] private Slider          _mouseSensitivitySlider;
        [SerializeField] private TextMeshProUGUI _sensitivityValueText;
        [SerializeField] private Toggle          _invertYToggle;

        [Header("UI — Butonlar")]
        [SerializeField] private Button _applyButton;
        [SerializeField] private Button _resetButton;
        [SerializeField] private Button _closeButton;

        [Header("Panel")]
        [SerializeField] private GameObject _settingsPanel;

        // ─── PlayerPrefs Anahtarları ─────────────────────────────
        private const string MASTER_VOL    = "MasterVolume";
        private const string MUSIC_VOL     = "MusicVolume";
        private const string SFX_VOL       = "SFXVolume";
        private const string QUALITY       = "GraphicsQuality";
        private const string FULLSCREEN    = "Fullscreen";
        private const string VSYNC         = "VSync";
        private const string MOUSE_SENS    = "MouseSensitivity";
        private const string INVERT_Y      = "InvertY";
        private const string RESOLUTION    = "Resolution";

        // ─── Runtime ─────────────────────────────────────────────
        private Resolution[] _resolutions;
        public float MouseSensitivity { get; private set; } = 1f;
        public bool  InvertY          { get; private set; } = false;

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

        private void Start()
        {
            SetupResolutions();
            SetupQuality();
            SetupButtons();
            LoadSettings();

            _settingsPanel?.SetActive(false);
        }

        // ─── Kurulum ─────────────────────────────────────────────
        private void SetupResolutions()
        {
            if (_resolutionDropdown == null) return;

            _resolutions = Screen.resolutions;
            _resolutionDropdown.ClearOptions();

            var options = new System.Collections.Generic.List<string>();
            int currentIndex = 0;

            for (int i = 0; i < _resolutions.Length; i++)
            {
                string option = $"{_resolutions[i].width}x{_resolutions[i].height} " +
                                $"@{_resolutions[i].refreshRateRatio.numerator}Hz";
                options.Add(option);

                if (_resolutions[i].width  == Screen.currentResolution.width &&
                    _resolutions[i].height == Screen.currentResolution.height)
                    currentIndex = i;
            }

            _resolutionDropdown.AddOptions(options);
            _resolutionDropdown.value = currentIndex;
            _resolutionDropdown.RefreshShownValue();
        }

        private void SetupQuality()
        {
            if (_qualityDropdown == null) return;

            _qualityDropdown.ClearOptions();
            _qualityDropdown.AddOptions(
                new System.Collections.Generic.List<string>(QualitySettings.names));
            _qualityDropdown.value = QualitySettings.GetQualityLevel();
        }

        private void SetupButtons()
        {
            _applyButton?.onClick.AddListener(ApplySettings);
            _resetButton?.onClick.AddListener(ResetToDefaults);
            _closeButton?.onClick.AddListener(() => _settingsPanel?.SetActive(false));

            // Slider event'leri
            _masterSlider?.onValueChanged.AddListener(v =>
            {
                SetVolume(_masterVolumeParam, v);
                if (_masterValueText != null) _masterValueText.text = $"{(int)(v * 100)}%";
            });

            _musicSlider?.onValueChanged.AddListener(v =>
            {
                SetVolume(_musicVolumeParam, v);
                if (_musicValueText != null) _musicValueText.text = $"{(int)(v * 100)}%";
            });

            _sfxSlider?.onValueChanged.AddListener(v =>
            {
                SetVolume(_sfxVolumeParam, v);
                if (_sfxValueText != null) _sfxValueText.text = $"{(int)(v * 100)}%";
            });

            _mouseSensitivitySlider?.onValueChanged.AddListener(v =>
            {
                MouseSensitivity = v;
                if (_sensitivityValueText != null)
                    _sensitivityValueText.text = $"{v:F1}";
            });

            _invertYToggle?.onValueChanged.AddListener(v => InvertY = v);
        }

        // ─── Ayarları Yükle / Kaydet ──────────────────────────────
        private void LoadSettings()
        {
            // Ses
            float master = PlayerPrefs.GetFloat(MASTER_VOL, 0.8f);
            float music  = PlayerPrefs.GetFloat(MUSIC_VOL,  0.6f);
            float sfx    = PlayerPrefs.GetFloat(SFX_VOL,    0.8f);

            if (_masterSlider != null) _masterSlider.value = master;
            if (_musicSlider  != null) _musicSlider.value  = music;
            if (_sfxSlider    != null) _sfxSlider.value    = sfx;

            SetVolume(_masterVolumeParam, master);
            SetVolume(_musicVolumeParam,  music);
            SetVolume(_sfxVolumeParam,    sfx);

            // Grafik
            int quality    = PlayerPrefs.GetInt(QUALITY,    QualitySettings.GetQualityLevel());
            bool fullscreen = PlayerPrefs.GetInt(FULLSCREEN, 1) == 1;
            bool vsync      = PlayerPrefs.GetInt(VSYNC,      1) == 1;

            if (_qualityDropdown    != null) _qualityDropdown.value       = quality;
            if (_fullscreenToggle   != null) _fullscreenToggle.isOn       = fullscreen;
            if (_vsyncToggle        != null) _vsyncToggle.isOn            = vsync;

            QualitySettings.SetQualityLevel(quality);
            Screen.fullScreen        = fullscreen;
            QualitySettings.vSyncCount = vsync ? 1 : 0;

            // Oyuncu
            MouseSensitivity = PlayerPrefs.GetFloat(MOUSE_SENS, 1f);
            InvertY          = PlayerPrefs.GetInt(INVERT_Y, 0) == 1;

            if (_mouseSensitivitySlider != null) _mouseSensitivitySlider.value = MouseSensitivity;
            if (_invertYToggle          != null) _invertYToggle.isOn           = InvertY;

            // Çözünürlük
            int resIndex = PlayerPrefs.GetInt(RESOLUTION, 0);
            if (_resolutionDropdown != null && resIndex < _resolutions.Length)
                _resolutionDropdown.value = resIndex;

            Debug.Log("[SettingsManager] Ayarlar yüklendi.");
        }

        private void ApplySettings()
        {
            // Ses
            PlayerPrefs.SetFloat(MASTER_VOL, _masterSlider?.value ?? 0.8f);
            PlayerPrefs.SetFloat(MUSIC_VOL,  _musicSlider?.value  ?? 0.6f);
            PlayerPrefs.SetFloat(SFX_VOL,    _sfxSlider?.value    ?? 0.8f);

            // Grafik
            int quality = _qualityDropdown?.value ?? 0;
            QualitySettings.SetQualityLevel(quality);
            PlayerPrefs.SetInt(QUALITY, quality);

            bool fullscreen = _fullscreenToggle?.isOn ?? true;
            Screen.fullScreen = fullscreen;
            PlayerPrefs.SetInt(FULLSCREEN, fullscreen ? 1 : 0);

            bool vsync = _vsyncToggle?.isOn ?? true;
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            PlayerPrefs.SetInt(VSYNC, vsync ? 1 : 0);

            // Çözünürlük
            if (_resolutions != null && _resolutionDropdown != null)
            {
                int resIndex = _resolutionDropdown.value;
                if (resIndex < _resolutions.Length)
                {
                    Resolution res = _resolutions[resIndex];
                    Screen.SetResolution(res.width, res.height, fullscreen);
                    PlayerPrefs.SetInt(RESOLUTION, resIndex);
                }
            }

            // Oyuncu
            PlayerPrefs.SetFloat(MOUSE_SENS, MouseSensitivity);
            PlayerPrefs.SetInt(INVERT_Y,     InvertY ? 1 : 0);

            PlayerPrefs.Save();

            WeBussedUp.UI.UIManager.Instance?.ShowNotification(
                "Ayarlar uygulandı! ⚙️", Color.cyan);

            Debug.Log("[SettingsManager] Ayarlar kaydedildi.");
        }

        private void ResetToDefaults()
        {
            if (_masterSlider           != null) _masterSlider.value           = 0.8f;
            if (_musicSlider            != null) _musicSlider.value            = 0.6f;
            if (_sfxSlider              != null) _sfxSlider.value              = 0.8f;
            if (_mouseSensitivitySlider != null) _mouseSensitivitySlider.value = 1f;
            if (_invertYToggle          != null) _invertYToggle.isOn           = false;
            if (_fullscreenToggle       != null) _fullscreenToggle.isOn        = true;
            if (_vsyncToggle            != null) _vsyncToggle.isOn             = true;

            ApplySettings();
        }

        // ─── Ses ─────────────────────────────────────────────────
        private void SetVolume(string param, float value)
        {
            if (_audioMixer == null) return;

            // 0 → -80dB, 1 → 0dB (logaritmik)
            float db = value > 0.001f
                ? Mathf.Log10(value) * 20f
                : -80f;

            _audioMixer.SetFloat(param, db);
        }

        // ─── Public API ──────────────────────────────────────────
        public void OpenSettings()  => _settingsPanel?.SetActive(true);
        public void CloseSettings() => _settingsPanel?.SetActive(false);
    }
}