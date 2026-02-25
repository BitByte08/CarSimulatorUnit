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
        [SerializeField] float returnTorque      = 0.3f;   // 센터 복귀 토크 비율
        [SerializeField] float highSpeedDamping  = 0.4f;   // 고속 감쇠 (무거운 느낌)
        [SerializeField] float lateralGSensitivity = 0.25f;

        VehicleController _vc;
        SteeringHandler   _steering;
        Rigidbody         _rb;

        void Awake()
        {
            _vc       = GetComponent<VehicleController>();
            _rb       = GetComponent<Rigidbody>();
            _steering = FindObjectOfType<SteeringHandler>();
        }

        void FixedUpdate()
        {
            if (_steering == null) return;

            float speedNorm   = Mathf.Clamp01(_vc.SpeedKph / 120f);
            float lateralG    = _vc.LateralG;
            float steerAngle  = _steering.SteeringAngle;

            // 노면 반력 피드백
            float loadFeedback = lateralG * lateralGSensitivity;

            // 센터 복귀 토크 (고속일수록 강함)
            float returnFB = -steerAngle / 450f * returnTorque * speedNorm;

            // 속도 감응 감쇠 (고속 = 무거운 핸들)
            float damping = -Mathf.Sign(steerAngle) * speedNorm * highSpeedDamping;

            float totalFFB = Mathf.Clamp(loadFeedback + returnFB + damping,
                                         -maxFeedbackTorque, maxFeedbackTorque);

            // → OpenFFBoard로 CAN 송신
            _steering.SendFFBTorque(totalFFB);
        }
    }
}
