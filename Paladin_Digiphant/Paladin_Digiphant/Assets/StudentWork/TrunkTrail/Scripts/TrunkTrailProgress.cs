using System.Collections.Generic;
using UnityEngine;

namespace DigiPhant
{
    // Counts the Trunk Trail checkpoints and times the run. HUD bottom-left of the stage, in the DigiPhantUi style
    // (clear of the pose-action bar at the top right and the controller panel on the left).
    // The bonus branch (rolling lane and balance beam) only counts and shows while `optionalLevel` is on.
    // The run ends when the elephant crosses the finish arch (`finishLine`), whatever was missed.
    // Messages stay up for at least MessageSeconds; the hint for the active checkpoint stays up that long too.
    public class TrunkTrailProgress : MonoBehaviour
    {
        public TrunkCheckpoint[] mainCheckpoints = new TrunkCheckpoint[0];
        public TrunkCheckpoint[] bonusCheckpoints = new TrunkCheckpoint[0];
        [Tooltip("Shown only while the optional level is on.")]
        public GameObject bonusRoot;
        [Tooltip("A barrier over the bonus branch, shown only while the optional level is off.")]
        public GameObject closedRoot;
        [Tooltip("The finish arch: crossing it stops the timer.")]
        public TrunkCheckpoint finishLine;
        public bool optionalLevel;
        public bool showHud = true;
        public float messageSeconds = 4.5f;

        float startTime = -1, endTime = -1, messageUntil, hintUntil;
        string message = "", hint = "", finishedText = "";
        readonly Queue<string> queue = new Queue<string>();
        bool appliedBonus, applied;
        DigiPhantController controller;
        DigiPhantPoseActions pose;
        DigiPhantTrunkActions trunk;

        void OnEnable() { applied = false; }

        void ApplyBonus()
        {
            if (applied && appliedBonus == optionalLevel) return;
            applied = true; appliedBonus = optionalLevel;
            if (bonusRoot) bonusRoot.SetActive(optionalLevel);
            if (closedRoot) closedRoot.SetActive(!optionalLevel);
        }

        void Update()
        {
            ApplyBonus();
            if (!Finished && Time.time >= messageUntil && queue.Count > 0) { message = queue.Dequeue(); messageUntil = Time.time + messageSeconds; }
        }
        void OnValidate() { applied = false; }

        public int Total => mainCheckpoints.Length + (optionalLevel ? bonusCheckpoints.Length : 0);
        public int Passed
        {
            get
            {
                int n = 0;
                foreach (var c in mainCheckpoints) if (c && c.State == CheckpointState.Passed) n++;
                if (optionalLevel) foreach (var c in bonusCheckpoints) if (c && c.State == CheckpointState.Passed) n++;
                return n;
            }
        }
        public bool Finished => endTime >= 0;

        // Messages queue up so each one is readable for messageSeconds before the next replaces it.
        public void Notify(string text)
        {
            if (Finished || string.IsNullOrEmpty(text)) return;
            if ((text == message && Time.time < messageUntil) || queue.Contains(text)) return;
            queue.Enqueue(text);
            while (queue.Count > 3) queue.Dequeue();
        }

        public void OnCheckpointChanged(TrunkCheckpoint c)
        {
            if (c.State == CheckpointState.Active && startTime < 0) startTime = Time.time;
            if (c.State == CheckpointState.Passed) Notify("Passed: " + c.label);
            if (c.State == CheckpointState.Missed) Notify("Try again: " + c.label);
        }

        // Called by the finish arch when the elephant crosses it. Misses do not matter.
        public void Finish()
        {
            if (Finished) return;
            endTime = Time.time;
            float e = startTime < 0 ? 0 : endTime - startTime;
            finishedText = $"Finished {Passed} / {Total} in {(int)(e / 60):00}:{(int)(e % 60):00}";
            queue.Clear();
        }

        public void Restart()
        {
            startTime = endTime = -1;
            message = hint = finishedText = "";
            messageUntil = hintUntil = 0;
            queue.Clear();
            foreach (var c in mainCheckpoints) if (c) c.ResetCheckpoint();
            foreach (var c in bonusCheckpoints) if (c) c.ResetCheckpoint();
            if (finishLine) finishLine.ResetCheckpoint();
            if (!trunk) trunk = FindAnyObjectByType<DigiPhantTrunkActions>();
            if (trunk) trunk.ResetProp(); // the carry log goes back to its start pose and is no longer carried
        }

        // The hint of the first active checkpoint; it stays up for messageSeconds after that checkpoint ends.
        string CurrentHint()
        {
            TrunkCheckpoint active = null;
            foreach (var c in mainCheckpoints) if (c && c.State == CheckpointState.Active) { active = c; break; }
            if (!active && optionalLevel) foreach (var c in bonusCheckpoints) if (c && c.State == CheckpointState.Active) { active = c; break; }
            if (active) { hint = active.Hint; hintUntil = Time.time + messageSeconds; }
            else if (Time.time >= hintUntil) hint = Total > 0 && Passed == Total && !Finished ? "All checkpoints passed: walk through the finish arch" : "";
            return hint;
        }

        static string Clock(float seconds) => $"{(int)(seconds / 60)}:{seconds % 60:00.0}";

        void OnGUI()
        {
            if (!showHud) return;
            GUI.depth = -10;
            float Px(float v) => DigiPhantUi.Px(v);
            if (!controller)
            {
                controller = FindAnyObjectByType<DigiPhantController>();
                if (controller) { pose = controller.GetComponent<DigiPhantPoseActions>(); trunk = controller.GetComponent<DigiPhantTrunkActions>(); }
            }
            float left = (controller && controller.showControls ? DigiPhantUi.PanelWidth : 0) + Px(12);
            var label = DigiPhantUi.Text(DigiPhantUi.Small, DigiPhantUi.Muted, FontStyle.Bold);
            var value = DigiPhantUi.Text(DigiPhantUi.Body, DigiPhantUi.Ink, FontStyle.Bold);
            var button = DigiPhantUi.Button();
            float elapsed = startTime < 0 ? 0 : (endTime >= 0 ? endTime : Time.time) - startTime;
            string count = Passed + " / " + Total, clock = Clock(elapsed);
            string bonusText = "Optional level: " + (optionalLevel ? "On" : "Off"), restartText = "Restart";
            float h = Px(40), pad = Px(14), gap = Px(8);
            float bw(string s) => button.CalcSize(new GUIContent(s)).x + Px(8);
            float lw(string s) => label.CalcSize(new GUIContent(s + "  ")).x;
            float cw = Mathf.Max(Px(44), value.CalcSize(new GUIContent(count)).x), tw = Mathf.Max(Px(52), value.CalcSize(new GUIContent(clock)).x);
            float w = pad + lw("Checkpoints") + cw + Px(21) + lw("Time") + tw + Px(21) + bw(bonusText) + gap + bw(restartText) + pad;
            var bar = new Rect(left, Screen.height - h - Px(12), w, h);
            DigiPhantUi.Panel(bar);

            float x = bar.x + pad;
            GUI.Label(new Rect(x, bar.y, lw("Checkpoints"), h), "Checkpoints", label); x += lw("Checkpoints");
            GUI.Label(new Rect(x, bar.y, cw, h), count, value); x += cw;
            GUI.DrawTexture(new Rect(x + Px(10), bar.y + Px(10), 1, h - Px(20)), DigiPhantUi.Light); x += Px(21);
            GUI.Label(new Rect(x, bar.y, lw("Time"), h), "Time", label); x += lw("Time");
            GUI.Label(new Rect(x, bar.y, tw, h), clock, value); x += tw;
            GUI.DrawTexture(new Rect(x + Px(10), bar.y + Px(10), 1, h - Px(20)), DigiPhantUi.Light); x += Px(21);
            float by = bar.y + (h - Px(DigiPhantUi.ControlHeight)) / 2;
            if (GUI.Button(new Rect(x, by, bw(bonusText), Px(DigiPhantUi.ControlHeight)), bonusText, DigiPhantUi.Button(DigiPhantUi.Body, optionalLevel))) optionalLevel = !optionalLevel;
            x += bw(bonusText) + gap;
            if (GUI.Button(new Rect(x, by, bw(restartText), Px(DigiPhantUi.ControlHeight)), restartText, button)) Restart();

            // Stacked upward from the bar: test buttons (slider mode only), the hint, then the message.
            float top = bar.y, margin = Px(8);
            if (controller && controller.inputMode == InputMode.TestSliders)
            {
                string jumpText = "Test jump", grabText = "Test pick up/drop";
                float tw2 = pad + bw(jumpText) + gap + bw(grabText) + pad;
                var row = new Rect(bar.x, top - h - margin, tw2, h);
                DigiPhantUi.Panel(row);
                float tx = row.x + pad, ty = row.y + (h - Px(DigiPhantUi.ControlHeight)) / 2;
                if (GUI.Button(new Rect(tx, ty, bw(jumpText), Px(DigiPhantUi.ControlHeight)), jumpText, button) && pose) pose.TestJump();
                tx += bw(jumpText) + gap;
                if (GUI.Button(new Rect(tx, ty, bw(grabText), Px(DigiPhantUi.ControlHeight)), grabText, button) && trunk) trunk.TestToggle();
                top = row.y;
            }
            string hintText = Finished ? "" : CurrentHint();
            if (hintText.Length > 0)
            {
                var st = DigiPhantUi.Text(DigiPhantUi.Body, DigiPhantUi.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
                float mw = st.CalcSize(new GUIContent(hintText)).x + Px(28), mh = Px(34);
                var box = new Rect(bar.x, top - mh - margin, mw, mh);
                DigiPhantUi.Panel(box);
                GUI.Label(box, hintText, st);
                top = box.y;
            }
            string shown = Finished ? finishedText : (Time.time < messageUntil ? message : "");
            if (shown.Length > 0)
            {
                var big = DigiPhantUi.Text(DigiPhantUi.Title, DigiPhantUi.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
                float mw = Mathf.Max(Px(180), big.CalcSize(new GUIContent(shown)).x + Px(28)), mh = Px(36);
                var box = new Rect(bar.x, top - mh - margin, mw, mh);
                DigiPhantUi.Panel(box);
                GUI.Label(box, shown, big);
            }
        }
    }
}
