using DailyChallenges;
using UnityEditor;
using UnityEngine;

namespace DailyChallenges.Editor
{
    [CustomEditor(typeof(DailyChallengeController))]
    public sealed class DailyChallengeControllerEditor : UnityEditor.Editor
    {
        private DailyChallengeController controllerTarget;

        private void OnEnable()
        {
            controllerTarget = target as DailyChallengeController;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (controllerTarget == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("JSON Export", EditorStyles.boldLabel);

            if (GUILayout.Button("Export Daily Challenge Config"))
            {
                controllerTarget.ExportJsonConfig();
                AssetDatabase.Refresh();
            }

            EditorGUILayout.HelpBox(
                "This creates a JSON file under Assets/Resources using the current daily challenge data. You can use this format later for remote config or data-driven loading.",
                MessageType.Info);
        }
    }
}
