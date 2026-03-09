using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class MaterialConverterToURP : EditorWindow
{
    [MenuItem("Tools/Convert Materials to URP")]
    public static void ShowWindow()
    {
        GetWindow<MaterialConverterToURP>("Material Converter");
    }

    private void OnGUI()
    {
        GUILayout.Label("Convert Built-in Materials to URP", EditorStyles.boldLabel);
        GUILayout.Space(10);

        if (GUILayout.Button("Convert All Materials in Project", GUILayout.Height(40)))
        {
            ConvertAllMaterials();
        }

        GUILayout.Space(10);
        EditorGUILayout.HelpBox("This will convert all Standard shaders to URP/Lit shader.", MessageType.Info);
    }

    private static void ConvertAllMaterials()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material");
        int convertedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material != null && material.shader != null)
            {
                string shaderName = material.shader.name;

                // Built-in 쉐이더를 URP 쉐이더로 매핑
                if (shaderName.Contains("Standard") || shaderName.Contains("Diffuse") || shaderName.Contains("Bumped"))
                {
                    Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (urpShader != null)
                    {
                        material.shader = urpShader;
                        EditorUtility.SetDirty(material);
                        convertedCount++;
                        Debug.Log($"Converted: {path}");
                    }
                }
                else if (shaderName.Contains("Unlit"))
                {
                    Shader urpShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (urpShader != null)
                    {
                        material.shader = urpShader;
                        EditorUtility.SetDirty(material);
                        convertedCount++;
                        Debug.Log($"Converted: {path}");
                    }
                }
                else if (shaderName.Contains("Transparent") || shaderName.Contains("Cutout"))
                {
                    Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (urpShader != null)
                    {
                        material.shader = urpShader;
                        EditorUtility.SetDirty(material);
                        convertedCount++;
                        Debug.Log($"Converted: {path}");
                    }
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        EditorUtility.DisplayDialog("Conversion Complete", 
            $"Converted {convertedCount} materials to URP shaders.", "OK");
    }
}
