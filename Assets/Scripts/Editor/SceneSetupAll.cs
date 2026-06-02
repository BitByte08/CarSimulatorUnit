using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CarSim.UI;

public class SceneSetupAll
{
    [MenuItem("Tools/Setup ALL (Mirrors + Lights + CAN)")]
    public static void RunAll()
    {
        TurnSignalSetup.SuppressDialog   = true;
        BrakeLightSetup.SuppressDialog   = true;
        MirrorCameraSetup.SuppressDialog = true;
        try
        {
            TurnSignalSetup.SetupTurnSignals();
            BrakeLightSetup.Setup();
            MirrorCameraSetup.Setup();
        }
        finally
        {
            TurnSignalSetup.SuppressDialog   = false;
            BrakeLightSetup.SuppressDialog   = false;
            MirrorCameraSetup.SuppressDialog = false;
        }

        EnsureCanSettingsPanel();
        EnsureDriverCamera();
        EnsureBloom();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Setup ALL 완료",
            "적용 완료:\n" +
            "• 방향지시등 (노랑, 좌우 정렬)\n" +
            "• 후미 브레이크등 (빨강)\n" +
            "• 미러 카메라 3개 + 화면 표시\n" +
            "• 디버그 UI off\n" +
            "• CanSettings 오브젝트 (F2 설정창)\n" +
            "• DriverCamera 관성 헤드무빙 (Main Camera)\n" +
            "• Bloom 활성 (PP on + Global Volume)\n\n" +
            "Ctrl+S로 저장하세요.", "OK");
    }

    static void EnsureBloom()
    {
        var cam = Camera.main;
        if (cam != null)
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        if (Object.FindObjectOfType<Volume>() == null)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            var go = new GameObject("GlobalVolume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            if (profile != null) vol.sharedProfile = profile;
            Undo.RegisterCreatedObjectUndo(go, "Create GlobalVolume");
        }
    }

    static void EnsureDriverCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (cam.GetComponent<CarSim.Camera.DriverCamera>() == null)
            Undo.AddComponent<CarSim.Camera.DriverCamera>(cam.gameObject);
    }

    static void EnsureCanSettingsPanel()
    {
        if (Object.FindObjectOfType<CanSettingsPanel>() != null) return;
        var go = new GameObject("CanSettings");
        go.AddComponent<CanSettingsPanel>();
        Undo.RegisterCreatedObjectUndo(go, "Create CanSettings");
    }
}
