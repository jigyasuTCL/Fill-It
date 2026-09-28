using UnityEditor;
using UnityEngine;

namespace Game.SpinWheel.Editor
{
    /// <summary>
    /// Custom inspector for SpinWheelManager that adds a "Cheat Spin" button.
    ///
    /// IMPORTANT: this file must live inside a folder literally named
    /// "Editor" (e.g. Assets/Scripts/SpinWheel/Editor/). Unity automatically
    /// excludes anything under an "Editor" folder from player builds, which
    /// is what keeps this cheat button out of your shipped game.
    /// </summary>
    [CustomEditor(typeof(SpinWheelManager))]
    public class SpinWheelManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Draw the normal inspector fields first (player state, etc.)
            DrawDefaultInspector();

            var manager = (SpinWheelManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Cheats", EditorStyles.boldLabel);

            // Only usable in Play Mode since it needs remote config + a live
            // manager instance — GUI.enabled greys the button out otherwise
            // instead of letting you click it and hit a null-ref.
            GUI.enabled = Application.isPlaying;

            if (GUILayout.Button("Cheat Spin (bypass unlock + wins)", GUILayout.Height(30)))
            {
                manager.Debug_CheatSpin();
            }

            GUI.enabled = true;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use the cheat button — it needs the remote config to already be applied.", MessageType.Info);
            }
        }
    }
}