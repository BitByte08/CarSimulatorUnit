using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Reflection;
using CarSim.Vehicle;

public class BrakeLightSetup : EditorWindow
{
    public static bool SuppressDialog;

    static readonly string[] CreatedNames = { "BrakeLight_L", "BrakeLight_R" };

    [MenuItem("Tools/Setup Brake Lights")]
    public static void Setup()
    {
        var vl = Object.FindObjectOfType<VehicleLights>();
        if (vl == null)
        {
            EditorUtility.DisplayDialog("Error", "씬에서 VehicleLights를 찾을 수 없습니다.", "OK");
            return;
        }

        var field = typeof(VehicleLights).GetField("headlights",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var headlights = field?.GetValue(vl) as Light[];
        if (headlights == null || headlights.Length < 2)
        {
            EditorUtility.DisplayDialog("Error", "headlights 배열에 Light가 2개 이상 필요합니다.", "OK");
            return;
        }

        Light hl0 = headlights[0];
        Light hl1 = headlights[1];

        RemoveExisting(vl.gameObject.scene);

        Quaternion back = Quaternion.Euler(0f, 180f, 0f);

        Vector3 pos0 = hl0.transform.localPosition; pos0.z = -pos0.z;
        Vector3 pos1 = hl1.transform.localPosition; pos1.z = -pos1.z;

        Light bl = CreateBrakeLight("BrakeLight_L", hl0.transform.parent, pos0, hl0.transform.localRotation * back);
        Light br = CreateBrakeLight("BrakeLight_R", hl1.transform.parent, pos1, hl1.transform.localRotation * back);

        SetLightArray(vl, "brakeLights", new[] { bl, br });

        EditorUtility.SetDirty(vl);
        EditorSceneManager.MarkSceneDirty(vl.gameObject.scene);

        if (!SuppressDialog)
            EditorUtility.DisplayDialog("Complete",
                "후미 브레이크등 2개 생성 완료!\n위치는 Inspector에서 조정하세요.", "OK");
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

    static Light CreateBrakeLight(string name, Transform parent, Vector3 localPos, Quaternion localRot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;

        var light = go.AddComponent<Light>();
        light.type           = LightType.Spot;
        light.spotAngle      = 70f;
        light.innerSpotAngle = 40f;
        light.color          = Color.red;
        light.intensity      = 80f;
        light.range          = 8f;
        light.enabled        = false;

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
