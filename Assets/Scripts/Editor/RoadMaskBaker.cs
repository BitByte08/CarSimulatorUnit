using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

namespace CarSim.Editor
{
    /// <summary>
    /// Demo City에서 도로(asphalt) 재질 오브젝트만 흰색으로 렌더해서
    /// 단일 고해상도 road_mask.png 를 추출한다.
    ///
    /// 이 마스크를 tools/extract_road_graph.py 로 처리하면
    /// 네비게이션용 road_graph.json 이 생성된다.
    ///
    /// 출력: Assets/StreamingAssets/road_mask.png  (imageSize × imageSize, RGBA)
    ///
    /// 사용법: 상단 메뉴 CarSim → Bake Road Mask
    /// </summary>
    public class RoadMaskBaker : EditorWindow
    {
        // ── 도로 재질 이름 목록 ──────────────────────────────────────────────────
        static readonly string[] kRoadMaterials =
        {
            "asphalt_2_tracks",
            "asphalt_2-5_tracks",
            "asphalt_4_tracks",
            "asphalt_square",
            "asphalt_extra",
            "asphalt_highway",
        };

        // 재질별 밝기: 고속도로일수록 밝게 → Python 쪽에서 도로 등급 구분 가능
        static readonly System.Collections.Generic.Dictionary<string, float> kRoadBrightness =
            new System.Collections.Generic.Dictionary<string, float>
            {
                { "asphalt_highway",    1.00f },  // 고속도로 (가장 밝음)
                { "asphalt_4_tracks",   0.80f },  // 4차선
                { "asphalt_2-5_tracks", 0.65f },  // 2.5차선
                { "asphalt_2_tracks",   0.65f },  // 2차선
                { "asphalt_square",     0.60f },  // 교차로/광장
                { "asphalt_extra",      0.55f },  // 기타
            };

        // ── 설정 ─────────────────────────────────────────────────────────────
        string boundsCamera = "MapBoundsCamera";
        float worldMinX = -400f, worldMaxX = 450f;
        float worldMinZ = -200f, worldMaxZ = 240f;
        float cameraY   = 700f;
        int   imageSize = 8192;
        string outputPath = "Assets/StreamingAssets/road_mask.png";

        [MenuItem("CarSim/Bake Road Mask")]
        public static void ShowWindow() => GetWindow<RoadMaskBaker>("Road Mask Baker");

        void OnGUI()
        {
            GUILayout.Label("Road Mask Baker", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "아스팔트 재질 오브젝트만 흰색으로 렌더해서 road_mask.png 를 생성합니다.\n" +
                "이후 tools/extract_road_graph.py 로 road_graph.json 을 추출하세요.",
                MessageType.Info);

            EditorGUILayout.Space(6);
            GUILayout.Label("Bounds Camera", EditorStyles.boldLabel);
            boundsCamera = EditorGUILayout.TextField("Camera Name", boundsCamera);

            bool found = TryReadCamera(out float minX, out float maxX,
                                       out float minZ, out float maxZ, out float camH);
            if (found)
            {
                worldMinX = minX; worldMaxX = maxX;
                worldMinZ = minZ; worldMaxZ = maxZ;
                cameraY   = camH;
                EditorGUILayout.HelpBox(
                    $"X: {minX:F0} ~ {maxX:F0}  Z: {minZ:F0} ~ {maxZ:F0}  Y: {camH:F0}",
                    MessageType.None);
            }
            else
            {
                GUILayout.Label("Manual Bounds", EditorStyles.boldLabel);
                worldMinX = EditorGUILayout.FloatField("Min X", worldMinX);
                worldMaxX = EditorGUILayout.FloatField("Max X", worldMaxX);
                worldMinZ = EditorGUILayout.FloatField("Min Z", worldMinZ);
                worldMaxZ = EditorGUILayout.FloatField("Max Z", worldMaxZ);
                cameraY   = EditorGUILayout.FloatField("Camera Y", cameraY);
            }

            EditorGUILayout.Space(6);
            imageSize  = EditorGUILayout.IntField("Image Size (px)", imageSize);
            outputPath = EditorGUILayout.TextField("Output Path", outputPath);

            EditorGUILayout.Space(4);
            float worldW = worldMaxX - worldMinX;
            float pxPerUnit = imageSize / worldW;
            EditorGUILayout.HelpBox(
                $"해상도: {pxPerUnit:F1} px/unit   파일: ~{imageSize * imageSize * 4 / 1024 / 1024} MB",
                MessageType.Info);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Bake Road Mask", GUILayout.Height(36)))
                BakeRoadMask();
        }

        bool TryReadCamera(out float minX, out float maxX,
                           out float minZ, out float maxZ, out float camH)
        {
            minX = maxX = minZ = maxZ = camH = 0f;
            var go = GameObject.Find(boundsCamera);
            if (go == null) return false;
            var cam = go.GetComponent<UnityEngine.Camera>();
            if (cam == null || !cam.orthographic) return false;
            Vector3 pos = go.transform.position;
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            minX = pos.x - halfW; maxX = pos.x + halfW;
            minZ = pos.z - halfH; maxZ = pos.z + halfH;
            camH = pos.y;
            return true;
        }

        void BakeRoadMask()
        {
            // 1. 씬의 모든 Renderer 수집
            var allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            // 2. 도로 Renderer 분류
            var roadRenderers  = new List<(Renderer r, Material[] origMats)>();
            var otherRenderers = new List<Renderer>();

            foreach (var rend in allRenderers)
            {
                if (IsRoadRenderer(rend, out string matName))
                {
                    roadRenderers.Add((rend, rend.sharedMaterials));
                    // 도로 밝기 결정
                    float brightness = kRoadBrightness.TryGetValue(matName, out float b) ? b : 0.6f;
                    rend.sharedMaterials = BuildFlatMaterials(rend.sharedMaterials, matName, brightness);
                }
                else
                {
                    otherRenderers.Add(rend);
                }
            }

            // 3. 비도로 오브젝트 비활성화
            foreach (var rend in otherRenderers)
                rend.enabled = false;

            // 4. 카메라 설정
            var camGo = new GameObject("__RoadMaskCamera__");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.orthographic    = true;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask     = ~0;
            cam.nearClipPlane   = 0.3f;
            cam.farClipPlane    = cameraY + 500f;

            var urpData = camGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderShadows       = false;
            urpData.requiresColorOption = CameraOverrideOption.Off;
            urpData.requiresDepthOption = CameraOverrideOption.Off;

            float worldW = worldMaxX - worldMinX;
            float worldH = worldMaxZ - worldMinZ;
            cam.transform.position = new Vector3(
                (worldMinX + worldMaxX) / 2f, cameraY,
                (worldMinZ + worldMaxZ) / 2f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.orthographicSize   = worldH / 2f;
            cam.aspect             = worldW / worldH;

            try
            {
                // 5. 렌더
                var rt = new RenderTexture(imageSize, imageSize, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                var tex = new Texture2D(imageSize, imageSize, TextureFormat.ARGB32, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, imageSize, imageSize), 0, 0);
                tex.Apply();

                // 6. 저장
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(outputPath, tex.EncodeToPNG());

                cam.targetTexture    = null;
                RenderTexture.active = null;
                DestroyImmediate(rt);
                DestroyImmediate(tex);

                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Road Mask Baker",
                    $"완료!\n{outputPath}\n{imageSize}×{imageSize} px\n\n" +
                    "다음 단계:\n  python tools/extract_road_graph.py", "확인");
                Debug.Log($"[RoadMaskBaker] 저장 완료: {outputPath}");
            }
            finally
            {
                // 7. 복원
                foreach (var (rend, origMats) in roadRenderers)
                    if (rend != null) rend.sharedMaterials = origMats;
                foreach (var rend in otherRenderers)
                    if (rend != null) rend.enabled = true;
                DestroyImmediate(camGo);
                EditorUtility.ClearProgressBar();
            }
        }

        static bool IsRoadRenderer(Renderer rend, out string matchedMat)
        {
            matchedMat = null;
            foreach (var mat in rend.sharedMaterials)
            {
                if (mat == null) continue;
                foreach (var name in kRoadMaterials)
                {
                    if (mat.name == name)
                    {
                        matchedMat = name;
                        return true;
                    }
                }
            }
            return false;
        }

        static Material[] BuildFlatMaterials(Material[] origMats, string roadMatName, float brightness)
        {
            // URP Unlit 셰이더로 단색 재질 생성
            var flatMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            flatMat.color = new Color(brightness, brightness, brightness, 1f);
            var result = new Material[origMats.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = flatMat;
            return result;
        }
    }
}
