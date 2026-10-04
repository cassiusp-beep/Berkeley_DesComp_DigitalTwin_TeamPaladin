# One-time setup after cloning (Windows PowerShell). Fetches the instructor's DigiPhant
# starter, restores the elephant into Assets, and builds the camera tracker's
# Python environment. Safe to re-run: existing files are kept, not overwritten.
# Run with:  powershell -ExecutionPolicy Bypass -File .\setup.ps1
$ErrorActionPreference = "Stop"

$StarterUrl = "https://github.com/kommanderpi/studentstarter.git"
$StarterCommit = "5adcbea1f7b311b8bb15c17b3dc4742ae8fff3af"

$Root = $PSScriptRoot
$Project = Join-Path $Root "Paladin_Digiphant\Paladin_Digiphant"
$Starter = Join-Path $Project "DigiPhantStarter"
$Tracking = Join-Path $Starter "Tracking"
$VenvPython = Join-Path $Tracking ".venv\Scripts\python.exe"

Write-Host "1/3  Instructor starter"
if (Test-Path (Join-Path $Starter ".git")) {
    if (git -C $Starter status --porcelain) {
        Write-Host "     has local edits, leaving it as is (not updated to $($StarterCommit.Substring(0,7)))"
    } else {
        git -C $Starter fetch -q origin
        git -C $Starter -c advice.detachedHead=false checkout -q $StarterCommit
        if ($LASTEXITCODE -ne 0) { throw "git checkout failed" }
        Write-Host "     up to date at $($StarterCommit.Substring(0,7))"
    }
} else {
    git clone $StarterUrl $Starter
    if ($LASTEXITCODE -ne 0) { throw "git clone failed" }
    git -C $Starter -c advice.detachedHead=false checkout $StarterCommit
    if ($LASTEXITCODE -ne 0) { throw "git checkout failed" }
}

Write-Host "2/3  Elephant asset"
$Assets = Join-Path $Project "Assets"
if (Test-Path (Join-Path $Assets "Elephant")) {
    Write-Host "     already in Assets, keeping it"
} else {
    Copy-Item -LiteralPath (Join-Path $Starter "Elephant") -Destination (Join-Path $Assets "Elephant") -Recurse
    Copy-Item -LiteralPath (Join-Path $Starter "Elephant.meta") -Destination (Join-Path $Assets "Elephant.meta")
    Write-Host "     copied into Assets\Elephant"
}

Write-Host "3/3  Camera tracker Python environment"
if (Test-Path $VenvPython) {
    Write-Host "     .venv already exists, keeping it"
} else {
    if (Get-Command py -ErrorAction SilentlyContinue) { py -3 -m venv (Join-Path $Tracking ".venv") }
    elseif (Get-Command python -ErrorAction SilentlyContinue) { python -m venv (Join-Path $Tracking ".venv") }
    else { throw "Python 3 not found. Install it from python.org and re-run." }
    if ($LASTEXITCODE -ne 0) { throw "creating .venv failed" }
}
& $VenvPython -m pip install -q --upgrade pip
& $VenvPython -m pip install -q -r (Join-Path $Tracking "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "pip install failed" }
& $VenvPython -c "import cv2, mediapipe; print('     MediaPipe', mediapipe.__version__, '/ OpenCV', cv2.__version__)"

Write-Host ""
Write-Host "Done. Open Paladin_Digiphant\Paladin_Digiphant in Unity Hub, open"
Write-Host "Assets\StudentWork\Scenes\DigiPhant_Student.unity, and press Play."
