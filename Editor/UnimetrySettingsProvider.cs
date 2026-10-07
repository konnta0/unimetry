using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unimetry.Editor
{
    /// <summary>
    /// Project Settings editor for <see cref="UnimetrySettings"/>.
    /// </summary>
    internal static class UnimetrySettingsProvider
    {
        private const string AssetPath = "Assets/Resources/UnimetrySettings.asset";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Unimetry", SettingsScope.Project)
            {
                label = "Unimetry",
                guiHandler = _ => Draw(),
            };
        }

        private static void Draw()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnimetrySettings>(AssetPath);
            if (settings == null)
            {
                EditorGUILayout.HelpBox(
                    "Create a Resources asset to initialize Unimetry at startup when UNIMETRY_OTLP_ENDPOINT is not set.",
                    MessageType.Info);
                if (GUILayout.Button("Create Unimetry Settings"))
                {
                    CreateAsset();
                }

                return;
            }

            var serialized = new SerializedObject(settings);
            serialized.Update();
            EditorGUILayout.PropertyField(serialized.FindProperty("Endpoint"));
            EditorGUILayout.PropertyField(serialized.FindProperty("ServiceName"));
            EditorGUILayout.PropertyField(serialized.FindProperty("ServiceVersion"));
            EditorGUILayout.PropertyField(serialized.FindProperty("DeploymentEnvironment"));
            EditorGUILayout.PropertyField(serialized.FindProperty("AllowInsecureTls"));
            EditorGUILayout.PropertyField(serialized.FindProperty("ExportErrorSpans"));
            EditorGUILayout.PropertyField(serialized.FindProperty("CaptureNativeCrashes"));
            EditorGUILayout.PropertyField(serialized.FindProperty("CaptureMinidumps"));
            EditorGUILayout.PropertyField(serialized.FindProperty("CaptureMetrics"));
            serialized.ApplyModifiedProperties();
        }

        private static void CreateAsset()
        {
            var directory = Path.GetDirectoryName(AssetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = ScriptableObject.CreateInstance<UnimetrySettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
        }
    }
}
