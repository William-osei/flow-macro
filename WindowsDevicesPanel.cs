using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FarmMacro
{
    public sealed class WindowsDevicesPanel : UserControl
    {
        readonly Func<MacroWorkspaceProfile> capture;
        readonly Func<bool> busy;
        readonly Label message = new Label();
        string status = "Map each action on your phone before starting.";
        public string Status { get { return status; } }

        public WindowsDevicesPanel(Func<MacroWorkspaceProfile> capture, Func<bool> busy)
        {
            this.capture = capture; this.busy = busy;
            BackColor = UiTheme.Background; ForeColor = UiTheme.Text; Font = UiTheme.BodyFont;
            Dock = DockStyle.Fill; AutoScroll = true; AutoScrollMinSize = new Size(0, 270);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, MinimumSize = new Size(650, 270), Padding = new Padding(12), ColumnCount = 2, RowCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
            Controls.Add(layout);
            CardPanel android = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 12), Margin = new Padding(0, 0, 12, 0) };
            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            content.Controls.Add(new Label { Text = "Take your routine to Android", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14, FontStyle.Bold) }, 0, 0);
            content.Controls.Add(new Label { Text = "Export routine timers as a mobile draft. On your phone, choose a target app and map each action to its on-screen point. Desktop keyboard recordings are kept separate.", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }, 0, 1);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            Button export = new ModernButton { Text = "Export Android draft", Size = new Size(180, 34), Margin = new Padding(0, 0, 10, 0) };
            UiTheme.StyleButton(export, true); export.Click += delegate { ExportDraft(); }; buttons.Controls.Add(export);
            Button folder = new ModernButton { Text = "Android files", Size = new Size(118, 34) };
            UiTheme.StyleButton(folder, false); folder.Click += delegate { OpenAndroidFiles(); }; buttons.Controls.Add(folder);
            content.Controls.Add(buttons, 0, 2);
            content.Controls.Add(new Label { Text = "The native Android app needs a manually enabled accessibility service. It performs taps only in the chosen app. Imports require fresh point mapping; they never start automation.", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Padding = new Padding(0, 5, 0, 0) }, 0, 3);
            message.Text = status; message.Dock = DockStyle.Fill; message.AutoEllipsis = true; message.ForeColor = UiTheme.Muted;
            content.Controls.Add(message, 0, 4);
            android.Controls.Add(content); layout.Controls.Add(android, 0, 0);
            CardPanel platforms = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = new Padding(0) };
            Label compatibility = new Label { Dock = DockStyle.Fill, Text = "PLATFORM SUPPORT\n\nWindows\nNative timers and recordings\n\nAndroid\nNative tap runner, first release\n\nmacOS / Linux / iOS\nFuture adapters; not included yet", ForeColor = UiTheme.Muted };
            platforms.Controls.Add(compatibility); layout.Controls.Add(platforms, 1, 0);
        }
        void ExportDraft()
        {
            if (busy()) { SetStatus("Stop playback or recording before exporting.", true); return; }
            try
            {
                MacroWorkspaceProfile workspace = capture();
                MobileProfileExport.CreateDraft(workspace);
                using (SaveFileDialog dialog = new SaveFileDialog { Title = "Export an Android timer draft", Filter = "Mobile macro profile (*.json)|*.json", DefaultExt = "json", AddExtension = true, OverwritePrompt = true, FileName = "FlowMacro-mobile.json", CheckPathExists = true, RestoreDirectory = true })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    if (busy()) { SetStatus("Stop the macro before exporting.", true); return; }
                    MobileProfileExport.Export(workspace, dialog.FileName);
                    SetStatus("Android draft exported. Map its points on your phone before starting.", false);
                }
            }
            catch (Exception ex) { SetStatus("Could not export: " + ex.Message, true); }
        }
        void OpenAndroidFiles()
        {
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "android", "dist");
                if (!Directory.Exists(folder)) { SetStatus("Android files are not present beside this executable.", true); return; }
                System.Diagnostics.Process.Start(folder);
            }
            catch (Exception ex) { SetStatus("Could not open Android files: " + ex.Message, true); }
        }
        void SetStatus(string text, bool error) { status = text; message.Text = text; message.ForeColor = error ? Color.Salmon : UiTheme.Muted; }
    }
}
