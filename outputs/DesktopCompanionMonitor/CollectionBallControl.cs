using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PcCompanionMonitor;

internal sealed class CollectionBallControl : Control
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BallColor { get; set; } = Color.Red;

    public CollectionBallControl()
    {
        SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
        BackColor = Color.Transparent;
        Size = new Size(20, 20);
        Cursor = Cursors.Hand;
        UpdateRegion();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using SolidBrush brush = new(BallColor);
        e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
    }

    private void UpdateRegion()
    {
        using GraphicsPath path = new();
        path.AddEllipse(0, 0, Width - 1, Height - 1);
        Region = new Region(path);
    }
}
