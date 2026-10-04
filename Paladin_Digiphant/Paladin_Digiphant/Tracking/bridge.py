"""DigiPhant camera -> local Unity UDP bridge. Run with --help for setup options."""
import argparse
import errno
import itertools
import json
import math
from pathlib import Path
import socket
import time

FEATURES = ('LeftHandHeight', 'RightHandHeight', 'LeftFootLift', 'RightFootLift', 'Lean', 'ArmSpread')
MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task'


def extract_features(landmarks, upper_body_only=False):
    """Body-relative image measurements; Unity subtracts the calibrated neutral pose."""
    def xy(i):
        p = landmarks[i]
        return p.x, p.y
    def quality(indices):
        return min(min(landmarks[i].visibility, landmarks[i].presence) for i in indices)
    if upper_body_only:
        ls, rs = xy(11), xy(12)
        shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
        scale = max(math.dist(ls, rs), .03)
        left = (shoulder[1] - xy(15)[1]) / scale
        right = (shoulder[1] - xy(16)[1]) / scale
        # Shoulder tilt replaces hip-relative lean; leg channels use hand heights.
        raw = [left, right, left, right, (ls[1] - rs[1]) / scale,
               abs(xy(15)[0] - xy(16)[0]) / (2 * scale)]
        groups = [[11, 12, 15], [11, 12, 16], [11, 12, 15],
                  [11, 12, 16], [11, 12], [11, 12, 15, 16]]
        return shoulder, [max(-3, min(3, v)) for v in raw], [quality(g) for g in groups]
    ls, rs, lh, rh = (xy(i) for i in (11, 12, 23, 24))
    shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
    hip = ((lh[0] + rh[0]) / 2, (lh[1] + rh[1]) / 2)
    scale = max(math.dist(shoulder, hip), .03)
    base = [11, 12, 23, 24]
    groups = [base + [15], base + [16], base + [27], base + [28], base, base + [15, 16]]
    raw = [(shoulder[1] - xy(15)[1]) / scale,
           (shoulder[1] - xy(16)[1]) / scale,
           (hip[1] - xy(27)[1]) / scale,
           (hip[1] - xy(28)[1]) / scale,
           (shoulder[0] - hip[0]) / scale,
           abs(xy(15)[0] - xy(16)[0]) / (2 * scale)]
    return hip, [max(-3, min(3, v)) for v in raw], [quality(g) for g in groups]


class PerformerTracker:
    """Small-group nearest-position assignment with ambiguity rejection.

    This is not biometric identity. Crossings/long occlusions may need manual reset.
    """
    def __init__(self, count, max_distance=.20):
        if count not in (1, 2, 3, 4):
            raise ValueError("Choose 1, 2, 3, or 4 people")
        self.count = count
        self.max_distance = max_distance
        self.positions = None

    def reset(self):
        self.positions = None

    def assign(self, centers, sizes=None):
        if self.positions is None:
            if len(centers) < self.count:
                return {}
            order = sorted(range(len(centers)), key=lambda i: centers[i][0])[:self.count]
            self.positions = [centers[i] for i in order]
            return {slot + 1: index for slot, index in enumerate(order)}
        candidates = []
        for assignment in itertools.product(range(-1, len(centers)), repeat=self.count):
            used = [i for i in assignment if i >= 0]
            if len(set(used)) != len(used):
                continue
            distances = [math.dist(self.positions[s], centers[i]) for s, i in enumerate(assignment) if i >= 0]
            if any(d > self.max_distance for d in distances):
                continue
            candidates.append((-len(used), sum(distances), assignment))
        candidates.sort()
        best = candidates[0]
        # Avoid arbitrary role swaps when two assignments are almost equally plausible.
        if len(candidates) > 1 and candidates[1][0] == best[0] and candidates[1][1] - best[1] < .015:
            return {}
        result = {s + 1: i for s, i in enumerate(best[2]) if i >= 0}
        for slot, index in result.items():
            self.positions[slot - 1] = centers[index]
        return result


def body_size(landmarks, upper_body_only=False):
    """Torso length (full body) or shoulder width (seated), in image units, for the zone size gate."""
    def xy(i):
        return landmarks[i].x, landmarks[i].y
    ls, rs = xy(11), xy(12)
    if upper_body_only:
        return math.dist(ls, rs)
    lh, rh = xy(23), xy(24)
    shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
    hip = ((lh[0] + rh[0]) / 2, (lh[1] + rh[1]) / 2)
    return math.dist(shoulder, hip)


class ZoneTracker:
    """Identity by where people stand: N equal vertical bands of the unmirrored image, P1 leftmost.

    For a fixed formation (each performer on a taped mark, nobody crossing). Detections far bigger
    or smaller than the performers, such as someone sitting at the laptop in front of the lens,
    are ignored by a size gate.
    """
    def __init__(self, count, min_ratio=.6, max_ratio=1.6, tie=.015, size_blend=.05):
        if count not in (1, 2, 3, 4):
            raise ValueError("Choose 1, 2, 3, or 4 people")
        self.count = count
        self.min_ratio, self.max_ratio = min_ratio, max_ratio
        self.tie = tie
        self.size_blend = size_blend
        self.positions = None
        self.sizes = None

    def reset(self):
        self.positions = None
        self.sizes = None

    def zone(self, x):
        return min(self.count - 1, max(0, int(x * self.count)))

    def _fits(self, size, reference):
        return reference > 0 and self.min_ratio * reference <= size <= self.max_ratio * reference

    def assign(self, centers, sizes=None):
        if sizes is None:
            sizes = [1.0] * len(centers)
        by_zone = {z: [] for z in range(self.count)}
        for i, c in enumerate(centers):
            by_zone[self.zone(c[0])].append(i)

        if self.positions is None:
            if any(not by_zone[z] for z in range(self.count)):
                return {}
            # Lower median: performers stand far back, so a close-up extra (the clicker) is the large outlier.
            median = sorted(sizes)[(len(sizes) - 1) // 2]
            chosen = {}
            for z in range(self.count):
                fits = [i for i in by_zone[z] if self._fits(sizes[i], median)]
                if not fits:
                    return {}
                chosen[z + 1] = min(fits, key=lambda i: abs(sizes[i] - median))
            self.positions = [centers[chosen[s]] for s in range(1, self.count + 1)]
            self.sizes = [sizes[chosen[s]] for s in range(1, self.count + 1)]
            return chosen

        result = {}
        for z in range(self.count):
            fits = [i for i in by_zone[z] if self._fits(sizes[i], self.sizes[z])]
            if not fits:
                continue
            ranked = sorted(fits, key=lambda i: math.dist(centers[i], self.positions[z]))
            if len(ranked) > 1 and (math.dist(centers[ranked[1]], self.positions[z])
                                    - math.dist(centers[ranked[0]], self.positions[z])) < self.tie:
                continue  # two similar people in one zone: skip rather than guess
            i = ranked[0]
            result[z + 1] = i
            self.positions[z] = centers[i]
            self.sizes[z] += (sizes[i] - self.sizes[z]) * self.size_blend
        return result


def make_tracker(count, mode='zones'):
    return ZoneTracker(count) if mode == 'zones' else PerformerTracker(count)


def requested_count(data):
    """Validate Unity's local count request without changing tracking on bad input."""
    try:
        value = json.loads(data)
        if isinstance(value, dict) and value.get('version') == 1:
            count = value.get('performerCount')
            if type(count) is int and 1 <= count <= 4:
                return count
    except (ValueError, UnicodeError):
        pass
    return None


def encode_preview(frame, cv2):
    """Fit one low-latency JPEG into a local UDP datagram, preserving aspect ratio."""
    height, width = frame.shape[:2]
    scale = min(320 / width, 240 / height)
    preview = cv2.resize(frame, (max(1, round(width * scale)), max(1, round(height * scale))))
    # Stay below macOS's commonly configured 9216-byte UDP send limit.
    # JPEG size depends on scene detail, so reduce quality and then dimensions.
    for _ in range(4):
        for quality in (65, 45, 25):
            ok, encoded = cv2.imencode('.jpg', preview, [cv2.IMWRITE_JPEG_QUALITY, quality])
            if ok and len(encoded) <= 8000:
                return encoded.tobytes()
        h, w = preview.shape[:2]
        preview = cv2.resize(preview, (max(1, w // 2), max(1, h // 2)))
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--people', type=int, choices=(1, 2, 3, 4), default=3)
    parser.add_argument('--no-window', action='store_true', help='Preview is displayed inside Unity instead')
    parser.add_argument('--upper-body-only', action='store_true', help='Use shoulders and hands; feet and hips may be out of frame')
    parser.add_argument('--camera', type=int, default=0)
    parser.add_argument('--port', type=int, default=5055)
    parser.add_argument('--assign', choices=('zones', 'track'), default='zones',
                        help='zones: P1..PN by left-to-right image bands (fixed formation); track: follow movement (starter default)')
    parser.add_argument('--model', type=Path, default=Path(__file__).parent / 'pose_landmarker_full.task')
    parser.add_argument('--download-model', action='store_true', help='Download the official model if missing')
    args = parser.parse_args()
    if not 1024 <= args.port <= 65533:
        parser.error('--port must be between 1024 and 65533')
    if not args.model.exists():
        if not args.download_model:
            parser.error('Model missing. Run again with --download-model (internet required).')
        import urllib.request
        args.model.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.model.with_suffix('.download')
        print('Downloading the official MediaPipe pose model...')
        urllib.request.urlretrieve(MODEL_URL, temporary)
        temporary.replace(args.model)
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision

    options = vision.PoseLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(args.model)),
        running_mode=vision.RunningMode.VIDEO, num_poses=4,
        min_pose_detection_confidence=.5, min_pose_presence_confidence=.5,
        min_tracking_confidence=.5)
    tracker = make_tracker(args.people, args.assign)
    camera = cv2.VideoCapture(args.camera)
    if not camera.isOpened():
        raise SystemExit('Camera could not open. Check permissions or try --camera 1.')
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sender.bind(('127.0.0.1', args.port + 1))
        sender.setblocking(False)
    except OSError:
        camera.release()
        sender.close()
        raise SystemExit('Another camera bridge is already running. Close its preview with Q first.')
    edges = [(11, 12), (11, 23), (12, 24), (23, 24), (11, 13), (13, 15),
             (12, 14), (14, 16), (23, 25), (25, 27), (24, 26), (26, 28),
             (27, 29), (29, 31), (27, 31), (28, 30), (30, 32), (28, 32),
             (0, 7), (0, 8), (7, 11), (8, 12)]
    colors = [(100, 230, 100), (255, 180, 80), (100, 160, 255), (220, 100, 230)]
    previous_timestamp = -1
    last_preview = 0.0
    print('Stand side by side, all visible. IDs start left-to-right in the unmirrored preview.')
    print('R: reset identities (then recalibrate in Unity). Q: quit.')
    try:
        with vision.PoseLandmarker.create_from_options(options) as detector:
            while True:
                for _ in range(32):
                    try:
                        data, address = sender.recvfrom(1024)
                    except BlockingIOError:
                        break
                    if address == ('127.0.0.1', args.port):
                        try:
                            command = json.loads(data)
                            if isinstance(command, dict) and command.get('version') == 1:
                                upper = command.get('upperBodyOnly')
                                if type(upper) is bool and upper != args.upper_body_only:
                                    args.upper_body_only = upper
                                    tracker.reset()
                                    print('Movement mode changed. Set neutral pose in Unity.')
                                if command.get('reset') is True:
                                    tracker.reset()
                        except (ValueError, UnicodeError):
                            pass
                    count = requested_count(data) if address == ('127.0.0.1', args.port) else None
                    if count is not None and count != args.people:
                        args.people = count
                        tracker = make_tracker(count, args.assign)
                        print(f'Unity selected {count} people. Reassigning; set neutral pose in Unity.')
                ok, frame = camera.read()
                if not ok:
                    raise RuntimeError('Camera stopped delivering frames.')
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                timestamp = max(previous_timestamp + 1, time.monotonic_ns() // 1_000_000)
                previous_timestamp = timestamp
                result = detector.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), timestamp)
                observations = []
                for landmarks in result.pose_landmarks:
                    center, values, confidence = extract_features(landmarks, args.upper_body_only)
                    if confidence[4] >= .5:
                        observations.append((center, values, confidence, landmarks))
                assignments = tracker.assign([o[0] for o in observations],
                                             [body_size(o[3], args.upper_body_only) for o in observations])
                packet = {'version': 1, 'performerCount': args.people, 'upperBodyOnly': args.upper_body_only, 'people': [dict(slot=slot, values=observations[i][1], confidence=observations[i][2])
                                                 for slot, i in assignments.items()]}
                sender.sendto(json.dumps(packet, allow_nan=False).encode(), ('127.0.0.1', args.port))
                h, w = frame.shape[:2]
                if args.assign == 'zones' and args.people > 1:
                    # Zone boundaries, so performers can see which band is theirs.
                    for z in range(1, args.people):
                        x = int(w * z / args.people)
                        cv2.line(frame, (x, 0), (x, h - 1), (0, 0, 255), 1, cv2.LINE_AA)
                    for z in range(args.people):
                        cv2.putText(frame, f'P{z + 1} zone', (int(w * (z + .5) / args.people) - 40, h - 14),
                                    cv2.FONT_HERSHEY_SIMPLEX, .6, (0, 0, 255), 2, cv2.LINE_AA)
                # Draw every detection, even while waiting for the full group or
                # rejecting an ambiguous identity. Only assigned bodies drive Unity.
                slots = {id(observations[i][3]): slot for slot, i in assignments.items()}
                thickness = max(2, round(max(w / 320, h / 240) * 2))
                radius = max(3, round(thickness * 1.5))
                for landmarks in result.pose_landmarks:
                    slot = slots.get(id(landmarks))
                    color = colors[slot - 1] if slot is not None else (180, 180, 180)
                    points = {}
                    for index, landmark in enumerate(landmarks):
                        if (min(landmark.visibility, landmark.presence) >= .5
                                and math.isfinite(landmark.x) and math.isfinite(landmark.y)
                                and 0 <= landmark.x <= 1 and 0 <= landmark.y <= 1):
                            points[index] = (min(w - 1, int(landmark.x * w)),
                                             min(h - 1, int(landmark.y * h)))
                    for a, b in edges:
                        if a in points and b in points:
                            cv2.line(frame, points[a], points[b], (20, 20, 20), thickness + 2, cv2.LINE_AA)
                            cv2.line(frame, points[a], points[b], color, thickness, cv2.LINE_AA)
                    for point in points.values():
                        cv2.circle(frame, point, radius + 1, (20, 20, 20), -1, cv2.LINE_AA)
                        cv2.circle(frame, point, radius, color, -1, cv2.LINE_AA)
                    anchor = points.get(11, next(iter(points.values()), None))
                    if anchor is not None:
                        label = f'P{slot}' if slot is not None else 'Unassigned'
                        font_scale = max(.6, thickness * .3)
                        cv2.putText(frame, label, anchor, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                    (20, 20, 20), thickness + 2, cv2.LINE_AA)
                        cv2.putText(frame, label, anchor, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                    color, thickness, cv2.LINE_AA)
                message = f'{len(assignments)}/{args.people} assigned' if args.no_window else f'{len(assignments)}/{args.people} assigned | R: reassign | Q: quit'
                if tracker.positions is None:
                    message = f'Waiting for {args.people} visible people' if args.no_window else f'Stand side by side: waiting for {args.people} visible people | Q: quit'
                    if args.assign == 'zones':
                        message = f'Waiting: one person in each of the {args.people} zones'
                cv2.putText(frame, message, (12, 26), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2)
                now = time.monotonic()
                if now - last_preview >= .1:
                    jpeg = encode_preview(frame, cv2)
                    if jpeg is not None:
                        try:
                            sender.sendto(jpeg, ('127.0.0.1', args.port + 2))
                        except OSError as error:
                            # A dropped preview must not stop performer tracking.
                            if error.errno not in (errno.EMSGSIZE, errno.EAGAIN, errno.EWOULDBLOCK, errno.ENOBUFS):
                                raise
                    last_preview = now
                if not args.no_window:
                    cv2.imshow('DigiPhant tracking (unmirrored)', frame)
                key = (cv2.waitKey(1) & 0xff) if not args.no_window else -1
                if key == ord('q'):
                    break
                if key == ord('r'):
                    tracker.reset()
    finally:
        sender.sendto(b'{"version":1,"people":[]}', ('127.0.0.1', args.port))
        sender.close()
        camera.release()
        cv2.destroyAllWindows()


if __name__ == '__main__':
    main()
