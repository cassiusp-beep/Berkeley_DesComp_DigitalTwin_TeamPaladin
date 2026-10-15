using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace DigiPhant
{
    // Mac app only: DigiPhantCameraPreview starts bridge.py in the Editor alone, so the built app runs the
    // frozen tracker that tools/build_mac_app.sh copies into Contents/Resources/DigiPhantTracker.
    // One tracker serves every scene; People changes reach it over UDP, so it never needs a restart.
    public class DigiPhantAppTracker : MonoBehaviour
    {
        // The preview's non-Editor LaunchBridge sets exactly this, on first start and after each Retry.
        const string LaunchRequest = "Start the camera bridge on this computer.";
        Process tracker;
        DigiPhantCameraPreview preview;
        bool armed = true, authorized, authorizing;
        string running, log;
        readonly object logLock = new object();

#if !UNITY_EDITOR && UNITY_STANDALONE_OSX
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var host = new GameObject("DigiPhant App Tracker");
            DontDestroyOnLoad(host);
            host.AddComponent<DigiPhantAppTracker>();
        }
#endif

        void Update()
        {
            if (preview == null) preview = FindAnyObjectByType<DigiPhantCameraPreview>();
            if (preview == null) return;
            if (preview.Status != LaunchRequest) { armed = true; return; }
            if (!armed) return;
            armed = false;
            // A scene change re-requests the camera: keep a live tracker unless the model or camera changed.
            string wanted = preview.poseModel + " " + preview.cameraIndex;
            if (tracker != null && !tracker.HasExited && wanted == running) return;
            Stop();
            running = wanted;
            if (authorized) Launch();
            else if (!authorizing) StartCoroutine(AuthorizeThenLaunch());
        }

        // OpenCV in the frozen tracker gives up before macOS shows the camera prompt, so the app asks first.
        // The tracker is the app's child process and inherits the permission once it is granted.
        IEnumerator AuthorizeThenLaunch()
        {
            authorizing = true;
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            var devices = WebCamTexture.devices;
            int index = Mathf.Clamp(preview.cameraIndex, 0, 8);
            var camera = index < devices.Length ? new WebCamTexture(devices[index].name) : new WebCamTexture();
            camera.Play();
            // Up to a minute to click Allow; the first live frame proves access.
            float until = Time.realtimeSinceStartup + 60;
            while (!camera.didUpdateThisFrame && Time.realtimeSinceStartup < until) yield return null;
            authorized = camera.didUpdateThisFrame;
            camera.Stop();
            Destroy(camera);
            if (!authorized) UnityEngine.Debug.LogError("DigiPhant: camera access was not granted; allow DigiPhant in System Settings > Privacy & Security > Camera, then Retry.");
            authorizing = false;
            if (preview != null) Launch();
        }

        void Launch()
        {
            var controller = preview.GetComponent<DigiPhantController>();
            string folder = Path.Combine(Application.dataPath, "Resources", "DigiPhantTracker");
            string executable = Path.Combine(folder, "DigiPhantTracker");
            log = Path.Combine(Application.persistentDataPath, "tracker.log");
            if (!File.Exists(executable)) { UnityEngine.Debug.LogError("DigiPhant tracker missing: " + executable); return; }
            try
            {
                File.WriteAllText(log, "");
                var info = new ProcessStartInfo(executable,
                    "--pose-variant " + preview.poseModel.ToString().ToLowerInvariant() + " --no-window --people " + controller.performerCount +
                    " --camera " + Mathf.Clamp(preview.cameraIndex, 0, 8) + " --port " + controller.port)
                {
                    WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                info.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                tracker = new Process { StartInfo = info };
                tracker.OutputDataReceived += Append;
                tracker.ErrorDataReceived += Append;
                tracker.Start();
                tracker.BeginOutputReadLine();
                tracker.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                tracker?.Dispose(); tracker = null;
                UnityEngine.Debug.LogError("DigiPhant tracker launch failed: " + e.Message);
            }
        }

        void Append(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null) return;
            lock (logLock) File.AppendAllText(log, args.Data + "\n");
        }

        // Stop the tracker with the app, so the camera is free next launch.
        void OnApplicationQuit() => Stop();

        void Stop()
        {
            if (tracker == null) return;
            try { if (!tracker.HasExited) tracker.Kill(); }
            catch (InvalidOperationException) { }
            tracker.Dispose(); tracker = null;
        }
    }
}
