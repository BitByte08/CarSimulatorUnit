using UnityEngine;
using CarSim.Vehicle;

namespace CarSim.ADAS
{
    /// <summary>
    /// ESC (Electronic Stability Control)
    /// - 오버스티어: 바깥 앞바퀴 제동
    /// - 언더스티어: 안쪽 뒷바퀴 제동
    /// - 요레이트(Yaw Rate) 기반 판정
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class ESC : MonoBehaviour
    {
        [Header("ESC 설정")]
        [SerializeField] bool  escEnabled         = true;
        [SerializeField] float yawRateThreshold   = 0.15f;  // rad/s 편차 한계
        [SerializeField] float correctionTorque   = 1500f;  // 보정 제동 토크 (Nm)
        [SerializeField] float maxEscBrakeTorque  = 1200f;  // ESC 단일 휠 최대 추가 제동 (Nm)
        [SerializeField] float minSpeedKph        = 15f;    // ESC 작동 최소 속도
        [SerializeField] float minSteerDegForEsc  = 2f;     // 미세 조향 노이즈에서는 ESC 비개입
        [SerializeField] float minLateralSpeedMs  = 0.6f;   // 횡이동이 거의 없으면 ESC 비개입
        [SerializeField] float maxEscSpeedKph     = 160f;
        [SerializeField] float escThrottleBypass  = 0.85f;

        public bool  IsActive      { get; private set; }
        public float YawError      { get; private set; }

        VehicleController _vc;
        Rigidbody         _rb;
        WheelCollider[]   _wheels;
        Engine            _engine;

        void Awake()
        {
            _vc     = GetComponent<VehicleController>();
            _rb     = GetComponent<Rigidbody>();
            _wheels = _vc.GetAllWheels();
            _engine = GetComponent<Engine>();
        }

        void FixedUpdate()
        {
            IsActive = false;
            if (!escEnabled || _vc.SpeedKph < minSpeedKph || _vc.SpeedKph > maxEscSpeedKph) return;
            if (_engine != null && _engine.ThrottleInput > escThrottleBypass) return;

            // 실제 요레이트 (rad/s)
            float actualYawRate = _rb.angularVelocity.y;

            // 기대 요레이트: 스티어링 각도 × 속도 / 휠베이스(근사)
            float steerDeg     = _wheels[0].steerAngle;
            float steerRad     = steerDeg * Mathf.Deg2Rad;
            float wheelbase    = 2.7f; // m, 나중에 실측값으로 교체
            float expectedYaw  = (_vc.SpeedMs * Mathf.Tan(steerRad)) / wheelbase;

            float lateralSpeed = Mathf.Abs(Vector3.Dot(_rb.linearVelocity, _vc.transform.right));
            if (Mathf.Abs(steerDeg) < minSteerDegForEsc || lateralSpeed < minLateralSpeedMs)
                return;

            YawError = actualYawRate - expectedYaw;

            if (Mathf.Abs(YawError) < yawRateThreshold) return;

            IsActive = true;
            float error = Mathf.Abs(YawError);
            float errorGain = Mathf.InverseLerp(yawRateThreshold, yawRateThreshold * 4f, error);
            float speedGain = Mathf.InverseLerp(minSpeedKph, 120f, _vc.SpeedKph);
            float escBrake = correctionTorque * errorGain * speedGain;
            escBrake = Mathf.Min(escBrake, maxEscBrakeTorque);

            bool turningRight = steerDeg > 0f;

            if (YawError > 0f)
            {
                // 오버스티어: 바깥 앞바퀴 제동
                int outerFront = turningRight ? 0 : 1; // 우회전=FL, 좌회전=FR
                _wheels[outerFront].brakeTorque = Mathf.Min(_wheels[outerFront].brakeTorque + escBrake, maxEscBrakeTorque);
            }
            else
            {
                // 언더스티어: 안쪽 뒷바퀴 제동
                int innerRear = turningRight ? 3 : 2; // 우회전=RR, 좌회전=RL
                _wheels[innerRear].brakeTorque = Mathf.Min(_wheels[innerRear].brakeTorque + escBrake, maxEscBrakeTorque);
            }
        }
    }
}
