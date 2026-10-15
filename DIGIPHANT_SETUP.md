# DigiPhant setup

How to rebuild Team Paladin's DigiPhant project from this repository and run the performance scene.

This repository holds only our own work and the files needed to reproduce the project configuration. The heavy supplied assets are not committed. A setup script recovers them from a pinned version of the instructor's starter repository.

## What you need

- **Unity 6000.6.3f1** (Unity 6.6), installed through Unity Hub. The version is pinned in `Paladin_Digiphant/Paladin_Digiphant/ProjectSettings/ProjectVersion.txt`.
- **Python 3.10 or newer.** We tested 3.14.
- **Git.**
- **A webcam.** We performed with the built-in camera of a MacBook Pro.
- **Internet on the first run only**, to clone the starter and install Python packages.

## 1. Clone and run the setup script

```sh
git clone <this repository's URL>
cd <repository folder>
./setup.sh
```

On Windows, run `powershell -ExecutionPolicy Bypass -File .\setup.ps1` instead.

The script takes a few minutes and does three things:

1. **Clones the instructor's starter at a pinned commit.** It clones https://github.com/kommanderpi/studentstarter into `Paladin_Digiphant/Paladin_Digiphant/DigiPhantStarter/` and checks out commit `5adcbea1f7b311b8bb15c17b3dc4742ae8fff3af`. Pinning the commit means everyone gets exactly the same supplied files.
2. **Copies the elephant model and textures** from the starter into `Assets/Elephant/`.
3. **Sets up the camera tracker.**
   - It copies the MediaPipe pose model (`pose_landmarker_full.task`) from the starter into `Tracking/`.
   - It creates `Tracking/.venv` and installs `Tracking/requirements.txt`: `mediapipe==0.10.35` and `opencv-contrib-python==4.14.0.94`.

Running it again is safe. It never overwrites the elephant, our work, or a starter you've edited.

## 2. Open the project in Unity

1. In Unity Hub, choose **Add > Add project from disk** and select `Paladin_Digiphant/Paladin_Digiphant`.
2. Open it with Unity 6000.6.3f1. The first import takes a few minutes; Unity rebuilds `Library/` from `Packages/` and `ProjectSettings/`.

## 3. Run the performance scene

1. In the Project window, open `Assets/StudentWork/TrunkTrail/Scenes/DigiPhant_TrunkTrail.unity`, the scene we performed in: Adventure mode on our Trunk Trail course.
2. Press **Play**. Unity starts the camera tracker (`Tracking/bridge.py`) by itself. If macOS asks for camera access, allow it.
   - The first Play also downloads MediaPipe's hand gesture model (`gesture_recognizer.task`) into `Tracking/`, which needs internet. Without it, pose tracking still works and hand gestures stay off.
3. In the Adventure menu:
   1. Choose **Body + camera**.
   2. Set the players to **3 people**.
   3. Choose **Full body**.
4. **Get into position.** Three performers stand side by side in fixed zones, left to right: **P1** (Rohan), **P2** (Cassius), **P3** (Freddie). The camera preview draws a line between zones. Everyone must be visible from head to feet.
5. Press **CALIBRATE** and hold a neutral standing pose for the countdown.
6. Choose a challenge and press **START ADVENTURE**.

**Who controls what:**

| Performer | Controls |
|---|---|
| **P1** | Speed (hand height); jump (right knee lift) |
| **P2** | Steering (lean left or right); about-turn (T-pose); rear up (both arms overhead) |
| **P3** | The trunk: reach, then make a fist to pick up a parcel or drop it |

**Movement guide** in the menu shows each move, animated.

`Assets/StudentWork/Scenes/DigiPhant_Adventure.unity` (**DigiPhant > Open Adventure**) runs the same Adventure mode, and is the first scene of the Mac app.

## What is excluded, and where it comes from

| Not in this repository | Recovered by |
|---|---|
| Elephant models and textures (`Assets/Elephant/`) | `setup.sh`, from the starter at commit `5adcbea` |
| MediaPipe pose model (`Tracking/pose_landmarker_full.task`) | `setup.sh`, from the starter at commit `5adcbea` |
| MediaPipe gesture model (`Tracking/gesture_recognizer.task`) | Downloaded from Google on the first Play |
| Python environment (`Tracking/.venv/`) | `setup.sh`, from `Tracking/requirements.txt` |
| Unity `Library/`, `Temp/`, `Logs/`, `UserSettings/` | Unity, when it opens the project |
| Build output (`Builds/`), recordings (`Recordings/`), recorded frames | Not needed to run the project |

## Where our work is

- **`Assets/StudentWork/`**: our scripts, scenes, course, tutorial and editor tools, each with its Unity `.meta` file.
- **`Assets/DigiPhant/`**: the instructor's DigiPhant package, with small hooks we added for Adventure mode.
- **`Assets/SavannahCourse/`**: the instructor's supplied course package.
- **`Tracking/`**: our copy of the instructor's Python tracker, with our changes: fixed performer zones, hand gestures and Center Stage control. `Tracking/README.md` lists every change. Run the tests with `Tracking/.venv/bin/python -m unittest discover -s Tracking -v`.
- **`Packages/` and `ProjectSettings/`**: the Unity package list and project configuration.

## Optional: the standalone Mac app

`tools/build_mac_app.sh` builds `DigiPhant.app`, which runs without Unity or Python. See `MAC_APP.md`.

## Troubleshooting

- **The preview says it's waiting for a performer, or "nobody in zone".** Each performer must stand fully inside their own zone. The preview names the zone that's missing someone.
- **Calibration fails.** Every performer must be fully visible, feet included, when you press CALIBRATE.
- **The camera view zooms or follows people.** That's Center Stage, or an iPhone being used as the camera.
  - The tracker turns Center Stage off itself.
  - If an iPhone is nearby, turn off **Settings > General > AirPlay & Continuity > Continuity Camera** on the iPhone.
- **The elephant ignores one performer.** Recalibrate with all three in position.
