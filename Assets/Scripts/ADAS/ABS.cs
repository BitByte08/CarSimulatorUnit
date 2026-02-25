using UnityEngine;
using CarSim.Vehicle;

namespace CarSim.ADAS
{
    /// <summary>
    /// ABS (Anti-lock Braking System)
    /// - 휠 슬립 감지 → 브레이크 압력 펄스 변조
    /// - 실제 ABS: 10~20Hz 펄스 빈도
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class ABS : MonoBehaviour
    {
        [Header("ABS 설정")]
        [SerializeField] bool   absEnabled     = true;
        [SerializeField] float  lockSlipRatio  = 0.25f;  // 잠김 감지: 기대RPM 대비 25% 이상 느리면 잠김
        [SerializeField] float  releaseRatio   = 0.10f;  // 회복 감지: 10% 이하면 재적용
        [SerializeField] float  maxBrakeTorque = 2500f;
        [SerializeField] float  minSpeedKph    = 5f;     // 저속에선 ABS 비작동

        public bool IsActive { get; private set; }

        VehicleController _vc;
        WheelCollider[]   _wheels;
        bool[]            _wheelReleased;  // 휠별 압력 해제 상태 (히스테리시스)

        void Awake()
        {
            _vc            = GetComponent<VehicleController>();
            _wheels        = _vc.GetAllWheels();
            _wheelReleased = new bool[_wheels.Length];
        }

        void FixedUpdate()
        {
            if (!absEnabled)
            {
                ApplyBrakeNormal();
                return;
            }

            IsActive = false;
            float brakeInput  = _vc.BrakeInput;
            float vehicleKph  = _vc.SpeedKph;

            // 저속 / 브레이크 미입력 시 ABS 비작동 → 그냥 전체 적용
            if (vehicleKph < minSpeedKph || brakeInput < 0.05f)
            {
                ApplyBrakeNormal();
                System.Array.Clear(_wheelReleased, 0, _wheelReleased.Length);
                return;
            }

            float vehicleSpeedMs = _vc.SpeedMs;

            for (int i = 0; i < _wheels.Length; i++)
            {
                float wheelRadius       = _wheels[i].radius;
                float wheelCircumference = 2f * Mathf.PI * wheelRadius;
                float expectedRpm       = (vehicleSpeedMs / wheelCircumference) * 60f;
                float actualRpm         = Mathf.Abs(_wheels[i].rpm);

                // 슬립률: (기대 - 실제) / 기대  (0=미끄럼없음, 1=완전잠김)
                float slip = expectedRpm > 1f
                    ? Mathf.Clamp01((expectedRpm - actualRpm) / expectedRpm)
                    : 0f;

                // 히스테리시스: 잠김(lockSlipRatio↑) → 해제, 회복(releaseRatio↓) → 재적용
                if (slip > lockSlipRatio)
                    _wheelReleased[i] = true;
                else if (slip < releaseRatio)
                    _wheelReleased[i] = false;

                float brakeTorque;
                if (_wheelReleased[i])
                {
                    IsActive    = true;
                    brakeTorque = 0f;
                }
                else
                {
                    brakeTorque = brakeInput * maxBrakeTorque;
                }

                // 핸드브레이크는 뒷바퀴만 (ABS 비개입)
                if (i >= 2 && _vc.HandbrakeOn)
                    brakeTorque += 5000f;

                _wheels[i].brakeTorque = brakeTorque;
            }
        }

        void ApplyBrakeNormal()
        {
            float t = _vc.BrakeInput * maxBrakeTorque;
            foreach (var w in _wheels) w.brakeTorque = t;
        }
    }
}
