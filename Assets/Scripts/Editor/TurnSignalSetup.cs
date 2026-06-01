using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Reflection;
using CarSim.Vehicle;

public class TurnSignalSetup : EditorWindow
{
    public static bool SuppressDialog;

    [MenuItem("Tools/Setup Turn Signals")]
    public static void SetupTurnSignals()
    {
        // VehicleLights 찾기
        var vehicleLights = Object.FindObjectOfType<VehicleLights>();
        if (vehicleLights == null)
        {
            EditorUtility.DisplayDialog("Error", "씬에서 VehicleLights를 찾을 수 없습니다.", "OK");
            return;
        }

        // 기존 headlights 배열 읽기
        var field = typeof(VehicleLights).GetField("headlights",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
        {
            EditorUtility.DisplayDialog("Error", "headlights 필드를 찾을 수 없습니다.", "OK");
            return;
        }

        var headlights = field.GetValue(vehicleLights) as Light[];
        if (headlights == null || headlights.Length < 2)
        {
            EditorUtility.DisplayDialog("Error", "headlights 배열에 Light가 2개 이상 필요합니다.", "OK");
            return;
        }

        // headlights[0](Head_light_left)이 차량 우측(+x)에 위치 → 좌우 인덱스 교차 매핑
        Light hlLeft  = headlights[1];
        Light hlRight = headlights[0];
        Transform vehicleRoot = vehicleLights.transform;

        // 멱등: 기존 방향지시등(수동 추가분 포함) 모두 제거 후 새로 생성 — 재실행해도 중복 안 생김
        RemoveExistingSignals(vehicleLights.gameObject.scene);

        // 앞 좌/우 — 헤드라이트와 동일 위치
        Light frontLeft  = CreateTurnSignal("TurnSignal_FL", hlLeft.transform.parent,
            hlLeft.transform.localPosition, hlLeft.transform.localRotation);
        Light frontRight = CreateTurnSignal("TurnSignal_FR", hlRight.transform.parent,
            hlRight.transform.localPosition, hlRight.transform.localRotation);

        // 뒤 좌/우 — 헤드라이트 localPosition의 Z축 반전
        Vector3 rearPosL = hlLeft.transform.localPosition;
        rearPosL.z = -rearPosL.z;
        Vector3 rearPosR = hlRight.transform.localPosition;
        rearPosR.z = -rearPosR.z;

        Light rearLeft  = CreateTurnSignal("TurnSignal_RL", vehicleRoot,
            rearPosL, hlLeft.transform.localRotation * Quaternion.Euler(0, 180f, 0));
        Light rearRight = CreateTurnSignal("TurnSignal_RR", vehicleRoot,
            rearPosR, hlRight.transform.localRotation * Quaternion.Euler(0, 180f, 0));

        // VehicleLights 슬롯에 할당
        SetLightArray(vehicleLights, "turnSignalLeft",  new[] { frontLeft,  rearLeft  });
        SetLightArray(vehicleLights, "turnSignalRight", new[] { frontRight, rearRight });

        EditorUtility.SetDirty(vehicleLights);
        EditorSceneManager.MarkSceneDirty(vehicleLights.gameObject.scene);

        if (!SuppressDialog)
            EditorUtility.DisplayDialog("Complete",
                "방향지시등 4개 생성 완료!\n위치는 Inspector에서 조정하세요.", "OK");
    }

    static readonly string[] SignalNames =
        { "TurnSignal_FL", "TurnSignal_FR", "TurnSignal_RL", "TurnSignal_RR" };

    static void RemoveExistingSignals(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || go.scene != scene) continue;
            if (System.Array.IndexOf(SignalNames, go.name) < 0) continue;
            Undo.DestroyObjectImmediate(go);
        }
    }

    static Light CreateTurnSignal(string name, Transform parent, Vector3 localPos, Quaternion localRot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;

        var light = go.AddComponent<Light>();
        light.type        = LightType.Spot;
        light.spotAngle   = 50f;
        light.innerSpotAngle = 30f;
        light.color       = new Color(1f, 0.85f, 0f);   // 노랑
        light.intensity   = 150f;
        light.range       = 20f;
        light.enabled     = false;

        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        return light;
    }

    static void SetLightArray(VehicleLights target, string fieldName, Light[] lights)
    {
        var field = typeof(VehicleLights).GetField(fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
            field.SetValue(target, lights);
    }
}
