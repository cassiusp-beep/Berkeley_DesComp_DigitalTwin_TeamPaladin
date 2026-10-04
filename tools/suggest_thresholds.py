#!/usr/bin/env python3
"""Suggest DigiPhantPoseActions thresholds from labelled signal logs.

Easiest: use the in-game "Guided recording" button. It saves one file per pose,
named signals_<time>_<pose>.csv, and this script reads them all from the folder:

    python3 tools/suggest_thresholds.py --dir Paladin_Digiphant/Paladin_Digiphant/Recordings

Or label files from "Log signals" yourself:

    python3 tools/suggest_thresholds.py neutral=signals_A.csv tpose=signals_B.csv ...

Labels: neutral, tpose, overhead, knee, other, squat. Repeat a label to add more files.
"other" is for movements that must NOT trigger anything (walking, swinging arms,
lifting a hand to travel). Each suggestion sits halfway between the pose's
typical values and everything else's, and the script warns when they overlap.
"""
import argparse
import csv
import re
import sys
from pathlib import Path

LABELS = ('neutral', 'tpose', 'overhead', 'knee', 'other', 'squat')


def percentile(values, q):
    if not values:
        return None
    s = sorted(values)
    k = (len(s) - 1) * q / 100
    lo = int(k)
    hi = min(lo + 1, len(s) - 1)
    return s[lo] + (s[hi] - s[lo]) * (k - lo)


def load(path, driver, navigator, trim, trunk=3):
    """Rows of the signals each threshold uses, from the steady middle of a take; missing values are None."""
    with open(path, newline='') as f:
        rows = list(csv.DictReader(f))
    if not rows:
        return []
    cols = {'knee': f'P{driver}_RightFootLift', 'left': f'P{navigator}_LeftHandHeight',
            'right': f'P{navigator}_RightHandHeight', 'spread': f'P{navigator}_ArmSpread'}
    missing = [c for c in cols.values() if c not in rows[0]]
    if missing:
        sys.exit(f'{path}: no column {missing[0]}. Was the log recorded with enough performers?')
    # The trunk performer's feet are optional (only the squat suggestion uses them).
    for key, col in (('squat_l', f'P{trunk}_LeftFootLift'), ('squat_r', f'P{trunk}_RightFootLift')):
        cols[key] = col if col in rows[0] else None
    cut = int(len(rows) * trim)
    rows = rows[cut:len(rows) - cut] or rows

    def num(text):
        try:
            return float(text)
        except (TypeError, ValueError):
            return None
    return [{k: (num(r[c]) if c else None) for k, c in cols.items()} for r in rows]


def labelled_files(folder):
    """(label, path) for every signals_<date>_<time>_<label>.csv in a folder, oldest first."""
    found = []
    for path in sorted(Path(folder).glob('signals_*_*_*.csv')):
        m = re.fullmatch(r'signals_\d{8}_\d{6}_([a-z]+)\.csv', path.name)
        if m and m.group(1) in LABELS:
            found.append((m.group(1), str(path)))
    return found


def column(rows, key):
    return [r[key] for r in rows if r[key] is not None]


def feet(rows):
    """The lower of the two foot-lift values: a squat raises both, a knee lift only one."""
    return [min(r['squat_l'], r['squat_r']) for r in rows if r['squat_l'] is not None and r['squat_r'] is not None]


def hands(rows, pick):
    return [pick(r['left'], r['right']) for r in rows if r['left'] is not None and r['right'] is not None]


def split(pos, neg, above=True):
    """Threshold between a pose (pos) and non-poses (neg). above=True means the pose is the high side."""
    if not pos or not neg:
        return None
    if above:
        p, n = percentile(pos, 10), percentile(neg, 90)
    else:
        p, n = percentile(pos, 90), percentile(neg, 10)
    gap = (p - n) if above else (n - p)
    return (p + n) / 2, gap, p, n


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('takes', nargs='*', metavar='label=file.csv')
    ap.add_argument('--dir', help='read every signals_<time>_<pose>.csv saved by Guided recording in this folder')
    ap.add_argument('--trunk', type=int, default=3, help='performer who squats to reach (default 3)')
    ap.add_argument('--driver', type=int, default=1, help='performer who knee-lifts to jump (default 1)')
    ap.add_argument('--navigator', type=int, default=2, help='performer who does T-pose / arms overhead (default 2)')
    ap.add_argument('--trim', type=float, default=.2, help='fraction cut from each end of a take (default 0.2)')
    args = ap.parse_args(argv)

    data = {label: [] for label in LABELS}
    pairs = []
    for take in args.takes:
        label, _, path = take.partition('=')
        if label not in LABELS or not path:
            ap.error(f'"{take}" should look like tpose=path.csv (labels: {", ".join(LABELS)})')
        pairs.append((label, path))
    if args.dir:
        found = labelled_files(args.dir)
        if not found:
            ap.error(f'no signals_<time>_<pose>.csv files in {args.dir}. Use Guided recording, or label files by hand.')
        print(f'Read {len(found)} files from {args.dir}: ' + ', '.join(label for label, _ in found))
        pairs += found
    if not pairs:
        ap.error('give --dir or at least one label=file.csv')
    for label, path in pairs:
        data[label] += load(path, args.driver, args.navigator, args.trim, args.trunk)

    print('Samples per pose: ' + ', '.join(f'{k} {len(v)}' for k, v in data.items() if v))
    for label, rows in data.items():
        if rows and len(rows) < 20:
            print(f'  note: only {len(rows)} samples for {label}; hold the pose for 3-5 s per take')

    neutral = data['neutral']
    if neutral:
        drift = [(k, percentile(column(neutral, k), 50)) for k in ('knee', 'left', 'right', 'spread', 'squat_l', 'squat_r')]
        off = [f'{k} {v:+.2f}' for k, v in drift if v is not None and abs(v) > .3]
        if off:
            print('  warning: neutral take is far from 0 (' + ', '.join(off) + '). Recalibrate, then re-record.')

    def everything_but(*skip):
        return [r for label, rows in data.items() if label not in skip for r in rows]

    results = []  # (field, split result, what it separates)
    results.append(('kneeLiftThreshold',
                    split(column(data['knee'], 'knee'), column(everything_but('knee'), 'knee')),
                    f"P{args.driver} RightFootLift: knee lift vs everything else"))
    results.append(('tPoseSpreadMin',
                    split(column(data['tpose'], 'spread'), column(everything_but('tpose', 'knee'), 'spread')),
                    f"P{args.navigator} ArmSpread: T-pose vs other arm poses"))
    results.append(('tPoseHandMin',
                    split(hands(data['tpose'], min), hands(data['neutral'] + data['other'], min)),
                    f"P{args.navigator} lower hand: T-pose vs arms down"))
    results.append(('tPoseHandMax',
                    split(hands(data['tpose'], max), hands(data['overhead'], min), above=False),
                    f"P{args.navigator} higher hand: T-pose vs overhead"))
    rear_on = split(hands(data['overhead'], min), hands(everything_but('overhead', 'knee'), min))
    results.append(('rearHandOn', rear_on, f"P{args.navigator} lower hand: overhead vs everything else"))
    if rear_on:
        _, gap, _, neg = rear_on
        results.append(('rearHandOff', (neg + gap / 4, gap / 2, None, None),
                        'hysteresis: below rearHandOn so a wobble overhead does not drop the rear'))
    results.append(('rearSpreadMax',
                    split(column(data['overhead'], 'spread'), column(data['tpose'], 'spread'), above=False),
                    f"P{args.navigator} ArmSpread: overhead (hands together) vs T-pose"))
    results.append(('squatOn (trunk)',
                    split(feet(data['squat']), feet(everything_but('squat'))),
                    f"P{args.trunk} lower foot lift: squat vs everything else (for the trunk actions, not built yet)"))

    print('\nSuggested Inspector values (DigiPhant Controls → Digi Phant Pose Actions):\n')
    problems = 0
    for field, result, meaning in results:
        if result is None:
            print(f'  {field:<18}  —      need both takes for: {meaning}')
            continue
        value, gap, _, _ = result
        flag = '' if gap > .1 else '   ← OVERLAP: these poses look alike on this signal' if gap <= 0 else '   ← tight margin'
        problems += gap <= .1
        print(f'  {field:<18} {value:6.2f}   margin {gap:5.2f}   {meaning}{flag}')
    if problems:
        print('\nFor overlaps: exaggerate the pose, re-record, or check the performer stays fully in frame.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
