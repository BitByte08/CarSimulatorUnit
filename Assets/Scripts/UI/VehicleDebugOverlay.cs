using UnityEngine;
using CarSim.Vehicle;
using CarSim.CAN;

namespace CarSim.UI
{
    /// <summary>
    /// F1 토글 디버그 오버레이
    /// - 왼쪽: 입력 (스위치 / 페달 / 스티어링)
    /// - 오른쪽: 엔진 / 변속기 / 차량 물리
    /// - 배경 없이 텍스트만 표시
    /// </summary>
    public class VehicleDebugOverlay : MonoBehaviour
    {
        [Header("표시 설정")]
        [SerializeField] bool  showOverlay = true;
        [SerializeField] int   fontSize    = 13;
        [SerializeField] int   bigFontSize = 28;
        [SerializeField] float colWidth    = 260f;
        [SerializeField] float marginX     = 12f;
        [SerializeField] float marginY     = 12f;

        VehicleController  _vehicle;
        Engine             _engine;
        ManualTransmission _trans;
        PedalECUHandler    _pedals;
        SteeringHandler    _steering;
        SwitchPanelHandler _switches;

        GUIStyle _label;
        GUIStyle _header;
        GUIStyle _shadow;
        GUIStyle _big;
        GUIStyle _bigShadow;

        void Awake()
        {
            _vehicle = FindObjectOfType<VehicleController>();
            if (_vehicle != null)
            {
                _engine = _vehicle.GetComponent<Engine>();
                _trans  = _vehicle.GetComponent<ManualTransmission>();
            }
            _pedals   = FindObjectOfType<PedalECUHandler>();
            _steering = FindObjectOfType<SteeringHandler>();
            _switches = FindObjectOfType<SwitchPanelHandler>();
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame)
                showOverlay = !showOverlay;
        }

        void OnGUI()
        {
            if (!showOverlay) return;
            InitStyles();

            float y  = marginY;
            float lx = marginX;
            float rx = Screen.width - colWidth - marginX;
            float h  = Screen.height - marginY * 2f;

            // ── 상단 중앙: 속도 + RPM 큰 글씨 ──────────────────
            if (_vehicle != null && _engine != null)
            {
                float kph      = _vehicle.SpeedKph;
                float rpm      = _engine.RPM;
                float wheelRpm = _engine.WheelDrivenRpm;
                bool  slipping = _engine.IsRunning && Mathf.Abs(rpm - wheelRpm) > 300f;
                int   gear     = _trans != null ? _trans.CurrentGear : 0;

                string speedStr = $"{kph:F1} <size={bigFontSize - 8}>km/h</size>";
                string rpmStr   = $"{rpm:F0} <size={bigFontSize - 8}>rpm</size>";
                string wRpmStr  = $"({wheelRpm:F0})";
                string gearStr  = GearName(gear);

                float bw = Screen.width * 0.5f;
                float bx = (Screen.width - bw) * 0.5f;

                // 속도 왜쪽, RPM 오른쪽
                float hw = bw * 0.48f;

                DrawBig($"{gearStr}  {speedStr}", new Rect(bx, y, hw, bigFontSize + 8));
                DrawBig(rpmStr, new Rect(bx + hw + 8, y, hw, bigFontSize + 8),
                        slipping ? new Color(1f, 0.4f, 0.2f) : Color.white);

                // 이론 RPM (slipping 시 주황)
                if (_engine.IsRunning)
                {
                    string diff = slipping ? $" <color=#ff6633>SLIP Δ{Mathf.Abs(rpm - wheelRpm):F0}</color>" : "";
                    DrawShadow($"<size={fontSize - 1}>wheel×ratio: {wheelRpm:F0}{diff}</size>",
                               _label, new Rect(bx + hw + 8, y + bigFontSize + 2, hw, fontSize + 4));
                }
            }

            // ── 왼쪽: 입력 ──────────────────────────────
            float topOffset = marginY + bigFontSize + 16f;
            GUILayout.BeginArea(new Rect(lx, topOffset, colWidth, h - topOffset));
            GUILayout.BeginVertical();

            Col("[ 입력 ]  F1 토글");
            Sp();

            Hdr("■ 스위치");
            if (_switches != null)
            {
                Row("Ignition",    _switches.IgnitionOn  ? "ON"  : "off",  _switches.IgnitionOn);
                Row("Engine SW",   _switches.EngineStart ? "ON"  : "off",  _switches.EngineStart);
                Row("GearRequest", GearName(_switches.GearRequest));
            }
            else Err("SwitchPanelHandler");
            Sp();

            Hdr("■ 페달");
            if (_pedals != null)
            {
                Bar("Throttle", _pedals.Throttle, "g");
                Bar("Brake",    _pedals.Brake,    "r");
                Bar("Clutch",   _pedals.Clutch,   "c");
            }
            else Err("PedalECUHandler");
            Sp();

            Hdr("■ 스티어링");
            if (_steering != null)
                Row("Angle", $"{_steering.SteeringAngle:F1} °");
            else Err("SteeringHandler");
            Sp();

            Hdr("■ 차량 물리");
            if (_vehicle != null)
            {
                Row("Speed",  $"{_vehicle.SpeedKph:F1} km/h  {_vehicle.SpeedMs:F2} m/s");
                Row("LonG",   $"{_vehicle.LongitudinalG:F3} G");
                Row("LatG",   $"{_vehicle.LateralG:F3} G");
                Row("Brake",  $"{_vehicle.BrakeInput:F3}");
                Row("HBrake", _vehicle.HandbrakeOn ? "ON" : "off", _vehicle.HandbrakeOn);
            }
            else Err("VehicleController");

            GUILayout.EndVertical();
            GUILayout.EndArea();

            // ── 오른쪽: 엔진 / 변속기 / 휠 ──────────────
            GUILayout.BeginArea(new Rect(rx, topOffset, colWidth, h - topOffset));
            GUILayout.BeginVertical();

            Col("[ 차량 상태 ]");
            Sp();

            Hdr("■ 엔진");
            if (_engine != null)
            {
                bool slipping = _engine.IsRunning &&
                                Mathf.Abs(_engine.RPM - _engine.WheelDrivenRpm) > 300f;
                Row("Running",      _engine.IsRunning ? "ON" : "off",     _engine.IsRunning);
                Row("Stalled",      _engine.IsRunning ? "no" : "STALL",  _engine.IsRunning);
                Row("RPM (\ubb3c\ub9ac)",   $"{_engine.RPM:F0}");
                Row("RPM (\uc774\ub860)",   $"{_engine.WheelDrivenRpm:F0}", slipping);
                Row("Throttle",     $"{_engine.ThrottleInput:F3}");
                Row("OutputTorque", $"{_engine.OutputTorque:F1} Nm");
                Row("EngBrake Tq",  $"{_engine.EngineBrakeTorque:F1} Nm");
            }
            else Err("Engine");
            Sp();

            Hdr("■ 변속기");
            if (_trans != null)
            {
                bool shifting = _trans.RequestedGear != _trans.CurrentGear;
                Row("CurrentGear",  GearName(_trans.CurrentGear));
                Row("Requested",    GearName(_trans.RequestedGear), shifting);
                Row("Shifting",     shifting ? "YES..." : "no",    !shifting);
                Bar("ClutchIn",     _trans.ClutchInput,       "c");
                Bar("ClutchEngage", _trans.ClutchEngagement,  "y");
                Row("TxTorque",     $"{_trans.TransmittedTorque:F1} Nm");
                Row("ClutchEngage", $"{_trans.ClutchEngagement:F2}");
                Row("GearRatio",    $"{_trans.GetCurrentTotalRatio():F3}");
            }
            else Err("ManualTransmission");
            Sp();

            Hdr("■ 휠  rpm | motor | brake");
            if (_vehicle != null)
            {
                var wheels = _vehicle.GetAllWheels();
                string[] wn = { "FL", "FR", "RL", "RR" };
                for (int i = 0; i < wheels.Length; i++)
                {
                    var w = wheels[i];
                    Row(wn[i], $"{w.rpm:F0}  |  {w.motorTorque:F0}  |  {w.brakeTorque:F0}");
                }
            }
            else Err("VehicleController");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        // ── 스타일 초기화 ─────────────────────────────

        void InitStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                richText = true,
                normal   = { textColor = Color.white },
            };
            _header = new GUIStyle(_label)
            {
                fontSize  = fontSize,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.45f, 0.92f, 1f) },
            };
            // 그림자용 (검은색 오프셋)
            _shadow = new GUIStyle(_label)
            {
                normal = { textColor = new Color(0, 0, 0, 0.8f) },
            };
            _big = new GUIStyle(GUI.skin.label)
            {
                fontSize   = bigFontSize,
                fontStyle  = FontStyle.Bold,
                richText   = true,
                normal     = { textColor = Color.white },
            };
            _bigShadow = new GUIStyle(_big)
            {
                normal = { textColor = new Color(0, 0, 0, 0.85f) },
            };
        }

        // ── 헬퍼 ─────────────────────────────────────

        void Col(string t)  => DrawShadow(t, _header);
        void Hdr(string t)  => DrawShadow(t, _header);
        void Sp()           => GUILayout.Space(4);

        void Row(string label, string value, bool hi = false)
        {
            var st = new GUIStyle(_label);
            st.normal.textColor = hi ? new Color(0.4f, 1f, 0.5f) : Color.white;
            DrawShadow($"<b>{label,-14}</b> {value}", st);
        }

        void Bar(string label, float v, string col)
        {
            string color = col switch { "g" => "#88ff88", "r" => "#ff7777", "c" => "#88ffff", "y" => "#ffff66", _ => "white" };
            int filled  = Mathf.RoundToInt(Mathf.Clamp01(v) * 12);
            string bar  = new string('█', filled) + new string('░', 12 - filled);
            DrawShadow($"<b>{label,-14}</b> <color={color}>{bar}</color> {v:F2}", _label);
        }

        void Err(string name)
        {
            var st = new GUIStyle(_label) { normal = { textColor = Color.red } };
            GUILayout.Label($"  ✗ {name} 없음", st);
        }

        void DrawShadow(string text, GUIStyle style)
        {
            Rect r = GUILayoutUtility.GetRect(new GUIContent(text), style);
            _shadow.richText  = style.richText;
            _shadow.fontSize  = style.fontSize;
            _shadow.fontStyle = style.fontStyle;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, _shadow);
            GUI.Label(r, text, style);
        }

        void DrawShadow(string text, GUIStyle style, Rect r)
        {
            _shadow.richText  = style.richText;
            _shadow.fontSize  = style.fontSize;
            _shadow.fontStyle = style.fontStyle;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, _shadow);
            GUI.Label(r, text, style);
        }

        void DrawBig(string text, Rect r, Color? color = null)
        {
            var st = new GUIStyle(_big);
            if (color.HasValue) st.normal.textColor = color.Value;
            _bigShadow.fontSize = bigFontSize;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, _bigShadow);
            GUI.Label(r, text, st);
        }

        static string GearName(int g) => g switch
        {
            -1 => "R",
             0 => "N",
             _ => $"{g}단"
        };
    }
}
