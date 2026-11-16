using System;
using System.Drawing;
using System.Windows.Forms;

namespace FarmMacro
{
    public sealed class HelpForm : Form
    {
        readonly Icon appIcon;
        readonly Image mark;
        readonly bool previewMode;

        public HelpForm(string shortcutLegend) : this(shortcutLegend, false) { }
        public HelpForm(string shortcutLegend, bool preview)
        {
            previewMode = preview;
            Text = BrandAssets.AppName + " - Quick help";
            Size = new Size(740, 650);
            MinimumSize = new Size(600, 460);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            Font = UiTheme.BodyFont;
            appIcon = BrandAssets.LoadIcon(); Icon = appIcon;
            mark = BrandAssets.CreateMark(96);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            Controls.Add(layout);
            Panel header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new PictureBox { Image = mark, SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 2), Size = new Size(48, 48) });
            header.Controls.Add(new Label { Text = BrandAssets.AppName + " " + BrandAssets.Version, Location = new Point(62, 0), AutoSize = true, Font = new Font("Segoe UI", 21, FontStyle.Bold) });
            header.Controls.Add(new Label { Text = "A short guide to your local macro workspace.", Location = new Point(64, 42), AutoSize = true, ForeColor = UiTheme.Muted });
            layout.Controls.Add(header, 0, 0);
            ModernTabControl tabs = new ModernTabControl { Dock = DockStyle.Fill };
            UiTheme.StyleTabs(tabs);
            TabPage guide = Page("Getting started");
            TabPage updates = Page("What's new");
            tabs.TabPages.Add(guide); tabs.TabPages.Add(updates);
            FlowLayoutPanel guideFlow = Sections(guide);
            AddCard(guideFlow, "Start with the mouse clicker", "Choose a target window, a mouse button and an interval. Use single, double or custom click bursts, then focus the target during the countdown. Choose an exact total, a timed session or infinite clicking.");
            AddCard(guideFlow, "Choose a point", "Click at the current pointer or save a fixed point with the displayed F11 shortcut while stopped. Points are relative to the target window. Recheck them after resizing or changing its layout.");
            AddCard(guideFlow, "Build a routine", "Routines give each key or click an independent timer. Record & replay preserves order, including holds, chords and click positions. F8 starts or finishes recording. Continuous pointer and camera movement are not recorded.");
            AddCard(guideFlow, "Keep a cooldown after focus pauses", "Enable Keep CD on a single-press timer row to keep its next allowed time when you return to the target. Its first press uses the initial delay. Explicit Stop/Start begins a new session. This is a timer, not a reading of the target app's cooldown display.");
            AddCard(guideFlow, "Counted or continuous", "A mouse total counts individual clicks, including clicks in a burst. The last burst can be partial and its final hold releases normally. Keep Run indefinitely checked to repeat until stopped. F10 releases held inputs immediately.");
            AddCard(guideFlow, "Focus and the system tray", "Switching away from the target pauses input. The selected app still needs focus while the macro is hidden in the tray. Stop before typing inside the target. Double-click the tray icon to restore the window.");
            AddCard(guideFlow, "Check any input", "Check input can send a selected key or mouse button once or twice. Use the local input test pad to inspect received events. Counts mean Windows accepted the input; check what the receiving app did with it.");
            AddCard(guideFlow, "Match Windows permissions", "If the target runs as administrator, close the macro and use Start Farm Macro as Administrator.cmd, reviewing Windows' prompt yourself, or run the target normally. Higher or unknown target privileges block starting.");
            AddCard(guideFlow, "Your profiles and Android", "Profiles stores the mouse clicker, timer rows and recording together. Loading always stays stopped. Devices exports a timer draft for Android, where each tap point needs fresh mapping in the chosen app.");
            AddCard(guideFlow, "Your current shortcuts", String.IsNullOrWhiteSpace(shortcutLegend) ? "F9 starts or stops the session. F10 stops. F8 records. F11 captures a click point. Check the main window for Ctrl+Alt alternatives if a key is reserved." : shortcutLegend);
            FlowLayoutPanel updatesFlow = Sections(updates);
            AddCard(updatesFlow, "Version 1.5 · Presets within reach", "Open Profiles → Included presets to browse starter setups and personal examples. Add one to your library, then choose Load selected. Folder shortcuts help you find saved profiles and portable files. Loading always stays stopped.");
            AddCard(updatesFlow, "Version 1.4.1 · Cooldown timers", "Optional cooldown-preserving rows wait their full interval after the last accepted press, including across focus or modifier pauses. The personal C/V/Z preset adds a 24-second Z timer without moving the cursor.");
            AddCard(updatesFlow, "Version 1.4 · Flow Macro", "A general-purpose workspace with a dedicated mouse clicker, five clear pages, neutral starting settings and no automatic game selection. Existing FarmMacro.exe launchers and local profiles remain compatible.");
            AddCard(updatesFlow, "More control over clicks", "Left, right and middle buttons; single, double and custom bursts; fixed or current-pointer points; exact click totals; and a rate estimate that accounts for hold and release time.");
            AddCard(updatesFlow, "A cleaner recorder", "Start empty or use a general click or Space example. Clear and replacement protect existing editor events. The personal C/V example is optional and saved game routines remain available.");
            AddCard(updatesFlow, "General input checks", "Choose any supported key or mouse button instead of a game-specific pair. Test once or twice, with a configurable countdown and hold. Permission checks and emergency stop remain in place.");
            AddCard(updatesFlow, "Profiles grow with the app", "Named setups now include mouse-clicker settings. Older profiles keep their existing timers and recordings and receive a default clicker. Android now starts with a general Tap 1 routine and numbered test points.");
            layout.Controls.Add(tabs, 0, 1);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            Button close = new ModernButton { Text = "Close", Size = new Size(102, 32), DialogResult = DialogResult.Cancel };
            UiTheme.StyleButton(close, false);
            close.Click += delegate { Close(); };
            footer.Controls.Add(close);
            layout.Controls.Add(footer, 0, 2);
            CancelButton = close;
            Shown += delegate
            {
                if (previewMode) return;
                Rectangle area = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 16), Math.Min(MinimumSize.Height, area.Height - 16));
                Size = new Size(Math.Min(Width, area.Width - 16), Math.Min(Height, area.Height - 16));
            };
            Disposed += delegate { mark.Dispose(); appIcon.Dispose(); };
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

        static TabPage Page(string title)
        {
            return new TabPage(title) { BackColor = UiTheme.Background, ForeColor = UiTheme.Text, Padding = new Padding(0, 8, 0, 0) };
        }
        static FlowLayoutPanel Sections(TabPage page)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(4) };
            page.Controls.Add(panel);
            panel.Resize += delegate { foreach (Control control in panel.Controls) FitCard(control, panel.ClientSize.Width); };
            return panel;
        }
        static void AddCard(FlowLayoutPanel parent, string title, string body)
        {
            CardPanel card = new CardPanel { Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 0, 0, 10), Tag = body };
            Label heading = new Label { Text = title, Dock = DockStyle.Top, Height = 24, Font = new Font(UiTheme.BodyFont, FontStyle.Bold), ForeColor = UiTheme.Text };
            Label description = new Label { Text = body, Dock = DockStyle.Fill, ForeColor = UiTheme.Muted };
            card.Controls.Add(description); card.Controls.Add(heading);
            parent.Controls.Add(card);
            FitCard(card, parent.ClientSize.Width);
        }
        static void FitCard(Control card, int width)
        {
            card.Width = Math.Max(200, width - 28);
            Size measured = TextRenderer.MeasureText(Convert.ToString(card.Tag), UiTheme.BodyFont, new Size(Math.Max(100, card.Width - 30), 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            card.Height = Math.Max(90, measured.Height + 50);
        }
    }
}
