using UnityEngine;

namespace LevelBuilder
{
    /// <summary>Pure door state machine (no Unity objects): progress 0 closed .. 1 open.</summary>
    public class DoorMotion
    {
        bool _wantOpen;

        public float Progress { get; private set; }
        public int Direction { get; private set; } = 1;
        /// <summary>True while the door is opening or open (target state), false while closing or closed.</summary>
        public bool IsOpen => _wantOpen;
        public bool Locked;

        public void Open(int direction)
        {
            if (Locked) return;
            if (Progress <= 0f) Direction = direction >= 0 ? 1 : -1; // direction only changes when fully closed
            _wantOpen = true;
        }

        public void Close() { _wantOpen = false; }

        /// <summary>Advances by dt; speed = full 0..1 travels per second. Returns true if Progress changed.</summary>
        public bool Step(float dt, float speed, out bool justOpened, out bool justClosed)
        {
            justOpened = false;
            justClosed = false;
            float target = _wantOpen ? 1f : 0f;
            if (Mathf.Approximately(Progress, target)) return false;

            float before = Progress;
            Progress = Mathf.MoveTowards(Progress, target, Mathf.Max(0f, dt) * speed);
            if (Progress >= 1f && before < 1f) justOpened = true;
            if (Progress <= 0f && before > 0f) justClosed = true;
            return !Mathf.Approximately(before, Progress);
        }
    }
}
