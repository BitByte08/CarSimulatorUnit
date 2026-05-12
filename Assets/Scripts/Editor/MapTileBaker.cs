using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using System.IO;

namespace CarSim.Editor
{
    /// <summary>
    /// Demo City 월드를 위에서 내려다보는 카메라로 베이크해 슬리피맵 타일로 저장.
    ///
    /// 타일 포맷: {outputPath}/{zoom}/{tileX}/{tileY}.png  (256×256 PNG)
    /// 좌표 규약:
    ///   tileX=0, tileY=0 → 월드 북서쪽 (minX, maxZ)
    ///   tileX 증가 = East (+X), tileY 증가 = South (-Z)
    ///
    /// [Scene Camera 모드]
    ///   씬에 Orthographic 카메라를 만들고 이름을 boundsCamera 필드값으로 설정.
    ///   카메라를 맵 위에 배치하면 orthographicSize + aspect 에서 범위를 자동 계산.
    ///
    /// [Auto Detect 모드]
    ///   씬의 모든 Renderer bounds 를 합산해 범위 자동 계산.
    ///
    /// 사용법: 상단 메뉴 CarSim → Bake Map Tiles
    /// </summary>
    public class MapTileBaker : EditorWindow
    {
        enum BoundsMode { Manual, SceneCamera, AutoDetect }

        [MenuItem("CarSim/Bake Map Tiles")]
        public static void ShowWindow() => GetWindow<MapTileBaker>("Map Tile Baker");

        // ── 설정 ─────────────────────────────────────────────────────────────

        BoundsMode boundsMode   = BoundsMode.SceneCamera;
        string     boundsCamera = "MapBoundsCamera";

        // Manual / 읽기전용 미리보기용
        float worldMinX = -400f;
        float worldMaxX =  450f;
        float worldMinZ = -200f;
        float worldMaxZ =  240f;
        float cameraY   =  700f;

        int    maxZoom    = 4;
        int    tileSize   = 256;
        string outputPath = "Assets/StreamingAssets/tiles";

        // ── GUI ───────────────────────────────────────────────────────────────

        void OnGUI()
        {
            // ── 범위 결정 모드 ───────────────────────────────────────────────
            GUILayout.Label("Bounds Source", EditorStyles.boldLabel);
            boundsMode = (BoundsMode)GUILayout.Toolbar((int)boundsMode,
                new[] { "Manual", "Scene Camera", "Auto Detect" });

            EditorGUILayout.Space(4);

            switch (boundsMode)
            {
                case BoundsMode.Manual:
                    DrawManualBounds();
                    break;

                case BoundsMode.SceneCamera:
                    DrawSceneCameraMode();
                    break;

                case BoundsMode.AutoDetect:
                    DrawAutoDetectMode();
                    break;
            }

            // ── 베이크 설정 ──────────────────────────────────────────────────
            EditorGUILayout.Space(6);
            GUILayout.Label("Baker Settings", EditorStyles.boldLabel);
            maxZoom    = EditorGUILayout.IntSlider("Max Zoom Level", maxZoom, 0, 10);
            tileSize   = EditorGUILayout.IntField("Tile Size (px)", tileSize);
            outputPath = EditorGUILayout.TextField("Output Path", outputPath);

            // ── 정보 ─────────────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            float mapW     = worldMaxX - worldMinX;
            float mapH     = worldMaxZ - worldMinZ;
            int   tiles    = TileCount();
            float pxPerUnit = tileSize / (mapW / (1 << maxZoom));   // zoom 최대일 때 px/unit
            var   msgType  = tiles > 10000 ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(
                $"맵 크기: {mapW:F0} × {mapH:F0} 유닛\n" +
                $"최대 해상도: {pxPerUnit:F1} px/유닛 (zoom {maxZoom})\n" +
                $"총 타일 수: {tiles} (zoom 0~{maxZoom})\n" +
                $"예상 용량: ~{tiles * tileSize * tileSize * 3 / 1024 / 1024} MB",
                msgType);

            EditorGUILayout.Space(4);
            GUI.enabled = CanBake();
            if (GUILayout.Button("Bake Tiles", GUILayout.Height(36)))
                BakeTiles();
            GUI.enabled = true;
        }

        // ── 모드별 GUI ────────────────────────────────────────────────────────

        void DrawManualBounds()
        {
            GUILayout.Label("World Bounds (meters)", EditorStyles.boldLabel);
            worldMinX = EditorGUILayout.FloatField("Min X (West)",  worldMinX);
            worldMaxX = EditorGUILayout.FloatField("Max X (East)",  worldMaxX);
            worldMinZ = EditorGUILayout.FloatField("Min Z (South)", worldMinZ);
            worldMaxZ = EditorGUILayout.FloatField("Max Z (North)", worldMaxZ);
            cameraY   = EditorGUILayout.FloatField("Camera Height", cameraY);
        }

        void DrawSceneCameraMode()
        {
            boundsCamera = EditorGUILayout.TextField("Camera Name", boundsCamera);
            EditorGUILayout.Space(2);

            bool found = TryReadSceneCamera(out float minX, out float maxX,
                                            out float minZ, out float maxZ,
                                            out float camH);
            if (found)
            {
                worldMinX = minX; worldMaxX = maxX;
                worldMinZ = minZ; worldMaxZ = maxZ;
                cameraY   = camH;

                EditorGUILayout.HelpBox(
                    $"카메라 감지됨\n" +
                    $"X: {minX:F1} ~ {maxX:F1}\n" +
                    $"Z: {minZ:F1} ~ {maxZ:F1}\n" +
                    $"Camera Y: {camH:F1}",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"씬에서 '{boundsCamera}' 카메라를 찾을 수 없습니다.\n\n" +
                    "방법:\n" +
                    "1. Hierarchy에서 Camera 오브젝트 생성\n" +
                    $"2. 이름을 '{boundsCamera}' 으로 변경\n" +
                    "3. Projection을 Orthographic 으로 설정\n" +
                    "4. 맵 위 원하는 위치에 배치 (아래를 향하게)\n" +
                    "5. Orthographic Size 로 캡처 반경 조절",
                    MessageType.Warning);
            }
        }

        void DrawAutoDetectMode()
        {
            if (GUILayout.Button("씬 스캔 (Renderer 범위 자동 계산)"))
                AutoDetectFromRenderers();

            EditorGUILayout.Space(2);
            GUILayout.Label("감지된 범위 (읽기 전용)", EditorStyles.miniLabel);

            GUI.enabled = false;
            EditorGUILayout.FloatField("Min X", worldMinX);
            EditorGUILayout.FloatField("Max X", worldMaxX);
            EditorGUILayout.FloatField("Min Z", worldMinZ);
            EditorGUILayout.FloatField("Max Z", worldMaxZ);
            EditorGUILayout.FloatField("Camera Y", cameraY);
            GUI.enabled = true;
        }

        // ── 범위 계산 헬퍼 ────────────────────────────────────────────────────

        /// <summary>
        /// 씬에서 boundsCamera 이름의 Orthographic 카메라를 찾아 범위를 읽어옴.
        /// </summary>
        bool TryReadSceneCamera(out float minX, out float maxX,
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

            minX = pos.x - halfW;
            maxX = pos.x + halfW;
            minZ = pos.z - halfH;
            maxZ = pos.z + halfH;
            camH = pos.y;
            return true;
        }

        /// <summary>
        /// 씬의 모든 Renderer 를 합산해 XZ 범위를 계산.
        /// </summary>
        void AutoDetectFromRenderers()
        {
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[MapTileBaker] 씬에 Renderer 가 없습니다.");
                return;
            }

            // 모든 오브젝트의 Y 중앙값(median)을 구해서 하늘 높이 뻗은 배경 건물을 제외
            // → 지면 근처에 밀집된 실제 도로 오브젝트의 XZ 범위만 사용
            var centers = new System.Collections.Generic.List<Bounds>();
            foreach (var r in renderers)
                centers.Add(r.bounds);

            centers.Sort((a, b) => a.center.y.CompareTo(b.center.y));
            float medianY = centers[centers.Count / 2].center.y;
            float yThreshold = 80f;   // 중앙 Y 기준 ±80 유닛 안의 오브젝트만 사용

            bool init = false;
            Bounds road = default;
            float maxY  = float.MinValue;

            foreach (var b in centers)
            {
                if (Mathf.Abs(b.center.y - medianY) > yThreshold) continue;
                if (!init) { road = b; init = true; }
                else road.Encapsulate(b);
                if (b.max.y > maxY) maxY = b.max.y;
            }

            if (!init)
            {
                Debug.LogWarning("[MapTileBaker] 유효한 Renderer 를 찾지 못했습니다.");
                return;
            }

            worldMinX = road.min.x;
            worldMaxX = road.max.x;
            worldMinZ = road.min.z;
            worldMaxZ = road.max.z;
            cameraY   = maxY + 200f;   // 지면 위 넉넉한 높이

            Debug.Log($"[MapTileBaker] Auto Detect 완료 → X[{worldMinX:F1}, {worldMaxX:F1}]  Z[{worldMinZ:F1}, {worldMaxZ:F1}]  camY={cameraY:F1}  (기준 medianY={medianY:F1})");
            Repaint();
        }

        bool CanBake() => worldMaxX > worldMinX && worldMaxZ > worldMinZ;

        int TileCount()
        {
            int total = 0;
            for (int z = 0; z <= maxZoom; z++)
            {
                int n = 1 << z;
                total += n * n;
            }
            return total;
        }

        // ── 베이크 ────────────────────────────────────────────────────────────

        void BakeTiles()
        {
            // Scene Camera 모드면 베이크 직전에 한 번 더 읽어서 최신 값 반영
            if (boundsMode == BoundsMode.SceneCamera)
            {
                if (!TryReadSceneCamera(out float minX, out float maxX,
                                        out float minZ, out float maxZ, out float camH))
                {
                    EditorUtility.DisplayDialog("MapTileBaker",
                        $"'{boundsCamera}' 카메라를 찾을 수 없습니다.", "확인");
                    return;
                }
                worldMinX = minX; worldMaxX = maxX;
                worldMinZ = minZ; worldMaxZ = maxZ;
                cameraY   = camH;
            }

            float worldW = worldMaxX - worldMinX;
            float worldH = worldMaxZ - worldMinZ;

            var camGo = new GameObject("__MapBakerCamera__");
            camGo.hideFlags = HideFlags.HideAndDontSave;

            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.orthographic    = true;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask     = ~0;
            cam.nearClipPlane   = 0.3f;
            cam.farClipPlane    = cameraY + 500f;  // 지면까지 반드시 닿도록

            // URP: 이 컴포넌트 없으면 URP 파이프라인으로 렌더되지 않아 흑백/핑크로 나옴
            var urpData = camGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderShadows         = false;
            urpData.requiresColorOption   = CameraOverrideOption.Off;
            urpData.requiresDepthOption   = CameraOverrideOption.Off;

            try
            {
                int processed = 0;
                int total     = TileCount();

                for (int z = 0; z <= maxZoom; z++)
                {
                    int   tileCount = 1 << z;
                    float tileW     = worldW / tileCount;
                    float tileH     = worldH / tileCount;

                    for (int ty = 0; ty < tileCount; ty++)
                    {
                        for (int tx = 0; tx < tileCount; tx++)
                        {
                            float cx = worldMinX + (tx + 0.5f) * tileW;
                            float cz = worldMaxZ - (ty + 0.5f) * tileH;

                            cam.transform.position = new Vector3(cx, cameraY, cz);
                            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                            cam.orthographicSize   = tileH / 2f;
                            cam.aspect             = tileW / tileH;

                            var rt = new RenderTexture(tileSize, tileSize, 24,
                                                       RenderTextureFormat.ARGB32);
                            cam.targetTexture = rt;
                            cam.Render();

                            var tex = new Texture2D(tileSize, tileSize,
                                                    TextureFormat.ARGB32, false);
                            RenderTexture.active = rt;
                            tex.ReadPixels(new Rect(0, 0, tileSize, tileSize), 0, 0);
                            tex.Apply();

                            string dir = Path.Combine(outputPath, z.ToString(), tx.ToString());
                            Directory.CreateDirectory(dir);
                            File.WriteAllBytes(
                                Path.Combine(dir, ty + ".png"),
                                tex.EncodeToPNG());

                            cam.targetTexture    = null;
                            RenderTexture.active = null;
                            DestroyImmediate(rt);
                            DestroyImmediate(tex);

                            processed++;
                            if (EditorUtility.DisplayCancelableProgressBar(
                                    "Baking Map Tiles",
                                    $"z={z}, tx={tx}, ty={ty}  ({processed}/{total})",
                                    (float)processed / total))
                            {
                                Debug.Log("[MapTileBaker] 사용자가 취소했습니다.");
                                return;
                            }
                        }
                    }
                }

                AssetDatabase.Refresh();
                Debug.Log($"[MapTileBaker] 완료! 총 {processed}개 타일 → {outputPath}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                DestroyImmediate(camGo);
            }
        }
    }
}
