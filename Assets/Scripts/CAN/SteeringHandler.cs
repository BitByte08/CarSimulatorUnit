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
            CANBusManager.Instance.Register(0x102, OnFFBDiag);
            CANBusManager.Instance.Register(0x103, OnCANESR);
        }

        void OnFFBDiag(byte[] data)
        {
            if (data.Length < 8) return;
            ushort rxCount = BitConverter.ToUInt16(data, 0);
            short lastRaw  = BitConverter.ToInt16(data, 2);
            ushort lastDuty = BitConverter.ToUInt16(data, 4);
            short deg10    = BitConverter.ToInt16(data, 6);
            Debug.Log($"[FFB_DIAG] rxCount={rxCount} lastRaw={lastRaw} duty={lastDuty} deg={deg10/10f:F1}°");
        }

        void OnCANESR(byte[] data)
        {
            if (data.Length < 8) return;
            byte esrLo   = data[0];  // EWG/EPV/BOFF/LEC
            byte tec     = data[1];
            byte rec     = data[2];
            byte rf0r    = data[3];  // RX FIFO0: FMP0[1:0], FULL, FOVR
            byte tsrHi   = data[4];  // TX status
            byte pb8     = data[5];  // PB8 pin level (CAN_RX)
            byte btrLo   = data[6];  // BTR prescaler
            byte btrHi   = data[7];  // BTR TS1/TS2/SJW

            int fmp0 = rf0r & 0x03;  // pending RX messages in FIFO0
            bool full = (rf0r & 0x08) != 0;
            bool fovr = (rf0r & 0x10) != 0;
            bool boff = (esrLo & 0x04) != 0;
            int lec = (esrLo >> 4) & 0x07;

            string[] lecNames = {"None","Bit","Form","Stuff","CRC","Ack","Misc","None"};
            Debug.Log($"[CAN_ESR] TEC={tec} REC={rec} LEC={lecNames[lec]} BOF={boff} FMP0={fmp0} FULL={full} FOVR={fovr} PB8={pb8} BTR=0x{btrHi:X2}{btrLo:X2}");
        }

        void OnSteeringAngleData(byte[] data)
        {
            if (simMode) return;
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

        /// <summary>
        /// CAN 0x106: 엔코더 영점을 현재 위치로 설정 (핸들을 센터에 두고 H키)
        /// </summary>
        public void ZeroEncoder()
        {
            CANBusManager.Instance.Send(CANID.ENC_ZERO_CMD, new byte[1]);
            Debug.Log("[Steering] 엔코더 영점 명령 전송 (0x106)");
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // H키: 엔코더 영점 (simMode 무관)
            if (kb.hKey.wasPressedThisFrame) ZeroEncoder();

            if (!simMode) return;

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
