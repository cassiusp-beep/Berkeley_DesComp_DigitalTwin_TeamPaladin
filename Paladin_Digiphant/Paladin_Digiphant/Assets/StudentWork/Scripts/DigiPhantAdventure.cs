using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DigiPhant
{
    // Uses an existing Trunk Trail when present; otherwise builds the practice course in Play mode.
    [DefaultExecutionOrder(250)]
    public class DigiPhantAdventure : MonoBehaviour
    {
        public enum Game { RockRun, Delivery, Expedition }
        public enum Difficulty { Easy, Normal, Hard }
        public enum Phase { Menu, Playing, Results }
        public Phase State { get; private set; }
        public Game game;
        public Difficulty difficulty;
        public int Score { get; private set; }
        public float Health { get; private set; } = 100;
        public bool sound = true;
        public bool useHands = true;
        public bool thumbSteering; // Optional advanced controls; lean steering is the default.
        bool gameplayCameraVisible;
        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        DigiPhantPoseActions pose;
        DigiPhantTrunkActions trunk;
        DigiPhantGestureActions gestures;
        DigiPhantCameraPreview preview;
        DigiPhantTutorial movementGuide;
        Transform course;
        TrunkTrailProgress trail;
        GroundFollow groundFollow;
        TrunkTrailLayout trailLayout;
        int nextTrailGate;
        readonly List<Vector3> trailGates = new List<Vector3>();
        Vector3 origin, initialPosition, cameraPosition;
        Quaternion initialRotation;
        readonly List<Vector3> route = new List<Vector3>();
        readonly List<Rock> rocks = new List<Rock>();
        readonly List<Cargo> cargo = new List<Cargo>();
        readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        AudioSource audioSource;
        float length, halfWidth, elapsed, outsideSeconds, nextFootstep, feedbackUntil, damageFlash;
        string feedback = "", result = "", lastGrabGesture = "None";
        bool grabArmed, previousCarry, previousJump;
        float gestureSince;
        int activeCargo;
        Vector3 previousPosition;
        Vector2 menuScroll, hudScroll, instructionScroll;
        bool cameraExpanded;
        float cameraPanelBottom;
        GUISkin skin;
        Material road, grass, rockMaterial, orange, green, cream;
        struct Rock { public Transform transform; public Vector3 position, forward; public bool resolved; }
        class Cargo
        {
            public Transform item, target;
            public float deadline = -1;
            public bool resolved;
        }
        float DeliverySeconds => difficulty == Difficulty.Easy ? 45 : difficulty == Difficulty.Normal ? 30 : 20;
        float DamagePerSecond => difficulty == Difficulty.Easy ? 4 : difficulty == Difficulty.Normal ? 8 : 12;
        float Grace => difficulty == Difficulty.Easy ? 2 : difficulty == Difficulty.Normal ? 1.25f : .75f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "DigiPhant_Adventure" && SceneManager.GetActiveScene().name != "DigiPhant_TrunkTrail") return;
            var c = FindAnyObjectByType<DigiPhantController>();
            if (c && !c.GetComponent<DigiPhantAdventure>()) c.gameObject.AddComponent<DigiPhantAdventure>();
        }
        void Start()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            pose = GetComponent<DigiPhantPoseActions>();
            trunk = GetComponent<DigiPhantTrunkActions>();
            gestures = GetComponent<DigiPhantGestureActions>();
            if (!gestures) gestures = gameObject.AddComponent<DigiPhantGestureActions>();
            preview = GetComponent<DigiPhantCameraPreview>();
            if (!locomotion || !locomotion.travelRoot || !pose || !trunk)
            { Debug.LogError("Adventure needs the student scene's locomotion, pose and trunk components."); enabled = false; return; }
            initialPosition = origin = locomotion.travelRoot.position;
            initialRotation = locomotion.travelRoot.rotation;
            cameraPosition = locomotion.followCamera ? locomotion.followCamera.transform.position : Vector3.zero;
            previousPosition = initialPosition;
            trail = FindAnyObjectByType<TrunkTrailProgress>();
            groundFollow = GetComponent<GroundFollow>();
            if (trail)
            {
                trailLayout = new TrunkTrailLayout();
                trail.showHud = false;
                if (groundFollow) groundFollow.enabled = true;
                if (trunk.carryProp) trunk.carryProp.gameObject.SetActive(false);
            }
            // Only the standalone practice scene replaces its environment.
            if (!trail)
            {
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (transform.IsChildOf(root.transform) || locomotion.travelRoot.IsChildOf(root.transform)
                    || root.GetComponentInChildren<Camera>() || root.GetComponentInChildren<Light>()) continue;
                root.SetActive(false);
            }
            foreach (var follow in FindObjectsByType<GroundFollow>()) follow.enabled = false;
            }
            movementGuide = GetComponent<DigiPhantTutorial>();
            if (!movementGuide) movementGuide = gameObject.AddComponent<DigiPhantTutorial>();
            movementGuide.openOnFirstCamera = false;
            movementGuide.showButton = false;
            movementGuide.enabled = true;
            var diagnostics = GetComponent<StudentWork.StudentSignalDiagnostics>();
            if (diagnostics) diagnostics.show = false;
            pose.showSignals = trunk.showStatus = gestures.showPanel = false;
            controller.showControls = false;
            if (preview) preview.showPreview = false;
            gestures.adventureControls = true;
            locomotion.stageRadius = 0;
            locomotion.enableLocomotion = true;
            locomotion.followElephant = false;
            UpdateCamera(true);
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false; audioSource.spatialBlend = 0; audioSource.volume = .35f;
            CreateSounds();
            road = Material(new Color(.66f, .45f, .26f));
            grass = Material(new Color(.33f, .43f, .22f));
            rockMaterial = Material(new Color(.34f, .38f, .41f));
            orange = Material(new Color(1, .47f, .10f));
            green = Material(new Color(.2f, .75f, .4f));
            cream = Material(new Color(1, .88f, .59f));
            SetRoles(1);
            BuildCourse();
            Suspend(true);
        }
        Material Material(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            var m = new Material(shader) { color = color }; resources.Add(m); return m;
        }
        Vector3 World(Vector3 p) => origin + initialRotation * p;
        Vector3 Local(Vector3 p) => Quaternion.Inverse(initialRotation) * (p - origin);
        Vector3 Path(float z) => new Vector3(Mathf.Sin(z / 22f) * Mathf.SmoothStep(0, 5, z / 20), 0, z);
        Transform Shape(string name, PrimitiveType primitive, Vector3 p, Vector3 scale, Material material)
        {
            var o = GameObject.CreatePrimitive(primitive); o.name = name;
            o.transform.SetParent(course, false); o.transform.position = World(p);
            o.transform.rotation = initialRotation; o.transform.localScale = scale;
            o.GetComponent<Renderer>().sharedMaterial = material;
            var collider = o.GetComponent<Collider>(); if (collider) Destroy(collider);
            return o.transform;
        }
        void BuildCourse()
        {
            if (course) { course.gameObject.SetActive(false); Destroy(course.gameObject); }
            course = new GameObject("Adventure course (generated)").transform;
            trunk.AssignProp(null);
            route.Clear(); rocks.Clear(); cargo.Clear(); activeCargo = 0;
            if (trail) { BuildTrailTasks(); return; }
            length = difficulty == Difficulty.Easy ? 90 : difficulty == Difficulty.Normal ? 140 : 190;
            halfWidth = difficulty == Difficulty.Easy ? 5 : difficulty == Difficulty.Normal ? 4 : 3;
            Shape("Savannah", PrimitiveType.Cube, new Vector3(0, -.3f, length / 2), new Vector3(90, .5f, length + 80), grass);
            for (float z = 0; z <= length + 4; z += 2)
            {
                var point = Path(z); route.Add(point);
                if (route.Count < 2) continue;
                var a = route[route.Count - 2]; var delta = point - a;
                var segment = Shape("Trail", PrimitiveType.Cube, (a + point) / 2 + Vector3.down * .025f,
                    new Vector3(halfWidth * 2, .08f, delta.magnitude + .15f), road);
                segment.rotation = initialRotation * Quaternion.LookRotation(delta);
                if ((int)z % 8 == 0)
                {
                    Shape("Left road marker", PrimitiveType.Cylinder, point + new Vector3(-halfWidth, .28f, 0), new Vector3(.16f, .3f, .16f), cream);
                    Shape("Right road marker", PrimitiveType.Cylinder, point + new Vector3(halfWidth, .28f, 0), new Vector3(.16f, .3f, .16f), cream);
                }
                if ((int)z % 12 == 0)
                {
                    float side = (int)z % 24 == 0 ? -1 : 1;
                    var tree = point + new Vector3(side * (halfWidth + 9), 0, 0);
                    Shape("Acacia trunk", PrimitiveType.Cylinder, tree + Vector3.up * 2, new Vector3(.5f, 2, .5f), road);
                    Shape("Acacia canopy", PrimitiveType.Sphere, tree + Vector3.up * 4, new Vector3(6, 1.6f, 4), grass);
                }
            }
            if (game != Game.Delivery)
            {
                int count = 3 + (int)difficulty * 2;
                for (int i = 0; i < count; i++)
                {
                    float z = 16 + i * (length - 32) / Mathf.Max(1, count - 1);
                    Vector3 p = Path(z);
                    var t = Shape("Jump rock " + (i + 1), PrimitiveType.Sphere, p + Vector3.up * .3f, new Vector3(halfWidth * 1.5f, .7f, 1.2f), rockMaterial);
                    rocks.Add(new Rock { transform = t, position = p, forward = Vector3.forward });
                    LabelWorld("JUMP", p + new Vector3(halfWidth + 1, 1.5f, -2));
                }
            }
            if (game != Game.RockRun)
            {
                for (int i = 0; i < 3; i++)
                {
                    float z = 10 + i * (length - 35) / 3;
                    var p = Path(z) + new Vector3(halfWidth * .4f, .45f, 0);
                    var item = Shape("Parcel " + (i + 1), PrimitiveType.Cube, p, new Vector3(.9f, .9f, .9f), orange);
                    var targetPoint = Path(z + 12);
                    var target = Shape("Delivery zone " + (i + 1), PrimitiveType.Cylinder, targetPoint + Vector3.up * .07f, new Vector3(4, .05f, 4), green);
                    LabelWorld("PICK UP " + (i + 1), p + new Vector3(0, 1.5f, 0));
                    LabelWorld("DELIVER " + (i + 1), targetPoint + new Vector3(halfWidth + 1, 1.5f, 0));
                    cargo.Add(new Cargo { item = item, target = target });
                }
            }
            if (cargo.Count > 0) trunk.AssignProp(cargo[0].item); else trunk.AssignProp(null);
            Vector3 finish = Path(length);
            Shape("Finish left", PrimitiveType.Cube, finish + new Vector3(-halfWidth, 3, 0), new Vector3(.5f, 6, .5f), cream);
            Shape("Finish right", PrimitiveType.Cube, finish + new Vector3(halfWidth, 3, 0), new Vector3(.5f, 6, .5f), cream);
            Shape("Finish arch", PrimitiveType.Cube, finish + Vector3.up * 6, new Vector3(halfWidth * 2 + 1, .6f, .6f), orange);
            LabelWorld("FINISH", finish + Vector3.up * 5);
        }
        static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        Vector3 TrailPoint(TrunkTrailLayout.PathLine line, float distance)
        {
            Vector2 p = line.At(distance, out _);
            Vector3 world = World(new Vector3(p.x, 0, p.y));
            if (groundFollow && groundFollow.terrain)
                world.y = groundFollow.terrain.SampleHeight(world) + groundFollow.terrain.transform.position.y;
            return Local(world);
        }
        void BuildTrailTasks()
        {
            trail.Restart(); nextTrailGate = 0; trailGates.Clear();
            halfWidth = TrunkTrailLayout.CorridorHalf;
            length = trailLayout.leadIn.length + .925f * trailLayout.loopLength;
            for (float d = 0; d < trailLayout.leadIn.length; d += 1)
                route.Add(TrailPoint(trailLayout.leadIn, d));
            route.Add(TrailPoint(trailLayout.leadIn, trailLayout.leadIn.length));
            for (float d = .05f * trailLayout.loopLength; d <= trailLayout.loopLength; d += 1)
                route.Add(TrailPoint(trailLayout.loop, d));
            for (int i = 1; i <= 9; i++)
                if (!trail.optionalLevel || i < 2 || i > 4)
                    trailGates.Add(TrailPoint(trailLayout.loop, i * .1f * trailLayout.loopLength));
            foreach (var checkpoint in trail.mainCheckpoints)
            {
                if (!checkpoint || checkpoint.mode != CheckpointMode.Hurdle) continue;
                checkpoint.adventureManagedJump = true;
                checkpoint.gameObject.SetActive(game != Game.Delivery);
                if (game == Game.Delivery) continue;
                rocks.Add(new Rock { transform = checkpoint.transform, position = Local(checkpoint.transform.position),
                    forward = Quaternion.Inverse(initialRotation) * checkpoint.transform.forward });
            }
            // Additional jumps on open stretches; never replace the team's existing hurdles or scenery.
            if (game != Game.Delivery)
                for (int i = 0; i < (int)difficulty * 2; i++)
                {
                    float d = (.55f + i * .10f) * trailLayout.loopLength;
                    var p = TrailPoint(trailLayout.loop, d);
                    trailLayout.loop.At(d, out var direction);
                    var forward = new Vector3(direction.x, 0, direction.y);
                    var t = Shape("Adventure jump rock", PrimitiveType.Sphere, p + Vector3.up * .3f,
                        new Vector3(halfWidth * 1.5f, .7f, 1.2f), rockMaterial);
                    t.rotation = initialRotation * Quaternion.LookRotation(forward);
                    rocks.Add(new Rock { transform = t, position = p, forward = forward });
                }
            if (game != Game.RockRun)
                for (int i = 0; i < 3; i++)
                {
                    float d = (.24f + i * .27f) * trailLayout.loopLength;
                    var p = TrailPoint(trailLayout.loop, d);
                    var targetPoint = TrailPoint(trailLayout.loop, d + 10);
                    var item = Shape("Parcel " + (i + 1), PrimitiveType.Cube, p + Vector3.up * .45f, Vector3.one * .9f, orange);
                    var target = Shape("Delivery zone " + (i + 1), PrimitiveType.Cylinder, targetPoint + Vector3.up * .08f, new Vector3(4, .05f, 4), green);
                    LabelWorld("PICK UP " + (i + 1), p + Vector3.up * 2);
                    LabelWorld("DELIVER " + (i + 1), targetPoint + Vector3.up * 2);
                    cargo.Add(new Cargo { item = item, target = target });
                }
            trunk.AssignProp(cargo.Count > 0 ? cargo[0].item : null);
            Suspend(State != Phase.Playing);
        }
        void LabelWorld(string text, Vector3 p)
        {
            var o = new GameObject(text); o.transform.SetParent(course); o.transform.position = World(p);
            o.transform.rotation = initialRotation;
            var label = o.AddComponent<TextMesh>(); label.text = text; label.fontSize = 48; label.characterSize = .075f;
            label.anchor = TextAnchor.MiddleCenter; label.color = new Color(.12f, .09f, .04f);
        }
        void SetRoles(int count)
        {
            controller.SetPerformerCount(count);
            pose.driver = 1; pose.navigator = Mathf.Min(2, count);
            trunk.trunkPerformer = Mathf.Min(3, count);
            pose.trunkPerformerOverride = trunk.trunkPerformer; // guided-recording instructions name Adventure's roles
            foreach (var source in locomotion.forward.sources) source.performer = 1;
            foreach (var source in locomotion.steering.sources) source.performer = Mathf.Min(2, count);
        }
        void Suspend(bool value)
        {
            locomotion.movementSuspended = value;
            pose.movementSuspended = value;
            if (value) { locomotion.StopMotion(); pose.ResetGameMotion(); }
            trunk.handGesturesOnly = value || useHands;
        }
        public void BeginGame()
        {
            if (controller.inputMode == InputMode.Camera && (!controller.IsCalibrated || controller.CalibrationPending))
            { Say("Calibrate your resting pose before starting."); return; }
            Score = 0; Health = 100; elapsed = outsideSeconds = 0; result = "";
            grabArmed = true; lastGrabGesture = "None"; previousCarry = previousJump = false;
            locomotion.StopMotion(); pose.ResetGameMotion();
            locomotion.travelRoot.SetPositionAndRotation(initialPosition, initialRotation);
            if (locomotion.followCamera) locomotion.followCamera.transform.position = cameraPosition;
            previousPosition = initialPosition;
            UpdateCamera(true);
            BuildCourse(); State = Phase.Playing; Suspend(false);
            Say(game == Game.RockRun ? "Clear the rocks and reach the finish!" : "Deliver parcels to their numbered green circles.");
        }
        void ReturnToMenu()
        {
            State = Phase.Menu; Suspend(true); locomotion.handSteering = false;
            trunk.ResetProp(); previousCarry = false;
        }
        void EndGame(bool won)
        {
            State = Phase.Results; Suspend(true);
            result = won ? "TRAIL COMPLETE!" : "OUT OF HEALTH";
            Play(won ? "win" : "hit");
        }
        void Say(string text) { feedback = text; feedbackUntil = Time.unscaledTime + 4; }
        void Update()
        {
            if (!controller || !locomotion) return;
            controller.showControls = false;
            if (movementGuide && (movementGuide.IsOpen || movementGuide.ClosedThisFrame))
            {
                Suspend(true);
                return;
            }
            if (pose.GuideActive)
            {
                // Guided recording owns calibration and the screen until it is closed.
                cameraExpanded = false;
                Suspend(true);
                return;
            }
            if (cameraExpanded)
            {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) cameraExpanded = false;
                if (State == Phase.Playing) Suspend(true);
                return;
            }
            if (State != Phase.Playing) return;
            // Calibration/reassignment and lost performers must not spend timers or health.
            if (controller.inputMode == InputMode.Camera)
                for (int person = 1; person <= controller.performerCount; person++)
                    if (!controller.IsTracked(person, Time.realtimeSinceStartup))
                    { Suspend(true); return; }
            // Calibration/reassignment should not spend a delivery timer or drain health.
            if (controller.inputMode == InputMode.Camera && (!controller.IsCalibrated || controller.CalibrationPending))
            { Suspend(true); return; }
            Suspend(false);
            if (useHands && controller.inputMode == InputMode.Camera) ReadHands();
            else { locomotion.handSteering = false; grabArmed = false; }
            var keyboard = Keyboard.current;
            if (keyboard != null && controller.inputMode == InputMode.TestSliders)
            {
                locomotion.testForward = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? .55f : 0)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? .5f : 0);
                locomotion.testSteering = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                if (keyboard.spaceKey.wasPressedThisFrame) pose.TestJump();
                if (keyboard.eKey.wasPressedThisFrame) ToggleGrab();
            }
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ReturnToMenu();
        }
        void ReadHands()
        {
            // Keep the familiar calibrated lean steering unless thumb steering was explicitly chosen.
            locomotion.handSteering = thumbSteering;
            if (thumbSteering)
            {
                string steering = gestures.ReadAssignedGesture(Mathf.Min(2, controller.performerCount), true);
                locomotion.handSteeringValue = steering == "Thumb_Up" ? .8f : steering == "Thumb_Down" ? -.8f : 0;
            }
            ProcessGrabGesture(gestures.ReadAssignedGesture(Mathf.Min(3, controller.performerCount)), Time.unscaledTime);
        }
        void ProcessGrabGesture(string grab, float now)
        {
            if (grab != lastGrabGesture) { lastGrabGesture = grab; gestureSince = now; }
            // Missing detections never re-arm an action: a held fist cannot repeatedly pick up/drop.
            if (grab == "Open_Palm" && now - gestureSince >= .2f) grabArmed = true;
            if (grab != "Closed_Fist" || !grabArmed || now - gestureSince < .35f) return;
            grabArmed = false;
            ToggleGrab();
        }
        void ToggleGrab()
        {
            if (!trunk.TryToggleGrab()) Say("Move the trunk closer to the parcel and slow down.");
        }
        void UpdateCamera(bool snap = false)
        {
            if (!locomotion || !locomotion.followCamera || !locomotion.travelRoot) return;
            var cam = locomotion.followCamera;
            var target = locomotion.travelRoot.position;
            var heading = trail ? Quaternion.Euler(0, locomotion.travelRoot.eulerAngles.y, 0) : initialRotation;
            Vector3 destination = target + heading * new Vector3(0, 10, -15);
            if (trail && groundFollow && groundFollow.terrain)
                destination.y = Mathf.Max(destination.y, groundFollow.terrain.SampleHeight(destination) + groundFollow.terrain.transform.position.y + 3);
            cam.transform.position = snap ? destination : Vector3.Lerp(cam.transform.position, destination, 1 - Mathf.Exp(-8 * Time.deltaTime));
            cam.transform.LookAt(target + heading * new Vector3(0, 1.5f, 8));
            cam.fieldOfView = 60;
        }
        void LateUpdate()
        {
            UpdateCamera();
            if (State != Phase.Playing || locomotion.movementSuspended) return;
            elapsed += Time.deltaTime;
            var position = Local(locomotion.travelRoot.position);
            float roadDistance = DistanceToRoad(position);
            outsideSeconds = roadDistance > halfWidth + .8f ? outsideSeconds + Time.deltaTime : 0;
            if (outsideSeconds > Grace)
            {
                Health = Mathf.Max(0, Health - DamagePerSecond * Time.deltaTime);
                damageFlash = Time.unscaledTime + .12f;
            }
            UpdateRocks(position);
            UpdateDeliveries();
            bool jumping = pose.JumpHeightNow > .05f;
            if (jumping && !previousJump) Play("jump");
            previousJump = jumping;
            if (!jumping && Mathf.Abs(locomotion.CurrentSpeed) > .15f && Time.time >= nextFootstep)
            { Play("step"); nextFootstep = Time.time + Mathf.Lerp(.6f, .25f, Mathf.Abs(locomotion.CurrentSpeed) / 4.5f); }
            if (Health <= 0) EndGame(false);
            else if (trail)
            {
                // Ordered gates prevent finishing immediately by walking backwards to the nearby loop exit.
                if (nextTrailGate < trailGates.Count && FlatDistance(position, trailGates[nextTrailGate]) < 5)
                    nextTrailGate++;
                if (nextTrailGate == trailGates.Count && trail.finishLine)
                {
                    var frame = trail.finishLine.transform;
                    var a = frame.InverseTransformPoint(previousPosition);
                    var b = frame.InverseTransformPoint(locomotion.travelRoot.position);
                    if (a.z < 0 && b.z >= 0 && Mathf.Abs(Mathf.Lerp(a.x, b.x, -a.z / (b.z - a.z))) <= trail.finishLine.zoneSize.x / 2)
                        EndGame(true);
                }
            }
            else if (position.z >= length && Mathf.Abs(position.x - Path(length).x) <= halfWidth) EndGame(true);
            previousPosition = locomotion.travelRoot.position;
        }
        float DistanceToRoad(Vector3 p)
        {
            float distance = float.MaxValue; p.y = 0;
            for (int i = 1; i < route.Count; i++)
            {
                Vector3 a = route[i - 1], b = route[i]; a.y = b.y = 0; Vector3 d = b - a;
                if (d.sqrMagnitude < .0001f) continue;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude);
                distance = Mathf.Min(distance, Vector3.Distance(p, a + d * t));
            }
            if (trail && trail.optionalLevel)
            {
                var points = trailLayout.branch.pts;
                for (int i = 1; i < points.Length; i++)
                {
                    var a = new Vector3(points[i - 1].x, 0, points[i - 1].y);
                    var d = new Vector3(points[i].x, 0, points[i].y) - a;
                    if (d.sqrMagnitude < .0001f) continue;
                    float t = Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude);
                    float width = Mathf.Lerp(trailLayout.branch.half[i - 1], trailLayout.branch.half[i], t);
                    distance = Mathf.Min(distance, Vector3.Distance(p, a + d * t) + halfWidth - width);
                }
            }
            return distance;
        }
        void UpdateRocks(Vector3 p)
        {
            Vector3 before = Local(previousPosition);
            for (int i = 0; i < rocks.Count; i++)
            {
                Rock rock = rocks[i]; if (rock.resolved) continue;
                // Resolve at the crossing, not anywhere in a large checkpoint box. Once per run, either direction.
                Vector3 forward = rock.forward.sqrMagnitude > 0 ? rock.forward : Vector3.forward;
                float a = Vector3.Dot(before - rock.position, forward), b = Vector3.Dot(p - rock.position, forward);
                if (a * b > 0 || Mathf.Approximately(a, b)) continue;
                Vector3 crossing = Vector3.Lerp(before, p, -a / (b - a));
                if (FlatDistance(crossing, rock.position) > halfWidth * .75f + .5f) continue;
                bool clear = pose.JumpHeightNow > .55f;
                Score += clear ? 1 : -1;
                rock.resolved = true; rocks[i] = rock;
                var renderer = rock.transform.GetComponent<Renderer>();
                if (renderer) renderer.sharedMaterial = clear ? green : orange;
                var checkpoint = rock.transform.GetComponent<TrunkCheckpoint>();
                if (checkpoint) checkpoint.ResolveAdventureJump(clear);
                Say(clear ? "Jump cleared · +1" : "Obstacle hit · −1"); Play(clear ? "point" : "hit");
            }
        }
        void UpdateDeliveries()
        {
            if (activeCargo >= cargo.Count) return;
            Cargo c = cargo[activeCargo];
            if (trunk.Carrying && !previousCarry)
            {
                if (c.deadline < 0) c.deadline = elapsed + DeliverySeconds;
                Play("pickup"); Say("Deliver parcel " + (activeCargo + 1) + " to its green circle.");
            }
            if (!trunk.Carrying && previousCarry) Play("drop");
            bool expired = c.deadline >= 0 && elapsed >= c.deadline;
            Vector3 delta = c.item.position - c.target.position; delta.y = 0;
            bool delivered = c.deadline >= 0 && !trunk.Carrying && delta.magnitude <= 2;
            if (expired || delivered)
            {
                c.resolved = true;
                if (!expired && delivered) { Score++; Play("point"); Say("Delivery complete · +1"); }
                else Say("Delivery timed out · no points");
                trunk.AssignProp(null); c.item.gameObject.SetActive(false);
                activeCargo++;
                if (activeCargo < cargo.Count) trunk.AssignProp(cargo[activeCargo].item);
            }
            previousCarry = trunk.Carrying;
        }
        void CreateSounds()
        {
            // Original synthesized cues: no downloaded sound licenses or external audio dependencies.
            AddSound("step", 75, 45, .12f); AddSound("jump", 190, 600, .3f);
            AddSound("pickup", 380, 760, .18f); AddSound("drop", 420, 160, .22f);
            AddSound("point", 660, 990, .25f); AddSound("hit", 130, 65, .25f);
            const int sampleRate = 22050;
            var samples = new float[sampleRate];
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate, part = t * 4 % 1;
                samples[i] = Mathf.Sin(2 * Mathf.PI * notes[Mathf.Min(3, (int)(t * 4))] * t) * Mathf.Sin(Mathf.PI * part) * .3f;
            }
            var win = AudioClip.Create("Adventure finish", samples.Length, 1, sampleRate, false);
            win.SetData(samples, 0); clips["win"] = win; resources.Add(win);
        }
        void AddSound(string key, float from, float to, float seconds)
        {
            const int sampleRate = 22050;
            var samples = new float[(int)(sampleRate * seconds)];
            float phase = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)samples.Length;
                phase += 2 * Mathf.PI * Mathf.Lerp(from, to, t) / sampleRate;
                samples[i] = Mathf.Sin(phase) * Mathf.Sin(Mathf.PI * t) * .35f;
            }
            var clip = AudioClip.Create(key, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0); clips[key] = clip; resources.Add(clip);
        }
        void Play(string key) { if (sound && audioSource && clips.TryGetValue(key, out var clip)) audioSource.PlayOneShot(clip); }
        void OnDestroy()
        {
            if (course) Destroy(course.gameObject);
            if (skin) Destroy(skin);
            foreach (var resource in resources) if (resource) Destroy(resource);
        }
        void EnsureSkin()
        {
            if (skin) return;
            skin = Instantiate(GUI.skin);
            skin.label = new GUIStyle(skin.label) { fontSize = 16, wordWrap = true, normal = { textColor = new Color(.16f, .18f, .15f) } };
            skin.button = new GUIStyle(skin.button) { fontSize = 17, fontStyle = FontStyle.Bold, fixedHeight = 0,
                margin = new RectOffset(4, 4, 5, 5), padding = new RectOffset(10, 10, 12, 12) };
            var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, new Color(.85f, .32f, .055f)); texture.Apply(); resources.Add(texture);
            foreach (var state in new[] { skin.button.normal, skin.button.hover, skin.button.active, skin.button.focused,
                skin.button.onNormal, skin.button.onHover, skin.button.onActive, skin.button.onFocused })
            { state.background = texture; state.textColor = Color.white; }
            skin.box = new GUIStyle(skin.box) { normal = { background = Texture2D.whiteTexture }, padding = new RectOffset(20, 20, 18, 18) };
        }
        void Heading(string text, int size = 28) => GUILayout.Label(text, new GUIStyle(skin.label) { fontSize = size, fontStyle = FontStyle.Bold });
        void Panel(Rect rect, float opacity = .94f)
        {
            Color old = GUI.color; GUI.color = new Color(1, 1, 1, opacity); GUI.Box(rect, GUIContent.none); GUI.color = old;
        }
        int Choices(int selected, string[] labels)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
            {
                var old = GUI.backgroundColor;
                GUI.backgroundColor = i == selected ? Color.white : new Color(.6f, .6f, .6f);
                if (GUILayout.Button((i == selected ? "✓ " : "") + labels[i])) selected = i;
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal(); return selected;
        }
        void OnGUI()
        {
            if (!controller || !locomotion) return;
            EnsureSkin();
            var oldSkin = GUI.skin; var oldMatrix = GUI.matrix; var oldColor = GUI.color; int oldDepth = GUI.depth;
            // Pose Actions draws its guided-recording panel at depth -10; drop behind it while that panel is up.
            GUI.skin = skin; GUI.depth = pose.GuideActive ? 0 : -60; GUI.color = Color.white;
            float scale = Mathf.Min(Screen.width / 1200f, Screen.height / 760f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1200 * scale) / 2, (Screen.height - 760 * scale) / 2), Quaternion.identity, Vector3.one * scale);
            // Live numbers sit under the top HUD bar in play, and under the camera panel in the menu.
            var numbers = State == Phase.Playing ? new Vector2(18, 82) : new Vector2(18, cameraPanelBottom + 8);
            pose.numbersCorner = new Vector2((Screen.width - 1200 * scale) / 2 + numbers.x * scale, (Screen.height - 760 * scale) / 2 + numbers.y * scale);
            try
            {
                if (movementGuide && movementGuide.IsOpen) return;
                if (pose.GuideActive)
                {
                    DrawGuideCamera();
                    return;
                }
                if (cameraExpanded)
                {
                    DrawExpandedCamera();
                    return;
                }
                if (State != Phase.Playing) DrawCameraPanel();
                else if (gameplayCameraVisible) DrawGameplayCamera();
                if (State == Phase.Playing)
                {
                    DrawHud();
                }
                else
                {
                    Panel(new Rect(540, 96, 640, 644), .95f);
                    GUILayout.BeginArea(new Rect(562, 113, 596, State == Phase.Menu ? 485 : 607));
                    menuScroll = GUILayout.BeginScrollView(menuScroll);
                    if (State == Phase.Results)
                    {
                        Heading(result, 42); GUILayout.Space(16);
                        Heading("Score  " + Score + "     Health  " + Mathf.CeilToInt(Health), 26);
                        GUILayout.Label("Time  " + TimeSpan.FromSeconds(elapsed).ToString(@"mm\:ss"));
                        GUILayout.Label("Deliveries  " + activeCargo + " / " + cargo.Count + " resolved");
                        GUILayout.Space(22);
                        if (GUILayout.Button("PLAY AGAIN", GUILayout.Height(64))) BeginGame();
                        if (GUILayout.Button("Choose another game")) ReturnToMenu();
                    }
                    else DrawMenu();
                    GUILayout.EndScrollView(); GUILayout.EndArea();
                    if (State == Phase.Menu) DrawStartButton();
                }
                if (Time.unscaledTime < feedbackUntil)
                {
                    float feedbackY = State == Phase.Playing ? 82 : 22;
                    Panel(new Rect(540, feedbackY, 640, 55));
                    GUI.Label(new Rect(558, feedbackY + 13, 604, 38), feedback);
                }
            }
            finally { GUI.skin = oldSkin; GUI.matrix = oldMatrix; GUI.color = oldColor; GUI.depth = oldDepth; }
        }
        void DrawCameraPanel()
        {
            var image = preview ? preview.PreviewTexture : null;
            float aspect = image && image.height > 0 ? (float)image.width / image.height : 16f / 9f;
            float height = Mathf.Min(320, 476 / aspect);
            float width = height * aspect;
            cameraPanelBottom = 146 + height + 82;
            Panel(new Rect(18, 96, 500, cameraPanelBottom - 96));
            GUI.Label(new Rect(34, 110, 230, 26), "CAMERA · FULL FRAME");
            if (GUI.Button(new Rect(364, 104, 140, 36), "Enlarge")) cameraExpanded = true;
            if (preview) preview.DrawImage(new Rect(30 + (476 - width) / 2, 146, width, height));
            GUI.Label(new Rect(32, 152 + height, 472, 64), preview ? preview.Status : "Camera unavailable");
        }
        void DrawGameplayCamera()
        {
            // Optional corner thumbnail; setup/calibration retains the large full-frame preview.
            var texture = preview ? preview.PreviewTexture : null;
            float aspect = texture && texture.height > 0 ? (float)texture.width / texture.height : 16f / 9f;
            float height = Mathf.Min(150, 220 / aspect), width = height * aspect;
            float top = 674 - height;
            Panel(new Rect(944, top - 42, 236, height + 50), .85f);
            var compact = new GUIStyle(skin.button) { fontSize = 16, padding = new RectOffset(4, 4, 2, 2) };
            if (GUI.Button(new Rect(952, top - 36, 130, 30), "Enlarge", compact)) cameraExpanded = true;
            if (GUI.Button(new Rect(1088, top - 36, 84, 30), "Hide", compact)) gameplayCameraVisible = false;
            if (preview) preview.DrawImage(new Rect(952 + (220 - width) / 2, top, width, height));
        }
        void DrawGuideCamera()
        {
            // Full camera view behind the translucent guided-recording panel, so everyone can see their zone.
            Panel(new Rect(12, 12, 1176, 736), 1);
            if (preview) preview.DrawImage(new Rect(32, 32, 1136, 660));
            GUI.Label(new Rect(32, 700, 1136, 38), "GUIDED RECORDING · " + (preview ? preview.Status : "Camera unavailable"));
        }
        void DrawExpandedCamera()
        {
            Panel(new Rect(12, 12, 1176, 736), 1);
            GUI.Label(new Rect(32, 26, 840, 32), "FULL CAMERA VIEW · original proportions · no crop");
            if (GUI.Button(new Rect(996, 24, 170, 42), "Back / Esc")) cameraExpanded = false;
            if (preview) preview.DrawImage(new Rect(32, 82, 1136, 552));
            GUI.Label(new Rect(32, 644, 1136, 40), preview ? preview.Status : "Camera unavailable");
            GUI.Label(new Rect(32, 690, 1136, 38), State == Phase.Playing ? "Game paused while the camera is enlarged."
                : controller.CalibrationPending ? "Calibration in progress. Hold your resting pose."
                : "For a closer setup, choose Seated / close-up in the menu, then calibrate.");
        }
        void DrawMenu()
        {
            Heading("TRUNK TRAIL", 42); GUILayout.Label("ADVENTURE  /  Team Paladin"); GUILayout.Space(10);
            Heading("Choose your challenge", 20);
            var nextGame = (Game)Choices((int)game, new[] { "Rock run", "Delivery", "Expedition" });
            var nextDifficulty = (Difficulty)Choices((int)difficulty, new[] { "Easy", "Normal", "Hard" });
            if (nextGame != game || nextDifficulty != difficulty)
            { game = nextGame; difficulty = nextDifficulty; BuildCourse(); }
            GUILayout.Label(game == Game.RockRun ? "Jump rocks: +1 clear, −1 hit. Reach the finish."
                : game == Game.Delivery ? "Carry three parcels to their green circles. +1 per delivery before its timer expires."
                : "Combine jumps and three timed deliveries on one trail.");
            GUILayout.Label(Mathf.RoundToInt(length) + " m trail · " + DeliverySeconds + " s per delivery · Off-road health loss after " + Grace + " s");
            if (trail)
            {
                bool bonus = Choices(trail.optionalLevel ? 1 : 0, new[] { "Main trail", "Open bonus route" }) == 1;
                if (bonus != trail.optionalLevel) { trail.optionalLevel = bonus; BuildCourse(); }
                GUILayout.Label("Follow the loop clockwise, through the tunnel, to the finish arch.");
            }
            GUILayout.Space(8);
            int mode = Choices(controller.inputMode == InputMode.Camera ? 1 : 0, new[] { "Keyboard practice", "Body + camera" });
            if ((mode == 1) != (controller.inputMode == InputMode.Camera)) controller.SetInputMode(mode == 1 ? InputMode.Camera : InputMode.TestSliders);
            if (controller.inputMode == InputMode.Camera)
            {
                int people = Choices(controller.performerCount - 1, new[] { "Solo", "2 people", "3 people", "4 people" }) + 1;
                if (people != controller.performerCount) SetRoles(people);
                bool seated = Choices(controller.upperBodyOnly ? 1 : 0, new[] { "Full body", "Seated / close-up" }) == 1;
                if (seated != controller.upperBodyOnly) controller.SetUpperBodyOnly(seated);
                GUILayout.Label(seated ? "Stay closer: keep shoulders and both hands visible. Feet are not required."
                    : "Need more room? Choose Seated / close-up to play without showing your legs.");
                useHands = Choices(useHands ? 1 : 0, new[] { "Pose only", "Easy pose + hands" }) == 1;
                if (useHands)
                {
                    thumbSteering = Choices(thumbSteering ? 1 : 0, new[] { "Lean to steer", "Thumb steering" }) == 1;
                    GUILayout.Label("Hold a fist briefly to grab/drop. Open your hand before the next action.");
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(controller.CalibrationPending ? "Cancel calibration" : "CALIBRATE · 10 seconds"))
                {
                    if (controller.CalibrationPending) controller.CancelCalibrationCountdown();
                    else controller.BeginCalibrationCountdown(Time.realtimeSinceStartup);
                }
                if (GUILayout.Button("Retry camera") && preview) preview.BeginPreview();
                GUILayout.EndHorizontal();
                GUILayout.Label(controller.Status);
                if (preview)
                {
                    GUILayout.Label("Pose model experiment (switching restarts the camera)");
                    int model = Choices((int)preview.poseModel, new[] { "Full", "Lite", "Heavy" });
                    if (model != (int)preview.poseModel)
                    {
                        preview.poseModel = (DigiPhantCameraPreview.PoseModel)model;
                        controller.InvalidateCalibration(); preview.BeginPreview();
                        Say("Loading model. First use downloads the official Google model; then recalibrate.");
                    }
                }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Movement guide")) OpenMovementGuide();
            if (GUILayout.Button(sound ? "Sound: on" : "Sound: off")) sound = !sound;
            GUILayout.EndHorizontal();
            if (controller.inputMode == InputMode.Camera)
            {
                GUILayout.Label("Pose tools: guided recording recalibrates, then saves one signal file per pose in Recordings.");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Guided recording")) pose.BeginGuidedRecording();
                if (GUILayout.Button(pose.showNumbers ? "Hide numbers" : "Show numbers")) pose.showNumbers = !pose.showNumbers;
                if (GUILayout.Button(pose.Logging ? "Stop signal log" : "Log signals")) pose.ToggleSignalLog();
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Advanced control setup remains in DigiPhant_Student.");
        }
        void DrawStartButton()
        {
            bool ready = controller.inputMode != InputMode.Camera || (controller.IsCalibrated && !controller.CalibrationPending);
            string message = ready ? "Ready to start."
                : controller.CalibrationPending ? "Calibration is counting down. Hold your resting pose until it finishes."
                : "Start is locked until calibration succeeds. Choose CALIBRATE above. For a closer setup, use Seated / close-up.";
            GUI.Label(new Rect(562, 614, 596, 54), message);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && ready;
            if (GUI.Button(new Rect(562, 676, 596, 52), ready ? "START ADVENTURE"
                : controller.CalibrationPending ? "CALIBRATING…" : "CALIBRATE TO START")) BeginGame();
            GUI.enabled = enabled;
        }
        void DrawHud()
        {
            Panel(new Rect(18, 18, 1164, 52), .88f);
            GUI.Label(new Rect(34, 28, 1128, 36), "SCORE  " + Score + "     HP  " + Mathf.CeilToInt(Health) + " / 100     "
                + TimeSpan.FromSeconds(elapsed).ToString(@"mm\:ss") + "     " + (trail ? Mathf.RoundToInt(95f * nextTrailGate / Mathf.Max(1, trailGates.Count)) : Mathf.Clamp(Mathf.FloorToInt(Local(locomotion.travelRoot.position).z / length * 100), 0, 100)) + "%");
            if (Time.unscaledTime < damageFlash)
            {
                Color old = GUI.color; GUI.color = new Color(1, .1f, .1f, .3f);
                GUI.DrawTexture(new Rect(18, 18, 1164, 52), Texture2D.whiteTexture); GUI.color = old;
            }
            string task = activeCargo < cargo.Count ? "Parcel " + (activeCargo + 1) + " → green circle " + (activeCargo + 1) : "Follow the trail to the finish arch";
            if (activeCargo < cargo.Count && cargo[activeCargo].deadline >= 0)
                task += "   " + Mathf.CeilToInt(Mathf.Max(0, cargo[activeCargo].deadline - elapsed)) + " s left";
            if (outsideSeconds > 0) task = "Return to the marked road!";
            if (locomotion.movementSuspended) task = "Tracking paused · keep all players in view, or use Menu to calibrate.";
            Panel(new Rect(18, 632, 900, 50), .82f);
            GUI.Label(new Rect(34, 644, 864, 34), task);
            Panel(new Rect(18, 694, 1164, 48), .88f);
            if (GUI.Button(new Rect(28, 701, 175, 34), gameplayCameraVisible ? "Hide camera" : "Show camera")) gameplayCameraVisible = !gameplayCameraVisible;
            if (GUI.Button(new Rect(213, 701, 175, 34), "Movement guide")) OpenMovementGuide();
            if (GUI.Button(new Rect(398, 701, 125, 34), sound ? "Sound: on" : "Sound: off")) sound = !sound;
            if (GUI.Button(new Rect(1008, 701, 164, 34), "Menu / restart")) ReturnToMenu();
            bool bodyMode = controller.inputMode == InputMode.Camera;
            if (bodyMode && GUI.Button(new Rect(533, 701, 150, 34), pose.showNumbers ? "Hide numbers" : "Show numbers")) pose.showNumbers = !pose.showNumbers;
            GUI.Label(bodyMode ? new Rect(693, 704, 305, 30) : new Rect(538, 704, 460, 30), bodyMode
                ? (useHands ? (thumbSteering ? "Thumbs: steer · Fist: grab/drop" : "Lean: steer · Fist: grab/drop") : "Lean: steer · Squat: grab/drop")
                : "WASD · Space: jump · E: grab/drop");
        }
        void OpenMovementGuide()
        {
            if (!movementGuide) return;
            movementGuide.OpenGuide();
            Suspend(true);
        }
    }
}
