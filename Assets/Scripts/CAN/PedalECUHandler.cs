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

        // 원시 ADC 값 (켈리브레이션 확인용)
        public ushort RawThrottle { get; private set; }
        public ushort RawBrake    { get; private set; }
        public ushort RawClutch   { get; private set; }

        [Header("페달 캘리브레이션 (ADC 원시값)")]
        [SerializeField] ushort throttleMin = 0,  throttleMax = 4095;
        [SerializeField] ushort brakeMin    = 0,  brakeMax    = 4095;
        [SerializeField] ushort clutchMin   = 0,  clutchMax   = 4095;

        [SerializeField] float clutchSmoothSpeed = 2f; // 클러치 변화 속도 (초당)

        [Header("유닛 모드")]
        [Tooltip("ON=CAN 하드웨어, OFF=키보드 시뮬레이션")]
        public bool useCanMode = true;

        const string PrefUnit = "unit.pedal";

        void Start()
        {
            if (PlayerPrefs.HasKey(PrefUnit))
                useCanMode = PlayerPrefs.GetInt(PrefUnit) != 0;

            CANBusManager.Instance.Register(CANID.PEDAL_STATUS, OnPedalData);
        }

        void OnPedalData(byte[] data)
        {
            if (!useCanMode) return;
            if (data.Length < 6) return;
            RawThrottle = BitConverter.ToUInt16(data, 0);
            RawBrake    = BitConverter.ToUInt16(data, 2);
            RawClutch   = BitConverter.ToUInt16(data, 4);

            Throttle = Normalize(RawThrottle, throttleMin, throttleMax);
            Brake    = Normalize(RawBrake,    brakeMin,    brakeMax);
            Clutch   = Normalize(RawClutch,   clutchMin,   clutchMax);
        }

        void Update()
        {
            if (useCanMode) return;

            // 키보드 시뮬레이션
            var kb = Keyboard.current;
            if (kb == null) return;

            float vertical = (kb.wKey.isPressed || kb.upArrowKey.isPressed   ? 0.5f : 0f)
                           - (kb.sKey.isPressed || kb.downArrowKey.isPressed  ? 1f : 0f);
            Throttle = vertical > 0f ?  vertical : 0f;
            Brake    = vertical < 0f ? -vertical : 0f;

            // 클러치: Left Shift(완전분리), Z(반클러치) — 부드럽게 변화
            float clutchTarget = 1f;
            if (kb.leftShiftKey.isPressed) clutchTarget = 0f;
            else if (kb.zKey.isPressed) clutchTarget = 0.45f;
            Clutch = Mathf.MoveTowards(Clutch, clutchTarget, Time.deltaTime * clutchSmoothSpeed);
        }

        static float Normalize(ushort val, ushort min, ushort max)
            => Mathf.Clamp01((float)(val - min) / (max - min));
    }
}
