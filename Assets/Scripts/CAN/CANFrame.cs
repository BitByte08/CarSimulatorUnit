using System;

namespace CarSim.CAN
{
    /// <summary>
    /// CAN 버스 단일 프레임 데이터 구조체
    /// </summary>
    public struct CANFrame
    {
        public uint Id;       // CAN ID (11bit: 0x000~0x7FF)
        public byte[] Data;   // 최대 8바이트
        public int Dlc;       // Data Length Code

        public CANFrame(uint id, byte[] data)
        {
            Id = id;
            Data = data;
            Dlc = data.Length;
        }

        public override string ToString()
            => $"[0x{Id:X3}] DLC={Dlc} Data={BitConverter.ToString(Data)}";
    }

    /// <summary>
    /// 프로젝트 전체 CAN ID 정의
    /// </summary>
    public static class CANID
    {
        // ── OpenFFBoard (스티어링) ──────────────────
        public const uint STEERING_ANGLE    = 0x100; // int16 각도 (÷100 = deg)
        public const uint STEERING_COLUMN   = 0x101; // uint16 스위치 비트필드 (방향지시등, 와이퍼 등)
        public const uint FFB_TORQUE_CMD    = 0x105; // int16 토크 명령 → OpenFFBoard

        // ── 페달 ECU (STM32) ──────────────────────
        public const uint PEDAL_STATUS      = 0x200; // [액셀u16][브레이크u16][클러치u16]
        public const uint PEDAL_RAW         = 0x201; // 원시 ADC 값 디버그용

        // ── 스위치 패널 (STM32) ───────────────────
        public const uint SWITCH_STATUS     = 0x300; // 비트필드: 이그니션,라이트,와이퍼...
        public const uint GEAR_STATUS       = 0x301; // 기어 위치 (0=N,1~6,7=R)

        // ── 라즈베리파이 인포테인먼트 ─────────────
        public const uint INFO_SPEED_RPM    = 0x400; // [속도u16 x10][RPM u16]
        public const uint INFO_WARNING      = 0x401; // 경고등 비트필드
        public const uint INFO_CMD          = 0x402; // 라즈베리파이→Unity 명령

        // ── Unity → 전체 브로드캐스트 ─────────────
        public const uint VEHICLE_STATE     = 0x500; // [속도][RPM][기어][ABS][TCS]
        public const uint ENGINE_STATE      = 0x501; // [수온][유압][연료]
    }

    /// <summary>
    /// 스위치 패널 비트 정의
    /// </summary>
    [Flags]
    public enum SwitchFlags : ushort
    {
        None        = 0,
        Ignition    = 1 << 0,  // 이그니션 ON
        Engine      = 1 << 1,  // 시동
        HeadLight   = 1 << 2,  // 헤드라이트
        HighBeam    = 1 << 3,  // 상향등
        Hazard      = 1 << 4,  // 비상등
        WiperSlow   = 1 << 5,  // 와이퍼 저속
        WiperFast   = 1 << 6,  // 와이퍼 고속
        Horn        = 1 << 7,  // 경적
        TurnLeft    = 1 << 8,  // 좌 방향지시등
        TurnRight   = 1 << 9,  // 우 방향지시등
    }
}
