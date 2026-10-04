using DigiPhant;
using UnityEngine;

namespace StudentWork
{
    // Read-only overlay: shows each performer's six signals relative to neutral, and whether
    // each is visible (confidence >= Minimum Confidence). Add it to the DigiPhant Controls object.
    [RequireComponent(typeof(DigiPhantController))]
    public class StudentSignalDiagnostics : MonoBehaviour
    {
        public bool show = true;
        static readonly string[] Names = { "L hand", "R hand", "L foot*", "R foot*", "Lean", "Spread" };
        DigiPhantController controller;
        GUIStyle style;

        [Tooltip("Append signals to Logs/digiphant_signals.csv (project root, not in Git) at ~10 Hz while in Camera mode.")]
        public bool logToFile = true;
        System.IO.StreamWriter log;
        float nextLog;

        void Awake() => controller = GetComponent<DigiPhantController>();

        void Update()
        {
            if (!logToFile || controller == null || controller.inputMode != InputMode.Camera) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextLog) return;
            nextLog = now + .1f;
            if (log == null)
            {
                string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Logs/digiphant_signals.csv"));
                bool header = !System.IO.File.Exists(path);
                log = new System.IO.StreamWriter(path, true) { AutoFlush = true };
                if (header) log.WriteLine("time,calibrated,performer,tracked,Lhand,Rhand,Lfoot,Rfoot,Lean,Spread,hidden,action,speed");
            }
            var loco = GetComponent<DigiPhantLocomotion>();
            for (int p = 1; p <= controller.performerCount; p++)
            {
                var line = new System.Text.StringBuilder();
                line.Append(now.ToString("0.00")).Append(',').Append(controller.IsCalibrated ? 1 : 0).Append(',').Append(p)
                    .Append(',').Append(controller.IsTracked(p, now) ? 1 : 0);
                string hidden = "";
                for (int k = 0; k < Names.Length; k++)
                {
                    var m = (Movement)k;
                    line.Append(',').Append(controller.TryReadMovement(p, m, now, out float v) ? v.ToString("0.000") : "");
                    if (!controller.IsMovementVisible(p, m, now)) hidden += k;
                }
                line.Append(',').Append(hidden).Append(',').Append(loco ? loco.CurrentAction.Replace(',', ' ') : "")
                    .Append(',').Append(loco ? loco.CurrentSpeed.ToString("0.00") : "");
                log.WriteLine(line);
            }
        }

        void OnDisable() { log?.Dispose(); log = null; }

        void OnGUI()
        {
            if (!show || controller == null || controller.inputMode != InputMode.Camera) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            float now = Time.realtimeSinceStartup;
            float width = 260, height = 36 + controller.performerCount * (Names.Length * 17 + 22);
            var area = new Rect(Screen.width - width - 10, 10, width, height);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 8, area.y + 4, width - 16, height - 8));
            GUILayout.Label(controller.IsCalibrated ? "<b>Calibrated</b> · value − neutral" : "<b>Not calibrated</b> · set neutral pose", style);
            for (int p = 1; p <= controller.performerCount; p++)
            {
                GUILayout.Label("<b>P" + p + "</b> " + (controller.IsTracked(p, now) ? "tracked" : "<color=#ff8080>lost</color>"), style);
                for (int k = 0; k < Names.Length; k++)
                {
                    var m = (Movement)k;
                    bool visible = controller.IsMovementVisible(p, m, now);
                    string value = controller.TryReadMovement(p, m, now, out float v) ? v.ToString("+0.00;-0.00") : "  —  ";
                    GUILayout.Label("  " + Names[k].PadRight(8) + value + (visible ? "" : "  <color=#ff8080>hidden</color>"), style);
                }
            }
            GUILayout.EndArea();
        }
    }
}
