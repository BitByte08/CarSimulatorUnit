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
        [SerializeField] float  lockThreshold  = 0.7f;   // 잠김 판정 임계값: 기대RPM의 70% 이하 = 잠김
        [SerializeField] float  releaseThreshold = 0.85f; // 해제 판정 임계값: 기대RPM의 85% 이상 = 정상
        [SerializeField] float  minSpeedKph    = 3f;     // 저속에선 ABS 비작동
        [SerializeField] float  pulseFrequency = 10f;    // ABS 펄스 빈도 (Hz)

        public bool IsActive { get; private set; }

        VehicleController _vc;
        WheelCollider[]   _wheels;
        bool[]            _wheelLocked;  // 휠별 잠금 상태
        float             _pulseCycle;   // 펄스 사이클 (0~1)

        void Awake()
        {
            _vc            = GetComponent<VehicleController>();
            _wheels        = _vc.GetAllWheels();
            _wheelLocked   = new bool[_wheels.Length];
            _pulseCycle    = 0f;
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

            // 저속 / 브레이크 미입력 시 ABS 비작동
            if (vehicleKph < minSpeedKph || brakeInput < 0.05f)
            {
                ApplyBrakeNormal();
                System.Array.Clear(_wheelLocked, 0, _wheelLocked.Length);
                _pulseCycle = 0f;
                return;
            }

            // 펄스 주기 업데이트 (0~1)
            _pulseCycle += Time.fixedDeltaTime * pulseFrequency;
            if (_pulseCycle > 1f) _pulseCycle -= 1f;
            bool pulseOn = _pulseCycle > 0.5f;  // 50% 듀티 사이클

            float vehicleSpeedMs = _vc.ForwardSpeedMs;
            float maxBrake = _vc.MaxBrakeTorque;

            for (int i = 0; i < _wheels.Length; i++)
            {
                float wheelRadius       = _wheels[i].radius;
                float wheelCircumference = 2f * Mathf.PI * wheelRadius;
                float expectedRpm       = (vehicleSpeedMs / wheelCircumference) * 60f;
                float actualRpm         = Mathf.Abs(_wheels[i].rpm);

                // 휠 속도 비율 (0~1)
                float speedRatio = expectedRpm > 1f ? actualRpm / expectedRpm : 1f;

                // 휠 잠김 판정 (히스테리시스)
                if (speedRatio < lockThreshold)
                    _wheelLocked[i] = true;
                else if (speedRatio > releaseThreshold)
                    _wheelLocked[i] = false;

                float brakeTorque;
                if (_wheelLocked[i])
                {
                    IsActive = true;
                    // ABS 펄스: on일 때만 30% 압력, off일 때 0
                    brakeTorque = (pulseOn ? 0.3f : 0f) * brakeInput * maxBrake;
                }
                else
                {
                    // 정상 제동
                    brakeTorque = brakeInput * maxBrake;
                }

                // 핸드브레이크는 뒷바퀴만 (ABS 비개입)
                if (i >= 2 && _vc.HandbrakeOn)
                    brakeTorque += 5000f;

                _wheels[i].brakeTorque = brakeTorque;
            }
        }

        void ApplyBrakeNormal()
        {
            float t = _vc.BrakeInput * _vc.MaxBrakeTorque;
            foreach (var w in _wheels) w.brakeTorque = t;
        }
    }
}
