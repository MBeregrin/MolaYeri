using UnityEngine;
using Unity.Netcode;
using WeBussedUp.Player;

namespace WeBussedUp.Network
{
    /// <summary>
    /// FPS oyuncusunun pozisyon ve rotasyonunu network üzerinden senkronize eder.
    /// NetworkTransform yerine custom sync — daha düşük bant genişliği, interpolasyon kontrolü.
    /// Sadece owner gönderir, diğerleri alır ve interpolate eder.
    /// </summary>
    public class PlayerNetworkSync : NetworkBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("Senkronizasyon Ayarları")]
        [SerializeField] private float _sendRate         = 20f;  // Saniyede kaç paket
        [SerializeField] private float _positionThreshold = 0.01f; // Bu kadar hareket yoksa gönderme
        [SerializeField] private float _rotationThreshold = 0.1f;

        [Header("Interpolasyon")]
        [SerializeField] private float _positionLerpSpeed = 15f;
        [SerializeField] private float _rotationLerpSpeed = 15f;

        [Header("Referanslar")]
        [SerializeField] private Transform _cameraHolder; // Kamera rotasyonu (pitch)

        // ─── Network State ───────────────────────────────────────
        private NetworkVariable<Vector3> _networkPosition = new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        private NetworkVariable<float> _networkYaw = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        private NetworkVariable<float> _networkPitch = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        private NetworkVariable<bool> _networkIsMoving = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        private NetworkVariable<bool> _networkIsSprinting = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        // ─── Runtime ─────────────────────────────────────────────
        private float     _sendTimer;
        private float     _sendInterval;

        private Vector3   _lastSentPosition;
        private float     _lastSentYaw;
        private float     _lastSentPitch;

        // Remote interpolasyon hedefleri
        private Vector3   _targetPosition;
        private float     _targetYaw;
        private float     _targetPitch;

        // ─── NetworkBehaviour ────────────────────────────────────
        public override void OnNetworkSpawn()
        {
            _sendInterval = 1f / _sendRate;

            if (IsOwner)
            {
                // Owner: kamera referansını otomatik bul
                if (_cameraHolder == null)
                {
                    Camera cam = GetComponentInChildren<Camera>();
                    if (cam != null) _cameraHolder = cam.transform.parent;
                }

                // Başlangıç değerlerini yaz
                _networkPosition.Value  = transform.position;
                _networkYaw.Value       = transform.eulerAngles.y;
                _networkPitch.Value     = _cameraHolder != null
                    ? _cameraHolder.localEulerAngles.x : 0f;

                _lastSentPosition = transform.position;
                _lastSentYaw      = transform.eulerAngles.y;
                _lastSentPitch    = _networkPitch.Value;
            }
            else
            {
                // Remote: network değerlerini hedef olarak al
                _targetPosition = _networkPosition.Value;
                _targetYaw      = _networkYaw.Value;
                _targetPitch    = _networkPitch.Value;

                _networkPosition.OnValueChanged  += OnPositionChanged;
                _networkYaw.OnValueChanged       += OnYawChanged;
                _networkPitch.OnValueChanged     += OnPitchChanged;
                _networkIsMoving.OnValueChanged  += OnMovingChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner)
            {
                _networkPosition.OnValueChanged  -= OnPositionChanged;
                _networkYaw.OnValueChanged       -= OnYawChanged;
                _networkPitch.OnValueChanged     -= OnPitchChanged;
                _networkIsMoving.OnValueChanged  -= OnMovingChanged;
            }
        }

        // ─── Update ──────────────────────────────────────────────
        private void Update()
        {
            if (IsOwner)
                HandleOwnerUpdate();
            else
                HandleRemoteUpdate();
        }

        // ─── Owner: Gönder ───────────────────────────────────────
        private void HandleOwnerUpdate()
        {
            _sendTimer += Time.deltaTime;
            if (_sendTimer < _sendInterval) return;
            _sendTimer = 0f;

            Vector3 currentPos   = transform.position;
            float   currentYaw   = transform.eulerAngles.y;
            float   currentPitch = _cameraHolder != null
                ? _cameraHolder.localEulerAngles.x : 0f;

            bool posChanged = Vector3.Distance(currentPos, _lastSentPosition) > _positionThreshold;
            bool yawChanged = Mathf.Abs(Mathf.DeltaAngle(currentYaw, _lastSentYaw)) > _rotationThreshold;
            bool pitchChanged = Mathf.Abs(Mathf.DeltaAngle(currentPitch, _lastSentPitch)) > _rotationThreshold;

            if (posChanged)
            {
                _networkPosition.Value = currentPos;
                _lastSentPosition      = currentPos;
            }

            if (yawChanged)
            {
                _networkYaw.Value = currentYaw;
                _lastSentYaw      = currentYaw;
            }

            if (pitchChanged)
            {
                _networkPitch.Value = currentPitch;
                _lastSentPitch      = currentPitch;
            }

            // Hareket durumu
            bool isMoving = posChanged;
            if (_networkIsMoving.Value != isMoving)
                _networkIsMoving.Value = isMoving;
        }

        // ─── Remote: Interpolate ─────────────────────────────────
        private void HandleRemoteUpdate()
        {
            // Pozisyon interpolasyonu
            transform.position = Vector3.Lerp(
                transform.position,
                _targetPosition,
                Time.deltaTime * _positionLerpSpeed
            );

            // Yaw (body rotasyonu)
            float currentYaw = transform.eulerAngles.y;
            float newYaw     = Mathf.LerpAngle(currentYaw, _targetYaw,
                Time.deltaTime * _rotationLerpSpeed);
            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);

            // Pitch (kamera rotasyonu)
            if (_cameraHolder != null)
            {
                float currentPitch = _cameraHolder.localEulerAngles.x;
                float newPitch     = Mathf.LerpAngle(currentPitch, _targetPitch,
                    Time.deltaTime * _rotationLerpSpeed);
                _cameraHolder.localRotation = Quaternion.Euler(newPitch, 0f, 0f);
            }
        }

        // ─── Network Callbacks ────────────────────────────────────
        private void OnPositionChanged(Vector3 oldVal, Vector3 newVal)
        {
            _targetPosition = newVal;
        }

        private void OnYawChanged(float oldVal, float newVal)
        {
            _targetYaw = newVal;
        }

        private void OnPitchChanged(float oldVal, float newVal)
        {
            _targetPitch = newVal;
        }

        private void OnMovingChanged(bool oldVal, bool newVal)
        {
            // Animator güncelleme — PlayerController'daki animator ile koordineli çalışır
            // Remote oyuncunun animasyonu için kullanılır
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            // Remote hedef pozisyonu göster
            if (!IsOwner)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(_targetPosition, 0.2f);
                Gizmos.DrawLine(transform.position, _targetPosition);
            }
        }
#endif
    }
}