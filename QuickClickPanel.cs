using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace FarmMacro
{
    public sealed class QuickClickPanel : UserControl
    {
        readonly ComboBox button = new ComboBox();
        readonly ComboBox pattern = new ComboBox();
        readonly ComboBox position = new ComboBox();
        readonly NumericUpDown interval = new NumericUpDown();
        readonly NumericUpDown hold = new NumericUpDown();
        readonly NumericUpDown burst = new NumericUpDown();
        readonly NumericUpDown delay = new NumericUpDown();
        readonly NumericUpDown maxClicks = new NumericUpDown();
        readonly NumericUpDown activeSeconds = new NumericUpDown();
        readonly CheckBox indefinite = new CheckBox();
        readonly TextBox x = new TextBox();
        readonly TextBox y = new TextBox();
        readonly Button pick = new ModernButton();
        readonly Button routePick = new ModernButton();
        readonly Button arrange = new ModernButton();
        readonly ClickPointStrip pointStrip = new ClickPointStrip();
        readonly List<ClickPoint> routePoints = new List<ClickPoint>();
        readonly Panel pointHost = new Panel();
        TableLayoutPanel fixedPointRow, routeRow;
        readonly Label summary = new Label();
        readonly Action pointHelp;
        bool binding;
        bool editing = true;
        public string Summary { get { return summary.Text; } }
        public bool UsesMultiplePoints { get { return position.SelectedIndex == 2; } }
        public event EventHandler PointsEditRequested;

        public QuickClickPanel(Action pointHelp)
        {
            this.pointHelp = pointHelp;
            Dock = DockStyle.Fill; BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
            Font = UiTheme.BodyFont; AutoScroll = true; AutoScrollMinSize = new Size(700, 270);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, MinimumSize = new Size(700, 270), Padding = new Padding(10), ColumnCount = 2, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(layout);
            CardPanel clickCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 0, 10, 5) };
            CardPanel runCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 0, 0, 5) };
            layout.Controls.Add(clickCard, 0, 0); layout.Controls.Add(runCard, 1, 0);
            TableLayoutPanel clicks = CardLayout(clickCard, "CLICK BEHAVIOUR", 4);
            TableLayoutPanel run = CardLayout(runCard, "SESSION", 4);

            ConfigureCombo(button, new[] { "Left button", "Right button", "Middle button" });
            ConfigureCombo(pattern, new[] { "Single click", "Double click", "Custom burst" });
            ConfigureCombo(position, new[] { "Current cursor", "Fixed point", "Multiple points" });
            ConfigureNumber(interval, 100, 86400000, 1000);
            ConfigureNumber(hold, 10, 5000, 40);
            ConfigureNumber(burst, 1, 20, 3);
            ConfigureNumber(delay, 1, 30, 3);
            ConfigureNumber(maxClicks, 0, QuickClickSettings.MaximumClicks, 0);
            ConfigureNumber(activeSeconds, 0, 86400, 0);
            clicks.Controls.Add(Pair("Mouse button", button, "Click type", pattern), 0, 1);
            clicks.Controls.Add(Pair("Interval · ms", interval, "Hold · ms", hold), 0, 2);
            clicks.Controls.Add(Pair("Click position", position, "Custom repeats", burst), 0, 3);
            pointHost.Dock = DockStyle.Fill; pointHost.Margin = new Padding(0);
            clicks.Controls.Add(pointHost, 0, 4);
            TableLayoutPanel point = fixedPointRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Padding = new Padding(0, 2, 0, 0), Margin = new Padding(0) };
            point.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18)); point.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            point.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22)); point.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); point.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            point.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            point.Controls.Add(Caption("X"), 0, 0); point.Controls.Add(x, 1, 0); point.Controls.Add(Caption("Y"), 2, 0); point.Controls.Add(y, 3, 0);
            ConfigurePointField(x); ConfigurePointField(y);
            pick.Text = "Pick point · F11"; pick.Dock = DockStyle.Fill; pick.Margin = new Padding(8, 0, 0, 0); UiTheme.StyleButton(pick, false);
            pick.Click += delegate { if (this.pointHelp != null) this.pointHelp(); };
            point.Controls.Add(pick, 4, 0); pointHost.Controls.Add(point);
            routeRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 2, 0, 0), Margin = new Padding(0), Visible = false };
            routeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            routeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            routeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            routeRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            pointStrip.Dock = DockStyle.Fill; pointStrip.Margin = new Padding(0);
            pointStrip.Click += delegate { RequestPointEditor(); };
            routeRow.Controls.Add(pointStrip, 0, 0);
            arrange.Text = "Arrange points"; arrange.Dock = DockStyle.Fill; arrange.Margin = new Padding(8, 0, 0, 0);
            UiTheme.StyleButton(arrange, false); arrange.Click += delegate { RequestPointEditor(); };
            arrange.AccessibleDescription = "Place, move, reorder or remove numbered points over the selected target window.";
            routeRow.Controls.Add(arrange, 1, 0);
            routePick.Text = "+ F11"; routePick.Dock = DockStyle.Fill; routePick.Margin = new Padding(8, 0, 0, 0);
            UiTheme.StyleButton(routePick, true); routePick.Click += delegate { if (this.pointHelp != null) this.pointHelp(); };
            routePick.AccessibleName = "Add a click point with F11";
            routeRow.Controls.Add(routePick, 2, 0); pointHost.Controls.Add(routeRow);

            indefinite.Text = "Run indefinitely"; indefinite.AutoSize = true; indefinite.Checked = true; indefinite.Margin = new Padding(0, 3, 0, 0);
            run.Controls.Add(indefinite, 0, 1);
            run.Controls.Add(Pair("Exact clicks · 0 = no limit", maxClicks, "Active sec · 0 = no limit", activeSeconds), 0, 2);
            run.Controls.Add(Pair("Start delay · sec", delay, "", new Label { Text = "First limit wins.", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }), 0, 3);
            run.Controls.Add(new Label { Text = "Pauses when the target loses focus.", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Padding = new Padding(0, 5, 0, 0) }, 0, 4);

            summary.Dock = DockStyle.Fill; summary.ForeColor = UiTheme.Accent; summary.Font = new Font(UiTheme.BodyFont, FontStyle.Bold);
            summary.TextAlign = ContentAlignment.MiddleLeft; summary.AutoEllipsis = true;
            layout.Controls.Add(summary, 0, 1); layout.SetColumnSpan(summary, 2);
            foreach (NumericUpDown field in new[] { interval, hold, burst, delay, maxClicks, activeSeconds }) field.ValueChanged += delegate { UpdateState(); };
            button.SelectedIndexChanged += delegate { UpdateState(); };
            pattern.SelectedIndexChanged += delegate { UpdateState(); };
            position.SelectedIndexChanged += delegate { UpdateState(); };
            x.TextChanged += delegate { UpdateState(); }; y.TextChanged += delegate { UpdateState(); };
            indefinite.CheckedChanged += delegate {
                if (binding) return;
                binding = true;
                if (indefinite.Checked) { maxClicks.Value = 0; activeSeconds.Value = 0; }
                else if (maxClicks.Value == 0 && activeSeconds.Value == 0) maxClicks.Value = 100;
                binding = false; UpdateState();
            };
            ApplySettings(new QuickClickSettings());
        }

        static TableLayoutPanel CardLayout(CardPanel card, string heading, int rows)
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows + 1, Margin = new Padding(0) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            for (int row = 0; row < rows; row++) layout.RowStyles.Add(new RowStyle(row < rows - 1 ? SizeType.Absolute : SizeType.Percent, row < rows - 1 ? 48 : 100));
            layout.Controls.Add(new Label { Text = heading, Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = UiTheme.ButtonFont, Margin = new Padding(0) }, 0, 0);
            card.Controls.Add(layout); return layout;
        }
        static Control Pair(string firstLabel, Control first, string secondLabel, Control second)
        {
            TableLayoutPanel row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 17)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.Controls.Add(Caption(firstLabel), 0, 0); row.Controls.Add(Caption(secondLabel), 1, 0);
            first.Margin = new Padding(0, 0, 9, 1); second.Margin = new Padding(0, 0, 0, 1);
            first.Dock = DockStyle.Fill; second.Dock = DockStyle.Fill;
            row.Controls.Add(first, 0, 1); row.Controls.Add(second, 1, 1); return row;
        }
        static Label Caption(string text) { return new Label { Text = text, Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Margin = new Padding(0), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }; }
        static void ConfigureCombo(ComboBox combo, string[] choices)
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.Items.AddRange(choices); UiTheme.StyleCombo(combo); combo.SelectedIndex = 0;
        }
        static void ConfigureNumber(NumericUpDown field, decimal minimum, decimal maximum, decimal value)
        {
            field.Minimum = minimum; field.Maximum = maximum; field.Value = value; field.ThousandsSeparator = true; UiTheme.StyleNumber(field);
        }
        static void ConfigurePointField(TextBox field)
        {
            field.Dock = DockStyle.Fill; field.BackColor = UiTheme.SurfaceRaised; field.ForeColor = UiTheme.Text; field.BorderStyle = BorderStyle.FixedSingle; field.Margin = new Padding(0, 1, 0, 0); field.MaxLength = 10;
        }
        public void SetPointShortcut(string shortcut)
        {
            pick.Text = shortcut.Length > 4 ? "Pick point" : "Pick point · " + shortcut; pick.AccessibleName = "Pick point with " + shortcut;
            routePick.Text = shortcut.Length > 4 ? "+ Point" : "+ " + shortcut; routePick.AccessibleName = "Add a click point with " + shortcut;
        }
        void RequestPointEditor() { if (editing && PointsEditRequested != null) PointsEditRequested(this, EventArgs.Empty); }
        public List<ClickPoint> GetPoints()
        {
            List<ClickPoint> points = new List<ClickPoint>();
            foreach (ClickPoint point in routePoints) points.Add(point.Clone());
            return points;
        }
        public void SetPoints(IEnumerable<ClickPoint> points)
        {
            if (points == null) throw new ArgumentNullException("points");
            List<ClickPoint> snapshot = new List<ClickPoint>();
            foreach (ClickPoint point in points)
            {
                if (point == null || point.X < 0 || point.Y < 0) throw new ArgumentException("Choose valid click point coordinates.");
                snapshot.Add(point.Clone());
                if (snapshot.Count > QuickClickSettings.MaximumPoints) throw new ArgumentException("Use at most " + QuickClickSettings.MaximumPoints + " click points.");
            }
            binding = true; routePoints.Clear(); routePoints.AddRange(snapshot); position.SelectedIndex = 2; binding = false; UpdateState();
        }
        public void SetCapturedPoint(int pointX, int pointY)
        {
            if (!UsesMultiplePoints) { SetFixedPoint(pointX, pointY); return; }
            if (pointX < 0 || pointY < 0) throw new ArgumentException("Choose a point inside the selected target.");
            if (routePoints.Count >= QuickClickSettings.MaximumPoints) throw new ArgumentException("Use at most " + QuickClickSettings.MaximumPoints + " click points.");
            routePoints.Add(new ClickPoint { X = pointX, Y = pointY }); UpdateState();
        }
        public void SetFixedPoint(int pointX, int pointY)
        {
            binding = true; position.SelectedIndex = 1; x.Text = pointX.ToString(CultureInfo.InvariantCulture); y.Text = pointY.ToString(CultureInfo.InvariantCulture); binding = false; UpdateState();
        }
        public QuickClickSettings CaptureSettings()
        {
            int pointX, pointY; bool fixedPoint = position.SelectedIndex == 1;
            if (fixedPoint && (!Int32.TryParse(x.Text, NumberStyles.None, CultureInfo.InvariantCulture, out pointX) || !Int32.TryParse(y.Text, NumberStyles.None, CultureInfo.InvariantCulture, out pointY)))
                throw new ArgumentException("Enter both point coordinates, or point inside the target and press the pick-point shortcut.");
            QuickClickSettings settings = new QuickClickSettings {
                Input = button.SelectedIndex == 1 ? "Right click" : button.SelectedIndex == 2 ? "Middle click" : "Left click",
                IntervalMs = (int)interval.Value, HoldMs = (int)hold.Value,
                Burst = pattern.SelectedIndex == 0 ? 1 : pattern.SelectedIndex == 1 ? 2 : (int)burst.Value,
                X = fixedPoint ? (int?)Int32.Parse(x.Text, CultureInfo.InvariantCulture) : null,
                Y = fixedPoint ? (int?)Int32.Parse(y.Text, CultureInfo.InvariantCulture) : null,
                CountdownSeconds = (int)delay.Value, MaxClicks = indefinite.Checked ? 0 : (long)maxClicks.Value,
                SessionSeconds = indefinite.Checked ? 0 : (int)activeSeconds.Value,
                UseMultiplePoints = UsesMultiplePoints, Points = GetPoints()
            };
            settings.Validate(); return settings;
        }
        public void ApplySettings(QuickClickSettings source)
        {
            QuickClickSettings settings = (source ?? new QuickClickSettings()).Clone(); settings.Validate();
            binding = true;
            button.SelectedIndex = settings.Input.Trim().Equals("Right click", StringComparison.OrdinalIgnoreCase) ? 1 : settings.Input.Trim().Equals("Middle click", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            interval.Value = settings.IntervalMs; hold.Value = settings.HoldMs; burst.Value = settings.Burst;
            pattern.SelectedIndex = settings.Burst == 1 ? 0 : settings.Burst == 2 ? 1 : 2;
            delay.Value = settings.CountdownSeconds; maxClicks.Value = settings.MaxClicks; activeSeconds.Value = settings.SessionSeconds;
            indefinite.Checked = settings.MaxClicks == 0 && settings.SessionSeconds == 0;
            routePoints.Clear(); foreach (ClickPoint point in settings.Points) routePoints.Add(point.Clone());
            position.SelectedIndex = settings.UseMultiplePoints ? 2 : settings.X.HasValue ? 1 : 0;
            x.Text = settings.X.HasValue ? settings.X.Value.ToString(CultureInfo.InvariantCulture) : "";
            y.Text = settings.Y.HasValue ? settings.Y.Value.ToString(CultureInfo.InvariantCulture) : "";
            binding = false; UpdateState();
        }
        public void SetEditing(bool enabled) { editing = enabled; UpdateState(); }
        public void AssertLayout()
        {
            AssertControls(this);
        }
        void AssertControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (!control.Visible) continue;
                if (control is ButtonBase || control is ComboBox || control is NumericUpDown || control is TextBox || control is ClickPointStrip)
                {
                    Button textButton = control as Button;
                    if (textButton != null && TextRenderer.MeasureText(textButton.Text, textButton.Font).Width > textButton.ClientSize.Width - textButton.Padding.Horizontal)
                        throw new InvalidOperationException("A mouse-clicker button label is clipped: " + textButton.Text);
                    Rectangle bounds = control.Bounds;
                    Control ancestor = control.Parent;
                    while (ancestor != null)
                    {
                        if (!ancestor.ClientRectangle.Contains(bounds)) throw new InvalidOperationException("A mouse-clicker control is clipped: " + (String.IsNullOrEmpty(control.Text) ? control.GetType().Name : control.Text));
                        if (ancestor == this) break;
                        bounds.Offset(ancestor.Left, ancestor.Top); ancestor = ancestor.Parent;
                    }
                }
                if (!(control is NumericUpDown)) AssertControls(control);
            }
        }
        void UpdateState()
        {
            if (binding) return;
            button.Enabled = pattern.Enabled = interval.Enabled = hold.Enabled = delay.Enabled = position.Enabled = indefinite.Enabled = editing;
            burst.Enabled = editing && pattern.SelectedIndex == 2;
            bool multiple = UsesMultiplePoints;
            fixedPointRow.Visible = !multiple; routeRow.Visible = multiple;
            x.Enabled = y.Enabled = editing && position.SelectedIndex == 1; pick.Enabled = editing;
            arrange.Enabled = pointStrip.Enabled = editing; routePick.Enabled = editing && routePoints.Count < QuickClickSettings.MaximumPoints;
            pointStrip.PointCount = routePoints.Count;
            maxClicks.Enabled = activeSeconds.Enabled = editing && !indefinite.Checked;
            try
            {
                QuickClickSettings settings = CaptureSettings();
                double cycle = Math.Max(settings.IntervalMs, settings.Burst * Math.Max(60, settings.HoldMs + 30));
                string limit = settings.MaxClicks > 0 ? settings.MaxClicks.ToString("N0") + " clicks" : settings.SessionSeconds > 0 ? settings.SessionSeconds + " active seconds" : "until stopped";
                if (settings.MaxClicks > 0 && settings.SessionSeconds > 0) limit += " or " + settings.SessionSeconds + " active sec";
                string rate = (settings.Burst * 1000.0 / cycle).ToString("0.##", CultureInfo.InvariantCulture);
                string location = settings.UseMultiplePoints ? settings.Points.Count + " points in order" : settings.X.HasValue ? "fixed point" : "cursor position";
                summary.Text = settings.UseMultiplePoints && settings.Points.Count == 0 ? "Add your first numbered point with Arrange points or the pick-point shortcut." : "Approx. " + rate + (rate == "1" ? " click / sec" : " clicks / sec") + "  ·  " + location + "  ·  " + limit;
                summary.ForeColor = UiTheme.Accent;
            }
            catch (ArgumentException error) { summary.Text = error.Message; summary.ForeColor = Color.FromArgb(243, 201, 113); }
        }

        sealed class ClickPointStrip : Control
        {
            int count;
            public int PointCount
            {
                get { return count; }
                set { if (count == value) return; count = value; AccessibleName = count + " numbered click points. Arrange or add points."; Invalidate(); }
            }
            public ClickPointStrip()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Font = UiTheme.ButtonFont; TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PushButton;
                AccessibleName = "No numbered click points. Arrange or add points.";
            }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = e.SuppressKeyPress = true; OnClick(EventArgs.Empty); }
                base.OnKeyDown(e);
            }
            protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = Font.Size / UiTheme.ButtonFont.Size;
                int diameter = Math.Min((int)Math.Round(27 * scale), Math.Max(16, Height - 3)), gap = Math.Max(3, (int)Math.Round(5 * scale));
                int slots = Math.Max(1, Width / (diameter + gap));
                int visible = Math.Min(count, Math.Min(3, Math.Max(0, slots - 1)));
                for (int index = 0; index < visible; index++)
                    DrawCircle(e.Graphics, index * (diameter + gap), diameter, count > visible && index == visible - 1 ? "…" : (index + 1).ToString(CultureInfo.InvariantCulture), false);
                DrawCircle(e.Graphics, visible * (diameter + gap), diameter, "+", true);
                if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, UiTheme.Accent, BackColor);
            }
            void DrawCircle(Graphics graphics, int left, int diameter, string text, bool add)
            {
                Rectangle circle = new Rectangle(left + 1, Math.Max(1, (Height - diameter) / 2), diameter - 2, diameter - 2);
                using (Brush fill = new SolidBrush(add && Enabled ? UiTheme.Accent : UiTheme.SurfaceRaised)) graphics.FillEllipse(fill, circle);
                using (Pen border = new Pen(Enabled ? UiTheme.Accent : UiTheme.Border)) graphics.DrawEllipse(border, circle);
                TextRenderer.DrawText(graphics, text, Font, circle, add && Enabled ? UiTheme.Background : Enabled ? UiTheme.Text : UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
