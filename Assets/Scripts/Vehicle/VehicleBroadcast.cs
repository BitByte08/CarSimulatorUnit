using System;
using UnityEngine;
using CarSim.CAN;
using CarSim.ADAS;

namespace CarSim.Vehicle
{
    /// <summary>
    /// Unity 차량 물리 상태를 Usb2Can을 통해 CAN 버스로 브로드캐스트.
    ///
    /// 송신 프레임:
    ///   0x400 INFO_SPEED_RPM  Big-Endian  20 Hz  → EntertainmentCluster (구 포맷 호환)
    ///   0x500 VEHICLE_STATE   LE          20 Hz  → 전체 브로드캐스트
    ///   0x501 ENGINE_STATE    LE           5 Hz  → 클러스터 수온/연료
    ///   0x401 INFO_WARNING    LE           2 Hz  → 경고등
    ///
    /// 0x400 프레임 포맷 (Big-Endian):
    ///   [0-1] speed × 10  (uint16)   km/h × 10
    ///   [2-3] RPM         (uint16)
    ///
    /// 0x500 프레임 포맷 (Big-Endian speed/RPM, 8 bytes):
    ///   [0-1] speed × 10  (uint16 BE)
    ///   [2-3] RPM         (uint16 BE)
    ///   [4]   gear        (uint8: 0=N, 1~6, 0xFF=R)  ← TransmissionShifterUnit 포맷
    ///   [5]   flags       (bit0=ABS active, bit1=TCS active)
    ///   [6-7] reserved
    ///
    /// 0x501 프레임 포맷 (3 bytes, ClusterModel 파싱 기준):
    ///   [0]   coolant temp (uint8, °C)
    ///   [1]   oil pressure (uint8, 0-100 %)
    ///   [2]   fuel %       (uint8, 0-100)
    ///
    /// 0x600 POSITION (8 bytes, Big-Endian):
    ///   [0-3] World X × 100  (int32 BE)  cm 단위 정수
    ///   [4-7] World Z × 100  (int32 BE)  cm 단위 정수
    ///
    /// 0x601 HEADING (2 bytes, Big-Endian):
    ///   [0-1] EulerAngles.y × 10  (uint16 BE)  0~3599 = 0.0°~359.9°
    ///         0 = North (+Z), 90 = East (+X)
    ///
    /// 전송 주기: position/heading 10 Hz
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    [RequireComponent(typeof(Engine))]
    [RequireComponent(typeof(ManualTransmission))]
    public class VehicleBroadcast : MonoBehaviour
    {
        [Header("전송 주기 (초)")]
        [SerializeField] float speedRpmPeriod    = 0.05f;   // 0x400, 0x500  20 Hz
        [SerializeField] float engineStatePeriod = 0.20f;   // 0x501          5 Hz
        [SerializeField] float warningPeriod     = 0.50f;   // 0x401          2 Hz
        [SerializeField] float positionPeriod    = 0.10f;   // 0x600, 0x601  10 Hz

        [Header("연료 시뮬레이션")]
        [SerializeField] float fuelCapacityLiters  = 50f;
        [SerializeField] float fuelConsumptionLpH  = 8f;    // 전부하 기준 L/h

        [Header("수온 시뮬레이션")]
        [SerializeField] float coolantHeatCoeff  = 0.008f;  // (°C/s) per RPM unit
        [SerializeField] float coolantCoolRate   = 4f;      // °C/s (엔진 정지 시)
        [SerializeField] float coolantMaxTemp    = 95f;     // 정상 최고 온도

        VehicleController  _vc;
        Engine             _engine;
        ManualTransmission _trans;
        ABS                _abs;

        float _fuelLiters;
        float _coolantTemp = 20f;

        float _tSpeed;
        float _tEngine;
        float _tWarning;
        float _tPosition;

        // ── Unity 생명주기 ───────────────────────────────────────────────────

        void Awake()
        {
            _vc     = GetComponent<VehicleController>();
            _engine = GetComponent<Engine>();
            _trans  = GetComponent<ManualTransmission>();
            _abs    = GetComponent<ABS>();

            _fuelLiters = fuelCapacityLiters;

            Debug.Log($"[VehicleBroadcast] Awake on '{gameObject.name}' — engine={(_engine != null ? _engine.gameObject.name : "NULL")}");
        }

        void FixedUpdate()
        {
            UpdateSimulations(Time.fixedDeltaTime);
        }

        void Update()
        {
            float now = Time.time;

            if (now - _tSpeed >= speedRpmPeriod)
            {
                _tSpeed = now;
                SendSpeedRpm();
                SendVehicleState();
            }
            if (now - _tEngine >= engineStatePeriod)
            {
                _tEngine = now;
                SendEngineState();
            }
            if (now - _tWarning >= warningPeriod)
            {
                _tWarning = now;
                SendWarnings();
            }
            if (now - _tPosition >= positionPeriod)
            {
                _tPosition = now;
                SendPosition();
                SendHeading();
            }
        }

        // ── 시뮬레이션 ───────────────────────────────────────────────────────

        void UpdateSimulations(float dt)
        {
            if (_engine.IsRunning)
            {
                float loadFactor = Mathf.Clamp01(_engine.RPM / 6500f);
                _fuelLiters -= fuelConsumptionLpH * loadFactor * dt / 3600f;
                _fuelLiters  = Mathf.Max(_fuelLiters, 0f);

                float heatRate = _engine.RPM * coolantHeatCoeff * dt;
                _coolantTemp = Mathf.Min(_coolantTemp + heatRate, coolantMaxTemp);
            }
            else
            {
                _coolantTemp = Mathf.MoveTowards(_coolantTemp, 20f, coolantCoolRate * dt);
            }
        }

        // ── CAN 송신 헬퍼 ────────────────────────────────────────────────────

        void SendSpeedRpm()
        {
            ushort speed = (ushort)Mathf.Clamp(_vc.SpeedKph * 10f, 0f, 65535f);
            ushort rpm   = (ushort)Mathf.Clamp(_engine.RPM,         0f, 65535f);

            Debug.Log($"[VehicleBroadcast] 0x400 TX  speed={_vc.SpeedKph:F1}km/h  rpm={rpm}  obj='{gameObject.name}'");

            // Big-Endian: EntertainmentCluster 파싱 포맷
            CANBusManager.Instance.Send(CANID.INFO_SPEED_RPM, new byte[]
            {
                (byte)(speed >> 8), (byte)(speed & 0xFF),
                (byte)(rpm   >> 8), (byte)(rpm   & 0xFF),
            });
        }

        void SendVehicleState()
        {
            ushort speed = (ushort)Mathf.Clamp(_vc.SpeedKph * 10f, 0f, 65535f);
            ushort rpm   = (ushort)Mathf.Clamp(_engine.RPM,         0f, 65535f);

            // TransmissionShifterUnit 포맷: 0=N, 1~6, 0xFF=R
            // ManualTransmission.CurrentGear = -1(R) → (byte)(-1) = 0xFF 자동 변환
            byte gearByte = (byte)_trans.CurrentGear;

            byte flags = 0;
            if (_abs != null && _abs.IsActive) flags |= 0x01;

            // bytes 0-1: speed × 10 (uint16 BE)
            // bytes 2-3: RPM       (uint16 BE)
            // byte  4  : gear
            // byte  5  : flags (bit0=ABS, bit1=TCS)
            CANBusManager.Instance.Send(CANID.VEHICLE_STATE, new byte[]
            {
                (byte)(speed >> 8), (byte)(speed & 0xFF),
                (byte)(rpm   >> 8), (byte)(rpm   & 0xFF),
                gearByte,
                flags,
            });
        }

        void SendEngineState()
        {
            // 클러스터 파싱: byte[0]=수온°C, byte[1]=유압0~100, byte[2]=연료%
            byte temp    = (byte)Mathf.Clamp(_coolantTemp, 0f, 255f);
            byte oilPct  = (byte)(_engine.IsRunning ? 75u : 0u);   // 정상 75%
            byte fuel    = (byte)(Mathf.Clamp01(_fuelLiters / fuelCapacityLiters) * 100f);

            CANBusManager.Instance.Send(CANID.ENGINE_STATE, new byte[]
            {
                temp, oilPct, fuel,
            });
        }

        void SendWarnings()
        {
            ushort warn = 0;

            if (_fuelLiters < fuelCapacityLiters * 0.1f) warn |= 1 << 4;   // 연료 부족
            if (_coolantTemp > 100f)                      warn |= 1 << 5;   // 과열
            if (_fuelLiters <= 0f)                        warn |= 1 << 0;   // 연료 고갈 (체크엔진)

            CANBusManager.Instance.Send(CANID.INFO_WARNING, new byte[]
            {
                (byte)(warn & 0xFF), (byte)(warn >> 8),
            });
        }

        void SendPosition()
        {
            int xi = Mathf.RoundToInt(transform.position.x * 100f);
            int zi = Mathf.RoundToInt(transform.position.z * 100f);
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
            float heading = Mathf.Repeat(transform.eulerAngles.y, 360f);
            ushort h = (ushort)Mathf.RoundToInt(heading * 10f);
            if (h >= 3600) h = 3599;
            CANBusManager.Instance.Send(CANID.HEADING, new byte[]
            {
                (byte)(h >> 8), (byte)(h & 0xFF),
            });
        }

        // ── 디버그 ───────────────────────────────────────────────────────────

        /// <summary>연료 잔량 (0.0 ~ 1.0)</summary>
        public float FuelRatio => Mathf.Clamp01(_fuelLiters / fuelCapacityLiters);

        /// <summary>냉각수 온도 (°C)</summary>
        public float CoolantTemp => _coolantTemp;
    }
}
