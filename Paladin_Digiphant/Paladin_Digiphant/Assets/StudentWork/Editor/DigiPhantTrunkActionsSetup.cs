using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiPhant.Editor
{
    // One-click setup for P3's trunk moves in the open scene: a "Trunk swing" control (P3 lean for now,
    // later the tracker's hand-to-the-side signal), a placeholder log to pick up, and DigiPhantTrunkActions
    // wired to the trunk tip. Safe to run again; everything is undoable.
    public static class DigiPhantTrunkActionsSetup
    {
        const string SwingLabel = "Trunk swing", PropName = "Carry Log", TipBone = "elephant_Trunk7-nub_bone";
        const string MaterialPath = "Assets/StudentWork/Materials/CarryLog.mat";

        [MenuItem("DigiPhant/Set Up Trunk Actions")]
        public static void SetUp()
        {
            if (Application.isPlaying) { Debug.LogWarning("Stop Play before setting up trunk actions."); return; }
            var controller = Object.FindAnyObjectByType<DigiPhantController>();
            if (!controller) { Debug.LogWarning("Open the DigiPhant_Student scene first."); return; }
            var locomotion = controller.GetComponent<DigiPhantLocomotion>();
            if (!locomotion || !locomotion.travelRoot)
            { Debug.LogWarning("Run DigiPhant → Set Up Pose Actions first."); return; }
            var curl = controller.controls.FirstOrDefault(c => c.label == "Trunk curl");
            if (curl == null || curl.bones == null || curl.bones.Length == 0)
            { Debug.LogWarning("Couldn't find the \"Trunk curl\" control and its bones."); return; }
            var animator = locomotion.elephantAnimator ? locomotion.elephantAnimator : curl.bones[0].GetComponentInParent<Animator>();
            var tip = animator ? animator.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == TipBone) : null;
            if (!tip) { Debug.LogWarning("Couldn't find the trunk tip bone " + TipBone + "."); return; }

            Undo.SetCurrentGroupName("Set up trunk actions");

            // Stage 1: trunk swing on the same seven trunk bones, turning about local Y (check with its test slider).
            Undo.RecordObject(controller, "Add trunk swing");
            if (!controller.controls.Any(c => c.label == SwingLabel))
            {
                var swing = new BoneControl
                {
                    label = SwingLabel, performer = curl.performer, movement = Movement.Lean,
                    bones = curl.bones.ToArray(), localAxis = Vector3.up, degrees = 7,
                    sensitivity = 3, smoothing = 8, locomotionWeight = 1,
                };
                controller.controls = controller.controls.Append(swing).ToArray();
            }

            // Stage 2: a placeholder log on the ground ahead of the elephant, lying across its path.
            var prop = GameObject.Find(PropName);
            if (!prop)
            {
                prop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                prop.name = PropName;
                Undo.RegisterCreatedObjectUndo(prop, "Add carry log");
                Object.DestroyImmediate(prop.GetComponent<Collider>());
                var root = locomotion.travelRoot;
                prop.transform.localScale = new Vector3(.35f, .7f, .35f); // 1.4 units long, 0.35 thick
                prop.transform.rotation = root.rotation * Quaternion.Euler(0, 0, 90);
                prop.transform.position = root.position + root.forward * 6 + Vector3.up * .175f;
                prop.GetComponent<Renderer>().sharedMaterial = LogMaterial();
            }

            var actions = controller.GetComponent<DigiPhantTrunkActions>();
            if (!actions) actions = Undo.AddComponent<DigiPhantTrunkActions>(controller.gameObject);
            Undo.RecordObject(actions, "Configure trunk actions");
            actions.trunkPerformer = curl.performer;
            actions.trunkTip = tip;
            actions.carryProp = prop.transform;

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(actions);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("Trunk actions set up: \"Trunk swing\" control, Carry Log ahead of the elephant, trunk tip linked. Save the scene (Cmd+S).");
        }

        static Material LogMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing) return existing;
            if (!AssetDatabase.IsValidFolder("Assets/StudentWork/Materials"))
                AssetDatabase.CreateFolder("Assets/StudentWork", "Materials");
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "CarryLog" };
            var brown = new Color(.42f, .27f, .14f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", brown);
            if (material.HasProperty("_Color")) material.SetColor("_Color", brown);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }
    }
}
