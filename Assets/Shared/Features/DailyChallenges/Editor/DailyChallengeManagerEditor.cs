using DailyChallenges;
using UnityEditor;
using UnityEngine;

namespace DailyChallenges.Editor
{
    [CustomEditor(typeof(DailyChallengeManager))]
    public sealed class DailyChallengeManagerEditor : UnityEditor.Editor
    {
        private DailyChallengeManager managerTarget;

        private void OnEnable()
        {
            managerTarget = target as DailyChallengeManager;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (managerTarget == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Daily Challenge Controls", EditorStyles.boldLabel);

            if (GUILayout.Button("Reset All Daily Challenge States"))
            {
                Undo.RecordObject(managerTarget, "Reset Daily Challenge States");
                managerTarget.ResetAllDailyChallengeStates();
                EditorUtility.SetDirty(managerTarget);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Reward Progression Testing", EditorStyles.boldLabel);

            if (GUILayout.Button("Add +1 Completed Day (Reward Progress)"))
            {
                Undo.RecordObject(managerTarget, "Debug Add Reward Progress Day");
                managerTarget.DebugAddRewardProgressDay();
                EditorUtility.SetDirty(managerTarget);
            }

            if (GUILayout.Button("Reset Reward Progress"))
            {
                Undo.RecordObject(managerTarget, "Debug Reset Reward Progress");
                managerTarget.DebugResetRewardProgress();
                EditorUtility.SetDirty(managerTarget);
            }

            EditorGUILayout.HelpBox(
                "Play mode only. Fakes completed days to drive the reward track's progress bar and tier auto-claim without actually winning puzzles.",
                MessageType.Info);

            EditorGUILayout.Space();

            if (GUILayout.Button("Simulate Full Day Win -> Reward Screen"))
            {
                Undo.RecordObject(managerTarget, "Debug Simulate Full Day Win");
                managerTarget.DebugSimulateFullDayWinAndShowRewardScreen();
                EditorUtility.SetDirty(managerTarget);
            }

            EditorGUILayout.HelpBox(
                "Play mode only. Marks the selected (or today's) day fully won and reopens this popup, so the win result flow and the reward track's auto-claim/collect animation play exactly as they would after a real win.",
                MessageType.Info);
        }
    }
}
