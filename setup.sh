#!/usr/bin/env bash
# One-time setup after cloning (macOS/Linux). Fetches the instructor's DigiPhant
# starter, restores the elephant into Assets, and builds the camera tracker's
# Python environment. Safe to re-run: existing files are kept, not overwritten.
set -euo pipefail

STARTER_URL="https://github.com/kommanderpi/studentstarter.git"
STARTER_COMMIT="5adcbea1f7b311b8bb15c17b3dc4742ae8fff3af"

ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$ROOT/Paladin_Digiphant/Paladin_Digiphant"
STARTER="$PROJECT/DigiPhantStarter"
TRACKING="$STARTER/Tracking"

echo "1/3  Instructor starter"
if [ -d "$STARTER/.git" ]; then
  if [ -n "$(git -C "$STARTER" status --porcelain)" ]; then
    echo "     has local edits, leaving it as is (not updated to ${STARTER_COMMIT:0:7})"
  else
    git -C "$STARTER" fetch -q origin
    git -C "$STARTER" -c advice.detachedHead=false checkout -q "$STARTER_COMMIT"
    echo "     up to date at ${STARTER_COMMIT:0:7}"
  fi
else
  git clone "$STARTER_URL" "$STARTER"
  git -C "$STARTER" -c advice.detachedHead=false checkout "$STARTER_COMMIT"
fi

echo "2/3  Elephant asset"
if [ -d "$PROJECT/Assets/Elephant" ]; then
  echo "     already in Assets, keeping it"
else
  cp -R "$STARTER/Elephant" "$PROJECT/Assets/Elephant"
  cp "$STARTER/Elephant.meta" "$PROJECT/Assets/Elephant.meta"
  echo "     copied into Assets/Elephant"
fi

echo "3/3  Camera tracker Python environment"
PY=""
for candidate in python3.14 python3.13 python3.12 python3.11 python3.10 python3; do
  if command -v "$candidate" >/dev/null 2>&1; then PY="$candidate"; break; fi
done
if [ -z "$PY" ]; then
  echo "     Python 3 not found. Install it (macOS: brew install python@3.14) and re-run." >&2
  exit 1
fi
if [ -x "$TRACKING/.venv/bin/python" ]; then
  echo "     .venv already exists, keeping it"
else
  "$PY" -m venv "$TRACKING/.venv"
fi
"$TRACKING/.venv/bin/python" -m pip install -q --upgrade pip
"$TRACKING/.venv/bin/python" -m pip install -q -r "$TRACKING/requirements.txt"
"$TRACKING/.venv/bin/python" -c "import cv2, mediapipe; print('     MediaPipe', mediapipe.__version__, '/ OpenCV', cv2.__version__)"

echo
echo "Done. Open Paladin_Digiphant/Paladin_Digiphant in Unity Hub, open"
echo "Assets/StudentWork/Scenes/DigiPhant_Student.unity, and press Play."
