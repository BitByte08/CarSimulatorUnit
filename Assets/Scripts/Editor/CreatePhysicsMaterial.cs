using UnityEditor;
using UnityEngine;

public class CreatePhysicsMaterial : EditorWindow
{
    [MenuItem("Tools/Create Road Physics Material")]
    public static void CreateRoadMaterial()
    {
        // Physics Material 생성
        PhysicsMaterial roadMaterial = new PhysicsMaterial("RoadSurface");
        roadMaterial.dynamicFriction = 0.8f;
        roadMaterial.staticFriction = 0.9f;
        roadMaterial.bounciness = 0f;
        roadMaterial.frictionCombine = PhysicsMaterialCombine.Average;
        roadMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;

        // Assets 폴더에 저장
        string path = "Assets/RoadSurface.physicMaterial";
        AssetDatabase.CreateAsset(roadMaterial, path);
        AssetDatabase.SaveAssets();

        // 선택된 오브젝트에 자동 적용
        GameObject[] selected = Selection.gameObjects;
        int appliedCount = 0;

        foreach (GameObject obj in selected)
        {
            Collider col = obj.GetComponent<Collider>();
            if (col != null)
            {
                col.material = roadMaterial;
                EditorUtility.SetDirty(col);
                appliedCount++;
            }

            // 자식 오브젝트의 Collider에도 적용
            Collider[] childColliders = obj.GetComponentsInChildren<Collider>();
            foreach (Collider childCol in childColliders)
            {
                if (childCol.gameObject != obj) // 부모는 이미 처리했으므로 제외
                {
                    childCol.material = roadMaterial;
                    EditorUtility.SetDirty(childCol);
                    appliedCount++;
                }
            }
        }

        EditorUtility.DisplayDialog("Complete", 
            $"Physics Material 생성 완료!\n저장 위치: {path}\n\n적용된 Collider: {appliedCount}개", "OK");
        
        // Project 창에서 선택
        Selection.activeObject = roadMaterial;
        EditorGUIUtility.PingObject(roadMaterial);
    }
}
