using System.Drawing;
using System.Windows.Forms;

namespace AssetStudioGUI
{
    // Catppuccin palettes (https://catppuccin.com/palette), applied over the WinForms light/dark color modes
    internal sealed class CatppuccinPalette
    {
        public string Name { get; init; }
        public bool IsDark { get; init; }
        public Color Base { get; init; }
        public Color Mantle { get; init; }
        public Color Crust { get; init; }
        public Color Surface0 { get; init; }
        public Color Surface1 { get; init; }
        public Color Overlay0 { get; init; }
        public Color Text { get; init; }
        public Color Subtext0 { get; init; }
        public Color Accent { get; init; }  // mauve
        public Color Link { get; init; }    // blue

        private static Color Hex(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        public static readonly CatppuccinPalette Latte = new CatppuccinPalette
        {
            Name = "Latte", IsDark = false,
            Base = Hex(0xeff1f5), Mantle = Hex(0xe6e9ef), Crust = Hex(0xdce0e8),
            Surface0 = Hex(0xccd0da), Surface1 = Hex(0xbcc0cc), Overlay0 = Hex(0x9ca0b0),
            Text = Hex(0x4c4f69), Subtext0 = Hex(0x6c6f85), Accent = Hex(0x8839ef), Link = Hex(0x1e66f5),
        };

        public static readonly CatppuccinPalette Frappe = new CatppuccinPalette
        {
            Name = "Frappé", IsDark = true,
            Base = Hex(0x303446), Mantle = Hex(0x292c3c), Crust = Hex(0x232634),
            Surface0 = Hex(0x414559), Surface1 = Hex(0x51576d), Overlay0 = Hex(0x737994),
            Text = Hex(0xc6d0f5), Subtext0 = Hex(0xa5adce), Accent = Hex(0xca9ee6), Link = Hex(0x8caaee),
        };

        public static readonly CatppuccinPalette Macchiato = new CatppuccinPalette
        {
            Name = "Macchiato", IsDark = true,
            Base = Hex(0x24273a), Mantle = Hex(0x1e2030), Crust = Hex(0x181926),
            Surface0 = Hex(0x363a4f), Surface1 = Hex(0x494d64), Overlay0 = Hex(0x6e738d),
            Text = Hex(0xcad3f5), Subtext0 = Hex(0xa5adcb), Accent = Hex(0xc6a0f6), Link = Hex(0x8aadf4),
        };

        public static readonly CatppuccinPalette Mocha = new CatppuccinPalette
        {
            Name = "Mocha", IsDark = true,
            Base = Hex(0x1e1e2e), Mantle = Hex(0x181825), Crust = Hex(0x11111b),
            Surface0 = Hex(0x313244), Surface1 = Hex(0x45475a), Overlay0 = Hex(0x6c7086),
            Text = Hex(0xcdd6f4), Subtext0 = Hex(0xa6adc8), Accent = Hex(0xcba6f7), Link = Hex(0x89b4fa),
        };

        public static CatppuccinPalette FromTheme(GuiColorTheme theme) => theme switch
        {
            GuiColorTheme.CatppuccinLatte => Latte,
            GuiColorTheme.CatppuccinFrappe => Frappe,
            GuiColorTheme.CatppuccinMacchiato => Macchiato,
            GuiColorTheme.CatppuccinMocha => Mocha,
            _ => null,
        };
    }

    internal static class CatppuccinTheme
    {
        // Set once at startup from the saved theme; null for the stock light/dark/system themes
        public static CatppuccinPalette Current { get; private set; }

        public static void Initialize(GuiColorTheme theme)
        {
            Current = CatppuccinPalette.FromTheme(theme);
            if (Current != null)
            {
                ToolStripManager.Renderer = new CatppuccinToolStripRenderer(Current);
            }
        }

        public static void Apply(Control root)
        {
            if (Current == null)
                return;
            ApplyTo(root, Current);
        }

        private static void ApplyTo(Control control, CatppuccinPalette p)
        {
            switch (control)
            {
                case Form form:
                    form.BackColor = p.Base;
                    form.ForeColor = p.Text;
                    break;
                case TextBoxBase or ListView or TreeView or ComboBox or NumericUpDown or ListBox:
                    control.BackColor = p.Mantle;
                    control.ForeColor = p.Text;
                    if (control is ListView listView)
                        listView.GridLines = false;
                    break;
                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = p.Overlay0;
                    button.FlatAppearance.MouseOverBackColor = p.Surface1;
                    button.FlatAppearance.MouseDownBackColor = p.Overlay0;
                    button.BackColor = p.Surface0;
                    button.ForeColor = p.Text;
                    button.UseVisualStyleBackColor = false;
                    break;
                case LinkLabel link:
                    link.LinkColor = p.Link;
                    link.ActiveLinkColor = p.Accent;
                    link.VisitedLinkColor = p.Accent;
                    link.BackColor = Color.Transparent;
                    break;
                case TabControl tabs:
                    tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                    tabs.DrawItem -= DrawTab;
                    tabs.DrawItem += DrawTab;
                    if (tabs.Tag is not TabStripPainter)
                        tabs.Tag = new TabStripPainter(tabs);
                    break;
                case TabPage page:
                    page.BackColor = p.Base;
                    page.ForeColor = p.Text;
                    page.UseVisualStyleBackColor = false;
                    break;
                case ToolStrip strip:
                    strip.BackColor = p.Crust;
                    strip.ForeColor = p.Text;
                    break;
                case ProgressBar:
                    break;
                default:
                    // Panels, group boxes, labels, check boxes, split containers...
                    control.BackColor = control.BackColor == Color.Transparent ? Color.Transparent : p.Base;
                    control.ForeColor = p.Text;
                    break;
            }

            if (control.ContextMenuStrip != null)
            {
                control.ContextMenuStrip.BackColor = p.Mantle;
                control.ContextMenuStrip.ForeColor = p.Text;
            }

            foreach (Control child in control.Controls)
            {
                ApplyTo(child, p);
            }
        }

        private static void DrawTab(object sender, DrawItemEventArgs e)
        {
            var p = Current;
            var tabs = (TabControl)sender;
            var selected = e.Index == tabs.SelectedIndex;
            using (var back = new SolidBrush(selected ? p.Base : p.Mantle))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }
            if (selected)
            {
                using var accent = new SolidBrush(p.Accent);
                e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Bottom - 2, e.Bounds.Width, 2);
            }
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, e.Font ?? tabs.Font, e.Bounds,
                selected ? p.Text : p.Subtext0, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Windows paints the empty strip next to the tabs itself, ignoring BackColor; paint over it afterwards
    internal sealed class TabStripPainter : NativeWindow
    {
        private const int WM_PAINT = 0x000F;
        private const int WM_PRINTCLIENT = 0x0318;
        private readonly TabControl tabs;

        public TabStripPainter(TabControl tabs)
        {
            this.tabs = tabs;
            if (tabs.IsHandleCreated)
                AssignHandle(tabs.Handle);
            tabs.HandleCreated += (_, _) => AssignHandle(tabs.Handle);
            tabs.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WM_PAINT && m.Msg != WM_PRINTCLIENT || tabs.TabCount == 0 || CatppuccinTheme.Current == null)
                return;
            using var g = m.Msg == WM_PRINTCLIENT ? Graphics.FromHdc(m.WParam) : Graphics.FromHwnd(tabs.Handle);
            var last = tabs.GetTabRect(tabs.TabCount - 1);
            using var back = new SolidBrush(tabs.Parent?.BackColor ?? CatppuccinTheme.Current.Base);
            g.FillRectangle(back, last.Right + 1, 0, tabs.Width - last.Right, last.Bottom + 1);
        }
    }

    internal sealed class CatppuccinToolStripRenderer : ToolStripProfessionalRenderer
    {
        private readonly CatppuccinPalette palette;

        public CatppuccinToolStripRenderer(CatppuccinPalette palette) : base(new CatppuccinColorTable(palette))
        {
            this.palette = palette;
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? palette.Text : palette.Overlay0;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = palette.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Draw our own check mark: the default glyph is black, invisible on dark palettes
            var r = new Rectangle(e.ImageRectangle.X - 1, e.ImageRectangle.Y - 1, e.ImageRectangle.Width + 2, e.ImageRectangle.Height + 2);
            using (var back = new SolidBrush(palette.Surface1))
            {
                e.Graphics.FillRectangle(back, r);
            }
            using var pen = new Pen(palette.Accent, 2f);
            var g = e.Graphics;
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawLines(pen, new[]
            {
                new Point(r.Left + r.Width / 4, r.Top + r.Height / 2),
                new Point(r.Left + r.Width / 2 - 1, r.Bottom - r.Height / 4),
                new Point(r.Right - r.Width / 5, r.Top + r.Height / 4),
            });
            g.SmoothingMode = smoothing;
        }
    }

    internal sealed class CatppuccinColorTable : ProfessionalColorTable
    {
        private readonly CatppuccinPalette p;

        public CatppuccinColorTable(CatppuccinPalette palette)
        {
            p = palette;
            UseSystemColors = false;
        }

        public override Color MenuStripGradientBegin => p.Crust;
        public override Color MenuStripGradientEnd => p.Crust;
        public override Color ToolStripGradientBegin => p.Crust;
        public override Color ToolStripGradientMiddle => p.Crust;
        public override Color ToolStripGradientEnd => p.Crust;
        public override Color ToolStripBorder => p.Crust;
        public override Color StatusStripGradientBegin => p.Crust;
        public override Color StatusStripGradientEnd => p.Crust;
        public override Color ToolStripDropDownBackground => p.Mantle;
        public override Color ImageMarginGradientBegin => p.Mantle;
        public override Color ImageMarginGradientMiddle => p.Mantle;
        public override Color ImageMarginGradientEnd => p.Mantle;
        public override Color MenuBorder => p.Surface1;
        public override Color MenuItemBorder => p.Surface1;
        public override Color MenuItemSelected => p.Surface0;
        public override Color MenuItemSelectedGradientBegin => p.Surface0;
        public override Color MenuItemSelectedGradientEnd => p.Surface0;
        public override Color MenuItemPressedGradientBegin => p.Surface1;
        public override Color MenuItemPressedGradientMiddle => p.Surface1;
        public override Color MenuItemPressedGradientEnd => p.Surface1;
        public override Color ButtonSelectedHighlight => p.Surface0;
        public override Color ButtonSelectedGradientBegin => p.Surface0;
        public override Color ButtonSelectedGradientEnd => p.Surface0;
        public override Color ButtonPressedGradientBegin => p.Surface1;
        public override Color ButtonPressedGradientEnd => p.Surface1;
        public override Color ButtonCheckedGradientBegin => p.Surface1;
        public override Color ButtonCheckedGradientEnd => p.Surface1;
        public override Color CheckBackground => p.Surface1;
        public override Color CheckSelectedBackground => p.Surface1;
        public override Color CheckPressedBackground => p.Surface1;
        public override Color SeparatorDark => p.Surface1;
        public override Color SeparatorLight => p.Surface0;
        public override Color GripDark => p.Surface1;
        public override Color GripLight => p.Surface0;
    }
}
