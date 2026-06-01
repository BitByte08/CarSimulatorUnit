using UnityEngine;
using UnityEngine.InputSystem;
using System.IO.Ports;
using CarSim.CAN;

namespace CarSim.UI
{
    /// <summary>
    /// F2 토글 런타임 설정창 — 빌드에서 USB(시리얼) 포트/보드레이트/시뮬레이션 모드를 변경.
    /// 씬 배치 없이 자동 생성되며 설정은 PlayerPrefs에 저장된다.
    /// </summary>
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

        GUIStyle _box, _label, _btn, _header, _toggle, _field;
        int      _fontSize;

        void EnsureStyles()
        {
            int fs = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 48f), 15, 42);
            if (_label != null && fs == _fontSize) return;
            _fontSize = fs;

            _label  = new GUIStyle(GUI.skin.label)     { fontSize = fs };
            _btn    = new GUIStyle(GUI.skin.button)    { fontSize = fs };
            _toggle = new GUIStyle(GUI.skin.toggle)    { fontSize = fs };
            _field  = new GUIStyle(GUI.skin.textField) { fontSize = fs };
            _header = new GUIStyle(GUI.skin.button)     { fontSize = fs + 2, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _box    = new GUIStyle(GUI.skin.box)        { fontSize = fs + 3, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
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
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame)
            {
                _show = !_show;
                if (_show) RefreshPorts();
            }

            // 시작 직후 미연결이면 설정창 자동 표시 (포트 선택 유도)
            if (!_startupChecked && Time.timeSinceLevelLoad > 1.5f)
            {
                _startupChecked = true;
                var m = CANBusManager.Instance;
                if (m != null && !m.IsConnected)
                {
                    _show = true;
                    RefreshPorts();
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

            // ── CAN / USB 섹션 (접이식) ──
            if (GUILayout.Button((_secCan ? "▼  " : "▶  ") + "CAN / USB", _header))
                _secCan = !_secCan;

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
                if (GUILayout.Button("새로고침", _btn)) RefreshPorts();
                if (GUILayout.Button("적용 & 연결", _btn)) Apply();
                GUILayout.EndHorizontal();

                var m = CANBusManager.Instance;
                GUILayout.Space(6f);
                GUILayout.Label(m != null && m.IsConnected ? "상태: 연결됨" :
                                (_sim ? "상태: 시뮬레이션" : "상태: 미연결"), _label);
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
