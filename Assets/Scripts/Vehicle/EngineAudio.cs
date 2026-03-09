using UnityEngine;

namespace CarSim.Vehicle
{
    /// <summary>
    /// 엔진 사운드 시뮬레이션
    /// - RPM 기반 피치 변조
    /// - 스로틀에 따라 아이들 ↔ 가속 클립 크로스페이드
    /// - 시동 켜기/끄기 사운드
    ///
    /// [사용 방법]
    /// 1. 이 컴포넌트를 차량 GameObject에 추가
    /// 2. Inspector에서 AudioClip 슬롯에 클립 할당
    ///    - clipIdle      : 공회전 루프 사운드
    ///    - clipAccel     : 가속 루프 사운드
    ///    - clipDecel     : 엔진브레이크/감속 루프 사운드 (없으면 idle 사용)
    ///    - clipStart     : 시동 one-shot
    ///    - clipStall     : 시동 꺼짐 one-shot
    /// </summary>
    [RequireComponent(typeof(Engine))]
    public class EngineAudio : MonoBehaviour
    {
        [Header("오디오 클립")]
        [SerializeField] AudioClip clipIdle;
        [SerializeField] AudioClip clipAccel;
        [SerializeField] AudioClip clipDecel;       // 없으면 idle로 대체
        [SerializeField] AudioClip clipStart;
        [SerializeField] AudioClip clipStall;

        [Header("피치 설정")]
        [Tooltip("기준 RPM: 클립이 녹음된 RPM (보통 1000~2000)")]
        [SerializeField] float baseRpm        = 1000f;
        [Tooltip("RPM→피치 배율. 값이 클수록 RPM 변화에 피치가 더 민감")]
        [SerializeField] float pitchMultiplier = 1.0f;
        [SerializeField] float pitchMin        = 0.5f;
        [SerializeField] float pitchMax        = 3.5f;

        [Header("볼륨 설정")]
        [SerializeField] float masterVolume     = 1.0f;
        [SerializeField] float crossfadeSpeed   = 4.0f;   // 크로스페이드 속도 (초^-1)

        [Header("공간 오디오")]
        [SerializeField] float spatialBlend     = 1.0f;   // 0=2D, 1=3D (1인칭이면 0 권장)
        [SerializeField] float minDistance      = 2.0f;
        [SerializeField] float maxDistance      = 50.0f;

        // ── 내부 AudioSource ────────────────────────
        AudioSource _srcIdle;
        AudioSource _srcAccel;
        AudioSource _srcDecel;
        AudioSource _srcOneShot;

        Engine _engine;
        bool   _wasRunning;

        void Awake()
        {
            _engine = GetComponent<Engine>();

            _srcIdle    = CreateSource("SND_Idle",    clipIdle,  true);
            _srcAccel   = CreateSource("SND_Accel",   clipAccel, true);
            _srcDecel   = CreateSource("SND_Decel",   clipDecel != null ? clipDecel : clipIdle, true);
            _srcOneShot = CreateSource("SND_OneShot", null,      false);
        }

        void Update()
        {
            bool running = _engine.IsRunning;
            var currentState = _engine.State;

            // ── 시동 / 스톨 이벤트 ───────────────────
            if (currentState == Engine.EngineState.Cranking && !_wasRunning)
            {
                PlayOneShot(clipStart);
                _srcIdle.Play();
                _srcAccel.Play();
                _srcDecel.Play();
                _wasRunning = true; // 크랭킹 시작 시 한 번만 재생
            }
            else if (currentState == Engine.EngineState.Off && _wasRunning)
            {
                PlayOneShot(clipStall);
                _wasRunning = false;
            }

            // 엔진이 완전히 꺼졌을 때만 모든 소스 페이드 아웃
            if (currentState == Engine.EngineState.Off)
            {
                FadeSource(_srcIdle,  0f);
                FadeSource(_srcAccel, 0f);
                FadeSource(_srcDecel, 0f);
                return;
            }

            // ── 피치 계산 ────────────────────────────
            float pitch = Mathf.Clamp((_engine.RPM / baseRpm) * pitchMultiplier,
                                      pitchMin, pitchMax);
            _srcIdle.pitch  = pitch;
            _srcAccel.pitch = pitch;
            _srcDecel.pitch = pitch;

            // ── 크로스페이드 ─────────────────────────
            // throttle 0 = idle/decel, throttle 1 = accel
            float throttle = _engine.ThrottleInput;
            bool  decel    = throttle < 0.05f;

            float targetIdle  = decel ? 1.0f : Mathf.Clamp01(1.0f - throttle * 2f);
            float targetAccel = Mathf.Clamp01(throttle * 2f - 0.2f);
            float targetDecel = decel ? Mathf.Clamp01(1.0f - throttle * 20f) : 0f;

            FadeSource(_srcIdle,  targetIdle  * masterVolume);
            FadeSource(_srcAccel, targetAccel * masterVolume);
            FadeSource(_srcDecel, targetDecel * masterVolume);
        }

        // ── 헬퍼 ─────────────────────────────────────

        void FadeSource(AudioSource src, float targetVol)
        {
            src.volume = Mathf.MoveTowards(src.volume, targetVol,
                                           Time.deltaTime * crossfadeSpeed);
            // 볼륨 0에 가까우면 재생 멈춰 CPU 아낌
            if (src.volume < 0.005f && src.isPlaying)
                src.Pause();
            else if (src.volume >= 0.005f && !src.isPlaying)
                src.UnPause();
        }

        void PlayOneShot(AudioClip clip)
        {
            if (clip == null) return;
            _srcOneShot.PlayOneShot(clip, masterVolume);
        }

        AudioSource CreateSource(string goName, AudioClip clip, bool loop)
        {
            var go  = new GameObject(goName);
            go.transform.SetParent(transform, false);

            var src             = go.AddComponent<AudioSource>();
            src.clip            = clip;
            src.loop            = loop;
            src.volume          = 0f;
            src.pitch           = 1f;
            src.playOnAwake     = false;
            src.spatialBlend    = spatialBlend;
            src.minDistance     = minDistance;
            src.maxDistance     = maxDistance;
            src.rolloffMode     = AudioRolloffMode.Logarithmic;
            src.dopplerLevel    = 0.3f;
            src.priority        = 64;

            return src;
        }
    }
}
