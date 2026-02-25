using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarSim.CAN
{
    /// <summary>
    /// CAN 0x100: OpenFFBoard 스티어링 각도 수신
    /// CAN 0x105: FFB 토크 명령 송신 → OpenFFBoard
    /// </summary>
    public class SteeringHandler : MonoBehaviour
    {
        /// <summary>현재 스티어링 각도 (도), 좌: 음수, 우: 양수</summary>
        public float SteeringAngle { get; private set; }

        [Header("스티어링 범위")]
        [SerializeField] float maxAngle = 450f;  // 휠 최대 회전각 (±450도)

        [Header("시뮬레이션 모드")]
        [SerializeField] bool simMode = true;

        void Start()
        {
            CANBusManager.Instance.Register(CANID.STEERING_ANGLE, OnSteeringData);
        }

        void OnSteeringData(byte[] data)
        {
            if (data.Length < 2) return;
            // int16: ÷100 = 실제 각도 (도)
            short raw = BitConverter.ToInt16(data, 0);
            SteeringAngle = raw / 100f;
        }

        void Update()
        {
            if (!simMode) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            float h = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                    - (kb.aKey.isPressed || kb.leftArrowKey.isPressed  ? 1f : 0f);
            float target = h * maxAngle;
            SteeringAngle = Mathf.MoveTowards(SteeringAngle, target, Time.deltaTime * 300f);
        }

        /// <summary>FFB 토크를 OpenFFBoard로 송신 (-1.0 ~ 1.0)</summary>
        public void SendFFBTorque(float normalizedTorque)
        {
            short torque = (short)(Mathf.Clamp(normalizedTorque, -1f, 1f) * 32767f);
            CANBusManager.Instance.Send(CANID.FFB_TORQUE_CMD, BitConverter.GetBytes(torque));
        }
    }
}
