using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FarmMacro
{
    /// <summary>Explicit, stopped-only editor for target-relative numbered points.</summary>
    public sealed class PointRouteEditor : Form
    {
        readonly IntPtr target;
        readonly uint targetPid;
        readonly long targetStartTime;
        readonly Process targetProcess;
        readonly PointRouteModel model;
        readonly Timer targetTimer = new Timer();
        readonly List<PointMarker> markers = new List<PointMarker>();
        readonly int markerSize;
        RouteToolbar toolbar;
        Rectangle targetArea;
        List<ClickPoint> result;
        bool finishing;
        public string StatusMessage { get; private set; }
        public List<ClickPoint> ResultPoints
        {
            get
            {
                List<ClickPoint> copy = new List<ClickPoint>();
                if (result != null) foreach (ClickPoint point in result) copy.Add(point.Clone());
                return copy;
            }
        }

        public PointRouteEditor(IntPtr targetWindow, IEnumerable<ClickPoint> points)
        {
            if (targetWindow == IntPtr.Zero) throw new ArgumentException("Choose a target window before arranging points.");
            target = targetWindow;
            if (!ReadClientArea(target, out targetArea)) throw new ArgumentException("Open the target window before arranging points.");
            model = new PointRouteModel(points, targetArea.Width, targetArea.Height);
            GetWindowThreadProcessId(target, out targetPid);
            if (targetPid == 0) throw new ArgumentException("Cannot identify the selected target.");
            try
            {
                targetProcess = Process.GetProcessById((int)targetPid);
                targetStartTime = targetProcess.StartTime.ToUniversalTime().Ticks;
                if (targetStartTime <= 0 || targetProcess.HasExited) throw new ArgumentException("The selected target has closed.");
            }
            catch
            {
                if (targetProcess != null) targetProcess.Dispose();
                throw new ArgumentException("Cannot verify the selected target. Refresh the window list and try again.");
            }
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) markerSize = Math.Max(44, (int)Math.Round(44 * graphics.DpiX / 96f));
            AutoScaleMode = AutoScaleMode.None;
            Text = BrandAssets.AppName + " - Arrange click points";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true; BackColor = Color.Black; Opacity = 0.18;
            Bounds = targetArea; Cursor = Cursors.Cross; KeyPreview = true;
            StatusMessage = "Point editing cancelled. Your saved points were kept.";
            targetTimer.Interval = 120;
            targetTimer.Tick += delegate { CheckTarget(); };
            MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) AddPoint(e.X, e.Y);
            };
            Shown += delegate { BeginEditing(); };
            FormClosing += delegate { finishing = true; targetTimer.Stop(); CloseChildren(); };
        }
        PointRouteEditor(PointRouteModel previewModel)
        {
            model = previewModel;
            markerSize = 44;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x80; return value; }
        }
        void BeginEditing()
        {
            try
            {
                // The user explicitly chose Arrange. Bring the selected content
                // forward once, then let our own editing controls take focus.
                SetForegroundWindow(target);
                IntPtr foreground = GetForegroundWindow();
                if (foreground != target && GetAncestor(foreground, 2) != target)
                {
                    CancelWith("Windows could not bring the selected target forward. Focus it once, then arrange points again.");
                    return;
                }
                if (!CheckTarget()) return;
                toolbar = new RouteToolbar(this);
                toolbar.Show(this);
                PlaceToolbar();
                SynchronizeMarkers();
                if (model.HadClampedPoints) toolbar.SetStatus("Points fitted to this window. Review every number before Save.", true);
                else toolbar.SetStatus("Editing only. The target receives no clicks until you save and start.", false);
                targetTimer.Start();
                toolbar.Activate();
            }
            catch (Exception error) { CancelWith("Could not arrange points: " + error.Message); }
        }
        bool CheckTarget()
        {
            if (finishing) return false;
            Rectangle current;
            uint pid;
            GetWindowThreadProcessId(target, out pid);
            try
            {
                if (pid != targetPid || targetProcess.HasExited || !ReadClientArea(target, out current))
                { CancelWith("Point editing ended because the selected window closed, hid or minimized."); return false; }
                using (Process process = Process.GetProcessById((int)pid))
                    if (process.StartTime.ToUniversalTime().Ticks != targetStartTime)
                    { CancelWith("The selected target process changed. Reopen Arrange points."); return false; }
            }
            catch { CancelWith("Point editing ended because the target could no longer be verified."); return false; }
            if (current.Size != targetArea.Size)
            { CancelWith("The target changed size. Reopen Arrange points and review the positions at the new size."); return false; }
            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && foreground != target && GetAncestor(foreground, 2) != target)
            {
                uint foregroundPid;
                GetWindowThreadProcessId(foreground, out foregroundPid);
                if (foregroundPid != (uint)Process.GetCurrentProcess().Id)
                { CancelWith("Point editing ended because you switched to another app. Your saved points were kept."); return false; }
            }
            if (current.Location != targetArea.Location)
            {
                Point shift = new Point(current.Left - targetArea.Left, current.Top - targetArea.Top);
                targetArea = current; Bounds = current;
                if (toolbar != null) toolbar.Location = KeepVisible(new Point(toolbar.Left + shift.X, toolbar.Top + shift.Y), toolbar.Size);
                PositionMarkers();
            }
            return true;
        }
        void PlaceToolbar()
        {
            if (toolbar == null) return;
            Rectangle area = Screen.FromRectangle(targetArea).WorkingArea;
            int x = targetArea.Left + (targetArea.Width - toolbar.Width) / 2;
            int y = targetArea.Top - toolbar.Height - 8;
            if (y < area.Top) y = targetArea.Top + 16;
            toolbar.Location = KeepVisible(new Point(x, y), toolbar.Size);
        }
        static Point KeepVisible(Point location, Size size)
        {
            Rectangle work = Screen.FromPoint(location).WorkingArea;
            return new Point(Math.Max(work.Left, Math.Min(Math.Max(work.Left, work.Right - size.Width), location.X)),
                Math.Max(work.Top, Math.Min(Math.Max(work.Top, work.Bottom - size.Height), location.Y)));
        }
        public void ActivateControls()
        {
            if (toolbar != null && !toolbar.IsDisposed) { toolbar.BringToFront(); toolbar.Activate(); }
        }
        public void AddAtCursor()
        {
            if (finishing || !CheckTarget()) return;
            Point pointer = Cursor.Position;
            if (!targetArea.Contains(pointer)) { SetStatus("Point inside the selected target, then use the point shortcut.", true); return; }
            if (toolbar != null && toolbar.Bounds.Contains(pointer)) { SetStatus("Move off the editing controls before adding a point.", true); return; }
            foreach (PointMarker marker in markers)
                if (marker.Bounds.Contains(pointer)) { SetStatus("Move off the numbered circles before adding a point.", true); return; }
            AddPoint(pointer.X - targetArea.Left, pointer.Y - targetArea.Top);
        }
        void AddPoint(int x, int y)
        {
            if (finishing) return;
            try
            {
                int index = model.Add(x, y); SynchronizeMarkers();
                SetStatus("Added point " + (index + 1) + ". Drag its circle to move it.", false);
            }
            catch (ArgumentException error) { SetStatus(error.Message, true); }
        }
        void AddCentered()
        {
            int offset = (model.Count % 5) * Math.Max(16, markerSize / 2);
            AddPoint(Math.Min(model.Width - 1, model.Width / 2 + offset), Math.Min(model.Height - 1, model.Height / 2 + offset));
        }
        void SelectPoint(int index)
        {
            if (finishing || index < 0 || index >= model.Count) return;
            model.Select(index); SynchronizeMarkers();
        }
        void MovePoint(int index, Point screenPoint)
        {
            if (finishing || index < 0 || index >= model.Count) return;
            model.Move(index, screenPoint.X - targetArea.Left, screenPoint.Y - targetArea.Top);
            PositionMarkers();
            if (toolbar != null) toolbar.RefreshModel();
        }
        void ReorderSelected(int change)
        {
            int index = model.SelectedIndex, next = index + change;
            if (index < 0 || next < 0 || next >= model.Count) return;
            model.Reorder(index, next); SynchronizeMarkers();
            SetStatus("Point order changed. Playback follows 1, 2, 3 and repeats.", false);
        }
        void RemoveSelected()
        {
            if (model.SelectedIndex < 0) return;
            model.Remove(model.SelectedIndex); SynchronizeMarkers();
            SetStatus("Point removed. The remaining numbers follow their click order.", false);
        }
        void SynchronizeMarkers()
        {
            while (markers.Count > model.Count) { PointMarker last = markers[markers.Count - 1]; markers.RemoveAt(markers.Count - 1); last.Close(); last.Dispose(); }
            while (markers.Count < model.Count)
            {
                PointMarker marker = new PointMarker(this, markerSize);
                markers.Add(marker); marker.Show(this);
            }
            PositionMarkers();
            if (toolbar != null) { toolbar.RefreshModel(); toolbar.BringToFront(); }
        }
        void PositionMarkers()
        {
            for (int index = 0; index < markers.Count && index < model.Count; index++)
            {
                ClickPoint point = model.GetPoint(index);
                PointMarker marker = markers[index];
                marker.PointIndex = index; marker.IsSelected = index == model.SelectedIndex;
                marker.Location = new Point(targetArea.Left + point.X - marker.Width / 2, targetArea.Top + point.Y - marker.Height / 2);
                marker.Invalidate();
            }
        }
        void SetStatus(string message, bool error) { if (toolbar != null) toolbar.SetStatus(message, error); }
        void SavePoints()
        {
            if (!CheckTarget()) return;
            if (model.Count == 0) { SetStatus("Add at least one click point before saving.", true); return; }
            result = model.Snapshot();
            StatusMessage = "Saved " + result.Count + " ordered click point" + (result.Count == 1 ? "" : "s") + ". Playback is stopped.";
            DialogResult = DialogResult.OK; Close();
        }
        public void CancelEditing() { CancelWith("Point editing cancelled. Your saved points were kept."); }
        void CancelWith(string message)
        {
            if (finishing || IsDisposed) return;
            result = null; StatusMessage = message;
            DialogResult = DialogResult.Cancel; Close();
        }
        void CloseChildren()
        {
            foreach (PointMarker marker in markers) { marker.Close(); marker.Dispose(); }
            markers.Clear();
            if (toolbar != null) { toolbar.Close(); toolbar.Dispose(); toolbar = null; }
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape) { CancelEditing(); return true; }
            if (keyData == Keys.Delete) { RemoveSelected(); return true; }
            if (keyData == (Keys.Control | Keys.Up)) { ReorderSelected(-1); return true; }
            if (keyData == (Keys.Control | Keys.Down)) { ReorderSelected(1); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                finishing = true; targetTimer.Stop(); targetTimer.Dispose(); CloseChildren();
                if (targetProcess != null) targetProcess.Dispose();
            }
            base.Dispose(disposing);
        }

        sealed class PointMarker : Form
        {
            readonly PointRouteEditor editor;
            readonly ContextMenuStrip menu = new ContextMenuStrip();
            readonly Font numberFont = new Font("Segoe UI", 12, FontStyle.Bold);
            bool dragging;
            Point dragOffset;
            public int PointIndex;
            public bool IsSelected;
            public PointMarker(PointRouteEditor editor, int size)
            {
                this.editor = editor;
                AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
                ClientSize = new Size(size, size); BackColor = UiTheme.Background; Cursor = Cursors.SizeAll;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                using (GraphicsPath path = new GraphicsPath()) { path.AddEllipse(0, 0, size - 1, size - 1); Region = new Region(path); }
                menu.BackColor = UiTheme.Surface; menu.ForeColor = UiTheme.Text; menu.ShowImageMargin = false;
                menu.Renderer = new ToolStripProfessionalRenderer(new MacroMenuColors());
                menu.Items.Add("Move earlier", null, delegate { editor.SelectPoint(PointIndex); editor.ReorderSelected(-1); });
                menu.Items.Add("Move later", null, delegate { editor.SelectPoint(PointIndex); editor.ReorderSelected(1); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Remove point", null, delegate { editor.SelectPoint(PointIndex); editor.RemoveSelected(); });
                MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    editor.SelectPoint(PointIndex);
                    if (e.Button == MouseButtons.Left)
                    {
                        dragging = true; Capture = true;
                        dragOffset = new Point(e.X - Width / 2, e.Y - Height / 2);
                    }
                    else if (e.Button == MouseButtons.Right)
                    {
                        menu.Items[0].Enabled = PointIndex > 0; menu.Items[1].Enabled = PointIndex + 1 < editor.model.Count;
                        menu.Show(this, e.Location);
                    }
                };
                MouseMove += delegate
                {
                    if (!dragging) return;
                    Point pointer = Cursor.Position;
                    editor.MovePoint(PointIndex, new Point(pointer.X - dragOffset.X, pointer.Y - dragOffset.Y));
                };
                MouseUp += delegate { dragging = false; Capture = false; };
                MouseCaptureChanged += delegate { if (!Capture) dragging = false; };
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams value = base.CreateParams; value.ExStyle |= 0x80 | 0x08000000; return value; }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle ring = new Rectangle(2, 2, Width - 5, Height - 5);
                using (SolidBrush fill = new SolidBrush(IsSelected ? UiTheme.SurfaceRaised : UiTheme.Background)) e.Graphics.FillEllipse(fill, ring);
                using (Pen outline = new Pen(IsSelected ? UiTheme.Accent : UiTheme.Indigo, IsSelected ? 3 : 2)) e.Graphics.DrawEllipse(outline, ring);
                TextRenderer.DrawText(e.Graphics, (PointIndex + 1).ToString(), numberFont, ClientRectangle, UiTheme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                base.OnPaint(e);
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) { menu.Dispose(); numberFont.Dispose(); if (Region != null) { Region region = Region; Region = null; region.Dispose(); } }
                base.Dispose(disposing);
            }
        }

        sealed class RouteToolbar : Form
        {
            readonly PointRouteEditor editor;
            readonly ComboBox selected = new ComboBox();
            readonly Button earlier = new ModernButton();
            readonly Button later = new ModernButton();
            readonly Button remove = new ModernButton();
            readonly Button add = new ModernButton();
            readonly Button save = new ModernButton();
            readonly Label status = new Label();
            readonly Font titleFont = new Font("Segoe UI", 12F, FontStyle.Bold);
            bool binding, dragging;
            Point dragStart, locationStart;
            public RouteToolbar(PointRouteEditor editor)
            {
                this.editor = editor;
                AutoScaleMode = AutoScaleMode.Font; Font = UiTheme.BodyFont;
                Text = BrandAssets.AppName + " - Click point controls";
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
                TopMost = true; StartPosition = FormStartPosition.Manual;
                BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
                ClientSize = new Size(560, 184); KeyPreview = true;
                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10), ColumnCount = 1, RowCount = 5, Margin = new Padding(0) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                Controls.Add(layout);
                Label title = new Label { Text = "Arrange click points  ·  drag here to move", Dock = DockStyle.Fill, Margin = new Padding(0), Font = titleFont, ForeColor = UiTheme.Accent, Cursor = Cursors.SizeAll };
                title.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragStart = Cursor.Position; locationStart = Location; title.Capture = true; } };
                title.MouseMove += delegate { if (dragging) { Point now = Cursor.Position; Location = KeepVisible(new Point(locationStart.X + now.X - dragStart.X, locationStart.Y + now.Y - dragStart.Y), Size); } };
                title.MouseUp += delegate { dragging = false; title.Capture = false; };
                title.MouseCaptureChanged += delegate { if (!title.Capture) dragging = false; };
                layout.Controls.Add(title, 0, 0);
                layout.Controls.Add(new Label { Text = "Click to add  ·  drag numbers  ·  right-click for options  ·  Esc cancels", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Margin = new Padding(0), AutoEllipsis = true }, 0, 1);
                TableLayoutPanel order = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
                order.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                order.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                order.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75)); order.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75)); order.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
                selected.DropDownStyle = ComboBoxStyle.DropDownList; selected.Dock = DockStyle.Fill; selected.Margin = new Padding(0, 2, 9, 3); UiTheme.StyleCombo(selected);
                selected.AccessibleName = "Selected click point";
                selected.SelectedIndexChanged += delegate { if (!binding && selected.SelectedIndex >= 0) editor.SelectPoint(selected.SelectedIndex); };
                order.Controls.Add(selected, 0, 0);
                PrepareButton(earlier, "Earlier", false); earlier.Click += delegate { editor.ReorderSelected(-1); }; order.Controls.Add(earlier, 1, 0);
                PrepareButton(later, "Later", false); later.Click += delegate { editor.ReorderSelected(1); }; order.Controls.Add(later, 2, 0);
                PrepareButton(remove, "Remove", false); remove.Click += delegate { editor.RemoveSelected(); }; order.Controls.Add(remove, 3, 0);
                layout.Controls.Add(order, 0, 2);
                TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
                footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
                PrepareButton(add, "+ Add point", false); add.Click += delegate { editor.AddCentered(); }; footer.Controls.Add(add, 0, 0);
                PrepareButton(save, "Save points", true); save.Click += delegate { editor.SavePoints(); }; footer.Controls.Add(save, 2, 0);
                Button cancel = new ModernButton(); PrepareButton(cancel, "Cancel", false); cancel.Click += delegate { editor.CancelEditing(); }; footer.Controls.Add(cancel, 3, 0);
                layout.Controls.Add(footer, 0, 3);
                status.Dock = DockStyle.Fill; status.Margin = new Padding(0, 3, 0, 0); status.ForeColor = UiTheme.Muted; status.AutoEllipsis = true;
                layout.Controls.Add(status, 0, 4);
                RefreshModel();
            }
            static void PrepareButton(Button button, string label, bool primary)
            { button.Text = label; button.Dock = DockStyle.Fill; button.Margin = new Padding(0, 2, 6, 3); UiTheme.StyleButton(button, primary); }
            public void RefreshModel()
            {
                binding = true; selected.BeginUpdate();
                try
                {
                    selected.Items.Clear();
                    for (int index = 0; index < editor.model.Count; index++)
                    {
                        ClickPoint point = editor.model.GetPoint(index);
                        selected.Items.Add("Point " + (index + 1) + "   ·   " + point.X + ", " + point.Y);
                    }
                    selected.SelectedIndex = editor.model.SelectedIndex;
                }
                finally { selected.EndUpdate(); binding = false; }
                earlier.Enabled = editor.model.SelectedIndex > 0;
                later.Enabled = editor.model.SelectedIndex >= 0 && editor.model.SelectedIndex + 1 < editor.model.Count;
                remove.Enabled = editor.model.Count > 0; save.Enabled = editor.model.Count > 0;
                add.Enabled = editor.model.Count < QuickClickSettings.MaximumPoints;
            }
            public void SetStatus(string message, bool error) { status.Text = message; status.ForeColor = error ? Color.Salmon : UiTheme.Muted; }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override bool ProcessCmdKey(ref Message message, Keys keyData) { return editor.ProcessCmdKey(ref message, keyData) || base.ProcessCmdKey(ref message, keyData); }
            protected override CreateParams CreateParams
            { get { CreateParams value = base.CreateParams; value.ExStyle |= 0x80; return value; } }
            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) titleFont.Dispose();
            }
        }

        public static void RenderPreview(string filePath)
        {
            // A synthetic example of our own controls; no target is captured,
            // focused or controlled. Only our toolbar is rendered offscreen.
            string folder = Path.GetDirectoryName(Path.GetFullPath(filePath));
            Directory.CreateDirectory(folder);
            using (Bitmap toolbarImage = RenderToolbar(1f))
            using (Bitmap bitmap = new Bitmap(960, 600))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Font body = new Font("Segoe UI", 11))
            using (Font numbers = new Font("Segoe UI", 15, FontStyle.Bold))
            {
                graphics.Clear(UiTheme.Background); graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen grid = new Pen(UiTheme.Border))
                {
                    for (int x = 24; x < bitmap.Width; x += 48) graphics.DrawLine(grid, x, 24, x, bitmap.Height - 24);
                    for (int y = 24; y < bitmap.Height; y += 48) graphics.DrawLine(grid, 24, y, bitmap.Width - 24, y);
                }
                graphics.DrawImageUnscaled(toolbarImage, (bitmap.Width - toolbarImage.Width) / 2, 30);
                Point[] points = { new Point(205, 310), new Point(480, 440), new Point(750, 310) };
                using (Pen route = new Pen(UiTheme.Indigo, 2)) { route.DashStyle = DashStyle.Dash; graphics.DrawLines(route, points); }
                for (int index = 0; index < points.Length; index++)
                {
                    Rectangle circle = new Rectangle(points[index].X - 25, points[index].Y - 25, 50, 50);
                    using (SolidBrush fill = new SolidBrush(UiTheme.Surface)) graphics.FillEllipse(fill, circle);
                    using (Pen outline = new Pen(index == 0 ? UiTheme.Accent : UiTheme.Indigo, 3)) graphics.DrawEllipse(outline, circle);
                    TextRenderer.DrawText(graphics, (index + 1).ToString(), numbers, circle, UiTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                TextRenderer.DrawText(graphics, "1 → 2 → 3 → repeat", body, new Rectangle(24, 535, 912, 32), UiTheme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(graphics, "Illustration of the point editor · no target content captured", body, new Rectangle(24, 568, 912, 24), UiTheme.Muted, TextFormatFlags.HorizontalCenter);
                bitmap.Save(filePath, ImageFormat.Png);
            }
            using (Bitmap scaled = RenderToolbar(1.5f))
                scaled.Save(Path.Combine(folder, Path.GetFileNameWithoutExtension(filePath) + "-toolbar-150pct.png"), ImageFormat.Png);
        }

        static Bitmap RenderToolbar(float scale)
        {
            PointRouteModel preview = new PointRouteModel(new[] { new ClickPoint { X = 205, Y = 310 }, new ClickPoint { X = 480, Y = 440 }, new ClickPoint { X = 750, Y = 310 } }, 960, 600);
            using (PointRouteEditor editor = new PointRouteEditor(preview))
            using (RouteToolbar bar = new RouteToolbar(editor))
            {
                List<Font> ownedFonts = new List<Font>();
                try
                {
                    bar.AutoScaleMode = AutoScaleMode.None;
                    bar.Location = new Point(-20000, -20000);
                    bar.SetStatus("Editing only. Save points, then Start to click in order.", false);
                    if (scale != 1f)
                    {
                        Dictionary<Control, Font> originals = new Dictionary<Control, Font>();
                        CollectFonts(bar, originals);
                        bar.Scale(new SizeF(scale, scale));
                        foreach (KeyValuePair<Control, Font> entry in originals)
                        {
                            Font font = new Font(entry.Value.FontFamily, entry.Value.Size * scale, entry.Value.Style);
                            ownedFonts.Add(font); entry.Key.Font = font;
                        }
                        bar.ClientSize = new Size((int)(560 * scale), (int)(184 * scale));
                    }
                    bar.Show(); bar.PerformLayout(); Application.DoEvents();
                    AssertToolbarControls(bar, bar);
                    Bitmap result = new Bitmap(bar.Width, bar.Height);
                    bar.DrawToBitmap(result, new Rectangle(Point.Empty, bar.Size));
                    bar.Hide();
                    return result;
                }
                finally
                {
                    bar.Dispose();
                    foreach (Font font in ownedFonts) font.Dispose();
                }
            }
        }
        static void CollectFonts(Control parent, Dictionary<Control, Font> fonts)
        {
            fonts.Add(parent, parent.Font);
            ContainerControl container = parent as ContainerControl;
            if (container != null) container.AutoScaleMode = AutoScaleMode.None;
            foreach (Control child in parent.Controls) CollectFonts(child, fonts);
        }
        static void AssertToolbarControls(Control parent, Control root)
        {
            foreach (Control child in parent.Controls)
            {
                if (child.Visible && (child is ButtonBase || child is ComboBox))
                {
                    Rectangle bounds = child.Bounds;
                    Control ancestor = child.Parent;
                    while (ancestor != null)
                    {
                        if (!ancestor.ClientRectangle.Contains(bounds)) throw new InvalidOperationException("A point editor toolbar control is clipped: " + child.Text + " " + bounds + " in " + ancestor.GetType().Name + " " + ancestor.ClientRectangle);
                        if (ancestor == root) break;
                        bounds.Offset(ancestor.Left, ancestor.Top); ancestor = ancestor.Parent;
                    }
                    ButtonBase button = child as ButtonBase;
                    if (button != null && TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width > button.ClientSize.Width - button.Padding.Horizontal)
                        throw new InvalidOperationException("A point editor toolbar button label is clipped: " + button.Text);
                }
                AssertToolbarControls(child, root);
            }
        }

        static bool ReadClientArea(IntPtr window, out Rectangle area)
        {
            area = Rectangle.Empty;
            RECT client; POINT origin = new POINT();
            if (!IsWindow(window) || !IsWindowVisible(window) || IsIconic(window) || !GetClientRect(window, out client) || !ClientToScreen(window, ref origin)) return false;
            if (client.Right <= 0 || client.Bottom <= 0) return false;
            area = new Rectangle(origin.X, origin.Y, client.Right, client.Bottom); return true;
        }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out RECT rectangle);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref POINT point);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    }
}
