using UnityEngine;
using WeBussedUp.Core.Data;

namespace WeBussedUp.Gameplay.Building
{
    /// <summary>
    /// Yerleştirilmiş eşyanın runtime building davranışı.
    /// Boşluk/gap hesabı ItemData.SO'dan okunur — magic number yok.
    /// BuildingSystem spawn ettikten sonra bu bileşeni configure eder.
    /// </summary>
    public class BuildingItem : MonoBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Eşya Verisi")]
        [SerializeField] private ItemData _itemData;

        [Header("Runtime Override")]
        [Tooltip("SO değerini geçersiz kılmak istersen işaretle")]
        [SerializeField] private bool  _overrideGap = false;
        [SerializeField, Range(0f, 1f)] private float _customGap = 0.75f;

        // ─── Public API ──────────────────────────────────────────
        public ItemData ItemData => _itemData;

        /// <summary>
        /// Grid üzerinde komşu eşyalar arası boşluk katsayısı.
        /// 0 = yapışık, 1 = tam hücre boşluğu
        /// </summary>
        public float GapMultiplier
        {
            get
            {
                if (_overrideGap) return _customGap;
                if (_itemData    != null) return _itemData.gapMultiplier;
                return 0.75f; // fallback
            }
        }

        /// <summary>
        /// BuildingSystem tarafından spawn sonrası çağrılır.
        /// </summary>
        public void Initialize(ItemData data)
        {
            _itemData = data;
        }

        /// <summary>
        /// Bu eşyanın grid'de kapladığı world-space boyutu.
        /// BuildingSystem.CheckForObstacles ile tutarlı.
        /// </summary>
        public Vector3 GetWorldSize(float gridCellSize)
        {
            if (_itemData == null) return Vector3.one * gridCellSize;

            return new Vector3(
                _itemData.gridSize.x * gridCellSize,
                1f,
                _itemData.gridSize.y * gridCellSize
            );
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_itemData == null) return;

            // Grid sınırını editörde göster
            Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
            Gizmos.DrawWireCube(
                transform.position + Vector3.up * 0.5f,
                GetWorldSize(1f)
            );
        }
#endif
    }
}