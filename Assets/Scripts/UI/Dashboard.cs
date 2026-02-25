using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CarSim.Vehicle;
using CarSim.ADAS;
using CarSim.CAN;

namespace CarSim.UI
{
    /// <summary>
    /// 대시보드 UI
    /// - 속도계, RPM 게이지, 기어 표시
    /// - ABS/TCS/ESC 경고등
    /// - 이그니션 상태
    /// - 라즈베리파이 인포테인먼트로 CAN 데이터 송신
    /// </summary>
    public class Dashboard : MonoBehaviour
    {
        [Header("속도계")]
        [SerializeField] RectTransform speedNeedle;
        [SerializeField] TMP_Text       speedText;
        [SerializeField] float          speedNeedleMin = -135f;  // 0 km/h 각도
        [SerializeField] float          speedNeedleMax =  135f;  // 최대속도 각도
        [SerializeField] float          maxSpeedKph    =  260f;

        [Header("RPM 게이지")]
        [SerializeField] RectTransform rpmNeedle;
        [SerializeField] float          rpmNeedleMin = -135f;
        [SerializeField] float          rpmNeedleMax =  135f;
        [SerializeField] float          maxRpm       = 8000f;

        [Header("기어 표시")]
        [SerializeField] TMP_Text gearText;

        [Header("경고등")]
        [SerializeField] Image absLight;
        [SerializeField] Image tcsLight;
        [SerializeField] Image escLight;
        [SerializeField] Image engineLight;   // Check Engine
        [SerializeField] Image batteryLight;

        [Header("색상")]
        [SerializeField] Color warningOn  = Color.red;
        [SerializeField] Color warningOff = new Color(0.2f, 0.2f, 0.2f);

        // ── 컴포넌트 참조
        VehicleController  _vc;
        Engine             _engine;
        ManualTransmission _trans;
        ABS                _abs;
        TCS                _tcs;
        ESC                _esc;

        float _canSendTimer;
        const float CAN_SEND_INTERVAL = 0.05f; // 20Hz

        void Awake()
        {
            var car = FindObjectOfType<VehicleController>();
            if (car == null) return;
            _vc     = car;
            _engine = car.GetComponent<Engine>();
            _trans  = car.GetComponent<ManualTransmission>();
            _abs    = car.GetComponent<ABS>();
            _tcs    = car.GetComponent<TCS>();
            _esc    = car.GetComponent<ESC>();
        }

        void Update()
        {
            if (_vc == null) return;

            UpdateSpeedometer();
            UpdateRPM();
            UpdateGear();
            UpdateWarningLights();
            SendCANTelemetry();
        }

        void UpdateSpeedometer()
        {
            float speed = _vc.SpeedKph;
            if (speedText)   speedText.text = $"{speed:F0}";
            if (speedNeedle) speedNeedle.localEulerAngles = new Vector3(0, 0,
                Mathf.Lerp(speedNeedleMin, speedNeedleMax, speed / maxSpeedKph));
        }

        void UpdateRPM()
        {
            float rpm = _engine != null ? _engine.RPM : 0f;
            if (rpmNeedle) rpmNeedle.localEulerAngles = new Vector3(0, 0,
                Mathf.Lerp(rpmNeedleMin, rpmNeedleMax, rpm / maxRpm));
        }

        void UpdateGear()
        {
            if (gearText == null || _trans == null) return;
            int g = _trans.CurrentGear;
            gearText.text = g == 0 ? "N" : g == -1 ? "R" : g.ToString();
        }

        void UpdateWarningLights()
        {
            SetLight(absLight,    _abs  != null && _abs.IsActive);
            SetLight(tcsLight,    _tcs  != null && _tcs.IsActive);
            SetLight(escLight,    _esc  != null && _esc.IsActive);
            SetLight(engineLight, _engine != null && !_engine.IsRunning);
        }

        void SetLight(Image img, bool on)
        {
            if (img) img.color = on ? warningOn : warningOff;
        }

        void SendCANTelemetry()
        {
            _canSendTimer -= Time.deltaTime;
            if (_canSendTimer > 0f) return;
            _canSendTimer = CAN_SEND_INTERVAL;

            if (CANBusManager.Instance == null) return;

            // 0x400: [속도 u16 ×10][RPM u16] → 라즈베리파이
            ushort speedVal = (ushort)(_vc.SpeedKph * 10f);
            ushort rpmVal   = (ushort)(_engine != null ? _engine.RPM : 0f);
            byte[] data400  = new byte[4];
            System.BitConverter.GetBytes(speedVal).CopyTo(data400, 0);
            System.BitConverter.GetBytes(rpmVal  ).CopyTo(data400, 2);
            CANBusManager.Instance.Send(CANID.INFO_SPEED_RPM, data400);

            // 0x401: 경고등 비트필드
            byte warn = 0;
            if (_abs  != null && _abs.IsActive)  warn |= 1;
            if (_tcs  != null && _tcs.IsActive)  warn |= 2;
            if (_esc  != null && _esc.IsActive)  warn |= 4;
            if (_engine != null && _engine.IsStalled) warn |= 8;
            CANBusManager.Instance.Send(CANID.INFO_WARNING, new[] { warn });
        }
    }
}
