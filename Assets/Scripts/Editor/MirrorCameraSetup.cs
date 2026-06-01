using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using CarSim.Vehicle;
using CarSim.UI;

public class MirrorCameraSetup : EditorWindow
{
    public static bool SuppressDialog;

    const string RtDir = "Assets/RenderTextures";

    static readonly string[] CreatedNames =
        { "RearMirrorCam", "LeftMirrorCam", "RightMirrorCam", "MirrorCanvas" };

    [MenuItem("Tools/Setup Mirror Cameras")]
    public static void Setup()
    {
        var vc = Object.FindObjectOfType<VehicleController>();
        if (vc == null)
        {
            EditorUtility.DisplayDialog("Error", "씬에서 VehicleController를 찾을 수 없습니다.", "OK");
            return;
        }

        Transform carRoot = vc.transform;
        Scene scene = vc.gameObject.scene;

        DisableDebugOverlay();
        RemoveExisting(scene);

        if (!AssetDatabase.IsValidFolder(RtDir))
            AssetDatabase.CreateFolder("Assets", "RenderTextures");

        Camera rear  = CreateMirrorCam("RearMirrorCam",  carRoot, new Vector3( 0f,   1.15f, 0.10f), 180f);
        Camera left  = CreateMirrorCam("LeftMirrorCam",  carRoot, new Vector3(-0.85f, 1.0f, 0.30f), 195f);
        Camera right = CreateMirrorCam("RightMirrorCam", carRoot, new Vector3( 0.85f, 1.0f, 0.30f), 165f);

        Transform canvas = CreateCanvas();
        CreateView(canvas, "RearMirrorView",  rear.targetTexture,  new Vector2(0.5f, 1f), new Vector2(0f,  -90f), new Vector2(380f, 120f));
        CreateView(canvas, "LeftMirrorView",  left.targetTexture,  new Vector2(0f,   0f), new Vector2(120f, 260f), new Vector2(230f, 150f));
        CreateView(canvas, "RightMirrorView", right.targetTexture, new Vector2(1f,   0f), new Vector2(-120f,260f), new Vector2(230f, 150f));

        EditorUtility.SetDirty(vc);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();

        if (!SuppressDialog)
            EditorUtility.DisplayDialog("Complete",
                "미러 카메라 3개 + 표시 UI 생성 완료!\n위치/각도는 각 *MirrorCam의 Inspector에서 조정하세요.", "OK");
    }

    static void DisableDebugOverlay()
    {
        var overlay = Object.FindObjectOfType<VehicleDebugOverlay>();
        if (overlay == null) return;

        var so   = new SerializedObject(overlay);
        var prop = so.FindProperty("showOverlay");
        if (prop != null)
        {
            prop.boolValue = false;
            so.ApplyModifiedProperties();
        }
    }

    static void RemoveExisting(Scene scene)
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || go.scene != scene) continue;
            if (System.Array.IndexOf(CreatedNames, go.name) < 0) continue;
            Undo.DestroyObjectImmediate(go);
        }
    }

    static Camera CreateMirrorCam(string name, Transform parent, Vector3 localPos, float yaw)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        var cam = go.AddComponent<Camera>();
        cam.fieldOfView   = 50f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane  = 300f;
        cam.depth         = 1;
        cam.targetTexture = CreateRenderTexture(name);

        var urp = cam.GetUniversalAdditionalCameraData();
        urp.renderType = CameraRenderType.Base;
        urp.renderPostProcessing = false;

        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        return cam;
    }

    static RenderTexture CreateRenderTexture(string camName)
    {
        string path = $"{RtDir}/{camName}.renderTexture";
        var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (existing != null) return existing;

        var rt = new RenderTexture(512, 256, 24, RenderTextureFormat.DefaultHDR) { name = camName };
        AssetDatabase.CreateAsset(rt, path);
        return rt;
    }

    static Transform CreateCanvas()
    {
        var go = new GameObject("MirrorCanvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        go.AddComponent<GraphicRaycaster>();
        Undo.RegisterCreatedObjectUndo(go, "Create MirrorCanvas");
        return go.transform;
    }

    const float BorderPx = 3f;

    static void CreateView(Transform canvas, string name, Texture tex,
                           Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        var frameGO = new GameObject(name + "Frame");
        frameGO.transform.SetParent(canvas, false);
        var frame = frameGO.AddComponent<Image>();
        frame.color = Color.black;
        frame.raycastTarget = false;

        var frt = frame.rectTransform;
        frt.anchorMin        = anchor;
        frt.anchorMax        = anchor;
        frt.pivot            = anchor;
        frt.anchoredPosition = anchoredPos;
        frt.sizeDelta        = size + new Vector2(BorderPx * 2f, BorderPx * 2f);

        var go = new GameObject(name);
        go.transform.SetParent(frameGO.transform, false);

        var raw = go.AddComponent<RawImage>();
        raw.texture = tex;
        raw.uvRect  = new Rect(1f, 0f, -1f, 1f);   // 좌우 반전 — 실제 거울처럼 보이게

        var rt = raw.rectTransform;
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = size;

        Undo.RegisterCreatedObjectUndo(frameGO, $"Create {name}Frame");
    }
}
