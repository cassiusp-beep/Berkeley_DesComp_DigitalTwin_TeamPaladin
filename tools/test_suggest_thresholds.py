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

    def test_bad_label_is_rejected(self):
        with self.assertRaises(SystemExit), contextlib.redirect_stderr(io.StringIO()):
            run(['jump=x.csv'])


if __name__ == '__main__':
    unittest.main()
