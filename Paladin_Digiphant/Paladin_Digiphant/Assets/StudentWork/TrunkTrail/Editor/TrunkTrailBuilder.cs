using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DigiPhant.Editor
{
    // Builds the Trunk Trail course (terrain, tree-trunk obstacles, scenery, light, fog) into a scene, from the menu.
    // Uses the instructor's Savannah meshes and materials read-only; everything it makes lives under
    // Assets/StudentWork/TrunkTrail. Safe to run again: the "Trunk Trail (generated)" root is replaced.
    public static class TrunkTrailBuilder
    {
        const string RootName = "Trunk Trail (generated)";
        const string Folder = "Assets/StudentWork/TrunkTrail", Gen = Folder + "/Generated", SceneFolder = Folder + "/Scenes";
        const string SourceScene = "Assets/StudentWork/Scenes/DigiPhant_Student.unity", TargetScene = SceneFolder + "/DigiPhant_TrunkTrail.unity";
        const string V4 = "Assets/SavannahCourse/Generated/v4", CarryLogMaterial = "Assets/StudentWork/Materials/CarryLog.mat";
        const float TerrainSize = 120;
        static readonly string[] OldSceneObjects =
        {
            "Savannah Course [layout 1]", "Savannah Course [layout 2]", "Savannah Course [layout 3]", "Savannah Course [layout 4]",
            "Stage", "Stage distance grid (4 units)", "Carry Log",
        };
        static readonly Color Fog = new Color(.84f, .73f, .58f);

        // State for one build.
        static TrunkTrailLayout layout;
        static Terrain terrain;
        static Vector3 start;
        static Quaternion yawRot;
        static float yawDeg;
        static Mesh cylinder, canopy, post;
        static Material bark, olive, sage, ivory, terracotta, stone, passMaterial, missMaterial, carryMaterial;
        static uint random;

        [MenuItem("DigiPhant/Trunk Trail/Create Trunk Trail Scene")]
        public static void CreateScene()
        {
            string previous = null;
            bool copied = false;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before creating the scene.");
                if (File.Exists(TargetScene))
                { Debug.LogWarning("Trunk Trail: " + TargetScene + " already exists. Open it and use DigiPhant > Trunk Trail > Build Course in Open Scene to rebuild; nothing was changed."); return; }
                // Everything that can be checked without the scene is checked before anything is copied.
                Preflight();
                if (!File.Exists(SourceScene)) throw new FileNotFoundException("Missing " + SourceScene);
                if (!SceneHasLocomotion(SourceScene)) throw new InvalidOperationException(SourceScene + " has no DigiPhantLocomotion, so the course could not be wired. Nothing was changed.");
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                previous = SceneManager.GetActiveScene().path;
                EnsureFolder(SceneFolder);
                if (!AssetDatabase.CopyAsset(SourceScene, TargetScene)) throw new IOException("Could not copy the student scene.");
                copied = true;
                EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
                Run(false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (copied)
                {
                    // Leave no half-made copy behind, or the next click would refuse with "already exists".
                    if (string.IsNullOrEmpty(previous)) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    else EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                    AssetDatabase.DeleteAsset(TargetScene);
                }
                Fail(e, copied ? " The new scene copy was deleted." : "");
            }
        }

        [MenuItem("DigiPhant/Trunk Trail/Build Course in Open Scene")]
        public static void BuildInOpenScene()
        {
            try { Run(true); }
            catch (Exception e) { Debug.LogException(e); Fail(e, ""); }
        }

        static void Fail(Exception e, string extra) =>
            EditorUtility.DisplayDialog("Trunk Trail could not build", e.Message + extra + "\n\nSee the Console for details.", "OK");

        // Checks that need neither the scene nor any change: the instructor's assets, the shaders, and the generated folder.
        static void Preflight()
        {
            LoadInstructorAssets();
            LitShader();
            if (!Shader.Find("Universal Render Pipeline/Particles/Unlit")) throw new InvalidOperationException("Universal Render Pipeline/Particles/Unlit is unavailable.");
        }

        static bool SceneHasLocomotion(string scenePath)
        {
            foreach (var guid in AssetDatabase.FindAssets("DigiPhantLocomotion t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script && script.GetClass() == typeof(DigiPhantLocomotion)) return File.ReadAllText(scenePath).Contains(guid);
            }
            return false;
        }

        static void Run(bool backup)
        {
            // All validation first: nothing is copied, destroyed or written until every check has passed.
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before building the course.");
            if (SceneManager.sceneCount != 1) throw new InvalidOperationException("Open just the elephant scene before building.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) throw new InvalidOperationException("Exit Prefab Mode first.");
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Save the scene first (Ctrl/Cmd+S).");
            if (scene.path == SourceScene)
                throw new InvalidOperationException("This is the source scene DigiPhant_Student.unity, which the whole team shares, so the course is not built into it. Use DigiPhant > Trunk Trail > Create Trunk Trail Scene to build in a copy. Nothing was changed.");
            // The instructor's packages must stay pristine so their updates and validators keep working.
            if (scene.path.StartsWith("Assets/DigiPhant/") || scene.path.StartsWith("Assets/SavannahCourse/"))
                throw new InvalidOperationException("This scene belongs to the instructor's package, so the course is not built into it. Use DigiPhant > Trunk Trail > Create Trunk Trail Scene instead. Nothing was changed.");
            if (scene.isDirty) throw new InvalidOperationException("Save the scene (Ctrl/Cmd+S) before building, so the backup matches. Nothing was changed.");
            var locomotion = Object.FindAnyObjectByType<DigiPhantLocomotion>();
            if (!locomotion || !locomotion.travelRoot) throw new InvalidOperationException("No DigiPhant locomotion with a travel root in this scene. Use DigiPhant > Trunk Trail > Create Trunk Trail Scene, or open its Trunk Trail copy.");
            var oldRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (oldRoot && oldRoot.GetComponentsInChildren<Component>(true).Any(c => !c || !Generated(c)))
                throw new InvalidOperationException("\"" + RootName + "\" holds objects or components that were not generated. Move your own objects outside it; nothing was changed.");
            Preflight();
            sceneKey = SafeName(scene.name);
            walkSpeed = locomotion.walkSpeed;
            start = locomotion.travelRoot.position;
            yawDeg = locomotion.travelRoot.eulerAngles.y;
            yawRot = Quaternion.Euler(0, yawDeg, 0);
            layout = new TrunkTrailLayout();
            random = TrunkTrailLayout.Seed;

            string scenePath = scene.path, backupPath = null;
            bool saved = false;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build Trunk Trail");
            int group = Undo.GetCurrentGroup();
            try
            {
                if (backup)
                {
                    backupPath = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(scenePath, null) + "_BeforeTrunkTrail.unity");
                    if (!AssetDatabase.CopyAsset(scenePath, backupPath)) throw new IOException("Could not back up the scene; build cancelled.");
                }
                EnsureFolder(Gen);
                if (oldRoot) Undo.DestroyObjectImmediate(oldRoot);

                var root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "Build Trunk Trail");
                BuildTerrain(root.transform);
                var sun = BuildSunAndAtmosphere(root.transform);
                var progress = BuildCourse(root.transform, out Transform carryLog);
                BuildScenery(root.transform);
                var disabled = WireScene(scene, locomotion, carryLog, sun);
                Selection.activeGameObject = root;
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the scene.");
                saved = true;
                AssetDatabase.SaveAssets();
                if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.Frame(new Bounds(start, new Vector3(70, 10, 70)), false);
                CheckSoftShadows();
                Debug.Log("Trunk Trail built in " + scene.path + (backupPath != null ? " | backup: " + backupPath : "")
                    + " | checkpoints: " + progress.mainCheckpoints.Length + " main + " + progress.bonusCheckpoints.Length + " bonus"
                    + " | stageRadius 30 | disabled old objects: " + (disabled.Count == 0 ? "none" : string.Join(", ", disabled)));
            }
            catch
            {
                EditorUtility.ClearProgressBar();
                if (!saved)
                {
                    // Undo what the Undo system tracked, then reload from disk: the scene was clean when we started, and
                    // this also restores the render settings that Undo does not track.
                    Undo.RevertAllDownToGroup(group);
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    if (backupPath != null) AssetDatabase.DeleteAsset(backupPath);
                }
                throw;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static string sceneKey = "scene";

        // The scene's own name, safe in a file name, so scenes (and their backups) never share a generated asset.
        static string SafeName(string name)
        {
            var chars = name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == '/' ? '_' : ch).ToArray();
            return new string(chars);
        }

        static bool Generated(Component c) =>
            c is Transform || c is MeshFilter || c is MeshRenderer || c is Terrain || c is TerrainCollider || c is Light
            || c is Volume || c is ParticleSystem || c is ParticleSystemRenderer || c is UniversalAdditionalLightData
            || c is TrunkCheckpoint || c is TrunkTrailProgress || c is RollingTrunk || c is WalkableSurface;

        // ---------- shared assets ----------

        static T Instructor<T>(string file) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(V4 + "/" + file);
            if (!a) throw new InvalidOperationException("Missing " + V4 + "/" + file + ". Run DigiPhant > Savannah > Inject Course into Open Scene once to create it.");
            return a;
        }

        static void LoadInstructorAssets()
        {
            cylinder = Instructor<Mesh>("Octagonal cylinder.asset");
            canopy = Instructor<Mesh>("Acacia canopy.asset");
            post = Instructor<Mesh>("Four-sided post.asset");
            bark = Instructor<Material>("Bark.mat");
            olive = Instructor<Material>("Olive foliage.mat");
            sage = Instructor<Material>("Sage foliage.mat");
            ivory = Instructor<Material>("Ivory markers.mat");
            terracotta = Instructor<Material>("Terracotta markers.mat");
            stone = Instructor<Material>("Basin stone.mat");
            carryMaterial = AssetDatabase.LoadAssetAtPath<Material>(CarryLogMaterial) ?? bark;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static T Asset<T>(string path, Func<T> make) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a) return a;
            a = make();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static Shader LitShader()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("Universal Render Pipeline/Lit is unavailable; Trunk Trail needs URP.");
            return shader;
        }

        static Material FlatMaterial(string name, Color color, Color? glow = null)
        {
            var m = Asset(Gen + "/" + name + ".mat", () => new Material(LitShader()) { name = name, enableInstancing = true });
            m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0);
            if (glow.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", glow.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D FlatTexture(string name, Color c)
        {
            var t = Asset(Gen + "/" + name + ".asset", () => new Texture2D(32, 32, TextureFormat.RGBA32, true) { name = name });
            var px = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float v = 1 + (Mathf.Repeat(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f, 1) - .5f) * .07f; // faint, fixed speckle
                    px[y * 32 + x] = new Color(c.r * v, c.g * v, c.b * v, 1);
                }
            t.SetPixels(px);
            t.wrapMode = TextureWrapMode.Repeat;
            t.Apply(true);
            EditorUtility.SetDirty(t);
            return t;
        }

        static TerrainLayer Layer(string name, Color c)
        {
            var l = Asset(Gen + "/" + name + ".terrainlayer", () => new TerrainLayer { name = name });
            l.diffuseTexture = FlatTexture(name + " texture", c);
            l.tileSize = new Vector2(6, 6);
            l.smoothness = 0;
            l.metallic = 0;
            EditorUtility.SetDirty(l);
            return l;
        }

        // ---------- coordinates ----------

        static Vector2 ToLocal(Vector2 worldOffset)
        {
            var v = Quaternion.Inverse(yawRot) * new Vector3(worldOffset.x, 0, worldOffset.y);
            return new Vector2(v.x, v.z);
        }

        static Vector3 ToWorld(Vector2 local)
        {
            var v = yawRot * new Vector3(local.x, 0, local.y);
            return new Vector3(start.x + v.x, 0, start.z + v.z);
        }

        static float GroundY(Vector3 world) => terrain.SampleHeight(world) + terrain.transform.position.y;
        static Vector3 GroundAt(Vector2 local, float up = 0) { var w = ToWorld(local); w.y = GroundY(w) + up; return w; }
        static Quaternion Heading(Vector2 dir) => Quaternion.Euler(0, yawDeg + Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 0);

        static Vector3 OnGround(Transform frame, float x, float z, float up = 0)
        {
            var w = frame.TransformPoint(x, 0, z);
            w.y = GroundY(w) + up;
            return w;
        }

        static float Next() { random = unchecked(random * 1664525u + 1013904223u); return (random >> 8) / 16777216f; }

        // ---------- terrain ----------

        static void BuildTerrain(Transform parent)
        {
            const int res = 257, alphaRes = 256;
            float step = TerrainSize / (res - 1);
            var raw = new float[res, res];
            float min = float.MaxValue, max = float.MinValue;
            for (int j = 0; j < res; j++)
            {
                if (j % 16 == 0) EditorUtility.DisplayProgressBar("Trunk Trail", "Sculpting the terrain (a few seconds)", j / (float)res * .5f);
                for (int i = 0; i < res; i++)
                {
                    var w = new Vector2(i * step - TerrainSize / 2, j * step - TerrainSize / 2);
                    float h = layout.Sample(ToLocal(w), out _, out _) + 6.5f * Mathf.SmoothStep(0, 1, (Mathf.Max(Mathf.Abs(w.x), Mathf.Abs(w.y)) - 40) / 18); // rim
                    raw[j, i] = h; min = Mathf.Min(min, h); max = Mathf.Max(max, h);
                }
            }
            float baseline = min - .05f, range = max - baseline + .05f;
            var heights = new float[res, res];
            for (int j = 0; j < res; j++) for (int i = 0; i < res; i++) heights[j, i] = (raw[j, i] - baseline) / range;

            var alpha = new float[alphaRes, alphaRes, 3];
            for (int j = 0; j < alphaRes; j++)
            {
                if (j % 16 == 0) EditorUtility.DisplayProgressBar("Trunk Trail", "Painting the terrain", .5f + j / (float)alphaRes * .5f);
                for (int i = 0; i < alphaRes; i++)
                {
                    var w = new Vector2((i + .5f) * TerrainSize / alphaRes - TerrainSize / 2, (j + .5f) * TerrainSize / alphaRes - TerrainSize / 2);
                    layout.Sample(ToLocal(w), out float dirt, out float sand);
                    float grass = Mathf.Max(0, 1 - dirt - sand), sum = dirt + grass + sand;
                    alpha[j, i, 0] = dirt / sum; alpha[j, i, 1] = grass / sum; alpha[j, i, 2] = sand / sum;
                }
            }
            EditorUtility.ClearProgressBar();

            var td = Asset(Gen + "/TrunkTrail Terrain - " + sceneKey + ".asset", () => new TerrainData { name = "TrunkTrail Terrain - " + sceneKey });
            td.heightmapResolution = res;
            td.size = new Vector3(TerrainSize, range, TerrainSize);
            td.SetHeights(0, 0, heights);
            td.alphamapResolution = alphaRes;
            td.terrainLayers = new[] { Layer("Dirt track", new Color(.52f, .38f, .22f)), Layer("Olive grass", new Color(.33f, .40f, .14f)), Layer("Dry sand", new Color(.78f, .64f, .38f)) };
            td.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(td);

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(start.x - TerrainSize / 2, start.y + baseline, start.z - TerrainSize / 2); // course start sits at the centre, at its own height
            terrain = go.GetComponent<Terrain>();
        }

        // ---------- light, fog, post-processing ----------

        static Light BuildSunAndAtmosphere(Transform parent)
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(parent, false);
            sunGo.transform.rotation = Quaternion.Euler(35, -40, 0); // 35 degrees above the horizon
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1, .86f, .66f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .85f;

            var volumeGo = new GameObject("Post-processing");
            volumeGo.transform.SetParent(parent, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = BuildProfile();

            BuildGroundFog(parent);
            return sun;
        }

        static VolumeProfile BuildProfile()
        {
            // One profile per scene, kept (same GUID) across rebuilds: its components are sub-assets, so they are removed
            // and re-added rather than deleting the profile, which would break the reference in every scene using it.
            string path = Gen + "/TrunkTrail Volume - " + sceneKey + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile)
            {
                foreach (var old in profile.components.ToArray())
                {
                    if (!old) continue;
                    AssetDatabase.RemoveObjectFromAsset(old);
                    Object.DestroyImmediate(old, true);
                }
                profile.components.Clear();
            }
            else
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "TrunkTrail Volume - " + sceneKey;
                AssetDatabase.CreateAsset(profile, path);
            }
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(); // no blanket overrides: only the parameters set below are overridden
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var white = Add<WhiteBalance>(); white.temperature.Override(18); white.tint.Override(4);
            var color = Add<ColorAdjustments>(); color.postExposure.Override(.1f); color.contrast.Override(8); color.saturation.Override(10); color.colorFilter.Override(new Color(1, .96f, .9f));
            var bloom = Add<Bloom>(); bloom.threshold.Override(1); bloom.intensity.Override(.25f); bloom.scatter.Override(.6f);
            var vignette = Add<Vignette>(); vignette.intensity.Override(.25f); vignette.smoothness.Override(.4f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static void BuildGroundFog(Transform parent)
        {
            var tex = Asset(Gen + "/Fog puff.asset", () => new Texture2D(64, 64, TextureFormat.RGBA32, true) { name = "Fog puff" });
            var px = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                    px[y * 64 + x] = new Color(1, 1, 1, Mathf.Pow(1 - Mathf.SmoothStep(0, 1, d), 1.5f));
                }
            tex.SetPixels(px); tex.wrapMode = TextureWrapMode.Clamp; tex.Apply(true);
            EditorUtility.SetDirty(tex);

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!shader) throw new InvalidOperationException("Universal Render Pipeline/Particles/Unlit is unavailable.");
            var m = Asset(Gen + "/Fog puff.mat", () => new Material(shader) { name = "Fog puff" });
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(Fog.r, Fog.g, Fog.b, .35f));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_SoftParticlesEnabled", 1); // the fog fades where it meets the terrain
            m.EnableKeyword("_SOFTPARTICLES_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);

            // A thin slab of slow puffs just above the start level: hills bury them, dips and the beam pit show them.
            var go = new GameObject("Ground fog");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(start.x, start.y + .5f, start.z);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 14; main.startSpeed = .1f;
            main.startSize = new ParticleSystem.MinMaxCurve(4, 7);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
            main.maxParticles = 400;
            var emission = ps.emission; emission.rateOverTime = 28;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(80, .2f, 80);
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .25f), new GradientAlphaKey(1, .75f), new GradientAlphaKey(0, 1) });
            fade.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m;
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard; // flat patches lying on the ground, so no hard cuts through the terrain
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }

        // ---------- geometry helpers ----------

        static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // Local placement (used inside groups).
        static Renderer Part(Transform parent, string name, Mesh mesh, Material material, Vector3 localPos, Vector3 scale, Vector3 localEuler = default)
        {
            var t = Group(parent, name);
            t.localPosition = localPos; t.localRotation = Quaternion.Euler(localEuler); t.localScale = scale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            return r;
        }

        // World placement (parents here are unscaled).
        static Renderer Placed(Transform parent, string name, Mesh mesh, Material material, Vector3 world, Quaternion rotation, Vector3 scale)
        {
            var r = Part(parent, name, mesh, material, Vector3.zero, scale);
            r.transform.SetPositionAndRotation(world, rotation);
            return r;
        }

        // A log (octagonal cylinder) from a to b.
        static Renderer LogBetween(Transform parent, string name, Vector3 a, Vector3 b, float diameter, Material material) =>
            Placed(parent, name, cylinder, material, (a + b) / 2, Quaternion.FromToRotation(Vector3.up, b - a), new Vector3(diameter, (b - a).magnitude, diameter));

        static Renderer Marker(Transform frame, List<Renderer> list, float x, float z, float height)
        {
            var r = Placed(frame, "Marker post", post, ivory, OnGround(frame, x, z, height / 2 - .1f), frame.rotation, new Vector3(.22f, height, .22f));
            list.Add(r);
            return r;
        }

        // ---------- the course ----------

        static TrunkCheckpoint Checkpoint(Transform parent, string name, CheckpointMode mode, string label, Vector2 local, Vector2 dir, Vector2 zone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(GroundAt(local), Heading(dir));
            var c = go.AddComponent<TrunkCheckpoint>();
            c.mode = mode; c.label = label; c.zoneSize = zone;
            c.idleMaterial = ivory; c.passMaterial = passMaterial; c.missMaterial = missMaterial;
            return c;
        }

        static TrunkTrailProgress BuildCourse(Transform parent, out Transform carryLog)
        {
            passMaterial = FlatMaterial("Marker passed", new Color(.25f, .62f, .28f), new Color(.2f, 1f, .3f) * 1.6f); // glows, and the markers rise
            missMaterial = FlatMaterial("Marker missed", new Color(.2f, .09f, .07f));                                   // dark, and the markers sink
            var main = new List<TrunkCheckpoint>();
            var bonus = new List<TrunkCheckpoint>();
            var course = Group(parent, "Course");
            var branchRoot = Group(parent, "Bonus branch");
            var closedRoot = Group(parent, "Bonus branch closed");
            float L = layout.loopLength;
            Vector2 dir;

            // Lead-in hurdle, then two hurdles on the rise.
            Vector2 p = layout.leadIn.At(10, out dir);
            main.Add(Hurdle(course, "Hurdle 1", p, dir));
            p = layout.loop.At(.10f * L, out dir); main.Add(Hurdle(course, "Hurdle 2", p, dir));
            p = layout.loop.At(.155f * L, out dir); main.Add(Hurdle(course, "Hurdle 3", p, dir));

            p = layout.loop.At(.485f * L, out dir); main.Add(Tunnel(course, p, dir));
            p = layout.loop.At(.63f * L, out dir); main.Add(CarryStation(course, p, dir, out carryLog));
            p = layout.loop.At(.95f * L, out dir); main.Add(DeliverStation(course, p, dir));
            p = layout.loop.At(.975f * L, out dir); var finish = FinishArch(course, p, dir);

            p = layout.branch.At(.36f * layout.branch.length, out dir); bonus.Add(RollingLane(branchRoot, p, dir));
            p = layout.branch.At(.66f * layout.branch.length, out dir); bonus.Add(BalanceBeam(branchRoot, p, dir));
            // The "closed" barrier must sit where it cannot reach into the main corridor: further along the branch than the
            // point where a barrier log clears the loop by the corridor, its half length and 1 unit.
            float barrierF = layout.FirstClearBranchFraction(BarrierHalf, 1);
            if (barrierF < 0 || barrierF > .3f)
                throw new InvalidOperationException("No spot on the bonus branch is clear of the main track for the closed barrier (first clear fraction " + barrierF.ToString("0.00") + ").");
            p = layout.branch.At(barrierF * layout.branch.length, out dir); ClosedBarrier(closedRoot, p, dir);
            Debug.Log("Trunk Trail: closed barrier at branch fraction " + barrierF.ToString("0.000") + ", " + layout.DistanceToMain(p).ToString("0.0") + " units from the main track.");

            var progressGo = new GameObject("Trunk Trail progress");
            progressGo.transform.SetParent(parent, false);
            var progress = progressGo.AddComponent<TrunkTrailProgress>();
            progress.mainCheckpoints = main.ToArray();
            progress.bonusCheckpoints = bonus.ToArray();
            progress.bonusRoot = branchRoot.gameObject;
            progress.closedRoot = closedRoot.gameObject;
            progress.finishLine = finish;
            progress.optionalLevel = false;
            foreach (var c in main.Concat(bonus)) c.progress = progress;
            branchRoot.gameObject.SetActive(false);
            return progress;
        }

        static TrunkCheckpoint Hurdle(Transform parent, string label, Vector2 at, Vector2 dir)
        {
            var c = Checkpoint(parent, label, CheckpointMode.Hurdle, label, at, dir, new Vector2(6, 3.2f));
            var marks = new List<Renderer>();
            Vector3 a = OnGround(c.transform, -2.4f, 0), b = OnGround(c.transform, 2.4f, 0);
            a.y = b.y = Mathf.Max(a.y, b.y) + .3f;
            LogBetween(c.transform, "Hurdle log", a, b, .6f, bark);
            Marker(c.transform, marks, -2.9f, 0, 1.4f);
            Marker(c.transform, marks, 2.9f, 0, 1.4f);
            c.markers = marks.ToArray();
            return c;
        }

        static TrunkCheckpoint Tunnel(Transform parent, Vector2 at, Vector2 dir)
        {
            var c = Checkpoint(parent, "Tunnel", CheckpointMode.Tunnel, "Tunnel", at, dir, new Vector2(3.4f, 8));
            var marks = new List<Renderer>();
            foreach (int side in new[] { -1, 1 }) // two stacked logs per side; open on top so the camera never clips
            {
                LogBetween(c.transform, "Wall log", OnGround(c.transform, side * 2.25f, -4, .45f), OnGround(c.transform, side * 2.25f, 4, .45f), 1.1f, bark);
                LogBetween(c.transform, "Wall log (upper)", OnGround(c.transform, side * 2.2f, -4, 1.35f), OnGround(c.transform, side * 2.2f, 4, 1.35f), .9f, bark);
                Marker(c.transform, marks, side * 2.25f, -4.5f, 2);
                Marker(c.transform, marks, side * 2.25f, 4.5f, 2);
            }
            c.markers = marks.ToArray();
            return c;
        }

        // The log is picked up here (P3 squats beside it) and carried the rest of the way to the finish stone.
        static TrunkCheckpoint CarryStation(Transform parent, Vector2 at, Vector2 dir, out Transform log)
        {
            var c = Checkpoint(parent, "Carry station", CheckpointMode.Carry, "Pick up the log", at, dir, new Vector2(9, 18));
            var marks = new List<Renderer>();
            Vector3 a = OnGround(c.transform, -2.6f, -5, .175f), b = OnGround(c.transform, -1.2f, -5, .175f);
            a.y = b.y = (a.y + b.y) / 2;
            var carry = LogBetween(c.transform, "Carry log", a, b, .35f, carryMaterial);
            log = carry.transform;

            foreach (int side in new[] { -1, 1 })
                Placed(c.transform, "Gate post", post, bark, OnGround(c.transform, side * 3.3f, 7.5f, 1.4f), c.transform.rotation, new Vector3(.25f, 3.2f, .25f));
            marks.Add(Placed(c.transform, "Gate beam", post, ivory, OnGround(c.transform, 0, 7.5f, 3.2f), c.transform.rotation, new Vector3(6.9f, .4f, .3f)));
            c.markers = marks.ToArray();
            return c;
        }

        // Just before the finish arch: the carried log is dropped on the stone.
        static TrunkCheckpoint DeliverStation(Transform parent, Vector2 at, Vector2 dir)
        {
            var c = Checkpoint(parent, "Deliver station", CheckpointMode.Deliver, "Deliver the log", at, dir, new Vector2(9, 10));
            var marks = new List<Renderer>();
            var drop = Placed(c.transform, "Finish drop zone", cylinder, stone, OnGround(c.transform, 1.9f, 0, .02f), c.transform.rotation, new Vector3(3.6f, .04f, 3.6f));
            c.dropZone = drop.transform;
            c.dropRadius = 2.2f;
            foreach (int side in new[] { -1, 1 }) Marker(c.transform, marks, side * 3.3f, -4.4f, 1.4f);
            Marker(c.transform, marks, 3.3f, 4.4f, 1.4f);
            Marker(c.transform, marks, -3.3f, 4.4f, 1.4f);
            c.markers = marks.ToArray();
            return c;
        }

        // A real finish line: crossing it forwards ends the run (the timer stops), misses or not.
        static TrunkCheckpoint FinishArch(Transform parent, Vector2 at, Vector2 dir)
        {
            var c = Checkpoint(parent, "Finish arch", CheckpointMode.Finish, "Finish", at, dir, new Vector2(6.4f, 3));
            var arch = c.transform;
            foreach (int side in new[] { -1, 1 }) Part(arch, "Post", post, bark, new Vector3(side * 3.2f, 1.7f, 0), new Vector3(.22f, 3.8f, .22f));
            Part(arch, "Finish beam", post, terracotta, new Vector3(0, 3.65f, 0), new Vector3(6.8f, .55f, .25f));
            for (int i = 0; i < 7; i++) Part(arch, "Ivory stripe", post, ivory, new Vector3(-2.4f + i * .8f, 3.65f, -.14f), new Vector3(.35f, .4f, .04f));
            c.markers = new Renderer[0];
            return c;
        }

        // Two short logs lie along the lane and roll sideways, at different speeds, with the outer 0.65 m of the lane always
        // clear. The touch reach is about the body half-width, so walking a straight line through (at walking speed) passes
        // roughly half the time; the build refuses a layout where fewer than MinRollingPass would pass.
        const float MinRollingPass = .4f;
        static readonly float[] RollZ = { -1.75f, 1.75f }, RollPeriod = { 7f, 7.7f }, RollPhase = { 0, 0 };
        const float RollSpan = 1f, RollHalfLength = .5f, RollRadius = .3f, RollReach = .55f;
        static float walkSpeed = 1.8f;

        static TrunkCheckpoint RollingLane(Transform parent, Vector2 at, Vector2 dir)
        {
            var c = Checkpoint(parent, "Rolling lane", CheckpointMode.Rolling, "Rolling logs", at, dir, new Vector2(5, 8));
            c.touchRadius = RollReach;
            var marks = new List<Renderer>();
            var logs = new List<RollingTrunk>();
            for (int k = 0; k < RollZ.Length; k++)
            {
                var go = new GameObject("Rolling log " + (k + 1));
                go.transform.SetParent(c.transform, false);
                go.transform.position = OnGround(c.transform, 0, RollZ[k], RollRadius);
                var rolling = go.AddComponent<RollingTrunk>();
                rolling.halfSpan = RollSpan; rolling.periodSeconds = RollPeriod[k]; rolling.phase = RollPhase[k];
                rolling.radius = RollRadius; rolling.halfLength = RollHalfLength;
                Part(go.transform, "Log", cylinder, bark, Vector3.zero, new Vector3(RollRadius * 2, RollHalfLength * 2, RollRadius * 2), new Vector3(90, 0, 0)); // axis along the lane
                logs.Add(rolling);
            }
            c.rollingTrunks = logs.ToArray();
            float pass = TrunkTrailLayout.RollingPassFraction(RollZ, RollPeriod, RollPhase, RollSpan, RollHalfLength, RollRadius, RollReach,
                c.zoneSize.x / 2, c.zoneSize.y / 2, walkSpeed);
            Debug.Log("Trunk Trail: rolling lane, " + (pass * 100).ToString("0") + "% of straight walks at " + walkSpeed.ToString("0.0") + " units/s pass.");
            if (pass < MinRollingPass)
                throw new InvalidOperationException("The rolling lane is too hard: only " + (pass * 100).ToString("0") + "% of straight walks pass (need " + (MinRollingPass * 100).ToString("0") + "%).");
            foreach (int side in new[] { -1, 1 }) { Marker(c.transform, marks, side * 2.9f, -4.4f, 1.4f); Marker(c.transform, marks, side * 2.9f, 4.4f, 1.4f); }
            c.markers = marks.ToArray();
            return c;
        }

        // A flat-topped log 2.5 m wide (the lane width), standing 0.5 m above the ridge it spans. GroundFollow treats its
        // top as ground (WalkableSurface), so the elephant visibly walks on it.
        static TrunkCheckpoint BalanceBeam(Transform parent, Vector2 at, Vector2 dir)
        {
            const float width = 2.5f, length = 8, thick = .8f, rise = .5f;
            var c = Checkpoint(parent, "Balance beam", CheckpointMode.Beam, "Balance beam", at, dir, new Vector2(width, length));
            var marks = new List<Renderer>();
            Vector3 a = OnGround(c.transform, 0, -length / 2), b = OnGround(c.transform, 0, length / 2);
            a.y = b.y = Mathf.Max(a.y, b.y) + rise - thick / 2;
            var log = Placed(c.transform, "Beam log", cylinder, bark, (a + b) / 2, Quaternion.FromToRotation(Vector3.up, b - a), new Vector3(width, length, thick));
            var walk = Group(c.transform, "Beam walkable surface");
            var surface = walk.gameObject.AddComponent<WalkableSurface>();
            surface.halfSize = new Vector2(width / 2, length / 2);
            surface.topLocalY = log.bounds.max.y - walk.position.y;
            foreach (int side in new[] { -1, 1 }) { Marker(c.transform, marks, side * 1.7f, -4.4f, 1.4f); Marker(c.transform, marks, side * 1.7f, 4.4f, 1.4f); }
            c.markers = marks.ToArray();
            return c;
        }

        const float BarrierHalf = 2.4f;

        static void ClosedBarrier(Transform parent, Vector2 at, Vector2 dir)
        {
            var frame = Group(parent, "Barrier");
            frame.SetPositionAndRotation(GroundAt(at), Heading(dir));
            LogBetween(frame, "Barrier log", OnGround(frame, -BarrierHalf, 0, .6f), OnGround(frame, BarrierHalf, 0, .6f), .5f, bark);
            foreach (int side in new[] { -1, 1 }) Placed(frame, "Barrier stake", post, terracotta, OnGround(frame, side * 2.6f, 0, .6f), frame.rotation, new Vector3(.22f, 1.4f, .22f));
        }

        // ---------- scenery ----------

        static void BuildScenery(Transform parent)
        {
            var trees = Group(parent, "Acacia trees");
            var bushes = Group(parent, "Bushes");
            int nTrees = 0, nBushes = 0;
            for (int tries = 0; tries < 4000 && (nTrees < 44 || nBushes < 30); tries++)
            {
                var w = new Vector2(Next() * 104 - 52, Next() * 104 - 52);
                Vector2 local = ToLocal(w);
                float scale = .8f + Next() * .6f, yaw = Next() * 360;
                bool tree = Next() < .6f;
                float clear = tree ? 8 : 6.5f;
                if (local.magnitude < 7 || layout.DistanceToPaths(local) < clear) continue;
                if (tree ? nTrees >= 44 : nBushes >= 30) continue;
                var pos = new Vector3(start.x + w.x, 0, start.z + w.y);
                pos.y = GroundY(pos);
                if (tree) Acacia(trees, ++nTrees, pos, yaw, scale); else Bush(bushes, ++nBushes, pos, yaw, scale);
            }
        }

        static void Acacia(Transform parent, int index, Vector3 pos, float yaw, float scale)
        {
            var tree = Group(parent, "Acacia " + index.ToString("00"));
            tree.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            tree.localScale = Vector3.one * scale;
            float height = 3.4f + Next() * 1.2f, width = 3.8f + Next() * 1.3f;
            Part(tree, "Trunk", cylinder, bark, new Vector3(0, height * .45f, 0), new Vector3(.36f, height * .9f, .36f));
            for (int side = -1; side <= 1; side += 2)
            {
                Part(tree, "Branch", cylinder, bark, new Vector3(side * .43f, height * .76f, 0), new Vector3(.19f, height * .42f, .19f), new Vector3(0, 0, side * -40));
                Part(tree, "Flat canopy", canopy, index % 2 == 0 ? olive : sage, new Vector3(side * .66f, height, 0), new Vector3(width, .85f, width * .76f));
            }
        }

        static void Bush(Transform parent, int index, Vector3 pos, float yaw, float scale)
        {
            var bush = Group(parent, "Bush " + index.ToString("00"));
            bush.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            bush.localScale = Vector3.one * scale;
            Part(bush, "Foliage", canopy, olive, new Vector3(0, .7f, 0), new Vector3(2.2f, 1.6f, 2));
            Part(bush, "Foliage crown", canopy, sage, new Vector3(.5f, 1.1f, .1f), new Vector3(1.3f, 1, 1.4f));
        }

        // ---------- wiring into the scene ----------

        static List<string> WireScene(Scene scene, DigiPhantLocomotion locomotion, Transform carryLog, Light sun)
        {
            Undo.RecordObject(locomotion, "Trunk Trail stage radius");
            locomotion.stageRadius = 30;
            EditorUtility.SetDirty(locomotion);

            var follow = locomotion.GetComponent<GroundFollow>();
            if (!follow) follow = Undo.AddComponent<GroundFollow>(locomotion.gameObject);
            Undo.RecordObject(follow, "Wire ground follow");
            follow.locomotion = locomotion;
            follow.terrain = terrain;
            EditorUtility.SetDirty(follow);

            var trunk = locomotion.GetComponent<DigiPhantTrunkActions>();
            if (trunk)
            {
                Undo.RecordObject(trunk, "Assign trail carry log");
                trunk.carryProp = carryLog;
                EditorUtility.SetDirty(trunk);
            }
            else Debug.LogWarning("Trunk Trail: no DigiPhantTrunkActions on the controller, so the carry log is not assigned. Run DigiPhant > Set Up Trunk Actions, then Build Course in Open Scene again.");

            var disabled = new List<string>();
            // One sun only: switch off every other active directional light (not just one by name).
            var generatedRoot = sun.transform.parent;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (l.type != LightType.Directional || !l.enabled || l.transform.IsChildOf(generatedRoot)) continue;
                Undo.RecordObject(l, "Disable old directional light");
                l.enabled = false;
                disabled.Add("light \"" + l.name + "\"");
            }
            foreach (var g in scene.GetRootGameObjects())
            {
                if (!g.activeSelf || !OldSceneObjects.Contains(g.name)) continue;
                if (g.GetComponentInChildren<DigiPhantController>(true) || locomotion.travelRoot == g.transform || locomotion.travelRoot.IsChildOf(g.transform)) continue;
                Undo.RecordObject(g, "Disable old scene object");
                g.SetActive(false);
                disabled.Add(g.name);
            }

            var cam = locomotion.followCamera ? locomotion.followCamera : Camera.main;
            if (cam)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                Undo.RecordObject(data, "Enable post-processing");
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
                if (cam.clearFlags == CameraClearFlags.SolidColor) { Undo.RecordObject(cam, "Haze background"); cam.backgroundColor = Fog; EditorUtility.SetDirty(cam); }
            }

            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = .012f;
            RenderSettings.fogColor = Fog;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.62f, .66f, .72f);
            RenderSettings.ambientEquatorColor = new Color(.45f, .40f, .34f);
            RenderSettings.ambientGroundColor = new Color(.22f, .18f, .14f);
            return disabled;
        }

        static void CheckSoftShadows()
        {
            var asset = GraphicsSettings.currentRenderPipeline;
            var property = asset ? new SerializedObject(asset).FindProperty("m_SoftShadowsSupported") : null;
            if (property != null && !property.boolValue)
                Debug.LogWarning("Trunk Trail: soft shadows are off in the URP asset (" + asset.name + "). The sun asks for soft shadows, but it is your call whether to turn them on there; the project asset was not changed.");
        }
    }
}
