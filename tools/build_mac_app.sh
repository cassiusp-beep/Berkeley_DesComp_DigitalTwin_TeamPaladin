#!/usr/bin/env bash
# Builds Paladin_Digiphant/Paladin_Digiphant/Builds/DigiPhant.app: a Mac app that runs without Unity or Python.
# Step 1 freezes Tracking/bridge.py (Python, MediaPipe, OpenCV, all models) with PyInstaller.
# Step 2 has Unity build the Adventure and Trunk Trail scenes and pack the tracker inside (DigiPhantMacBuild.cs).
# Usage: tools/build_mac_app.sh [--refresh-tracker]. Quit Unity first; with Unity open, run step 2 from DigiPhant > Build Mac App.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
project="$repo/Paladin_Digiphant/Paladin_Digiphant"
work="$project/Builds/Tracker"
unity="/Applications/Unity/Hub/Editor/$(sed -n 's/^m_EditorVersion: //p' "$project/ProjectSettings/ProjectVersion.txt")/Unity.app/Contents/MacOS/Unity"

if [[ "${1:-}" == "--refresh-tracker" ]]; then rm -rf "$work"; fi

tracker="$work/dist/DigiPhantTracker.app"
if [[ ! -x "$tracker/Contents/MacOS/DigiPhantTracker" ]]; then
  echo "== Freezing the tracker (first run downloads packages and models, about 5 minutes)"
  [[ -x "$project/Tracking/.venv/bin/python" ]] || { echo "Run setup.sh first."; exit 1; }
  mkdir -p "$work/models"
  [[ -x "$work/venv/bin/python" ]] || "$project/Tracking/.venv/bin/python" -m venv "$work/venv"
  "$work/venv/bin/python" -m pip install --quiet -r "$project/Tracking/requirements.txt" certifi pyinstaller
  # Bundle every model the menu can pick, so the app never needs the internet.
  "$work/venv/bin/python" - "$project/Tracking" "$work/models" <<'EOF'
import sys
from pathlib import Path
sys.path.insert(0, sys.argv[1])
import bridge
models = Path(sys.argv[2])
urls = {f'pose_landmarker_{v}.task': bridge.MODEL_URL.replace('pose_landmarker_full', f'pose_landmarker_{v}') for v in ('lite', 'full', 'heavy')}
urls['gesture_recognizer.task'] = bridge.GESTURE_MODEL_URL
for name, url in urls.items():
    if not (models / name).exists():
        print('Downloading', name)
        bridge.download_model(url, models / name)
EOF
  data=()
  for model in "$work"/models/*.task; do data+=(--add-data "$model:."); done
  # Its own app bundle, so bridge.py can turn off Center Stage (macOS applies camera effects per app).
  rm -rf "$work/dist" "$work/build"
  "$work/venv/bin/python" -m PyInstaller --noconfirm --windowed --onedir --name DigiPhantTracker \
    --osx-bundle-identifier com.teampaladin.digiphant.tracker \
    --distpath "$work/dist" --workpath "$work/build" --specpath "$work" \
    --collect-all mediapipe "${data[@]}" "$project/Tracking/bridge.py"
  plutil -insert LSUIElement -bool YES "$tracker/Contents/Info.plist"  # no Dock icon
  "$tracker/Contents/MacOS/DigiPhantTracker" --help > /dev/null
  echo "== Tracker frozen: $tracker"
fi

if pgrep -f "Unity.app/Contents/MacOS/Unity" > /dev/null; then
  echo "Unity is open. Finish in Unity with DigiPhant > Build Mac App, or quit Unity and rerun this script."
  exit 0
fi
echo "== Building the Unity app (several minutes; log at $project/Builds/unity_build.log)"
"$unity" -batchmode -quit -projectPath "$project" -buildTarget StandaloneOSX \
  -executeMethod DigiPhant.DigiPhantMacBuild.BuildBatch -logFile "$project/Builds/unity_build.log"
echo "== Done: $project/Builds/DigiPhant.app"
