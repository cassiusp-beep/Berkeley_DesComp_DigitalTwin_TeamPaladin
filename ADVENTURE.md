# Trunk Trail Adventure

Open **DigiPhant → Open Adventure** in Unity, then press Play. Or open
`Assets/StudentWork/Scenes/DigiPhant_Adventure.unity`.
Adventure automatically uses the team's generated Trunk Trail when it is present in the scene.
It also starts in `Assets/StudentWork/TrunkTrail/Scenes/DigiPhant_TrunkTrail.unity` if that scene has been created.
The original student scene remains available for advanced control setup.
All added task objects exist only in Play mode; the terrain and scenery are preserved.
An Adventure scene without Trunk Trail still generates the standalone practice course.

## Start a game

Choose **Rock run**, **Delivery**, or **Expedition** (both tasks), and Easy / Normal / Hard.
On Trunk Trail, the terrain and loop stay fixed. Difficulty changes jump obstacles (3 / 5 / 7),
delivery deadline (45 / 30 / 20 seconds), and off-road health loss. The three existing log hurdles
count as jump obstacles; Normal/Hard add rocks on later stretches. Delivery mode hides hurdles.
Choose **Open bonus route** to retain access to the rolling-log lane and balance beam.
The standalone practice course additionally changes length (90 / 140 / 190 metres) and road width.

Start with **Keyboard practice**: W/S move, A/D steer, Space jumps, E grabs/drops.
For camera play, select **Body + camera**, the number of people and Full body or Seated.
Hold a comfortable resting pose, click **Calibrate**, and wait ten seconds.
P1 rests the left hand around waist height so it can move above and below neutral.
For seated play, keep both hands and shoulders visible. Start becomes available after calibration.

Roles adapt automatically:

| Players | Travel / jump | Steering | Grab / drop |
| --- | --- | --- | --- |
| Solo | P1 | P1 | P1 |
| 2 | P1 | P2 | P2 |
| 3–4 | P1 | P2 | P3 |

Existing body-part mappings still apply; four-person mode also assigns trunk curling to P4.

## Pose + hands

MediaPipe Pose continues to track body motion. Gesture Recognizer reads hands alongside it.

- P1 raises/lowers the left hand for forward/reverse travel. Lift the right knee to jump;
  in seated mode raise the right hand. Return to neutral between jumps.
- Steering performer: lean left/right; return to your calibrated resting pose to go straight.
  Optional **Thumb steering** retains thumb up/right and thumb down/left.
- Trunk performer: hold a fist for 0.35 seconds to grab or drop. Open your palm
  for 0.2 seconds before another grab; missed detections cannot trigger repeated actions. Move the trunk close to the active parcel and slow down.
- Actions use confidently assigned, fresh hand detections; unassigned/bystander hands cannot drive the game.
- With **Pose only**, the existing lean steering and squat/hand-low trunk action remain available.

The guide stays translucent and leaves the camera preview unobstructed. It does not record video.
The original guided-recording overlay and animated tutorial also have more transparent backgrounds.

## Rules

- Crossing a rock while sufficiently airborne: **+1**. Hitting it: **−1**. Each rock scores once per run.
- Three numbered parcels are delivered in order to matching green circles. A timer starts on the first pickup.
- Drop inside the matching circle before the deadline: **+1**. Timeout: **0**, then advance to the next parcel.
  Dropping and picking up again does not reset the timer. Walking around a rock earns no point.
- Leaving the road drains HP after a brief grace period. A red HUD flash shows damage.
- Follow Trunk Trail clockwise through the tunnel and around the loop, then cross the finish arch forwards.
  Ordered route gates prevent a shortcut from the start directly to the nearby finish. Lose at zero HP. Menu / restart begins a fresh run.
- Lost body tracking or calibration pauses movement, task time, and HP loss.
- Sound can be toggled. Footsteps, jump, pickup/drop, hit, point, and finish cues are synthesized locally;
  no third-party sound files are required.

## Compare MediaPipe models

The camera menu offers **Full**, **Lite**, and **Heavy** Pose Landmarker models. Switching restarts
Unity's camera bridge and requires recalibration. First use downloads the official Google model with
verified HTTPS. The default remains Full. Gesture Recognizer is a separate model used alongside each.
Models are stored in `Tracking/*.task` and excluded from Git. On this machine all four models are installed.
If a separately launched bridge owns the camera, stop it before switching models in Unity.

Manual tracker example, from `Paladin_Digiphant/Paladin_Digiphant/Tracking`:

```sh
.venv/bin/python bridge.py --pose-variant lite --download-model --people 1
```

Repeat with `full` and `heavy`, comparing missed legs/hands, latency, and stability under the same lighting
and distance. These are experiments, not a claim that one model performs best for your team.
Use `--no-gestures` for a pose-only comparison.

For a repeatable still-image comparison without opening the webcam:

```sh
.venv/bin/python compare_models.py --image /path/to/your/test-photo.jpg --runs 20
```

Omit `--image` for a blank-image model-loading smoke test. Its timings do **not** measure tracking accuracy
or live multi-person FPS. The script processes the supplied image locally and prints JSON.

Official references: [Pose Landmarker](https://ai.google.dev/edge/mediapipe/solutions/vision/pose_landmarker),
[Gesture Recognizer](https://ai.google.dev/edge/mediapipe/solutions/vision/gesture_recognizer).

## Validation

In the Adventure scene during Play, run **DigiPhant → Validate Adventure in Play mode**.
This resets the current practice run and checks rock scoring, duplicate prevention, timed deliveries,
deadline boundaries, difficulty, HP, menu suspension, group roles, and hand ownership/staleness.
On Trunk Trail it also checks terrain following, parcel ground placement, curved road/bonus branch safety,
finish shortcut rejection, complete-loop finishing, and route reset.
It leaves you at the menu in solo keyboard mode.

Existing Python tests:

```sh
Tracking/.venv/bin/python -m unittest discover -s Tracking -v
```

The new scene is an Editor-playable prototype. Live calibration and gesture comfort still need a real
solo/group playtest. Camera auto-launch and recording retain the existing Editor-only limitations.

## Map integration notes

The builder and Adventure share `TrunkTrail/Scripts/TrunkTrailLayout.cs` (moved from Editor with its meta).
Road safety and ordered finish gates use this same generated layout, while prop elevations sample the scene's
actual terrain. GroundFollow stays enabled, and the chase camera follows the elephant's heading around bends.
The original map checkpoint HUD is hidden to avoid two scores. Adventure owns jump scoring and timed parcels;
the tunnel, rolling lane and beam retain their checkpoint behavior. Original carry-log checks are replaced by
the three parcel tasks. Bonus obstacles do not add Adventure points.
Keep the elephant's scene starting position/heading aligned with the builder's original start. A manually
rerouted map (rather than the current generated Trunk Trail) needs its route geometry updated too.

## Solo seated hand controls

With **Solo**, **Seated / close-up** and **Easy pose + hands**, one player drives everything with hand shapes.
The LEFT hand is movement and the RIGHT hand is the trunk and actions (the player's own left and right), so the
two combine freely. Rest with both palms open at chest height, facing the camera, apart, fingers clear of the face.

| Left hand | Elephant | | Right hand | Elephant |
| --- | --- | --- | --- | --- |
| Open palm | Stop | | Open palm | Trunk free; drops what it carries |
| One finger up | Walk | | Closed fist | Pick up, and keep carrying |
| Victory (two fingers) | Run | | Thumb up | Jump (forward when moving) |
| Thumb down | Walk backward | | Victory, held 0.5 s | Victory toot |
| I-love-you, held 0.6 s | About-turn | | I-love-you, held | Rear up while held |

- **Turn:** tilt your shoulders, at any speed. **Trunk:** it follows where the right hand is, whatever its shape.
- A shape counts after 4 confident tracker frames in a row (score 0.6 or higher). Jump, toot and about-turn fire
  once per sign; jump also has a 1 s cooldown.
- If the left hand shows no shape for 0.3 s the elephant stops. Only a right open palm drops, so a lost right
  hand keeps carrying. Rear up and about-turn pause travel; jump and toot do not.
- Body signals do not move the elephant in this mode. The toolbar shows what each hand reads, e.g. `L: point up · R: fist`.
- Tune on **Digi Phant Adventure**: **Seated Lean Dead Zone**, **Seated Lean Full**, and **Trunk Swing Sign**
  (set to 1 if the trunk swings the opposite way to the hand).
- The tracker now tags every hand with its owner's side (`side`: L/R, by nearest pose wrist, not the recognizer's
  mirrored label) and its offset from its own shoulder (`rx`, `ry`, in shoulder widths) on the gesture port.

Every other mode (standing, 2 to 4 players, Pose only, keyboard) is unchanged.

## Pose tools in Adventure

In **Body + camera** mode the menu has three tuning buttons, shared with the student scene's status bar:

- **Guided recording** recalibrates, then counts each pose in and saves one file per pose as
  `Recordings/signals_<time>_<pose>.csv`. The menu is hidden and the camera fills the window behind the panel.
  Instructions name Adventure's roles for the chosen player count. In seated mode the jump take is a right-hand
  raise and the squat take is skipped. Poses do not move the elephant during a recording.
- **Show numbers** shows every player's six live signals, measured from neutral. It is also on the gameplay toolbar.
- **Log signals** records one continuous file, including during play. Return to the menu to stop it.

Analyse a session with `tools/suggest_thresholds.py` (see `POSE_TRAINING.md`).

## Larger webcam view

During play the camera is hidden by default. **Show camera** opens a small corner thumbnail;
**Hide camera** clears it again. Score and controls sit along the screen edges.

During setup, Adventure shows a larger preview at the incoming camera aspect ratio, without cropping or stretching.
Use **Enlarge** for a full-window preview; **Back / Esc** returns to the menu or game.
Gameplay pauses while enlarged. The Start button stays visible and explains whether calibration is needed.
For closer camera play, choose **Seated / close-up**, keep shoulders and both hands visible, and recalibrate.
Enlarging the preview does not widen the physical camera lens or reveal body parts outside its image.

## Animated movement guide

**Movement guide** in the menu and gameplay toolbar opens the team's existing
`Assets/StudentWork/Tutorial/DigiPhantTutorial.cs` animated guide, replacing Adventure's text-only How to play.
It includes setup, movement, steering, trunk, hand gestures (when enabled), and course pages.
Adventure captions follow its current player assignments, seated mode, fist controls and scoring.
Gameplay pauses while the guide is open. Close / Done / Esc returns to the game; arrows navigate pages.
The guide retains its translucent background. Other scenes keep their original guide content.
