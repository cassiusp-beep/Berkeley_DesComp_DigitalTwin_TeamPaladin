# Digital Twin

Team Paladin's DigiPhant project: a group of people controls a rigged elephant in Unity through webcam pose tracking.

## Setup (once, after cloning)

You need Unity 6000.6.3f1 (install it through Unity Hub), Git, and Python 3.10 or newer (3.14 is what we tested).

1. Clone this repo and run the setup script from its folder:
   - macOS: `./setup.sh`
   - Windows (PowerShell): `powershell -ExecutionPolicy Bypass -File .\setup.ps1`

   The script downloads the instructor's DigiPhant starter (about 130 MB), copies the elephant into `Assets/Elephant`, and installs the camera tracker's Python packages (about 300 MB). It takes a few minutes. Run it again after pulling to get instructor updates; it never overwrites the elephant, your work, or a starter you've edited.
2. In Unity Hub, choose **Add → Add project from disk** and select `Paladin_Digiphant/Paladin_Digiphant`. The first time it opens, Unity takes a few minutes to import.
3. Open `Assets/StudentWork/Scenes/DigiPhant_Student.unity` and press **Play**. The camera preview appears above the controls. If macOS asks for camera access, allow it.

The controls are explained in `Paladin_Digiphant/Paladin_Digiphant/DigiPhantStarter/controls.md` (the instructor's student guide) and `Assets/DigiPhant/README.md`.

## Why the elephant isn't in this repo

The elephant model is a third-party asset whose original license terms still apply, and this repo is public. So we don't republish the model here. `setup.sh` and `setup.ps1` fetch it from the instructor's starter at a fixed version (commit `5adcbea`), so everyone gets the same files. The `DigiPhantStarter/` folder and `Assets/Elephant/` are listed in `.gitignore`.

Commit changes to our own work: `Assets/DigiPhant/`, `Assets/StudentWork/`, scenes, settings, and the camera tracker in `Paladin_Digiphant/Paladin_Digiphant/Tracking/`. That folder is our own copy of the instructor's tracker, and Unity runs it from there. Edit `Tracking/bridge.py` there, not in `DigiPhantStarter/` (see `Tracking/README.md`).
