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
            UpdateRegionFromBitmap();
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegionFromBitmap();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
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

    private void UpdateRegionFromBitmap()
    {
        if (_artBitmap is null || Width <= 0 || Height <= 0)
        {
            Region = null;
            return;
        }

        using GraphicsPath path = new();
        float scaleX = Width / (float)_artBitmap.Width;
        float scaleY = Height / (float)_artBitmap.Height;
        for (int y = 0; y < _artBitmap.Height; y++)
        {
            for (int x = 0; x < _artBitmap.Width; x++)
            {
                if (_artBitmap.GetPixel(x, y).A <= 0)
                {
                    continue;
                }

                path.AddRectangle(new RectangleF(
                    x * scaleX,
                    y * scaleY,
                    Math.Max(1f, scaleX),
                    Math.Max(1f, scaleY)));
            }
        }

        Region replacement = new(path);
        Region? previous = Region;
        Region = replacement;
        previous?.Dispose();
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
