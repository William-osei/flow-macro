using System;
using System.Collections.Generic;
using FarmMacro;

static class PointRouteTests
{
    static int passed;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Run(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static bool Rejected(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
    public static int Main()
    {
        try
        {
            Run("Point editor starts detached and warns when fitting an old window size", delegate {
                List<ClickPoint> source = new List<ClickPoint> { new ClickPoint { X = 10, Y = 20 }, new ClickPoint { X = 400, Y = 500 } };
                PointRouteModel model = new PointRouteModel(source, 200, 100);
                Check(model.Count == 2 && model.HadClampedPoints && model.GetPoint(1).X == 199 && model.GetPoint(1).Y == 99, "old points were not fitted into draft");
                Check(source[1].X == 400 && source[1].Y == 500, "editing changed the saved source");
                source[0].X = 150; Check(model.GetPoint(0).X == 10, "source mutation leaked into draft");
            });
            Run("Adding selects the new numbered point and clamps the client boundary", delegate {
                PointRouteModel model = new PointRouteModel(null, 320, 200);
                Check(model.SelectedIndex == -1, "empty draft was selected");
                model.Add(40, 80); model.Add(-8, 900);
                Check(model.SelectedIndex == 1 && model.Count == 2 && model.GetPoint(1).X == 0 && model.GetPoint(1).Y == 199, "addition boundary or order");
            });
            Run("Moving a marker retains count and safely clamps its centre", delegate {
                PointRouteModel model = new PointRouteModel(new[] { new ClickPoint { X = 20, Y = 30 }, new ClickPoint { X = 60, Y = 70 } }, 200, 100);
                model.Move(0, 1000, -100);
                Check(model.Count == 2 && model.SelectedIndex == 0 && model.GetPoint(0).X == 199 && model.GetPoint(0).Y == 0 && model.GetPoint(1).X == 60, "marker movement changed another point");
            });
            Run("Earlier and later change actual playback order while selecting the moved point", delegate {
                PointRouteModel model = new PointRouteModel(new[] { new ClickPoint { X = 10 }, new ClickPoint { X = 20 }, new ClickPoint { X = 30 } }, 100, 100);
                model.Reorder(2, 0); Check(model.SelectedIndex == 0 && model.GetPoint(0).X == 30 && model.GetPoint(1).X == 10 && model.GetPoint(2).X == 20, "move earlier");
                model.Reorder(0, 2); Check(model.SelectedIndex == 2 && model.GetPoint(0).X == 10 && model.GetPoint(2).X == 30, "move later");
            });
            Run("Removing renumbers points and keeps a valid neighbouring selection", delegate {
                PointRouteModel model = new PointRouteModel(new[] { new ClickPoint { X = 10 }, new ClickPoint { X = 20 }, new ClickPoint { X = 30 } }, 100, 100);
                model.Select(2); model.Remove(0); Check(model.SelectedIndex == 1 && model.GetPoint(1).X == 30, "selection after earlier removal");
                model.Remove(1); Check(model.SelectedIndex == 0 && model.Count == 1, "selection after selected removal");
                model.Remove(0); Check(model.Count == 0 && model.SelectedIndex == -1, "empty selection");
            });
            Run("Snapshots and individual points cannot mutate a saved draft", delegate {
                PointRouteModel model = new PointRouteModel(new[] { new ClickPoint { X = 12, Y = 34 } }, 100, 100);
                List<ClickPoint> copy = model.Snapshot(); copy[0].X = 77; copy.Clear();
                ClickPoint point = model.GetPoint(0); point.Y = 88;
                Check(model.Count == 1 && model.GetPoint(0).X == 12 && model.GetPoint(0).Y == 34, "snapshot leaked mutable state");
            });
            Run("Point count and invalid rows reject before changing editor state", delegate {
                PointRouteModel model = new PointRouteModel(null, 100, 100);
                for (int index = 0; index < QuickClickSettings.MaximumPoints; index++) model.Add(index, index);
                Check(Rejected(delegate { model.Add(1, 1); }) && model.Count == QuickClickSettings.MaximumPoints, "point cap");
                Check(Rejected(delegate { model.Reorder(0, 100); }) && model.GetPoint(0).X == 0, "invalid reordering mutated state");
                Check(Rejected(delegate { new PointRouteModel(new ClickPoint[] { null }, 100, 100); }) && Rejected(delegate { new PointRouteModel(null, 0, 100); }), "invalid draft shape");
            });
            Console.WriteLine(passed + " point editor model checks passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
