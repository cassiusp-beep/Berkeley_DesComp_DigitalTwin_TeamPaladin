# DigiPhant: Team Paladin

A digital twin of an elephant that three people control together with their bodies. One webcam and MediaPipe track all three performers, and a Unity game turns their poses into one elephant's walking, steering, jumping and trunk.

**Team Paladin**

| Performer | Name | Role |
|---|---|---|
| P1 | Rohan Karmarkar | Driver: speed and jumps |
| P2 | Cassius Palacio | Navigator: steering, about-turn, rear up |
| P3 | Freddie Yang | Trunk: reach, pick up and drop |

Submitted by Cassius Palacio.

## Video and documents

- **Performance video:** https://drive.google.com/file/d/1TrNWXggplJr-OyJgmXpXyit9uF-RPYte/view?usp=drive_link
- **Document:** https://docs.google.com/document/d/1n1lZRmmEF36ZWz0F07mDsH6fsxLEN6R77hFWLruTdjw/edit?usp=drive_link
- **Document (PDF):** https://drive.google.com/file/d/17g9MqpmuC2pqtiyNT0w5HgxP5-jawK4H/view?usp=drive_link
- **Project folder (Google Drive):** https://drive.google.com/drive/folders/1JzrJrxqDRkcv55oK-OOCVzuLTQ3k0nCz?usp=drive_link
- **Setup guide:** [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md)

## The performance scene

`Paladin_Digiphant/Paladin_Digiphant/Assets/StudentWork/TrunkTrail/Scenes/DigiPhant_TrunkTrail.unity`: Adventure mode on our Trunk Trail course. [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md) explains how to set up the project and run it.

## Group reflection

### A tested improvement: jumps that only fire once

In a seated test on 8 October, four deliberate knee raises made the elephant jump eight times. We replayed the recorded signals and found the cause: about 30% of frames had no tracking at all, and our code treated a missing frame as "knee down". Every time tracking came back, the same raised knee looked like a brand-new lift.

We changed the rule so that a missing frame counts as unknown, not as "down". The jump code keeps the last known state until tracking returns. We also split the threshold. Seated play reads the right hand in place of the knee, and a hand jitters far more than a knee, so seated mode now needs a real raise (1.0, about chest height), while standing keeps 0.23.

Replaying the same recording through the new rule gave four jumps for four raises (`DigiPhantPoseActions.cs`, commit `c85a9356`). The lesson for us was that the code was detecting the right pose, then forgetting it every time the camera blinked.

### A remaining limitation: the camera preview breaks down on a big screen

The preview is how we see ourselves while we perform, and it is low resolution. The tracker sends each preview frame to Unity as a single network packet, and macOS caps a packet at about 9 KB. To fit, `encode_preview()` in `Tracking/bridge.py` shrinks every frame to 320 × 240 or smaller and lowers the JPEG quality, sometimes as far as 25. Unity then stretches it up to 1136 × 660 when the preview is enlarged.

On a laptop in our test sessions, this was fine. On the projector, it fell apart. Tracking itself is unaffected, because MediaPipe reads the full-resolution camera image. Only what the performers see suffers.

We designed a fix, but haven't built or tested it. Each frame would be split into numbered packets on a new port and reassembled in Unity, and the old single-packet preview would stay as a fallback for older versions. The broader lesson is that a limit that was invisible in our test setup only showed up once the setup changed: a bigger screen, a real room, and the built app instead of the Editor.

## Full history

This submission repository is a lightweight snapshot. The team's working repository, with the full commit history and pull requests, is https://github.com/cassiusp-beep/Berkeley_DesComp_DigitalTwin_TeamPaladin.

## Setup (once, after cloning)

You need Unity 6000.6.3f1 (install it through Unity Hub), Git, and Python 3.10 or newer (3.14 is what we tested).

1. Clone this repo and run the setup script from its folder:
   - macOS: `./setup.sh`
   - Windows (PowerShell): `powershell -ExecutionPolicy Bypass -File .\setup.ps1`

   The script downloads the instructor's DigiPhant starter (about 130 MB), copies the elephant into `Assets/Elephant`, and installs the camera tracker's Python packages (about 300 MB). It takes a few minutes. Run it again after pulling to get instructor updates; it never overwrites the elephant, your work, or a starter you've edited.
2. In Unity Hub, choose **Add → Add project from disk** and select `Paladin_Digiphant/Paladin_Digiphant`. The first time it opens, Unity takes a few minutes to import.
3. In the Project window, open `Assets/StudentWork/TrunkTrail/Scenes/DigiPhant_TrunkTrail.unity` and press **Play**. If macOS asks for camera access, allow it. [DIGIPHANT_SETUP.md](DIGIPHANT_SETUP.md) walks through calibration and the menu.

The controls are explained in `Paladin_Digiphant/Paladin_Digiphant/DigiPhantStarter/controls.md` (the instructor's student guide) and `Assets/DigiPhant/README.md`.

## Why the elephant isn't in this repo

The elephant model is a third-party asset whose original license terms still apply, and this repo is public. So we don't republish the model here. `setup.sh` and `setup.ps1` fetch it from the instructor's starter at a fixed version (commit `5adcbea`), so everyone gets the same files. The `DigiPhantStarter/` folder and `Assets/Elephant/` are listed in `.gitignore`.

Commit changes to our own work: `Assets/DigiPhant/`, `Assets/StudentWork/`, scenes, settings, and the camera tracker in `Paladin_Digiphant/Paladin_Digiphant/Tracking/`. That folder is our own copy of the instructor's tracker, and Unity runs it from there. Edit `Tracking/bridge.py` there, not in `DigiPhantStarter/` (see `Tracking/README.md`).
