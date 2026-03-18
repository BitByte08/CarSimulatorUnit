using UnityEngine;
using CarSim.CAN;

namespace CarSim.Vehicle
{
    /// <summary>
    /// 차량 라이트 제어
    /// - 헤드라이트(하향등/상향등), 브레이크등, 방향지시등, 비상등
    /// - SwitchPanelHandler와 VehicleController에서 입력을 읽음
    /// </summary>
    public class VehicleLights : MonoBehaviour
    {
        [Header("헤드라이트 (하향등 / 상향등 공용)")]
        [SerializeField] Light[] headlights;
        [SerializeField] float   headlightLowIntensity  = 200f;
        [SerializeField] float   headlightHighIntensity = 600f;
        [SerializeField] float   headlightSpotAngle     = 80f;   // 빛 퍼짐 (spotAngle)
        [SerializeField] float   headlightInnerPercent  = 0.3f;  // innerSpotAngle = spotAngle * this (0~1)
        [SerializeField] float   headlightLowPitch      = 5f;    // 하향등 수직 각도 (도)
        [SerializeField] float   headlightHighPitch      = -2f;  // 상향등 수직 각도 (도)

        [Header("브레이크 등")]
        [SerializeField] Light[]    brakeLights;
        [SerializeField] Renderer[] brakeLightEmissive;          // 에미시브 머티리얼 (선택)
        [SerializeField] Color      brakeEmissiveColor = new Color(1f, 0.05f, 0.05f) * 3f;
        [SerializeField] float      brakeThreshold = 0.05f;

        [Header("방향지시등")]
        [SerializeField] Light[] turnSignalLeft;
        [SerializeField] Light[] turnSignalRight;
        [SerializeField] float   blinkInterval = 0.5f;           // ON/OFF 주기(초)

        // ── 내부 참조 ────────────────────────────────
        SwitchPanelHandler _switches;
        VehicleController  _vehicle;

        float _blinkTimer;
        bool  _blinkOn;

        static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            _switches = FindObjectOfType<SwitchPanelHandler>();
            _vehicle  = GetComponentInParent<VehicleController>();
            if (_vehicle == null)
                _vehicle = FindObjectOfType<VehicleController>();
        }

        void Update()
        {
            if (_switches == null || _vehicle == null) return;

            if (!_switches.IgnitionOn)
            {
                SetAllOff();
                return;
            }

            UpdateHeadlights();
            UpdateBrakeLights();
            UpdateBlinkers();
        }

        void SetAllOff()
        {
            foreach (var light in headlights)   if (light) light.enabled = false;
            foreach (var light in brakeLights)  if (light) light.enabled = false;
            foreach (var light in turnSignalLeft)  if (light) light.enabled = false;
            foreach (var light in turnSignalRight) if (light) light.enabled = false;
        }

        // ── 헤드라이트 ───────────────────────────────

        void UpdateHeadlights()
        {
            bool  on        = _switches.HeadLight;
            bool  high      = _switches.HighBeam;
            float intensity = high ? headlightHighIntensity : headlightLowIntensity;
            float pitch     = high ? headlightHighPitch     : headlightLowPitch;

            foreach (var light in headlights)
            {
                if (light == null) continue;
                light.enabled        = on;
                light.intensity      = intensity;
                light.spotAngle      = headlightSpotAngle;
                light.innerSpotAngle = headlightSpotAngle * headlightInnerPercent;

                // 수직 방향(위아래) 조절 — 부모 기준 로컬 X축 회전
                var angles = light.transform.localEulerAngles;
                angles.x = pitch;
                light.transform.localEulerAngles = angles;
            }
        }

        // ── 브레이크 등 ──────────────────────────────

        void UpdateBrakeLights()
        {
            bool on = _vehicle.BrakeInput > brakeThreshold;

            foreach (var light in brakeLights)
            {
                if (light == null) continue;
                light.enabled = on;
            }

            foreach (var r in brakeLightEmissive)
            {
                if (r == null) continue;
                // MaterialPropertyBlock을 사용해 배칭 비파괴
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor(EmissionColorID, on ? brakeEmissiveColor : Color.black);
                r.SetPropertyBlock(mpb);
            }
        }

        // ── 방향지시등 / 비상등 ──────────────────────

        void UpdateBlinkers()
        {
            _blinkTimer += Time.deltaTime;
            if (_blinkTimer >= blinkInterval)
            {
                _blinkOn    = !_blinkOn;
                _blinkTimer = 0f;
            }

            bool hazard = _switches.Hazard;

            SetBlinker(turnSignalLeft,  (hazard || _switches.TurnLeft)  && _blinkOn);
            SetBlinker(turnSignalRight, (hazard || _switches.TurnRight) && _blinkOn);
        }

        static void SetBlinker(Light[] lights, bool on)
        {
            foreach (var light in lights)
            {
                if (light == null) continue;
                light.enabled = on;
            }
        }
    }
}
