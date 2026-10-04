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
- **Spacing:** 1.8 m centre to centre, in a shallow arc with P1 and P3 about 30 cm forward. Tape an X for each person. Never cross or step in front of each other.
- **Order:** P1 on the left of the preview image and P3 on the right. The preview isn't mirrored.
- **Framing:** everyone stays fully in frame from raised hands to feet, including during a knee lift.
- **Look:** a plain background, light from the front, fitted clothes and visible wrists.

## 3. Calibrate

1. Press **Play**, choose **3 people**, **Full body**, then **Camera**. Wait for the P1, P2 and P3 labels.
2. Take your neutral poses. **P1 and P3: one hand at your waist. P2: arms down at your sides.** All the thresholds are measured from these poses, so use the same neutral poses every time.
3. Click **Set neutral pose (10 seconds)** and hold still until it says **Neutral pose saved**.

The panel in the top right shows each person's six signals live, measured from their neutral pose (0 = neutral). Those are the numbers you're tuning against.

## 4. Record training takes

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

## 5. Get the thresholds

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

## 6. Test, in the playbook's drill order

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

- **Grab (P3):** not built yet. The playbook's version needs a Gesture Recognizer in `bridge.py`, a tracker change. Since `Tracking/` comes from the instructor's starter and Git ignores it, we'll move it into this repo first. The fallback (hand at floor level for 1 s) can be built in Unity with no tracker change.
- **Trunk swing (P3):** needs a new `RightHandSide` signal, which is also a `bridge.py` change.
- **The playbook's "P1 must return to neutral after a rear or turn":** not built yet. Travel picks up again as soon as the action ends, so P1 should drop their hand before the action finishes.
- **Forward jumps ignore the stage edge.** A jump near the boundary can carry the elephant past it.
- **Use Full body only.** In Seated mode, the knee signal turns into right-hand height, and P1 raising that hand would trigger jumps.
