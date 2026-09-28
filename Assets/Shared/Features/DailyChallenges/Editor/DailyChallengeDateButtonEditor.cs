using DailyChallenges;
using UnityEditor;
using UnityEngine;

namespace DailyChallenges.Editor
{
    [CustomEditor(typeof(DailyChallengeDateButton))]
    public sealed class DailyChallengeDateButtonEditor : UnityEditor.Editor
    {
        private DailyChallengeDateButton buttonTarget;

        private void OnEnable()
        {
            buttonTarget = target as DailyChallengeDateButton;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (buttonTarget == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debug Play State", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(buttonTarget.Data == null))
            {
                // if (GUILayout.Button("Set Win"))
                // {
                //     Undo.RecordObject(buttonTarget, "Set Win State");
                //     buttonTarget.SetWinState();
                //     EditorUtility.SetDirty(buttonTarget);
                // }

                // if (GUILayout.Button("Set Lose"))
                // {
                //     Undo.RecordObject(buttonTarget, "Set Lose State");
                //     buttonTarget.SetLoseState();
                //     EditorUtility.SetDirty(buttonTarget);
                // }

                // if (GUILayout.Button("Set Not Played"))
                // {
                //     Undo.RecordObject(buttonTarget, "Set Not Played State");
                //     buttonTarget.SetNotPlayedState();
                //     EditorUtility.SetDirty(buttonTarget);
                // }
            }

            if (buttonTarget.Data == null)
            {
                EditorGUILayout.HelpBox("Day data is not initialized in edit mode. Play the scene or assign data through code.", MessageType.Info);
            }
        }
    }
}
