using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSim.CAN
{
    /// <summary>
    /// CAN 0x300: 스위치 패널 (이그니션, 라이트 등)
    /// CAN 0x301: 기어 위치
    /// CAN 0x101: 스티어링 컬럼 스위치 (방향지시등, 와이퍼) ← OpenFFBoard 송신
    /// </summary>
    public class SwitchPanelHandler : MonoBehaviour
    {
        /// <summary>메인 스위치 패널 (CAN 0x300)</summary>
        public SwitchFlags Switches        { get; private set; }
        /// <summary>스티어링 컬럼 스위치 (CAN 0x101) — 방향지시등·와이퍼 전용</summary>
        public SwitchFlags ColumnSwitches  { get; private set; }

        public int GearRequest { get; private set; } // 0=N, 1~6, -1=R

        // 편의 프로퍼티 — 패널 OR 스티어링 컬럼 둘 중 하나라도 켜지면 true
        public bool IgnitionOn  => Switches.HasFlag(SwitchFlags.Ignition);
        /// <summary>E키 또는 CAN Engine 비트 rising edge 프레임에만 true (모멘터리)</summary>
        public bool EngineStart { get; private set; }

        bool _prevEngineCANBit;
        public bool HeadLight   => Switches.HasFlag(SwitchFlags.HeadLight);
        public bool HighBeam    => Switches.HasFlag(SwitchFlags.HighBeam);
        public bool Hazard      => Combined.HasFlag(SwitchFlags.Hazard);
        public bool Horn        => Switches.HasFlag(SwitchFlags.Horn);
        public bool TurnLeft    => Combined.HasFlag(SwitchFlags.TurnLeft);
        public bool TurnRight   => Combined.HasFlag(SwitchFlags.TurnRight);
        public bool WiperSlow   => Combined.HasFlag(SwitchFlags.WiperSlow);
        public bool WiperFast   => Combined.HasFlag(SwitchFlags.WiperFast);

        /// <summary>두 패널 플래그를 합산한 값</summary>
        SwitchFlags Combined => Switches | ColumnSwitches;

        [Header("유닛 모드")]
        [Tooltip("ON=CAN 기어 하드웨어, OFF=키보드")]
        public bool gearCanMode = true;
        [Tooltip("ON=CAN 스위치 하드웨어, OFF=키보드")]
        public bool switchCanMode = true;

        const string PrefGear   = "unit.shifter";
        const string PrefSwitch = "unit.entertain";

        [Header("방향지시등 자동 취소")]
        [Tooltip("이 핸들각(도) 이상 꺾이면 자동취소 무장")]
        [SerializeField] float autoCancelArmAngle = 60f;
        [Tooltip("이 핸들각 이하로 센터 복귀 시 취소")]
        [SerializeField] float autoCancelReturnAngle = 15f;

        SteeringHandler _steering;
        bool _leftArmed, _rightArmed;

        // VehicleController가 전원 상태를 제어할 수 있도록 public 메서드 추가
        public void SetIgnition(bool on)
        {
            if (on)
                Switches |= SwitchFlags.Ignition;
            else
                Switches &= ~SwitchFlags.Ignition;
        }

        void Start()
        {
            if (PlayerPrefs.HasKey(PrefGear))
                gearCanMode = PlayerPrefs.GetInt(PrefGear) != 0;
            if (PlayerPrefs.HasKey(PrefSwitch))
                switchCanMode = PlayerPrefs.GetInt(PrefSwitch) != 0;

            CANBusManager.Instance.Register(CANID.SWITCH_STATUS,   OnSwitchData);
            CANBusManager.Instance.Register(CANID.GEAR_STATUS,     OnGearData);
            CANBusManager.Instance.Register(CANID.STEERING_COLUMN, OnColumnData);

            _steering = FindObjectOfType<SteeringHandler>();
        }

        void OnSwitchData(byte[] data)
        {
            if (!switchCanMode) return;
            if (data.Length < 2) return;
            var newFlags = (SwitchFlags)BitConverter.ToUInt16(data, 0);

            // Engine 비트 rising edge → EngineStart 트리거 (엔터테인먼트 버튼 연동)
            bool newEngine = newFlags.HasFlag(SwitchFlags.Engine);
            if (newEngine && !_prevEngineCANBit)
                EngineStart = true;
            _prevEngineCANBit = newEngine;

            Switches = newFlags;
        }

        void OnGearData(byte[] data)
        {
            if (!gearCanMode) return;
            if (data.Length < 1) return;
            GearRequest = (sbyte)data[0]; // -1=R, 0=N, 1~6
        }

        void OnColumnData(byte[] data)
        {
            if (!switchCanMode) return;
            if (data.Length < 2) return;
            ColumnSwitches = (SwitchFlags)BitConverter.ToUInt16(data, 0);
        }

        /// <summary>EngineStart 플래그를 읽고 소비 (한 번만 true 반환).</summary>
        public bool ConsumeEngineStart()
        {
            bool val = EngineStart;
            EngineStart = false;
            return val;
        }

        void Update()
        {
            UpdateTurnSignalAutoCancel();

            var kb = Keyboard.current;
            if (kb == null) return;

            // ── 스위치 시뮬레이션 (switchCanMode=false 일 때 키보드) ──
            if (!switchCanMode)
            {
                // 시동: E키 누른 프레임에만 ON
                if (kb.eKey.wasPressedThisFrame)
                    EngineStart = true;

                // 라이트
                if (kb.hKey.wasPressedThisFrame) Switches ^= SwitchFlags.HeadLight;
                if (kb.jKey.wasPressedThisFrame) Switches ^= SwitchFlags.HighBeam;
                if (kb.bKey.wasPressedThisFrame) Switches ^= SwitchFlags.Hazard;

                // 방향지시등 (좌/우 상호 배타)
                if (kb.zKey.wasPressedThisFrame) ToggleTurnSignal(SwitchFlags.TurnLeft);
                if (kb.xKey.wasPressedThisFrame) ToggleTurnSignal(SwitchFlags.TurnRight);

                // 와이퍼 (F=저속, G=고속, 상호 배타)
                if (kb.fKey.wasPressedThisFrame) ToggleWiper(WiperSpeed.Slow);
                if (kb.gKey.wasPressedThisFrame) ToggleWiper(WiperSpeed.Fast);
            }

            // ── 기어 시뮬레이션 (gearCanMode=false 일 때 키보드) ──
            if (!gearCanMode)
            {
                if (kb.nKey.wasPressedThisFrame)      GearRequest = 0;
                if (kb.rKey.wasPressedThisFrame)      GearRequest = -1;
                if (kb.digit1Key.wasPressedThisFrame) GearRequest = 1;
                if (kb.digit2Key.wasPressedThisFrame) GearRequest = 2;
                if (kb.digit3Key.wasPressedThisFrame) GearRequest = 3;
                if (kb.digit4Key.wasPressedThisFrame) GearRequest = 4;
                if (kb.digit5Key.wasPressedThisFrame) GearRequest = 5;
                if (kb.digit6Key.wasPressedThisFrame) GearRequest = 6;
            }
        }

        void ToggleTurnSignal(SwitchFlags target)
        {
            SwitchFlags other = target == SwitchFlags.TurnLeft
                ? SwitchFlags.TurnRight
                : SwitchFlags.TurnLeft;

            bool wasOn = ColumnSwitches.HasFlag(target);
            ColumnSwitches &= ~other;
            if (wasOn) ColumnSwitches &= ~target;
            else       ColumnSwitches |=  target;
        }

        enum WiperSpeed { Slow, Fast }

        void ToggleWiper(WiperSpeed speed)
        {
            SwitchFlags target = speed == WiperSpeed.Slow
                ? SwitchFlags.WiperSlow
                : SwitchFlags.WiperFast;
            SwitchFlags other  = speed == WiperSpeed.Slow
                ? SwitchFlags.WiperFast
                : SwitchFlags.WiperSlow;

            bool wasOn = ColumnSwitches.HasFlag(target);
            ColumnSwitches &= ~other;           // 반대 속도 항상 끔
            if (wasOn) ColumnSwitches &= ~target;
            else       ColumnSwitches |=  target;
        }

        // 핸들을 꺾었다(arm) 센터로 복귀하면(return) 방향지시등 자동 취소 — 실차 캔슬 캠 모사
        void UpdateTurnSignalAutoCancel()
        {
            if (_steering == null) return;
            float angle = _steering.SteeringAngle;  // 좌: 음수, 우: 양수

            if (Combined.HasFlag(SwitchFlags.TurnLeft))
            {
                if (angle < -autoCancelArmAngle) _leftArmed = true;
                else if (_leftArmed && angle > -autoCancelReturnAngle) { ClearTurnSignal(SwitchFlags.TurnLeft); _leftArmed = false; }
            }
            else _leftArmed = false;

            if (Combined.HasFlag(SwitchFlags.TurnRight))
            {
                if (angle > autoCancelArmAngle) _rightArmed = true;
                else if (_rightArmed && angle < autoCancelReturnAngle) { ClearTurnSignal(SwitchFlags.TurnRight); _rightArmed = false; }
            }
            else _rightArmed = false;
        }

        void ClearTurnSignal(SwitchFlags dir)
        {
            Switches       &= ~dir;
            ColumnSwitches &= ~dir;
        }
    }
}
