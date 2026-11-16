using System;
using System.Collections.Generic;

namespace FarmMacro
{
    public sealed class ClickPoint
    {
        public int X;
        public int Y;
        public ClickPoint Clone() { return (ClickPoint)MemberwiseClone(); }
    }

    // One mouse task or an ordered point route. MaxClicks counts individual downs.
    public sealed class QuickClickSettings
    {
        public const long MaximumClicks = MacroEngine.MaximumActions;
        public const int MaximumPoints = 100;
        public string Input = "Left click";
        public int IntervalMs = 1000;
        public int HoldMs = 40;
        public int Burst = 1;
        public int? X;
        public int? Y;
        public bool UseMultiplePoints;
        private List<ClickPoint> points = new List<ClickPoint>();
        public List<ClickPoint> Points
        {
            get { return points; }
            set { points = value ?? new List<ClickPoint>(); }
        }
        public int CountdownSeconds = 3;
        public int SessionSeconds;
        public long MaxClicks;

        public QuickClickSettings Clone()
        {
            QuickClickSettings result = (QuickClickSettings)MemberwiseClone();
            result.Points = new List<ClickPoint>();
            foreach (ClickPoint point in Points) result.Points.Add(point == null ? null : point.Clone());
            return result;
        }

        public void Validate()
        {
            string normalized = NativeInput.NormalizeInput(Input);
            if (normalized == null || !NativeInput.IsMouseInput(normalized))
                throw new ArgumentException("Choose Left click, Right click or Middle click.");
            if (IntervalMs < 100 || IntervalMs > 86400000)
                throw new ArgumentException("Click interval must be 100 to 86400000 ms.");
            if (HoldMs < 10 || HoldMs > 5000 || HoldMs > IntervalMs)
                throw new ArgumentException("Click hold must be 10 to 5000 ms and no longer than the interval.");
            if (Burst < 1 || Burst > 20) throw new ArgumentException("Burst must be 1 to 20 clicks.");
            if (X.HasValue != Y.HasValue || X.HasValue && (X.Value < 0 || Y.Value < 0))
                throw new ArgumentException("Choose both click coordinates, or use the current pointer.");
            if (Points.Count > MaximumPoints) throw new ArgumentException("Use at most " + MaximumPoints + " click points.");
            foreach (ClickPoint point in Points)
                if (point == null || point.X < 0 || point.Y < 0) throw new ArgumentException("Every click point needs nonnegative X and Y coordinates.");
            if (CountdownSeconds < 1 || CountdownSeconds > 30)
                throw new ArgumentException("Countdown must be 1 to 30 seconds.");
            if (SessionSeconds < 0 || SessionSeconds > 86400)
                throw new ArgumentException("Session must be 0 to 86400 seconds; 0 runs indefinitely.");
            if (MaxClicks < 0 || MaxClicks > MaximumClicks)
                throw new ArgumentException("Total clicks must be 0 to " + MaximumClicks + "; 0 runs indefinitely.");
        }

        public MacroAction ToAction()
        {
            Validate();
            if (UseMultiplePoints) throw new ArgumentException("Use ordered playback for multiple click points.");
            return new MacroAction {
                Enabled = true, Input = NativeInput.NormalizeInput(Input), IntervalMs = IntervalMs,
                HoldMs = HoldMs, Burst = Burst, OffsetMs = 0, X = X, Y = Y
            };
        }
    }
}
