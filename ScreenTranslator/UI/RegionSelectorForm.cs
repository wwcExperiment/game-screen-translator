using System.Drawing;
using System.Windows.Forms;

namespace ScreenTranslator.UI;

/// <summary>全屏半透明覆盖层，鼠标拖拽框选一块屏幕区域。</summary>
public sealed class RegionSelectorForm : Form
{
    private Point _start;
    private bool _dragging;
    private Rectangle _selection;

    /// <summary>框选结果（屏幕坐标），未框选则为 null。</summary>
    public Rectangle? SelectedRegion { get; private set; }

    public RegionSelectorForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        BackColor = Color.Black;
        Opacity = 0.3;
        ShowInTaskbar = false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        _start = e.Location;
        _dragging = true;
        _selection = Rectangle.Empty;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging)
            return;
        _selection = Normalize(_start, e.Location);
        Invalidate();
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left)
            return;
        _dragging = false;

        var rect = Normalize(PointToScreen(_start), PointToScreen(e.Location));
        if (rect.Width >= 4 && rect.Height >= 4)
        {
            SelectedRegion = rect;
            DialogResult = DialogResult.OK;
        }
        Close();
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
            Close();
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_dragging && _selection.Width > 0 && _selection.Height > 0)
        {
            using var pen = new Pen(Color.DeepSkyBlue, 2);
            using var brush = new SolidBrush(Color.FromArgb(40, Color.DeepSkyBlue));
            e.Graphics.FillRectangle(brush, _selection);
            e.Graphics.DrawRectangle(pen, _selection);
        }
    }

    private static Rectangle Normalize(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(a.X - b.X);
        var h = Math.Abs(a.Y - b.Y);
        return new Rectangle(x, y, w, h);
    }
}
