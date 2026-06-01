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

        // 시뮬레이션 여부는 CANBusManager 단일 소스에서 받아온다 (키보드 vs 하드웨어)
        bool Sim => CANBusManager.Instance == null || CANBusManager.Instance.SimulationMode;

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
            CANBusManager.Instance.Register(CANID.SWITCH_STATUS,   OnSwitchData);
            CANBusManager.Instance.Register(CANID.GEAR_STATUS,     OnGearData);
            CANBusManager.Instance.Register(CANID.STEERING_COLUMN, OnColumnData);
        }

        void OnSwitchData(byte[] data)
        {
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
            if (data.Length < 1) return;
            GearRequest = (sbyte)data[0]; // -1=R, 0=N, 1~6
        }

        void OnColumnData(byte[] data)
        {
            if (data.Length < 2) return;
            ColumnSwitches = (SwitchFlags)BitConverter.ToUInt16(data, 0);
        }

        void Update()
        {
            // 매 프레임 EngineStart 리셋 (모멘터리)
            EngineStart = false;

            if (!Sim) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            // 시동: E키 누른 프레임에만 ON (CAN rising edge는 OnSwitchData에서 처리)
            if (kb.eKey.wasPressedThisFrame)
                EngineStart = true;

            // ── 라이트 ──────────────────────────────────────
            if (kb.hKey.wasPressedThisFrame) Switches ^= SwitchFlags.HeadLight;
            if (kb.jKey.wasPressedThisFrame) Switches ^= SwitchFlags.HighBeam;
            if (kb.bKey.wasPressedThisFrame) Switches ^= SwitchFlags.Hazard;

            // ── 방향지시등 (좌/우 상호 배타) ─────────────────
            if (kb.zKey.wasPressedThisFrame) ToggleTurnSignal(SwitchFlags.TurnLeft);
            if (kb.xKey.wasPressedThisFrame) ToggleTurnSignal(SwitchFlags.TurnRight);

            // ── 와이퍼 (F=저속, G=고속, 상호 배타) ──────────
            if (kb.fKey.wasPressedThisFrame) ToggleWiper(WiperSpeed.Slow);
            if (kb.gKey.wasPressedThisFrame) ToggleWiper(WiperSpeed.Fast);

            // ── 기어: 숫자키 1~6, R키, N키 ──────────────────
            if (kb.nKey.wasPressedThisFrame)      GearRequest = 0;
            if (kb.rKey.wasPressedThisFrame)      GearRequest = -1;
            if (kb.digit1Key.wasPressedThisFrame) GearRequest = 1;
            if (kb.digit2Key.wasPressedThisFrame) GearRequest = 2;
            if (kb.digit3Key.wasPressedThisFrame) GearRequest = 3;
            if (kb.digit4Key.wasPressedThisFrame) GearRequest = 4;
            if (kb.digit5Key.wasPressedThisFrame) GearRequest = 5;
            if (kb.digit6Key.wasPressedThisFrame) GearRequest = 6;
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
    }
}
