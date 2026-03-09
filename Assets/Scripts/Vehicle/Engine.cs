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
        [SerializeField] float idleRpm      = 800f;     // 가솔린 승용차 아이들
        [SerializeField] float redlineRpm   = 6500f;    // 가솔린 일반 최대 RPM
        [SerializeField] float maxTorqueNm  = 160f;     // 준중형 1.6L NA 수준 (약 16kgf.m)

        [Header("토크 커브 (RPM 정규화 0-1)")]
        [SerializeField] AnimationCurve torqueCurve = DefaultTorqueCurve();

        [Header("레브 리밋")]
        [SerializeField] float revLimitRpm         = 6600f;  // 레드라인(6500) 도달 허용
        [SerializeField] float revLimitCutDuration = 0.1f;   // 컷 지속 시간 증가 (확실한 끊김)
        [SerializeField] float revLimitResumeRpm   = 6450f;  // 히스테리시스: 이 RPM 아래로 떨어져야 재개 (바운싱 유도)

        [Header("관성 / 마찰")]
        [SerializeField] float flywheelInertia = 0.25f;  // ↓ 관성 대폭 감소 (2.0 → 0.25) - 빠른 RPM 반응
        [SerializeField] float frictionCoeff   = 0.03f;  // ↓ Nm/RPM 스로틀 오프 드래그 감소 (0.06 → 0.03)

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
            // 바퀴에 의해 강제로 도는 경우 레드존을 넘을 수 있어야 함 (오버런 허용)
            RPM = Mathf.Max(0f, rpm);
        }

        public void PullRPM(float targetRpm, float strength)
        {
            if (!IsRunning) return;
            // 빠른 RPM 동기화 (15 → 25)
            float alpha = Mathf.Clamp01(strength) * Time.fixedDeltaTime * 25f;
            RPM = Mathf.Lerp(RPM, Mathf.Max(0f, targetRpm), alpha);
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
            if (!_revLimiterActive && RPM >= revLimitRpm)
            {
                _revLimiterActive = true;
                _revLimitTimer = revLimitCutDuration;
            }

            if (_revLimiterActive)
            {
                _revLimitTimer -= dt;
                // 타이머가 지났고 + RPM이 충분히 떨어졌을 때만 리미터 해제 (바운싱 효과)
                if (_revLimitTimer <= 0f && RPM < revLimitResumeRpm)
                    _revLimiterActive = false;
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

                // 물리 연산에서는 revLimitRpm보다 약간 높게 허용해야 리미터가 작동함
                RPM = Mathf.Clamp(RPM, 0f, revLimitRpm * 1.5f);
            }
            // (isCoupled: RPM은 ForceRPM/PullRPM 이 이미 바퀴 속도에 맞춰 설정)

            // 토크 캐싱 (항상 최신 RPM 기준으로)
            float n = Mathf.Clamp01(RPM / redlineRpm);
            OutputTorque      = torqueCurve.Evaluate(n) * maxTorqueNm * throttle;
            EngineBrakeTorque = throttle < 0.02f
                ? frictionCoeff * Mathf.Max(0f, RPM - idleRpm * 0.5f)
                : 0f;
            
            // 디버그: 엔진 토크 파이프라인
            if (throttle > 0.5f && Time.frameCount % 30 == 0)
            {
                Debug.Log($"[Engine] RPM={RPM:F0}, throttle={throttle:F2}, " +
                          $"curveVal={torqueCurve.Evaluate(n):F2}, maxTorque={maxTorqueNm}, " +
                          $"OutputTorque={OutputTorque:F1} Nm");
            }
        }

        public float RedlineRpm => redlineRpm;

        static AnimationCurve DefaultTorqueCurve()
        {
            // 가솔린 NA 특성: 중고회전(4000~5000rpm)에서 최대 토크
            return new AnimationCurve(
                new Keyframe(0.00f, 0.60f),  // 아이들
                new Keyframe(0.20f, 0.75f),  // ~1300rpm
                new Keyframe(0.40f, 0.85f),  // ~2600rpm
                new Keyframe(0.60f, 0.95f),  // ~3900rpm
                new Keyframe(0.70f, 1.00f),  // ~4500rpm (최대 토크)
                new Keyframe(0.85f, 0.92f),  // ~5500rpm
                new Keyframe(1.00f, 0.80f)   // 레드라인
            );
        }
    }
}
