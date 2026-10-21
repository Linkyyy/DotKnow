using System.Drawing;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DotKnow.App.Windows;

internal static class TrayAppearance
{
    public static void Apply(Forms.ContextMenuStrip menu)
    {
        static System.Drawing.Color Get(string key)
        {
            var color = ((SolidColorBrush)System.Windows.Application.Current.FindResource(key)).Color;
            return System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        }
        var colors = new MenuColors(Get("SurfaceBrush"), Get("SelectionBrush"), Get("BorderBrush"), Get("FocusBrush"), Get("AccentBrush"));
        menu.BackColor = colors.Surface;
        menu.ForeColor = Get("TextBrush");
        foreach (Forms.ToolStripItem item in menu.Items) item.ForeColor = menu.ForeColor;
        menu.Renderer = new MenuRenderer(colors);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        menu.Invalidate();
    }

    private sealed class MenuColors(System.Drawing.Color surface, System.Drawing.Color selection, System.Drawing.Color border, System.Drawing.Color focus, System.Drawing.Color accent) : Forms.ProfessionalColorTable
    {
        public System.Drawing.Color Surface => surface;
        public System.Drawing.Color Accent => accent;
        public override System.Drawing.Color ToolStripDropDownBackground => surface;
        public override System.Drawing.Color MenuItemSelected => selection;
        public override System.Drawing.Color MenuItemBorder => focus;
        public override System.Drawing.Color MenuBorder => border;
        public override System.Drawing.Color SeparatorDark => border;
        public override System.Drawing.Color SeparatorLight => surface;
        public override System.Drawing.Color ImageMarginGradientBegin => surface;
        public override System.Drawing.Color ImageMarginGradientMiddle => surface;
        public override System.Drawing.Color ImageMarginGradientEnd => surface;
        public override System.Drawing.Color CheckBackground => selection;
        public override System.Drawing.Color CheckSelectedBackground => selection;
        public override System.Drawing.Color CheckPressedBackground => selection;
    }

    private sealed class MenuRenderer(MenuColors colors) : Forms.ToolStripProfessionalRenderer(colors)
    {
        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var bounds = e.ImageRectangle;
            using var pen = new System.Drawing.Pen(colors.Accent, 1.8f);
            var middle = bounds.Top + bounds.Height / 2f;
            e.Graphics.DrawLines(pen, [new PointF(bounds.Left + 2, middle), new PointF(bounds.Left + 6, middle + 4), new PointF(bounds.Right - 2, middle - 4)]);
        }
    }
}
