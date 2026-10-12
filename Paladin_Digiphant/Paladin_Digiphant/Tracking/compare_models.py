"""Compare model inference on a supplied still image; without --image, only smoke-test loading.
No webcam is opened. Timings on a blank image do not measure tracking quality or live FPS.
"""
import argparse
import json
from pathlib import Path
import statistics
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--image', type=Path)
    parser.add_argument('--runs', type=int, default=8)
    args = parser.parse_args()
    if args.runs < 1:
        parser.error('--runs must be positive')
    import cv2
    import numpy as np
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
    root = Path(__file__).parent
    pixels = cv2.imread(str(args.image)) if args.image else np.zeros((480, 640, 3), dtype=np.uint8)
    if pixels is None:
        parser.error('Could not read image')
    image = mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(pixels, cv2.COLOR_BGR2RGB))
    report = {'input': str(args.image) if args.image else 'blank image: model-load smoke test only', 'models': []}
    for variant in ('lite', 'full', 'heavy'):
        options = vision.PoseLandmarkerOptions(
            base_options=python.BaseOptions(model_asset_path=str(root / f'pose_landmarker_{variant}.task')),
            running_mode=vision.RunningMode.IMAGE, num_poses=4)
        with vision.PoseLandmarker.create_from_options(options) as model:
            model.detect(image)  # warmup
            timings = []
            for _ in range(args.runs):
                start = time.perf_counter()
                result = model.detect(image)
                timings.append((time.perf_counter() - start) * 1000)
            report['models'].append({'model': variant, 'median_ms': round(statistics.median(timings), 2), 'poses': len(result.pose_landmarks)})
    options = vision.GestureRecognizerOptions(
        base_options=python.BaseOptions(model_asset_path=str(root / 'gesture_recognizer.task')),
        running_mode=vision.RunningMode.IMAGE, num_hands=8)
    with vision.GestureRecognizer.create_from_options(options) as model:
        result = model.recognize(image)
        report['gesture_model'] = {'loaded': True, 'hands': len(result.hand_landmarks)}
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
