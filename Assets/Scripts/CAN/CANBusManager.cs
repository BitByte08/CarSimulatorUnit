using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using CarSim.CAN;
#if UNITY_STANDALONE || UNITY_EDITOR
using System.IO.Ports;
#endif

namespace CarSim.CAN
{
    /// <summary>
    /// CAN 버스 통신 허브 (CANable SLCAN 프로토콜)
    /// 하드웨어 없을 때는 Inspector에서 simulationMode = true 로 테스트 가능
    /// </summary>
    public class CANBusManager : MonoBehaviour
    {
        public static CANBusManager Instance { get; private set; }

        [Header("포트 설정")]
        [SerializeField] string portName = "COM5";
        [SerializeField] int baudRate = 2000000;

        [Header("테스트 모드 (하드웨어 없을 때)")]
        public bool simulationMode = true;

#if UNITY_STANDALONE || UNITY_EDITOR
        SerialPort _port;
        Thread     _readThread;
        bool       _running;
#endif

        readonly Dictionary<uint, Action<byte[]>> _handlers = new();
        readonly Queue<CANFrame> _rxQueue = new();
        readonly object _queueLock = new();

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
                Debug.Log("[CAN] 시뮬레이션 모드 - 하드웨어 미연결");
                return;
            }

#if UNITY_STANDALONE || UNITY_EDITOR
            try
            {
                _port = new SerialPort(portName, baudRate) { ReadTimeout = 100 };
                _port.Open();
                _port.Write("C\r"); // CANable 닫기
                Thread.Sleep(100);
                _port.Write("S6\r"); // 500kbps
                Thread.Sleep(100);
                _port.Write("O\r"); // CAN 열기
                _running = true;
                _readThread = new Thread(ReadLoop) { IsBackground = true };
                _readThread.Start();
                Debug.Log($"[CAN] {portName} 연결 성공");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CAN] 포트 열기 실패: {e.Message} → 시뮬레이션 모드로 전환");
                simulationMode = true;
            }
#else
            Debug.LogWarning("[CAN] 이 플랫폼은 SerialPort 미지원 → 시뮬레이션 모드");
            simulationMode = true;
#endif
        }

        void ReadLoop()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            while (_running)
            {
                try
                {
                    string line = _port.ReadLine();
                    if (TryParseSLCAN(line, out CANFrame frame))
                    {
                        lock (_queueLock) { _rxQueue.Enqueue(frame); }
                    }
                }
                catch (TimeoutException) { }
                catch (Exception e) { Debug.LogError($"[CAN] 읽기 오류: {e.Message}"); }
            }
#endif
        }

        void Update()
        {
            // Unity 메인 스레드에서 핸들러 실행
            lock (_queueLock)
            {
                while (_rxQueue.Count > 0)
                {
                    var frame = _rxQueue.Dequeue();
                    if (_handlers.TryGetValue(frame.Id, out var handler))
                        handler(frame.Data);
                }
            }
        }

        // ── SLCAN 파싱 ──────────────────────────────────
        // 포맷: t<ID:3hex><DLC:1><DATA:DLC*2hex>\r
        static bool TryParseSLCAN(string raw, out CANFrame frame)
        {
            frame = default;
            if (string.IsNullOrEmpty(raw) || raw[0] != 't' || raw.Length < 6)
                return false;
            try
            {
                uint id  = Convert.ToUInt32(raw.Substring(1, 3), 16);
                int  dlc = raw[4] - '0';
                if (raw.Length < 5 + dlc * 2) return false;
                byte[] data = new byte[dlc];
                for (int i = 0; i < dlc; i++)
                    data[i] = Convert.ToByte(raw.Substring(5 + i * 2, 2), 16);
                frame = new CANFrame(id, data);
                return true;
            }
            catch { return false; }
        }

        // ── 공개 API ─────────────────────────────────────

        /// <summary>CAN ID에 수신 핸들러 등록</summary>
        public void Register(uint canId, Action<byte[]> handler)
            => _handlers[canId] = handler;

        /// <summary>CAN 프레임 송신 (시뮬레이션 모드에서는 로컬 루프백)</summary>
        public void Send(uint id, byte[] data)
        {
            if (simulationMode)
            {
                // 루프백: 내가 보낸 프레임을 내가 수신하도록
                lock (_queueLock) { _rxQueue.Enqueue(new CANFrame(id, data)); }
                return;
            }
            string frame = $"t{id:X3}{data.Length:X1}";
            foreach (var b in data) frame += $"{b:X2}";
#if UNITY_STANDALONE || UNITY_EDITOR
            try { _port.Write(frame + "\r"); }
            catch (Exception e) { Debug.LogError($"[CAN] 송신 오류: {e.Message}"); }
#endif
        }

        void OnDestroy()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            _running = false;
            _readThread?.Join(500);
            if (_port?.IsOpen == true)
            {
                _port.Write("C\r"); // CAN 닫기
                _port.Close();
            }
#endif
        }
    }
}
