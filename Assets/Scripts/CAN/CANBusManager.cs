using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using UnityEngine;

namespace CarSim.CAN
{
    /// <summary>
    /// Cockpit.Usb2Can (ESP32-C3) 브릿지를 통한 CAN 버스 통신 관리자.
    ///
    /// 프로토콜 (115200 baud, ASCII):
    ///   수신/송신 동일 형식: "t{ID_HEX 3자리}{DLC 1자리}{DATA[0..7]}"
    ///   예시: "t10023A0B" (ID=0x100, DLC=2, Data=0x23, 0xA0)
    ///   ACK  ESP32→PC : "z\n" | "\r"  (무시)
    ///
    /// simulationMode=true 일 때는 시리얼 없이 루프백으로 동작.
    /// </summary>
    public class CANBusManager : MonoBehaviour
    {
        public static CANBusManager Instance { get; private set; }

        [Header("Usb2Can 포트 설정")]
        [SerializeField] string portName = "/dev/ttyUSB0";
        [SerializeField] int    baudRate = 115200;

        [Header("테스트 모드")]
        [Tooltip("실제 하드웨어 없이 루프백으로 테스트")]
        public bool simulationMode = true;

        /// <summary>시리얼 포트가 열려 있으면 true</summary>
        public bool IsConnected => _port?.IsOpen == true;

        readonly Dictionary<uint, Action<byte[]>> _handlers   = new();
        readonly Queue<CANFrame>                  _rxQueue    = new();
        readonly Queue<string>                    _logQueue   = new();
        readonly object                           _queueLock  = new();

        SerialPort       _port;
        Thread           _rxThread;
        volatile bool    _running;

        // ── Unity 생명주기 ───────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (simulationMode)
            {
                Debug.Log("[CAN] 시뮬레이션 모드 (루프백)");
                return;
            }
            Connect();
        }

        void Update()
        {
            lock (_queueLock)
            {
                while (_logQueue.Count > 0)
                    Debug.Log(_logQueue.Dequeue());
                while (_rxQueue.Count > 0)
                {
                    var frame = _rxQueue.Dequeue();
                    if (_handlers.TryGetValue(frame.Id, out var handler))
                        handler(frame.Data);
                }
            }
        }

        void OnDestroy()
        {
            _running = false;
            _rxThread?.Join(300);
            if (_port?.IsOpen == true) _port.Close();
            _port?.Dispose();
        }

        // ── 공개 API ─────────────────────────────────────────────────────────

        /// <summary>특정 CAN ID에 대한 수신 핸들러를 등록한다.</summary>
        public void Register(uint canId, Action<byte[]> handler)
            => _handlers[canId] = handler;

        /// <summary>
        /// CAN 프레임을 송신한다.
        /// simulationMode 시에는 수신 큐에 루프백하여 로컬 테스트를 지원한다.
        /// 프로토콜: "t{ID_HEX 3자리}{DLC 1자리}{DATA[0..7]}"
        /// 예시: "t10023A0B" (ID=0x100, DLC=2, Data=0x23, 0xA0)
        /// </summary>
        public void Send(uint id, byte[] data)
        {
            if (simulationMode)
            {
                lock (_queueLock) _rxQueue.Enqueue(new CANFrame(id, data));
                return;
            }

            if (_port?.IsOpen != true) return;

            // "t{ID_HEX 3자리}{DLC 1자리}{DATA}\n"
            var sb = new StringBuilder(32);
            sb.Append('t').Append(id.ToString("X3")).Append(data.Length.ToString("X"));
            foreach (byte b in data)
                sb.Append(b.ToString("X2"));
            sb.Append('\n');

            try   { _port.Write(sb.ToString()); }
            catch (Exception ex) { Debug.LogWarning($"[CAN] TX 실패: {ex.Message}"); }
        }

        // ── 내부 구현 ────────────────────────────────────────────────────────

        void Connect()
        {
            try
            {
                _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout  = 100,
                    WriteTimeout = 100,
                    NewLine      = "\n",
                };
                _port.Open();
                _running  = true;
                _rxThread = new Thread(RxLoop) { IsBackground = true, Name = "CAN_RX" };
                _rxThread.Start();
                Debug.Log($"[CAN] 연결됨: {portName} @ {baudRate} bps, Thread started={_rxThread.IsAlive}");
                _logQueue.Enqueue("[CAN] RxLoop 시작 (메인스레드에서 추가)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CAN] 포트 열기 실패 ({portName}): {ex.Message}  →  시뮬레이션 모드 전환");
                simulationMode = true;
            }
        }

        void RxLoop()
        {
            lock (_queueLock) _logQueue.Enqueue("[CAN] RxLoop 시작");
            var sb = new StringBuilder(64);
            while (_running)
            {
                try
                {
                    int b = _port.BaseStream.ReadByte();
                    if (b < 0) continue;

                    if (b == '\r' || b == '\n')
                    {
                        if (sb.Length == 0) continue;
                        string line = sb.ToString();
                        sb.Clear();

                        lock (_queueLock) _logQueue.Enqueue($"[CAN] Raw: \"{line}\" (len={line.Length})");
                        ProcessLine(line);
                    }
                    else
                    {
                        sb.Append((char)b);
                        if (sb.Length > 64) sb.Clear();
                    }
                }
                catch (TimeoutException) { }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        lock (_queueLock) _logQueue.Enqueue($"[CAN] RX 에러: {ex.Message}");
                        break;
                    }
                }
            }
        }

        void ProcessLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            char first = line.TrimStart()[0];
            if (first != 't' && first != 'T')
            {
                lock (_queueLock) _logQueue.Enqueue($"[CAN] Skip (first={first}): \"{line}\"");
                return;
            }

            if (TryParseUsb2Can(line, out CANFrame frame))
            {
                lock (_queueLock) _rxQueue.Enqueue(frame);
            }
            else
            {
                lock (_queueLock) _logQueue.Enqueue($"[CAN] Parse failed: \"{line}\"");
            }
        }

        /// <summary>
        /// "t{ID_HEX 3자리}{DLC 1자리}{DATA}" 형식을 CANFrame으로 파싱한다.
        /// 예시: "t10023A0B" (ID=0x100, DLC=2, Data=0x3A, 0x0B)
        /// </summary>
        static bool TryParseUsb2Can(string line, out CANFrame frame)
        {
            frame = default;
            line = line.Trim();
            
            if (line.Length < 5 || (line[0] != 't' && line[0] != 'T')) return false;
            
            try
            {
                uint id = Convert.ToUInt32(line.Substring(1, 3), 16);
                int dlc = Convert.ToInt32(line.Substring(4, 1), 16);
                if (dlc < 0 || dlc > 8) return false;

                byte[] data = new byte[dlc];
                int dataStart = 5;
                for (int i = 0; i < dlc; i++)
                {
                    if (dataStart + i * 2 + 1 < line.Length)
                        data[i] = Convert.ToByte(line.Substring(dataStart + i * 2, 2), 16);
                }

                frame = new CANFrame(id, data);
                return true;
            }
            catch { return false; }
        }
    }
}
