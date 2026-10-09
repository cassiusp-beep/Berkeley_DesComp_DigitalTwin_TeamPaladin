# Trunk Trail: redesigned savannah course with tree-trunk obstacles

Date: 2026-10-08 · Branch: `savannah-course` · Approach chosen: B (full terrain redesign)

## Goal

A new course for Team Paladin's DigiPhant, with sculpted terrain and tree-trunk obstacles. The obstacles give checkpoint feedback, and P3's trunk carry is part of the run. Everything is built natively in Unity from existing project assets and primitives, with no imported assets.

## Constraints

- **The instructor's package stays untouched.** No edits under `Assets/SavannahCourse` or `Assets/DigiPhant`. This keeps his Validate commands passing and lets his updates re-import safely. His course is meant to be the same for every student, so this is a separate course in its own scene, not a replacement.
- **No movement code changes.** `DigiPhantLocomotion` moves on a flat plane, and terrain height is added by a separate component. `stageRadius` is raised to 30 in the new scene only; `DigiPhant_Student.unity` keeps 15.
- **Tested setup:** Unity 6000.6.3f1 and URP 17.6.0. ProBuilder is not installed and not needed. The project's URP asset is not changed. If soft shadows are off there, the user is told and decides.
- The editor is usually open, so all generation runs from menu items.

## Files (all under `Assets/StudentWork/TrunkTrail/`)

| File | Responsibility |
|---|---|
| `Scenes/DigiPhant_TrunkTrail.unity` | Copy of `DigiPhant_Student.unity` with the course built in and `stageRadius = 30`. |
| `Editor/TrunkTrailBuilder.cs` | Menu **DigiPhant > Trunk Trail > Build Course in Open Scene**. Saves a backup (`*_BeforeTrunkTrail.unity`), then creates or replaces the `Trunk Trail (generated)` root. It refuses if that root holds non-generated components, and refuses the source `DigiPhant_Student.unity` (use Create Trunk Trail Scene, which builds in a copy). It disables every other active directional light and logs which. Generation is deterministic from a fixed seed. |
| `Scripts/GroundFollow.cs` | Runs after locomotion each frame. Sets the travel root's height from `Terrain.SampleHeight` and tilts the model smoothly to the slope normal (clamped). Applies the same height change to the follow camera. Keeps the carried log on the trunk tip after it moves the root (no one-frame lag). Ground = the higher of the terrain and any `WalkableSurface`. |
| `Scripts/WalkableSurface.cs` | A box on the balance beam log (2.5 m wide, 8 m long) whose top GroundFollow treats as ground, with short ramps at the edges. Only active while the bonus branch is on. |
| `Scripts/TrunkCheckpoint.cs` | A per-frame zone test (no trigger colliders) with seven modes: Hurdle, Carry (pick up), Tunnel, Beam, Rolling, Deliver, Finish. Reports pass or miss to the progress script, changes the marker material, and raises passed markers and sinks missed ones. Provides the hint text for the HUD. |
| `Scripts/TrunkTrailProgress.cs` | Counts checkpoints and times the run. Shows "n / total", the time, queued messages (each on screen at least 4.5 s), the hint for the active checkpoint, and "Finished n / total in mm:ss" in the `DigiPhantUi` black-on-white style. In TestSliders input mode it also shows "Test jump" and "Test pick up/drop" buttons. Restart also resets the carry log. Has an `optionalLevel` toggle, also shown as an on-screen button, that enables the bonus branch and its checkpoints. |
| `Scripts/RollingTrunk.cs` | A log that rolls back and forth across the bonus lane on a timed loop. Deviation: no trigger; the Rolling checkpoint tests each frame whether the elephant is within reach of the log's segment (`Touches`). |
| Generated assets folder | TerrainData, three TerrainLayers with flat-colour generated textures, a Volume profile, and the fog particle material. |

## Checkpoint rules (feedback only; nothing blocks the elephant)

- **Hurdle:** passes if `DigiPhantPoseActions.CurrentPoseAction` contains "Jump" at any moment while the elephant is inside the zone. Misses if it leaves without one. The zone is about 3 m deep so the 0.7 s jump is caught. Hint: "Hurdle: P1 lift right knee to jump".
- **Carry the log (two steps):** P3 picks up the log at the carry station and carries it across the rest of the course.
  - **Pick up** (mode Carry): passes when `DigiPhantTrunkActions.Carrying` is true while the elephant is in the station zone. Hint: "Pick up: P3 squat, hand low, hold 1 s".
  - **Deliver** (mode Deliver, just before the finish arch): passes when the log has been dropped within the finish stone's radius. Hint: "Deliver: P3 drop the log on the finish stone (squat, hand low, hold 1 s)".
  - One `carryProp`. The log follows the trunk tip every frame, including over slopes (GroundFollow re-places it after moving the root).
- **Tunnel and Beam:** pass when the elephant goes from the entry end to the exit end without leaving the lane width for more than 0.4 s (tracking is noisy). The beam lane is 2.5 m wide, and the beam log is a flat-topped log of the same width, 0.5 m above the ridge, which the elephant walks on. Hints: "Tunnel: stay in the lane", "Beam: stay in the lane".
- **Rolling:** passes on reaching the lane exit. Touching a rolling log counts as a miss, shows "try again", and lets the elephant keep going. Hint: "Rolling: dodge the logs".
- **Finish line:** the finish arch is a real finish line. Crossing it forwards stops the timer and shows "Finished n / total in mm:ss", even with misses. It is not counted as a checkpoint.
- A miss can be retried by re-entering. Order is not enforced, but the count shows progress.
- **Feedback beyond colour:** passed markers rise and glow (emissive green); missed markers sink and turn dark brown. Messages stay at least 4.5 s; a checkpoint's hint stays up while it is active and for 4.5 s after.
- **Restart** resets every checkpoint, the timer, and the carry log (back to its start pose, no longer carried).

## Layout

- **Terrain:** 120 × 120 m, with the course in the central 60 × 60. The elephant starts at the centre.
- **Main loop (about 160 m, clockwise):**
  1. Start
  2. One hurdle
  3. Two hurdles on a gentle rise
  4. A banked turn
  5. A hollow-trunk tunnel through a low hill; the tunnel is open on top to avoid camera clipping
  6. The carry-log station at a gate (pick up)
  7. A banked downhill turn
  8. The finish stone (deliver), then the finish arch near the start
- **Bonus branch** (east side; hidden unless the optional level is on): rolling-trunk lane, then a balance beam over a dip, rejoining the main loop.
- **Path corridor:** 5 m wide, slopes of 8° or less, banking of about 3°. Off the path: rolling hills up to 4 m, with a higher rim at the edges.
- **Every point the elephant must reach is within 28 m of the start.**

## Visuals

- **Style:** low-poly. Logs are 8–10-sided cylinders using the instructor's `Octagonal cylinder` mesh and `Bark` material.
- **Scenery:** acacia trees, bushes and gates reuse the instructor's `Generated/v4` meshes and materials, with seeded scale (0.8–1.4×), rotation and placement kept off the path.
- **Ground textures:** three terrain layers (dirt track, olive grass, dry sand), with the dirt path painted along the corridor.
- **Sun:** a warm directional light at about 35° elevation with soft shadows. Other active directional lights in the scene are disabled so there is one sun.
- **Post-processing:** a URP Volume (components added without blanket overrides; only the changed parameters are overridden) with ACES tonemapping, a warm white balance and colour adjustments, low bloom and a light vignette.
- **Fog:** exponential distance haze in a warm colour, plus a native particle system of horizontal, soft-particle patches for low ground fog in the dips (no hard cuts through the terrain).

## Testing

1. Run the menu on a copy of the scene. It should build without errors, a rebuild should replace the root rather than duplicate it, and the backup file should exist.
2. Run the instructor's two Validate commands; both should still pass.
3. In Play mode with the test buttons ("Test jump", "Test pick up/drop", shown in TestSliders mode) or keyboard, along the whole loop:
   - the elephant never sinks into or floats above the terrain;
   - the camera follows smoothly;
   - each checkpoint mode passes when done correctly and shows "try again" when missed;
   - turning the optional level on and off shows and hides the bonus branch;
   - the elephant stands on top of the beam log while crossing it;
   - the carried log stays on the trunk tip over slopes, and Deliver passes when it is dropped on the finish stone;
   - crossing the finish arch shows "Finished n / total in mm:ss"; Restart puts the log back.
4. A live run with P1, P2 and P3 to check tracking-driven jumps and the carry-log at real distances.

## Out of scope

Collisions that block movement, audio, scoring persistence, and changes to the instructor's course or to movement code.
