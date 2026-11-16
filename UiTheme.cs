using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FarmMacro
{
    public sealed class MacroMenuColors : ProfessionalColorTable
    {
        public MacroMenuColors() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return UiTheme.Surface; } }
        public override Color MenuItemSelected { get { return UiTheme.SurfaceRaised; } }
        public override Color MenuItemBorder { get { return UiTheme.Border; } }
        public override Color MenuBorder { get { return UiTheme.Border; } }
        public override Color SeparatorDark { get { return UiTheme.Border; } }
        public override Color SeparatorLight { get { return UiTheme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return UiTheme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return UiTheme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return UiTheme.Surface; } }
        public override Color MenuItemSelectedGradientBegin { get { return UiTheme.SurfaceRaised; } }
        public override Color MenuItemSelectedGradientEnd { get { return UiTheme.SurfaceRaised; } }
        public override Color MenuItemPressedGradientBegin { get { return UiTheme.SurfaceRaised; } }
        public override Color MenuItemPressedGradientMiddle { get { return UiTheme.SurfaceRaised; } }
        public override Color MenuItemPressedGradientEnd { get { return UiTheme.SurfaceRaised; } }
    }

    public static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(14, 20, 34);
        public static readonly Color Surface = Color.FromArgb(25, 34, 55);
        public static readonly Color SurfaceRaised = Color.FromArgb(34, 47, 72);
        public static readonly Color Text = Color.FromArgb(237, 242, 255);
        public static readonly Color Muted = Color.FromArgb(148, 163, 184);
        public static readonly Color Accent = Color.FromArgb(93, 222, 186);
        public static readonly Color Indigo = Color.FromArgb(137, 145, 255);
        public static readonly Color Border = Color.FromArgb(53, 68, 94);
        public static readonly Font BodyFont = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        public static readonly Font ButtonFont = new Font("Segoe UI", 9.5F, FontStyle.Bold);

        public static string InputFailureSummary(Exception error)
        {
            InputPermissionException permission = error as InputPermissionException;
            return permission == null ? error.Message : permission.Headline;
        }
        public static void ShowPermissionDetails(IWin32Window owner, Exception error)
        {
            if (error is InputPermissionException)
                MessageBox.Show(owner, error.Message, BrandAssets.AppName + " - Input permissions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void StyleButton(Button button, bool primary)
        {
            if (button == null) return;
            button.Font = ButtonFont;
            button.AutoEllipsis = true;
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = button is ModernButton ? 0 : 1;
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Blend(Accent, Color.White, 0.12F) : Blend(SurfaceRaised, Indigo, 0.14F);
            button.FlatAppearance.MouseDownBackColor = primary ? Blend(Accent, Background, 0.14F) : Blend(SurfaceRaised, Indigo, 0.23F);
            button.BackColor = primary ? Accent : SurfaceRaised;
            button.ForeColor = primary ? Color.FromArgb(14, 43, 38) : Text;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Cursor = Cursors.Hand;
            ModernButton modern = button as ModernButton;
            if (modern != null) modern.Primary = primary;
        }

        public static void StyleCombo(ComboBox combo)
        {
            if (combo == null) return;
            combo.Font = BodyFont;
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = SurfaceRaised;
            combo.ForeColor = Text;
            combo.IntegralHeight = false;
            combo.MaxDropDownItems = 12;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 24;
            combo.DrawItem -= DrawComboItem;
            combo.DrawItem += DrawComboItem;
        }

        static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null || e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color fill = !edit && selected ? Blend(SurfaceRaised, Indigo, 0.26F) : SurfaceRaised;
            using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, e.Bounds);
            string value = e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
            Rectangle label = new Rectangle(e.Bounds.X + 7, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 14), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, value, combo.Font, label, combo.Enabled ? Text : Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (!edit && (e.State & DrawItemState.Focus) != 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), Accent, fill);
        }

        public static void StyleNumber(NumericUpDown number)
        {
            if (number == null) return;
            number.Font = BodyFont;
            number.BackColor = SurfaceRaised;
            number.ForeColor = Text;
            number.BorderStyle = BorderStyle.FixedSingle;
            number.TextAlign = HorizontalAlignment.Center;
        }

        public static void StyleGrid(DataGridView grid)
        {
            if (grid == null) return;
            grid.Font = BodyFont;
            grid.BackgroundColor = Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Blend(Surface, Border, 0.65F);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.RowHeadersVisible = false;
            grid.AllowUserToResizeRows = false;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 36;
            grid.RowTemplate.Height = Math.Max(32, grid.RowTemplate.Height);
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = Text,
                SelectionBackColor = Blend(SurfaceRaised, Indigo, 0.24F),
                SelectionForeColor = Text,
                Padding = new Padding(7, 3, 7, 3),
                WrapMode = DataGridViewTriState.False,
                Font = BodyFont
            };
            grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Blend(Surface, SurfaceRaised, 0.25F) };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = SurfaceRaised,
                ForeColor = Muted,
                SelectionBackColor = SurfaceRaised,
                SelectionForeColor = Text,
                Font = ButtonFont,
                Padding = new Padding(7, 0, 7, 0),
                WrapMode = DataGridViewTriState.False
            };
            foreach (DataGridViewColumn column in grid.Columns)
            {
                DataGridViewCheckBoxColumn check = column as DataGridViewCheckBoxColumn;
                if (check != null) check.FlatStyle = FlatStyle.Flat;
                DataGridViewComboBoxColumn combo = column as DataGridViewComboBoxColumn;
                if (combo != null)
                {
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.DisplayStyleForCurrentCellOnly = true;
                }
            }
            grid.EditingControlShowing -= StyleGridEditor;
            grid.EditingControlShowing += StyleGridEditor;
        }

        static void StyleGridEditor(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            e.Control.BackColor = SurfaceRaised;
            e.Control.ForeColor = Text;
            e.Control.Font = BodyFont;
            ComboBox combo = e.Control as ComboBox;
            if (combo != null) StyleCombo(combo);
        }

        public static void StyleTabs(TabControl tabs)
        {
            if (tabs == null) return;
            tabs.Font = ButtonFont;
            tabs.DrawMode = tabs is ModernTabControl ? TabDrawMode.Normal : TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(184, 40);
            tabs.Padding = new Point(14, 7);
            tabs.HotTrack = true;
            tabs.BackColor = Background;
            foreach (TabPage page in tabs.TabPages)
            {
                page.BackColor = Background;
                page.ForeColor = Text;
                page.UseVisualStyleBackColor = false;
            }
            tabs.DrawItem -= DrawTabItem;
            if (!(tabs is ModernTabControl)) tabs.DrawItem += DrawTabItem;
            tabs.Invalidate();
        }

        static void DrawTabItem(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = sender as TabControl;
            if (tabs == null || e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
            PaintTab(e.Graphics, tabs, e.Index, e.Bounds);
        }

        internal static void PaintTab(Graphics graphics, TabControl tabs, int index, Rectangle bounds)
        {
            GraphicsState saved = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush background = new SolidBrush(Background)) graphics.FillRectangle(background, bounds);
                Rectangle pill = Rectangle.Inflate(bounds, -4, -4);
                if (pill.Width < 2 || pill.Height < 2) return;
                bool selected = tabs.SelectedIndex == index;
                using (GraphicsPath path = RoundRect(pill, 8))
                {
                    using (SolidBrush fill = new SolidBrush(selected ? SurfaceRaised : Background)) graphics.FillPath(fill, path);
                    if (selected)
                        using (Pen border = new Pen(Blend(Border, Indigo, 0.35F))) graphics.DrawPath(border, path);
                }
                Rectangle label = Rectangle.Inflate(pill, -10, -2);
                TextRenderer.DrawText(graphics, tabs.TabPages[index].Text, tabs.Font, label,
                    selected ? Text : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (selected && tabs.Focused)
                {
                    Rectangle focus = Rectangle.Inflate(pill, -5, -5);
                    ControlPaint.DrawFocusRectangle(graphics, focus, Accent, SurfaceRaised);
                }
            }
            finally { graphics.Restore(saved); }
        }

        internal static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb((int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
        }

        internal static GraphicsPath RoundRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }
            Rectangle arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Retains the native tab pages, selection, hit testing and keyboard handling;
    // paints the full tab surface so the native gray strip cannot show through.
    public class ModernTabControl : TabControl
    {
        bool autoFitTabs;
        bool fittingTabs;
        Control sizingParent;
        public bool AutoFitTabs
        {
            get { return autoFitTabs; }
            set { autoFitTabs = value; FitTabs(); }
        }
        public ModernTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            Font = UiTheme.ButtonFont;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(184, 40);
            Padding = new Point(14, 7);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush fill = new SolidBrush(UiTheme.Background)) e.Graphics.FillRectangle(fill, e.ClipRectangle);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush fill = new SolidBrush(UiTheme.Background)) e.Graphics.FillRectangle(fill, ClientRectangle);
            Rectangle page = DisplayRectangle;
            if (page.Width > 0 && page.Height > 0)
            {
                page.Inflate(1, 1);
                using (Pen border = new Pen(UiTheme.Border)) e.Graphics.DrawRectangle(border, page);
            }
            for (int index = 0; index < TabPages.Count; index++) UiTheme.PaintTab(e.Graphics, this, index, GetTabRect(index));
            base.OnPaint(e);
        }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); FitTabs(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitTabs(); }
        protected override void OnParentChanged(EventArgs e)
        {
            if (sizingParent != null) sizingParent.Resize -= ParentResized;
            base.OnParentChanged(e);
            sizingParent = Parent;
            if (sizingParent != null) sizingParent.Resize += ParentResized;
            FitTabs();
        }
        void ParentResized(object sender, EventArgs e) { FitTabs(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            TabPage page = e.Control as TabPage;
            if (page != null) { page.UseVisualStyleBackColor = false; page.BackColor = UiTheme.Background; page.ForeColor = UiTheme.Text; }
            FitTabs();
            Invalidate();
        }
        protected override void OnControlRemoved(ControlEventArgs e) { base.OnControlRemoved(e); FitTabs(); Invalidate(); }
        void FitTabs()
        {
            if (!autoFitTabs || fittingTabs || TabPages.Count == 0) return;
            fittingTabs = true;
            try
            {
                float scale = Font.Size / UiTheme.ButtonFont.Size;
                int width = ClientSize.Width;
                if (Parent != null) width = Math.Min(width, Parent.ClientSize.Width - Parent.Padding.Horizontal - Margin.Horizontal);
                Size desired = new Size(Math.Max(1, Math.Min((int)(224 * scale), (width - 8) / TabPages.Count)), Math.Max(32, (int)(40 * scale)));
                if (ItemSize != desired) ItemSize = desired;
            }
            finally { fittingTabs = false; }
        }
    }

    public class ModernButton : Button
    {
        bool primary;
        bool hovered;
        bool mousePressed;
        bool keyPressed;

        public bool Primary
        {
            get { return primary; }
            set { if (primary != value) { primary = value; Invalidate(); } }
        }

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            ForeColor = UiTheme.Text;
            Font = UiTheme.ButtonFont;
            Cursor = Cursors.Hand;
            Size = new Size(112, 36);
            Padding = new Padding(10, 0, 10, 0);
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color backdrop = Parent == null ? UiTheme.Background : Parent.BackColor;
            if (backdrop.A < 255) backdrop = UiTheme.Background;
            using (SolidBrush brush = new SolidBrush(backdrop)) e.Graphics.FillRectangle(brush, e.ClipRectangle);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            GraphicsState saved = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool pressed = (mousePressed && hovered) || keyPressed;
                Color fill = primary ? UiTheme.Accent : UiTheme.SurfaceRaised;
                Color label = primary ? Color.FromArgb(14, 43, 38) : UiTheme.Text;
                Color border = primary ? UiTheme.Accent : UiTheme.Border;
                if (!Enabled)
                {
                    fill = UiTheme.Blend(UiTheme.Surface, UiTheme.SurfaceRaised, 0.45F);
                    label = Color.FromArgb(119, 135, 157);
                    border = UiTheme.Blend(UiTheme.Surface, UiTheme.Border, 0.5F);
                }
                else if (pressed)
                {
                    fill = primary ? UiTheme.Blend(fill, UiTheme.Background, 0.18F) : UiTheme.Blend(fill, UiTheme.Indigo, 0.23F);
                    border = primary ? fill : UiTheme.Indigo;
                }
                else if (hovered)
                {
                    fill = primary ? UiTheme.Blend(fill, Color.White, 0.12F) : UiTheme.Blend(fill, UiTheme.Indigo, 0.14F);
                    border = primary ? fill : UiTheme.Blend(UiTheme.Border, UiTheme.Indigo, 0.65F);
                }
                if (Enabled && IsDefault) border = UiTheme.Accent;
                Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = UiTheme.RoundRect(bounds, 8))
                {
                    using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                    using (Pen pen = new Pen(border)) e.Graphics.DrawPath(pen, path);
                }
                Rectangle textBounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
                if (pressed) textBounds.Offset(0, 1);
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
                else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, label, flags);
                if (Enabled && Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -5, -5), label, fill);
            }
            finally { e.Graphics.Restore(saved); }
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool inside = ClientRectangle.Contains(e.Location);
            if (hovered != inside) { hovered = inside; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) mousePressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { mousePressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) mousePressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { keyPressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { keyPressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keyPressed = false; mousePressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) { keyPressed = false; mousePressed = false; } Invalidate(); base.OnEnabledChanged(e); }
    }

    public class CardPanel : Panel
    {
        int cornerRadius = 12;
        bool drawBorder = true;
        public int CornerRadius
        {
            get { return cornerRadius; }
            set { cornerRadius = Math.Max(0, Math.Min(40, value)); Invalidate(); }
        }
        public bool DrawBorder
        {
            get { return drawBorder; }
            set { drawBorder = value; Invalidate(); }
        }
        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.Surface;
            ForeColor = UiTheme.Text;
            Font = UiTheme.BodyFont;
            Padding = new Padding(16);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiTheme.Background : Parent.BackColor);
            if (Width < 2 || Height < 2) return;
            GraphicsState saved = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = UiTheme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), cornerRadius))
                using (SolidBrush fill = new SolidBrush(BackColor)) e.Graphics.FillPath(fill, path);
            }
            finally { e.Graphics.Restore(saved); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (drawBorder && Width > 1 && Height > 1)
            {
                GraphicsState saved = e.Graphics.Save();
                try
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = UiTheme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), cornerRadius))
                    using (Pen pen = new Pen(UiTheme.Border)) e.Graphics.DrawPath(pen, path);
                }
                finally { e.Graphics.Restore(saved); }
            }
            base.OnPaint(e);
        }
    }
}
