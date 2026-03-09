using UnityEngine;

namespace CarSim.Vehicle
{
    /// <summary>
    /// 클러치 + 수동 변속기 시뮬레이션 v2 — 커플링 기반
    ///
    /// 물리 원칙:
    ///   - 클러치 완전 결합: 엔진 RPM = 바퀴RPM × 기어비  (하드락, ForceRPM)
    ///   - 클러치 슬립 구간: 엔진 RPM을 드라이브트레인 RPM 쪽으로 당김 (PullRPM)
    ///   - 클러치 분리/변속 중: 엔진 자유 회전 (Engine 자체 물리)
    ///
    ///   스톨: 클러치 결합 중 드라이브트레인 RPM이 stall 임계 이하일 때
    ///   엔진 브레이크: EngineBrakeTorque × 기어비 × 효율이 바퀴에 음수 토크로 전달
    /// </summary>
    public class ManualTransmission : MonoBehaviour
    {
        [Header("기어비")]
        // 기어비 재조정: 각 단수 간 간격을 좁혀(Close Ratio) 변속 시 RPM 낙차를 줄임
        [SerializeField] float[] forwardGearRatios = { 3.45f, 2.05f, 1.45f, 1.10f, 0.88f, 0.72f };
        [SerializeField] float   reverseGearRatio  = -3.40f;
        [SerializeField] float   finalDriveRatio   = 4.30f; // 종감속비 소폭 하향 (기어비 간격 조정에 맞춤)

        [Header("클러치")]
        [SerializeField] float bitePoint       = 0.35f;  // 결합 시작 지점 (0~1)
        [SerializeField] float maxSlipTorque   = 200f;   // 클러치 용량. 엔진 토크(160)에 맞춰 조정

        [Header("변속 딜레이")]
        [SerializeField] float shiftTime       = 0.15f;
        [SerializeField] float shiftRpmDropStrength = 1.0f;
        [SerializeField] float coupledRpmFollowRate = 0.9f;

        [Header("드라이브트레인 효율")]
        [SerializeField] float driveEfficiency = 0.92f;  // 기어박스 마찰 손실

        [Header("스톨 임계값")]
        [SerializeField] float stallRpm        = 700f;   // 이 RPM 아래 + 클러치 결합 → 스톨 (아이들 800 기준)

        // ── 공개 상태 ──────────────────────────────────────────────────────────
        public int   CurrentGear       { get; private set; }
        public int   RequestedGear     { get; set; }
        public float ClutchInput       { get; set; }   // 0=분리, 1=접속
        public float ClutchEngagement  { get; private set; }

        /// <summary>휘에 전달할 순 토크 (Nm). 양수=가속, 음수=엔진브레이크</summary>
        public float TransmittedTorque { get; private set; }

        // VehicleController 에서 주입
        public float WheelSpeedRpm  { set => _wheelSpeedRpm  = value; }
        /// <summary>스톨 판정용 실제 차량 속도 (m/s, VehicleController 주입)</summary>
        public float VehicleSpeedMs { get; set; }

        bool  _shifting;
        float _shiftTimer;
        float _wheelSpeedRpm;

        Engine _engine;

        void Awake() => _engine = GetComponent<Engine>();

        void FixedUpdate()
        {
            HandleGearShift();
            UpdateCoupling();
        }

        // ── 기어 변속 ─────────────────────────────────────────────────────────
        void HandleGearShift()
        {
            // 디버그: 기어 요청 상태 출력 (후진 관련)
            if (Time.frameCount % 30 == 0 && (RequestedGear < 0 || CurrentGear < 0))
            {
                Debug.Log($"[MT 기어] RequestedGear={RequestedGear}, CurrentGear={CurrentGear}, Shifting={_shifting}, " +
                          $"ClutchInput={ClutchInput:F2}, VehicleSpeed={VehicleSpeedMs:F2}m/s");
            }
            
            if (_shifting)
            {
                _shiftTimer -= Time.fixedDeltaTime;
                if (_shiftTimer <= 0f)
                {
                    CurrentGear = RequestedGear;
                    _shifting   = false;

                    // 클러치가 물린 상태에서만 변속 직후 RPM 동기화를 수행한다.
                    // (클러치 분리 상태 N->1에서는 엔진이 자유회전해야 하므로 RPM을 강제로 내리면 안 됨)
                    bool clutchCoupledForShiftSync = ClutchInput >= bitePoint;
                    if (clutchCoupledForShiftSync)
                    {
                        float postShiftRatio = Mathf.Abs(GetGearRatio(CurrentGear) * finalDriveRatio);
                        float postShiftRpm = Mathf.Abs(_wheelSpeedRpm) * postShiftRatio;
                        if (_engine != null && _engine.RPM > postShiftRpm)
                            _engine.PullRPM(postShiftRpm, shiftRpmDropStrength);
                    }

                    Debug.Log($"[MT] 기어 변경 완료: {CurrentGear}");
                }
                return;
            }

            // 저속에서는 동기변속 보조로 클러치 없이도 기어 진입 허용
            bool clutchShiftAllowed = ClutchInput < 0.3f;
            bool lowSpeedShiftAllowed = VehicleSpeedMs < 1.2f;

            // 후진 기어 보호: 5 km/h 이상에서는 R단 진입 차단 (현실적 기어 락)
            if (RequestedGear == -1 && VehicleSpeedMs > 1.4f)
            {
                if (Time.frameCount % 60 == 0)
                    Debug.LogWarning($"[MT] 후진 기어 진입 거부 - 속도가 너무 빠름 ({VehicleSpeedMs * 3.6f:F1} km/h)");
                return;
            }

            if (RequestedGear != CurrentGear && (clutchShiftAllowed || lowSpeedShiftAllowed))
            {
                _shifting   = true;
                _shiftTimer = shiftTime;
            }
        }

        // ── 클러치 커플링 + 토크 계산 ──────────────────────────────────────────
        void UpdateCoupling()
        {
            bool forcedDisengage = _shifting || CurrentGear == 0;

            // 결합률 계산: 선형 (Linear) - bitePoint 로직 제거하고 입력값 그대로 사용
            if (forcedDisengage) ClutchEngagement = 0f;
            else                 ClutchEngagement = ClutchInput;
            
            // 디버그: 클러치 상태
            if (Time.frameCount % 30 == 0)
            {
                Debug.Log($"[클러치] Input={ClutchInput:F2}, Engagement={ClutchEngagement:F2}, " +
                          $"BitePt={bitePoint:F2}, Forcing={forcedDisengage}, Gear={CurrentGear}");
            }

            float gearRatio  = GetGearRatio(CurrentGear);
            float totalRatio = gearRatio * finalDriveRatio;
            float drivetrainRpm = Mathf.Abs(_wheelSpeedRpm) * Mathf.Abs(totalRatio);

            if (_engine != null)
                _engine.WheelDrivenRpm = drivetrainRpm;

            bool isCoupled = false;

            // 최소한의 데드존(0.05)만 적용하여 즉각적인 선형 반응 유도
            if (!forcedDisengage && CurrentGear != 0 && ClutchInput >= 0.05f)
            {
                if (ClutchEngagement >= 0.95f)
                {
                    // 완전 결합: 엔진 RPM이 드라이브트레인 RPM을 항상 추종
                    // (업/다운시프트에서 RPM이 유지되는 문제 방지)
                    _engine?.PullRPM(drivetrainRpm, coupledRpmFollowRate);
                    isCoupled = true;

                    // 스톨: RPM이 stallRpm 이하로 떨어지면 꺼짐
                    if (_engine != null && _engine.IsRunning && _engine.RPM < stallRpm)
                    {
                        _engine.StopEngine();
                        Debug.Log("[MT] 스톨");
                    }
                    
                    // // 고속 후진 기어 스톨 (현실적이지 않아 비활성화)
                    // if (_engine != null && _engine.IsRunning && CurrentGear == -1 && VehicleSpeedMs > 2.8f)
                    // {
                    //     _engine.StopEngine();
                    //     Debug.LogWarning($"[MT] 고속 후진 기어 스톨! 속도: {VehicleSpeedMs * 3.6f:F1} km/h");
                    // }
                }
                else
                {
                    // 바이트포인트 ~ 완전결합 슬립 구간에서도 RPM은 점진적으로 추종
                    // 부하 계수 대폭 감소 (0.1 -> 0.02): 클러치가 엔진을 잡는 힘을 최소화하여 RPM 상승 원활하게 함
                    _engine?.PullRPM(drivetrainRpm, ClutchEngagement * 0.02f);
                    // 슬립 구간에서는 엔진이 스로틀에 반응해야 하므로 물리 연산을 켜둠 (isCoupled = false)
                    isCoupled = false;
                }
            }

            // Engine RPM 물리 Tick (단 한 곳에서만 호출)
            _engine?.Tick(Time.fixedDeltaTime, isCoupled);

            // ── 전달 토크 계산 ────────────────────────────────────────────
            if (_engine == null || forcedDisengage || CurrentGear == 0 || ClutchInput < 0.05f)
            {
                TransmittedTorque = 0f;
                return;
            }

            if (ClutchEngagement >= 0.95f)
            {
                // 완전 결합: 구동토크와 엔진브레이크를 분리 계산
                // 엔진브레이크는 바퀴 회전 방향을 항상 감속시키도록 작용해야 한다.
                float driveTorque = _engine.OutputTorque * totalRatio * driveEfficiency;

                float wheelRpmAbs = Mathf.Abs(_wheelSpeedRpm);
                float engineBrakeWheelTorque = 0f;
                if (wheelRpmAbs > 1f)
                {
                    float wheelDir = Mathf.Sign(_wheelSpeedRpm);
                    engineBrakeWheelTorque = -wheelDir * _engine.EngineBrakeTorque * Mathf.Abs(totalRatio) * driveEfficiency;
                }

                TransmittedTorque = driveTorque + engineBrakeWheelTorque;
                
                // 디버그: 토크 계산 상세 추적
                if (Time.frameCount % 30 == 0)
                {
                    Debug.Log($"[MT토크] Gear={CurrentGear}, gearRatio={gearRatio:F2}, finalDrive={finalDriveRatio:F2}, " +
                              $"totalRatio={totalRatio:F2}, driveEff={driveEfficiency:F2}, " +
                              $"engineTorque={_engine.OutputTorque:F1}, driveTorque={driveTorque:F1}, " +
                              $"engineBrake={engineBrakeWheelTorque:F1}, FINAL={TransmittedTorque:F1}");
                }
            }
            else
            {
                // 바이트포인트 ~ 완전결합 슬립 구간
                // 클러치 면에서 전달되는 토크 × 기어비 × 효율 (완전결합과 동일한 경로)
                float slipMax     = maxSlipTorque * ClutchEngagement;
                float dir         = _engine.RPM >= drivetrainRpm ? 1f : -1f;
                // 후진 기어(totalRatio < 0)일 때 올바른 방향(음수 토크)을 내기 위해 Abs 제거
                TransmittedTorque = dir * slipMax * totalRatio * driveEfficiency;
            }
        }

        // ── 헬퍼 ──────────────────────────────────────────────────────────────
        float GetGearRatio(int gear)
        {
            if (gear == -1) return reverseGearRatio;
            if (gear ==  0) return 0f;
            int idx = Mathf.Clamp(gear - 1, 0, forwardGearRatios.Length - 1);
            return forwardGearRatios[idx];
        }

        public float GetCurrentTotalRatio()
            => GetGearRatio(CurrentGear) * finalDriveRatio;
    }
}
