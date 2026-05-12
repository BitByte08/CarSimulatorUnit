using UnityEngine;
using CarSim.CAN;

namespace CarSim.Vehicle
{
    /// <summary>
    /// 차량 위치와 방향을 CAN 버스로 브로드캐스트.
    ///
    /// 0x600 POSITION (8 bytes, Big-Endian):
    ///   [0-3] World X × 100  (int32 BE)  cm 단위 정수
    ///   [4-7] World Z × 100  (int32 BE)  cm 단위 정수
    ///
    /// 0x601 HEADING (2 bytes, Big-Endian):
    ///   [0-1] EulerAngles.y × 10  (uint16 BE)  0~3599 = 0.0°~359.9°
    ///         0 = North (+Z), 90 = East (+X), 180 = South, 270 = West
    ///
    /// 전송 주기: 10 Hz
    /// </summary>
    public class PositionBroadcast : MonoBehaviour
    {
        [Header("전송 주기 (초)")]
        [SerializeField] float positionPeriod = 0.1f;  // 10 Hz

        float _tPosition;

        void Update()
        {
            float now = Time.time;
            if (now - _tPosition >= positionPeriod)
            {
                _tPosition = now;
                SendPosition();
                SendHeading();
            }
        }

        void SendPosition()
        {
            // Unity 좌표: X = East, Z = North (1 unit = 1 meter)
            int xi = Mathf.RoundToInt(transform.position.x * 100f);
            int zi = Mathf.RoundToInt(transform.position.z * 100f);

            // Big-Endian int32 × 2 = 8 bytes
            CANBusManager.Instance.Send(CANID.POSITION, new byte[]
            {
                (byte)((xi >> 24) & 0xFF), (byte)((xi >> 16) & 0xFF),
                (byte)((xi >>  8) & 0xFF), (byte)( xi        & 0xFF),
                (byte)((zi >> 24) & 0xFF), (byte)((zi >> 16) & 0xFF),
                (byte)((zi >>  8) & 0xFF), (byte)( zi        & 0xFF),
            });
        }

        void SendHeading()
        {
            // EulerAngles.y: 0=+Z(North), 90=+X(East) — Unity 좌수계 Y-up
            float heading = Mathf.Repeat(transform.eulerAngles.y, 360f);
            ushort h = (ushort)Mathf.RoundToInt(heading * 10f);
            // Clamp: Mathf.Repeat 이미 [0,360) 보장, ×10 → max 3599
            if (h >= 3600) h = 3599;

            CANBusManager.Instance.Send(CANID.HEADING, new byte[]
            {
                (byte)(h >> 8), (byte)(h & 0xFF),
            });
        }
    }
}
