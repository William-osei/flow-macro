using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FarmMacro
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--test-pad") Application.Run(new InputTestPad());
            else if (args.Length > 1 && args[0] == "--preview")
            {
                try { using (MainForm preview = new MainForm(true)) preview.RenderPreviews(args[1]); }
                catch (Exception error)
                {
                    try { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "preview-error.txt"), error.ToString()); } catch { }
                    Environment.ExitCode = 1;
                }
            }
            else
            {
                bool created;
                System.Threading.Mutex instance;
                try { instance = new System.Threading.Mutex(true, @"Local\FarmMacro.Main.v1", out created); }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show("Another Flow Macro copy may be running with different Windows privileges. Close the existing copy first, then launch Flow Macro again. If your target runs as administrator, use Start Farm Macro as Administrator.cmd after closing the app.", "Flow Macro is already open", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (instance)
                {
                    if (!created) { AppInstance.ShowExisting(); return; }
                    try { Application.Run(new MainForm()); }
                    finally { instance.ReleaseMutex(); }
                }
            }
        }
    }

    public class SavedProfile
    {
        public int Version = 1;
        public int CountdownSeconds = 3;
        public int SessionSeconds;
        public List<MacroAction> Actions = new List<MacroAction>();
    }

    sealed class MainForm : Form
    {
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        const int ToggleHotkey = 1, StopHotkey = 2, PointHotkey = 3, RecordHotkey = 4;
        const int ClickerMode = 0, RoutineMode = 1, ReplayMode = 2, ProfilesMode = 3, DevicesMode = 4;
        readonly Color background = UiTheme.Background;
        readonly Color surface = UiTheme.Surface;
        readonly Color textColor = UiTheme.Text;
        readonly Color muted = UiTheme.Muted;
        readonly NativeInput native = new NativeInput();
        readonly MacroEngine engine;
        readonly ComboBox targets = new ComboBox();
        readonly ComboBox presets = new ComboBox();
        readonly DataGridView grid = new DataGridView();
        readonly ModernTabControl modes = new ModernTabControl { AutoFitTabs = true };
        QuickClickPanel quickClickPanel;
        SequencePanel sequencePanel;
        ProfilePanel profilePanel;
        WindowsDevicesPanel devicesPanel;
        InputDiagnosticsForm inputDiagnostics;
        readonly NumericUpDown countdown = new NumericUpDown();
        readonly NumericUpDown session = new NumericUpDown();
        readonly CheckBox indefiniteTimers = new CheckBox();
        readonly Label stateLabel = new Label();
        readonly Label statsLabel = new Label();
        readonly Label hotkeyLabel = new Label();
        readonly Label hint = new Label();
        readonly Button startButton = new ModernButton { Primary = true };
        readonly Button stopButton = new ModernButton();
        readonly Timer timer = new Timer();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolTip tips = new ToolTip();
        ContextMenuStrip trayMenu;
        ToolStripMenuItem trayToggle;
        ToolStripMenuItem trayRecord;
        Icon brandIcon;
        Image brandMark;
        int lastTrayUpdate;
        readonly List<Button> editButtons = new List<Button>();
        readonly List<int> hotkeys = new List<int>();
        string toggleLabel = "F9", stopLabel = "F10", pointLabel = "F11", recordLabel = "F8";
        string lastStatus = "";
        string observedEngineStatus;
        int activeMode = ClickerMode;
        bool closing;
        bool layoutReady;
        IntPtr registrationHandle;
        string profilePath;
        readonly bool previewMode;

        public MainForm() : this(false) { }
        public MainForm(bool preview)
        {
            previewMode = preview;
            engine = new MacroEngine(native);
            profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FarmMacro", "profile.json");
            Text = BrandAssets.AppName + " " + BrandAssets.Version + " - Clicks & routines";
            brandIcon = BrandAssets.LoadIcon();
            Icon = brandIcon;
            Size = new Size(1080, 820);
            MinimumSize = new Size(900, 690);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = background;
            ForeColor = textColor;
            Font = new Font("Segoe UI", 10F);
            BuildLayout();
            ApplyKeyboardMode(InputPreferences.Load().KeyboardMode, false);
            if (!previewMode) SetupTray();
            layoutReady = true;
            RefreshTargets();
            ApplyPreset("Keyboard repeat");
            if (!previewMode && File.Exists(profilePath)) LoadProfile(false);
            modes.SelectedIndex = ClickerMode;
            ShowStatus("Choose a target window, then start clicking when you are ready.", false);
            timer.Interval = 15;
            timer.Tick += delegate { TickEngine(); };
            if (!previewMode) timer.Start();
            FormClosing += delegate { closing = true; timer.Stop(); if (inputDiagnostics != null && !inputDiagnostics.IsDisposed) inputDiagnostics.StopTest(); engine.Stop(); sequencePanel.Stop(); };
            Disposed += delegate
            {
                tray.Visible = false; tray.Dispose(); tips.Dispose();
                if (trayMenu != null) trayMenu.Dispose();
                if (brandMark != null) brandMark.Dispose();
                if (brandIcon != null) brandIcon.Dispose();
            };
            Shown += delegate
            {
                if (previewMode) return;
                Rectangle area = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 16), Math.Min(MinimumSize.Height, area.Height - 16));
                Size = new Size(Math.Min(Width, area.Width - 16), Math.Min(Height, area.Height - 16));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            };
        }

        void BuildLayout()
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(24, 18, 24, 14);
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            Controls.Add(layout);

            Panel header = new Panel { Dock = DockStyle.Fill };
            brandMark = BrandAssets.CreateMark(96);
            header.Controls.Add(new PictureBox { Image = brandMark, SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 4), Size = new Size(48, 48), AccessibleName = "Flow Macro app mark" });
            header.Controls.Add(new Label { Text = BrandAssets.AppName, Location = new Point(58, 0), AutoSize = true, Font = new Font("Segoe UI", 24, FontStyle.Bold) });
            header.Controls.Add(new Label { Text = "Automate clicks, keys and repeatable tasks.", Location = new Point(60, 45), AutoSize = true, ForeColor = muted });
            Button help = MakeButton("Quick help", delegate { new HelpForm(hotkeyLabel.Text).Show(this); });
            help.Size = new Size(112, 34);
            help.Location = new Point(870, 8);
            help.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.Add(help);
            header.Resize += delegate { help.Left = Math.Max(0, header.ClientSize.Width - help.Width); };
            layout.Controls.Add(header, 0, 0);

            CardPanel targetCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 4), Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel targetRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0), BackColor = surface };
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            targetRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            targetRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            targetRow.Controls.Add(new Label { Text = "TARGET APPLICATION", ForeColor = muted, AutoSize = true }, 0, 0);
            targets.DropDownStyle = ComboBoxStyle.DropDownList;
            targets.Dock = DockStyle.Fill;
            UiTheme.StyleCombo(targets);
            targetRow.Controls.Add(targets, 0, 1);
            Button refresh = MakeButton("Refresh", delegate { RefreshTargets(); });
            refresh.Dock = DockStyle.Fill;
            targetRow.Controls.Add(refresh, 1, 1);
            editButtons.Add(refresh);
            targetCard.Controls.Add(targetRow);
            layout.Controls.Add(targetCard, 0, 1);

            modes.Dock = DockStyle.Fill;
            UiTheme.StyleTabs(modes);
            TabPage mouse = new TabPage("Mouse clicker") { BackColor = background, Padding = new Padding(5) };
            TabPage repeated = new TabPage("Routines") { BackColor = background, Padding = new Padding(5) };
            TabPage recorded = new TabPage("Record & replay") { BackColor = background, Padding = new Padding(5) };
            TabPage library = new TabPage("Profiles") { BackColor = background, Padding = new Padding(5) };
            TabPage devices = new TabPage("Devices") { BackColor = background, Padding = new Padding(5) };
            TableLayoutPanel repeatedLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = new Padding(0) };
            repeatedLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            repeatedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            repeatedLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            repeatedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            repeatedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            repeated.Controls.Add(repeatedLayout);
            quickClickPanel = new QuickClickPanel(delegate { ShowStatus("Point inside the selected target, then press " + pointLabel + " while stopped to save its position.", false); });
            mouse.Controls.Add(quickClickPanel);
            sequencePanel = new SequencePanel(delegate { return targets.SelectedItem as TargetWindow; }, delegate { return engine.IsArmed || (inputDiagnostics != null && !inputDiagnostics.IsDisposed); });
            sequencePanel.Dock = DockStyle.Fill;
            recorded.Controls.Add(sequencePanel);
            profilePanel = new ProfilePanel(CaptureWorkspace, ApplyWorkspace, delegate { return engine.IsArmed || sequencePanel.IsBusy; });
            profilePanel.Dock = DockStyle.Fill;
            library.Controls.Add(profilePanel);
            devicesPanel = new WindowsDevicesPanel(CaptureMobileTimers, delegate { return engine.IsArmed || sequencePanel.IsBusy || (inputDiagnostics != null && !inputDiagnostics.IsDisposed); });
            devicesPanel.Dock = DockStyle.Fill;
            devices.Controls.Add(devicesPanel);
            modes.TabPages.Add(mouse); modes.TabPages.Add(repeated); modes.TabPages.Add(recorded);
            modes.TabPages.Add(library);
            modes.TabPages.Add(devices);
            modes.SelectedIndexChanged += delegate
            {
                if (engine.IsArmed && modes.SelectedIndex != activeMode) modes.SelectedIndex = activeMode;
                else if (sequencePanel.IsBusy && modes.SelectedIndex != ReplayMode) modes.SelectedIndex = ReplayMode;
                if (modes.SelectedIndex == ProfilesMode) profilePanel.RefreshProfiles();
                else if (modes.SelectedIndex < ReplayMode && !engine.IsArmed && !sequencePanel.IsBusy)
                    ShowStatus("Stopped - ready when you are", false);
            };
            layout.Controls.Add(modes, 0, 2);

            TableLayoutPanel toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            FlowLayoutPanel firstTools = Flow(); firstTools.WrapContents = false;
            FlowLayoutPanel secondTools = Flow(); secondTools.WrapContents = false;
            presets.DropDownStyle = ComboBoxStyle.DropDownList;
            presets.Items.AddRange(new object[] { "Keyboard repeat", "Click repeat", "Multiple click points" });
            presets.Width = 208;
            presets.Margin = new Padding(0, 4, 8, 0);
            presets.SelectedIndex = 0;
            UiTheme.StyleCombo(presets);
            firstTools.Controls.Add(presets);
            Button apply = MakeButton("Apply", delegate { ApplyPreset((string)presets.SelectedItem); });
            Button add = MakeButton("+ Key", delegate { AddRow(new MacroAction { Enabled = true, Input = "Space", IntervalMs = 1000, HoldMs = 60, Burst = 1 }); });
            Button addClick = MakeButton("+ Click", delegate { AddRow(new MacroAction { Enabled = true, Input = "Left click", IntervalMs = 1000, HoldMs = 40, Burst = 1 }); });
            Button remove = MakeButton("Remove", delegate { if (grid.CurrentRow != null) grid.Rows.Remove(grid.CurrentRow); });
            firstTools.Controls.Add(apply); firstTools.Controls.Add(add); firstTools.Controls.Add(addClick);
            Button duplicate = MakeButton("Duplicate", delegate { DuplicateAction(); });
            secondTools.Controls.Add(remove); secondTools.Controls.Add(duplicate); editButtons.Add(duplicate);
            Button saveTimers = MakeButton("Save", delegate { SaveProfile(); });
            Button loadTimers = MakeButton("Load", delegate { LoadProfile(true); });
            secondTools.Controls.Add(saveTimers); secondTools.Controls.Add(loadTimers);
            editButtons.Add(loadTimers);
            editButtons.AddRange(new Button[] { apply, add, addClick, remove, saveTimers });
            toolbar.Controls.Add(firstTools, 0, 0); toolbar.Controls.Add(secondTools, 0, 1);
            repeatedLayout.Controls.Add(toolbar, 0, 0);

            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = surface;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.MultiSelect = false;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(42, 50, 66), ForeColor = textColor, Font = new Font(Font, FontStyle.Bold), SelectionBackColor = Color.FromArgb(42, 50, 66) };
            grid.ColumnHeadersHeight = 38;
            grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = surface, ForeColor = textColor, SelectionBackColor = Color.FromArgb(50, 82, 119), SelectionForeColor = Color.White, Padding = new Padding(5) };
            grid.RowTemplate.Height = 40;
            grid.GridColor = Color.FromArgb(50, 58, 72);
            DataGridViewCheckBoxColumn enabledColumn = new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "On", FillWeight = 38 };
            enabledColumn.HeaderCell.Style.Padding = Padding.Empty;
            grid.Columns.Add(enabledColumn);
            DataGridViewComboBoxColumn input = new DataGridViewComboBoxColumn { Name = "Input", HeaderText = "Key / click", FillWeight = 110, FlatStyle = FlatStyle.Flat };
            foreach (string key in NativeInput.SupportedInputs) input.Items.Add(key);
            grid.Columns.Add(input);
            DataGridViewTextBoxColumn intervalColumn = new DataGridViewTextBoxColumn { Name = "Interval", HeaderText = "Every s", FillWeight = 85 };
            intervalColumn.HeaderCell.ToolTipText = "Requested repeat interval in seconds. Each row has its own timer.";
            grid.Columns.Add(intervalColumn);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hold", HeaderText = "Hold ms", FillWeight = 78 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Burst", HeaderText = "Repeats", FillWeight = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "X", HeaderText = "Click X", FillWeight = 65 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Y", HeaderText = "Click Y", FillWeight = 65 });
            DataGridViewTextBoxColumn offsetColumn = new DataGridViewTextBoxColumn { Name = "Offset", HeaderText = "First s", FillWeight = 100 };
            offsetColumn.HeaderCell.ToolTipText = "Delay before the first press, in seconds. Normal rows also use it after a focus pause; cooldown rows retain their next due time after a press.";
            grid.Columns.Add(offsetColumn);
            DataGridViewCheckBoxColumn cooldown = new DataGridViewCheckBoxColumn { Name = "KeepCooldown", HeaderText = "Keep CD", FillWeight = 78 };
            string cooldownHelp = "Keep this row's cooldown deadline after focus or held-key pauses. Time away still counts. Requires Repeats = 1; stopping and starting resets the deadline.";
            cooldown.HeaderCell.ToolTipText = cooldownHelp;
            cooldown.CellTemplate.ToolTipText = cooldownHelp;
            grid.Columns.Add(cooldown);
            UiTheme.StyleGrid(grid);
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; ShowStatus("Invalid cell value. Choose a supported input.", true); };
            repeatedLayout.Controls.Add(grid, 0, 1);

            hint.Dock = DockStyle.Fill; hint.ForeColor = muted; hint.Padding = new Padding(0, 4, 0, 0);
            repeatedLayout.Controls.Add(hint, 0, 2);

            FlowLayoutPanel options = Flow();
            options.Controls.Add(new Label { Text = "Start delay", AutoSize = true, Margin = new Padding(0, 12, 8, 0), ForeColor = muted });
            StyleNumber(countdown, 1, 30, 3);
            options.Controls.Add(countdown);
            options.Controls.Add(new Label { Text = "sec", AutoSize = true, Margin = new Padding(2, 12, 24, 0), ForeColor = muted });
            options.Controls.Add(new Label { Text = "Stop after", AutoSize = true, Margin = new Padding(0, 12, 8, 0), ForeColor = muted });
            StyleNumber(session, 0, 86400, 0);
            options.Controls.Add(session);
            options.Controls.Add(new Label { Text = "sec active (0 = unlimited)", AutoSize = true, Margin = new Padding(2, 12, 0, 0), ForeColor = muted });
            indefiniteTimers.Text = "Run indefinitely";
            indefiniteTimers.AutoSize = true;
            indefiniteTimers.Checked = true;
            indefiniteTimers.Margin = new Padding(18, 10, 0, 0);
            indefiniteTimers.CheckedChanged += delegate
            {
                if (indefiniteTimers.Checked) session.Value = 0;
                else if (session.Value == 0) session.Value = 60;
                session.Enabled = !engine.IsArmed && !indefiniteTimers.Checked;
            };
            session.Enabled = false;
            options.Controls.Add(indefiniteTimers);
            repeatedLayout.Controls.Add(options, 0, 3);

            CardPanel statusPanel = new CardPanel { Dock = DockStyle.Fill, BackColor = surface, Padding = new Padding(12), Margin = new Padding(0, 7, 0, 4) };
            stateLabel.Text = "Stopped - ready when you are";
            stateLabel.Location = new Point(12, 9);
            stateLabel.AutoSize = false;
            stateLabel.AutoEllipsis = true;
            stateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            stateLabel.Size = new Size(900, 24);
            stateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            stateLabel.Font = new Font(Font, FontStyle.Bold);
            statsLabel.Location = new Point(12, 38);
            statsLabel.AutoSize = true;
            statsLabel.ForeColor = muted;
            statusPanel.Controls.Add(stateLabel); statusPanel.Controls.Add(statsLabel);
            statusPanel.Resize += delegate { stateLabel.Width = Math.Max(10, statusPanel.ClientSize.Width - 24); };
            layout.Controls.Add(statusPanel, 0, 3);

            FlowLayoutPanel bottom = Flow();
            startButton.Text = "Start clicking  F9";
            StyleButton(startButton);
            startButton.Width = 166;
            UiTheme.StyleButton(startButton, true);
            startButton.Click += delegate { Toggle(); };
            stopButton.Text = "Stop  F10";
            StyleButton(stopButton);
            stopButton.Width = 115;
            stopButton.Click += delegate { Stop(); };
            bottom.Controls.Add(startButton); bottom.Controls.Add(stopButton);
            Button testPad = MakeButton("Check input", delegate { OpenInputDiagnostics(); });
            testPad.Width = 112;
            bottom.Controls.Add(testPad); editButtons.Add(testPad);
            Button hide = MakeButton("Hide to tray", delegate { HideToTray(); });
            hide.Width = 116;
            bottom.Controls.Add(hide);
            tips.SetToolTip(hide, "Hide this window while the app stays open. Double-click the tray icon to restore it. F10 still stops the macro.");
            tips.SetToolTip(startButton, "Start the current mode, or stop its current session. Only the selected target receives input while focused.");
            hotkeyLabel.AutoSize = true;
            bottom.Resize += delegate { hotkeyLabel.MaximumSize = new Size(Math.Max(100, bottom.ClientSize.Width - 4), 0); };
            hotkeyLabel.ForeColor = muted;
            hotkeyLabel.Margin = new Padding(0, 5, 0, 0);
            hotkeyLabel.Text = "Only sends while the selected window is focused. Stop before typing in the target.";
            bottom.SetFlowBreak(bottom.Controls[bottom.Controls.Count - 1], true);
            bottom.Controls.Add(hotkeyLabel);
            layout.Controls.Add(bottom, 0, 4);
        }

        FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 3, 0, 0) }; }
        void StyleNumber(NumericUpDown field, int min, int max, int value)
        {
            field.Minimum = min; field.Maximum = max; field.Value = value;
            field.Width = 80; field.Margin = new Padding(0, 8, 0, 0);
            field.BackColor = surface; field.ForeColor = textColor;
            UiTheme.StyleNumber(field);
        }
        Button MakeButton(string text, EventHandler handler) { Button b = new ModernButton { Text = text }; StyleButton(b); b.Click += handler; return b; }
        void StyleButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(61, 72, 90);
            b.BackColor = surface; b.ForeColor = textColor; b.Size = new Size(90, 34); b.Margin = new Padding(0, 2, 8, 0);
            b.Cursor = Cursors.Hand;
            UiTheme.StyleButton(b, false);
        }

        void RegisterShortcuts()
        {
            if (registrationHandle == Handle) return;
            registrationHandle = Handle;
            List<string> failed = new List<string>();
            toggleLabel = RegisterOne(ToggleHotkey, 0x78, "F9", failed);
            stopLabel = RegisterOne(StopHotkey, 0x79, "F10", failed);
            pointLabel = RegisterOne(PointHotkey, 0x7A, "F11", failed);
            recordLabel = RegisterOne(RecordHotkey, 0x77, "F8", failed);
            sequencePanel.SetRecordHotkeyLabel(recordLabel);
            sequencePanel.SetPlaybackAllowed(hotkeys.Contains(StopHotkey));
            startButton.Text = "Start clicking  " + toggleLabel;
            stopButton.Text = "Stop  " + stopLabel;
            startButton.Width = toggleLabel.Length > 4 ? 220 : 166;
            stopButton.Width = stopLabel.Length > 4 ? 190 : 115;
            hotkeyLabel.Text = "Start / stop  " + toggleLabel + "     Stop  " + stopLabel + "     Record  " + recordLabel + "     Pick point  " + pointLabel;
            hint.Text = "Each row repeats independently. First after staggers its start; pick click points with " + pointLabel + ".";
            quickClickPanel.SetPointShortcut(pointLabel);
            startButton.Enabled = hotkeys.Contains(StopHotkey);
            if (failed.Count > 0)
            {
                hotkeyLabel.Text += " Unavailable: " + string.Join(", ", failed.ToArray()) + ".";
                hotkeyLabel.ForeColor = Color.Salmon;
                if (!hotkeys.Contains(StopHotkey)) { startButton.Enabled = false; ShowStatus("A stop hotkey must be available before starting.", true); }
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (layoutReady && !previewMode)
            {
                AppInstance.RegisterWindow(Handle);
                RegisterShortcuts();
            }
        }
        void OpenTestPad()
        {
            try
            {
                System.Diagnostics.Process.Start(Application.ExecutablePath, "--test-pad");
                Timer refreshTimer = new Timer { Interval = 800 };
                refreshTimer.Tick += delegate { refreshTimer.Stop(); refreshTimer.Dispose(); if (!closing) RefreshTargets(); };
                refreshTimer.Start();
                ShowStatus("Test pad opened. Select it in the target dropdown to test inputs.", false);
            }
            catch (Exception ex) { ShowStatus("Could not open test pad: " + ex.Message, true); }
        }
        void OpenInputDiagnostics()
        {
            Stop();
            RefreshTargets();
            TargetWindow target = targets.SelectedItem as TargetWindow;
            if (inputDiagnostics != null && !inputDiagnostics.IsDisposed) { inputDiagnostics.Activate(); return; }
            inputDiagnostics = new InputDiagnosticsForm(target, native, delegate(KeyboardInputMode mode) { ApplyKeyboardMode(mode, true); }, OpenTestPad);
            modes.Enabled = false; targets.Enabled = false;
            inputDiagnostics.FormClosed += delegate { inputDiagnostics = null; modes.Enabled = true; targets.Enabled = true; };
            inputDiagnostics.Show(this);
        }
        void ApplyKeyboardMode(KeyboardInputMode mode, bool persist)
        {
            native.KeyboardMode = mode;
            sequencePanel.SetKeyboardMode(mode);
            if (persist)
            {
                try { new InputPreferences { KeyboardMode = mode }.Save(); }
                catch (Exception ex) { ShowStatus("Input mode applied, but it could not be saved: " + ex.Message, true); }
            }
        }
        public void RenderPreviews(string folder)
        {
            if (!previewMode) throw new InvalidOperationException("Preview rendering requires preview mode.");
            Directory.CreateDirectory(folder);
            hotkeyLabel.Text = "Start / stop  F9     Stop  F10     Record  F8     Pick point  F11";
            hint.Text = "Each row repeats independently. First after staggers its start; pick click points with F11.";
            statsLabel.Text = "0 OS-accepted presses / clicks    |    00:00:00 active";
            MinimumSize = Size.Empty;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            Show();
            string[] names = new string[] { "clicker", "routines", "recorder", "profiles", "devices" };
            foreach (Size size in new Size[] { new Size(1080, 820), new Size(900, 690) })
            {
                Size = size;
                for (int index = 0; index < modes.TabPages.Count; index++)
                {
                    modes.SelectedIndex = index;
                    CreateControl();
                    PerformLayout();
                    Application.DoEvents();
                    TickEngine();
                    AssertPreviewLayout();
                    using (Bitmap bitmap = new Bitmap(Width, Height))
                    {
                        DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                        bitmap.Save(Path.Combine(folder, names[index] + "-" + size.Width + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
            }
            using (HelpForm help = new HelpForm(hotkeyLabel.Text, true))
                help.RenderPreview(Path.Combine(folder, "help-740.png"), new Size(740, 650));
            profilePanel.RenderIncludedPresetsPreview(Path.Combine(folder, "included-presets-650.png"));
            Hide();
        }
        protected override bool ShowWithoutActivation { get { return previewMode || base.ShowWithoutActivation; } }
        void AssertPreviewLayout()
        {
            if (modes.TabPages.Count != 5) throw new InvalidOperationException("The workspace must show five pages.");
            if (!modes.Parent.ClientRectangle.Contains(modes.Bounds)) throw new InvalidOperationException("The workspace extends beyond the window.");
            for (int index = 0; index < modes.TabPages.Count; index++)
            {
                Rectangle tab = modes.GetTabRect(index);
                if (tab.Left < 0 || tab.Right > modes.ClientSize.Width || tab.Height < 30)
                    throw new InvalidOperationException("A navigation tab is clipped at this preview size.");
            }
            if (!startButton.Parent.ClientRectangle.Contains(startButton.Bounds) || !stopButton.Parent.ClientRectangle.Contains(stopButton.Bounds))
                throw new InvalidOperationException("The primary start or stop control is clipped.");
            if (modes.SelectedIndex == ClickerMode && quickClickPanel.CaptureSettings().Input != "Left click")
                throw new InvalidOperationException("The fresh mouse page should start with a general left-click setup.");
            if (modes.SelectedIndex == ClickerMode) quickClickPanel.AssertLayout();
        }
        string RegisterOne(int id, uint key, string label, List<string> failed)
        {
            if (hotkeys.Contains(id)) return label;
            if (RegisterHotKey(Handle, id, 0x4000, key)) { hotkeys.Add(id); return label; }
            if (RegisterHotKey(Handle, id, 0x4003, key)) { hotkeys.Add(id); return "Ctrl+Alt+" + label; }
            int error = Marshal.GetLastWin32Error();
            failed.Add(label + " (Windows error " + error + ")");
            return "unavailable";
        }
        protected override void WndProc(ref Message m)
        {
            if (AppInstance.ShowMessage != 0 && (uint)m.Msg == AppInstance.ShowMessage && !previewMode && !closing)
            {
                RestoreFromTray();
            }
            if (m.Msg == 0x0312 && !closing)
            {
                int id = m.WParam.ToInt32();
                if (inputDiagnostics != null && !inputDiagnostics.IsDisposed && id != StopHotkey) { inputDiagnostics.Activate(); base.WndProc(ref m); return; }
                if (id == ToggleHotkey) Toggle();
                else if (id == StopHotkey) Stop();
                else if (id == PointHotkey) CapturePoint();
                else if (id == RecordHotkey)
                {
                    if (engine.IsArmed) Stop();
                    modes.SelectedIndex = ReplayMode;
                    sequencePanel.ToggleRecording();
                }
            }
            base.WndProc(ref m);
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            AppInstance.UnregisterWindow(registrationHandle);
            foreach (int id in hotkeys) UnregisterHotKey(registrationHandle, id);
            hotkeys.Clear(); registrationHandle = IntPtr.Zero;
            base.OnHandleDestroyed(e);
        }
        void SetupTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.BackColor = surface;
            trayMenu.ForeColor = textColor;
            trayMenu.Font = UiTheme.BodyFont;
            trayMenu.ShowImageMargin = false;
            trayMenu.Renderer = new ToolStripProfessionalRenderer(new MacroMenuColors());
            trayMenu.Items.Add("Open " + BrandAssets.AppName, null, delegate { RestoreFromTray(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayToggle = new ToolStripMenuItem("Start macro", null, delegate { Toggle(); });
            trayRecord = new ToolStripMenuItem("Record routine", null, delegate
            {
                if (inputDiagnostics != null && !inputDiagnostics.IsDisposed) { inputDiagnostics.Activate(); return; }
                if (engine.IsArmed) return;
                modes.SelectedIndex = ReplayMode;
                sequencePanel.ToggleRecording();
            });
            trayMenu.Items.Add(trayToggle);
            trayMenu.Items.Add(trayRecord);
            trayMenu.Items.Add("Stop all macros  F10", null, delegate { Stop(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Quick help", null, delegate { new HelpForm(hotkeyLabel.Text).Show(this); });
            trayMenu.Items.Add("Exit", null, delegate { Close(); });
            tray.Icon = brandIcon;
            tray.Text = BrandAssets.AppName + " " + BrandAssets.Version + " - Stopped";
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { RestoreFromTray(); };
            tray.Visible = true;
            lastTrayUpdate = unchecked(Environment.TickCount - 500);
            UpdateTray();
        }
        void HideToTray()
        {
            if (previewMode || !tray.Visible) return;
            Hide();
            tray.ShowBalloonTip(2200, BrandAssets.AppName + " is in the tray", "Double-click the icon to reopen. F10 stops input. Keep the selected target focused.", ToolTipIcon.Info);
        }
        void RestoreFromTray()
        {
            if (closing) return;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }
        void UpdateTray()
        {
            if (previewMode || trayMenu == null || unchecked(Environment.TickCount - lastTrayUpdate) < 500) return;
            lastTrayUpdate = Environment.TickCount;
            bool armed = engine.IsArmed || sequencePanel.IsBusy;
            string state = sequencePanel.IsRecording ? "Recording" : armed ? (engine.IsSending || sequencePanel.IsSending ? "Running" : "Paused / waiting") : "Stopped";
            long actions = sequencePanel.IsBusy || modes.SelectedIndex == ReplayMode ? sequencePanel.TotalActions : engine.TotalActions;
            string summary = BrandAssets.AppName + " " + BrandAssets.Version + " - " + state + " - " + actions + " actions";
            tray.Text = summary.Length > 63 ? summary.Substring(0, 63) : summary;
            trayToggle.Text = sequencePanel.IsRecording ? "Finish recording" : armed ? "Stop session" : modes.SelectedIndex == ReplayMode ? "Play sequence" : modes.SelectedIndex == ClickerMode ? "Start clicking" : "Start routine";
            trayToggle.Enabled = modes.SelectedIndex <= ReplayMode && hotkeys.Contains(StopHotkey);
            trayRecord.Text = sequencePanel.IsRecording ? "Finish recording" : "Record routine";
            trayRecord.Enabled = !engine.IsArmed && (!sequencePanel.IsBusy || sequencePanel.IsRecording);
        }

        void RefreshTargets()
        {
            if (engine.IsArmed || sequencePanel.IsBusy) return;
            TargetWindow previous = targets.SelectedItem as TargetWindow;
            List<TargetWindow> windows = NativeInput.ListTargets();
            targets.Items.Clear();
            targets.Items.Add("Choose an application window…");
            int selected = 0;
            foreach (TargetWindow window in windows)
            {
                if (window.Handle == Handle) continue;
                int index = targets.Items.Add(window);
                if (previous != null && previous.Handle == window.Handle) selected = index;
            }
            targets.SelectedIndex = selected;
        }
        void AddRow(MacroAction action)
        {
            grid.Rows.Add(action.Enabled, action.Input, (action.IntervalMs / 1000m).ToString("0.###", CultureInfo.InvariantCulture), action.HoldMs, action.Burst,
                action.X.HasValue ? action.X.Value.ToString(CultureInfo.InvariantCulture) : "", action.Y.HasValue ? action.Y.Value.ToString(CultureInfo.InvariantCulture) : "", (action.OffsetMs / 1000m).ToString("0.###", CultureInfo.InvariantCulture), action.KeepCooldownOnResume);
        }
        void ApplyPreset(string name)
        {
            if (engine.IsArmed) return;
            grid.Rows.Clear();
            if (name == "Click repeat" || name == "Multiple click points")
            {
                AddRow(new MacroAction { Enabled = true, Input = "Left click", IntervalMs = 1000, HoldMs = 40, Burst = 1 });
                if (name == "Multiple click points") AddRow(new MacroAction { Enabled = true, Input = "Left click", IntervalMs = 1000, HoldMs = 40, Burst = 1, OffsetMs = 500 });
            }
            else AddRow(new MacroAction { Enabled = true, Input = "Space", IntervalMs = 1000, HoldMs = 60, Burst = 1 });
            ShowStatus("Example loaded. Review its inputs and timing before starting.", false);
        }
        List<MacroAction> ReadActions(bool includeDisabled = false)
        {
            grid.EndEdit();
            List<MacroAction> actions = new List<MacroAction>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                try
                {
                    bool enabled = Convert.ToBoolean(row.Cells[0].Value ?? false);
                    if (!enabled && !includeDisabled) continue;
                    decimal seconds = decimal.Parse(Convert.ToString(row.Cells[2].Value), CultureInfo.InvariantCulture);
                    int interval = checked((int)(seconds * 1000m));
                    string xText = Convert.ToString(row.Cells[5].Value).Trim();
                    string yText = Convert.ToString(row.Cells[6].Value).Trim();
                    actions.Add(new MacroAction { Enabled = enabled, Input = Convert.ToString(row.Cells[1].Value), IntervalMs = interval,
                        HoldMs = int.Parse(Convert.ToString(row.Cells[3].Value), CultureInfo.InvariantCulture), Burst = int.Parse(Convert.ToString(row.Cells[4].Value), CultureInfo.InvariantCulture),
                        X = xText == "" ? (int?)null : int.Parse(xText, CultureInfo.InvariantCulture), Y = yText == "" ? (int?)null : int.Parse(yText, CultureInfo.InvariantCulture),
                        OffsetMs = checked((int)(decimal.Parse(Convert.ToString(row.Cells[7].Value), CultureInfo.InvariantCulture) * 1000m)),
                        KeepCooldownOnResume = Convert.ToBoolean(row.Cells[8].Value ?? false) });
                }
                catch (Exception ex) { throw new ArgumentException("Row " + (row.Index + 1) + ": check timings, hold, repeats and X/Y. " + ex.Message); }
            }
            return actions;
        }
        void Toggle()
        {
            if (inputDiagnostics != null && !inputDiagnostics.IsDisposed) { inputDiagnostics.Activate(); return; }
            if (modes.SelectedIndex > ReplayMode) { ShowStatus("Choose Mouse clicker, Routines or Record & replay to start.", false); return; }
            if (modes.SelectedIndex == ReplayMode)
            {
                if (sequencePanel.IsRecording) { sequencePanel.Stop(); return; }
                if (!hotkeys.Contains(StopHotkey)) { ShowStatus("A stop shortcut must be available before replay.", true); return; }
                sequencePanel.TogglePlayback(); return;
            }
            if (engine.IsArmed) { Stop(); return; }
            if (!hotkeys.Contains(StopHotkey)) { ShowStatus("Stop hotkey is unavailable. Close the conflicting app and restart.", true); return; }
            try
            {
                TargetWindow target = targets.SelectedItem as TargetWindow;
                RefreshTargets();
                target = targets.SelectedItem as TargetWindow;
                if (target == null) throw new ArgumentException("Choose a target window first.");
                int requestedMode = modes.SelectedIndex;
                if (requestedMode == ClickerMode)
                {
                    QuickClickSettings settings = quickClickPanel.CaptureSettings();
                    engine.Start(target.Handle, new[] { settings.ToAction() }, settings.CountdownSeconds * 1000, settings.SessionSeconds * 1000, settings.MaxClicks);
                }
                else engine.Start(target.Handle, ReadActions(), (int)countdown.Value * 1000, (int)session.Value * 1000);
                activeMode = requestedMode;
                SetEditing(false);
                ShowStatus(engine.Status, false);
            }
            catch (Exception ex) { engine.Stop(); SetEditing(true); ShowStatus(UiTheme.InputFailureSummary(ex), true); UiTheme.ShowPermissionDetails(this, ex); }
        }
        void Stop()
        {
            if (inputDiagnostics != null && !inputDiagnostics.IsDisposed) inputDiagnostics.StopTest();
            engine.Stop(); sequencePanel.Stop(); SetEditing(true);
            ShowStatus("Stopped - all held inputs released", false);
        }
        void SetEditing(bool enabled)
        {
            grid.Enabled = enabled; targets.Enabled = enabled; presets.Enabled = enabled;
            quickClickPanel.SetEditing(enabled);
            countdown.Enabled = enabled; indefiniteTimers.Enabled = enabled; session.Enabled = enabled && !indefiniteTimers.Checked;
            foreach (Button b in editButtons) b.Enabled = enabled;
            startButton.Text = (enabled ? modes.SelectedIndex == ClickerMode ? "Start clicking  " : "Start routine  " : "Stop session  ") + toggleLabel;
        }
        void TickEngine()
        {
            if ((engine.IsArmed || sequencePanel.IsBusy) && NativeInput.IsEmergencyStopPressed()) { Stop(); return; }
            sequencePanel.Tick();
            try { engine.Tick(); }
            catch (Exception ex) { engine.Stop(); ShowStatus("Input stopped: " + ex.Message, true); }
            UpdateTray();
            if (inputDiagnostics != null && !inputDiagnostics.IsDisposed)
            {
                targets.Enabled = false; modes.Enabled = false; startButton.Enabled = false;
                ShowStatus("Input check open - close it before starting a routine", false);
                return;
            }
            startButton.Enabled = (previewMode || hotkeys.Contains(StopHotkey)) && modes.SelectedIndex <= ReplayMode && (inputDiagnostics == null || inputDiagnostics.IsDisposed);
            if (modes.SelectedIndex > ReplayMode)
            {
                targets.Enabled = true;
                startButton.Text = "Choose a mode";
                ShowStatus(modes.SelectedIndex == ProfilesMode ? profilePanel.Status : devicesPanel.Status, false);
                statsLabel.Text = modes.SelectedIndex == ProfilesMode ? "Named setups keep mouse, routine and recording settings. Loading always leaves playback stopped." : "Export routine drafts for Android; map their tap points on your phone before running.";
                return;
            }
            if (modes.SelectedIndex == ReplayMode)
            {
                targets.Enabled = !sequencePanel.IsBusy;
                ShowStatus(sequencePanel.Status, false);
                stateLabel.ForeColor = sequencePanel.IsRecording ? Color.Salmon : sequencePanel.IsSending ? Color.FromArgb(109, 221, 177) : textColor;
                statsLabel.Text = sequencePanel.IsRecording ? sequencePanel.TotalActions.ToString("N0") + " recorded events" : sequencePanel.TotalActions.ToString("N0") + " OS-accepted replay presses / clicks    |    " + sequencePanel.ActiveDuration.ToString(@"hh\:mm\:ss") + " active";
                startButton.Text = sequencePanel.IsRecording ? "Finish record  " + toggleLabel : sequencePanel.IsBusy ? "Stop session  " + toggleLabel : "Play sequence  " + toggleLabel;
                return;
            }
            if (!engine.IsArmed) SetEditing(true);
            if (engine.IsArmed || modes.SelectedIndex == activeMode && observedEngineStatus != engine.Status && engine.Status != "Stopped") ShowStatus(engine.Status, false);
            observedEngineStatus = engine.Status;
            long total = modes.SelectedIndex == activeMode ? engine.TotalActions : 0;
            TimeSpan duration = modes.SelectedIndex == activeMode ? engine.ActiveDuration : TimeSpan.Zero;
            string remaining = "";
            if (modes.SelectedIndex == ClickerMode)
            {
                try { long limit = quickClickPanel.CaptureSettings().MaxClicks; if (limit > 0) remaining = " / " + limit.ToString("N0"); }
                catch (ArgumentException) { }
            }
            statsLabel.Text = total.ToString("N0") + remaining + (modes.SelectedIndex == ClickerMode ? " OS-accepted clicks" : " OS-accepted presses / clicks") + "    |    " + duration.ToString(@"hh\:mm\:ss") + " active";
            if (engine.IsArmed) stateLabel.ForeColor = engine.IsSending ? Color.FromArgb(109, 221, 177) : Color.FromArgb(243, 201, 113);
        }
        void ShowStatus(string message, bool error)
        {
            lastStatus = message; stateLabel.Text = message;
            stateLabel.ForeColor = error ? Color.Salmon : textColor;
        }
        void CapturePoint()
        {
            if (modes.SelectedIndex > ReplayMode) return;
            if (modes.SelectedIndex == ReplayMode) { ShowStatus("Record a click in the target to capture a sequence point.", false); return; }
            if (engine.IsArmed) { ShowStatus("Stop first to change a click point.", true); return; }
            TargetWindow target = targets.SelectedItem as TargetWindow;
            if (modes.SelectedIndex == ClickerMode)
            {
                int quickX, quickY;
                if (target == null || !NativeInput.GetClientCursor(target.Handle, out quickX, out quickY)) { ShowStatus("Point inside the selected target, then press " + pointLabel + ".", true); return; }
                quickClickPanel.SetFixedPoint(quickX, quickY);
                ShowStatus("Fixed point saved: " + quickX + ", " + quickY + " (relative to the target window)", false);
                return;
            }
            DataGridViewRow row = grid.CurrentRow;
            if (target == null || row == null || !Convert.ToString(row.Cells[1].Value).EndsWith("click", StringComparison.OrdinalIgnoreCase))
            { ShowStatus("Select a mouse-click row, point in the target, then press " + pointLabel + ".", true); return; }
            int x, y;
            if (!NativeInput.GetClientCursor(target.Handle, out x, out y)) { ShowStatus("Point inside the selected target window, then press " + pointLabel + ".", true); return; }
            row.Cells[5].Value = x; row.Cells[6].Value = y;
            ShowStatus("Click point saved: " + x + ", " + y + " (target client coordinates)", false);
        }
        SavedProfile CurrentProfile()
        {
            return new SavedProfile { Actions = ReadActions(true), CountdownSeconds = (int)countdown.Value, SessionSeconds = (int)session.Value };
        }
        MacroWorkspaceProfile CaptureWorkspace()
        {
            return new MacroWorkspaceProfile { Clicker = quickClickPanel.CaptureSettings(), Timers = CurrentProfile(), Sequence = sequencePanel.CaptureProfile() };
        }
        MacroWorkspaceProfile CaptureMobileTimers()
        {
            // Mobile drafts transfer timer settings only. A sequence being edited
            // independently must not prevent an Android timer export.
            return new MacroWorkspaceProfile { Name = "My mobile routine", Timers = CurrentProfile() };
        }
        void ApplyWorkspace(MacroWorkspaceProfile profile)
        {
            if (engine.IsArmed || sequencePanel.IsBusy) throw new InvalidOperationException("Stop the macro before loading a profile.");
            ProfileLibrary.Validate(profile);
            quickClickPanel.ApplySettings(profile.Clicker ?? new QuickClickSettings());
            sequencePanel.ApplyProfile(profile.Sequence);
            grid.Rows.Clear();
            foreach (MacroAction action in profile.Timers.Actions) AddRow(action);
            countdown.Value = profile.Timers.CountdownSeconds;
            session.Value = profile.Timers.SessionSeconds;
            indefiniteTimers.Checked = profile.Timers.SessionSeconds == 0;
            ShowStatus("Loaded " + profile.Name + " - ready to review", false);
        }
        void DuplicateAction()
        {
            if (engine.IsArmed || grid.CurrentRow == null) return;
            grid.EndEdit();
            DataGridViewRow original = grid.CurrentRow;
            object[] values = new object[grid.ColumnCount];
            for (int i = 0; i < values.Length; i++) values[i] = original.Cells[i].Value;
            int index = original.Index + 1;
            grid.Rows.Insert(index, values);
            grid.CurrentCell = grid.Rows[index].Cells[1];
            ShowStatus("Action duplicated. Adjust its key, timer or click point.", false);
        }
        void SaveProfile()
        {
            try
            {
                SavedProfile profile = CurrentProfile();
                string json = new JavaScriptSerializer().Serialize(profile);
                Directory.CreateDirectory(Path.GetDirectoryName(profilePath));
                string temp = profilePath + ".tmp";
                File.WriteAllText(temp, json);
                if (File.Exists(profilePath)) File.Replace(temp, profilePath, null); else File.Move(temp, profilePath);
                ShowStatus("Saved profile. It will load next time, stopped.", false);
            }
            catch (Exception ex) { ShowStatus("Could not save: " + ex.Message, true); }
        }
        void LoadProfile(bool report)
        {
            if (engine.IsArmed) return;
            try
            {
                SavedProfile profile = new JavaScriptSerializer().Deserialize<SavedProfile>(File.ReadAllText(profilePath));
                if (profile == null || profile.Version != 1 || profile.Actions == null || profile.Actions.Count > 100) throw new ArgumentException("Unsupported profile.");
                foreach (MacroAction action in profile.Actions)
                    if (action == null || Array.IndexOf(NativeInput.SupportedInputs, action.Input) < 0) throw new ArgumentException("Unsupported input in profile.");
                if (profile.CountdownSeconds < 1 || profile.CountdownSeconds > 30 || profile.SessionSeconds < 0 || profile.SessionSeconds > 86400) throw new ArgumentException("Invalid session settings.");
                grid.Rows.Clear(); foreach (MacroAction action in profile.Actions) AddRow(action);
                countdown.Value = profile.CountdownSeconds; session.Value = profile.SessionSeconds;
                indefiniteTimers.Checked = profile.SessionSeconds == 0;
                ShowStatus("Profile loaded - stopped", false);
            }
            catch (Exception ex) { if (report) ShowStatus("Could not load: " + ex.Message, true); }
        }
    }

    sealed class InputTestPad : Form
    {
        readonly Label counts = new Label();
        readonly TextBox log = new TextBox();
        int keyCount, clicks;
        public InputTestPad()
        {
            Text = BrandAssets.AppName + " Input Test Pad (" + System.Diagnostics.Process.GetCurrentProcess().Id + ")";
            Size = new Size(660, 520); StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
            Font = new Font("Segoe UI", 12); KeyPreview = true;
            Label info = new Label { Text = "INPUT TEST PAD\nSelect this window as the macro target to test keys and clicks.\nUse the shortcuts shown in the main window. Moving focus away pauses sending.", Dock = DockStyle.Top, Height = 105, Padding = new Padding(18) };
            counts.Text = "Keys: 0    Clicks: 0"; counts.Dock = DockStyle.Top; counts.Height = 40; counts.Padding = new Padding(18, 0, 0, 0);
            log.Multiline = true; log.ReadOnly = true; log.Dock = DockStyle.Fill; log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = UiTheme.Surface; log.ForeColor = UiTheme.Accent;
            log.BorderStyle = BorderStyle.None;
            log.TabStop = false;
            Controls.Add(log); Controls.Add(counts); Controls.Add(info);
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Alt && e.KeyCode == Keys.F4) return; keyCount++; Record("KEY " + e.KeyCode); e.SuppressKeyPress = true; };
            MouseDown += OnTestClick; info.MouseDown += OnTestClick; counts.MouseDown += OnTestClick; log.MouseDown += OnTestClick;
        }
        void OnTestClick(object sender, MouseEventArgs e) { clicks++; Record("CLICK " + e.Button); }
        void Record(string action)
        {
            counts.Text = "Keys: " + keyCount + "    Clicks: " + clicks;
            if (log.TextLength > 20000) log.Clear();
            log.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + action + Environment.NewLine);
        }
    }
}
