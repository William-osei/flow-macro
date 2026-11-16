using System;
using System.Collections.Generic;

namespace FarmMacro
{
    // Detached editing state. Nothing here moves the pointer or sends input.
    public sealed class PointRouteModel
    {
        readonly List<ClickPoint> points = new List<ClickPoint>();
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int SelectedIndex { get; private set; }
        public int Count { get { return points.Count; } }
        public bool HadClampedPoints { get; private set; }

        public PointRouteModel(IEnumerable<ClickPoint> source, int width, int height)
        {
            if (width < 1 || height < 1) throw new ArgumentException("The target needs a visible client area.");
            Width = width; Height = height; SelectedIndex = -1;
            if (source == null) return;
            foreach (ClickPoint point in source)
            {
                if (point == null || point.X < 0 || point.Y < 0) throw new ArgumentException("A click point has invalid coordinates.");
                int x = Clamp(point.X, width), y = Clamp(point.Y, height);
                if (x != point.X || y != point.Y) HadClampedPoints = true;
                Add(x, y);
            }
            if (points.Count > 0) SelectedIndex = 0;
        }
        static int Clamp(int value, int length) { return Math.Max(0, Math.Min(length - 1, value)); }
        void RequireIndex(int index) { if (index < 0 || index >= points.Count) throw new ArgumentOutOfRangeException("index", "Choose a click point first."); }
        public int Add(int x, int y)
        {
            if (points.Count >= QuickClickSettings.MaximumPoints) throw new ArgumentException("Use at most " + QuickClickSettings.MaximumPoints + " click points.");
            points.Add(new ClickPoint { X = Clamp(x, Width), Y = Clamp(y, Height) });
            SelectedIndex = points.Count - 1;
            return SelectedIndex;
        }
        public void Select(int index) { RequireIndex(index); SelectedIndex = index; }
        public ClickPoint GetPoint(int index) { RequireIndex(index); return points[index].Clone(); }
        public void Move(int index, int x, int y)
        {
            RequireIndex(index);
            points[index].X = Clamp(x, Width); points[index].Y = Clamp(y, Height); SelectedIndex = index;
        }
        public void Reorder(int index, int destination)
        {
            RequireIndex(index); RequireIndex(destination);
            ClickPoint point = points[index]; points.RemoveAt(index); points.Insert(destination, point);
            SelectedIndex = destination;
        }
        public void Remove(int index)
        {
            RequireIndex(index); points.RemoveAt(index);
            if (points.Count == 0) SelectedIndex = -1;
            else if (SelectedIndex == index) SelectedIndex = Math.Min(index, points.Count - 1);
            else if (SelectedIndex > index) SelectedIndex--;
        }
        public List<ClickPoint> Snapshot()
        {
            List<ClickPoint> result = new List<ClickPoint>();
            foreach (ClickPoint point in points) result.Add(point.Clone());
            return result;
        }
    }
}
