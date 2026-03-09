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
        [Tooltip("WheelCollider에 전달할 토크 배율. 낮출수록 가속이 느려짐")]
        [SerializeField] float torqueScale = 1.0f; // 엔진 토크(160Nm)를 그대로 전달
        [Tooltip("저속(출발/재가속)에서 추가 토크 배율")]
        [SerializeField] float launchAssistMultiplier = 1.00f;
        [Tooltip("이 속도(km/h)까지 launchAssistMultiplier가 1.0으로 선형 감소")]
        [SerializeField] float launchAssistEndKph = 10f;

        [Header("브레이크")]
        [SerializeField] float maxBrakeTorque  = 1800f;
        [SerializeField] float handBrakeTorque = 4000f;

        [Header("저속 안정화")]
        [Tooltip("스로틀/브레이크 입력이 거의 없고 저속일 때 정차 유지용 브레이크 토크")]
        [SerializeField] float idleHoldBrakeTorque = 380f;
        [Tooltip("정차 유지가 개입되는 최대 속도 (m/s)")]
        [SerializeField] float idleHoldMaxSpeedMs = 0.35f;

        [Header("입력 데드존")]
        [SerializeField] float throttleDeadzone = 0.03f;
        [SerializeField] float brakeDeadzone    = 0.04f;

        [Header("스티어링")]
        [SerializeField] Transform steeringWheelModel;   // 핸들 3D 모델 (Y축으로 회전)
        [SerializeField] float maxSteerAngle   = 35f;   // 앞바퀴 최대 조향각 (도)
        [SerializeField] float steeringRatio   = 13f;   // 스티어링 기어비 (휠각÷조향각)
        [SerializeField] float highSpeedSteerAngle = 14f; // 고속에서의 최대 조향각 (도)
        [SerializeField] float steerFadeStartKph   = 25f; // 이 속도부터 조향각 감쇠 시작
        [SerializeField] float steerFadeEndKph     = 170f; // 이 속도에서 highSpeedSteerAngle에 도달
        [SerializeField] float steerResponseRate   = 220f; // 조향각 변화 속도(도/초)

        [Header("질량 설정")]
        [SerializeField] Vector3 centerOfMassOffset = new Vector3(0, -0.25f, 0.1f);

        [Header("타이어 마찰 (WheelCollider 커브)")]
        [Tooltip("앞바퀴 종방향 마찰 강성 (가속/제동 그립)")]
        [SerializeField] float fwdFrictionStiffnessFront   = 2.5f;
        [Tooltip("뒷바퀴 종방향 마찰 강성")]
        [SerializeField] float fwdFrictionStiffnessRear    = 3.0f;
        [Tooltip("앞바퀴 횡방향 마찰 강성 (코너링)")]
        [SerializeField] float sideFrictionStiffnessFront  = 2.5f;
        [Tooltip("뒷바퀴 횡방향 마찰 강성")]
        [SerializeField] float sideFrictionStiffnessRear   = 2.5f;

        [Header("차체 롤 제어")]
        [SerializeField] float antiRollFront = 9000f;
        [SerializeField] float antiRollRear  = 7000f;

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
        
        public enum PowerState { Off, Acc, On }
        public PowerState CurrentPowerState { get; private set; } = PowerState.Off;

        Vector3 _prevVelocity;
        Vector3 _lastCenterOfMassOffset;
        bool _wasEngineStartSwitchOn; // 버튼 시동용 이전 상태

        void Awake()
        {
            _rb       = GetComponent<Rigidbody>();
            _engine   = GetComponent<Engine>();
            _trans    = GetComponent<ManualTransmission>();
            _pedals   = FindObjectOfType<PedalECUHandler>();
            _steering = FindObjectOfType<SteeringHandler>();
            _switches = FindObjectOfType<SwitchPanelHandler>();
            _abs      = GetComponent<ABS>();

            ApplyCenterOfMass();
            _lastCenterOfMassOffset = centerOfMassOffset;
            
            // 공기 저항 최소화 (스로틀 오프 시 자연스러운 타력 주행)
            _rb.linearDamping = 0.01f;        // ↓ 공기 저항 대폭 감소 (0.05 → 0.01)
            _rb.angularDamping = 0.5f;         // 회전 안정성 증가

            // Unity 그림자 경고 필터링
            Application.logMessageReceived += FilterShadowWarnings;

            // WheelCollider 물리 설정
            ConfigureWheelPhysics(wheelFL);
            ConfigureWheelPhysics(wheelFR);
            ConfigureWheelPhysics(wheelRL);
            ConfigureWheelPhysics(wheelRR);

            // 저속에서 휠 접지 계산 분해능을 올려 떨림(jitter) 감소
            wheelFL?.ConfigureVehicleSubsteps(5f, 12, 15);
            wheelFR?.ConfigureVehicleSubsteps(5f, 12, 15);
            wheelRL?.ConfigureVehicleSubsteps(5f, 12, 15);
            wheelRR?.ConfigureVehicleSubsteps(5f, 12, 15);

            // 타이어 마찰 커브 설정
            SetWheelFriction(wheelFL, fwdFrictionStiffnessFront, sideFrictionStiffnessFront);
            SetWheelFriction(wheelFR, fwdFrictionStiffnessFront, sideFrictionStiffnessFront);
            SetWheelFriction(wheelRL, fwdFrictionStiffnessRear,  sideFrictionStiffnessRear);
            SetWheelFriction(wheelRR, fwdFrictionStiffnessRear,  sideFrictionStiffnessRear);
        }

        static void ConfigureWheelPhysics(WheelCollider w)
        {
            if (w == null) return;
            
            // 휠 질량: 너무 크면 브레이크가 안 먹힘 (타이어+휠 실제 무게 ~20kg)
            w.mass = 20f;
            
            // 휠 댐핑: 고속 안정성과 응답성의 균형점
            w.wheelDampingRate = 0.65f;
            
            // 힘 적용점: 0으로 설정 (Unity 권장, 안정성 최대)
            w.forceAppPointDistance = 0f;  // ↓ 고속 안정성 (0.1 → 0)
            
            // 과도한 강성/감쇠는 저속 떨림을 만들 수 있어 안정 구간으로 조정
            JointSpring spring = w.suspensionSpring;
            spring.spring = 38000f;
            spring.damper = 5000f;
            spring.targetPosition = 0.5f;
            w.suspensionSpring = spring;
            
            // 너무 짧으면 노면 추종이 깨져 미세 진동이 커짐
            w.suspensionDistance = 0.22f;
        }

        static void SetWheelFriction(WheelCollider w, float fwdStiffness, float sideStiffness)
        {
            if (w == null) return;

            WheelFrictionCurve fwd  = w.forwardFriction;
            fwd.extremumSlip        = 0.08f;   // 피크 그립 지점을 더 빠르게 (0.08로 감소)
            fwd.extremumValue       = 1.15f;
            fwd.asymptoteSlip       = 0.4f;    // 슬립 후 감소 지점 조정
            fwd.asymptoteValue      = 0.95f;
            fwd.stiffness           = fwdStiffness;
            w.forwardFriction       = fwd;

            WheelFrictionCurve side = w.sidewaysFriction;
            side.extremumSlip       = 0.12f;   // 코너링 피크 슬립 포인트
            side.extremumValue      = 1.15f;
            side.asymptoteSlip      = 0.4f;
            side.asymptoteValue     = 0.98f;
            side.stiffness          = sideStiffness;
            w.sidewaysFriction      = side;
        }

        void ApplyCenterOfMass()
        {
            if (_rb == null) return;
            _rb.centerOfMass = centerOfMassOffset;
        }

        // UI 슬라이더/디버그 패널에서 바로 호출할 수 있는 API
        public void SetCenterOfMassOffset(Vector3 offset)
        {
            centerOfMassOffset = offset;
            ApplyCenterOfMass();
            _lastCenterOfMassOffset = centerOfMassOffset;
        }

        public void SetCenterOfMassY(float y)
        {
            centerOfMassOffset = new Vector3(centerOfMassOffset.x, y, centerOfMassOffset.z);
            ApplyCenterOfMass();
            _lastCenterOfMassOffset = centerOfMassOffset;
        }

        void ApplyAntiRollBars()
        {
            ApplyAntiRollPair(wheelFL, wheelFR, antiRollFront);
            ApplyAntiRollPair(wheelRL, wheelRR, antiRollRear);
        }

        static void ApplyAntiRollPair(WheelCollider left, WheelCollider right, float antiRoll)
        {
            if (left == null || right == null || antiRoll <= 0f) return;

            bool leftGrounded = left.GetGroundHit(out WheelHit leftHit);
            bool rightGrounded = right.GetGroundHit(out WheelHit rightHit);

            float leftTravel = 1f;
            float rightTravel = 1f;

            if (leftGrounded)
                leftTravel = (-left.transform.InverseTransformPoint(leftHit.point).y - left.radius) / left.suspensionDistance;
            if (rightGrounded)
                rightTravel = (-right.transform.InverseTransformPoint(rightHit.point).y - right.radius) / right.suspensionDistance;

            float antiRollForce = (leftTravel - rightTravel) * antiRoll;

            if (leftGrounded)
                left.attachedRigidbody.AddForceAtPosition(left.transform.up * -antiRollForce, left.transform.position);
            if (rightGrounded)
                right.attachedRigidbody.AddForceAtPosition(right.transform.up * antiRollForce, right.transform.position);
        }

        void FixedUpdate()
        {
            if (_pedals  == null || _steering == null || _switches == null) return;

            // Play 중 Scene/Inspector에서 무게중심을 바꾸면 즉시 반영
            if (_lastCenterOfMassOffset != centerOfMassOffset)
            {
                ApplyCenterOfMass();
                _lastCenterOfMassOffset = centerOfMassOffset;
            }

            // 종방향 가속도 계산 (m/s² → G)
            Vector3 accel = (_rb.linearVelocity - _prevVelocity) / Time.fixedDeltaTime;
            LongitudinalG = Vector3.Dot(accel, transform.forward) / 9.81f;
            _prevVelocity = _rb.linearVelocity;

            HandleIgnition();
            FeedInputToModules();
            ApplyDrive();
            ApplySteering();
            ApplyAntiRollBars();
            if (_abs == null) ApplyBrakes();  // ABS 있으면 ABS.FixedUpdate가 브레이크 처리
            SyncWheelMeshes();
        }

        void HandleIgnition()
        {
            // 버튼 시동 로직: EngineStart 스위치의 '눌리는 순간'(rising edge)을 감지
            bool engineStartPressed = _switches.EngineStart && !_wasEngineStartSwitchOn;
            _wasEngineStartSwitchOn = _switches.EngineStart;

            if (!engineStartPressed) return;

            // 시동이 걸려있을 때 버튼을 누르면 무조건 시동 OFF
            if (_engine.State != Engine.EngineState.Off)
            {
                _engine.StopEngine();
                CurrentPowerState = PowerState.Off;
                if (_switches != null) _switches.SetIgnition(false);
                Debug.Log("[VC] Engine & Power OFF");
                return;
            }

            // 시동이 꺼져있을 때
            bool clutchDepressed = _pedals.Clutch < 0.3f;
            bool brakePressed = _pedals.Brake > 0.1f;

            // 1. 클러치와 브레이크를 동시에 밟고 누르면 바로 시동 (안전 시동)
            // 주행 중 시동이 꺼졌을 때도 이 조건을 만족하면 재시동 가능
            if (clutchDepressed && brakePressed)
            {
                _engine.StartEngine();
                CurrentPowerState = PowerState.On; // 시동 걸리면 무조건 ON 상태
                Debug.Log("[VC] Engine Start sequence initiated. Power state is ON.");
            }
            // 2. 안 밟고 누르면 OFF -> ACC -> ON -> OFF 순환
            else
            {
                switch (CurrentPowerState)
                {
                    case PowerState.Off:
                        CurrentPowerState = PowerState.Acc;
                        if (_switches != null) _switches.SetIgnition(true);
                        Debug.Log("[VC] Power state: ACC ON");
                        break;
                    case PowerState.Acc:
                        CurrentPowerState = PowerState.On;
                        if (_switches != null) _switches.SetIgnition(true);
                        Debug.Log("[VC] Power state: IGNITION ON");
                        break;
                    case PowerState.On:
                        CurrentPowerState = PowerState.Off;
                        if (_switches != null) _switches.SetIgnition(false);
                        Debug.Log("[VC] Power state: OFF");
                        break;
                }
            }
        }

        void FeedInputToModules()
        {
            _engine.ThrottleInput = ApplyDeadzone(_pedals.Throttle, throttleDeadzone);
            _trans.ClutchInput    = _pedals.Clutch;
            _trans.RequestedGear  = _switches.GearRequest;

            // 변속기용 휠 RPM은 부호가 있는 종속도로 계산해야 후진 기어 물리가 정상 동작한다.
            // (R에서 전진하는 현상 방지)
            float longitudinalSpeedMs = Vector3.Dot(_rb.linearVelocity, transform.forward);

            // ABS/디버그는 전진 속도 기준으로 유지
            ForwardSpeedMs = Mathf.Max(0f, longitudinalSpeedMs);
            float wheelRadius    = (wheelRL != null) ? wheelRL.radius : 0.32f;
            float speedWheelRpm  = (longitudinalSpeedMs / (2f * Mathf.PI * wheelRadius)) * 60f;
            _trans.WheelSpeedRpm  = speedWheelRpm;
            _trans.VehicleSpeedMs = Mathf.Abs(longitudinalSpeedMs);
        }

        void ApplyDrive()
        {
            // TransmittedTorque 는 양수(구동) 또는 음수(엔진 브레이크) 모두 가능
            float torque = _engine.IsRunning ? _trans.TransmittedTorque * torqueScale : 0f;

            // RPM에 따른 비선형 토크 곡선 적용
            float rpmNormalized = Mathf.Clamp01(_engine.RPM / _engine.RedlineRpm); // RedlineRpm으로 수정
            float torqueCurveFactor = Mathf.Lerp(0.8f, 1.2f, Mathf.Sin(rpmNormalized * Mathf.PI));
            torque *= torqueCurveFactor;

            // 저속에서 추가 토크 배율 적용
            float launchBlend = Mathf.InverseLerp(0f, launchAssistEndKph, SpeedKph);
            float launchFactor = Mathf.Lerp(launchAssistMultiplier, 1f, launchBlend);
            torque *= launchFactor;

            float perWheel = torque * 0.5f;

            switch (driveType)
            {
                case DriveType.RWD:
                    wheelRL.motorTorque = perWheel;
                    wheelRR.motorTorque = perWheel;
                    break;
                case DriveType.FWD:
                    wheelFL.motorTorque = perWheel;
                    wheelFR.motorTorque = perWheel;
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
            // 고속에서 급조향으로 타이어가 잠기는 느낌을 줄이기 위해 속도별 최대 조향각을 감소
            float speedFade = Mathf.InverseLerp(steerFadeStartKph, steerFadeEndKph, SpeedKph);
            float speedMaxSteer = Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, speedFade);

            // 스티어링 기어비 적용: 휠 각도 ÷ 기어비 = 앞바퀴 조향각
            float steerAngle = Mathf.Clamp(_steering.SteeringAngle / steeringRatio,
                                           -speedMaxSteer, speedMaxSteer);

            // 에커만 근사 (내측 바퀴 더 많이 꺾임)
            const float ackermannMultiplier = 1.02f;
            float targetFL;
            float targetFR;
            if (steerAngle > 0f)
            {
                // 우회전: 왼쪽 앞바퀴 더 꺾임
                targetFL = steerAngle * ackermannMultiplier;
                targetFR = steerAngle;
            }
            else
            {
                // 좌회전: 오른쪽 앞바퀴 더 꺾임
                targetFL = steerAngle;
                targetFR = steerAngle * ackermannMultiplier;
            }

            targetFL = Mathf.Clamp(targetFL, -speedMaxSteer, speedMaxSteer);
            targetFR = Mathf.Clamp(targetFR, -speedMaxSteer, speedMaxSteer);

            float maxStep = steerResponseRate * Time.fixedDeltaTime;
            wheelFL.steerAngle = Mathf.MoveTowards(wheelFL.steerAngle, targetFL, maxStep);
            wheelFR.steerAngle = Mathf.MoveTowards(wheelFR.steerAngle, targetFR, maxStep);

            // 핸들 3D 모델 회전 (스티어링 휠)
            if (steeringWheelModel != null)
            {
                // OpenFFBoard 스티어링: -450도 ~ +450도 범위
                // 핸들은 보통 Y축으로 회전 (마이너스=시계방향, 양수=반시계방향)
                float wheelRotation = -_steering.SteeringAngle;  // 마이너스를 붙여서 방향 일치
                steeringWheelModel.localRotation = Quaternion.Euler(0, 0, wheelRotation);
            }
        }

        // 브레이크는 ADAS 모듈(ABS)에서 수정 가능하도록 공개
        public float BrakeInput  => GetBrakeInputWithDeadzone();
        public float MaxBrakeTorque => maxBrakeTorque;
        public float ForwardSpeedMs { get; private set; }  // ABS가 사용하는 전진 속도 (횡속도 제외)
        public bool  HandbrakeOn => Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
        public DriveType CurrentDriveType => driveType;

        void ApplyBrakes()
        {
            if (GetComponent<CarSim.ADAS.ABS>() != null) return;

            float brake = BrakeInput * maxBrakeTorque;
            float hb    = HandbrakeOn ? handBrakeTorque : 0f;

            // 정차 근처에서 입력이 없으면 가벼운 홀드 브레이크를 걸어 경사면 롤백 방지
            bool noDriveInput = _pedals != null && _pedals.Throttle < 0.04f && _pedals.Brake < 0.05f;
            bool nearStop = _rb != null && _rb.linearVelocity.magnitude < idleHoldMaxSpeedMs;
            bool inDriveGear = _trans != null && _trans.CurrentGear != 0;
            if (noDriveInput && nearStop && inDriveGear)
            {
                brake = Mathf.Max(brake, idleHoldBrakeTorque);
            }

            // 앞바퀴 80%, 뒷바퀴 20% 배분 (앞이 뜨는 문제 해결)
            wheelFL.brakeTorque = brake * 0.8f;
            wheelFR.brakeTorque = brake * 0.8f;
            wheelRL.brakeTorque = brake * 0.2f + hb;
            wheelRR.brakeTorque = brake * 0.2f + hb;
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

        float ApplyDeadzone(float value, float deadzone)
        {
            float dz = Mathf.Clamp(deadzone, 0f, 0.95f);
            if (value <= dz) return 0f;
            return Mathf.Clamp01((value - dz) / (1f - dz));
        }

        float GetBrakeInputWithDeadzone()
            => ApplyDeadzone(_pedals.Brake, brakeDeadzone);

        void FilterShadowWarnings(string logString, string stackTrace, LogType type)
        {
            // Unity 그림자 경고 메시지 필터링
            if (logString.Contains("punctual light") || 
                logString.Contains("shadow atlas") || 
                logString.Contains("shadow maps"))
            {
                return; // 무시
            }
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= FilterShadowWarnings;
        }

        void OnValidate()
        {
            if (!Application.isPlaying) return;
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            ApplyCenterOfMass();
        }

        public enum DriveType { RWD, FWD, AWD }
    }
}
