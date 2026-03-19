using UnityEngine;
using Unity.Netcode;
using WeBussedUp.Gameplay;
using WeBussedUp.Core.Managers;
using WeBussedUp.Gameplay.Building;


namespace WeBussedUp.Player
{
    /// <summary>
    /// Temizlik modu — T tuşu ile aç/kapat.
    /// Sol tık + raycast → DirtSurface veya CleaningTask'a temizleme aksiyonu gönderir.
    /// Build mod veya elde eşya varsa temizlik moduna geçilmez.
    /// </summary>
    public class CleaningTool : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Temizlik Ayarları")]
        [SerializeField] private float     _cleanRange       = 3f;
        [SerializeField] private float     _cleanRate        = 0.2f;  // Saniyede kaç aksiyon
        [SerializeField] private LayerMask _cleanableLayer;

        [Header("Görsel")]
        [SerializeField] private GameObject _toolVisual;      // Bez/sünger modeli
        [SerializeField] private ParticleSystem _cleanParticle;

        [Header("UI")]
        [SerializeField] private string _cleanModeOnMsg  = "Temizlik Modu — Sol tık: Temizle | T: Kapat";
        [SerializeField] private string _cleanModeOffMsg = "Temizlik Modu Kapatıldı";

        // ─── Runtime ─────────────────────────────────────────────
        private bool         _isCleanMode  = false;
        private float        _cleanTimer   = 0f;
        private PlayerInputs _input;
        private Transform    _cameraTransform;

        // ─── Public API ──────────────────────────────────────────
        public bool IsCleanMode => _isCleanMode;

        // ─── Unity ───────────────────────────────────────────────
        private void Awake()
        {
            _input = new PlayerInputs();
        }

        private void OnEnable()  => _input.Enable();
        private void OnDisable() => _input.Disable();

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;

            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) _cameraTransform = cam.transform;

            SetToolVisual(false);
        }

        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (!IsOwner || _cameraTransform == null) return;

            HandleCleanModeToggle();

            if (!_isCleanMode) return;

            _cleanTimer += Time.deltaTime;
            HandleCleanAction();
        }

        // ─── Mod Aç/Kapat ────────────────────────────────────────
        private void HandleCleanModeToggle()
        {
            if (!_input.Player.ToggleCleanMode.WasPressedThisFrame()) return;

            if (_isCleanMode)
            {
                ExitCleanMode();
                return;
            }

            // Build mod aktifse geçme
            if (TryGetComponent(out BuildingSystem buildSys) &&
                buildSys.IsBuildMode)
            {
                WeBussedUp.UI.UIManager.Instance?.ShowNotification(
                    "Önce inşaat modundan çık! (B)", Color.yellow);
                return;
            }

            // Elde eşya varsa geçme
            if (TryGetComponent(out PlayerCarrySystem carry) && carry.IsHoldingItem)
            {
                WeBussedUp.UI.UIManager.Instance?.ShowNotification(
                    "Elindeki eşyayı bırak! (G)", Color.yellow);
                return;
            }

            EnterCleanMode();
        }

        private void EnterCleanMode()
        {
            _isCleanMode = true;
            SetToolVisual(true);
            WeBussedUp.UI.UIManager.Instance?.ShowNotification(_cleanModeOnMsg, Color.cyan);
        }

        private void ExitCleanMode()
        {
            _isCleanMode = false;
            SetToolVisual(false);
            _cleanParticle?.Stop();
            WeBussedUp.UI.UIManager.Instance?.ShowNotification(_cleanModeOffMsg, Color.white);
        }

        // ─── Temizleme ───────────────────────────────────────────
        private void HandleCleanAction()
        {
            // Sol tık basılı tutulunca temizle
            if (!_input.Player.PrimaryAction.IsPressed()) return;
            if (_cleanTimer < _cleanRate)                 return;

            _cleanTimer = 0f;

            Ray ray = new Ray(_cameraTransform.position, _cameraTransform.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, _cleanRange, _cleanableLayer))
            {
                _cleanParticle?.Stop();
                return;
            }

            _cleanParticle?.Play();

            ulong localId = NetworkManager.Singleton.LocalClientId;

            // CleaningTask mı?
            if (hit.collider.TryGetComponent(out CleaningTask task))
            {
                task.Interact(localId);
                return;
            }

            // DirtSurface mi?
            if (hit.collider.TryGetComponent(out DirtSurface dirt))
            {
                dirt.CleanServerRpc(0.1f, localId);
                return;
            }
        }

        // ─── Util ────────────────────────────────────────────────
        private void SetToolVisual(bool active)
        {
            if (_toolVisual != null) _toolVisual.SetActive(active);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_cameraTransform == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(_cameraTransform.position,
                _cameraTransform.forward * _cleanRange);
        }
#endif
    }
}