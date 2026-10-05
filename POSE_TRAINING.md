# Pose training: teaching the elephant your poses

This is how we get the Pose Playbook's moves working on our camera. The elephant doesn't learn from examples. `DigiPhantPoseActions` compares each person's body signals to threshold numbers, and "training" means measuring our own bodies and setting those numbers. Expect one session of about 45 minutes with all three of us.

## What's built

| Who | Move | Elephant does | Status |
|---|---|---|---|
| P1 (left) | Left hand up or down from the waist | Walk or run forward, back up | Starter, working |
| P1 | Right knee lift | Jump. It's a forward jump if travelling, otherwise in place | Tuned (session 1) |
| P2 (centre) | Lean left or right | Turn | Starter, moved to P2 by the setup menu |
| P2 | T-pose, held 0.6 s | 180° about-turn | Tuned (session 1) |
| P2 | Both arms straight overhead, held 0.4 s | Rear up | Tuned (session 1) |
| P3 (right) | Right hand height | Trunk curl | Starter, working |
| P3 | Lean left or right | Trunk swing left or right (later: right hand to the side) | **New, needs testing** |
| P3 | Squat with the right hand low | Reach | **New, needs testing** |
| P3 | Hold the reach 1 s near the log | Pick up the log, then carry it; hold again after standing up to drop it | **New, needs testing** |
| P3 | Fist / open hand | Carry / drop with a hand gesture | Not built yet (see the end) |

When moves collide, the order is **Rear up > About-turn > Jump > Travel and turn**. While the elephant rears or turns around, it ignores P1's travel and P2's lean.

## 1. One-time Unity setup

1. Open `Assets/StudentWork/Scenes/DigiPhant_Student.unity`. Make sure Play is stopped.
2. Click **DigiPhant → Set Up Pose Actions** in the top menu bar. The menu item does four things:
   - adds an `Action Pivot` between `Elephant Travel` and the elephant, so jumps and rears move the model
   - adds the Pose Actions component to **DigiPhant Controls** (P1 is the driver, P2 the navigator)
   - sets the group to 3 performers in Full body mode
   - moves steering from P1's lean to P2's lean
3. Press **Cmd+S** (Mac) or **Ctrl+S** (Windows) to save the scene. If the Console says "Add Locomotion To Open Scene first", run that menu item, then this one again.

## 2. Set up the space

- **Camera:** on a tripod with the lens about 1.1 m high (waist to chest), in landscape, 3.8–4.2 m from where you stand.
- **Zones:** the camera preview shows thin lines splitting it into **P1 zone / P2 zone / P3 zone**. Who's who depends only on which zone you stand in, so each person stays inside their own zone the whole time. Anyone sitting at the laptop is ignored automatically.
- **Spacing:** 1.8 m centre to centre, in a shallow arc with P1 and P3 about 30 cm forward. Tape an X for each person. Never cross or step in front of each other.
- **Order:** P1 on the left of the preview image and P3 on the right. The preview isn't mirrored.
- **Framing:** everyone stays fully in frame from raised hands to feet, including during a knee lift.
- **Look:** a plain background, light from the front, fitted clothes and visible wrists.

## 3. Calibrate

1. Press **Play**, choose **3 people**, **Full body**, then **Camera**. Wait for the P1, P2 and P3 labels.
2. Take your neutral poses. **P1 and P3: one hand at your waist. P2: arms down at your sides.** All the thresholds are measured from these poses, so use the same neutral poses every time.
3. Click **Set neutral pose (10 seconds)** and hold still until it says **Neutral pose saved**.

The panel in the top right shows each person's six signals live, measured from their neutral pose (0 = neutral). Those are the numbers you're tuning against.

## 4. Tune the continuous controls (P1 travel, P2 steering, P3 trunk)

The method is the same for each: do the move, read that person's numbers in the readout, then type settings into the Inspector (select **DigiPhant Controls**).

**Changes made during Play are lost when you stop.** Write the values down, or right-click the component's title → **Copy Component**, stop Play, then right-click → **Paste Component Values**.

### P1 (driver): travel (Digi Phant Locomotion → Forward)

1. Have P1 move their left hand **waist → shoulder → overhead → down by the thigh**, and note **P1 LeftHandHeight** at each position.
2. **Sensitivity** = 1 ÷ the overhead reading. For example, overhead 1.4 → 0.7, so full speed is at overhead.
3. **Dead Zone:** how far the number wobbles while P1 holds still at the waist, × Sensitivity. For example, 0.15 × 0.7 ≈ 0.1. Raise it if the elephant creeps.
4. **Run Threshold** (in Digi Phant Locomotion) ≈ the shoulder reading × Sensitivity. For example, 1.0 × 0.7 = 0.7: walk below the shoulder, run above it.
5. **Backward:** a hand down by the thigh only reaches about −0.3 to −0.4. **Backward Sensitivity** (4) multiplies that so a thigh drop gives full reverse at **Backward Speed** (1.8, the same as walking). Lower the sensitivity if reverse starts too easily, raise it if P1 has to drop their hand too far.

The jump threshold comes from the analyzer in step 6. P1's knee also lifts the elephant's front-right leg; that's the starter's own mapping, and it's intended.

### P2 (navigator): steering (Digi Phant Locomotion → Steering)

1. Have P2 lean as far as is comfortable each way, hips still, and note **P2 Lean**. The playbook expects about ±0.3.
2. **Sensitivity** = 1 ÷ that lean (0.3 → about 3, the maximum).
3. **Dead Zone:** how much P2 wobbles standing upright, × Sensitivity. The playbook's 0.08 × 3 ≈ 0.24.
4. **Turn Degrees Per Second:** the playbook suggests 90 (the default is 60).
5. **Direction check:** P2 leaning to *their own* left must turn the elephant left. If it's backwards, set the source **Weight** to **−1**.

The T-pose and arms-overhead thresholds come from the analyzer in step 6. P2's lean also sways the tail.

### P3: trunk (Digi Phant Controller → Controls → Trunk curl)

1. Have P3 raise their right hand from the waist to their highest trunk position, and note **P3 RightHandHeight**.
2. **Sensitivity** = 1 ÷ that reading (1.6 → about 0.6).
3. **Degrees:** curl per trunk joint (12 by default). Make it bigger for more curl, or negative if it curls the wrong way.
4. **Smoothing:** try **12** (the default is 8) for a responsive trunk, as the playbook asks.
5. P3 also drives **Head turn** (their lean) and the **ears** (how far apart their hands are), so raising the hand flaps the ears a little. Set the ear controls' **Sensitivity** to 0 if that's unwanted.

### Smoothing works the opposite way to the playbook

The playbook's 0.7, 0.6 and 0.5 mean "keep this much of the old value", so a bigger number is slower. Unity's **Smoothing** is a speed, so **a bigger number is more responsive**. Don't copy the playbook's numbers across.
- Travel and steering share **Input Smoothing** (Digi Phant Locomotion, default 6). Lower it if jittery, raise it if laggy.
- Each body part has its own **Smoothing**.

**Done when:** P1 walks, runs and stops cleanly, P2 steers both ways, and P3's trunk follows their hand.

## 5. Record training takes

**No clicker needed:** press **▶ Play**, click **3**, **Full body** and **Camera**, then click **Guided recording** in the top-right box. Everyone goes to their zone. A big panel in the middle of the Game view runs the session:

1. **Calibrate:** "Stand in your zone", with a 10-second countdown.
2. **Each take:** the pose name and who does what, then **GET READY 5…** (orange), then **HOLD 6…** (green) while it records.
3. **ALL DONE** when every take is saved.

Each take is saved as `Recordings/signals_<time>_<pose>.csv`, so nobody has to remember the order. **Cancel** stops the session at any point. If calibration fails, the panel says why; fix it and press **Guided recording** again. You can change the countdown lengths (**Get Ready Seconds**, **Hold Seconds**) and the squat take in **Digi Phant Pose Actions**.

The takes, in order:

| Take | Who does what | Everyone else |
|---|---|---|
| `neutral` | Everyone holds neutral | — |
| `tpose` | P2: arms straight out at shoulder height | Neutral |
| `overhead` | P2: both arms straight up, hands close. **Raise them through the front, not out to the sides** | Neutral |
| `knee` | P1: right knee up, thigh level, held | Neutral |
| `other` | Things that must *not* trigger: P1 raises their left hand to walk; P2 leans both ways and swings their arms a bit; P2 raises one arm only | Neutral or normal play |
| `squat` | P3: squat with the right hand low, held (for the trunk actions, coming next) | Neutral |

Run **Guided recording** 2–3 times if you can, ideally swapping who plays P2. More takes make the thresholds work for more people. (The manual **Log signals** button still works too; its files only have the time in the name.)

## 6. Get the thresholds

From the repo folder, run:

```sh
python3 tools/suggest_thresholds.py --dir Paladin_Digiphant/Paladin_Digiphant/Recordings
```

It reads every guided take in the folder, by pose name. For manual **Log signals** files, label them yourself: `neutral=signals_A.csv tpose=signals_B.csv …`. It prints a value for each field:

```
  kneeLiftThreshold    0.51   margin  0.79   P1 RightFootLift: knee lift vs everything else
  tPoseSpreadMin       0.65   margin  0.67   P2 ArmSpread: T-pose vs other arm poses
  ...
```

Stop Play, select **DigiPhant Controls**, and type each value into **Digi Phant Pose Actions** in the Inspector. Save the scene.

- **Margin:** how far apart the pose and the non-poses are. A larger margin means fewer false triggers.
- **OVERLAP:** that signal can't tell the two poses apart. Make the pose bigger, check that the performer is fully in frame, and re-record. Don't pick a number by hand to force it.
- **Neutral warning:** the neutral take wasn't near 0, so recalibrate and re-record.

## 7. Test, in the playbook's drill order

1. **Each role on its own.** Do every move 10 times and count misses and false triggers. Keep the count; it's evidence for the write-up.
2. **P1 and P2 driving a figure-eight**, with walk, lean and the occasional T-pose.
3. **Add P3** moving the trunk while the elephant is stopped.
4. **A full course run.** P2 calls the moves one elephant-length early, using the calls in the playbook.

Tune these in the Inspector if needed:
- **Jump Height** and **Jump Forward Distance**: how big the jump is.
- **Rear Degrees**: how far the elephant rears up (35° by default).
- **Rear Pivot**: where the elephant's rear hips are, in the pivot's own space. If the elephant swings around the wrong point when rearing, move it.
- **Hold seconds**: hold longer if poses trigger too easily, shorter if they feel slow.

## P3 trunk moves (Freddie)

**One-time setup:** stop Play, open `DigiPhant_Student`, click **DigiPhant → Set Up Trunk Actions**, then press **Cmd+S**. This adds:
- a **Trunk swing** control
- a brown **Carry Log** on the ground 6 units in front of the elephant
- the **Digi Phant Trunk Actions** component

**Check the swing direction first (no camera needed):**
1. Press Play and choose **Test sliders**.
2. Drag the **Trunk swing** slider. The trunk should sweep left and right.
3. If it bends up or down instead, stop Play, open **Controls → Trunk swing** and change **Local Axis** from (0, 1, 0) to (1, 0, 0).

**How it plays:**
- **Reach:** Freddie squats with his right hand low. The panel at the bottom right shows **P3 trunk: Reach**.
- **Pick up:** P1 walks the elephant up to the log. Freddie holds the reach for **1 second** and the panel shows **Picked up**. The log now follows the trunk tip.
  - The elephant must be at **walking speed or slower**; otherwise the panel shows **too fast to grab**.
  - The trunk tip must be within **2.5 units** of the log; otherwise the panel shows **nothing in range**.
- **Drop:** Freddie stands up, then squats and holds again. The log is dropped where the trunk is.

**Inspector fields** (Digi Phant Trunk Actions):

| Field | Meaning |
|---|---|
| Squat On / Squat Off | Squat thresholds: 0.25 / 0.12, from session 1's squat take |
| Reach Hand Max | Set to 3 to allow a reach without the hand being low |
| Grab Hold Seconds | How long to hold the reach before it picks up or drops |
| Max Grab Speed | Grabs are ignored above this speed |
| Pickup Radius | How close the trunk tip must be to the log |
| Carry Offset | Where the log hangs from the trunk tip |

**Testing without the camera:** the **⋮** menu on the component has **Test: pick up or drop** and **Reset prop to its start**.

## Our tuning record

**Session 1, 4 Oct 2026.** All three of us were recorded with Guided recording, using the MacBook Pro's built-in webcam and zone tracking. All three were tracked through every take with no gaps.

Measured from neutral (median of each take):

| Pose | Signal | Measured | Notes |
|---|---|---|---|
| P2 T-pose | hand heights / arm spread | +0.94…+1.01 / **+0.41** | The spread was the same across runs, so it's how wide P2 reaches. The default threshold (0.8) would never have triggered |
| P2 arms overhead | hand heights / arm spread | +2.0 / −0.10 | Very clear |
| P1 knee lift | right foot lift | **+0.28…+0.33** | Below the default threshold (0.45); one run's take failed because the knee wasn't held |
| P2 lean | lean | ±0.20 | The default steering turned only ~5°/s at this lean |
| P3 squat | both foot lifts / right hand | +0.6…+0.7 / −0.83 | For the trunk actions (next) |

Values saved in `DigiPhant_Student` (verified in the final run: T-pose → 180° about-turn, overhead → rear up, knee → jump, lean → turns up to 82°):

| Component | Field | Default | Ours |
|---|---|---|---|
| Locomotion → Steering | Sensitivity / Dead Zone | 1 / 0.12 | **3 / 0.05** |
| Locomotion | Turn Degrees Per Second | 60 | **90** |
| Pose Actions | Knee Lift Threshold | 0.45 | **0.23** (thin margin, 0.11; re-record if the jump misfires) |
| Pose Actions | T Pose Spread Min / Hand Min / Hand Max | 0.8 / 0.6 / 1.5 | **0.2 / 0.48 / 1.5** |
| Pose Actions | Rear Hand On / Off / Spread Max | 1.5 / 1.1 / 0.4 | **1.45 / 1.21 / 0.16** |

The raw takes are in `Paladin_Digiphant/Paladin_Digiphant/Recordings/` on the recording laptop. They aren't in Git; copy the ones you need for the write-up.

## Known gaps

- **Grab by hand gesture (P3):** not built yet. The squat-and-hold grab above is the fallback that works now. The playbook's version needs a Gesture Recognizer in `Tracking/bridge.py`, a tracker change. The tracker now lives in this repo, so it can be edited directly. The fallback (hand at floor level for 1 s) can be built in Unity with no tracker change.
- **Trunk swing (P3):** needs a new `RightHandSide` signal, which is also a `bridge.py` change.
- **The playbook's "P1 must return to neutral after a rear or turn":** not built yet. Travel picks up again as soon as the action ends, so P1 should drop their hand before the action finishes.
- **Forward jumps ignore the stage edge.** A jump near the boundary can carry the elephant past it.
- **Use Full body only.** In Seated mode, the knee signal turns into right-hand height, and P1 raising that hand would trigger jumps.
