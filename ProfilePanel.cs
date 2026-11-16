using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FarmMacro
{
    /// <summary>A local library of clicker, timer and recorded-sequence setups.</summary>
    public sealed class ProfilePanel : UserControl
    {
        readonly Func<MacroWorkspaceProfile> capture;
        readonly Action<MacroWorkspaceProfile> apply;
        readonly Func<bool> busy;
        readonly ProfileLibrary library;
        readonly string presetsFolder;
        readonly ListBox names = new ListBox();
        readonly TextBox search = new TextBox();
        readonly List<string> savedNames = new List<string>();
        readonly TextBox profileName = new TextBox();
        readonly Label countLabel = new Label();
        readonly Label emptyLabel = new Label();
        readonly Label summary = new Label();
        readonly Label statusLabel = new Label();
        readonly Button loadButton = new ModernButton();
        bool refreshing;
        string status = "Save a name to keep your clicker, timers and recording together.";

        public string Status { get { return status; } }

        public ProfilePanel(Func<MacroWorkspaceProfile> capture, Action<MacroWorkspaceProfile> apply, Func<bool> busy)
            : this(capture, apply, busy, new ProfileLibrary(), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets")) { }

        internal ProfilePanel(Func<MacroWorkspaceProfile> capture, Action<MacroWorkspaceProfile> apply, Func<bool> busy, ProfileLibrary library, string presetsFolder)
        {
            if (capture == null || apply == null || busy == null || library == null || String.IsNullOrWhiteSpace(presetsFolder)) throw new ArgumentNullException();
            this.capture = capture;
            this.apply = apply;
            this.busy = busy;
            this.library = library;
            this.presetsFolder = Path.GetFullPath(presetsFolder);
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            Font = UiTheme.BodyFont;
            AutoScroll = true;
            AutoScrollMinSize = new Size(0, 270);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(12);
            layout.Margin = new Padding(0);
            layout.ColumnCount = 2;
            layout.RowCount = 1;
            layout.MinimumSize = new Size(660, 270);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            CardPanel libraryCard = new CardPanel();
            libraryCard.Dock = DockStyle.Fill;
            libraryCard.Padding = new Padding(14);
            libraryCard.Margin = new Padding(0, 0, 12, 0);
            layout.Controls.Add(libraryCard, 0, 0);

            TableLayoutPanel libraryLayout = new TableLayoutPanel();
            libraryLayout.Dock = DockStyle.Fill;
            libraryLayout.ColumnCount = 1;
            libraryLayout.RowCount = 5;
            libraryLayout.Margin = new Padding(0);
            libraryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            libraryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            libraryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            libraryCard.Controls.Add(libraryLayout);
            countLabel.Text = "SAVED PROFILES";
            countLabel.ForeColor = UiTheme.Muted;
            countLabel.Dock = DockStyle.Fill;
            countLabel.Font = new Font(Font, FontStyle.Bold);
            libraryLayout.Controls.Add(countLabel, 0, 0);

            TableLayoutPanel searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 5) };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.Controls.Add(new Label { Text = "Find", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted, Margin = new Padding(0) }, 0, 0);
            search.Dock = DockStyle.Top;
            search.BackColor = UiTheme.SurfaceRaised;
            search.ForeColor = UiTheme.Text;
            search.BorderStyle = BorderStyle.FixedSingle;
            search.Margin = new Padding(0, 3, 0, 0);
            search.MaxLength = 60;
            search.AccessibleName = "Find saved profiles";
            search.AccessibleDescription = "Filter saved profile names. Escape clears the search.";
            search.TextChanged += delegate { FilterProfiles(); };
            search.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { search.Clear(); e.SuppressKeyPress = true; } };
            searchRow.Controls.Add(search, 1, 0);
            libraryLayout.Controls.Add(searchRow, 0, 1);

            Panel listHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            libraryLayout.Controls.Add(listHost, 0, 2);
            names.Dock = DockStyle.Fill;
            names.BorderStyle = BorderStyle.None;
            names.BackColor = UiTheme.Surface;
            names.ForeColor = UiTheme.Text;
            names.IntegralHeight = false;
            names.DrawMode = DrawMode.OwnerDrawFixed;
            names.ItemHeight = 38;
            names.AccessibleName = "Saved profile names";
            names.DrawItem += DrawProfile;
            names.SelectedIndexChanged += delegate { if (!refreshing) SelectProfile(); };
            names.DoubleClick += delegate { LoadSelected(); };
            listHost.Controls.Add(names);
            emptyLabel.Dock = DockStyle.Fill;
            emptyLabel.ForeColor = UiTheme.Muted;
            emptyLabel.Text = "No profiles yet.\r\n\r\nSave your setup or choose an included preset below.";
            emptyLabel.Padding = new Padding(2, 18, 6, 0);
            listHost.Controls.Add(emptyLabel);

            Button presets = new ModernButton();
            presets.Text = "Included presets";
            presets.Dock = DockStyle.Fill;
            presets.Margin = new Padding(0, 5, 0, 3);
            presets.AccessibleDescription = "Browse the portable example profiles included with this app. Adding one never starts playback.";
            UiTheme.StyleButton(presets, true);
            presets.Click += delegate { BrowsePresets(); };
            libraryLayout.Controls.Add(presets, 0, 3);

            TableLayoutPanel libraryLinks = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            libraryLinks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            libraryLinks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            libraryLayout.Controls.Add(libraryLinks, 0, 4);
            LinkLabel refresh = new LinkLabel();
            refresh.Text = "Refresh";
            refresh.Dock = DockStyle.Fill;
            refresh.TextAlign = ContentAlignment.BottomLeft;
            refresh.LinkColor = UiTheme.Accent;
            refresh.ActiveLinkColor = UiTheme.Text;
            refresh.VisitedLinkColor = UiTheme.Accent;
            refresh.LinkClicked += delegate { RefreshProfiles(); };
            libraryLinks.Controls.Add(refresh, 0, 0);
            LinkLabel openLibrary = new LinkLabel { Text = "Open folder", Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomRight, LinkColor = UiTheme.Accent, ActiveLinkColor = UiTheme.Text, VisitedLinkColor = UiTheme.Accent };
            openLibrary.AccessibleDescription = "Open the saved profile library in File Explorer.";
            openLibrary.LinkClicked += delegate
            {
                try { Directory.CreateDirectory(library.FolderPath); OpenFolder(library.FolderPath); }
                catch (Exception error) { ShowStatus("Could not open library: " + error.Message, true); }
            };
            libraryLinks.Controls.Add(openLibrary, 1, 0);

            CardPanel editorCard = new CardPanel();
            editorCard.Dock = DockStyle.Fill;
            editorCard.Padding = new Padding(18, 14, 18, 12);
            editorCard.Margin = new Padding(0);
            layout.Controls.Add(editorCard, 1, 0);
            TableLayoutPanel editor = new TableLayoutPanel();
            editor.Dock = DockStyle.Fill;
            editor.ColumnCount = 1;
            editor.RowCount = 7;
            editor.Margin = new Padding(0);
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            editorCard.Controls.Add(editor);

            Label title = new Label();
            title.Text = "Your setups, ready to load";
            title.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            title.Dock = DockStyle.Fill;
            title.ForeColor = UiTheme.Text;
            editor.Controls.Add(title, 0, 0);
            Label description = new Label();
            description.Text = "Save clicks, keyboard timers and recordings together in one named setup.";
            description.ForeColor = UiTheme.Muted;
            description.Dock = DockStyle.Fill;
            editor.Controls.Add(description, 0, 1);
            Label nameLabel = new Label();
            nameLabel.Text = "PROFILE NAME";
            nameLabel.Font = new Font(Font, FontStyle.Bold);
            nameLabel.ForeColor = UiTheme.Muted;
            nameLabel.Dock = DockStyle.Fill;
            editor.Controls.Add(nameLabel, 0, 2);
            profileName.Dock = DockStyle.Top;
            profileName.MaxLength = 60;
            profileName.BackColor = UiTheme.SurfaceRaised;
            profileName.ForeColor = UiTheme.Text;
            profileName.BorderStyle = BorderStyle.FixedSingle;
            profileName.Font = new Font("Segoe UI", 11F);
            profileName.AccessibleName = "Profile name";
            profileName.Margin = new Padding(0, 1, 0, 0);
            profileName.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SaveCurrent(); }
            };
            editor.Controls.Add(profileName, 0, 3);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = new Padding(0);
            buttons.WrapContents = false;
            Button saveButton = new ModernButton();
            saveButton.Text = "Save current";
            saveButton.Size = new Size(120, 34);
            saveButton.Margin = new Padding(0, 3, 10, 0);
            UiTheme.StyleButton(saveButton, true);
            saveButton.Click += delegate { SaveCurrent(); };
            buttons.Controls.Add(saveButton);
            loadButton.Text = "Load selected";
            loadButton.Size = new Size(120, 34);
            loadButton.Margin = new Padding(0, 3, 10, 0);
            UiTheme.StyleButton(loadButton, false);
            loadButton.Enabled = false;
            loadButton.Click += delegate { LoadSelected(); };
            buttons.Controls.Add(loadButton);
            Button importButton = new ModernButton();
            importButton.Text = "Import";
            importButton.Size = new Size(84, 34);
            importButton.Margin = new Padding(0, 3, 10, 0);
            UiTheme.StyleButton(importButton, false);
            importButton.AccessibleDescription = "Add a JSON profile to this library without loading or starting it.";
            importButton.Click += delegate { ImportProfile(); };
            buttons.Controls.Add(importButton);
            Button exportButton = new ModernButton();
            exportButton.Text = "Export";
            exportButton.Size = new Size(84, 34);
            exportButton.Margin = new Padding(0, 3, 0, 0);
            UiTheme.StyleButton(exportButton, false);
            exportButton.AccessibleDescription = "Save the selected profile as a portable JSON file.";
            exportButton.Click += delegate { ExportProfile(); };
            buttons.Controls.Add(exportButton);
            editor.Controls.Add(buttons, 0, 4);

            summary.Dock = DockStyle.Fill;
            summary.ForeColor = UiTheme.Muted;
            summary.Text = "Load a setup, then choose a mode to run.";
            summary.AutoEllipsis = true;
            summary.Margin = new Padding(0, 5, 0, 0);
            editor.Controls.Add(summary, 0, 5);
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.ForeColor = UiTheme.Muted;
            statusLabel.AutoEllipsis = true;
            statusLabel.Text = status;
            statusLabel.Margin = new Padding(0, 5, 0, 0);
            editor.Controls.Add(statusLabel, 0, 6);
            VisibleChanged += delegate { if (Visible) RefreshProfiles(); };
            RefreshProfiles();
        }

        void DrawProfile(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= names.Items.Count) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush brush = new SolidBrush(selected ? UiTheme.SurfaceRaised : UiTheme.Surface))
                e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected)
                using (SolidBrush accent = new SolidBrush(UiTheme.Accent))
                    e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + 7, 3, e.Bounds.Height - 14);
            Rectangle text = new Rectangle(e.Bounds.Left + 12, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 18), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, Convert.ToString(names.Items[e.Index]), names.Font, text,
                selected ? UiTheme.Text : UiTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }

        public void RefreshProfiles()
        {
            try
            {
                List<string> availableNames = library.ListNames();
                savedNames.Clear();
                savedNames.AddRange(availableNames);
                savedNames.Sort(StringComparer.OrdinalIgnoreCase);
                FilterProfiles();
            }
            catch (Exception error) { ShowStatus("Could not read profiles: " + error.Message, true); }
        }

        void FilterProfiles()
        {
            string selected = names.SelectedItem as string;
            string query = search.Text.Trim();
            refreshing = true;
            names.BeginUpdate();
            try
            {
                names.Items.Clear();
                foreach (string name in savedNames)
                    if (query.Length == 0 || name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) names.Items.Add(name);
                if (selected != null && names.Items.Contains(selected)) names.SelectedItem = selected;
                else if (names.Items.Count > 0) names.SelectedIndex = 0;
            }
            finally { names.EndUpdate(); refreshing = false; }
            emptyLabel.Text = savedNames.Count == 0 ? "No profiles yet.\r\n\r\nSave a setup or add an included preset." : "No matching profiles.\r\n\r\nClear Find to see all saved setups.";
            emptyLabel.Visible = names.Items.Count == 0;
            names.Visible = names.Items.Count != 0;
            countLabel.Text = query.Length == 0 ? "SAVED PROFILES  " + savedNames.Count : "MATCHES  " + names.Items.Count + " / " + savedNames.Count;
            loadButton.Enabled = names.SelectedItem != null;
            if (names.SelectedItem != null) SelectProfile();
            else summary.Text = savedNames.Count == 0 ? "Save or add a setup, then choose Load selected." : "Clear Find or enter another name to choose a setup.";
        }

        void SelectProfile()
        {
            string selected = names.SelectedItem as string;
            loadButton.Enabled = selected != null;
            if (selected == null) return;
            profileName.Text = selected;
            try
            {
                MacroWorkspaceProfile profile = library.Load(selected);
                int enabled = 0;
                foreach (MacroAction action in profile.Timers.Actions) if (action.Enabled) enabled++;
                QuickClickSettings clicker = profile.Clicker ?? new QuickClickSettings();
                summary.Text = (clicker.UseMultiplePoints ? clicker.Points.Count + " ordered click points" : clicker.Input) + "  \u2022  " + enabled + " timer" + (enabled == 1 ? "" : "s") + "  \u2022  " +
                    profile.Sequence.Steps.Count + " recorded events";
            }
            catch (Exception error) { ShowStatus("Could not inspect profile: " + error.Message, true); }
        }

        void SaveCurrent()
        {
            if (busy()) { ShowStatus("Stop playback or recording before saving a profile.", true); return; }
            string name = profileName.Text.Trim();
            if (name.Length == 0) { ShowStatus("Enter a name for this profile.", true); profileName.Focus(); return; }
            try
            {
                MacroWorkspaceProfile profile = capture();
                if (profile == null) throw new InvalidOperationException("The current setup could not be captured.");
                profile.Name = name;
                library.Save(profile);
                search.Clear();
                RefreshProfiles();
                names.SelectedItem = profile.Name;
                profileName.Text = profile.Name;
                ShowStatus("Saved \u201c" + profile.Name + "\u201d. All three modes are included.", false);
            }
            catch (Exception error) { ShowStatus("Could not save: " + error.Message, true); }
        }

        void LoadSelected()
        {
            if (busy()) { ShowStatus("Stop playback or recording before loading a profile.", true); return; }
            string selected = names.SelectedItem as string;
            if (selected == null) { ShowStatus("Choose a saved profile first.", true); return; }
            try
            {
                MacroWorkspaceProfile profile = library.Load(selected);
                apply(profile);
                profileName.Text = profile.Name;
                ShowStatus("Loaded \u201c" + profile.Name + "\u201d. Ready when you are.", false);
            }
            catch (Exception error) { ShowStatus("Could not load: " + error.Message, true); }
        }

        void ImportProfile()
        {
            if (busy()) { ShowStatus("Stop playback or recording before importing a profile.", true); return; }
            try
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Import a Flow Macro profile";
                    dialog.Filter = "Macro profile (*.json)|*.json|All files (*.*)|*.*";
                    dialog.DefaultExt = "json";
                    dialog.CheckFileExists = true;
                    dialog.CheckPathExists = true;
                    dialog.Multiselect = false;
                    dialog.ValidateNames = true;
                    dialog.RestoreDirectory = true;
                    if (Directory.Exists(presetsFolder)) dialog.InitialDirectory = presetsFolder;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    ImportFile(dialog.FileName);
                }
            }
            catch (Exception error) { ShowStatus("Could not import: " + error.Message, true); }
        }

        void BrowsePresets()
        {
            if (busy()) { ShowStatus("Stop playback or recording before adding a preset.", true); return; }
            try
            {
                using (BundledPresetDialog dialog = new BundledPresetDialog(presetsFolder))
                    if (dialog.ShowDialog(this) == DialogResult.OK) ImportFile(dialog.SelectedPath);
            }
            catch (Exception error) { ShowStatus("Could not read included presets: " + error.Message, true); }
        }

        internal void ImportFile(string path)
        {
            if (busy()) { ShowStatus("Stop playback or recording before importing a profile.", true); return; }
            MacroWorkspaceProfile profile = ProfileTransfer.Import(path);
            profile.Name = UniqueImportName(profile.Name);
            library.Save(profile);
            search.Clear();
            RefreshProfiles();
            names.SelectedItem = profile.Name;
            profileName.Text = profile.Name;
            ShowStatus("Added to library. Choose Load selected, then a mode to run.", false);
        }

        internal static void OpenFolder(string path)
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }

        // Called only by the app's explicit --preview path. The dialog registers
        // no hotkeys, runs no timers and never opens a folder or imports a file.
        internal void RenderIncludedPresetsPreview(string filePath)
        {
            using (BundledPresetDialog dialog = new BundledPresetDialog(presetsFolder, true))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-20000, -20000);
                dialog.Show();
                dialog.PerformLayout();
                Application.DoEvents();
                using (Bitmap image = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
                    image.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
                }
                dialog.Hide();
            }
        }

        void ExportProfile()
        {
            if (busy()) { ShowStatus("Stop playback or recording before exporting a profile.", true); return; }
            try
            {
                string selected = names.SelectedItem as string;
                MacroWorkspaceProfile profile;
                if (selected != null) profile = library.Load(selected);
                else
                {
                    string name = profileName.Text.Trim();
                    if (name.Length == 0)
                    {
                        ShowStatus("Enter a profile name before exporting the current setup.", true);
                        profileName.Focus();
                        return;
                    }
                    profile = capture();
                    if (profile == null) throw new InvalidOperationException("The current setup could not be captured.");
                    profile.Name = name;
                    ProfileLibrary.Validate(profile);
                }
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "Export a Flow Macro profile";
                    dialog.Filter = "Macro profile (*.json)|*.json|All files (*.*)|*.*";
                    dialog.DefaultExt = "json";
                    dialog.AddExtension = true;
                    dialog.CheckPathExists = true;
                    dialog.OverwritePrompt = true;
                    dialog.ValidateNames = true;
                    dialog.RestoreDirectory = true;
                    dialog.FileName = SuggestedExportName(profile.Name);
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    if (busy()) { ShowStatus("Stop playback or recording before exporting a profile.", true); return; }
                    ProfileTransfer.Export(profile, dialog.FileName);
                    ShowStatus("Exported \u201c" + profile.Name + "\u201d as a JSON profile.", false);
                }
            }
            catch (Exception error) { ShowStatus("Could not export: " + error.Message, true); }
        }

        string UniqueImportName(string importedName)
        {
            string original = importedName.Trim().Normalize();
            HashSet<string> existing = new HashSet<string>(library.ListNames(), StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(original)) return original;
            for (int index = 1; index <= 10000; index++)
            {
                string suffix = index == 1 ? " (imported)" : " (imported " + index + ")";
                int maxBaseLength = 60 - suffix.Length;
                string stem = original.Length > maxBaseLength ? original.Substring(0, maxBaseLength) : original;
                if (stem.Length > 0 && char.IsHighSurrogate(stem[stem.Length - 1])) stem = stem.Substring(0, stem.Length - 1);
                string candidate = stem.TrimEnd() + suffix;
                if (!existing.Contains(candidate)) return candidate;
            }
            throw new InvalidOperationException("Choose a different profile name before importing this file.");
        }

        static string SuggestedExportName(string name)
        {
            char[] chars = name.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int index = 0; index < chars.Length; index++)
                if (Array.IndexOf(invalid, chars[index]) >= 0) chars[index] = '_';
            string clean = new string(chars).Trim().TrimEnd('.');
            return "FarmMacro-" + clean + ".json";
        }

        void ShowStatus(string message, bool error)
        {
            status = message;
            statusLabel.Text = message;
            statusLabel.ForeColor = error ? Color.FromArgb(244, 151, 151) : UiTheme.Muted;
        }
    }

    internal sealed class BundledPresetDialog : Form
    {
        readonly ListBox presets = new ListBox();
        readonly Label details = new Label();
        readonly Button add = new ModernButton();
        readonly bool previewMode;
        Icon ownedIcon;
        public string SelectedPath { get; private set; }

        public BundledPresetDialog(string folder, bool preview = false)
        {
            previewMode = preview;
            Text = BrandAssets.AppName + " - Included presets";
            ownedIcon = BrandAssets.LoadIcon();
            Icon = ownedIcon;
            Font = UiTheme.BodyFont;
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(650, 420);
            MinimumSize = new Size(600, 420);

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 6, Margin = new Padding(0) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(layout);
            Label heading = new Label { Text = "Start with an included preset", Dock = DockStyle.Fill, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 16F, FontStyle.Bold), Margin = new Padding(0) };
            layout.Controls.Add(heading, 0, 0);
            Label description = new Label { Text = "Add a setup to your library, then load it when ready.\r\nYour current workspace stays stopped and unchanged.", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Margin = new Padding(0) };
            layout.Controls.Add(description, 0, 1);
            presets.Dock = DockStyle.Fill;
            presets.BackColor = UiTheme.Surface;
            presets.ForeColor = UiTheme.Text;
            presets.BorderStyle = BorderStyle.FixedSingle;
            presets.IntegralHeight = false;
            presets.ItemHeight = 36;
            presets.DrawMode = DrawMode.OwnerDrawFixed;
            presets.AccessibleName = "Included preset names";
            presets.Margin = new Padding(0, 0, 0, 8);
            presets.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= presets.Items.Count) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush brush = new SolidBrush(selected ? UiTheme.SurfaceRaised : UiTheme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
                Rectangle label = new Rectangle(e.Bounds.Left + 12, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 24), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, presets.Items[e.Index].ToString(), presets.Font, label, selected ? UiTheme.Text : UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
            };
            presets.SelectedIndexChanged += delegate
            {
                BundledPresetCatalog.Entry entry = presets.SelectedItem as BundledPresetCatalog.Entry;
                add.Enabled = entry != null;
                details.Text = entry == null ? "Choose a preset to inspect its contents." : entry.Summary;
            };
            layout.Controls.Add(presets, 0, 2);
            details.Dock = DockStyle.Fill;
            details.ForeColor = UiTheme.Muted;
            details.Margin = new Padding(0, 3, 0, 0);
            layout.Controls.Add(details, 0, 3);

            int skipped;
            List<BundledPresetCatalog.Entry> entries = BundledPresetCatalog.Read(folder, out skipped);
            foreach (BundledPresetCatalog.Entry entry in entries) presets.Items.Add(entry);
            Label status = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Margin = new Padding(0), Text = entries.Count + " included preset" + (entries.Count == 1 ? "" : "s") + (skipped == 0 ? "" : "  \u2022  " + skipped + " unreadable file" + (skipped == 1 ? "" : "s") + " skipped") };
            if (entries.Count == 0) details.Text = "No valid presets found. Keep the presets folder beside FarmMacro.exe, or use Profiles \u2192 Import to choose a JSON file.";
            layout.Controls.Add(status, 0, 4);

            TableLayoutPanel buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            add.Text = "Add to library";
            add.Enabled = false;
            add.Dock = DockStyle.Fill;
            add.Margin = new Padding(0, 3, 10, 0);
            UiTheme.StyleButton(add, true);
            add.Click += delegate
            {
                BundledPresetCatalog.Entry entry = presets.SelectedItem as BundledPresetCatalog.Entry;
                if (entry == null) return;
                SelectedPath = entry.FilePath;
                DialogResult = DialogResult.OK;
            };
            buttons.Controls.Add(add, 0, 0);
            Button open = new ModernButton { Text = "Open folder", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 10, 0), Enabled = Directory.Exists(folder) };
            UiTheme.StyleButton(open, false);
            open.Click += delegate
            {
                try { ProfilePanel.OpenFolder(folder); }
                catch (Exception error) { status.Text = "Could not open folder: " + error.Message; status.ForeColor = Color.FromArgb(244, 151, 151); }
            };
            buttons.Controls.Add(open, 1, 0);
            Button close = new ModernButton { Text = "Close", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 3, 0, 0) };
            UiTheme.StyleButton(close, false);
            buttons.Controls.Add(close, 3, 0);
            layout.Controls.Add(buttons, 0, 5);
            CancelButton = close;
            AcceptButton = add;
            if (presets.Items.Count > 0)
            {
                presets.SelectedIndex = 0;
                for (int index = 0; index < presets.Items.Count; index++)
                    if (presets.Items[index].ToString().StartsWith("Starter - ", StringComparison.OrdinalIgnoreCase)) { presets.SelectedIndex = index; break; }
            }
        }

        protected override bool ShowWithoutActivation { get { return previewMode || base.ShowWithoutActivation; } }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && ownedIcon != null) { ownedIcon.Dispose(); ownedIcon = null; }
        }
    }
}
