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
        enum BoundsMode { AutoDetect, SceneCamera, Manual }

        // ── 실제 주행 도로 재질: 밝기(회색조)로 렌더 ────────────────────────────────
        static readonly string[] kRoadMaterials =
        {
            "asphalt_2_tracks",
            "asphalt_2-5_tracks",
            "asphalt_4_tracks",
            "asphalt_extra",    // 시내 도로에 사용됨 → 도로로 처리
            "asphalt_highway",
        };

        // 건물 바닥에 사용되는 재질: 파란색으로 렌더 → Python에서 완전 제외
        static readonly string[] kSquareMaterials =
        {
            "asphalt_square",
        };

        // 도로 재질별 밝기: 고속도로일수록 밝게 → Python 쪽에서 도로 등급 구분 가능
        static readonly System.Collections.Generic.Dictionary<string, float> kRoadBrightness =
            new System.Collections.Generic.Dictionary<string, float>
            {
                { "asphalt_highway",    1.00f },  // 고속도로 (가장 밝음)
                { "asphalt_4_tracks",   0.80f },  // 4차선
                { "asphalt_2-5_tracks", 0.65f },  // 2.5차선
                { "asphalt_2_tracks",   0.65f },  // 2차선
                { "asphalt_extra",      0.60f },  // 시내 도로
            };

        // ── 설정 ─────────────────────────────────────────────────────────────
        BoundsMode boundsMode = BoundsMode.AutoDetect;
        string boundsCamera = "MapBoundsCamera";
        float worldMinX = -400f, worldMaxX = 450f;
        float worldMinZ = -200f, worldMaxZ = 240f;
        float cameraY   = 700f;
        float boundsPadding = 20f;
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
            GUILayout.Label("Bounds Source", EditorStyles.boldLabel);
            boundsMode = (BoundsMode)GUILayout.Toolbar((int)boundsMode,
                new[] { "Auto Detect", "Scene Camera", "Manual" });
            EditorGUILayout.Space(4);

            switch (boundsMode)
            {
                case BoundsMode.AutoDetect:
                    DrawAutoDetectMode();
                    break;
                case BoundsMode.SceneCamera:
                    DrawSceneCameraMode();
                    break;
                case BoundsMode.Manual:
                    DrawManualMode();
                    break;
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

        void DrawAutoDetectMode()
        {
            boundsPadding = EditorGUILayout.FloatField("Bounds Padding", boundsPadding);
            if (GUILayout.Button("도로 범위 자동 스캔"))
                AutoDetectRoadBounds();

            EditorGUILayout.HelpBox(
                "Bake Road Mask를 누르면 도로 재질(asphalt_*) 범위를 다시 스캔해서 카메라 중심, 크기, 높이를 자동 설정합니다.",
                MessageType.Info);
            DrawReadonlyBounds();
        }

        void DrawSceneCameraMode()
        {
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
                EditorGUILayout.HelpBox(
                    $"씬에서 '{boundsCamera}' Orthographic 카메라를 찾을 수 없습니다.",
                    MessageType.Warning);
            }
        }

        void DrawManualMode()
        {
            GUILayout.Label("Manual Bounds", EditorStyles.boldLabel);
            worldMinX = EditorGUILayout.FloatField("Min X", worldMinX);
            worldMaxX = EditorGUILayout.FloatField("Max X", worldMaxX);
            worldMinZ = EditorGUILayout.FloatField("Min Z", worldMinZ);
            worldMaxZ = EditorGUILayout.FloatField("Max Z", worldMaxZ);
            cameraY   = EditorGUILayout.FloatField("Camera Y", cameraY);
        }

        void DrawReadonlyBounds()
        {
            GUI.enabled = false;
            EditorGUILayout.FloatField("Min X", worldMinX);
            EditorGUILayout.FloatField("Max X", worldMaxX);
            EditorGUILayout.FloatField("Min Z", worldMinZ);
            EditorGUILayout.FloatField("Max Z", worldMaxZ);
            EditorGUILayout.FloatField("Camera Y", cameraY);
            GUI.enabled = true;
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

        bool AutoDetectRoadBounds()
        {
            var allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            bool init = false;
            Bounds roadBounds = default;
            float maxY = float.MinValue;

            foreach (var rend in allRenderers)
            {
                if (!IsAnyAsphaltRenderer(rend, out _)) continue;

                if (!init)
                {
                    roadBounds = rend.bounds;
                    init = true;
                }
                else
                {
                    roadBounds.Encapsulate(rend.bounds);
                }

                if (rend.bounds.max.y > maxY)
                    maxY = rend.bounds.max.y;
            }

            if (!init)
            {
                Debug.LogWarning("[RoadMaskBaker] 도로 재질(asphalt_*) Renderer 를 찾지 못했습니다.");
                return false;
            }

            float padding = Mathf.Max(0f, boundsPadding);
            worldMinX = roadBounds.min.x - padding;
            worldMaxX = roadBounds.max.x + padding;
            worldMinZ = roadBounds.min.z - padding;
            worldMaxZ = roadBounds.max.z + padding;
            cameraY   = maxY + 200f;

            Debug.Log($"[RoadMaskBaker] Auto Detect 완료 -> X[{worldMinX:F1}, {worldMaxX:F1}] Z[{worldMinZ:F1}, {worldMaxZ:F1}] Y={cameraY:F1}");
            Repaint();
            return true;
        }

        void BakeRoadMask()
        {
            if (boundsMode == BoundsMode.AutoDetect)
            {
                // StyledMapTileBaker가 저장한 tiles/bounds.json 있으면 그 bounds로 덮어쓰기
                string styledBoundsPath = Path.Combine(
                    Path.GetDirectoryName(outputPath) ?? "", "styled_tiles", "bounds.json");
                if (File.Exists(styledBoundsPath))
                {
                    try
                    {
                        var styledBounds = JsonUtility.FromJson<StyledBounds>(
                            File.ReadAllText(styledBoundsPath));
                        worldMinX = (float)(styledBounds.min_x - 20);
                        worldMaxX = (float)(styledBounds.max_x + 20);
                        worldMinZ = (float)(styledBounds.min_z - 20);
                        worldMaxZ = (float)(styledBounds.max_z + 20);
                        Debug.Log($"[RoadMaskBaker] StyledMapTileBaker bounds 감지: X[{worldMinX:F1},{worldMaxX:F1}] Z[{worldMinZ:F1},{worldMaxZ:F1}]");
                    }
                    catch {}
                }

                if (!AutoDetectRoadBounds())
                {
                    EditorUtility.DisplayDialog("RoadMaskBaker",
                        "도로 재질(asphalt_*) Renderer 를 찾지 못했습니다.", "확인");
                    return;
                }
            }
            else if (boundsMode == BoundsMode.SceneCamera)
            {
                if (!TryReadCamera(out float minX, out float maxX,
                                   out float minZ, out float maxZ, out float camH))
                {
                    EditorUtility.DisplayDialog("RoadMaskBaker",
                        $"'{boundsCamera}' 카메라를 찾을 수 없습니다.", "확인");
                    return;
                }

                worldMinX = minX; worldMaxX = maxX;
                worldMinZ = minZ; worldMaxZ = maxZ;
                cameraY = camH;
            }

            // 1. 씬의 모든 Renderer 비활성화
            var allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (var rend in allRenderers)
                rend.enabled = false;

            // 2. city_part_collider MeshCollider 메시를 임시 MeshRenderer로 렌더
            //    → 재질 이름에 의존하지 않고 콜라이더 메시(도로와 완벽히 일치)를 사용
            var tempObjects = new List<GameObject>();
            var allColliders = FindObjectsByType<MeshCollider>(FindObjectsSortMode.None);
            foreach (var mc in allColliders)
            {
                if (mc.sharedMesh == null) continue;
                if (!mc.gameObject.name.Contains("city_part_collider")) continue;

                var tempGo = new GameObject("__RoadMeshTemp__");
                tempGo.hideFlags = HideFlags.HideAndDontSave;
                tempGo.transform.position   = mc.transform.position;
                tempGo.transform.rotation   = mc.transform.rotation;
                tempGo.transform.localScale = mc.transform.lossyScale;

                var mf = tempGo.AddComponent<MeshFilter>();
                mf.sharedMesh = mc.sharedMesh;

                var mr = tempGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = BuildFlatMaterial(new Color(1f, 1f, 1f, 1f));

                tempObjects.Add(tempGo);
                Debug.Log($"[RoadMaskBaker] 도로 콜라이더 렌더: {mc.gameObject.name}");
            }

            if (tempObjects.Count == 0)
            {
                foreach (var rend in allRenderers) if (rend != null) rend.enabled = true;
                EditorUtility.DisplayDialog("RoadMaskBaker",
                    "city_part_collider MeshCollider를 찾지 못했습니다.\n" +
                    "씬에 demo_city_by_versatile_studio 프리팹이 있는지 확인하세요.", "확인");
                return;
            }

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

                string boundsPath = Path.ChangeExtension(outputPath, ".bounds.json");
                string boundsJson =
                    $"{{\"min_x\":{worldMinX:F3},\"max_x\":{worldMaxX:F3}," +
                    $"\"min_z\":{worldMinZ:F3},\"max_z\":{worldMaxZ:F3}," +
                    $"\"camera_y\":{cameraY:F3},\"image_size\":{imageSize}}}";
                File.WriteAllText(boundsPath, boundsJson);

                cam.targetTexture    = null;
                RenderTexture.active = null;
                DestroyImmediate(rt);
                DestroyImmediate(tex);

                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Road Mask Baker",
                    $"완료!\n{outputPath}\n{imageSize}×{imageSize} px\n" +
                    $"bounds: X[{worldMinX:F1},{worldMaxX:F1}] Z[{worldMinZ:F1},{worldMaxZ:F1}]\n\n" +
                    "다음 단계:\n  bash tools/bake_map.sh", "확인");
                Debug.Log($"[RoadMaskBaker] 저장 완료: {outputPath}");
                Debug.Log($"[RoadMaskBaker] bounds sidecar: {boundsPath}");
            }
            finally
            {
                // 7. 복원
                foreach (var go in tempObjects)
                    if (go != null) DestroyImmediate(go);
                foreach (var rend in allRenderers)
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
                string matName = CleanMaterialName(mat.name);
                foreach (var name in kRoadMaterials)
                {
                    if (matName == name) { matchedMat = name; return true; }
                }
            }
            return false;
        }

        // AutoDetectRoadBounds 용: 실제 도로 + 광장 재질 모두 포함
        static bool IsAnyAsphaltRenderer(Renderer rend, out string matchedMat)
        {
            if (IsRoadRenderer(rend, out matchedMat)) return true;
            if (IsSquareRenderer(rend)) { matchedMat = "asphalt_square"; return true; }
            return false;
        }

        static bool IsSquareRenderer(Renderer rend)
        {
            foreach (var mat in rend.sharedMaterials)
            {
                if (mat == null) continue;
                string matName = CleanMaterialName(mat.name);
                foreach (var name in kSquareMaterials)
                {
                    if (matName == name) return true;
                }
            }
            return false;
        }

        static Material[] BuildFlatMaterials(Material[] origMats, bool isSquare)
        {
            var result = new Material[origMats.Length];
            for (int i = 0; i < result.Length; i++)
            {
                Color color;
                if (isSquare)
                {
                    // 파란색: 교차로/광장 표시 → Python이 blue 채널로 감지 후
                    // 실제 도로에 연결된 부분만 도로망에 포함
                    color = new Color(0f, 0f, 1f, 1f);
                }
                else
                {
                    string matName = origMats[i] != null ? CleanMaterialName(origMats[i].name) : "";
                    float brightness = kRoadBrightness.TryGetValue(matName, out float b) ? b : 0f;
                    color = new Color(brightness, brightness, brightness, 1f);
                }
                result[i] = BuildFlatMaterial(color);
            }
            return result;
        }

        static Material BuildFlatMaterial(Color color)
        {
            var flatMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            flatMat.color = color;
            return flatMat;
        }

        static string CleanMaterialName(string name)
        {
            return name.Replace(" (Instance)", "").Trim();
        }

        [System.Serializable]
        private class StyledBounds
        {
            public double min_x;
            public double max_x;
            public double min_z;
            public double max_z;
        }
    }
}
