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
        // finalDrive=4.30 기준, idle=400 redline=5000 RPM, 바퀴반지름=0.32m
        // 1단: ~30km/h@5000  2단: ~56km/h  3단: ~82km/h  4단: ~110km/h  5단: ~135km/h  6단: ~165km/h
        [SerializeField] float[] forwardGearRatios = { 4.50f, 2.50f, 1.72f, 1.28f, 1.00f, 0.82f };
        [SerializeField] float   reverseGearRatio  = -4.50f;
        [SerializeField] float   finalDriveRatio   = 4.30f;

        [Header("클러치")]
        [SerializeField] float bitePoint       = 0.35f;  // 결합 시작 지점 (0~1)
        [SerializeField] float maxSlipTorque   = 350f;   // 슬립 구간 최대 전달 토크 (Nm)

        [Header("변속 딜레이")]
        [SerializeField] float shiftTime       = 0.15f;

        [Header("드라이브트레인 효율")]
        [SerializeField] float driveEfficiency = 0.92f;  // 기어박스 마찰 손실

        [Header("스톨 임계값")]
        [SerializeField] float stallRpm        = 400f;   // 이 RPM 아래 + 클러치 결합 → 스톨

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
            if (_shifting)
            {
                _shiftTimer -= Time.fixedDeltaTime;
                if (_shiftTimer <= 0f)
                {
                    CurrentGear = RequestedGear;
                    _shifting   = false;
                    // SetRPM 호출 없음 — 클러치 재결합 시 UpdateCoupling 이 자동으로 ForceRPM
                }
                return;
            }

            // 클러치가 충분히 분리된 상태에서만 변속
            if (RequestedGear != CurrentGear && ClutchInput < 0.3f)
            {
                _shifting   = true;
                _shiftTimer = shiftTime;
            }
        }

        // ── 클러치 커플링 + 토크 계산 ──────────────────────────────────────────
        void UpdateCoupling()
        {
            bool forcedDisengage = _shifting || CurrentGear == 0;

            // 결합률 계산
            if (forcedDisengage || ClutchInput <= bitePoint)
                ClutchEngagement = forcedDisengage ? 0f
                    : Mathf.InverseLerp(0f, bitePoint, ClutchInput) * 0.15f;
            else
                ClutchEngagement = Mathf.InverseLerp(bitePoint, 1f, ClutchInput);

            float gearRatio  = GetGearRatio(CurrentGear);
            float totalRatio = gearRatio * finalDriveRatio;
            float drivetrainRpm = Mathf.Abs(_wheelSpeedRpm) * Mathf.Abs(totalRatio);

            if (_engine != null)
                _engine.WheelDrivenRpm = drivetrainRpm;

            bool isCoupled = false;

            if (!forcedDisengage && CurrentGear != 0 && ClutchInput >= bitePoint)
            {
                if (ClutchEngagement >= 0.95f)
                {
                    // 완전 결합: 엔진 ↔ 바퀴 하나의 시스템 → 항상 RPM 동기화
                    // "엔진이 바퀴보다 빠를 때 자유회전"은 현실에서 불가능
                    // 그 RPM에서의 엔진 토크가 그대로 차를 밂
                    _engine?.ForceRPM(drivetrainRpm);
                    isCoupled = true;

                    // 스톨: RPM이 stallRpm 이하로 떨어지면 꺼짐 (현실적)
                    if (_engine != null && _engine.IsRunning && _engine.RPM < stallRpm)
                    {
                        _engine.StopEngine();
                        Debug.Log("[MT] 스톨");
                    }
                }
                else
                {
                    // 바이트포인트 ~ 완전결합 슬립 구간: 엔진이 바퀴보다 빠를 때만 당김
                    float engineRpm = _engine != null ? _engine.RPM : 0f;
                    if (engineRpm <= drivetrainRpm)
                    {
                        _engine?.PullRPM(drivetrainRpm, ClutchEngagement);
                        isCoupled = true;
                    }
                }
            }

            // Engine RPM 물리 Tick (단 한 곳에서만 호출)
            _engine?.Tick(Time.fixedDeltaTime, isCoupled);

            // ── 전달 토크 계산 ────────────────────────────────────────────
            if (_engine == null || forcedDisengage || CurrentGear == 0 || ClutchInput < bitePoint)
            {
                TransmittedTorque = 0f;
                return;
            }

            if (ClutchEngagement >= 0.95f)
            {
                // 완전 결합: 순 토크(구동 - 엔진드래그) × 기어비 × 효율
                float netTorque   = _engine.OutputTorque - _engine.EngineBrakeTorque;
                TransmittedTorque = netTorque * totalRatio * driveEfficiency;
            }
            else
            {
                // 바이트포인트 ~ 완전결합 슬립 구간
                // 클러치 면에서 전달되는 토크 × 기어비 × 효율 (완전결합과 동일한 경로)
                float slipMax     = maxSlipTorque * ClutchEngagement;
                float dir         = _engine.RPM >= drivetrainRpm ? 1f : -1f;
                TransmittedTorque = dir * slipMax * Mathf.Abs(totalRatio) * driveEfficiency;
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
