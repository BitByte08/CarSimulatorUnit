using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSim.CAN
{
    /// <summary>
    /// CAN 0x300: 스위치 패널 (이그니션, 라이트 등)
    /// CAN 0x301: 기어 위치
    /// </summary>
    public class SwitchPanelHandler : MonoBehaviour
    {
        public SwitchFlags Switches    { get; private set; }
        public int         GearRequest { get; private set; } // 0=N, 1~6, -1=R

        // 편의 프로퍼티
        public bool IgnitionOn  => Switches.HasFlag(SwitchFlags.Ignition);
        /// <summary>E키를 누른 프레임에만 true (모멘터리) — 누른 채로 유지해도 재시동 루프 없음</summary>
        public bool EngineStart { get; private set; }
        public bool HeadLight   => Switches.HasFlag(SwitchFlags.HeadLight);
        public bool Hazard      => Switches.HasFlag(SwitchFlags.Hazard);
        public bool Horn        => Switches.HasFlag(SwitchFlags.Horn);

        [Header("시뮬레이션 모드")]
        [SerializeField] bool simMode = true;

        bool _prevEngineKey;

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
            CANBusManager.Instance.Register(CANID.SWITCH_STATUS, OnSwitchData);
            CANBusManager.Instance.Register(CANID.GEAR_STATUS,   OnGearData);
        }

        void OnSwitchData(byte[] data)
        {
            if (data.Length < 2) return;
            Switches = (SwitchFlags)BitConverter.ToUInt16(data, 0);
        }

        void OnGearData(byte[] data)
        {
            if (data.Length < 1) return;
            sbyte gear = (sbyte)data[0]; // -1=R, 0=N, 1~6
            GearRequest = gear;
        }

        void Update()
        {
            // 매 프레임 EngineStart 리셋 (모멘터리)
            EngineStart = false;

            if (!simMode) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            // 이그니션: I키 토글 -> 이제 VehicleController가 E키로 제어하므로 비활성화
            // if (kb.iKey.wasPressedThisFrame)
            //     Switches ^= SwitchFlags.Ignition;

            // 시동: E키 누른 프레임에만 ON
            if (kb.eKey.wasPressedThisFrame)
                EngineStart = true;

            // 기어: 숫자키 1~6, R키, N키
            if (kb.nKey.wasPressedThisFrame) GearRequest = 0;
            if (kb.rKey.wasPressedThisFrame) GearRequest = -1;
            if (kb.digit1Key.wasPressedThisFrame) GearRequest = 1;
            if (kb.digit2Key.wasPressedThisFrame) GearRequest = 2;
            if (kb.digit3Key.wasPressedThisFrame) GearRequest = 3;
            if (kb.digit4Key.wasPressedThisFrame) GearRequest = 4;
            if (kb.digit5Key.wasPressedThisFrame) GearRequest = 5;
            if (kb.digit6Key.wasPressedThisFrame) GearRequest = 6;
        }
    }
}
