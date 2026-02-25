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
        [SerializeField] float slipThreshold   = 0.12f;  // 구동 슬립 한계
        [SerializeField] float throttleCutRate = 8f;     // 출력 복구 속도

        public bool  IsActive        { get; private set; }
        public float ThrottleLimit   { get; private set; } = 1f;

        VehicleController _vc;
        Engine            _engine;
        WheelCollider[]   _wheels;

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

            // 비구동 휠 평균 속도 = 차체 기준 속도
            float refSpeedFL = Mathf.Abs(_wheels[0].rpm) * _wheels[0].radius * Mathf.PI / 30f;
            float refSpeedFR = Mathf.Abs(_wheels[1].rpm) * _wheels[1].radius * Mathf.PI / 30f;
            float refSpeed   = (refSpeedFL + refSpeedFR) * 0.5f; // FWD면 뒤로 바꿔야 함

            // 구동 휠 슬립 검사 (RWD 기준: 뒷바퀴)
            float driveSpeedRL = Mathf.Abs(_wheels[2].rpm) * _wheels[2].radius * Mathf.PI / 30f;
            float driveSpeedRR = Mathf.Abs(_wheels[3].rpm) * _wheels[3].radius * Mathf.PI / 30f;
            float driveSpeed   = (driveSpeedRL + driveSpeedRR) * 0.5f;

            float slip = refSpeed > 0.5f ? (driveSpeed - refSpeed) / refSpeed : 0f;

            if (slip > slipThreshold)
            {
                IsActive      = true;
                ThrottleLimit = Mathf.MoveTowards(ThrottleLimit, 0f,
                                                  Time.fixedDeltaTime * throttleCutRate);
            }
            else
            {
                IsActive      = slip > slipThreshold * 0.5f;
                ThrottleLimit = Mathf.MoveTowards(ThrottleLimit, 1f,
                                                  Time.fixedDeltaTime * throttleCutRate * 0.5f);
            }

            // 엔진 스로틀 제한
            _engine.ThrottleInput *= ThrottleLimit;
        }
    }
}
