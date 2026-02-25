using UnityEngine;
using UnityEngine.InputSystem;
using CarSim.CAN;
using CarSim.ADAS;

namespace CarSim.Vehicle
{
    /// <summary>
    /// 차량 물리 메인 컨트롤러
    /// - WheelCollider 4개 제어
    /// - CAN 핸들러로부터 입력 수신
    /// - Engine, ManualTransmission, ADAS 모듈 연결
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Engine))]
    [RequireComponent(typeof(ManualTransmission))]
    public class VehicleController : MonoBehaviour
    {
        [Header("휠 콜라이더")]
        [SerializeField] WheelCollider wheelFL;
        [SerializeField] WheelCollider wheelFR;
        [SerializeField] WheelCollider wheelRL;
        [SerializeField] WheelCollider wheelRR;

        [Header("휠 메시 - 림")]
        [SerializeField] Transform meshFL;
        [SerializeField] Transform meshFR;
        [SerializeField] Transform meshRL;
        [SerializeField] Transform meshRR;

        [Header("휠 메시 - 타이어 (림과 분리된 경우)")]
        [SerializeField] Transform tireFL;
        [SerializeField] Transform tireFR;
        [SerializeField] Transform tireRL;
        [SerializeField] Transform tireRR;

        [Header("구동방식")]
        [SerializeField] DriveType driveType = DriveType.RWD;

        [Header("구동 효율")]
        [Tooltip("WheelCollider에 전달할 토크 배율. 낮출수록 가속이 느려짐 (기본 0.15)")]
        [SerializeField] float torqueScale = 0.15f;

        [Header("브레이크")]
        [SerializeField] float maxBrakeTorque  = 2500f;  // Nm/휠 (4휠 합산: 1.5톤 차 기준 ~1.7g 제동)
        [SerializeField] float handBrakeTorque = 4000f;

        [Header("스티어링")]
        [SerializeField] float maxSteerAngle   = 35f;   // 앞바퀴 최대 조향각 (도)
        [SerializeField] float steeringRatio   = 13f;   // 스티어링 기어비 (휠각÷조향각)

        [Header("질량 설정")]
        [SerializeField] Vector3 centerOfMassOffset = new Vector3(0, -0.3f, 0.1f);

        [Header("타이어 마찰 (WheelCollider 커브)")]
        [Tooltip("앞바퀴 종방향 마찰 강성 (가속/제동 그립)")]
        [SerializeField] float fwdFrictionStiffnessFront   = 1.4f;
        [Tooltip("뒷바퀴 종방향 마찰 강성")]
        [SerializeField] float fwdFrictionStiffnessRear    = 1.6f;
        [Tooltip("앞바퀴 횡방향 마찰 강성 (코너링)")]
        [SerializeField] float sideFrictionStiffnessFront  = 1.8f;
        [Tooltip("뒷바퀴 횡방향 마찰 강성")]
        [SerializeField] float sideFrictionStiffnessRear   = 1.5f;

        // ── 컴포넌트 참조 ──────────────────────────────
        Rigidbody          _rb;
        Engine             _engine;
        ManualTransmission _trans;
        PedalECUHandler    _pedals;
        SteeringHandler    _steering;
        SwitchPanelHandler _switches;
        ABS                _abs;

        // ── 공개 상태 ──────────────────────────────────
        public float SpeedKph      => _rb.linearVelocity.magnitude * 3.6f;
        public float SpeedMs       => _rb.linearVelocity.magnitude;
        public float LateralG      => Vector3.Dot(_rb.linearVelocity, transform.right) / 9.81f;
        public float LongitudinalG { get; private set; }

        Vector3 _prevVelocity;

        void Awake()
        {
            _rb       = GetComponent<Rigidbody>();
            _engine   = GetComponent<Engine>();
            _trans    = GetComponent<ManualTransmission>();
            _pedals   = FindObjectOfType<PedalECUHandler>();
            _steering = FindObjectOfType<SteeringHandler>();
            _switches = FindObjectOfType<SwitchPanelHandler>();
            _abs      = GetComponent<ABS>();

            _rb.centerOfMass = centerOfMassOffset;

            // 타이어 마찰 커브 설정
            SetWheelFriction(wheelFL, fwdFrictionStiffnessFront, sideFrictionStiffnessFront);
            SetWheelFriction(wheelFR, fwdFrictionStiffnessFront, sideFrictionStiffnessFront);
            SetWheelFriction(wheelRL, fwdFrictionStiffnessRear,  sideFrictionStiffnessRear);
            SetWheelFriction(wheelRR, fwdFrictionStiffnessRear,  sideFrictionStiffnessRear);
        }

        static void SetWheelFriction(WheelCollider w, float fwdStiffness, float sideStiffness)
        {
            if (w == null) return;

            WheelFrictionCurve fwd  = w.forwardFriction;
            fwd.extremumSlip        = 0.1f;   // 실제 타이어 피크 그립 지점 (0.05~0.15)
            fwd.extremumValue       = 1.0f;
            fwd.asymptoteSlip       = 0.5f;
            fwd.asymptoteValue      = 0.6f;
            fwd.stiffness           = fwdStiffness;
            w.forwardFriction       = fwd;

            WheelFrictionCurve side = w.sidewaysFriction;
            side.extremumSlip       = 0.2f;
            side.extremumValue      = 1.0f;
            side.asymptoteSlip      = 0.5f;
            side.asymptoteValue     = 0.75f;
            side.stiffness          = sideStiffness;
            w.sidewaysFriction      = side;
        }

        void FixedUpdate()
        {
            if (_pedals  == null || _steering == null || _switches == null) return;

            // 종방향 가속도 계산 (m/s² → G)
            Vector3 accel = (_rb.linearVelocity - _prevVelocity) / Time.fixedDeltaTime;
            LongitudinalG = Vector3.Dot(accel, transform.forward) / 9.81f;
            _prevVelocity = _rb.linearVelocity;

            HandleIgnition();
            FeedInputToModules();
            ApplyDrive();
            ApplySteering();
            if (_abs == null) ApplyBrakes();  // ABS 있으면 ABS.FixedUpdate가 브레이크 처리
            SyncWheelMeshes();
        }

        void HandleIgnition()
        {
            // 시동 조건: 이그니션 ON + EngineStart 신호 + 클러치 충분히 밟힌 상태 (분리) + 현재 꺼진 상태
            bool clutchDepressed = _pedals.Clutch < 0.3f;
            if (_switches.IgnitionOn && _switches.EngineStart && !_engine.IsRunning && clutchDepressed)
                _engine.StartEngine();
            else if (!_switches.IgnitionOn && _engine.IsRunning)
                _engine.StopEngine();
        }

        void FeedInputToModules()
        {
            _engine.ThrottleInput = _pedals.Throttle;
            _trans.ClutchInput    = _pedals.Clutch;
            _trans.RequestedGear  = _switches.GearRequest;

            // 전진 속도만으로 wheel RPM 계산 (횡방향 미끄러짐 속도 제외)
            // velocity.magnitude 사용 시 선회 중 횡속도가 합산돼 RPM 과대계산 → 고RPM 토크 감소 → 가속 불가
            float forwardSpeedMs = Mathf.Max(0f, Vector3.Dot(_rb.linearVelocity, transform.forward));
            float wheelRadius    = (wheelRL != null) ? wheelRL.radius : 0.32f;
            float speedWheelRpm  = (forwardSpeedMs / (2f * Mathf.PI * wheelRadius)) * 60f;
            _trans.WheelSpeedRpm  = speedWheelRpm;
            _trans.VehicleSpeedMs = forwardSpeedMs;
        }

        void ApplyDrive()
        {
            // TransmittedTorque 는 양수(구동) 또는 음수(엔진 브레이크) 모두 가능
            // 시동 꺼짐 시에는 0 처리 — 엔진 브레이크는 클러치 결합 시에만 발생
            float torque   = _engine.IsRunning ? _trans.TransmittedTorque * torqueScale : 0f;

            // 브레이크 밟을 때 음수 motorTorque(엔진브레이크) 제거
            // 뒷바퀴에 엔진브레이크+풋브레이크 중첩 → ABS 즉시 발동 → 앞바퀴만 제동되는 현상 방지
            if (BrakeInput > 0f && torque < 0f) torque = 0f;

            float perWheel = torque * 0.5f;

            switch (driveType)
            {
                case DriveType.RWD:
                    wheelRL.motorTorque = perWheel;
                    wheelRR.motorTorque = perWheel;
                    wheelFL.motorTorque = 0f;
                    wheelFR.motorTorque = 0f;
                    break;
                case DriveType.FWD:
                    wheelFL.motorTorque = perWheel;
                    wheelFR.motorTorque = perWheel;
                    wheelRL.motorTorque = 0f;
                    wheelRR.motorTorque = 0f;
                    break;
                case DriveType.AWD:
                    float awd = torque * 0.25f;
                    wheelFL.motorTorque = awd;
                    wheelFR.motorTorque = awd;
                    wheelRL.motorTorque = awd;
                    wheelRR.motorTorque = awd;
                    break;
            }
        }

        void ApplySteering()
        {
            // 스티어링 기어비 적용: 휠 각도 ÷ 기어비 = 앞바퀴 조향각
            float steerAngle = Mathf.Clamp(_steering.SteeringAngle / steeringRatio,
                                           -maxSteerAngle, maxSteerAngle);

            // 에커만 근사 (내측 바퀴 더 많이 꺾임)
            float ackermann = steerAngle > 0 ? 1.05f : -1.05f;
            if (steerAngle > 0f)
            {
                wheelFL.steerAngle = steerAngle * ackermann;
                wheelFR.steerAngle = steerAngle;
            }
            else
            {
                wheelFL.steerAngle = steerAngle;
                wheelFR.steerAngle = steerAngle * ackermann;
            }
        }

        // 브레이크는 ADAS 모듈(ABS)에서 수정 가능하도록 공개
        public float BrakeInput  => _pedals.Brake;
        public bool  HandbrakeOn => Keyboard.current != null && Keyboard.current.spaceKey.isPressed;

        void ApplyBrakes()
        {
            if (GetComponent<CarSim.ADAS.ABS>() != null) return;

            float brake = BrakeInput * maxBrakeTorque;
            float hb    = HandbrakeOn ? handBrakeTorque : 0f;

            wheelFL.brakeTorque = brake;
            wheelFR.brakeTorque = brake;
            wheelRL.brakeTorque = brake + hb;
            wheelRR.brakeTorque = brake + hb;
        }

        void SyncWheelMeshes()
        {
            SyncMesh(wheelFL, meshFL);
            SyncMesh(wheelFR, meshFR);
            SyncMesh(wheelRL, meshRL);
            SyncMesh(wheelRR, meshRR);
            // 타이어도 같이 동기화
            SyncMesh(wheelFL, tireFL);
            SyncMesh(wheelFR, tireFR);
            SyncMesh(wheelRL, tireRL);
            SyncMesh(wheelRR, tireRR);
        }

        static void SyncMesh(WheelCollider col, Transform mesh)
        {
            if (mesh == null) return;
            col.GetWorldPose(out Vector3 pos, out Quaternion rot);
            mesh.SetPositionAndRotation(pos, rot);
        }

        public WheelCollider[] GetAllWheels()
            => new[] { wheelFL, wheelFR, wheelRL, wheelRR };

        public enum DriveType { RWD, FWD, AWD }
    }
}
