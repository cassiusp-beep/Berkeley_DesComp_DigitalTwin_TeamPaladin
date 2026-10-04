# Tracking (Team Paladin copy)

This is the camera tracker that Unity launches when you press Play. It reads the webcam, runs MediaPipe Pose, and sends each performer's six signals to Unity.

**Source:** `Tracking/` from the instructor's starter, https://github.com/kommanderpi/studentstarter, at commit `5adcbea`. We keep our own copy so our changes are versioned with the project, as `DigiPhantStarter/STUDENT_GIT.md` asks.

**Not stored in Git:** `.venv/` and `pose_landmarker_full.task`. `setup.sh` (or `setup.ps1`) copies the model in from the starter and builds `.venv`.

## Our changes

- **Fixed-zone assignment** (`ZoneTracker`, now the default). The image is split into equal vertical zones, and P1 is whoever stands in the left zone of the unmirrored preview, P2 the next, and so on. Thin lines labelled "P1 zone", "P2 zone"… are drawn on the preview.
  - Someone much larger or smaller than the performers is ignored. That covers whoever sits at the laptop in front of the built-in webcam, and people far in the background.
  - It needs everyone to stay in their own zone (on their taped X). Nobody crosses.
  - To go back to the starter's movement-following assignment, pass `--assign track`.
- **Size measurement:** `body_size()` measures torso length (Full body) or shoulder width (Seated) for that size check.

## Run the tests

```sh
.venv/bin/python -m unittest discover -s . -v
```
