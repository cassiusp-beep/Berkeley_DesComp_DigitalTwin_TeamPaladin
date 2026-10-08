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
| `Editor/TrunkTrailBuilder.cs` | Menu **DigiPhant > Trunk Trail > Build Course in Open Scene**. Saves a backup (`*_BeforeTrunkTrail.unity`), then creates or replaces the `Trunk Trail (generated)` root. It refuses if that root holds non-generated components. Generation is deterministic from a fixed seed. |
| `Scripts/GroundFollow.cs` | Runs after locomotion each frame. Sets the travel root's height from `Terrain.SampleHeight` and tilts the model smoothly to the slope normal (clamped). Applies the same height change to the follow camera. |
| `Scripts/TrunkCheckpoint.cs` | A trigger zone with five modes: Hurdle, Carry, Tunnel, Beam, Rolling. Reports pass or miss to the progress script and lights the marker material. |
| `Scripts/TrunkTrailProgress.cs` | Counts checkpoints and times the run. Shows "n / total", the time and a "try again" message in the `DigiPhantUi` style. Has an `optionalLevel` toggle, also shown as an on-screen button, that enables the bonus branch and its checkpoints. |
| `Scripts/RollingTrunk.cs` | A log that rolls back and forth across the bonus lane on a timed loop, with a kinematic trigger. |
| Generated assets folder | TerrainData, three TerrainLayers with flat-colour generated textures, a Volume profile, and the fog particle material. |

## Checkpoint rules (feedback only; nothing blocks the elephant)

- **Hurdle:** passes if `DigiPhantPoseActions.CurrentPoseAction` contains "Jump" at any moment while the elephant is inside the trigger. Fails if it exits without one. The trigger is about 3 m deep so the 0.7 s jump is caught.
- **Carry:** passes when `DigiPhantTrunkActions.carryProp` enters the drop zone and the log is then dropped (`Carrying` becomes false). The station's log is assigned as `carryProp`.
- **Tunnel and Beam:** pass when the elephant goes from the entry trigger to the exit trigger without leaving the lane volume. The beam lane is 2.5 m wide.
- **Rolling:** passes on reaching the lane exit. Touching a rolling log counts as a miss, shows "try again", and lets the elephant keep going.
- A miss can be retried by re-entering. Order is not enforced, but the count shows progress.

## Layout

- **Terrain:** 120 × 120 m, with the course in the central 60 × 60. The elephant starts at the centre.
- **Main loop (about 160 m, clockwise):**
  1. Start
  2. One hurdle
  3. Two hurdles on a gentle rise
  4. A banked turn
  5. A hollow-trunk tunnel through a low hill; the tunnel is open on top to avoid camera clipping
  6. The carry-log station at a gate
  7. A banked downhill turn
  8. The finish arch near the start
- **Bonus branch** (east side; hidden unless the optional level is on): rolling-trunk lane, then a balance beam over a dip, rejoining the main loop.
- **Path corridor:** 5 m wide, slopes of 8° or less, banking of about 3°. Off the path: rolling hills up to 4 m, with a higher rim at the edges.
- **Every point the elephant must reach is within 28 m of the start.**

## Visuals

- **Style:** low-poly. Logs are 8–10-sided cylinders using the instructor's `Octagonal cylinder` mesh and `Bark` material.
- **Scenery:** acacia trees, bushes and gates reuse the instructor's `Generated/v4` meshes and materials, with seeded scale (0.8–1.4×), rotation and placement kept off the path.
- **Ground textures:** three terrain layers (dirt track, olive grass, dry sand), with the dirt path painted along the corridor.
- **Sun:** a warm directional light at about 35° elevation with soft shadows.
- **Post-processing:** a URP Volume with ACES tonemapping, a warm white balance and colour adjustments, low bloom and a light vignette.
- **Fog:** exponential distance haze in a warm colour, plus a native particle system for low ground fog in the dips.

## Testing

1. Run the menu on a copy of the scene. It should build without errors, a rebuild should replace the root rather than duplicate it, and the backup file should exist.
2. Run the instructor's two Validate commands; both should still pass.
3. In Play mode with keyboard or test input, along the whole loop:
   - the elephant never sinks into or floats above the terrain;
   - the camera follows smoothly;
   - each checkpoint mode passes when done correctly and shows "try again" when missed;
   - turning the optional level on and off shows and hides the bonus branch.
4. A live run with P1, P2 and P3 to check tracking-driven jumps and the carry-log at real distances.

## Out of scope

Collisions that block movement, audio, scoring persistence, and changes to the instructor's course or to movement code.
