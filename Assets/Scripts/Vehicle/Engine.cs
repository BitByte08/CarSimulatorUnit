using UnityEngine;

namespace CarSim.Vehicle
{
    /// <summary>
    /// 엔진 시뮬레이션 v3
    /// Unity 루프(FixedUpdate/LateUpdate) 없음 — ManualTransmission.FixedUpdate 에서
    /// 매 프레임 Tick(dt, isCoupled) 를 단독 호출. 실행 순서 문제 원천 차단.
    /// </summary>
    public class Engine : MonoBehaviour
    {
        [Header("엔진 스펙")]
        [SerializeField] float idleRpm      = 400f;
        [SerializeField] float redlineRpm   = 5000f;
        [SerializeField] float maxTorqueNm  = 320f;

        [Header("토크 커브 (RPM 정규화 0-1)")]
        [SerializeField] AnimationCurve torqueCurve = DefaultTorqueCurve();

        [Header("레브 리밋")]
        [SerializeField] float revLimitRpm         = 4800f;
        [SerializeField] float revLimitCutDuration = 0.08f;

        [Header("관성 / 마찰")]
        [SerializeField] float flywheelInertia = 2.0f;   // kg·m²
        [SerializeField] float frictionCoeff   = 0.06f;  // Nm/RPM 스로틀 오프 드래그 (엔진 브레이크)

        // ── 공개 상태 ─────────────────────────────────────────────────────────
        public float RPM           { get; private set; }
        public float ThrottleInput { get; set; }
        public bool  IsRunning     { get; private set; }
        public bool  IsStalled     => !IsRunning;

        /// <summary>스로틀 구동 토크 (Nm, ≥ 0)</summary>
        public float OutputTorque      { get; private set; }
        /// <summary>스로틀 오프 시 드래그 토크 (Nm, ≥ 0)</summary>
        public float EngineBrakeTorque { get; private set; }
        /// <summary>디버그: 바퀴RPM × 기어비 (ManualTransmission 세팅)</summary>
        public float WheelDrivenRpm    { get; set; }

        float _revLimitTimer;
        bool  _revLimiterActive;

        // ── 시동 제어 ─────────────────────────────────────────────────────────
        public void StartEngine()
        {
            if (IsRunning) return;
            RPM = idleRpm;
            IsRunning = true;
            Debug.Log("[Engine] 시동 ON");
        }

        public void StopEngine()
        {
            if (!IsRunning) return;
            IsRunning = false;
            Debug.Log("[Engine] 시동 OFF");
        }

        // ── RPM 직접 설정 (ManualTransmission 에서만 호출) ────────────────────
        public void ForceRPM(float rpm)
        {
            if (!IsRunning) return;
            // 스톨 처리는 ManualTransmission의 RPM 조건이 담당
            RPM = Mathf.Clamp(rpm, 0f, redlineRpm);
        }

        public void PullRPM(float targetRpm, float strength)
        {
            if (!IsRunning) return;
            float alpha = Mathf.Clamp01(strength) * Time.fixedDeltaTime * 15f;
            RPM = Mathf.Lerp(RPM, Mathf.Clamp(targetRpm, 0f, redlineRpm), alpha);
        }

        // ── ManualTransmission 에서 매 FixedUpdate 호출 ───────────────────────
        /// <param name="dt">Time.fixedDeltaTime</param>
        /// <param name="isCoupled">true=클러치 결합(RPM이미 설정됨) false=자유 회전</param>
        public void Tick(float dt, bool isCoupled)
        {
            if (!IsRunning)
            {
                RPM = Mathf.MoveTowards(RPM, 0f, dt * 600f);
                OutputTorque = EngineBrakeTorque = 0f;
                return;
            }

            // 레브 리밋
            if (RPM >= revLimitRpm) { _revLimiterActive = true; _revLimitTimer = revLimitCutDuration; }
            if (_revLimiterActive)
            {
                _revLimitTimer -= dt;
                if (_revLimitTimer <= 0f) _revLimiterActive = false;
            }

            float throttle = _revLimiterActive ? 0f : ThrottleInput;

            // 클러치 분리(자유 회전): RPM 자체 물리 계산
            if (!isCoupled)
            {
                float norm  = Mathf.Clamp01(RPM / redlineRpm);
                float torq  = torqueCurve.Evaluate(norm) * maxTorqueNm * throttle;
                RPM += (torq / flywheelInertia) * 9.549f * dt;

                if (throttle < 0.02f)
                {
                    float drag = frictionCoeff * Mathf.Max(0f, RPM - idleRpm * 0.5f);
                    RPM -= (drag / flywheelInertia) * 9.549f * dt;
                }

                // 아이들 유지
                if (throttle < 0.02f && RPM < idleRpm * 1.1f)
                    RPM = Mathf.MoveTowards(RPM, idleRpm, dt * 150f);

                RPM = Mathf.Clamp(RPM, 0f, redlineRpm);
            }
            // (isCoupled: RPM은 ForceRPM/PullRPM 이 이미 바퀴 속도에 맞춰 설정)

            // 토크 캐싱 (항상 최신 RPM 기준으로)
            float n = Mathf.Clamp01(RPM / redlineRpm);
            OutputTorque      = torqueCurve.Evaluate(n) * maxTorqueNm * throttle;
            EngineBrakeTorque = throttle < 0.02f
                ? frictionCoeff * Mathf.Max(0f, RPM - idleRpm * 0.5f)
                : 0f;
        }

        static AnimationCurve DefaultTorqueCurve()
        {
            return new AnimationCurve(
                new Keyframe(0.00f, 0.50f),
                new Keyframe(0.15f, 0.75f),
                new Keyframe(0.35f, 1.00f),
                new Keyframe(0.60f, 0.95f),
                new Keyframe(0.80f, 0.80f),
                new Keyframe(1.00f, 0.50f)
            );
        }
    }
}
