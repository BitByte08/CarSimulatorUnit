using System;
using UnityEngine;
using CarSim.CAN;

namespace CarSim.UI
{
    /// <summary>
    /// 라즈베리파이 인포테인먼트 양방향 CAN 브릿지
    /// 수신 (0x402): 라즈베리파이 → Unity (에어컨 온도, 음악 볼륨 등)
    /// 송신은 Dashboard.cs에서 처리 (0x400, 0x401)
    /// </summary>
    public class InfotainmentBridge : MonoBehaviour
    {
        // 라즈베리파이로부터 수신된 상태
        public float ACTemperature   { get; private set; } = 22f;   // °C
        public int   ACFanLevel      { get; private set; } = 2;     // 0~5
        public bool  ACOn            { get; private set; }
        public int   MediaVolume     { get; private set; } = 50;    // 0~100
        public bool  MediaPlaying    { get; private set; }

        void Start()
        {
            CANBusManager.Instance.Register(CANID.INFO_CMD, OnInfoCommand);
        }

        void OnInfoCommand(byte[] data)
        {
            if (data.Length < 4) return;

            // 포맷: [커맨드 u8][파라미터 u8][값 u16]
            byte  cmd   = data[0];
            byte  param = data[1];
            ushort val  = BitConverter.ToUInt16(data, 2);

            switch (cmd)
            {
                case 0x01: // 에어컨
                    ACOn          = param == 1;
                    ACTemperature = val * 0.1f;
                    break;
                case 0x02: // 팬
                    ACFanLevel = Mathf.Clamp(param, 0, 5);
                    break;
                case 0x03: // 미디어 볼륨
                    MediaVolume  = Mathf.Clamp(val, 0, 100);
                    AudioListener.volume = MediaVolume / 100f;
                    break;
                case 0x04: // 미디어 재생/정지
                    MediaPlaying = param == 1;
                    break;
            }

            Debug.Log($"[Infotainment] CMD=0x{cmd:X2} PARAM={param} VAL={val}");
        }
    }
}
