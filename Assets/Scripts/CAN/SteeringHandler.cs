using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSim.CAN
{
    /// <summary>
    /// 스티어링 휠 CAN 통신 핸들러
    ///
    /// 수신 (보드 → PC):
    ///   CAN 0x100: Steering Angle (int16, 각도 × 10)
    ///   CAN 0x101: Switches (uint16 비트필드 - 방향지시등, 와이퍼 등)
    ///
    /// 송신 (PC → 보드):
    ///   CAN 0x105: FFB Torque (int16, -32767 ~ +32767)
    /// </summary>
    public class SteeringHandler : MonoBehaviour
    {
        /// <summary>현재 스티어링 각도 (도), 좌: 음수, 우: 양수</summary>
        public float SteeringAngle { get; private set; }

        /// <summary>스티어링 컬럼 스위치 상태 (방향지시등, 와이퍼 등)</summary>
        public SwitchFlags ColumnSwitches { get; private set; }

        [Header("스티어링 범위")]
        [SerializeField] float maxAngle = 450f;  // 휠 최대 회전각 (±450도)

        [Header("시뮬레이션 모드")]
        [SerializeField] bool simMode = true;

        // 편의 프로퍼티
        public bool TurnLeft => ColumnSwitches.HasFlag(SwitchFlags.TurnLeft);
        public bool TurnRight => ColumnSwitches.HasFlag(SwitchFlags.TurnRight);
        public bool WiperSlow => ColumnSwitches.HasFlag(SwitchFlags.WiperSlow);
        public bool WiperFast => ColumnSwitches.HasFlag(SwitchFlags.WiperFast);

        void Start()
        {
            CANBusManager.Instance.Register(CANID.STEERING_ANGLE, OnSteeringAngleData);
            CANBusManager.Instance.Register(CANID.STEERING_COLUMN, OnSwitchesData);
        }

        void OnSteeringAngleData(byte[] data)
        {
            if (data.Length < 2) return;
            
            short raw = BitConverter.ToInt16(data, 0);
            SteeringAngle = raw / 10f;
        }

        /// <summary>
        /// CAN 0x101: Switches 수신
        /// 데이터 형식: uint16, bit0=좌회전, bit1=우회전
        /// </summary>
        void OnSwitchesData(byte[] data)
        {
            if (data.Length < 2) return;
            ushort raw = BitConverter.ToUInt16(data, 0);
            
            ColumnSwitches = SwitchFlags.None;
            if ((raw & 0x01) != 0) ColumnSwitches |= SwitchFlags.TurnLeft;
            if ((raw & 0x02) != 0) ColumnSwitches |= SwitchFlags.TurnRight;
        }

        void Update()
        {
            if (!simMode) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            // 스티어링 각도 시뮬레이션
            float h = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                    - (kb.aKey.isPressed || kb.leftArrowKey.isPressed  ? 1f : 0f);
            float target = h * maxAngle;
            SteeringAngle = Mathf.MoveTowards(SteeringAngle, target, Time.deltaTime * 300f);

            // 스위치 시뮬레이션 (토글)
            if (kb.zKey.wasPressedThisFrame) ColumnSwitches ^= SwitchFlags.TurnLeft;
            if (kb.xKey.wasPressedThisFrame) ColumnSwitches ^= SwitchFlags.TurnRight;
            if (kb.fKey.wasPressedThisFrame) ToggleWiper(WiperSpeed.Slow);
            if (kb.gKey.wasPressedThisFrame) ToggleWiper(WiperSpeed.Fast);
        }

        enum WiperSpeed { Slow, Fast }

        void ToggleWiper(WiperSpeed speed)
        {
            SwitchFlags target = speed == WiperSpeed.Slow
                ? SwitchFlags.WiperSlow
                : SwitchFlags.WiperFast;
            SwitchFlags other = speed == WiperSpeed.Slow
                ? SwitchFlags.WiperFast
                : SwitchFlags.WiperSlow;

            bool wasOn = ColumnSwitches.HasFlag(target);
            ColumnSwitches &= ~other;           // 반대 속도 항상 끔
            if (wasOn) ColumnSwitches &= ~target;
            else       ColumnSwitches |=  target;
        }

        /// <summary>
        /// CAN 0x105: FFB 토크를 스티어링 휠로 송신
        /// </summary>
        /// <param name="normalizedTorque">정규화된 토크 (-1.0 ~ 1.0)</param>
        public void SendFFBTorque(float normalizedTorque)
        {
            short torque = (short)(Mathf.Clamp(normalizedTorque, -1f, 1f) * 32767f);
            CANBusManager.Instance.Send(CANID.FFB_TORQUE_CMD, BitConverter.GetBytes(torque));
        }

        /// <summary>
        /// FFB 토크를 직접 값으로 송신 (-32767 ~ 32767)
        /// </summary>
        public void SendFFBTorqueRaw(short torque)
        {
            CANBusManager.Instance.Send(CANID.FFB_TORQUE_CMD, BitConverter.GetBytes(torque));
        }
    }
}
