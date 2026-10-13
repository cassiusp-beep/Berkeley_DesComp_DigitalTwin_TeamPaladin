using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DigiPhant.Editor
{
    public static class DigiPhantAdventureValidation
    {
        const string Scene = "Assets/StudentWork/Scenes/DigiPhant_Adventure.unity";
        [MenuItem("DigiPhant/Open Adventure")]
        public static void Open()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play before opening Adventure."); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(Scene);
        }
        [MenuItem("DigiPhant/Validate Adventure in Play mode")]
        public static void Validate()
        {
            var game = UnityEngine.Object.FindAnyObjectByType<DigiPhantAdventure>();
            if (!EditorApplication.isPlaying || !game) throw new InvalidOperationException("Open Adventure and press Play before validating.");
            var c = game.GetComponent<DigiPhantController>();
            var loco = game.GetComponent<DigiPhantLocomotion>();
            var pose = game.GetComponent<DigiPhantPoseActions>();
            var trunk = game.GetComponent<DigiPhantTrunkActions>();
            c.SetInputMode(InputMode.TestSliders);
            void Check(bool ok, string why) { if (!ok) throw new Exception("Adventure: " + why); }
            object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
            void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
            object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(target, args);
            var wallTest = typeof(DigiPhantAdventure).GetMethod("WallEntry", BindingFlags.Static | BindingFlags.NonPublic);
            float Entry(Vector2 from, Vector2 to) => (float)wallTest.Invoke(null, new object[] { from, to, new Rect(1, -4, 1, 8) });
            Check(Entry(new Vector2(0, 0), new Vector2(3, 0)) < .34f, "wall sweep must stop even a full crossing in one frame");
            Check(Entry(new Vector2(0, -5), new Vector2(0, 5)) == 1, "open corridor must remain passable");
            Check(Entry(new Vector2(.9f, 0), new Vector2(0, 0)) == 1, "backing away must remain possible");
            Check(Entry(new Vector2(1.1f, 0), new Vector2(.8f, 0)) == 1, "initial overlap must allow escape");
            game.sound = false;
            foreach (DigiPhantAdventure.Difficulty difficulty in Enum.GetValues(typeof(DigiPhantAdventure.Difficulty)))
            {
                game.difficulty = difficulty; game.game = DigiPhantAdventure.Game.RockRun; game.BeginGame();
                var rocks = (IList)Get(game, "rocks");
                Check(rocks.Count == 3 + (int)difficulty * 2, "difficulty did not change rock count");
                object rock = rocks[rocks.Count - 1]; Vector3 local = (Vector3)Get(rock, "position");
                Vector3 forward = (Vector3)Get(rock, "forward");
                Vector3 before = (Vector3)Call(game, "World", local - forward);
                Set(game, "previousPosition", before);
                Call(game, "UpdateRocks", local + forward);
                Check(game.Score == -1, "rock hit must subtract one");
                Call(game, "UpdateRocks", local + forward);
                Check(game.Score == -1, "same rock scored twice");
                game.BeginGame(); rocks = (IList)Get(game, "rocks"); local = (Vector3)Get(rocks[rocks.Count - 1], "position");
                Set(game, "previousPosition", (Vector3)Call(game, "World", local - forward));
                Set(pose, "jumpStart", Time.time - pose.jumpSeconds / 2);
                Call(game, "UpdateRocks", local + forward);
                Check(game.Score == 1, "airborne crossing must award one");
            }
            game.game = DigiPhantAdventure.Game.Delivery; game.BeginGame();
            var parcels = (IList)Get(game, "cargo");
            Check(parcels.Count == 3 && game.Score == 0 && game.Health == 100, "new run must reset tasks, score and health");
            object parcel = parcels[0]; var target = (Transform)Get(parcel, "target");
            var item = (Transform)Get(parcel, "item");
            item.position = target.position;
            Call(game, "UpdateDeliveries"); Check(game.Score == 0, "delivery before pickup must not score");
            Set(parcel, "deadline", 20f); Set(game, "elapsed", 1f);
            Call(game, "UpdateDeliveries"); Check(game.Score == 1, "on-time delivery must score");
            Call(game, "UpdateDeliveries"); Check(game.Score == 1, "delivery scored twice");
            parcel = parcels[1]; Set(parcel, "deadline", 20f); Set(game, "elapsed", 20f);
            ((Transform)Get(parcel, "item")).position = ((Transform)Get(parcel, "target")).position;
            Call(game, "UpdateDeliveries"); Check(game.Score == 1, "expired delivery must never score, including at deadline");
            Check((int)Get(game, "activeCargo") == 2, "timeout must advance to next parcel");
            game.BeginGame();
            var p = (Vector3)Call(game, "World", new Vector3(40, 0, 10));
            loco.travelRoot.position = p; Set(game, "outsideSeconds", 10f);
            Call(game, "LateUpdate"); Check(game.Health < 100, "off-road health should drain");
            Call(game, "ReturnToMenu"); float health = game.Health;
            Call(game, "LateUpdate"); Check(game.Health == health && loco.movementSuspended, "menu must stop health and movement");
            var trail = UnityEngine.Object.FindAnyObjectByType<TrunkTrailProgress>();
            if (trail)
            {
                game.game = DigiPhantAdventure.Game.Expedition; game.BeginGame();
                var follow = game.GetComponent<GroundFollow>();
                Check(follow && follow.enabled && follow.terrain && follow.terrain.gameObject.activeInHierarchy, "Trunk Trail terrain/following was disabled");
                Check(!((Transform)Get(game, "course")).Find("Savannah"), "practice ground must not overwrite Trunk Trail");
                var layout = new TrunkTrailLayout();
                Vector3 onRoad = (Vector3)Call(game, "TrailPoint", layout.loop, .7f * layout.loopLength);
                Check((float)Call(game, "DistanceToRoad", onRoad) < .1f, "curved road must count as safe");
                foreach (object box in (IList)Get(game, "cargo"))
                {
                    var location = ((Transform)Get(box, "item")).position;
                    float ground = follow.terrain.SampleHeight(location) + follow.terrain.transform.position.y;
                    Check(Mathf.Abs(location.y - ground - .45f) < .01f, "parcel must sit on terrain");
                }
                var frame = trail.finishLine.transform;
                Set(game, "previousPosition", frame.TransformPoint(0, 0, -1));
                loco.travelRoot.position = frame.TransformPoint(0, 0, 1);
                Call(game, "LateUpdate"); Check(game.State == DigiPhantAdventure.Phase.Playing, "nearby finish shortcut must not win");
                var gates = (IList)Get(game, "trailGates");
                for (int i = 0; i < gates.Count; i++)
                {
                    loco.travelRoot.position = (Vector3)Call(game, "World", gates[i]);
                    Set(game, "previousPosition", loco.travelRoot.position);
                    Call(game, "LateUpdate");
                }
                Check((int)Get(game, "nextTrailGate") == gates.Count, "clockwise route gates did not advance");
                Set(game, "previousPosition", frame.TransformPoint(0, 0, -1));
                loco.travelRoot.position = frame.TransformPoint(0, 0, 1);
                Call(game, "LateUpdate"); Check(game.State == DigiPhantAdventure.Phase.Results, "complete loop must finish at the real arch");
                game.BeginGame(); Check((int)Get(game, "nextTrailGate") == 0, "restart must reset route progress");
                trail.optionalLevel = true; game.BeginGame();
                Vector3 branch = (Vector3)Call(game, "TrailPoint", layout.branch, layout.branch.length * .5f);
                Check((float)Call(game, "DistanceToRoad", branch) <= TrunkTrailLayout.CorridorHalf, "bonus branch center must be safe");
                // Check both sides along the entire branch, not just one center point.
                for (float d = 0; d <= layout.branch.length; d += 1)
                {
                    layout.branch.At(d, out var tangent);
                    var center = (Vector3)Call(game, "TrailPoint", layout.branch, d);
                    var side = new Vector3(tangent.y, 0, -tangent.x) * 2.4f;
                    Check((float)Call(game, "DistanceToRoad", center + side) <= 3.3f &&
                        (float)Call(game, "DistanceToRoad", center - side) <= 3.3f, "bonus trail edges must not cause HP loss");
                }
                game.game = DigiPhantAdventure.Game.RockRun; game.BeginGame();
                var bonusRocks = (IList)Get(game, "rocks");
                object bonusRock = bonusRocks[bonusRocks.Count - 1];
                Check((int)Get(bonusRock, "reward") == 3, "bonus hurdle must award three");
                var bonusPos = (Vector3)Get(bonusRock, "position");
                var bonusForward = (Vector3)Get(bonusRock, "forward");
                Set(game, "previousPosition", (Vector3)Call(game, "World", bonusPos - bonusForward));
                Set(pose, "jumpStart", Time.time - pose.jumpSeconds / 2);
                Call(game, "UpdateRocks", bonusPos + bonusForward);
                Check(game.Score == 3, "bonus jump must score +3");
                Call(game, "UpdateRocks", bonusPos + bonusForward);
                Check(game.Score == 3, "bonus jump must only score once");
                game.BeginGame();
                bonusRocks = (IList)Get(game, "rocks");
                bonusRock = bonusRocks[bonusRocks.Count - 1];
                bonusPos = (Vector3)Get(bonusRock, "position");
                bonusForward = (Vector3)Get(bonusRock, "forward");
                Set(game, "previousPosition", (Vector3)Call(game, "World", bonusPos - bonusForward));
                Call(game, "UpdateRocks", bonusPos + bonusForward);
                Check(game.Score == 0, "bonus hit must not deduct points");
                Call(game, "UpdateRocks", bonusPos + bonusForward);
                Check(game.Score == 0, "resolved bonus hit must remain zero");
                foreach (var checkpoint in trail.bonusCheckpoints)
                {
                    if (!checkpoint || checkpoint.mode != CheckpointMode.Beam) continue;
                    var bridge = checkpoint.transform.Find("Beam log");
                    Check(!bridge || !bridge.gameObject.activeSelf, "old bridge must be hidden");
                    foreach (var surface in checkpoint.GetComponentsInChildren<WalkableSurface>(true))
                        Check(!surface.enabled, "removed bridge must not retain invisible raised ground");
                }
                trail.optionalLevel = false;
            }
            foreach (int people in new[] { 1, 2, 3, 4 })
            {
                Call(game, "SetRoles", people);
                Check(pose.driver <= people && pose.navigator <= people && trunk.trunkPerformer <= people, "roles exceed group size");
            }
            game.BeginGame();
            Check(!(bool)Get(game, "thumbSteering"), "easy controls must default to lean steering");
            Call(game, "ProcessGrabGesture", "Closed_Fist", 100f);
            Check((bool)Get(game, "grabArmed"), "brief fist must not trigger");
            Call(game, "ProcessGrabGesture", "Closed_Fist", 100.4f);
            Check(!(bool)Get(game, "grabArmed"), "held fist must trigger without an initial palm");
            Call(game, "ProcessGrabGesture", "None", 101f);
            Call(game, "ProcessGrabGesture", "Closed_Fist", 102f);
            Call(game, "ProcessGrabGesture", "Closed_Fist", 103f);
            Check(!(bool)Get(game, "grabArmed"), "lost hand detection must not rearm a held fist");
            Call(game, "ProcessGrabGesture", "Open_Palm", 104f);
            Call(game, "ProcessGrabGesture", "Open_Palm", 104.3f);
            Check((bool)Get(game, "grabArmed"), "opening the hand must rearm the next grab");
            var guide = game.GetComponent<DigiPhantTutorial>();
            Check(guide && guide.enabled && !guide.showButton, "Adventure must use the teammate movement guide without a duplicate button");
            Call(game, "OpenMovementGuide");
            float guideTime = (float)Get(game, "elapsed"), guideHealth = game.Health;
            Call(game, "Update"); Call(game, "LateUpdate");
            Check(guide.IsOpen && loco.movementSuspended && (float)Get(game, "elapsed") == guideTime && game.Health == guideHealth,
                "movement guide must pause movement, timer, and HP");
            guide.CloseGuide();
            Call(game, "Update");
            Check(!guide.IsOpen && loco.movementSuspended, "closing guide must consume this frame's input");
            Set(guide, "closedFrame", -1); Call(game, "Update");
            Check(!loco.movementSuspended, "game must resume after closing guide");
            var hands = game.GetComponent<DigiPhantGestureActions>();
            ((float[])Get(c, "lastSeen"))[0] = Time.realtimeSinceStartup;
            ((float[,])Get(c, "confidence"))[0, 0] = 1;
            Set(hands, "lastPacket", Time.realtimeSinceStartup);
            Set(hands, "hands", new[] {
                new HandGesture { slot = 1, gesture = "Open_Palm", score = .99f },
                new HandGesture { slot = 1, gesture = "Closed_Fist", score = .85f },
                new HandGesture { slot = 1, gesture = "Thumb_Up", score = .8f },
                new HandGesture { slot = 0, gesture = "Thumb_Down", score = 1 }
            });
            Check(hands.ReadAssignedGesture(1) == "Closed_Fist", "palm must not hide trunk action");
            Check(hands.ReadAssignedGesture(1, true) == "Thumb_Up", "steering must filter trunk gestures and unassigned hands");
            Check(hands.ReadAssignedGesture(2) == "None", "wrong performer must not drive trunk");
            Set(hands, "lastPacket", Time.realtimeSinceStartup - 10);
            Check(hands.ReadAssignedGesture(1) == "None", "stale hands must stop controlling");
            Call(game, "SetRoles", 1); game.game = DigiPhantAdventure.Game.RockRun; game.difficulty = DigiPhantAdventure.Difficulty.Easy;
            game.BeginGame(); Call(game, "ReturnToMenu"); game.sound = true;
            Debug.Log("Adventure validation PASSED: difficulty, rock hit/clear/duplicate, timed delivery/timeout/duplicate, HP, menu suspension, solo/multiplayer roles, hand ownership/staleness restart, easy-hand hold/release safety, and Trunk Trail terrain/curved route/parcels/finish gates.");
        }
    }
}
