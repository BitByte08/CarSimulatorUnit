using UnityEngine;
using UnityEngine.InputSystem;
using System.IO.Ports;
using CarSim.CAN;
using CarSim.Vehicle;
using CarSim.ADAS;

namespace CarSim.UI
{
    public class CanSettingsPanel : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (FindObjectOfType<CanSettingsPanel>() != null) return;
            var go = new GameObject("CanSettings");
            go.AddComponent<CanSettingsPanel>();
            DontDestroyOnLoad(go);
        }

        bool    _show;
        bool    _startupChecked;
        Vector2 _scroll;
        bool    _secCan = true;

        string[] _ports = new string[0];
        int      _portIdx;
        string   _baud = "115200";
        bool     _sim;

        bool    _secUnit = true;
        bool    _pedalCan     = true;
        bool    _shifterCan   = true;
        bool    _steerCan     = true;
        bool    _clusterCan   = true;
        bool    _entertainCan = true;
        bool    _ffbCan       = true;

        const string PrefPedal     = "unit.pedal";
        const string PrefShifter   = "unit.shifter";
        const string PrefSteering  = "unit.steering";
        const string PrefCluster   = "unit.cluster";
        const string PrefEntertain = "unit.entertain";
        const string PrefFfb       = "unit.ffb";

        GUIStyle _box, _label, _btn, _header, _toggle, _field;
        int      _fontSize;

        PedalECUHandler    _pedalRef;
        SwitchPanelHandler _switchRef;
        SteeringHandler    _steerRef;
        VehicleBroadcast   _broadcastRef;
        EPS                _epsRef;

        bool _pedalPrev, _shifterPrev, _steerPrev, _clusterPrev, _entertainPrev, _ffbPrev;

        void EnsureStyles()
        {
            int fs = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 48f), 15, 42);
            if (_label != null && fs == _fontSize) return;
            _fontSize = fs;

            _label  = new GUIStyle(GUI.skin.label)     { fontSize = fs };
            _btn    = new GUIStyle(GUI.skin.button)    { fontSize = fs };
            _toggle = new GUIStyle(GUI.skin.toggle)    { fontSize = fs };
            _field  = new GUIStyle(GUI.skin.textField) { fontSize = fs };
            _header = new GUIStyle(GUI.skin.button)    { fontSize = fs + 2, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _box    = new GUIStyle(GUI.skin.box)       { fontSize = fs + 3, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
        }

        void RefreshPorts()
        {
            _ports = SerialPort.GetPortNames();
            var mgr = CANBusManager.Instance;
            if (mgr == null) return;
            _baud = mgr.BaudRate.ToString();
            _sim  = mgr.SimulationMode;
            int idx = System.Array.IndexOf(_ports, mgr.PortName);
            if (idx >= 0) _portIdx = idx;

            _pedalRef     = FindObjectOfType<PedalECUHandler>();
            _switchRef    = FindObjectOfType<SwitchPanelHandler>();
            _steerRef     = FindObjectOfType<SteeringHandler>();
            _broadcastRef = FindObjectOfType<VehicleBroadcast>();
            _epsRef       = FindObjectOfType<EPS>();

            _pedalCan     = PlayerPrefs.GetInt(PrefPedal,     1) != 0;
            _shifterCan   = PlayerPrefs.GetInt(PrefShifter,   1) != 0;
            _steerCan     = PlayerPrefs.GetInt(PrefSteering,  1) != 0;
            _clusterCan   = PlayerPrefs.GetInt(PrefCluster,   1) != 0;
            _entertainCan = PlayerPrefs.GetInt(PrefEntertain, 1) != 0;
            _ffbCan       = PlayerPrefs.GetInt(PrefFfb,       1) != 0;
        }

        void SnapshotPrev()
        {
            _pedalPrev = _pedalCan; _shifterPrev = _shifterCan; _steerPrev = _steerCan;
            _clusterPrev = _clusterCan; _entertainPrev = _entertainCan; _ffbPrev = _ffbCan;
        }

        void ApplyUnitChanges()
        {
            if (_pedalRef != null && _pedalCan != _pedalPrev)
            { _pedalRef.useCanMode = _pedalCan; PlayerPrefs.SetInt(PrefPedal, _pedalCan ? 1 : 0); _pedalPrev = _pedalCan; }
            if (_switchRef != null && _shifterCan != _shifterPrev)
            { _switchRef.gearCanMode = _shifterCan; PlayerPrefs.SetInt(PrefShifter, _shifterCan ? 1 : 0); _shifterPrev = _shifterCan; }
            if (_switchRef != null && _entertainCan != _entertainPrev)
            { _switchRef.switchCanMode = _entertainCan; PlayerPrefs.SetInt(PrefEntertain, _entertainCan ? 1 : 0); _entertainPrev = _entertainCan; }
            if (_steerRef != null && _steerCan != _steerPrev)
            { _steerRef.useCanMode = _steerCan; PlayerPrefs.SetInt(PrefSteering, _steerCan ? 1 : 0); _steerPrev = _steerCan; }
            if (_broadcastRef != null && _clusterCan != _clusterPrev)
            { _broadcastRef.broadcastEnabled = _clusterCan; PlayerPrefs.SetInt(PrefCluster, _clusterCan ? 1 : 0); _clusterPrev = _clusterCan; }
            if (_epsRef != null && _ffbCan != _ffbPrev)
            { _epsRef.ffbEnabled = _ffbCan; PlayerPrefs.SetInt(PrefFfb, _ffbCan ? 1 : 0); _ffbPrev = _ffbCan; }
            PlayerPrefs.Save();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame)
            {
                _show = !_show;
                if (_show) { RefreshPorts(); SnapshotPrev(); }
            }
            if (!_startupChecked && Time.timeSinceLevelLoad > 1.5f)
            {
                _startupChecked = true;
                var m = CANBusManager.Instance;
                if (m != null && !m.IsConnected && !_sim)
                {
                    _show = true;
                    RefreshPorts();
                    SnapshotPrev();
                }
            }
        }

        void OnGUI()
        {
            EnsureStyles();
            GUI.Label(new Rect(Screen.width - 220f, 8f, 210f, _fontSize + 10f), "F2: 설정", _label);
            if (!_show) return;

            float w = Mathf.Clamp(Screen.width  * 0.46f, 380f, 720f);
            float h = Mathf.Clamp(Screen.height * 0.70f, 300f, 820f);
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(rect, "설정  (F2 닫기)", _box);

            float pad = 14f;
            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y + _fontSize + 18f, w - pad * 2f, h - _fontSize - 30f));
            _scroll = GUILayout.BeginScrollView(_scroll);

            if (GUILayout.Button((_secCan ? "▼  " : "▶  ") + "CAN / USB", _header)) _secCan = !_secCan;
            if (_secCan)
            {
                GUILayout.Space(4f);
                GUILayout.Label("USB 포트:", _label);
                if (_ports.Length == 0)
                    GUILayout.Label("  (포트 없음 — '새로고침')", _label);
                else
                    _portIdx = GUILayout.SelectionGrid(Mathf.Clamp(_portIdx, 0, _ports.Length - 1), _ports, 1, _btn);

                GUILayout.Space(6f);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Baud:", _label, GUILayout.Width(_fontSize * 4.5f));
                _baud = GUILayout.TextField(_baud, _field, GUILayout.Width(_fontSize * 7f));
                GUILayout.EndHorizontal();

                GUILayout.Space(6f);
                _sim = GUILayout.Toggle(_sim, " 시뮬레이션 모드 (하드웨어 없이)", _toggle);

                GUILayout.Space(10f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("새로고침", _btn)) { RefreshPorts(); SnapshotPrev(); }
                if (GUILayout.Button("적용 & 연결", _btn)) Apply();
                GUILayout.EndHorizontal();

                var m = CANBusManager.Instance;
                GUILayout.Space(6f);
                GUILayout.Label(m != null && m.IsConnected ? "상태: 연결됨" : (_sim ? "상태: 시뮬레이션" : "상태: 미연결"), _label);
            }

            GUILayout.Space(8f);

            if (GUILayout.Button((_secUnit ? "▼  " : "▶  ") + "유닛 선택  (ON=CAN, OFF=키보드)", _header)) _secUnit = !_secUnit;
            if (_secUnit)
            {
                GUILayout.Space(4f);
                GUILayout.Label("각 유닛 개별 CAN/시뮬레이션 전환:", _label);
                GUILayout.Space(2f);

                _pedalCan     = GUILayout.Toggle(_pedalCan,     " 페달 (Pedal)",               _toggle);
                _shifterCan   = GUILayout.Toggle(_shifterCan,   " 시프터 / 기어 (Shifter)",    _toggle);
                _steerCan     = GUILayout.Toggle(_steerCan,     " 스티어링 (Steering)",        _toggle);
                _clusterCan   = GUILayout.Toggle(_clusterCan,   " 클러스터 (Broadcast)",       _toggle);
                _entertainCan = GUILayout.Toggle(_entertainCan, " 엔터테인먼트 (SwitchPanel)", _toggle);
                _ffbCan       = GUILayout.Toggle(_ffbCan,       " FFB (Force Feedback)",       _toggle);

                ApplyUnitChanges();
            }

            GUILayout.Space(14f);
            if (GUILayout.Button("닫기", _btn)) _show = false;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Apply()
        {
            var mgr = CANBusManager.Instance;
            if (mgr == null) return;
            string port = _ports.Length > 0 ? _ports[Mathf.Clamp(_portIdx, 0, _ports.Length - 1)] : "";
            int    baud = int.TryParse(_baud, out int b) ? b : 115200;
            mgr.ApplySettings(port, baud, _sim);
            _show = false;
        }
    }
}
