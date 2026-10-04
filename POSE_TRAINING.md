# Pose training: teaching the elephant your poses

This is how we get the Pose Playbook's moves working on our camera. The elephant doesn't learn from examples. `DigiPhantPoseActions` compares each person's body signals to threshold numbers, and "training" means measuring our own bodies and setting those numbers. Expect one session of about 45 minutes with all three of us.

## What's built

| Who | Move | Elephant does | Status |
|---|---|---|---|
| P1 (left) | Left hand up or down from the waist | Walk or run forward, back up | Starter, working |
| P1 | Right knee lift | Jump. It's a forward jump if travelling, otherwise in place | **New, needs tuning** |
| P2 (centre) | Lean left or right | Turn | Starter, moved to P2 by the setup menu |
| P2 | T-pose, held 0.6 s | 180° about-turn | **New, needs tuning** |
| P2 | Both arms straight overhead, held 0.4 s | Rear up | **New, needs tuning** |
| P3 (right) | Right hand height | Trunk curl | Starter, working |
| P3 | Hand gesture or floor-level hand | Grab | Not built yet (see the end) |

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
5. **Backward:** a hand down by the thigh only reaches about −0.3 to −0.4, so reversing is slow. Raise **Backward Speed** if needed.

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

Use the **Log signals** button at the top of that panel. Record one take per row below, about 5 seconds each, **holding the pose the whole time**. Click **Log signals** to start and **Stop signal log** to finish. Each take saves a new file in `Paladin_Digiphant/Paladin_Digiphant/Recordings/`.

As you go, write down which file is which pose. The file names only contain the time.

| Take | Who does what | Everyone else |
|---|---|---|
| `neutral` | Everyone holds neutral | — |
| `tpose` | P2: arms straight out at shoulder height | Neutral |
| `overhead` | P2: both arms straight up, hands close. **Raise them through the front, not out to the sides** | Neutral |
| `knee` | P1: right knee up, thigh level, held | Neutral |
| `other` | Things that must *not* trigger: P1 raises their left hand to walk; P2 leans both ways and swings their arms a bit; P2 raises one arm only | Neutral or normal play |

Record 2–3 takes of each pose if you can, ideally with each of us trying the P2 poses. More takes make the thresholds work for more people.

## 6. Get the thresholds

From the repo folder, run:

```sh
python3 tools/suggest_thresholds.py \
  neutral=Paladin_Digiphant/Paladin_Digiphant/Recordings/signals_XXXX.csv \
  tpose=Paladin_Digiphant/Paladin_Digiphant/Recordings/signals_XXXX.csv \
  overhead=... knee=... other=...
```

Repeat a label (`tpose=a.csv tpose=b.csv`) to combine takes. It prints a value for each field:

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

## Known gaps

- **Grab (P3):** not built yet. The playbook's version needs a Gesture Recognizer in `Tracking/bridge.py`, a tracker change. The tracker now lives in this repo, so it can be edited directly. The fallback (hand at floor level for 1 s) can be built in Unity with no tracker change.
- **Trunk swing (P3):** needs a new `RightHandSide` signal, which is also a `bridge.py` change.
- **The playbook's "P1 must return to neutral after a rear or turn":** not built yet. Travel picks up again as soon as the action ends, so P1 should drop their hand before the action finishes.
- **Forward jumps ignore the stage edge.** A jump near the boundary can carry the elephant past it.
- **Use Full body only.** In Seated mode, the knee signal turns into right-hand height, and P1 raising that hand would trigger jumps.
