using UnityEditor;
using UnityEngine;
using System.Reflection;
using CarSim.Vehicle;

public class VehicleControllerSetup : EditorWindow
{
    [MenuItem("Tools/Setup Vehicle Wheels")]
    public static void SetupVehicleWheels()
    {
        GameObject selected = Selection.activeGameObject;
        
        if (selected == null)
        {
            EditorUtility.DisplayDialog("Error", 
                "오브젝트를 선택해주세요.", "OK");
            return;
        }

        // WheelCollider 찾기
        WheelCollider[] allWheels = selected.GetComponentsInChildren<WheelCollider>();
        
        if (allWheels.Length < 4)
        {
            EditorUtility.DisplayDialog("Error", 
                $"WheelCollider 4개 필요, {allWheels.Length}개만 있습니다.", "OK");
            return;
        }

        // 모든 Transform 찾기
        Transform[] allTransforms = selected.GetComponentsInChildren<Transform>();

        // VehicleController 찾기
        var vehicleController = selected.GetComponent<VehicleController>();
        if (vehicleController == null)
        {
            EditorUtility.DisplayDialog("Error", 
                "VehicleController 컴포넌트를 찾을 수 없습니다.", "OK");
            return;
        }

        int assignCount = 0;

        // WheelCollider 할당 (WC_FL, WC_FR 등)
        assignCount += AssignWheel(vehicleController, allWheels, "wc_fl", "wheelFL");
        assignCount += AssignWheel(vehicleController, allWheels, "wc_fr", "wheelFR");
        assignCount += AssignWheel(vehicleController, allWheels, "wc_rl", "wheelRL");
        assignCount += AssignWheel(vehicleController, allWheels, "wc_rr", "wheelRR");

        // Mesh 할당 (Wheel_FL_fim 등)
        assignCount += AssignTransform(vehicleController, allTransforms, "wheel_fl", "meshFL");
        assignCount += AssignTransform(vehicleController, allTransforms, "wheel_fr", "meshFR");
        assignCount += AssignTransform(vehicleController, allTransforms, "wheel_rl", "meshRL");
        assignCount += AssignTransform(vehicleController, allTransforms, "wheel_rr", "meshRR");

        // Tire 할당 (Wheel_FL_tire 등)
        assignCount += AssignTransform(vehicleController, allTransforms, "wheel_fl_tire", "tireFL");
        assignCount += AssignTransform(vehicleController, allTransforms, "wheel_fr_tire", "tireFR");
        assignCount += AssignTransform(vehicleController, allTransforms, "wheel_rl_tire", "tireRL");
        assignCount += AssignTransform(vehicleController, allTransforms, "wheel_rr_tire", "tireRR");

        EditorUtility.SetDirty(vehicleController);
        EditorUtility.DisplayDialog("Complete", 
            $"{assignCount}개의 컴포넌트를 할당했습니다!", "OK");
    }

    private static int AssignWheel(Component target, WheelCollider[] wheels, string pattern, string fieldName)
    {
        foreach (var wheel in wheels)
        {
            if (wheel.name.ToLower().Contains(pattern))
            {
                var field = typeof(VehicleController).GetField(fieldName, 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (field != null)
                {
                    field.SetValue(target, wheel);
                    Debug.Log($"✓ {fieldName} ← {wheel.name}");
                    return 1;
                }
            }
        }
        return 0;
    }

    private static int AssignTransform(Component target, Transform[] transforms, string pattern, string fieldName)
    {
        foreach (var transform in transforms)
        {
            if (transform.name.ToLower().Contains(pattern))
            {
                var field = typeof(VehicleController).GetField(fieldName, 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (field != null)
                {
                    field.SetValue(target, transform);
                    Debug.Log($"✓ {fieldName} ← {transform.name}");
                    return 1;
                }
            }
        }
        return 0;
    }
}
