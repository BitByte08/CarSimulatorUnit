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
        [SerializeField] float minFFBTorque = 0.3f;  // 30% 최소 (모터 정지마찰 극복)
        
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

            // 노면 반력 피드백
            float loadFeedback = lateralG * lateralGSensitivity;

            // 속도 기반 복귀력: 정차 시 0, 속도 오를수록 증가 (실제 캐스터 효과)
            float returnBase = Mathf.Clamp01(_vc.SpeedKph / 60f);
            float returnFB = -steerNorm * returnTorque * returnBase;

            // 속도 감응 감쇠 (고속 = 무거운 핸들, 정차 시엔 감쇠 없음)
            float damping = steerAngle != 0f
                ? -Mathf.Sign(steerAngle) * speedNorm * highSpeedDamping
                : 0f;

            float totalFFB = Mathf.Clamp(loadFeedback + returnFB + damping,
                                         -maxFeedbackTorque, maxFeedbackTorque);

            // 최소 FFB 보장: 주행 중 + 핸들 꺾여 있을 때만 (정차 시 불필요)
            if (minFFBTorque > 0f && _vc.SpeedKph > 5f && Mathf.Abs(steerAngle) > 5f && Mathf.Abs(totalFFB) < minFFBTorque)
                totalFFB = -Mathf.Sign(steerAngle) * minFFBTorque;

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
