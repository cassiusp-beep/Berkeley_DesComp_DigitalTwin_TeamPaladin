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
        Material road, grass, hurdleBlue, hurdleRed, hurdleWhite, orange, green, cream;
        Material[] giftWrap, giftRibbon;
        Material halloweenDark, halloweenBone, pumpkinOrange;
        Texture2D heartIcon, scoreIcon;
        Transform stopSign;
        Renderer stopPositiveText, stopNegativeText;
        struct Rock { public Transform transform; public Vector3 position, forward; public TrunkCheckpoint checkpoint; public int reward; public bool resolved; }
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
            hurdleBlue = Material(new Color(.38f, .12f, .57f)); // Plum Halloween uprights
            hurdleRed = Material(new Color(.94f, .08f, .16f));
            hurdleWhite = Material(new Color(1f, .55f, .12f)); // Pumpkin-orange stripes
            orange = Material(new Color(1, .47f, .10f));
            green = Material(new Color(.2f, .75f, .4f));
            cream = Material(new Color(1, .88f, .59f));
            halloweenDark = Material(new Color(.075f, .055f, .10f));
            halloweenBone = Material(new Color(.85f, .83f, .71f));
            pumpkinOrange = Material(new Color(.95f, .30f, .045f));
            giftWrap = new[] { pumpkinOrange, hurdleBlue, Material(new Color(.35f, .57f, .16f)) };
            giftRibbon = new[] { halloweenDark, hurdleWhite, hurdleBlue };
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
        Transform GiftDetail(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(part.GetComponent<Collider>());
            return part.transform;
        }
        Transform GiftBox(int index, Vector3 position)
        {
            var wrap = giftWrap[index % giftWrap.Length];
            var ribbon = giftRibbon[index % giftRibbon.Length];
            // Keep the original body bounds/pivot for pickup range and terrain settling.
            var box = Shape("Gift " + (index + 1), PrimitiveType.Cube, position, Vector3.one * .9f, wrap);
            GiftDetail(box, "Overhanging lid", PrimitiveType.Cube, new Vector3(0, .48f, 0), new Vector3(1.08f, .16f, 1.08f), wrap);
            GiftDetail(box, "Ribbon around front", PrimitiveType.Cube, Vector3.zero, new Vector3(.18f, 1.02f, 1.015f), ribbon);
            GiftDetail(box, "Ribbon around sides", PrimitiveType.Cube, Vector3.zero, new Vector3(1.015f, 1.02f, .18f), ribbon);
            GiftDetail(box, "Lid ribbon across", PrimitiveType.Cube, new Vector3(0, .57f, 0), new Vector3(1.09f, .025f, .18f), ribbon);
            GiftDetail(box, "Lid ribbon along", PrimitiveType.Cube, new Vector3(0, .57f, 0), new Vector3(.18f, .025f, 1.09f), ribbon);
            for (int side = -1; side <= 1; side += 2)
            {
                var loop = GiftDetail(box, "Bow loop", PrimitiveType.Sphere, new Vector3(side * .22f, .70f, 0), new Vector3(.5f, .17f, .27f), ribbon);
                loop.localRotation = Quaternion.Euler(0, 0, side * 24);
                var tail = GiftDetail(box, "Ribbon tail", PrimitiveType.Cube, new Vector3(side * .18f, .60f, .23f), new Vector3(.15f, .03f, .44f), ribbon);
                tail.localRotation = Quaternion.Euler(0, side * 25, 0);
            }
            GiftDetail(box, "Bow knot", PrimitiveType.Sphere, new Vector3(0, .70f, 0), Vector3.one * .22f, ribbon);
            var tag = GiftDetail(box, "Gift tag", PrimitiveType.Cube, new Vector3(.28f, .17f, -.515f), new Vector3(.28f, .32f, .025f), cream);
            var text = new GameObject("Gift number"); text.transform.SetParent(tag, false);
            text.transform.localPosition = new Vector3(0, 0, -.6f); text.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var label = text.AddComponent<TextMesh>(); label.text = (index + 1).ToString(); label.anchor = TextAnchor.MiddleCenter;
            label.fontSize = 48; label.characterSize = .55f; label.color = new Color(.12f, .09f, .15f);
            return box;
        }
        Transform RunningHurdle(Vector3 position, Vector3 forward, int number, bool bonus = false)
        {
            var accent = bonus ? hurdleRed : hurdleBlue;
            var root = new GameObject((bonus ? "Bonus hurdle +3 " : "Track hurdle ") + number).transform;
            root.SetParent(course, false);
            root.SetPositionAndRotation(World(position), initialRotation * Quaternion.LookRotation(forward));
            float span = halfWidth * 1.5f + 1f;
            float Ground(float x)
            {
                var world = root.TransformPoint(x, 0, 0);
                return trail && groundFollow && groundFollow.terrain
                    ? WalkableSurface.Raise(world.x, world.z, groundFollow.terrain.SampleHeight(world) + groundFollow.terrain.transform.position.y) - root.position.y : 0;
            }
            float left = Ground(-span / 2), right = Ground(span / 2);
            float barHeight = Mathf.Max(left, right) + .60f;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * span / 2, ground = side < 0 ? left : right;
                GiftDetail(root, "Stabilizer foot", PrimitiveType.Cube, new Vector3(x, ground + .055f, .25f), new Vector3(.32f, .11f, 1.1f), accent);
                GiftDetail(root, "Upright", PrimitiveType.Cube, new Vector3(x, (ground + barHeight) / 2, 0), new Vector3(.15f, barHeight - ground, .16f), accent);
                GiftDetail(root, "White end cap", PrimitiveType.Cube, new Vector3(x, ground + .06f, .78f), new Vector3(.33f, .12f, .12f), hurdleWhite);
            }
            GiftDetail(root, "Crossbar", PrimitiveType.Cube, new Vector3(0, barHeight, 0), new Vector3(span + .25f, .18f, .16f), hurdleWhite);
            for (int i = 0; i < 9; i += 2)
                GiftDetail(root, "Crossbar stripe", PrimitiveType.Cube,
                    new Vector3((i - 4) * span / 9, barHeight, 0), new Vector3(span / 9, .185f, .17f), accent);
            return root;
        }
        Transform GiftDestination(int index, Vector3 position)
        {
            var target = Shape("Delivery zone " + (index + 1), PrimitiveType.Cylinder, position + Vector3.up * .07f, new Vector3(4, .05f, 4), green);
            // A matching colored border links each present with its numbered green landing pad.
            for (int n = 0; n < 24; n++)
            {
                float angle = n * Mathf.PI * 2 / 24;
                var p = position + new Vector3(Mathf.Sin(angle) * 1.94f, .13f, Mathf.Cos(angle) * 1.94f);
                if (trail && groundFollow && groundFollow.terrain)
                {
                    var w = World(p); w.y = groundFollow.terrain.SampleHeight(w) + groundFollow.terrain.transform.position.y + .13f; p = Local(w);
                }
                var rim = Shape("Gift zone border", PrimitiveType.Cube, p, new Vector3(.48f, .12f, .13f), giftWrap[index % giftWrap.Length]);
                rim.rotation = initialRotation * Quaternion.Euler(0, n * 15, 0);
            }
            return target;
        }
        void BuildCourse()
        {
            if (course) { course.gameObject.SetActive(false); Destroy(course.gameObject); }
            course = new GameObject("Adventure course (generated)").transform;
            trunk.AssignProp(null);
            route.Clear(); rocks.Clear(); cargo.Clear(); activeCargo = 0;
            if (trail) { BuildTrailTasks(); BuildHalloweenDecorations(); return; }
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
                    var t = RunningHurdle(p, Vector3.forward, i + 1);
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
                    var item = GiftBox(i, p);
                    var targetPoint = Path(z + 12);
                    var target = GiftDestination(i, targetPoint);
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
            BuildHalloweenDecorations();
        }
        void BuildHalloweenDecorations()
        {
            // Small, deterministic accents. Clearance includes BOTH routes even when the bonus is closed.
            for (int i = 0; i < 14; i++)
            {
                Vector3 center, forward;
                if (trail)
                {
                    var point = trailLayout.loop.At((i + .5f) / 14 * trailLayout.loopLength, out var tangent);
                    center = new Vector3(point.x, 0, point.y);
                    forward = new Vector3(tangent.x, 0, tangent.y);
                }
                else { center = Path((i + .5f) / 14 * length); forward = Vector3.forward; }
                Vector3 side = new Vector3(forward.z, 0, -forward.x) * (i % 2 == 0 ? 1 : -1);
                Vector3 location = Vector3.zero;
                bool placed = false;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    location = center + side * (halfWidth + 4.5f + attempt * 1.5f);
                    if (trail && trailLayout.DistanceToPaths(new Vector2(location.x, location.z)) < 6.5f) continue;
                    var world = World(location);
                    if (trail && groundFollow && groundFollow.terrain)
                    {
                        var terrain = groundFollow.terrain;
                        var relative = world - terrain.transform.position;
                        var size = terrain.terrainData.size;
                        if (relative.x < 2 || relative.z < 2 || relative.x > size.x - 2 || relative.z > size.z - 2) continue;
                        world.y = terrain.SampleHeight(world) + terrain.transform.position.y;
                        location = Local(world);
                    }
                    placed = true; break;
                }
                if (!placed) continue;
                var decoration = new GameObject("Halloween scenery " + (i + 1)).transform;
                decoration.SetParent(course, false);
                decoration.SetPositionAndRotation(World(location), initialRotation * Quaternion.LookRotation(-side));
                Transform Part(string name, PrimitiveType type, Vector3 at, Vector3 size, Material material) =>
                    GiftDetail(decoration, name, type, at, size, material);
                if (i % 7 == 5)
                {
                    // Friendly, small skeleton: skull, ribs, and simple jointed limbs.
                    Part("Skull", PrimitiveType.Sphere, new Vector3(0, 1.45f, 0), new Vector3(.38f, .43f, .32f), halloweenBone);
                    for (int eye = -1; eye <= 1; eye += 2)
                        Part("Eye socket", PrimitiveType.Sphere, new Vector3(eye * .09f, 1.48f, .145f), new Vector3(.085f, .10f, .045f), halloweenDark);
                    void Bone(Vector3 a, Vector3 b)
                    {
                        var bone = Part("Bone", PrimitiveType.Cylinder, (a + b) / 2, new Vector3(.065f, (b - a).magnitude / 2, .065f), halloweenBone);
                        bone.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                    }
                    Bone(new Vector3(0, .64f, 0), new Vector3(0, 1.23f, 0));
                    for (int rib = 0; rib < 3; rib++)
                        Bone(new Vector3(-.18f, 1.15f - rib * .12f, 0), new Vector3(.18f, 1.15f - rib * .12f, 0));
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Bone(new Vector3(0, .67f, 0), new Vector3(sign * .15f, .36f, 0));
                        Bone(new Vector3(sign * .15f, .36f, 0), new Vector3(sign * .2f, .05f, .06f));
                        Bone(new Vector3(sign * .18f, 1.17f, 0), new Vector3(sign * .3f, .93f, 0));
                        Bone(new Vector3(sign * .3f, .93f, 0), new Vector3(sign * .36f, .73f, .04f));
                    }
                }
                else if (i % 5 == 3)
                {
                    Part("Ghost sheet", PrimitiveType.Sphere, new Vector3(0, 1, 0), new Vector3(.75f, 1.1f, .6f), halloweenBone);
                    for (int hem = -1; hem <= 1; hem++)
                        Part("Sheet hem", PrimitiveType.Sphere, new Vector3(hem * .22f, .59f, 0), new Vector3(.3f, .4f, .45f), halloweenBone);
                    for (int eye = -1; eye <= 1; eye += 2)
                        Part("Ghost eye", PrimitiveType.Sphere, new Vector3(eye * .13f, 1.14f, .28f), new Vector3(.10f, .15f, .04f), halloweenDark);
                    Part("Ghost mouth", PrimitiveType.Sphere, new Vector3(0, .94f, .30f), new Vector3(.10f, .12f, .04f), halloweenDark);
                }
                else
                {
                    for (int rib = 0; rib < 8; rib++)
                    {
                        float angle = rib * Mathf.PI / 4;
                        Part("Pumpkin lobe", PrimitiveType.Sphere, new Vector3(Mathf.Sin(angle) * .19f, .31f, Mathf.Cos(angle) * .19f), new Vector3(.46f, .6f, .46f), pumpkinOrange);
                    }
                    Part("Pumpkin stem", PrimitiveType.Cylinder, new Vector3(0, .66f, 0), new Vector3(.10f, .09f, .10f), halloweenDark);
                    for (int eye = -1; eye <= 1; eye += 2)
                        Part("Carved eye", PrimitiveType.Cube, new Vector3(eye * .13f, .38f, .39f), new Vector3(.10f, .095f, .025f), halloweenDark);
                    Part("Carved smile", PrimitiveType.Cube, new Vector3(0, .23f, .405f), new Vector3(.24f, .065f, .025f), halloweenDark);
                }
            }
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
        void EnsureClosedTrackSign()
        {
            if (!trail.closedRoot) return;
            var barrier = trail.closedRoot.transform.Find("Barrier");
            if (!barrier || barrier.Find("Bonus track STOP sign")) return;
            // Unlit, non-HDR colors keep the sign flat under the map's bright sun and bloom.
            Material SignMaterial(Color color)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (!shader) shader = Shader.Find("Unlit/Color");
                var material = new Material(shader);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                resources.Add(material);
                return material;
            }
            var signRed = SignMaterial(new Color(.72f, .025f, .045f));
            var signWhite = SignMaterial(new Color(.65f, .65f, .65f));
            // Parenting to the existing closure makes the sign follow the bonus-route toggle.
            var sign = new GameObject("Bonus track STOP sign").transform;
            sign.SetParent(barrier, false);
            stopSign = sign;
            sign.localPosition = new Vector3(0, 2.5f, 0);
            // End the support at the octagon's bottom edge, never inside the sign face.
            float postTop = -.86f * Mathf.Cos(22.5f * Mathf.Deg2Rad);
            const float postBottom = -1.8f;
            GiftDetail(sign, "Sign post", PrimitiveType.Cube,
                new Vector3(0, (postTop + postBottom) / 2, 0),
                new Vector3(.1f, postTop - postBottom, .1f), signWhite);
            var mesh = new Mesh { name = "Stop sign octagon" };
            var vertices = new Vector3[9];
            var triangles = new int[48];
            for (int i = 0; i < 8; i++)
            {
                float angle = (22.5f + i * 45) * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                int next = (i + 1) % 8 + 1, t = i * 6;
                triangles[t] = 0; triangles[t + 1] = i + 1; triangles[t + 2] = next;
                triangles[t + 3] = 0; triangles[t + 4] = next; triangles[t + 5] = i + 1;
            }
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            resources.Add(mesh);
            void Face(string name, float radius, float z, Material material)
            {
                var go = new GameObject(name); go.transform.SetParent(sign, false);
                go.transform.localPosition = new Vector3(0, 0, z); go.transform.localScale = Vector3.one * radius;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            Face("White octagonal border", .86f, 0, signWhite);
            foreach (int side in new[] { -1, 1 })
            {
                Face("Red sign face", .77f, side * .015f, signRed);
                var text = new GameObject("STOP"); text.transform.SetParent(sign, false);
                text.transform.localPosition = new Vector3(0, 0, side * .025f);
                text.transform.localRotation = Quaternion.Euler(0, side > 0 ? 180 : 0, 0);
                var label = text.AddComponent<TextMesh>(); label.text = "STOP";
                label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
                label.fontSize = 64; label.characterSize = .055f; label.fontStyle = FontStyle.Bold; label.color = new Color(.75f, .75f, .75f);
                var textRenderer = text.GetComponent<Renderer>();
                if (side > 0) stopPositiveText = textRenderer; else stopNegativeText = textRenderer;
                textRenderer.enabled = false;
            }
        }
        void UpdateStopSignLettering()
        {
            if (!stopSign || !stopPositiveText || !stopNegativeText) return;
            var camera = locomotion.followCamera ? locomotion.followCamera : Camera.main;
            if (!camera) return;
            // TextMesh's font material is double-sided. Hide the far label explicitly,
            // rather than relying on the sign face to occlude transparent font rendering.
            float side = Vector3.Dot(camera.transform.position - stopSign.position, stopSign.forward);
            stopPositiveText.enabled = side > .01f;
            stopNegativeText.enabled = side < -.01f;
        }
        void BuildTrailTasks()
        {
            EnsureClosedTrackSign();
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
                var oldLog = checkpoint.transform.Find("Hurdle log");
                if (oldLog) oldLog.gameObject.SetActive(false);
                var p = Local(checkpoint.transform.position);
                var forward = Quaternion.Inverse(initialRotation) * checkpoint.transform.forward;
                var hurdle = RunningHurdle(p, forward, rocks.Count + 1);
                rocks.Add(new Rock { transform = hurdle, position = p, forward = forward, checkpoint = checkpoint });
            }
            // Additional track hurdles on open stretches, with the same scoring as the map checkpoints.
            if (game != Game.Delivery)
                for (int i = 0; i < (int)difficulty * 2; i++)
                {
                    float d = (.55f + i * .10f) * trailLayout.loopLength;
                    var p = TrailPoint(trailLayout.loop, d);
                    trailLayout.loop.At(d, out var direction);
                    var forward = new Vector3(direction.x, 0, direction.y);
                    var t = RunningHurdle(p, forward, rocks.Count + 1);
                    rocks.Add(new Rock { transform = t, position = p, forward = forward });
                }
            // Bonus challenges use the map's actual checkpoint positions and orientations.
            foreach (var checkpoint in trail.bonusCheckpoints)
            {
                if (!checkpoint) continue;
                checkpoint.adventureManagedJump = true;
                // Old checkpoint posts no longer describe the Adventure bonus challenge.
                if (checkpoint.markers != null)
                    foreach (var marker in checkpoint.markers)
                        if (marker) marker.gameObject.SetActive(false);
                // Remove the old bridge and its raised ground so the elephant follows the terrain.
                if (checkpoint.mode == CheckpointMode.Beam)
                {
                    var bridge = checkpoint.transform.Find("Beam log");
                    if (bridge) bridge.gameObject.SetActive(false);
                    foreach (var surface in checkpoint.GetComponentsInChildren<WalkableSurface>(true))
                        surface.enabled = false;
                    // The former bridge sits on a steep ridge: its replacement hurdle was
                    // partly buried. Remove that remnant, leaving the usable red lane hurdle.
                    continue;
                }
                // Replace moving logs with a jump.
                if (checkpoint.rollingTrunks != null)
                    foreach (var log in checkpoint.rollingTrunks) if (log) log.gameObject.SetActive(false);
                if (!trail.optionalLevel || game == Game.Delivery) continue;
                var p = Local(checkpoint.transform.position);
                var forward = Quaternion.Inverse(initialRotation) * checkpoint.transform.forward;
                var hurdle = RunningHurdle(p, forward, rocks.Count + 1, true);
                rocks.Add(new Rock { transform = hurdle, position = p, forward = forward, checkpoint = checkpoint, reward = 3 });
                LabelWorld("BONUS +3", p + Vector3.up * 2);
            }
            if (game != Game.RockRun)
                for (int i = 0; i < 3; i++)
                {
                    float d = (.24f + i * .27f) * trailLayout.loopLength;
                    var p = TrailPoint(trailLayout.loop, d);
                    var targetPoint = TrailPoint(trailLayout.loop, d + 10);
                    var item = GiftBox(i, p + Vector3.up * .45f);
                    var target = GiftDestination(i, targetPoint);
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
            Say(game == Game.RockRun ? "Clear the hurdles and reach the finish!" : "Deliver parcels to their numbered green circles.");
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
        // Locomotion writes transforms directly, so ordinary wall colliders cannot stop it.
        // Sweep the elephant's horizontal footprint against the two tunnel walls each frame.
        static float WallEntry(Vector2 from, Vector2 to, Rect wall)
        {
            var delta = to - from;
            if (wall.Contains(from))
            {
                // Allow escape if a run/scene starts with the footprint already overlapping.
                float Depth(Vector2 p) => Mathf.Min(p.x - wall.xMin, wall.xMax - p.x, p.y - wall.yMin, wall.yMax - p.y);
                return !wall.Contains(to) || Depth(to) < Depth(from) ? 1 : 0;
            }
            float enter = 0, leave = 1;
            for (int axis = 0; axis < 2; axis++)
            {
                float a = axis == 0 ? from.x : from.y, d = axis == 0 ? delta.x : delta.y;
                float min = axis == 0 ? wall.xMin : wall.yMin, max = axis == 0 ? wall.xMax : wall.yMax;
                if (Mathf.Abs(d) < .00001f) { if (a < min || a > max) return 1; continue; }
                float first = (min - a) / d, last = (max - a) / d;
                if (first > last) { float swap = first; first = last; last = swap; }
                enter = Mathf.Max(enter, first); leave = Mathf.Min(leave, last);
                if (enter > leave) return 1;
            }
            return Mathf.Clamp01(enter);
        }
        void StopAtCourseWalls()
        {
            if (!trail) return;
            Vector3 destination = locomotion.travelRoot.position;
            float fraction = 1;
            const float bodyRadius = .65f;
            foreach (var checkpoint in trail.mainCheckpoints)
            {
                if (!checkpoint || !checkpoint.gameObject.activeInHierarchy || checkpoint.mode != CheckpointMode.Tunnel) continue;
                var a = checkpoint.transform.InverseTransformPoint(previousPosition);
                var b = checkpoint.transform.InverseTransformPoint(destination);
                // Dimensions match TrunkTrailBuilder.Tunnel: centers ±2.25, diameter 1.1, length 8.
                for (int side = -1; side <= 1; side += 2)
                {
                    var wall = new Rect(side * 2.25f - .55f - bodyRadius, -4 - bodyRadius,
                        1.1f + 2 * bodyRadius, 8 + 2 * bodyRadius);
                    fraction = Mathf.Min(fraction, WallEntry(new Vector2(a.x, a.z), new Vector2(b.x, b.z), wall));
                }
            }
            if (fraction >= 1) return;
            float distance = FlatDistance(previousPosition, destination);
            var stopped = Vector3.Lerp(previousPosition, destination, Mathf.Max(0, fraction - .02f / Mathf.Max(distance, .001f)));
            if (groundFollow && groundFollow.terrain)
                stopped.y = WalkableSurface.Raise(stopped.x, stopped.z,
                    groundFollow.terrain.SampleHeight(stopped) + groundFollow.terrain.transform.position.y);
            locomotion.travelRoot.position = stopped;
            if (trunk.Carrying && trunk.carryProp && trunk.trunkTip)
                trunk.carryProp.position = trunk.trunkTip.position + trunk.carryOffset;
            locomotion.StopMotion();
        }
        void LateUpdate()
        {
            if (State == Phase.Playing && !locomotion.movementSuspended) StopAtCourseWalls();
            UpdateCamera();
            UpdateStopSignLettering();
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
                    // The visible path and elephant footprint are wider than the narrow terrain ridge.
                    float width = Mathf.Max(halfWidth, Mathf.Lerp(trailLayout.branch.half[i - 1], trailLayout.branch.half[i], t));
                    distance = Mathf.Min(distance, Vector3.Distance(p, a + d * t) + halfWidth - width);
                }
                // Include the full marked challenge lanes, rather than only their centerline.
                foreach (var checkpoint in trail.bonusCheckpoints)
                {
                    if (!checkpoint) continue;
                    var local = checkpoint.transform.InverseTransformPoint(World(p));
                    float x = Mathf.Max(0, Mathf.Abs(local.x) - checkpoint.zoneSize.x / 2);
                    float z = Mathf.Max(0, Mathf.Abs(local.z) - checkpoint.zoneSize.y / 2);
                    distance = Mathf.Min(distance, halfWidth + Mathf.Sqrt(x * x + z * z));
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
                int reward = rock.reward > 0 ? rock.reward : 1;
                int penalty = reward == 3 ? 0 : -1;
                Score += clear ? reward : penalty;
                rock.resolved = true; rocks[i] = rock;
                var renderer = rock.transform.Find("Crossbar").GetComponent<Renderer>();
                if (renderer) renderer.sharedMaterial = clear ? green : orange;
                var checkpoint = rock.checkpoint;
                if (checkpoint) checkpoint.ResolveAdventureJump(clear);
                Say(clear ? "Jump cleared · +" + reward : penalty == 0 ? "Bonus hurdle hit · no points" : "Obstacle hit · −1"); Play(clear ? "point" : "hit");
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
            var nextGame = (Game)Choices((int)game, new[] { "Hurdle run", "Delivery", "Expedition" });
            var nextDifficulty = (Difficulty)Choices((int)difficulty, new[] { "Easy", "Normal", "Hard" });
            if (nextGame != game || nextDifficulty != difficulty)
            { game = nextGame; difficulty = nextDifficulty; BuildCourse(); }
            GUILayout.Label(game == Game.RockRun ? "Jump hurdles: +1 clear, −1 hit. Reach the finish."
                : game == Game.Delivery ? "Carry three parcels to their green circles. +1 per delivery before its timer expires."
                : "Combine jumps and three timed deliveries on one trail.");
            GUILayout.Label(Mathf.RoundToInt(length) + " m trail · " + DeliverySeconds + " s per delivery · Off-road health loss after " + Grace + " s");
            if (trail)
            {
                bool bonus = Choices(trail.optionalLevel ? 1 : 0, new[] { "Main trail", "Bonus hurdles · +3" }) == 1;
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
        void DrawHealthBar(Rect rect)
        {
            var old = GUI.color;
            void Fill(Rect r, Color color) { GUI.color = color; GUI.DrawTexture(r, Texture2D.whiteTexture); }
            Fill(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), new Color(.08f, .1f, .09f));
            Fill(rect, new Color(.18f, .22f, .20f));
            Color[] segments = { new Color(.68f, .88f, .76f), new Color(.43f, .79f, .60f), new Color(.20f, .72f, .46f), new Color(.02f, .64f, .34f), new Color(0, .49f, .25f) };
            float remaining = Mathf.Clamp01(Health / 100) * rect.width;
            for (int i = 0; i < 5; i++)
            {
                float start = i * rect.width / 5, width = Mathf.Clamp(remaining - start, 0, rect.width / 5);
                if (width > 0) Fill(new Rect(rect.x + start, rect.y, width, rect.height), segments[i]);
            }
            if (!heartIcon)
            {
                heartIcon = new Texture2D(64, 64, TextureFormat.RGBA32, false); resources.Add(heartIcon);
                // Original procedural heart silhouette; no artwork or watermark copied from the reference.
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float a = (x - 31.5f) / 27f, b = (y - 30f) / 27f;
                    float q = a * a + b * b - 1;
                    bool inside = q * q * q - a * a * b * b * b <= 0;
                    heartIcon.SetPixel(x, y, inside ? new Color(.88f, .10f, .24f) : Color.clear);
                }
                heartIcon.Apply();
            }
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(rect.x, rect.y + 25, 21, 20), heartIcon);
            GUI.Label(new Rect(rect.x + 27, rect.y + 25, 160, 22), Mathf.CeilToInt(Health) + "/100", new GUIStyle(skin.label) { fontSize = 17, fontStyle = FontStyle.Bold });
            GUI.color = old;
        }
        void DrawScorePanel(Rect rect)
        {
            var old = GUI.color;
            GUI.color = new Color(.08f, .1f, .09f);
            GUI.DrawTexture(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), Texture2D.whiteTexture);
            GUI.color = new Color(1, .94f, .72f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            if (!scoreIcon)
            {
                scoreIcon = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                resources.Add(scoreIcon);
                // A gold coin with a dark rim, matching the health bar's outlined style.
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
                    scoreIcon.SetPixel(x, y, radius > 30 ? Color.clear : radius > 26 ? new Color(.15f, .12f, .05f)
                        : radius > 22 ? new Color(1, .88f, .34f) : new Color(.96f, .65f, .08f));
                }
                scoreIcon.Apply();
            }
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(rect.x + 7, rect.y + 4, 34, 34), scoreIcon);
            var caption = new GUIStyle(skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, padding = new RectOffset() };
            var number = new GUIStyle(caption) { fontSize = 30, alignment = TextAnchor.MiddleRight };
            number.normal.textColor = Score < 0 ? new Color(.72f, .12f, .18f) : new Color(.12f, .10f, .06f);
            GUI.Label(new Rect(rect.x + 50, rect.y, 66, rect.height), "SCORE", caption);
            GUI.Label(new Rect(rect.x + 119, rect.y, rect.width - 131, rect.height), Score.ToString(), number);
            GUI.color = old;
        }
        void DrawHud()
        {
            Panel(new Rect(18, 18, 1164, 60), .88f);
            DrawScorePanel(new Rect(34, 27, 270, 42));
            GUI.Label(new Rect(340, 28, 460, 36), "TIME  " + TimeSpan.FromSeconds(elapsed).ToString(@"mm\:ss") + "     TRAIL  " + (trail ? Mathf.RoundToInt(95f * nextTrailGate / Mathf.Max(1, trailGates.Count)) : Mathf.Clamp(Mathf.FloorToInt(Local(locomotion.travelRoot.position).z / length * 100), 0, 100)) + "%");
            DrawHealthBar(new Rect(826, 27, 338, 22));
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
