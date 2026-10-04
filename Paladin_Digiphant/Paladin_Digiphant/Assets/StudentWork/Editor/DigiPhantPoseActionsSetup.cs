using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiPhant.Editor
{
    // One-click setup for DigiPhantPoseActions in the open scene, following the Pose Playbook:
    // three performers, full body, P1 drives (left hand travel, knee jump), P2 steers by lean and
    // calls T-pose / arms-overhead actions. Safe to run again; everything is undoable.
    public static class DigiPhantPoseActionsSetup
    {
        const string PivotName = "Action Pivot";

        [MenuItem("DigiPhant/Set Up Pose Actions")]
        public static void SetUp()
        {
            if (Application.isPlaying) { Debug.LogWarning("Stop Play before setting up pose actions."); return; }
            var controller = Object.FindAnyObjectByType<DigiPhantController>();
            if (!controller) { Debug.LogWarning("Open the DigiPhant_Student scene first."); return; }
            var locomotion = controller.GetComponent<DigiPhantLocomotion>();
            if (!locomotion || !locomotion.travelRoot)
            { Debug.LogWarning("Run DigiPhant → Add Locomotion To Open Scene first."); return; }
            var animator = locomotion.elephantAnimator
                ? locomotion.elephantAnimator
                : controller.controls.SelectMany(c => c.bones).Where(b => b).Select(b => b.GetComponentInParent<Animator>()).FirstOrDefault(a => a);
            if (!animator) { Debug.LogWarning("Couldn't find the elephant's Animator."); return; }

            Undo.SetCurrentGroupName("Set up pose actions");

            // Travel root → Action Pivot → elephant, so jump and rear up move the model without fighting travel.
            Transform pivot = animator.transform.parent;
            if (!pivot || pivot.name != PivotName)
            {
                var go = new GameObject(PivotName);
                Undo.RegisterCreatedObjectUndo(go, "Add action pivot");
                pivot = go.transform;
                Undo.SetTransformParent(pivot, locomotion.travelRoot, "Parent action pivot");
                pivot.localPosition = Vector3.zero;
                pivot.localRotation = Quaternion.identity;
                pivot.localScale = Vector3.one;
                Undo.SetTransformParent(animator.transform, pivot, "Parent elephant under action pivot");
            }

            var actions = controller.GetComponent<DigiPhantPoseActions>();
            if (!actions) actions = Undo.AddComponent<DigiPhantPoseActions>(controller.gameObject);
            Undo.RecordObject(actions, "Configure pose actions");
            actions.actionPivot = pivot;
            actions.driver = 1;
            actions.navigator = 2;

            Undo.RecordObject(controller, "Pose playbook performers");
            if (controller.performerCount != 3) controller.SetPerformerCount(3);
            controller.upperBodyOnly = false; // knee lift needs ankles

            // Playbook: travel stays on P1 left hand; steering moves from P1 lean to P2 lean.
            Undo.RecordObject(locomotion, "Pose playbook steering");
            locomotion.steering.sources = new[] { new MovementSource { performer = 2, movement = Movement.Lean, weight = 1 } };

            EditorUtility.SetDirty(actions);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(locomotion);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("Pose actions set up: Action Pivot added, 3 performers, full body, steering on P2 lean. Save the scene (Cmd+S).");
        }
    }
}
