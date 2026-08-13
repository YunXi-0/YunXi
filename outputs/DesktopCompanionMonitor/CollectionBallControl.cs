using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PcCompanionMonitor;

internal sealed class CollectionBallControl : Control
{
    private CollectionArt? _art;
    private Bitmap? _artBitmap;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CollectionArt? Art
    {
        get => _art;
        set
        {
            if (ReferenceEquals(_art, value))
            {
                return;
            }

            _art = value;
            _artBitmap?.Dispose();
            _artBitmap = value is null ? null : CollectionArtCatalog.Render(value);
            Invalidate();
        }
    }

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
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_artBitmap is null)
        {
            return;
        }

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(
            _artBitmap,
            new Rectangle(0, 0, Width, Height),
            new Rectangle(0, 0, _artBitmap.Width, _artBitmap.Height),
            GraphicsUnit.Pixel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _artBitmap?.Dispose();
            _artBitmap = null;
        }

        base.Dispose(disposing);
    }
}
