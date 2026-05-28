using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace CarSim.Editor
{
    /// <summary>
    /// Bakes semantic, navigation-style map tiles from the scene.
    ///
    /// The baker temporarily replaces scene materials with flat map colors,
    /// renders the same slippy-map tile layout as MapTileBaker, then adds a
    /// small pixel outline pass so roads and building footprints read clearly.
    ///
    /// Usage: CarSim -> Bake Styled Map Tiles
    /// </summary>
    public class StyledMapTileBaker : EditorWindow
    {
        enum BoundsMode { Manual, SceneCamera, AutoDetect }
        enum MapClass { Background, Road, Highway, Sidewalk, Building, Vegetation, Barrier, Other }

        struct RendererState
        {
            public Renderer renderer;
            public bool enabled;
            public Material[] materials;
        }

        static readonly string[] kRoadMaterials =
        {
            "asphalt_2_tracks",
            "asphalt_2-5_tracks",
            "asphalt_4_tracks",
            "asphalt_square",
            "asphalt_extra",
        };

        static readonly string[] kHighwayMaterials =
        {
            "asphalt_highway",
        };

        static readonly string[] kBuildingMaterials =
        {
            "building_bases",
            "building_facades",
            "building_interior",
            "building_windows_wet",
            "highrise_facades",
        };

        static readonly string[] kSidewalkMaterials =
        {
            "sideway_tile",
        };

        static readonly string[] kVegetationMaterials =
        {
            "vegetation_main",
            "vegetation_non_tp",
        };

        static readonly string[] kBarrierMaterials =
        {
            "highway_wall",
            "road_sideway_fences",
        };

        readonly Color32 backgroundColor = Hex(0xEC, 0xEF, 0xF2);
        readonly Color32 roadColor       = Hex(0xFF, 0xFF, 0xFA);
        readonly Color32 highwayColor    = Hex(0xFF, 0xF3, 0xD4);
        readonly Color32 sidewalkColor   = Hex(0xDD, 0xE2, 0xE7);
        readonly Color32 buildingColor   = Hex(0xC8, 0xD0, 0xD8);
        readonly Color32 vegetationColor = Hex(0xC9, 0xDE, 0xC7);
        readonly Color32 barrierColor    = Hex(0xB8, 0xC0, 0xC8);
        readonly Color32 otherColor      = Hex(0xD7, 0xDB, 0xDF);

        readonly Color32 roadOutline       = Hex(0xC3, 0xC8, 0xCE);
        readonly Color32 highwayOutline    = Hex(0xD7, 0xBE, 0x84);
        readonly Color32 buildingOutline   = Hex(0x96, 0xA0, 0xAA);
        readonly Color32 vegetationOutline = Hex(0xA9, 0xC0, 0xA6);
        readonly Color32 barrierOutline    = Hex(0x91, 0x9A, 0xA3);

        BoundsMode boundsMode = BoundsMode.SceneCamera;
        string boundsCamera = "MapBoundsCamera";

        float worldMinX = -400f;
        float worldMaxX =  450f;
        float worldMinZ = -200f;
        float worldMaxZ =  240f;
        float cameraY   =  700f;

        int maxZoom = 6;
        int tileSize = 256;
        int outlinePixels = 1;
        bool hideUnclassified = true;
        bool drawOutlines = true;
        string outputPath = "Assets/StreamingAssets/styled_tiles";

        Dictionary<MapClass, Material> styleMaterials;

        [MenuItem("CarSim/Bake Styled Map Tiles")]
        public static void ShowWindow() => GetWindow<StyledMapTileBaker>("Styled Map Baker");

        void OnGUI()
        {
            GUILayout.Label("Styled Map Baker", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "도로, 건물, 인도, 녹지를 단색 팔레트로 바꿔 내비게이션 지도 느낌의 타일을 생성합니다.",
                MessageType.Info);

            EditorGUILayout.Space(6);
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

            EditorGUILayout.Space(6);
            GUILayout.Label("Tile Settings", EditorStyles.boldLabel);
            maxZoom = EditorGUILayout.IntSlider("Max Zoom Level", maxZoom, 0, 10);
            tileSize = EditorGUILayout.IntField("Tile Size (px)", tileSize);
            outputPath = EditorGUILayout.TextField("Output Path", outputPath);

            EditorGUILayout.Space(6);
            GUILayout.Label("Style", EditorStyles.boldLabel);
            drawOutlines = EditorGUILayout.Toggle("Draw Outlines", drawOutlines);
            using (new EditorGUI.DisabledScope(!drawOutlines))
                outlinePixels = EditorGUILayout.IntSlider("Outline Pixels", outlinePixels, 1, 3);
            hideUnclassified = EditorGUILayout.Toggle("Hide Unclassified", hideUnclassified);

            EditorGUILayout.Space(4);
            float mapW = worldMaxX - worldMinX;
            float mapH = worldMaxZ - worldMinZ;
            int tiles = TileCount();
            float pxPerUnit = tileSize / (mapW / (1 << maxZoom));
            EditorGUILayout.HelpBox(
                $"맵 크기: {mapW:F0} x {mapH:F0} 유닛\n" +
                $"최대 해상도: {pxPerUnit:F1} px/유닛 (zoom {maxZoom})\n" +
                $"총 타일 수: {tiles} (zoom 0~{maxZoom})",
                tiles > 10000 ? MessageType.Warning : MessageType.Info);

            GUI.enabled = CanBake();
            if (GUILayout.Button("Bake Styled Tiles", GUILayout.Height(36)))
                BakeStyledTiles();
            GUI.enabled = true;
        }

        void DrawManualBounds()
        {
            GUILayout.Label("World Bounds", EditorStyles.boldLabel);
            worldMinX = EditorGUILayout.FloatField("Min X (West)", worldMinX);
            worldMaxX = EditorGUILayout.FloatField("Max X (East)", worldMaxX);
            worldMinZ = EditorGUILayout.FloatField("Min Z (South)", worldMinZ);
            worldMaxZ = EditorGUILayout.FloatField("Max Z (North)", worldMaxZ);
            cameraY = EditorGUILayout.FloatField("Camera Height", cameraY);
        }

        void DrawSceneCameraMode()
        {
            boundsCamera = EditorGUILayout.TextField("Camera Name", boundsCamera);

            if (TryReadSceneCamera(out float minX, out float maxX,
                                   out float minZ, out float maxZ, out float camH))
            {
                worldMinX = minX; worldMaxX = maxX;
                worldMinZ = minZ; worldMaxZ = maxZ;
                cameraY = camH;

                EditorGUILayout.HelpBox(
                    $"카메라 감지됨\nX: {minX:F1} ~ {maxX:F1}\nZ: {minZ:F1} ~ {maxZ:F1}\nCamera Y: {camH:F1}",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"씬에서 '{boundsCamera}' Orthographic 카메라를 찾을 수 없습니다.",
                    MessageType.Warning);
            }
        }

        void DrawAutoDetectMode()
        {
            if (GUILayout.Button("씬 스캔 (Renderer 범위 자동 계산)"))
                AutoDetectFromRenderers();

            GUI.enabled = false;
            EditorGUILayout.FloatField("Min X", worldMinX);
            EditorGUILayout.FloatField("Max X", worldMaxX);
            EditorGUILayout.FloatField("Min Z", worldMinZ);
            EditorGUILayout.FloatField("Max Z", worldMaxZ);
            EditorGUILayout.FloatField("Camera Y", cameraY);
            GUI.enabled = true;
        }

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

        void AutoDetectFromRenderers()
        {
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[StyledMapTileBaker] 씬에 Renderer 가 없습니다.");
                return;
            }

            var bounds = new List<Bounds>();
            foreach (var renderer in renderers)
            {
                if (ClassifyRenderer(renderer) == MapClass.Other && hideUnclassified) continue;
                bounds.Add(renderer.bounds);
            }

            if (bounds.Count == 0)
            {
                Debug.LogWarning("[StyledMapTileBaker] 지도에 사용할 Renderer 를 찾지 못했습니다.");
                return;
            }

            bounds.Sort((a, b) => a.center.y.CompareTo(b.center.y));
            float medianY = bounds[bounds.Count / 2].center.y;
            float yThreshold = 80f;

            bool init = false;
            Bounds mapBounds = default;
            float maxY = float.MinValue;

            foreach (var b in bounds)
            {
                if (Mathf.Abs(b.center.y - medianY) > yThreshold) continue;
                if (!init) { mapBounds = b; init = true; }
                else mapBounds.Encapsulate(b);
                if (b.max.y > maxY) maxY = b.max.y;
            }

            if (!init)
            {
                Debug.LogWarning("[StyledMapTileBaker] 유효한 Renderer 범위를 찾지 못했습니다.");
                return;
            }

            worldMinX = mapBounds.min.x;
            worldMaxX = mapBounds.max.x;
            worldMinZ = mapBounds.min.z;
            worldMaxZ = mapBounds.max.z;
            cameraY = maxY + 200f;
            Repaint();
        }

        bool CanBake() => tileSize > 0 && worldMaxX > worldMinX && worldMaxZ > worldMinZ;

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

        void BakeStyledTiles()
        {
            if (boundsMode == BoundsMode.SceneCamera)
            {
                if (!TryReadSceneCamera(out float minX, out float maxX,
                                        out float minZ, out float maxZ, out float camH))
                {
                    EditorUtility.DisplayDialog("StyledMapTileBaker",
                        $"'{boundsCamera}' 카메라를 찾을 수 없습니다.", "확인");
                    return;
                }

                worldMinX = minX; worldMaxX = maxX;
                worldMinZ = minZ; worldMaxZ = maxZ;
                cameraY = camH;
            }

            EnsureMaterials();
            var states = ApplyStyledScene();

            var camGo = new GameObject("__StyledMapBakerCamera__");
            camGo.hideFlags = HideFlags.HideAndDontSave;

            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ToColor(backgroundColor);
            cam.cullingMask = ~0;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = cameraY + 500f;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            var urpData = camGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderShadows = false;
            urpData.requiresColorOption = CameraOverrideOption.Off;
            urpData.requiresDepthOption = CameraOverrideOption.Off;

            try
            {
                BakeTilesWithCamera(cam);

                string boundsPath = Path.Combine(outputPath, "bounds.json");
                string boundsJson =
                    $"{{\"min_x\":{worldMinX:F3},\"max_x\":{worldMaxX:F3}," +
                    $"\"min_z\":{worldMinZ:F3},\"max_z\":{worldMaxZ:F3}," +
                    $"\"camera_y\":{cameraY:F3},\"tile_size\":{tileSize}," +
                    $"\"max_zoom\":{maxZoom}}}";
                File.WriteAllText(boundsPath, boundsJson);
                Debug.Log($"[StyledMapTileBaker] bounds sidecar: {boundsPath}");
            }
            finally
            {
                RestoreScene(states);
                EditorUtility.ClearProgressBar();
                DestroyImmediate(camGo);
            }
        }

        void BakeTilesWithCamera(UnityEngine.Camera cam)
        {
            float worldW = worldMaxX - worldMinX;
            float worldH = worldMaxZ - worldMinZ;
            int processed = 0;
            int total = TileCount();

            for (int z = 0; z <= maxZoom; z++)
            {
                int tileCount = 1 << z;
                float tileW = worldW / tileCount;
                float tileH = worldH / tileCount;

                for (int ty = 0; ty < tileCount; ty++)
                {
                    for (int tx = 0; tx < tileCount; tx++)
                    {
                        float cx = worldMinX + (tx + 0.5f) * tileW;
                        float cz = worldMaxZ - (ty + 0.5f) * tileH;

                        cam.transform.position = new Vector3(cx, cameraY, cz);
                        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                        cam.orthographicSize = tileH / 2f;
                        cam.aspect = tileW / tileH;

                        RenderTile(cam, z, tx, ty);

                        processed++;
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Baking Styled Map Tiles",
                                $"z={z}, tx={tx}, ty={ty} ({processed}/{total})",
                                (float)processed / total))
                        {
                            Debug.Log("[StyledMapTileBaker] 사용자가 취소했습니다.");
                            return;
                        }
                    }
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[StyledMapTileBaker] 완료! 총 {processed}개 타일 -> {outputPath}");
        }

        void RenderTile(UnityEngine.Camera cam, int z, int tx, int ty)
        {
            var rt = new RenderTexture(tileSize, tileSize, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };
            cam.targetTexture = rt;
            cam.Render();

            var tex = new Texture2D(tileSize, tileSize, TextureFormat.ARGB32, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, tileSize, tileSize), 0, 0);
            tex.Apply();

            if (drawOutlines)
                ApplyOutlines(tex);

            string dir = Path.Combine(outputPath, z.ToString(), tx.ToString());
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, ty + ".png"), tex.EncodeToPNG());

            cam.targetTexture = null;
            RenderTexture.active = null;
            DestroyImmediate(rt);
            DestroyImmediate(tex);
        }

        List<RendererState> ApplyStyledScene()
        {
            var states = new List<RendererState>();
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            foreach (var renderer in renderers)
            {
                states.Add(new RendererState
                {
                    renderer = renderer,
                    enabled = renderer.enabled,
                    materials = renderer.sharedMaterials
                });

                var sourceMaterials = renderer.sharedMaterials;
                bool hasMapMaterial = false;
                var styled = new Material[sourceMaterials.Length];

                for (int i = 0; i < sourceMaterials.Length; i++)
                {
                    MapClass mapClass = ClassifyMaterial(sourceMaterials[i]);
                    if (mapClass != MapClass.Other)
                        hasMapMaterial = true;
                    styled[i] = MaterialForClass(mapClass);
                }

                if (!hasMapMaterial && hideUnclassified)
                {
                    renderer.enabled = false;
                    continue;
                }

                renderer.enabled = true;
                renderer.sharedMaterials = styled;
            }

            return states;
        }

        void RestoreScene(List<RendererState> states)
        {
            foreach (var state in states)
            {
                if (state.renderer == null) continue;
                state.renderer.sharedMaterials = state.materials;
                state.renderer.enabled = state.enabled;
            }
        }

        MapClass ClassifyRenderer(Renderer renderer)
        {
            bool hasBuilding = false;
            bool hasSidewalk = false;
            bool hasVegetation = false;
            bool hasBarrier = false;

            foreach (var material in renderer.sharedMaterials)
            {
                MapClass mapClass = ClassifyMaterial(material);
                if (mapClass == MapClass.Highway) return MapClass.Highway;
                if (mapClass == MapClass.Road) return MapClass.Road;
                if (mapClass == MapClass.Building) hasBuilding = true;
                if (mapClass == MapClass.Sidewalk) hasSidewalk = true;
                if (mapClass == MapClass.Vegetation) hasVegetation = true;
                if (mapClass == MapClass.Barrier) hasBarrier = true;
            }

            if (hasBuilding) return MapClass.Building;
            if (hasSidewalk) return MapClass.Sidewalk;
            if (hasVegetation) return MapClass.Vegetation;
            if (hasBarrier) return MapClass.Barrier;
            return MapClass.Other;
        }

        MapClass ClassifyMaterial(Material material)
        {
            if (material == null) return MapClass.Other;

            string name = CleanMaterialName(material.name);
            if (ContainsName(kHighwayMaterials, name)) return MapClass.Highway;
            if (ContainsName(kRoadMaterials, name)) return MapClass.Road;
            if (ContainsName(kBuildingMaterials, name)) return MapClass.Building;
            if (ContainsName(kSidewalkMaterials, name)) return MapClass.Sidewalk;
            if (ContainsName(kVegetationMaterials, name)) return MapClass.Vegetation;
            if (ContainsName(kBarrierMaterials, name)) return MapClass.Barrier;
            return MapClass.Other;
        }

        Material MaterialForClass(MapClass mapClass)
        {
            if (styleMaterials.TryGetValue(mapClass, out var material))
                return material;
            return styleMaterials[MapClass.Other];
        }

        void EnsureMaterials()
        {
            if (styleMaterials != null) return;

            styleMaterials = new Dictionary<MapClass, Material>
            {
                { MapClass.Road, CreateFlatMaterial("Styled Road", roadColor) },
                { MapClass.Highway, CreateFlatMaterial("Styled Highway", highwayColor) },
                { MapClass.Sidewalk, CreateFlatMaterial("Styled Sidewalk", sidewalkColor) },
                { MapClass.Building, CreateFlatMaterial("Styled Building", buildingColor) },
                { MapClass.Vegetation, CreateFlatMaterial("Styled Vegetation", vegetationColor) },
                { MapClass.Barrier, CreateFlatMaterial("Styled Barrier", barrierColor) },
                { MapClass.Other, CreateFlatMaterial("Styled Other", otherColor) },
            };
        }

        static Material CreateFlatMaterial(string name, Color32 color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var material = new Material(shader)
            {
                name = name,
                color = ToColor(color),
                hideFlags = HideFlags.HideAndDontSave
            };

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", ToColor(color));
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", ToColor(color));

            return material;
        }

        void ApplyOutlines(Texture2D tex)
        {
            int width = tex.width;
            int height = tex.height;
            var src = tex.GetPixels32();
            var dst = (Color32[])src.Clone();

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int index = y * width + x;
                    MapClass current = PixelClass(src[index]);
                    if (current == MapClass.Background) continue;

                    MapClass right = PixelClass(src[index + 1]);
                    MapClass down = PixelClass(src[index - width]);

                    if (ShouldOutline(current, right))
                        StampOutline(dst, width, height, x, y, OutlineColor(current, right));
                    if (ShouldOutline(current, down))
                        StampOutline(dst, width, height, x, y, OutlineColor(current, down));
                }
            }

            tex.SetPixels32(dst);
            tex.Apply();
        }

        bool ShouldOutline(MapClass a, MapClass b)
        {
            if (a == b) return false;
            if (a == MapClass.Other || b == MapClass.Other) return false;
            return a != MapClass.Background || b != MapClass.Background;
        }

        Color32 OutlineColor(MapClass a, MapClass b)
        {
            if (a == MapClass.Building || b == MapClass.Building) return buildingOutline;
            if (a == MapClass.Highway || b == MapClass.Highway) return highwayOutline;
            if (a == MapClass.Road || b == MapClass.Road) return roadOutline;
            if (a == MapClass.Barrier || b == MapClass.Barrier) return barrierOutline;
            if (a == MapClass.Vegetation || b == MapClass.Vegetation) return vegetationOutline;
            return roadOutline;
        }

        void StampOutline(Color32[] pixels, int width, int height, int x, int y, Color32 color)
        {
            for (int oy = -outlinePixels; oy <= outlinePixels; oy++)
            {
                for (int ox = -outlinePixels; ox <= outlinePixels; ox++)
                {
                    int px = x + ox;
                    int py = y + oy;
                    if (px < 0 || px >= width || py < 0 || py >= height) continue;
                    pixels[py * width + px] = color;
                }
            }
        }

        MapClass PixelClass(Color32 color)
        {
            if (Near(color, backgroundColor)) return MapClass.Background;
            if (Near(color, roadColor)) return MapClass.Road;
            if (Near(color, highwayColor)) return MapClass.Highway;
            if (Near(color, sidewalkColor)) return MapClass.Sidewalk;
            if (Near(color, buildingColor)) return MapClass.Building;
            if (Near(color, vegetationColor)) return MapClass.Vegetation;
            if (Near(color, barrierColor)) return MapClass.Barrier;
            return MapClass.Other;
        }

        static bool Near(Color32 a, Color32 b)
        {
            const int threshold = 12;
            return Mathf.Abs(a.r - b.r) <= threshold &&
                   Mathf.Abs(a.g - b.g) <= threshold &&
                   Mathf.Abs(a.b - b.b) <= threshold;
        }

        static bool ContainsName(string[] names, string materialName)
        {
            foreach (var name in names)
                if (materialName == name) return true;
            return false;
        }

        static string CleanMaterialName(string name)
        {
            return name.Replace(" (Instance)", "").Trim();
        }

        static Color32 Hex(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        static Color ToColor(Color32 color) =>
            new Color(color.r / 255f, color.g / 255f, color.b / 255f, color.a / 255f);
    }
}
