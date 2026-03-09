using UnityEngine;
using CarSim.Vehicle;

namespace CarSim.ADAS
{
    /// <summary>
    /// TCS (Traction Control System)
    /// - 구동휠 과슬립 시 엔진 출력 제한
    /// - 실제 TCS: 각 휠 속도 센서 비교
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    [RequireComponent(typeof(Engine))]
    public class TCS : MonoBehaviour
    {
        [Header("TCS 설정")]
        [SerializeField] bool  tcsEnabled      = true;
        [SerializeField] float slipThreshold   = 0.14f;
        [SerializeField] float throttleCutRate = 10f;
        [SerializeField] float throttleRecoverRate = 9f;
        [SerializeField] float minReferenceSpeedMs = 2f; // 저속에서는 슬립 판정 비활성
        [SerializeField] float minThrottleLimit = 0.65f;
        [SerializeField] float maxTcsSpeedKph = 90f;
        [SerializeField] float slipFilterRate = 7f;

        public bool  IsActive        { get; private set; }
        public float ThrottleLimit   { get; private set; } = 1f;

        VehicleController _vc;
        Engine            _engine;
        WheelCollider[]   _wheels;
        float             _filteredSlip;

        void Awake()
        {
            _vc     = GetComponent<VehicleController>();
            _engine = GetComponent<Engine>();
            _wheels = _vc.GetAllWheels();
        }

        void FixedUpdate()
        {
            if (!tcsEnabled)
            {
                ThrottleLimit = 1f;
                IsActive = false;
                return;
            }

            if (_vc.SpeedKph >= maxTcsSpeedKph)
            {
                ThrottleLimit = Mathf.MoveTowards(ThrottleLimit, 1f,
                                                  Time.fixedDeltaTime * throttleRecoverRate);
                IsActive = false;
                _engine.ThrottleInput *= ThrottleLimit;
                return;
            }

            float refSpeed = Mathf.Max(_vc.ForwardSpeedMs, minReferenceSpeedMs);
            float driveSpeed = GetDriveWheelSpeedMs();
            float slip = (driveSpeed - refSpeed) / refSpeed;
            _filteredSlip = Mathf.Lerp(_filteredSlip, slip, Time.fixedDeltaTime * slipFilterRate);

            if (_filteredSlip > slipThreshold)
            {
                IsActive      = true;
                ThrottleLimit = Mathf.MoveTowards(ThrottleLimit, minThrottleLimit,
                                                  Time.fixedDeltaTime * throttleCutRate);
            }
            else
            {
                IsActive      = _filteredSlip > slipThreshold * 0.65f;
                ThrottleLimit = Mathf.MoveTowards(ThrottleLimit, 1f,
                                                  Time.fixedDeltaTime * throttleRecoverRate);
            }

            // 엔진 스로틀 제한
            _engine.ThrottleInput *= ThrottleLimit;
        }

        float GetDriveWheelSpeedMs()
        {
            float WheelSpeedMs(WheelCollider w) => Mathf.Abs(w.rpm) * w.radius * Mathf.PI / 30f;

            switch (_vc.CurrentDriveType)
            {
                case VehicleController.DriveType.FWD:
                    return (WheelSpeedMs(_wheels[0]) + WheelSpeedMs(_wheels[1])) * 0.5f;
                case VehicleController.DriveType.AWD:
                    return (WheelSpeedMs(_wheels[0]) + WheelSpeedMs(_wheels[1]) +
                            WheelSpeedMs(_wheels[2]) + WheelSpeedMs(_wheels[3])) * 0.25f;
                case VehicleController.DriveType.RWD:
                default:
                    return (WheelSpeedMs(_wheels[2]) + WheelSpeedMs(_wheels[3])) * 0.5f;
            }
        }
    }
}
