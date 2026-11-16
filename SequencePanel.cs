using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FarmMacro
{
    public sealed class SavedSequence
    {
        public int Version = 1;
        public int CountdownSeconds = 3;
        public int RepeatCount;
        public int LoopDelayMs = 1000;
        public int SessionSeconds;
        public List<MacroStep> Steps = new List<MacroStep>();
    }

    public sealed class SequencePanel : UserControl
    {
        readonly Func<TargetWindow> getTarget;
        readonly Func<bool> otherModeBusy;
        readonly NativeInput native = new NativeInput();
        readonly SequenceEngine player;
        readonly MacroRecorder recorder;
        readonly Color surface = UiTheme.Surface;
        readonly Color textColor = UiTheme.Text;
        readonly Color muted = UiTheme.Muted;
        readonly List<Control> editControls = new List<Control>();
        readonly NumericUpDown countdown = new NumericUpDown();
        readonly NumericUpDown loops = new NumericUpDown();
        readonly NumericUpDown gap = new NumericUpDown();
        readonly NumericUpDown limit = new NumericUpDown();
        readonly ComboBox examples = new ComboBox();
        readonly CheckBox indefinite = new CheckBox();
        readonly Button recordButton;
        readonly Button playButton;
        readonly Label statusLabel = new Label();
        readonly Label explanation = new Label();
        readonly string profilePath;
        string status = "Stopped";
        string recordKey = "F8";
        bool disposed;
        bool playbackAllowed = true;
        bool loadingSettings;
        int lastRecordCount;
        int lastCountTick;

        public DataGridView EditorGrid { get; private set; }
        public bool IsBusy { get { return recorder.IsRecording || player.IsArmed; } }
        public bool IsRecording { get { return recorder.IsRecording; } }
        public bool IsSending { get { return player.IsSending; } }
        public string Status { get { return status; } }
        public long TotalActions { get { return recorder.IsRecording ? lastRecordCount : player.TotalActions; } }
        public TimeSpan ActiveDuration { get { return recorder.IsRecording ? recorder.ActiveDuration : player.ActiveDuration; } }

        public SequencePanel(Func<TargetWindow> getTarget, Func<bool> otherModeBusy)
        {
            if (getTarget == null || otherModeBusy == null) throw new ArgumentNullException();
            this.getTarget = getTarget;
            this.otherModeBusy = otherModeBusy;
            player = new SequenceEngine(native);
            recorder = new MacroRecorder(native);
            profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FarmMacro", "sequence.json");
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Background;
            ForeColor = textColor;
            Font = new Font("Segoe UI", 9F);
            AutoScroll = true;

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10), MinimumSize = new Size(0, 270) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            Controls.Add(layout);

            FlowLayoutPanel toolbar = Flow();
            recordButton = Button("Record  F8", delegate { ToggleRecording(); }, 109);
            playButton = Button("Play", delegate { TogglePlayback(); }, 65);
            toolbar.Controls.Add(recordButton);
            toolbar.Controls.Add(playButton);
            toolbar.Controls.Add(Button("Stop", delegate { Stop(); }, 64));
            AddEditorButton(toolbar, "Save", delegate { SaveSequence(); }, 74);
            AddEditorButton(toolbar, "Load", delegate { LoadSequence(true); }, 74);
            AddEditorButton(toolbar, "Clear", delegate { ClearSequence(); }, 74);
            layout.Controls.Add(toolbar, 0, 0);

            FlowLayoutPanel editorTools = Flow();
            examples.DropDownStyle = ComboBoxStyle.DropDownList;
            examples.Items.AddRange(new object[] { "Single click", "Double-click", "Press Space" });
            examples.SelectedIndex = 0;
            examples.Width = 164; examples.Margin = new Padding(0, 0, 7, 0);
            examples.AccessibleName = "Routine example";
            UiTheme.StyleCombo(examples); editorTools.Controls.Add(examples); editControls.Add(examples);
            AddEditorButton(editorTools, "Use example", delegate { LoadPreset(); }, 107);
            AddEditorButton(editorTools, "+ Key", delegate { AddPair("Space"); }, 74);
            AddEditorButton(editorTools, "+ Click", delegate { AddPair("Left click"); }, 80);
            AddEditorButton(editorTools, "Remove", delegate { if (EditorGrid.CurrentRow != null) EditorGrid.Rows.Remove(EditorGrid.CurrentRow); }, 80);
            layout.Controls.Add(editorTools, 0, 1);

            FlowLayoutPanel settings = Flow();
            indefinite.Text = "Run indefinitely";
            indefinite.AutoSize = true;
            indefinite.Checked = true;
            indefinite.ForeColor = textColor;
            indefinite.Margin = new Padding(0, 4, 12, 0);
            indefinite.CheckedChanged += delegate
            {
                if (loadingSettings) return;
                if (indefinite.Checked) { loops.Value = 0; limit.Value = 0; }
                else if (loops.Value == 0 && limit.Value == 0) loops.Value = 1;
                SetEditing();
                SetStatus(indefinite.Checked ? "Runs until you stop it. Loop gap still applies." : "Set the loop count or a timed stop for replay.", false);
            };
            settings.Controls.Add(indefinite);
            editControls.Add(indefinite);
            AddNumber(settings, "Delay s", countdown, 1, 30, 3, 48);
            AddNumber(settings, "Loops", loops, 0, 100000, 0, 64);
            AddNumber(settings, "Gap ms", gap, 0, 60000, 1000, 66);
            AddNumber(settings, "Limit s", limit, 0, 86400, 0, 66);
            layout.Controls.Add(settings, 0, 3);

            EditorGrid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = surface, BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect, EnableHeadersVisualStyles = false, ColumnHeadersHeight = 29 };
            EditorGrid.RowTemplate.Height = 28;
            EditorGrid.GridColor = Color.FromArgb(50, 58, 72);
            EditorGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(42, 50, 66), ForeColor = textColor,
                SelectionBackColor = Color.FromArgb(42, 50, 66), Font = new Font(Font, FontStyle.Bold) };
            EditorGrid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = surface, ForeColor = textColor,
                SelectionBackColor = Color.FromArgb(50, 82, 119), SelectionForeColor = Color.White, Padding = new Padding(3) };
            EditorGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Delay", HeaderText = "Wait before (ms)", FillWeight = 105 });
            DataGridViewComboBoxColumn input = new DataGridViewComboBoxColumn { Name = "Input", HeaderText = "Key / click", FillWeight = 120, FlatStyle = FlatStyle.Flat };
            foreach (string key in NativeInput.SupportedInputs) input.Items.Add(key);
            EditorGrid.Columns.Add(input);
            DataGridViewComboBoxColumn edge = new DataGridViewComboBoxColumn { Name = "Edge", HeaderText = "Event", FillWeight = 75, FlatStyle = FlatStyle.Flat };
            edge.Items.AddRange("Down", "Up");
            EditorGrid.Columns.Add(edge);
            EditorGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "X", HeaderText = "Click X", FillWeight = 65 });
            EditorGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Y", HeaderText = "Click Y", FillWeight = 65 });
            UiTheme.StyleGrid(EditorGrid);
            EditorGrid.RowTemplate.Height = 28;
            EditorGrid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; SetStatus("Choose a supported input and Down or Up event.", true); };
            EditorGrid.RowsAdded += delegate { UpdateExplanation(); };
            EditorGrid.RowsRemoved += delegate { UpdateExplanation(); };
            EditorGrid.CellValueChanged += delegate { UpdateExplanation(); };
            editControls.Add(EditorGrid);
            layout.Controls.Add(EditorGrid, 0, 2);

            explanation.Dock = DockStyle.Fill;
            explanation.ForeColor = muted;
            explanation.Padding = new Padding(0, 3, 0, 0);
            UpdateExplanation();
            layout.Controls.Add(explanation, 0, 4);
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Visible = false;
            statusLabel.AutoEllipsis = true;
            statusLabel.ForeColor = muted;
            layout.Controls.Add(statusLabel, 0, 5);
            if (File.Exists(profilePath)) LoadSequence(false);
            SetEditing();
        }

        FlowLayoutPanel Flow()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0), AutoScroll = true };
        }
        Button Button(string text, EventHandler handler, int width)
        {
            Button button = new ModernButton { Text = text, Width = width, Height = 29, Margin = new Padding(0, 0, 5, 0),
                FlatStyle = FlatStyle.Flat, BackColor = surface, ForeColor = textColor, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(61, 72, 90);
            UiTheme.StyleButton(button, text == "Play");
            button.Click += handler;
            return button;
        }
        void AddEditorButton(FlowLayoutPanel row, string text, EventHandler handler, int width)
        {
            Button button = Button(text, handler, width); row.Controls.Add(button); editControls.Add(button);
        }
        void AddNumber(FlowLayoutPanel row, string label, NumericUpDown input, int min, int max, int value, int width)
        {
            row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 5, 5, 0), ForeColor = muted });
            input.Minimum = min; input.Maximum = max; input.Value = value; input.Width = width;
            input.Margin = new Padding(0, 1, 13, 0); input.BackColor = surface; input.ForeColor = textColor;
            UiTheme.StyleNumber(input);
            row.Controls.Add(input); editControls.Add(input);
        }

        public void SetRecordHotkeyLabel(string value)
        {
            recordKey = value;
            recordButton.Width = value.Length > 3 ? 185 : 109;
            recordButton.Text = recorder.IsRecording ? "Finish  " + recordKey : "Record  " + recordKey;
        }
        public void SetPlaybackAllowed(bool value)
        {
            playbackAllowed = value;
            SetEditing();
        }
        public void SetKeyboardMode(KeyboardInputMode mode) { native.KeyboardMode = mode; }
        public void Tick()
        {
            if (disposed) return;
            bool wasPlaying = player.IsArmed;
            bool wasRecording = recorder.IsRecording;
            try
            {
                if (wasRecording)
                {
                    recorder.Tick();
                    status = recorder.Status;
                    if (unchecked(Environment.TickCount - lastCountTick) >= 250)
                    {
                        lastRecordCount = recorder.StepCount;
                        lastCountTick = Environment.TickCount;
                    }
                    if (!recorder.IsRecording) Populate(recorder.Steps);
                }
                if (wasPlaying)
                {
                    player.Tick();
                    status = player.Status;
                }
            }
            catch (Exception ex)
            {
                Stop();
                SetStatus("Stopped: " + ex.Message, true);
            }
            if (wasRecording != recorder.IsRecording || wasPlaying != player.IsArmed) SetEditing();
            statusLabel.Text = status;
            if (IsBusy) statusLabel.ForeColor = IsSending ? Color.FromArgb(109, 221, 177) : Color.FromArgb(243, 201, 113);
        }
        public void TogglePlayback()
        {
            if (player.IsArmed) { Stop(); return; }
            if (!playbackAllowed) { SetStatus("A stop shortcut must be available before replay.", true); return; }
            if (recorder.IsRecording) { SetStatus("Finish recording before replaying.", true); return; }
            if (otherModeBusy()) { SetStatus("Stop the independent timers first.", true); return; }
            try
            {
                TargetWindow target = getTarget();
                if (target == null) throw new ArgumentException("Choose a target window first.");
                List<MacroStep> steps = ReadSteps();
                player.Start(target.Handle, steps, (int)countdown.Value * 1000, (int)loops.Value, (int)gap.Value, (int)limit.Value * 1000);
                SetStatus(player.Status, false);
                SetEditing();
            }
            catch (Exception ex) { player.Stop(); SetEditing(); SetStatus(UiTheme.InputFailureSummary(ex), true); UiTheme.ShowPermissionDetails(this, ex); }
        }
        public void ToggleRecording()
        {
            if (recorder.IsRecording) { Stop(); return; }
            if (player.IsArmed) { SetStatus("Stop replay before recording.", true); return; }
            if (otherModeBusy()) { SetStatus("Stop the independent timers first.", true); return; }
            try
            {
                TargetWindow target = getTarget();
                if (target == null) throw new ArgumentException("Choose a target window first.");
                recorder.Start(target.Handle);
                lastRecordCount = 0;
                lastCountTick = Environment.TickCount;
                SetStatus(recorder.Status, false);
                SetEditing();
            }
            catch (Exception ex) { recorder.Stop(); SetEditing(); SetStatus("Could not record: " + UiTheme.InputFailureSummary(ex), true); UiTheme.ShowPermissionDetails(this, ex); }
        }
        public void Stop()
        {
            bool wasRecording = recorder.IsRecording;
            recorder.Stop();
            player.Stop();
            if (wasRecording)
            {
                List<MacroStep> snapshot = recorder.Steps;
                lastRecordCount = snapshot.Count;
                Populate(snapshot);
                SetStatus("Recorded " + snapshot.Count + " events. Review the sequence before replaying.", false);
            }
            else SetStatus(player.Status, false);
            SetEditing();
        }
        void SetEditing()
        {
            bool editing = !IsBusy;
            foreach (Control control in editControls) control.Enabled = editing;
            loops.Enabled = editing && !indefinite.Checked;
            limit.Enabled = editing && !indefinite.Checked;
            recordButton.Enabled = !player.IsArmed;
            playButton.Enabled = !recorder.IsRecording && playbackAllowed;
            recordButton.Text = recorder.IsRecording ? "Finish  " + recordKey : "Record  " + recordKey;
            playButton.Text = player.IsArmed ? "Stop" : "Play";
        }
        void SetStatus(string message, bool error)
        {
            status = message;
            statusLabel.Text = message;
            statusLabel.ForeColor = error ? Color.Salmon : muted;
        }
        void AddPair(string input)
        {
            if (IsBusy) return;
            AddStep(new MacroStep { Input = input, IsDown = true, DelayMs = EditorGrid.Rows.Count == 0 ? 0 : 200 });
            AddStep(new MacroStep { Input = input, IsDown = false, DelayMs = 60 });
        }
        void AddStep(MacroStep step)
        {
            EditorGrid.Rows.Add(step.DelayMs.ToString(CultureInfo.InvariantCulture), step.Input, step.IsDown ? "Down" : "Up",
                step.X.HasValue ? step.X.Value.ToString(CultureInfo.InvariantCulture) : "", step.Y.HasValue ? step.Y.Value.ToString(CultureInfo.InvariantCulture) : "");
        }
        void Populate(IEnumerable<MacroStep> steps)
        {
            EditorGrid.Rows.Clear();
            foreach (MacroStep step in steps) AddStep(step);
        }
        void LoadPreset()
        {
            if (IsBusy) return;
            if (!ConfirmReplace()) return;
            List<MacroStep> steps = new List<MacroStep>();
            string input = examples.SelectedIndex == 2 ? "Space" : "Left click";
            steps.Add(new MacroStep { Input = input, IsDown = true, DelayMs = 0 });
            steps.Add(new MacroStep { Input = input, IsDown = false, DelayMs = 40 });
            if (examples.SelectedIndex == 1)
            {
                steps.Add(new MacroStep { Input = input, IsDown = true, DelayMs = 80 });
                steps.Add(new MacroStep { Input = input, IsDown = false, DelayMs = 40 });
            }
            Populate(steps);
            gap.Value = 1000;
            SetStatus("Example loaded. Review the target and timings before playing.", false);
        }

        bool ConfirmReplace()
        {
            return EditorGrid.Rows.Count == 0 || MessageBox.Show(this,
                "Replace the events currently in the editor? Save them first if you want to keep them.",
                BrandAssets.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
        void ClearSequence()
        {
            if (IsBusy || !ConfirmReplace()) return;
            EditorGrid.Rows.Clear();
            SetStatus("Empty routine. Record your actions or add a key or click.", false);
        }
        void UpdateExplanation()
        {
            long milliseconds = 0;
            if (EditorGrid != null)
                foreach (DataGridViewRow row in EditorGrid.Rows)
                {
                    int value;
                    if (Int32.TryParse(Convert.ToString(row.Cells[0].Value), out value) && value >= 0) milliseconds += value;
                }
            int count = EditorGrid == null ? 0 : EditorGrid.Rows.Count;
            explanation.Text = count == 0 ? "Record a routine or start with an example. Keys, holds and click points are editable." :
                count + " events · about " + (milliseconds / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + " seconds per pass, plus the loop gap.";
            explanation.Text += "\nFocus loss releases held inputs and restarts this pass. Stop before typing; camera motion is not recorded.";
        }
        List<MacroStep> ReadSteps()
        {
            EditorGrid.EndEdit();
            List<MacroStep> steps = new List<MacroStep>();
            foreach (DataGridViewRow row in EditorGrid.Rows)
            {
                try
                {
                    int delay = int.Parse(Convert.ToString(row.Cells[0].Value), CultureInfo.InvariantCulture);
                    string input = Convert.ToString(row.Cells[1].Value);
                    string edge = Convert.ToString(row.Cells[2].Value);
                    string x = Convert.ToString(row.Cells[3].Value).Trim();
                    string y = Convert.ToString(row.Cells[4].Value).Trim();
                    if (delay < 0 || delay > 86400000) throw new ArgumentException("Wait must be 0 to 86400000 ms.");
                    if (NativeInput.NormalizeInput(input) == null) throw new ArgumentException("Choose a supported input.");
                    if (edge != "Down" && edge != "Up") throw new ArgumentException("Choose Down or Up.");
                    if ((x == "") != (y == "")) throw new ArgumentException("Enter both X and Y, or leave both blank.");
                    steps.Add(new MacroStep { Input = input, IsDown = edge == "Down", DelayMs = delay,
                        X = x == "" ? (int?)null : int.Parse(x, CultureInfo.InvariantCulture),
                        Y = y == "" ? (int?)null : int.Parse(y, CultureInfo.InvariantCulture) });
                }
                catch (Exception ex) { throw new ArgumentException("Sequence row " + (row.Index + 1) + ": " + ex.Message); }
            }
            return steps;
        }
        void SaveSequence()
        {
            if (IsBusy) return;
            try
            {
                SavedSequence profile = CaptureProfile();
                string json = new JavaScriptSerializer().Serialize(profile);
                Directory.CreateDirectory(Path.GetDirectoryName(profilePath));
                string temporary = profilePath + ".tmp";
                File.WriteAllText(temporary, json);
                if (File.Exists(profilePath)) File.Replace(temporary, profilePath, null); else File.Move(temporary, profilePath);
                SetStatus("Sequence saved locally. It will load next time, stopped.", false);
            }
            catch (Exception ex) { SetStatus("Could not save sequence: " + ex.Message, true); }
        }
        public SavedSequence CaptureProfile()
        {
            return new SavedSequence { Steps = ReadSteps(), CountdownSeconds = (int)countdown.Value,
                RepeatCount = (int)loops.Value, LoopDelayMs = (int)gap.Value, SessionSeconds = (int)limit.Value };
        }
        public void ApplyProfile(SavedSequence profile)
        {
            if (IsBusy) throw new InvalidOperationException("Stop the macro before loading a sequence.");
            if (profile == null || profile.Version != 1 || profile.Steps == null || profile.Steps.Count > 10000)
                throw new ArgumentException("Unsupported sequence file.");
            if (profile.CountdownSeconds < 1 || profile.CountdownSeconds > 30 || profile.RepeatCount < 0 || profile.RepeatCount > 100000 ||
                profile.LoopDelayMs < 0 || profile.LoopDelayMs > 60000 || profile.SessionSeconds < 0 || profile.SessionSeconds > 86400)
                throw new ArgumentException("Invalid replay settings.");
            foreach (MacroStep step in profile.Steps)
                if (step == null || NativeInput.NormalizeInput(step.Input) == null || step.DelayMs < 0 || step.DelayMs > 86400000 ||
                    step.X.HasValue != step.Y.HasValue || step.X.HasValue && (step.X.Value < 0 || step.Y.Value < 0))
                    throw new ArgumentException("Invalid sequence event.");
            Populate(profile.Steps);
            loadingSettings = true;
            try
            {
                countdown.Value = profile.CountdownSeconds;
                loops.Value = profile.RepeatCount;
                gap.Value = profile.LoopDelayMs;
                limit.Value = profile.SessionSeconds;
                indefinite.Checked = profile.RepeatCount == 0 && profile.SessionSeconds == 0;
            }
            finally { loadingSettings = false; }
            SetEditing();
            SetStatus("Sequence loaded - ready to review", false);
        }
        void LoadSequence(bool report)
        {
            if (IsBusy) return;
            try
            {
                SavedSequence profile = new JavaScriptSerializer().Deserialize<SavedSequence>(File.ReadAllText(profilePath));
                if (profile == null || profile.Version != 1 || profile.Steps == null || profile.Steps.Count > 10000)
                    throw new ArgumentException("Unsupported sequence file.");
                if (profile.CountdownSeconds < 1 || profile.CountdownSeconds > 30 || profile.RepeatCount < 0 || profile.RepeatCount > 100000 ||
                    profile.LoopDelayMs < 0 || profile.LoopDelayMs > 60000 || profile.SessionSeconds < 0 || profile.SessionSeconds > 86400)
                    throw new ArgumentException("Invalid replay settings.");
                foreach (MacroStep step in profile.Steps)
                    if (step == null || NativeInput.NormalizeInput(step.Input) == null || step.DelayMs < 0 || step.DelayMs > 86400000 ||
                        step.X.HasValue != step.Y.HasValue || step.X.HasValue && (step.X.Value < 0 || step.Y.Value < 0))
                        throw new ArgumentException("Invalid sequence event.");
                Populate(profile.Steps);
                loadingSettings = true;
                try
                {
                    countdown.Value = profile.CountdownSeconds;
                    loops.Value = profile.RepeatCount;
                    gap.Value = profile.LoopDelayMs;
                    limit.Value = profile.SessionSeconds;
                    indefinite.Checked = profile.RepeatCount == 0 && profile.SessionSeconds == 0;
                }
                finally { loadingSettings = false; }
                SetEditing();
                SetStatus("Sequence loaded - stopped. Review the target and event timings before replaying.", false);
            }
            catch (Exception ex) { if (report) SetStatus("Could not load sequence: " + ex.Message, true); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                Stop();
                disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
