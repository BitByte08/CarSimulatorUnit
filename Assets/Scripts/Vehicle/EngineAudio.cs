using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CarSim.Vehicle
{
    /// <summary>
    /// 엔진 오디오 시스템
    /// - 시동(크랭킹), 아이들링, 주행(RPM 기반 피치/볼륨), 바람, 스키드 사운드
    /// - Inspector에서 컴포넌트 추가 시 또는 우클릭 > Auto-Assign Audio Clips 으로 자동 할당
    /// </summary>
    [RequireComponent(typeof(Engine))]
    [AddComponentMenu("CarSim/Vehicle/Engine Audio")]
    public class EngineAudio : MonoBehaviour
    {
        [Header("엔진 사운드 클립")]
        [SerializeField] AudioClip clipEngineStart;      // 크랭킹 시작 (engine start 01)
        [SerializeField] AudioClip clipEngineStarted;    // 시동 성공 (engine start 02)
        [SerializeField] AudioClip clipEngineLoop;       // 아이들/주행 루프 (engine loop)
        [SerializeField] AudioClip clipEngineHighRpm;    // 고RPM 레이어 (Car Engine Run 01)
        [SerializeField] AudioClip clipRumble;           // 저RPM 럼블 (car rumble)
        [SerializeField] AudioClip clipTurbo;            // 터보 (turbo)
        [SerializeField] AudioClip clipGearWhine;        // 기어 와인 (gearwhine)
        [SerializeField] AudioClip clipWind;             // 바람 (wind_ext)
        [SerializeField] AudioClip clipSkid;             // 스키드 (skid loop 4)

        [Header("피치 범위 (아이들 → 레드라인)")]
        [SerializeField] float idlePitch       = 0.55f;
        [SerializeField] float maxPitch        = 2.0f;
        [SerializeField] float pitchSmooth     = 0.08f;  // SmoothDamp 시간

        [Header("볼륨")]
        [SerializeField] float engineVolume    = 1.0f;
        [SerializeField] float turboMaxVolume  = 0.45f;
        [SerializeField] float gearMaxVolume   = 0.20f;
        [SerializeField] float windMaxVolume   = 0.55f;
        [SerializeField] float skidMaxVolume   = 0.85f;

        [Header("3D 공간 설정")]
        [SerializeField] float minDistance     = 2f;
        [SerializeField] float maxDistance     = 60f;

        // ── 런타임 AudioSource (자식 GameObject에 자동 생성) ─────────────────
        AudioSource _srcStart;
        AudioSource _srcLoop;
        AudioSource _srcHighRpm;
        AudioSource _srcRumble;
        AudioSource _srcTurbo;
        AudioSource _srcGear;
        AudioSource _srcWind;
        AudioSource _srcSkid;

        // ── 컴포넌트 참조 ────────────────────────────────────────────────────
        Engine            _engine;
        VehicleController _vehicle;

        // ── 내부 상태 ────────────────────────────────────────────────────────
        float _currentPitch;
        float _pitchVelocity;
        Engine.EngineState _prevState = Engine.EngineState.Off;

        // ═════════════════════════════════════════════════════════════════════

        void Awake()
        {
            _engine  = GetComponent<Engine>();
            _vehicle = GetComponent<VehicleController>();

            CreateAudioSources();
        }

        void Start()
        {
            // Awake에서 만든 소스에 클립 연결
            SetClip(_srcLoop,    clipEngineLoop);
            SetClip(_srcHighRpm, clipEngineHighRpm);
            SetClip(_srcRumble,  clipRumble);
            SetClip(_srcTurbo,   clipTurbo);
            SetClip(_srcGear,    clipGearWhine);
            SetClip(_srcWind,    clipWind);
            SetClip(_srcSkid,    clipSkid);

            _currentPitch = idlePitch;
        }

        void Update()
        {
            if (_engine == null) return;

            HandleStateTransition();
            UpdateEngineAudio();
            UpdateExteriorAudio();
        }

        // ── 상태 전환 감지 ────────────────────────────────────────────────────

        void HandleStateTransition()
        {
            var state = _engine.State;
            if (state == _prevState) return;

            switch (state)
            {
                case Engine.EngineState.Cranking:
                    // 시동 버튼 → 크랭킹 사운드 재생
                    PlayClip(_srcStart, clipEngineStart);
                    break;

                case Engine.EngineState.Running:
                    // 시동 성공 → 크랭킹 사운드 즉시 중단 + 시동 완료 사운드 + 루프 시작
                    PlayClip(_srcStart, clipEngineStarted);
                    StartEngineLayers();
                    break;

                case Engine.EngineState.Off:
                    // 시동 꺼짐 → 모든 루프 정지
                    _srcStart?.Stop();
                    StopEngineLayers();
                    break;
            }

            _prevState = state;
        }

        // ── 메인 엔진 오디오 업데이트 (Running 시) ───────────────────────────

        void UpdateEngineAudio()
        {
            if (!_engine.IsRunning)
            {
                // 꺼지는 중: 피치/볼륨 서서히 감소
                if (_srcLoop != null && _srcLoop.isPlaying)
                {
                    _srcLoop.volume = Mathf.MoveTowards(_srcLoop.volume, 0f, Time.deltaTime * 3f);
                    _srcLoop.pitch  = Mathf.MoveTowards(_srcLoop.pitch,  0.2f, Time.deltaTime * 2f);
                    if (_srcLoop.volume <= 0f) _srcLoop.Stop();
                }
                return;
            }

            float idle     = _engine.IdleRpm;
            float redline  = _engine.RedlineRpm;
            float rpm      = _engine.RPM;
            float rpmNorm  = Mathf.Clamp01((rpm - idle) / (redline - idle));
            float throttle = _engine.ThrottleInput;

            // 피치: SmoothDamp로 부드럽게 추종
            float targetPitch = Mathf.Lerp(idlePitch, maxPitch, rpmNorm);
            _currentPitch = Mathf.SmoothDamp(_currentPitch, targetPitch, ref _pitchVelocity, pitchSmooth);

            // 메인 루프 (아이들 ~ 전 RPM)
            if (_srcLoop != null)
            {
                _srcLoop.pitch  = _currentPitch;
                // 고RPM로 갈수록 메인 루프는 약해지고 highRpm 레이어가 대신함
                _srcLoop.volume = Mathf.Lerp(1.0f, 0.35f, rpmNorm) * engineVolume;
            }

            // 고RPM 레이어 (rpmNorm 0.3 → 1.0 페이드인)
            if (_srcHighRpm != null)
            {
                _srcHighRpm.pitch  = _currentPitch;
                _srcHighRpm.volume = Mathf.Clamp01((rpmNorm - 0.3f) / 0.5f) * engineVolume;
            }

            // 저RPM 럼블 (rpmNorm 0 → 0.35 구간)
            if (_srcRumble != null)
            {
                _srcRumble.pitch  = Mathf.Lerp(0.45f, 0.85f, rpmNorm);
                _srcRumble.volume = Mathf.Clamp01(1f - rpmNorm / 0.35f) * 0.65f * engineVolume;
            }

            // 터보 (고RPM + 스로틀 입력)
            if (_srcTurbo != null)
            {
                float turboBlend = Mathf.Clamp01((rpmNorm - 0.45f) / 0.35f) * throttle;
                _srcTurbo.pitch  = Mathf.Lerp(0.75f, 1.35f, rpmNorm);
                _srcTurbo.volume = turboBlend * turboMaxVolume;
            }

            // 기어 와인 (속도 비례, 엔진 결합 시)
            if (_srcGear != null && _vehicle != null)
            {
                float speedNorm  = Mathf.Clamp01(_vehicle.SpeedKph / 120f);
                _srcGear.pitch   = Mathf.Lerp(0.5f, 2.2f, speedNorm);
                _srcGear.volume  = speedNorm * gearMaxVolume;
            }
        }

        // ── 외부 사운드 (속도/스키드 - 엔진 상태 무관) ───────────────────────

        void UpdateExteriorAudio()
        {
            if (_vehicle == null) return;

            float speedKph  = _vehicle.SpeedKph;
            float windNorm  = Mathf.Clamp01(speedKph / 160f);
            float windCurve = windNorm * windNorm; // 제곱 커브 (저속엔 거의 안 들림)

            // 바람 소리
            if (_srcWind != null && clipWind != null)
            {
                if (windCurve > 0.02f)
                {
                    if (!_srcWind.isPlaying) _srcWind.Play();
                    _srcWind.volume = windCurve * windMaxVolume;
                    _srcWind.pitch  = Mathf.Lerp(0.7f, 1.6f, windNorm);
                }
                else if (_srcWind.isPlaying)
                {
                    _srcWind.Stop();
                }
            }

            // 스키드 (휠 슬립 감지)
            UpdateSkidAudio();
        }

        void UpdateSkidAudio()
        {
            if (_srcSkid == null || clipSkid == null || _vehicle == null) return;

            float maxSlip = 0f;
            foreach (var wheel in _vehicle.GetAllWheels())
            {
                if (wheel == null) continue;
                if (!wheel.GetGroundHit(out WheelHit hit)) continue;
                float slip = Mathf.Sqrt(hit.forwardSlip * hit.forwardSlip + hit.sidewaysSlip * hit.sidewaysSlip);
                if (slip > maxSlip) maxSlip = slip;
            }

            float skidBlend = Mathf.Clamp01((maxSlip - 0.35f) / 0.5f);

            if (skidBlend > 0.01f)
            {
                if (!_srcSkid.isPlaying) _srcSkid.Play();
                _srcSkid.volume = skidBlend * skidMaxVolume;
                _srcSkid.pitch  = Mathf.Lerp(0.8f, 1.3f, skidBlend);
            }
            else if (_srcSkid.isPlaying)
            {
                _srcSkid.Stop();
            }
        }

        // ── 루프 레이어 시작/정지 ────────────────────────────────────────────

        void StartEngineLayers()
        {
            PlayLoop(_srcLoop,    clipEngineLoop);
            PlayLoop(_srcHighRpm, clipEngineHighRpm);
            PlayLoop(_srcRumble,  clipRumble);
            PlayLoop(_srcTurbo,   clipTurbo);
            PlayLoop(_srcGear,    clipGearWhine);
        }

        void StopEngineLayers()
        {
            // 메인 루프는 Update에서 서서히 페이드아웃 처리
            _srcHighRpm?.Stop();
            _srcRumble?.Stop();
            _srcTurbo?.Stop();
            _srcGear?.Stop();
        }

        // ── 헬퍼 ─────────────────────────────────────────────────────────────

        // 이전 재생을 중단하고 새 클립을 즉시 재생 (상태 전환 사운드용)
        void PlayClip(AudioSource src, AudioClip clip)
        {
            if (src == null || clip == null) return;
            src.Stop();
            src.clip = clip;
            src.Play();
        }

        void PlayLoop(AudioSource src, AudioClip clip)
        {
            if (src == null || clip == null) return;
            if (src.clip != clip) src.clip = clip;
            if (!src.isPlaying) src.Play();
        }

        static void SetClip(AudioSource src, AudioClip clip)
        {
            if (src == null || clip == null) return;
            src.clip = clip;
        }

        void CreateAudioSources()
        {
            _srcStart   = MakeSource("Snd_EngineStart",   loop: false, vol: 1.0f, blend: 0.6f);
            _srcLoop    = MakeSource("Snd_EngineLoop",    loop: true,  vol: 0f,   blend: 1.0f);
            _srcHighRpm = MakeSource("Snd_EngineHighRPM", loop: true,  vol: 0f,   blend: 1.0f);
            _srcRumble  = MakeSource("Snd_Rumble",        loop: true,  vol: 0f,   blend: 1.0f);
            _srcTurbo   = MakeSource("Snd_Turbo",         loop: true,  vol: 0f,   blend: 1.0f);
            _srcGear    = MakeSource("Snd_GearWhine",     loop: true,  vol: 0f,   blend: 1.0f);
            _srcWind    = MakeSource("Snd_Wind",          loop: true,  vol: 0f,   blend: 0.4f);
            _srcSkid    = MakeSource("Snd_Skid",          loop: true,  vol: 0f,   blend: 1.0f);
        }

        AudioSource MakeSource(string goName, bool loop, float vol, float blend)
        {
            var go  = new GameObject(goName);
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;

            var src            = go.AddComponent<AudioSource>();
            src.loop           = loop;
            src.volume         = vol;
            src.spatialBlend   = blend;
            src.rolloffMode    = AudioRolloffMode.Logarithmic;
            src.minDistance    = minDistance;
            src.maxDistance    = maxDistance;
            src.playOnAwake    = false;
            src.dopplerLevel   = 0.3f;
            return src;
        }

        // ── 에디터 전용: 자동 클립 할당 ──────────────────────────────────────
#if UNITY_EDITOR
        [ContextMenu("Auto-Assign Audio Clips")]
        void AutoAssignClips()
        {
            clipEngineStart   = FindAudioClip("engine start 01");
            clipEngineStarted = FindAudioClip("engine start 02");
            clipEngineLoop    = FindAudioClip("engine loop");
            clipEngineHighRpm = FindAudioClip("Car Engine Run 01");
            clipRumble        = FindAudioClip("car rumble");
            clipTurbo         = FindAudioClip("turbo");
            clipGearWhine     = FindAudioClip("gearwhine");
            clipWind          = FindAudioClip("wind_ext");
            clipSkid          = FindAudioClip("skid loop 4");

            EditorUtility.SetDirty(this);
            Debug.Log("[EngineAudio] 오디오 클립 자동 할당 완료!");
        }

        static AudioClip FindAudioClip(string namePart)
        {
            string[] guids = AssetDatabase.FindAssets($"t:AudioClip {namePart}", new[] { "Assets/Audio" });
            if (guids.Length == 0)
            {
                // Assets 전체에서 재탐색
                guids = AssetDatabase.FindAssets($"t:AudioClip {namePart}");
            }
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // 컴포넌트를 Inspector에서 처음 추가할 때 자동 실행
        void Reset() => AutoAssignClips();
#endif
    }
}
