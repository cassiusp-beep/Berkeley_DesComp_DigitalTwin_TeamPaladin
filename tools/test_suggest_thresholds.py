import contextlib
import csv
import io
import os
import random
import tempfile
import unittest

import suggest_thresholds as st

MOVES = ('LeftHandHeight', 'RightHandHeight', 'LeftFootLift', 'RightFootLift', 'Lean', 'ArmSpread')
# Neutral-relative values for P2 (navigator) and P1's knee, matching the playbook's poses.
POSES = {
    'neutral': dict(knee=0, left=0, right=0, spread=0),
    'tpose': dict(knee=0, left=.95, right=.9, spread=1.05),
    'overhead': dict(knee=0, left=1.85, right=1.8, spread=-.2),
    'knee': dict(knee=.95, left=0, right=0, spread=0),
    'other': dict(knee=.1, left=.3, right=.2, spread=.3),
}
SQUAT_FEET = .6  # P3's foot lifts during the squat take; 0 in every other take


def write_named(folder, pose, stamp, seed=0):
    """A take saved the way Guided recording names it: signals_<date>_<time>_<pose>.csv."""
    path = write_take(folder, pose if pose in POSES else 'neutral', seed=seed)
    named = os.path.join(folder, f'signals_20261004_{stamp}_{pose}.csv')
    os.replace(path, named)
    if pose == 'squat':
        with open(named, newline='') as f:
            rows = list(csv.reader(f))
        head = rows[0]
        for r in rows[1:]:
            for col in ('P3_LeftFootLift', 'P3_RightFootLift'):
                r[head.index(col)] = f'{SQUAT_FEET + random.Random(len(r)).gauss(0, .03):.3f}'
        with open(named, 'w', newline='') as f:
            csv.writer(f).writerows(rows)
    return named


def write_take(folder, pose, n=60, noise=.05, seed=0):
    rng = random.Random(seed)
    path = os.path.join(folder, pose + '.csv')
    header = ['time', 'action', 'speed', 'x', 'z', 'heading'] + [f'P{p}_{m}' for p in (1, 2, 3) for m in MOVES]
    v = POSES[pose]
    with open(path, 'w', newline='') as f:
        w = csv.writer(f)
        w.writerow(header)
        for i in range(n):
            row = {h: '0' for h in header}
            row.update(time=f'{i * .1:.3f}', action='Idle')
            row['P1_RightFootLift'] = f"{v['knee'] + rng.gauss(0, noise):.3f}"
            row['P2_LeftHandHeight'] = f"{v['left'] + rng.gauss(0, noise):.3f}"
            row['P2_RightHandHeight'] = f"{v['right'] + rng.gauss(0, noise):.3f}"
            row['P2_ArmSpread'] = f"{v['spread'] + rng.gauss(0, noise):.3f}"
            row['P3_Lean'] = ''  # lost tracking is logged as an empty cell
            w.writerow([row[h] for h in header])
    return path


def run(args):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        st.main(args)
    return out.getvalue()


def suggested(output, field):
    for line in output.splitlines():
        parts = line.split()
        if parts and parts[0] == field and parts[1] != '—':
            return float(parts[1])
    return None


class SuggestThresholdsTest(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.takes = [f'{p}={write_take(self.dir, p, seed=i)}' for i, p in enumerate(POSES)]

    def test_suggestions_fall_between_poses(self):
        out = run(self.takes)
        self.assertNotIn('OVERLAP', out)
        self.assertTrue(.1 < suggested(out, 'kneeLiftThreshold') < .9)
        self.assertTrue(.3 < suggested(out, 'tPoseSpreadMin') < 1.0)
        self.assertTrue(.3 < suggested(out, 'tPoseHandMin') < .9)
        self.assertTrue(.95 < suggested(out, 'tPoseHandMax') < 1.8)
        on, off = suggested(out, 'rearHandOn'), suggested(out, 'rearHandOff')
        self.assertTrue(.95 < on < 1.8)
        self.assertTrue(.3 < off < on)
        self.assertTrue(-.2 < suggested(out, 'rearSpreadMax') < 1.05)

    def test_overlap_is_flagged(self):
        POSES['fake'] = POSES['tpose']
        try:
            same = write_take(self.dir, 'fake', seed=9)
        finally:
            del POSES['fake']
        out = run([f'tpose={same}', f'overhead={same}'])
        self.assertIn('OVERLAP', out)

    def test_missing_pose_is_reported_not_guessed(self):
        out = run([self.takes[0], self.takes[1]])  # neutral + tpose only
        self.assertIsNone(suggested(out, 'kneeLiftThreshold'))
        self.assertIsNone(suggested(out, 'rearHandOn'))
        self.assertIsNotNone(suggested(out, 'tPoseSpreadMin'))

    def test_dir_reads_guided_recording_files_by_name(self):
        folder = tempfile.mkdtemp()
        for i, pose in enumerate(['neutral', 'tpose', 'overhead', 'knee', 'other', 'squat']):
            write_named(folder, pose, f'15{i:02d}00', seed=i)
        open(os.path.join(folder, 'signals_20261004_150500.csv'), 'w').write('time\n')  # unlabelled: ignored
        out = run(['--dir', folder])
        self.assertIn('Read 6 files', out)
        self.assertNotIn('OVERLAP', out)
        self.assertTrue(.3 < suggested(out, 'tPoseSpreadMin') < 1.0)
        squat = [l for l in out.splitlines() if l.strip().startswith('squatOn')][0]
        self.assertTrue(.05 < float(squat.split()[2]) < SQUAT_FEET)

    def test_empty_dir_is_an_error(self):
        with self.assertRaises(SystemExit), contextlib.redirect_stderr(io.StringIO()):
            run(['--dir', tempfile.mkdtemp()])

    def test_bad_label_is_rejected(self):
        with self.assertRaises(SystemExit), contextlib.redirect_stderr(io.StringIO()):
            run(['jump=x.csv'])


if __name__ == '__main__':
    unittest.main()
