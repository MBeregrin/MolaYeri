using UnityEngine;
using Unity.Netcode;
using WeBussedUp.Core.Data;
using WeBussedUp.Core;
using WeBussedUp.Core.Managers;
using WeBussedUp.Gameplay.Building;
using WeBussedUp.Player;

namespace WeBussedUp.Gameplay.Building
{
    /// <summary>
    /// İnşaat/yerleştirme sistemi.
    /// B tuşu → Build mod aç/kapat
    /// Ghost preview yeşil/kırmızı — pivot offset + grid snap
    /// R tuşu → 90° rotate (sadece build modda)
    /// Sol tık → yerleştir, Sağ tık/ESC → iptal
    /// CarrySystem ve CleaningTool ile mod çakışması önlenir.
    /// </summary>
    public class BuildingSystem : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Yerleştirme Ayarları")]
        [SerializeField] private float _gridCellSize  = 1f;
        [SerializeField] private float _buildRange    = 8f;
        [SerializeField] private float _wallSnapDepth = 0.05f;

        [Header("Katmanlar")]
        [SerializeField] private LayerMask _floorLayer;
        [SerializeField] private LayerMask _wallLayer;
        [SerializeField] private LayerMask _obstacleLayer;

        [Header("Ghost Ayarları")]
        [SerializeField] private string _colorProperty = "_BaseColor";
        [SerializeField] private Color  _colorValid    = new Color(0f, 1f, 0f, 0.4f);
        [SerializeField] private Color  _colorInvalid  = new Color(1f, 0f, 0f, 0.4f);
        [SerializeField] private Color  _colorWall     = new Color(0f, 0.5f, 1f, 0.4f);

        [Header("UI Bildirimi")]
        [SerializeField] private string _buildModeOnMsg  = "İnşaat Modu Açık — Sol tık: Yerleştir | R: Döndür | Sağ tık: İptal";
        [SerializeField] private string _buildModeOffMsg = "İnşaat Modu Kapatıldı";

        // ─── Runtime ─────────────────────────────────────────────
        private ItemData    _currentItem;
        private GameObject  _ghostObject;
        private Renderer[]  _ghostRenderers;
        private Material[]  _ghostMaterials;

        private bool  _isBuildMode      = false;
        private bool  _canPlace         = false;
        private float _currentYRotation = 0f;

        private Transform    _cameraTransform;
        private PlayerInputs _input;

        // ─── Public API ──────────────────────────────────────────
        public bool IsBuildMode => _isBuildMode;

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;

            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) _cameraTransform = cam.transform;
            else Debug.LogWarning("[BuildingSystem] Camera bulunamadı!", this);

            _input = new PlayerInputs();
            _input.Enable();
        }

        public override void OnNetworkDespawn()
        {
            _input?.Disable();
            DestroyGhost();
        }

        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (!IsOwner || _cameraTransform == null) return;

            HandleBuildModeToggle();

            if (!_isBuildMode || _currentItem == null || _ghostObject == null) return;

            UpdateGhostTransform();
            HandleRotationInput();
            HandlePlacementInput();
        }

        // ─── Build Mod Aç/Kapat ──────────────────────────────────
        public void StartBuilding(ItemData itemData)
        {
            if (itemData == null || !itemData.isBuildable)
            {
                Debug.LogWarning("[BuildingSystem] Bu eşya inşa edilemez.");
                return;
            }

            // Temizlik modu aktifse build moda geçme
            if (TryGetComponent(out CleaningTool cleanTool) && cleanTool.IsCleanMode)
            {
                WeBussedUp.UI.UIManager.Instance?.ShowNotification(
                    "Önce temizlik modundan çık! (T)", Color.yellow);
                return;
            }

            // Elde eşya varsa build moda geçme
            if (TryGetComponent(out Player.PlayerCarrySystem carry) && carry.IsHoldingItem)
            {
                WeBussedUp.UI.UIManager.Instance?.ShowNotification(
                    "Elindeki eşyayı bırak! (G)", Color.yellow);
                return;
            }

            _currentItem      = itemData;
            _isBuildMode      = true;
            _currentYRotation = 0f;

            RebuildGhost();

            WeBussedUp.UI.UIManager.Instance?.ShowNotification(
                _buildModeOnMsg, Color.cyan);
        }

        public void StopBuilding()
        {
            _isBuildMode = false;
            _currentItem = null;
            DestroyGhost();

            WeBussedUp.UI.UIManager.Instance?.ShowNotification(
                _buildModeOffMsg, Color.white);
        }

        private void HandleBuildModeToggle()
        {
            if (!_input.Player.ToggleBuild.WasPressedThisFrame()) return;

            if (_isBuildMode)
            {
                StopBuilding();
                return;
            }

            // Test: ilk buildable item
            if (ItemDatabase.Instance != null)
            {
                foreach (var item in ItemDatabase.Instance.allItems)
                {
                    if (item != null && item.isBuildable)
                    {
                        StartBuilding(item);
                        break;
                    }
                }
            }
        }

        // ─── Ghost Yönetimi ──────────────────────────────────────
        private void RebuildGhost()
        {
            DestroyGhost();

            _ghostObject = Instantiate(_currentItem.prefab);

            foreach (var col in _ghostObject.GetComponentsInChildren<Collider>())
                col.enabled = false;

            foreach (var rb in _ghostObject.GetComponentsInChildren<Rigidbody>())
                Destroy(rb);

            if (_ghostObject.TryGetComponent(out NetworkObject netObj))
                Destroy(netObj);

            _ghostRenderers = _ghostObject.GetComponentsInChildren<Renderer>();
            CacheGhostMaterials();
        }

        private void DestroyGhost()
        {
            if (_ghostObject != null) Destroy(_ghostObject);
            _ghostObject    = null;
            _ghostRenderers = null;
            _ghostMaterials = null;
        }

        private void CacheGhostMaterials()
        {
            var mats = new System.Collections.Generic.List<Material>();
            foreach (var rend in _ghostRenderers)
                foreach (var mat in rend.materials)
                    mats.Add(new Material(mat));

            _ghostMaterials = mats.ToArray();

            int idx = 0;
            foreach (var rend in _ghostRenderers)
            {
                var assigned = new Material[rend.materials.Length];
                for (int i = 0; i < assigned.Length; i++)
                    assigned[i] = _ghostMaterials[idx++];
                rend.materials = assigned;
            }
        }

        private void SetGhostColor(Color color)
        {
            if (_ghostMaterials == null) return;
            foreach (var mat in _ghostMaterials)
                if (mat.HasProperty(_colorProperty))
                    mat.SetColor(_colorProperty, color);
        }

        // ─── Ghost Transform ─────────────────────────────────────
        private void UpdateGhostTransform()
        {
            bool      isWall      = _currentItem.placementSurface == PlacementSurface.Wall;
            LayerMask targetLayer = isWall ? _wallLayer : _floorLayer;

            Ray ray = new Ray(_cameraTransform.position, _cameraTransform.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, _buildRange, targetLayer))
            {
                _ghostObject.SetActive(false);
                _canPlace = false;
                return;
            }

            _ghostObject.SetActive(true);

            if (isWall) ApplyWallTransform(hit);
            else        ApplyFloorTransform(hit);

            CheckForObstacles();
        }

        private void ApplyFloorTransform(RaycastHit hit)
        {
            float snapX = Mathf.Round(hit.point.x / _gridCellSize) * _gridCellSize;
            float snapZ = Mathf.Round(hit.point.z / _gridCellSize) * _gridCellSize;

            _ghostObject.transform.position = new Vector3(
                snapX,
                hit.point.y + _currentItem.pivotOffset.y,
                snapZ);

            _ghostObject.transform.rotation = Quaternion.Euler(0f, _currentYRotation, 0f);
        }

        private void ApplyWallTransform(RaycastHit hit)
        {
            _ghostObject.transform.position = hit.point
                + hit.normal  * _wallSnapDepth
                + Vector3.up  * _currentItem.pivotOffset.y;

            _ghostObject.transform.rotation = Quaternion.LookRotation(-hit.normal)
                * Quaternion.Euler(0f, _currentYRotation, 0f);
        }

        private void CheckForObstacles()
        {
            Vector3 extents = new Vector3(
                _currentItem.gridSize.x * _gridCellSize * 0.5f,
                0.5f,
                _currentItem.gridSize.y * _gridCellSize * 0.5f
            );

            Collider[] hits = Physics.OverlapBox(
                _ghostObject.transform.position + Vector3.up * 0.5f,
                extents,
                _ghostObject.transform.rotation,
                _obstacleLayer
            );

            _canPlace = hits.Length == 0;

            // Duvar için ayrı renk
            bool isWall = _currentItem.placementSurface == PlacementSurface.Wall;
            if (_canPlace)
                SetGhostColor(isWall ? _colorWall : _colorValid);
            else
                SetGhostColor(_colorInvalid);
        }

        // ─── Input ───────────────────────────────────────────────
        private void HandleRotationInput()
        {
            // Build modda R tuşu ghost'u döndürür
            if (!_input.Player.Rotate.WasPressedThisFrame()) return;
            _currentYRotation += 90f;
            if (_currentYRotation >= 360f) _currentYRotation = 0f;
        }

        private void HandlePlacementInput()
        {
            // Sağ tık veya ESC → iptal
            if (_input.Player.SecondaryAction.WasPressedThisFrame())
            {
                StopBuilding();
                return;
            }

            if (!_input.Player.PlaceObject.WasPressedThisFrame()) return;
            if (!_canPlace || !_ghostObject.activeSelf)           return;

            if (EconomyManager.Instance != null &&
                !EconomyManager.Instance.HasEnoughMoney(_currentItem.buyPrice))
            {
                WeBussedUp.UI.UIManager.Instance?.ShowNotification(
                    "Yetersiz bakiye!", Color.red);
                return;
            }

            EconomyManager.Instance?.SpendMoneyServerRpc(
                _currentItem.buyPrice, TransactionCategory.Construction);

            PlaceObjectServerRpc(
                _currentItem.itemID,
                _ghostObject.transform.position,
                _ghostObject.transform.rotation
            );

            // Yerleştirme sonrası aynı item ile devam et
            WeBussedUp.UI.UIManager.Instance?.ShowNotification(
                $"{_currentItem.itemName} yerleştirildi!", Color.green);
        }

        // ─── Network ─────────────────────────────────────────────
        [Rpc(SendTo.Server)]
        private void PlaceObjectServerRpc(string itemID, Vector3 position, Quaternion rotation)
        {
            ItemData itemToBuild = ItemDatabase.Instance?.GetItemByID(itemID);
            if (itemToBuild == null)
            {
                Debug.LogError($"[BuildingSystem] ItemID bulunamadı: {itemID}");
                return;
            }

            if (itemToBuild.prefab.TryGetComponent(out PlacementDependency dep))
            {
                if (!PlacementDependency.CanPlace(
                    position, dep.CheckOffset, dep.CheckDistance, dep.AllowedSurfaceTags))
                {
                    Debug.LogWarning("[BuildingSystem] Server: PlacementDependency karşılanmadı.");
                    return;
                }
            }

            GameObject placed = Instantiate(itemToBuild.prefab, position, rotation);

            if (placed.TryGetComponent(out BuildingItem buildingItem))
                buildingItem.Initialize(itemToBuild);

            if (placed.TryGetComponent(out NetworkObject netObj))
                netObj.Spawn();
        }
    }
}