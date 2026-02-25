using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSim.CAN
{
    /// <summary>
    /// CAN 0x200: 페달 ECU (STM32) 수신
    /// 데이터 포맷: [액셀 u16][브레이크 u16][클러치 u16] → 각 0~65535
    /// </summary>
    public class PedalECUHandler : MonoBehaviour
    {
        // 0.0 ~ 1.0 정규화된 페달 값
        public float Throttle  { get; private set; }
        public float Brake     { get; private set; }
        public float Clutch    { get; private set; }  // 0=완전분리, 1=완전접속

        [Header("페달 캘리브레이션 (ADC 원시값)")]
        [SerializeField] ushort throttleMin = 0,  throttleMax = 65535;
        [SerializeField] ushort brakeMin    = 0,  brakeMax    = 65535;
        [SerializeField] ushort clutchMin   = 0,  clutchMax   = 65535;

        [Header("시뮬레이션 모드 (키보드 입력)")]
        [SerializeField] bool simMode = true;

        void Start()
        {
            CANBusManager.Instance.Register(CANID.PEDAL_STATUS, OnPedalData);
        }

        void OnPedalData(byte[] data)
        {
            if (data.Length < 6) return;
            ushort rawThrottle = BitConverter.ToUInt16(data, 0);
            ushort rawBrake    = BitConverter.ToUInt16(data, 2);
            ushort rawClutch   = BitConverter.ToUInt16(data, 4);

            Throttle = Normalize(rawThrottle, throttleMin, throttleMax);
            Brake    = Normalize(rawBrake,    brakeMin,    brakeMax);
            Clutch   = Normalize(rawClutch,   clutchMin,   clutchMax);
        }

        void Update()
        {
            if (!simMode) return;

            // 키보드 시뮬레이션
            var kb = Keyboard.current;
            if (kb == null) return;

            float vertical = (kb.wKey.isPressed || kb.upArrowKey.isPressed   ? 1f : 0f)
                           - (kb.sKey.isPressed || kb.downArrowKey.isPressed  ? 1f : 0f);
            Throttle = vertical > 0f ?  vertical : 0f;
            Brake    = vertical < 0f ? -vertical : 0f;

            // 클러치: Left Shift — 0.5초에 걸쳐 서서히 변화 (반클러치 흉내)
            float clutchTarget = kb.leftShiftKey.isPressed ? 0f : 1f;
            Clutch = Mathf.MoveTowards(Clutch, clutchTarget, Time.deltaTime * 2f);
        }

        static float Normalize(ushort val, ushort min, ushort max)
            => Mathf.Clamp01((float)(val - min) / (max - min));
    }
}
