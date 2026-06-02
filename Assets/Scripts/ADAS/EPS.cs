using UnityEngine;
using CarSim.Vehicle;
using CarSim.CAN;

namespace CarSim.ADAS
{
    /// <summary>
    /// EPS (Electric Power Steering)
    /// - 프런트 타이어 횡슬립 기반 자기정렬토크(SAT)로 핸들 복구력 계산
    /// - 노면 진동 럼블 추가
    /// - CAN으로 OpenFFBoard에 FFB 명령 송신
    /// - 복구토크(ReturnTorque)는 시뮬레이션(키보드) 조향에서도 사용 가능하도록 공개
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class EPS : MonoBehaviour
    {
        [Header("FFB 한계")]
        [SerializeField] float maxFeedbackTorque = 1.0f;

        [Header("핸들 복구력 (타이어 자기정렬토크 기반)")]
        [Tooltip("타이어 SAT → FFB 변환 게인 (복구력 전체 세기)")]
        [SerializeField] float satGain = 1.3f;
        [Tooltip("뉴매틱 트레일이 유지되는 횡슬립 한계 (WheelCollider sidewaysFriction extremumSlip ≈ 0.12). 부근에서 복구력 최대, 더 미끄러지면 가벼워짐")]
        [SerializeField] float pneumaticPeakSlip = 0.12f;
        [Tooltip("기계적 트레일(캐스터) 비중 — 슬립과 무관하게 조향각에 비례하는 복귀력")]
        [SerializeField] float mechanicalTrail = 0.35f;
        [Tooltip("조향 속도 감쇠 (오버슈트/떨림 억제)")]
        [SerializeField] float steeringDamping = 0.12f;
        [Tooltip("복구력이 완전히 살아나는 기준 속도(km/h). 정차 시 0, 저속(이 속도)에서 이미 충분히 정렬")]
        [SerializeField] float returnSpeedFull = 8f;

        [Header("도로 진동 (FFB 럼블)")]
        [Tooltip("도로 진동 최대 세기 (정규화). 0이면 비활성")]
        [SerializeField] float roadVibrationAmp  = 0.04f;
        [Tooltip("진동 주파수 (Hz 느낌). 클수록 거친 노면")]
        [SerializeField] float roadVibrationFreq = 28f;
        [Tooltip("이 속도(km/h) 이상에서만 진동")]
        [SerializeField] float roadVibrationMinSpeed = 3f;

        [Header("테스트 모드")]
        [Tooltip("0이면 정상 동작, 양수면 오른쪽으로, 음수면 왼쪽으로 고정 토크 계속 전송")]
        [SerializeField] float testForceTorque = 0f;

        /// <summary>정규화된 복구토크(-1~1, 센터 방향). 진동/엔드스탑 제외. 시뮬 조향이 사용.</summary>
        public float ReturnTorque { get; private set; }

        VehicleController _vc;
        SteeringHandler   _steering;

        float _logTimer;
        float _prevSteerAngle;

        void Awake()
        {
            _vc       = GetComponent<VehicleController>();
            _steering = FindObjectOfType<SteeringHandler>();
            Debug.Log($"[EPS] Awake: _steering={(_steering != null ? "found" : "NULL")}, _vc={(_vc != null ? "found" : "NULL")}");
        }

        void FixedUpdate()
        {
            if (_steering == null)
            {
                Debug.LogWarning("[EPS] _steering is NULL!");
                return;
            }

            float steerAngle = _steering.SteeringAngle;
            float steerNorm  = steerAngle / 450f;
            float speedScale = Mathf.Clamp01(_vc.SpeedKph / Mathf.Max(returnSpeedFull, 1f));

            // 타이어 자기정렬토크 크기 (프런트 횡슬립 기반, 한계에서 붕괴)
            float satMag = ComputeSelfAligningMagnitude(speedScale);

            // 방향은 안전상 조향각에 고정(항상 센터로). 세기는 SAT/캐스터로 변조.
            float sat    = -Mathf.Sign(steerNorm) * satMag * satGain;
            float caster = -steerNorm * mechanicalTrail * speedScale;

            // 조향 속도 감쇠
            float steerRate = (steerAngle - _prevSteerAngle) / Mathf.Max(Time.fixedDeltaTime, 1e-4f);
            _prevSteerAngle = steerAngle;
            float damping = -Mathf.Clamp(steerRate / 600f, -1f, 1f) * steeringDamping;

            ReturnTorque = Mathf.Clamp(sat + caster + damping, -maxFeedbackTorque, maxFeedbackTorque);

            float totalFFB = ReturnTorque;

            // 엔드스탑: 450° 초과 즉시 최대 반력
            if (Mathf.Abs(steerAngle) > 450f)
                totalFFB = -Mathf.Sign(steerAngle) * maxFeedbackTorque;

            // 도로 진동
            if (roadVibrationAmp > 0f && _vc.SpeedKph >= roadVibrationMinSpeed)
            {
                float noise = Mathf.PerlinNoise(Time.time * roadVibrationFreq, 0.37f) - 0.5f;
                totalFFB += noise * 2f * roadVibrationAmp * speedScale;
                totalFFB  = Mathf.Clamp(totalFFB, -maxFeedbackTorque, maxFeedbackTorque);
            }

            float sendTorque = testForceTorque != 0f ? testForceTorque : totalFFB;
            _steering.SendFFBTorque(sendTorque);

            _logTimer -= Time.fixedDeltaTime;
            if (_logTimer <= 0f)
            {
                _logTimer = 0.5f;
                Debug.Log($"[EPS] steer={steerAngle:F1}° satMag={satMag:F2} return={ReturnTorque:F3} send={sendTorque:F3}");
            }
        }

        // 프런트 타이어 횡슬립 → 자기정렬토크 크기(0~1). peak 부근 최대, 초과 시 붕괴(가벼워짐).
        float ComputeSelfAligningMagnitude(float speedScale)
        {
            var wheels = _vc.GetAllWheels();
            if (wheels == null || wheels.Length < 2) return 0f;

            float slip = 0f; int n = 0;
            if (wheels[0] != null && wheels[0].GetGroundHit(out WheelHit h0)) { slip += h0.sidewaysSlip; n++; }
            if (wheels[1] != null && wheels[1].GetGroundHit(out WheelHit h1)) { slip += h1.sidewaysSlip; n++; }
            if (n == 0) return 0f;

            float slipMag = Mathf.Abs(slip / n);
            float peak    = Mathf.Max(pneumaticPeakSlip, 0.01f);

            // 횡력: 슬립이 peak까지 오르며 증가 (정규화 0~1)
            float lateralForce = Mathf.Clamp01(slipMag / peak);
            // 뉴매틱 트레일: 슬립 작을수록 큼, peak의 2배 초과 시 0 → 한계에서 복구력 붕괴
            float pneuTrail = Mathf.Clamp01(1f - slipMag / (peak * 2f));

            return lateralForce * (0.4f + 0.6f * pneuTrail) * speedScale;
        }
    }
}
