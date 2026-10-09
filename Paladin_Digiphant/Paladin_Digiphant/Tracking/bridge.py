"""DigiPhant camera -> local Unity UDP bridge. Run with --help for setup options."""
import argparse
import contextlib
import errno
import itertools
import json
import math
from pathlib import Path
import socket
import time

FEATURES = ('LeftHandHeight', 'RightHandHeight', 'LeftFootLift', 'RightFootLift', 'Lean', 'ArmSpread')
MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task'
GESTURE_MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/latest/gesture_recognizer.task'
# Google's canned gestures: None, Closed_Fist, Open_Palm, Pointing_Up, Thumb_Down, Thumb_Up, Victory, ILoveYou.


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
        self.reset()

    def reset(self):
        self.positions = None
        self.sizes = None
        # Why the performers are (not) driving, for the preview: tracking fails silently otherwise.
        self.status = f'Waiting: one person in each of the {self.count} zones'

    def zone(self, x):
        return min(self.count - 1, max(0, int(x * self.count)))

    def _fits(self, size, reference):
        return reference > 0 and self.min_ratio * reference <= size <= self.max_ratio * reference

    def _size_problem(self, size, reference):
        return 'too close to camera' if size > self.max_ratio * reference else 'too far from camera'

    def assign(self, centers, sizes=None):
        if sizes is None:
            sizes = [1.0] * len(centers)
        by_zone = {z: [] for z in range(self.count)}
        for i, c in enumerate(centers):
            by_zone[self.zone(c[0])].append(i)

        if self.positions is None:
            empty = [f'P{z + 1}' for z in range(self.count) if not by_zone[z]]
            if empty:
                self.status = f"Waiting: nobody in {', '.join(empty)} zone{'s' if len(empty) > 1 else ''}"
                return {}
            # Lower median: performers stand far back, so a close-up extra (the clicker) is the large outlier.
            median = sorted(sizes)[(len(sizes) - 1) // 2]
            chosen, misfits = {}, []
            for z in range(self.count):
                fits = [i for i in by_zone[z] if self._fits(sizes[i], median)]
                if fits:
                    chosen[z + 1] = min(fits, key=lambda i: abs(sizes[i] - median))
                else:
                    nearest = min(by_zone[z], key=lambda i: abs(sizes[i] - median))
                    misfits.append(f'P{z + 1} {self._size_problem(sizes[nearest], median)}')
            if misfits:
                self.status = 'Waiting: ' + ', '.join(misfits)
                return {}
            self.positions = [centers[chosen[s]] for s in range(1, self.count + 1)]
            self.sizes = [sizes[chosen[s]] for s in range(1, self.count + 1)]
            self.status = 'Tracking ' + ' '.join(f'P{s}' for s in chosen)
            return chosen

        result, missing = {}, []
        for z in range(self.count):
            fits = [i for i in by_zone[z] if self._fits(sizes[i], self.sizes[z])]
            if not fits:
                if by_zone[z]:
                    nearest = min(by_zone[z], key=lambda i: abs(sizes[i] - self.sizes[z]))
                    missing.append(f'P{z + 1}: {self._size_problem(sizes[nearest], self.sizes[z])}')
                else:
                    missing.append(f'P{z + 1}: nobody in zone')
                continue
            ranked = sorted(fits, key=lambda i: math.dist(centers[i], self.positions[z]))
            if len(ranked) > 1 and (math.dist(centers[ranked[1]], self.positions[z])
                                    - math.dist(centers[ranked[0]], self.positions[z])) < self.tie:
                missing.append(f'P{z + 1}: two people in zone')
                continue  # two similar people in one zone: skip rather than guess
            i = ranked[0]
            result[z + 1] = i
            self.positions[z] = centers[i]
            self.sizes[z] += (sizes[i] - self.sizes[z]) * self.size_blend
        self.status = ' | '.join([('Tracking ' + ' '.join(f'P{s}' for s in result)) if result else 'Tracking nobody'] + missing)
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


def hand_owner(wrist, performer_wrists, max_distance=.15):
    """Performer slot whose pose wrist (landmark 15 or 16) is nearest this hand's wrist, or 0 if none is close."""
    best, best_distance = 0, max_distance
    for slot, wrists in performer_wrists.items():
        for w in wrists:
            d = math.dist(wrist, w)
            if d < best_distance:
                best, best_distance = slot, d
    return best


def gesture_packet(result, performer_wrists):
    """Top gesture per detected hand, tagged with the performer it belongs to (slot 0 = unassigned)."""
    hands = []
    for categories, landmarks in zip(result.gestures, result.hand_landmarks):
        if not categories or not landmarks:
            continue
        top = categories[0]
        wrist = (landmarks[0].x, landmarks[0].y)
        hands.append(dict(slot=hand_owner(wrist, performer_wrists), gesture=top.category_name,
                          score=round(float(top.score), 3), x=round(wrist[0], 3), y=round(wrist[1], 3)))
    return {'version': 1, 'hands': hands}


def draw_status(frame, lines, thickness, cv2):
    """Top-left status lines, outlined and scaled like the skeleton labels so they stay legible in Unity's small preview."""
    margin = thickness * 4
    room = frame.shape[1] - 2 * margin
    y = 0
    for line in lines:
        font_scale = max(.6, thickness * .3)
        text_w = cv2.getTextSize(line, cv2.FONT_HERSHEY_SIMPLEX, font_scale, thickness)[0][0]
        if text_w > room:
            font_scale *= room / text_w  # shrink a long line rather than cut it off at the edge
        (_, text_h), baseline = cv2.getTextSize(line, cv2.FONT_HERSHEY_SIMPLEX, font_scale, thickness)
        y += text_h + baseline + thickness * 2
        cv2.putText(frame, line, (margin, y), cv2.FONT_HERSHEY_SIMPLEX, font_scale, (20, 20, 20), thickness + 3, cv2.LINE_AA)
        cv2.putText(frame, line, (margin, y), cv2.FONT_HERSHEY_SIMPLEX, font_scale, (255, 255, 255), thickness, cv2.LINE_AA)


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
    parser.add_argument('--gesture-model', type=Path, default=Path(__file__).parent / 'gesture_recognizer.task')
    parser.add_argument('--no-gestures', action='store_true', help='Skip the hand gesture recognizer (sent to --port + 3)')
    args = parser.parse_args()
    if not 1024 <= args.port <= 65532:
        parser.error('--port must be between 1024 and 65532')
    if not args.model.exists():
        if not args.download_model:
            parser.error('Model missing. Run again with --download-model (internet required).')
        import urllib.request
        args.model.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.model.with_suffix('.download')
        print('Downloading the official MediaPipe pose model...')
        urllib.request.urlretrieve(MODEL_URL, temporary)
        temporary.replace(args.model)
    if not args.no_gestures and not args.gesture_model.exists():
        if args.download_model:
            import urllib.request
            temporary = args.gesture_model.with_suffix('.download')
            print('Downloading the official MediaPipe gesture model...')
            urllib.request.urlretrieve(GESTURE_MODEL_URL, temporary)
            temporary.replace(args.gesture_model)
        else:
            # Gestures are an extra: pose tracking still runs without them.
            print('Gesture model missing; hand gestures off. Run again with --download-model to fetch it.')
            args.no_gestures = True
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision

    options = vision.PoseLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(args.model)),
        running_mode=vision.RunningMode.VIDEO, num_poses=4,
        min_pose_detection_confidence=.5, min_pose_presence_confidence=.5,
        min_tracking_confidence=.5)
    # Separate from the pose model above, which is unchanged. Two hands per possible performer.
    gesture_options = None if args.no_gestures else vision.GestureRecognizerOptions(
        base_options=python.BaseOptions(model_asset_path=str(args.gesture_model)),
        running_mode=vision.RunningMode.VIDEO, num_hands=8,
        min_hand_detection_confidence=.5, min_hand_presence_confidence=.5, min_tracking_confidence=.5)
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
        with vision.PoseLandmarker.create_from_options(options) as detector, \
                (vision.GestureRecognizer.create_from_options(gesture_options) if gesture_options
                 else contextlib.nullcontext()) as recognizer:
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
                image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                result = detector.detect_for_video(image, timestamp)
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
                gestures = None
                if recognizer is not None:
                    hand_result = recognizer.recognize_for_video(image, timestamp)
                    wrists = {slot: [(observations[i][3][k].x, observations[i][3][k].y) for k in (15, 16)]
                              for slot, i in assignments.items()}
                    gestures = gesture_packet(hand_result, wrists)
                    try:
                        sender.sendto(json.dumps(gestures, allow_nan=False).encode(), ('127.0.0.1', args.port + 3))
                    except OSError:
                        pass  # a dropped gesture packet must not stop pose tracking
                h, w = frame.shape[:2]
                if args.assign == 'zones' and args.people > 1:
                    # Zone boundaries, so performers can see which band is theirs. Sized like the
                    # skeleton so they survive the preview's ~6x downscale.
                    zone_width = max(2, round(max(w / 320, h / 240) * 2))
                    zone_font = max(.6, zone_width * .3)
                    for z in range(1, args.people):
                        x = int(w * z / args.people)
                        cv2.line(frame, (x, 0), (x, h - 1), (0, 0, 255), zone_width, cv2.LINE_AA)
                    for z in range(args.people):
                        label = f'P{z + 1} zone'
                        (text_w, text_h), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, zone_font, zone_width)
                        cv2.putText(frame, label, (int(w * (z + .5) / args.people) - text_w // 2, h - text_h),
                                    cv2.FONT_HERSHEY_SIMPLEX, zone_font, (0, 0, 255), zone_width, cv2.LINE_AA)
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
                if gestures is not None:
                    # Gesture name beside each hand, in its performer's colour (grey if unassigned).
                    font_scale = max(.6, thickness * .3)
                    for hand in gestures['hands']:
                        if hand['gesture'] in ('', 'None'):
                            continue
                        color = colors[hand['slot'] - 1] if hand['slot'] else (180, 180, 180)
                        at = (min(w - 1, max(0, int(hand['x'] * w))), min(h - 1, max(0, int(hand['y'] * h))))
                        cv2.putText(frame, hand['gesture'], at, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                    (20, 20, 20), thickness + 2, cv2.LINE_AA)
                        cv2.putText(frame, hand['gesture'], at, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                    color, thickness, cv2.LINE_AA)
                lines = [f'{len(assignments)}/{args.people} assigned']
                if tracker.positions is None:
                    lines = [f'Waiting for {args.people} visible people']
                if hasattr(tracker, 'status'):
                    lines = tracker.status.split(' | ')
                ignored = len(result.pose_landmarks) - len(observations)
                if ignored:
                    part = 'shoulders' if args.upper_body_only else 'shoulders + hips'
                    lines.append(f"{ignored} {'person' if ignored == 1 else 'people'} ignored: show {part}")
                if not args.no_window:
                    lines.append('R: reassign | Q: quit')
                draw_status(frame, lines, thickness, cv2)
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
        sender.sendto(b'{"version":1,"hands":[]}', ('127.0.0.1', args.port + 3))
        sender.close()
        camera.release()
        cv2.destroyAllWindows()


if __name__ == '__main__':
    main()
