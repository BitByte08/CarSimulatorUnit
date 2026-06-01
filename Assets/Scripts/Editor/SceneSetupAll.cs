using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Setup ALL 완료",
            "적용 완료:\n" +
            "• 방향지시등 (노랑, 좌우 정렬)\n" +
            "• 후미 브레이크등 (빨강)\n" +
            "• 미러 카메라 3개 + 화면 표시\n" +
            "• 디버그 UI off\n" +
            "• CanSettings 오브젝트 (F2 설정창)\n" +
            "• DriverCamera 관성 헤드무빙 (Main Camera)\n\n" +
            "Ctrl+S로 저장하세요.", "OK");
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
