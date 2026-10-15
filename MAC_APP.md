# DigiPhant Mac app

`DigiPhant.app` runs the game without Unity or Python. It includes the Adventure and Trunk Trail scenes, the camera tracker (`Tracking/bridge.py` packaged with PyInstaller) and every pose and gesture model, so it also works offline. It is built for Apple Silicon and signed for the Mac that built it.

## Build or rebuild

Run this from the repo folder (Unity must be closed):

```
tools/build_mac_app.sh
```

The app appears at `Paladin_Digiphant/Paladin_Digiphant/Builds/DigiPhant.app`.

- **Unity open:** use **DigiPhant > Build Mac App** instead. It reuses the packaged tracker.
- **`bridge.py` or `Tracking/requirements.txt` changed:** run `tools/build_mac_app.sh --refresh-tracker`. Otherwise the app keeps the old tracker.
- **First build:** the first run of the script packages the tracker, which takes about 5 minutes. After that, a build takes 3–5 minutes.
- **Every rebuild resets the camera permission.** Rebuilding re-signs the app, so macOS treats it as a new app. On first launch, click **Allow** when DigiPhant asks for the camera. Don't rebuild right before a demo.
- **App icon:** the icon comes from `Paladin_Digiphant/Paladin_Digiphant/AppIcon/DigiPhantIcon.png` (1024 px). Replace that file and rebuild to change it.

## Camera

- **The app asks for the camera itself.** It opens the camera for a moment so macOS can show its prompt, waits up to a minute for **Allow**, then starts the tracker. The tracker inherits the permission. Doing it this way is necessary because OpenCV gives up before the prompt can appear.
- **The tracker turns Center Stage off.** It runs from its own small app bundle inside DigiPhant (`com.teampaladin.digiphant.tracker`), because macOS applies camera effects like Center Stage only per app. `tracker.log` should say `Center Stage off`. If it says `Center Stage unchanged`, the view may zoom and follow people, which moves performers between zones.
- **Only the MacBook camera should be available.** A nearby iPhone appears as a second camera (Continuity Camera), may be picked instead, and adds its own framing effects. On the iPhone, turn off **Settings > General > AirPlay & Continuity > Continuity Camera** before playing.
- **The camera takes about 10–20 seconds to start**, while the tracker loads Python and the pose models. The first launch after a build takes about 30 seconds.

## Where the app keeps files

Everything the app writes goes in `~/Library/Application Support/com.teampaladin.digiphant/`:

- `tracker.log`: the tracker's output. Read this first if the camera preview stays black.
- `Logs/digiphant_signals.csv`: the signal log.
- `Recordings/`: the guided-recording signal files.

The game's own log, with any errors, is `~/Library/Logs/Team Paladin/DigiPhant/Player.log`. Built-in video recording works only in the Unity Editor.

## Bug checklist

Run this before every build you share, and after any change to `Assets/StudentWork`, `Tracking/` or the build scripts. Each item is a bug the app has had, or a problem that only appears in a built app, never in Play mode.

### Code review (before building)

- [ ] **Components that attach themselves must attach on every scene load.** A `[RuntimeInitializeOnLoadMethod]` runs once per launch, so in the app it only covers the first scene. Use the `SceneManager.sceneLoaded` pattern in `DigiPhantAdventure.Bootstrap`. Search: `grep -rn RuntimeInitializeOnLoadMethod Assets/StudentWork`.
- [ ] **Never write files next to `Application.dataPath`.** In the app, that folder is inside `DigiPhant.app`: folders like `Logs/` don't exist there, and writing breaks the app's signature. Use `Application.isEditor ? <project folder> : Application.persistentDataPath`, and create the folder first. Search: `grep -rn dataPath Assets/StudentWork`.
- [ ] **Code inside `#if UNITY_EDITOR` doesn't run in the app.** If a feature depends on it, it needs a built-app version too. Example: the camera bridge launch, which `DigiPhantAppTracker` replaces.
- [ ] **Every shader named in `Shader.Find` is listed in `FoundShaders` in `DigiPhantMacBuild.cs`.** Unity strips shaders that no scene material uses, and `Shader.Find` then returns null. The Adventure course setup crashed this way. Search: `grep -rn Shader.Find Assets/StudentWork`.
- [ ] **No `UnityEditor` API in runtime scripts.** It belongs only in `Editor/` folders. The build fails with `error CS` if this rule is broken.
- [ ] **Network ports are released in `OnDisable`.** Otherwise a scene switch can fail with "address already in use".
- [ ] **New scenes are added to `Scenes` in `DigiPhantMacBuild.cs`.** The scene-switch button only appears when the other scene is in the build.
- [ ] **New Python imports or model files** go into the build: run `--refresh-tracker`, and add any new `.task` model to the download list in `build_mac_app.sh`.

### Build checks (after building)

- [ ] `Builds/unity_build.log` contains `Built /…/DigiPhant.app` and no `error CS`. You can ignore `[Licensing::Module] Error: Access token is unavailable`.
- [ ] `codesign --verify --deep --strict Builds/DigiPhant.app` prints nothing.
- [ ] `Builds/DigiPhant.app/Contents/Resources/DigiPhantTracker/DigiPhantTracker.app` exists, next to the `DigiPhantTracker` launcher script.
- [ ] `git status` shows no changes to `ProjectSettings/ProjectSettings.asset` or `GraphicsSettings.asset`. The build sets the app's name, bundle ID, camera text and shaders, then must restore them, or the Editor saves its settings under the app's name.
- [ ] After a play test, `Player.log` has no `Exception` lines: `grep -n Exception ~/Library/Logs/Team\ Paladin/DigiPhant/Player.log`.
- [ ] `Contents/Resources/Data/Managed/Assembly-CSharp.dll` has this build's timestamp, which shows the new code is inside.

### Play test (on the Mac, with the camera)

- [ ] **Fresh camera permission:** run `tccutil reset Camera com.teampaladin.digiphant`, open the app, and check that macOS asks for the camera, you can click **Allow**, and the preview goes live.
- [ ] `tracker.log` says `Center Stage off`, and the preview doesn't zoom or follow you when you move.
- [ ] Calibrate, then move: walk, steer, jump and use the trunk.
- [ ] **Go to Trunk Trail** and back: the menu, hand gestures and the movement guide are all still there, and the elephant still responds.
- [ ] Switching the pose model (Full, Lite or Heavy) restarts the camera and tracking resumes.
- [ ] Cmd-Q, then reopen: the camera starts again. If it doesn't, an old tracker may still be running: run `pkill DigiPhantTracker`.
- [ ] Log signals: a file appears in `~/Library/Application Support/com.teampaladin.digiphant/Logs/`, and the app bundle has no new files.
