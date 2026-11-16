using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FarmMacro
{
    public sealed class InputDiagnosticsForm : Form
    {
        readonly TargetWindow target;
        readonly NativeInput backend;
        readonly Action<KeyboardInputMode> applyMode;
        readonly Action openTestPad;
        readonly SequenceEngine tester;
        readonly Timer timer = new Timer();
        readonly ComboBox input = new ComboBox();
        readonly ComboBox mode = new ComboBox();
        readonly NumericUpDown hold = new NumericUpDown();
        readonly NumericUpDown delay = new NumericUpDown();
        readonly Label status = new Label();
        readonly Label accepted = new Label();
        readonly Button testOnce = new ModernButton();
        readonly Button testTwice = new ModernButton();
        readonly Button testPad = new ModernButton();
        readonly Icon appIcon;
        readonly Image mark;
        readonly bool previewMode;
        bool closing;
        string testDescription = "No input tested yet";

        public bool IsTesting { get { return tester.IsArmed; } }

        public InputDiagnosticsForm(TargetWindow target, NativeInput backend,
            Action<KeyboardInputMode> applyMode, Action openTestPad)
            : this(target, backend, applyMode, openTestPad, false) { }

        public InputDiagnosticsForm(TargetWindow target, NativeInput backend,
            Action<KeyboardInputMode> applyMode, Action openTestPad, bool preview)
        {
            previewMode = preview;
            if (backend == null) throw new ArgumentNullException("backend");
            this.target = target == null ? null : new TargetWindow { Handle = target.Handle, Title = target.Title, ProcessName = target.ProcessName };
            this.backend = backend;
            this.applyMode = applyMode;
            this.openTestPad = openTestPad;
            tester = new SequenceEngine(backend);
            appIcon = BrandAssets.LoadIcon(); Icon = appIcon;
            mark = BrandAssets.CreateMark(96);
            Text = BrandAssets.AppName + " - Input check";
            Size = new Size(700, 560);
            MinimumSize = new Size(620, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            Font = UiTheme.BodyFont;
            BuildLayout();
            timer.Interval = 15;
            timer.Tick += delegate { TickProbe(); };
            if (!previewMode) timer.Start();
            FormClosing += delegate { closing = true; timer.Stop(); tester.Stop(); };
            Disposed += delegate { timer.Dispose(); mark.Dispose(); appIcon.Dispose(); };
            Shown += delegate
            {
                if (previewMode) return;
                Rectangle area = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 16), Math.Min(MinimumSize.Height, area.Height - 16));
                Size = new Size(Math.Min(Width, area.Width - 16), Math.Min(Height, area.Height - 16));
            };
        }

        protected override bool ShowWithoutActivation { get { return previewMode || base.ShowWithoutActivation; } }

        public void RenderPreview(string path, Size size)
        {
            if (!previewMode) throw new InvalidOperationException("Preview mode is required.");
            MinimumSize = Size.Empty;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            Size = size;
            Show(); PerformLayout(); Application.DoEvents();
            using (Bitmap bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            Hide();
        }

        void BuildLayout()
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(layout);

            Panel header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new PictureBox { Image = mark, SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 0), Size = new Size(44, 44) });
            header.Controls.Add(new Label { Text = "Check an input", Location = new Point(56, 0), AutoSize = true, Font = new Font("Segoe UI", 19, FontStyle.Bold) });
            header.Controls.Add(new Label { Text = "Check input before running a full routine.", Location = new Point(58, 36), AutoSize = true, ForeColor = UiTheme.Muted });
            layout.Controls.Add(header, 0, 0);

            CardPanel targetCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 0, 0, 7) };
            targetCard.Controls.Add(new Label { Text = this.target == null ? "No target selected. Close this window and choose a target first." :
                "TARGET   " + this.target.Title + "  (" + this.target.ProcessName + ")", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted });
            layout.Controls.Add(targetCard, 0, 1);

            TableLayoutPanel options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Margin = new Padding(0) };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            options.Controls.Add(Caption("Input"), 0, 0);
            options.Controls.Add(Caption("Keyboard mode"), 1, 0);
            options.Controls.Add(Caption("Hold · ms"), 2, 0);
            options.Controls.Add(Caption("Delay · sec"), 3, 0);
            input.DropDownStyle = ComboBoxStyle.DropDownList;
            input.AccessibleName = "Input to test";
            input.Items.AddRange(new object[] { "Left click", "Right click", "Middle click" });
            foreach (string key in NativeInput.SupportedInputs) if (!NativeInput.IsMouseInput(key)) input.Items.Add(key);
            input.SelectedItem = "Space";
            input.SelectedIndexChanged += delegate { mode.Enabled = !tester.IsArmed && !NativeInput.IsMouseInput(input.SelectedItem as string); };
            UiTheme.StyleCombo(input); input.Dock = DockStyle.Fill; input.Margin = new Padding(0, 0, 10, 0);
            options.Controls.Add(input, 0, 1);
            mode.DropDownStyle = ComboBoxStyle.DropDownList;
            mode.AccessibleName = "Keyboard input mode";
            mode.Items.AddRange(new object[] { "Virtual keys", "Scan codes" });
            UiTheme.StyleCombo(mode);
            mode.Dock = DockStyle.Fill;
            mode.Margin = new Padding(0, 0, 10, 0);
            mode.SelectedIndex = backend.KeyboardMode == KeyboardInputMode.ScanCode ? 1 : 0;
            mode.SelectedIndexChanged += delegate { ApplyKeyboardMode(); };
            options.Controls.Add(mode, 1, 1);
            ConfigureNumber(hold, 10, 1000, 60);
            ConfigureNumber(delay, 1, 10, 3);
            hold.AccessibleName = "Input hold in milliseconds";
            delay.AccessibleName = "Test start delay in seconds";
            options.Controls.Add(hold, 2, 1); options.Controls.Add(delay, 3, 1);
            layout.Controls.Add(options, 0, 2);

            Label instructions = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Padding = new Padding(1, 6, 1, 5),
                Text = "Choose an input, then test it once or twice. Focus the selected window during the countdown. For clicks, place the pointer inside that window and away from its controls. Check the result in the target; an accepted input does not guarantee the target acted on it. Use the test pad for a local check." };
            layout.Controls.Add(instructions, 0, 3);

            CardPanel result = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 8) };
            TableLayoutPanel resultLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            status.Text = "Ready - no test is running";
            status.Dock = DockStyle.Fill; status.AutoEllipsis = true;
            status.Font = new Font(UiTheme.BodyFont, FontStyle.Bold);
            accepted.Text = "No input tested yet. Check the result in the selected window.";
            accepted.Dock = DockStyle.Fill; accepted.ForeColor = UiTheme.Muted; accepted.AutoEllipsis = true;
            resultLayout.Controls.Add(status, 0, 0); resultLayout.Controls.Add(accepted, 0, 1);
            result.Controls.Add(resultLayout); layout.Controls.Add(result, 0, 4);

            FlowLayoutPanel tests = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            ConfigureButton(testOnce, "Test once", true, 132);
            ConfigureButton(testTwice, "Test twice", false, 132);
            Button stop = new ModernButton(); ConfigureButton(stop, "Stop test · F10", false, 142);
            testOnce.Click += delegate { StartProbe(1); };
            testTwice.Click += delegate { StartProbe(2); };
            stop.Click += delegate { StopTest(); };
            tests.Controls.Add(testOnce); tests.Controls.Add(testTwice); tests.Controls.Add(stop);
            layout.Controls.Add(tests, 0, 5);

            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            ConfigureButton(testPad, "Open input test pad", false, 180);
            testPad.Click += delegate
            {
                StopTest();
                IWin32Window owner = Owner;
                Close();
                try { if (openTestPad != null) openTestPad(); }
                catch (Exception error) { MessageBox.Show(owner, "Could not open the input test pad: " + error.Message, BrandAssets.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            Button close = new ModernButton(); ConfigureButton(close, "Close", false, 90);
            close.Click += delegate { Close(); };
            footer.Controls.Add(testPad); footer.Controls.Add(close);
            layout.Controls.Add(footer, 0, 6);
            SetEditing(true);
        }

        static Label Caption(string value) { return new Label { Text = value, AutoSize = true, ForeColor = UiTheme.Muted, Margin = new Padding(0, 0, 5, 0) }; }
        static void ConfigureNumber(NumericUpDown field, int minimum, int maximum, int value)
        {
            field.Minimum = minimum; field.Maximum = maximum; field.Value = value;
            field.Dock = DockStyle.Fill; field.Margin = new Padding(0, 0, 10, 0); UiTheme.StyleNumber(field);
        }
        static void ConfigureButton(Button button, string value, bool primary, int width)
        {
            button.Text = value; button.Size = new Size(width, 34); button.Margin = new Padding(0, 1, 8, 0);
            UiTheme.StyleButton(button, primary);
        }
        void ApplyKeyboardMode()
        {
            if (tester.IsArmed) return;
            KeyboardInputMode selected = mode.SelectedIndex == 1 ? KeyboardInputMode.ScanCode : KeyboardInputMode.VirtualKey;
            try
            {
                backend.KeyboardMode = selected;
                if (applyMode != null) applyMode(selected);
                ShowStatus("Keyboard mode applied. Run one test and watch the target.", false);
            }
            catch (Exception error) { ShowStatus("Could not save the keyboard mode: " + error.Message, true); }
        }
        void StartProbe(int count)
        {
            if (tester.IsArmed || closing) return;
            try
            {
                if (target == null || target.Handle == IntPtr.Zero) throw new ArgumentException("Choose a target window in the main app first.");
                string key = input.SelectedItem as string;
                if (NativeInput.NormalizeInput(key) == null) throw new ArgumentException("Choose an input first.");
                List<MacroStep> steps = new List<MacroStep>();
                for (int index = 0; index < count; index++)
                {
                    steps.Add(new MacroStep { Input = key, IsDown = true, DelayMs = index == 0 ? 0 : 200 });
                    steps.Add(new MacroStep { Input = key, IsDown = false, DelayMs = (int)hold.Value });
                }
                tester.Start(target.Handle, steps, (int)delay.Value * 1000, 1, 100, 5000);
                testDescription = key + " / " + (NativeInput.IsMouseInput(key) ? "mouse" : backend.KeyboardMode == KeyboardInputMode.VirtualKey ? "virtual key" : "scan code") + " / " + hold.Value + " ms";
                SetEditing(false);
                ShowStatus(tester.Status + " - focus the target window", false);
            }
            catch (Exception error) { tester.Stop(); SetEditing(true); ShowStatus(UiTheme.InputFailureSummary(error), true); UiTheme.ShowPermissionDetails(this, error); }
        }
        public void StopTest()
        {
            tester.Stop(); SetEditing(true);
            ShowStatus(tester.Status, tester.Status.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        void TickProbe()
        {
            if (closing) return;
            if (tester.IsArmed && NativeInput.IsEmergencyStopPressed()) { StopTest(); return; }
            bool wasRunning = tester.IsArmed;
            tester.Tick();
            if (wasRunning) ShowStatus(tester.Status, tester.Status.StartsWith("Stopped -", StringComparison.Ordinal));
            if (wasRunning != tester.IsArmed) SetEditing(!tester.IsArmed);
            accepted.Text = testDescription + " | OS-accepted presses: " + tester.TotalActions;
            if (tester.IsArmed) status.ForeColor = tester.IsSending ? UiTheme.Accent : Color.FromArgb(243, 201, 113);
        }
        void SetEditing(bool editing)
        {
            input.Enabled = editing; mode.Enabled = editing && !NativeInput.IsMouseInput(input.SelectedItem as string); hold.Enabled = editing; delay.Enabled = editing;
            testOnce.Enabled = editing && target != null; testTwice.Enabled = editing && target != null;
            testPad.Enabled = editing && openTestPad != null;
        }
        void ShowStatus(string message, bool error)
        {
            status.Text = message; status.ForeColor = error ? Color.FromArgb(255, 171, 171) : UiTheme.Text;
        }
    }
}
