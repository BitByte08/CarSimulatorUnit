using UnityEngine;
using UnityEngine.InputSystem;
using CarSim.Vehicle;

namespace CarSim.Camera
{
    /// <summary>
    /// 1인칭 드라이버 카메라
    /// - 헤드 물리: G력에 반응해 카메라가 움직임
    /// - 진동: 노면 충격, RPM 진동
    /// </summary>
    public class DriverCamera : MonoBehaviour
    {
        [Header("마운트 위치 (드라이버 눈 위치)")]
        [SerializeField] Transform headMount;  // 차량 내부 빈 오브젝트

        [Header("헤드 물리 (G력 반응)")]
        [SerializeField] float lateralSensitivity      = 0.04f;  // 횡G 반응
        [SerializeField] float longitudinalSensitivity = 0.06f;  // 종G 반응
        [SerializeField] float verticalSensitivity     = 0.02f;  // 수직G 반응
        [SerializeField] float headSmoothing           = 8f;     // 복귀 속도

        [Header("RPM 진동")]
        [SerializeField] float idleVibrAmp  = 0.002f;
        [SerializeField] float idleVibrFreq = 25f;

        [Header("마우스 시점 조작")]
        [SerializeField] float mouseSensitivity = 2f;
        [SerializeField] float maxPitchUp       = 30f;
        [SerializeField] float maxPitchDown     = 20f;
        [SerializeField] float maxYaw           = 120f;

        VehicleController _vc;
        Engine            _engine;
        Rigidbody         _rb;
        UnityEngine.Camera _cam;

        Vector3 _baseLocalPos;     // Awake 시점의 로컬 기준 위치
        Vector3 _headOffset;
        float   _pitch, _yaw;

        void Awake()
        {
            _cam    = GetComponent<UnityEngine.Camera>();
            _vc     = GetComponentInParent<VehicleController>();
            _engine = GetComponentInParent<Engine>();
            _rb     = GetComponentInParent<Rigidbody>();

            // headMount 미설정 시 자신의 부모를 폴백으로 사용
            if (headMount == null)
                headMount = transform.parent;

            // 로컬 기준 위치 저장 (이것으로부터 오프셋 적용)
            _baseLocalPos = transform.localPosition;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        void LateUpdate()
        {
            // ── 마우스 시점 ───────────────────────────
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw   +=  delta.x * mouseSensitivity * 0.1f;
                _pitch -=  delta.y * mouseSensitivity * 0.1f;
                _yaw    = Mathf.Clamp(_yaw,   -maxYaw,       maxYaw);
                _pitch  = Mathf.Clamp(_pitch, -maxPitchDown,  maxPitchUp);
            }

            // ── G력 헤드 움직임 ────────────────────────
            if (_rb != null && _vc != null)
            {
                float latG  = _vc.LateralG;
                float lonG  = _vc.LongitudinalG;

                Vector3 targetOffset = new Vector3(
                    -latG  * lateralSensitivity,
                    -Mathf.Abs(lonG) * verticalSensitivity,
                    -lonG  * longitudinalSensitivity
                );

                // RPM 진동 추가
                if (_engine != null && _engine.IsRunning)
                {
                    float rpmNorm = _engine.RPM / 7200f;
                    float vibr    = Mathf.Sin(Time.time * idleVibrFreq * Mathf.PI * 2f)
                                    * idleVibrAmp * (0.3f + rpmNorm * 0.7f);
                    targetOffset.y += vibr;
                }

                _headOffset = Vector3.Lerp(_headOffset, targetOffset,
                                           Time.deltaTime * headSmoothing);
            }

            // ── 카메라 위치/회전 적용 (로컈 코오디네이트 — Rigidbody 물리와 충돌 없음) ──
            transform.localPosition = _baseLocalPos + _headOffset;
            transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void Update()
        {
            // ESC: 커서 토글
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible   = locked;
            }
        }
    }
}
