using UnityEngine;
using CarSim.Vehicle;
using CarSim.CAN;

namespace CarSim.ADAS
{
    /// <summary>
    /// EPS (Electric Power Steering)
    /// - 속도 감응형 조향력 지원
    /// - 노면 피드백 토크 계산 → CAN으로 OpenFFBoard에 FFB 명령 송신
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class EPS : MonoBehaviour
    {
        [Header("EPS 설정")]
        [SerializeField] float maxFeedbackTorque = 1.0f;   // FFB 최대 토크 (정규화)
        [SerializeField] float returnTorque      = 1.0f;   // 센터 복귀 토크 비율 (올림)
        [SerializeField] float highSpeedDamping  = 0.3f;   // 고속 감쇠
        [SerializeField] float lateralGSensitivity = 0.8f;
        [Tooltip("최소 FFB 토크 (정규화). 핸들이 치우쳐 있을 때 최소한 이만큼 복귀력 적용")]
        [SerializeField] float minFFBTorque = 0.7f;  // 70% 최소 (저속 센터링 보장)
        [Tooltip("엔드스탑 저항 강도. 최대 조향각 초과 시 반력 계수")]
        [SerializeField] float hardStopStiffness = 3.0f;
        
        [Header("테스트 모드")]
        [Tooltip("0이면 정상 동작, 양수면 오른쪽으로, 음수면 왼쪽으로 고정 토크 계속 전송")]
        [SerializeField] float testForceTorque = 0f;

        VehicleController _vc;
        SteeringHandler   _steering;
        Rigidbody         _rb;

        void Awake()
        {
            _vc       = GetComponent<VehicleController>();
            _rb       = GetComponent<Rigidbody>();
            _steering = FindObjectOfType<SteeringHandler>();
            Debug.Log($"[EPS] Awake: _steering={(_steering != null ? "found" : "NULL")}, _vc={(_vc != null ? "found" : "NULL")}");
        }

        float _logTimer;

        void FixedUpdate()
        {
            if (_steering == null)
            {
                Debug.LogWarning("[EPS] _steering is NULL!");
                return;
            }

            float speedNorm   = Mathf.Clamp01(_vc.SpeedKph / 120f);
            float lateralG    = _vc.LateralG;
            float steerAngle  = _steering.SteeringAngle;
            float steerNorm   = steerAngle / 450f; // -1 ~ 1

            float totalFFB = 0f;

            // 5 km/h 미만 = 센터링 없음 (정차 시 완전 비활성)
            if (_vc.SpeedKph >= 5f)
            {
                // 속도 비율: 5 km/h=0%, 30 km/h=100%
                float speedScale  = Mathf.Clamp01(_vc.SpeedKph / 30f);

                // 센터 복귀 (횡G 기반 자연 각도를 향해)
                float naturalNorm = Mathf.Clamp(lateralG * lateralGSensitivity, -1f, 1f);
                float returnFB    = -(steerNorm - naturalNorm) * returnTorque * speedScale;

                // 고속 감쇠
                float damping = -Mathf.Sign(steerAngle) * speedNorm * highSpeedDamping;

                totalFFB = Mathf.Clamp(returnFB + damping, -maxFeedbackTorque, maxFeedbackTorque);

                // 최소 토크 보장 (속도 비례 — 급격한 점프 없음)
                float minFFB = minFFBTorque * speedScale;
                if (Mathf.Abs(steerAngle) > 5f && Mathf.Abs(totalFFB) < minFFB)
                    totalFFB = -Mathf.Sign(steerAngle) * minFFB;
            }

            // 엔드스탑: 속도 무관, 450° 초과 즉시 최대 반력
            if (Mathf.Abs(steerAngle) > 450f)
                totalFFB = -Mathf.Sign(steerAngle) * maxFeedbackTorque;

            // → OpenFFBoard로 CAN 송신
            float sendTorque = testForceTorque != 0f ? testForceTorque : totalFFB;
            _steering.SendFFBTorque(sendTorque);

            // 주기적 디버그 로그 (0.5초마다)
            _logTimer -= Time.fixedDeltaTime;
            if (_logTimer <= 0f)
            {
                _logTimer = 0.5f;
                short raw = (short)(Mathf.Clamp(sendTorque, -1f, 1f) * 32767f);
                Debug.Log($"[EPS] {(testForceTorque != 0f ? "TEST" : "NORM")} steerAngle={steerAngle:F1}° " +
                          $"sendTorque={sendTorque:F3} raw={raw}");
            }
        }
    }
}
