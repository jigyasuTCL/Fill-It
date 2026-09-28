using Shared.Purchase;
using UnityEditor;
using UnityEngine;

namespace Shared.Purchase.Editor
{
    [CustomEditor(typeof(PurchaseSuccessPopupController))]
    public sealed class PurchaseSuccessPopupControllerEditor : UnityEditor.Editor
    {
        private PurchaseSuccessPopupController controllerTarget;

        private void OnEnable()
        {
            controllerTarget = target as PurchaseSuccessPopupController;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (controllerTarget == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Testing", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Test Show"))
                {
                    controllerTarget.ShowTestPreview();
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to test the popup.", MessageType.Info);
            }
        }
    }
}
