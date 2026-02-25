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
        [SerializeField] float minSpeedKph        = 15f;    // ESC 작동 최소 속도

        public bool  IsActive      { get; private set; }
        public float YawError      { get; private set; }

        VehicleController _vc;
        Rigidbody         _rb;
        WheelCollider[]   _wheels;

        float _prevYaw;

        void Awake()
        {
            _vc     = GetComponent<VehicleController>();
            _rb     = GetComponent<Rigidbody>();
            _wheels = _vc.GetAllWheels();
        }

        void FixedUpdate()
        {
            IsActive = false;
            if (!escEnabled || _vc.SpeedKph < minSpeedKph) return;

            // 실제 요레이트 (rad/s)
            float actualYawRate = _rb.angularVelocity.y;

            // 기대 요레이트: 스티어링 각도 × 속도 / 휠베이스(근사)
            float steerRad     = _wheels[0].steerAngle * Mathf.Deg2Rad;
            float wheelbase    = 2.7f; // m, 나중에 실측값으로 교체
            float expectedYaw  = (_vc.SpeedMs * Mathf.Tan(steerRad)) / wheelbase;

            YawError = actualYawRate - expectedYaw;

            if (Mathf.Abs(YawError) < yawRateThreshold) return;

            IsActive = true;

            if (YawError > 0f)
            {
                // 오버스티어 (차가 안쪽으로 파고듦): 바깥 앞바퀴(FR) 제동
                _wheels[1].brakeTorque += correctionTorque * Mathf.Abs(YawError);
            }
            else
            {
                // 언더스티어 (차가 밀림): 안쪽 뒷바퀴(RL) 제동
                _wheels[2].brakeTorque += correctionTorque * Mathf.Abs(YawError);
            }
        }
    }
}
