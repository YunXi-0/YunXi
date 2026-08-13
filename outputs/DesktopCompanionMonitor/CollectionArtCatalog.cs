using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PcCompanionMonitor;

internal enum CollectionArtKind
{
    Diamond,
    Candy,
    Crystal,
    Pumpkin,
}

internal sealed record CollectionArt(
    CollectionArtKind Kind,
    Color Primary,
    Color Secondary,
    Color Accent);

internal static class CollectionArtCatalog
{
    public static readonly IReadOnlyList<CollectionArt> All =
    [
        new(CollectionArtKind.Diamond, Color.FromArgb(70, 190, 255), Color.FromArgb(25, 95, 145), Color.FromArgb(235, 252, 255)),
        new(CollectionArtKind.Diamond, Color.FromArgb(255, 125, 190), Color.FromArgb(175, 35, 110), Color.FromArgb(255, 240, 250)),
        new(CollectionArtKind.Diamond, Color.FromArgb(110, 220, 145), Color.FromArgb(30, 120, 70), Color.FromArgb(235, 255, 240)),
        new(CollectionArtKind.Diamond, Color.FromArgb(255, 175, 60), Color.FromArgb(190, 95, 25), Color.FromArgb(255, 245, 220)),
        new(CollectionArtKind.Diamond, Color.FromArgb(190, 130, 255), Color.FromArgb(110, 50, 180), Color.FromArgb(245, 235, 255)),

        new(CollectionArtKind.Candy, Color.FromArgb(255, 96, 145), Color.FromArgb(210, 35, 75), Color.FromArgb(255, 245, 248)),
        new(CollectionArtKind.Candy, Color.FromArgb(85, 180, 255), Color.FromArgb(35, 90, 175), Color.FromArgb(245, 252, 255)),
        new(CollectionArtKind.Candy, Color.FromArgb(200, 120, 255), Color.FromArgb(125, 45, 190), Color.FromArgb(250, 245, 255)),
        new(CollectionArtKind.Candy, Color.FromArgb(255, 175, 55), Color.FromArgb(205, 95, 25), Color.FromArgb(255, 250, 235)),
        new(CollectionArtKind.Candy, Color.FromArgb(130, 220, 120), Color.FromArgb(45, 130, 60), Color.FromArgb(245, 255, 240)),

        new(CollectionArtKind.Crystal, Color.FromArgb(155, 95, 225), Color.FromArgb(80, 40, 135), Color.FromArgb(225, 205, 255)),
        new(CollectionArtKind.Crystal, Color.FromArgb(90, 190, 255), Color.FromArgb(35, 90, 175), Color.FromArgb(225, 245, 255)),
        new(CollectionArtKind.Crystal, Color.FromArgb(255, 110, 210), Color.FromArgb(175, 35, 125), Color.FromArgb(255, 230, 248)),
        new(CollectionArtKind.Crystal, Color.FromArgb(255, 195, 70), Color.FromArgb(190, 105, 25), Color.FromArgb(255, 248, 220)),
        new(CollectionArtKind.Crystal, Color.FromArgb(115, 225, 190), Color.FromArgb(25, 130, 95), Color.FromArgb(230, 255, 245)),

        new(CollectionArtKind.Pumpkin, Color.FromArgb(255, 143, 40), Color.FromArgb(195, 85, 25), Color.FromArgb(80, 145, 55)),
        new(CollectionArtKind.Pumpkin, Color.FromArgb(135, 205, 90), Color.FromArgb(55, 125, 45), Color.FromArgb(220, 175, 60)),
        new(CollectionArtKind.Pumpkin, Color.FromArgb(180, 120, 255), Color.FromArgb(105, 50, 180), Color.FromArgb(70, 180, 110)),
        new(CollectionArtKind.Pumpkin, Color.FromArgb(90, 175, 255), Color.FromArgb(35, 90, 175), Color.FromArgb(75, 175, 95)),
        new(CollectionArtKind.Pumpkin, Color.FromArgb(245, 95, 95), Color.FromArgb(165, 35, 50), Color.FromArgb(85, 155, 60)),
    ];

    public static CollectionArt RandomArt() => All[Random.Shared.Next(All.Count)];

    public static Bitmap Render(CollectionArt art) => art.Kind switch
    {
        CollectionArtKind.Diamond => RenderDiamond(art),
        CollectionArtKind.Candy => RenderCandy(art),
        CollectionArtKind.Crystal => RenderCrystal(art),
        CollectionArtKind.Pumpkin => RenderPumpkin(art),
        _ => RenderDiamond(art),
    };

    private static Bitmap NewCanvas()
    {
        Bitmap bitmap = new(20, 20, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        return bitmap;
    }

    private static Bitmap RenderDiamond(CollectionArt art)
    {
        Bitmap bitmap = NewCanvas();
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        using SolidBrush primary = new(art.Primary);
        using SolidBrush accent = new(art.Accent);
        using Pen pen = new(art.Secondary);
        Point[] outline = [new(10, 0), new(19, 10), new(10, 19), new(0, 10)];
        Point[] facet = [new(10, 0), new(14, 10), new(10, 19), new(6, 10)];
        g.FillPolygon(primary, outline);
        g.DrawPolygon(pen, outline);
        g.FillPolygon(accent, facet);
        g.DrawLine(pen, 10, 0, 10, 19);
        g.DrawLine(pen, 0, 10, 19, 10);
        g.DrawLine(pen, 6, 5, 14, 5);
        g.DrawLine(pen, 6, 15, 14, 15);
        return bitmap;
    }

    private static Bitmap RenderCandy(CollectionArt art)
    {
        Bitmap bitmap = NewCanvas();
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        using SolidBrush primary = new(art.Primary);
        using SolidBrush secondary = new(art.Secondary);
        using Pen primaryPen = new(art.Primary);
        using Pen accentPen = new(art.Accent);
        Point[] left = [new(2, 8), new(7, 5), new(7, 15), new(2, 12)];
        Point[] right = [new(18, 8), new(13, 5), new(13, 15), new(18, 12)];
        g.FillPolygon(primary, left);
        g.DrawPolygon(primaryPen, left);
        g.FillPolygon(primary, right);
        g.DrawPolygon(primaryPen, right);
        Rectangle body = new(5, 4, 10, 12);
        g.FillEllipse(secondary, body);
        g.DrawEllipse(accentPen, body);
        g.DrawLine(accentPen, 8, 4, 8, 16);
        g.DrawLine(accentPen, 12, 4, 12, 16);
        return bitmap;
    }

    private static Bitmap RenderCrystal(CollectionArt art)
    {
        Bitmap bitmap = NewCanvas();
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        using SolidBrush primary = new(art.Primary);
        using SolidBrush secondary = new(art.Secondary);
        using SolidBrush accent = new(art.Accent);
        using Pen pen = new(art.Secondary);
        Point[] outline = [new(10, 0), new(16, 9), new(11, 19), new(5, 13)];
        Point[] facet = [new(10, 0), new(16, 9), new(8, 12), new(4, 7)];
        Point[] lower = [new(5, 13), new(8, 12), new(11, 19)];
        g.FillPolygon(primary, outline);
        g.DrawPolygon(pen, outline);
        g.FillPolygon(accent, facet);
        g.DrawPolygon(pen, facet);
        g.FillPolygon(secondary, lower);
        g.DrawPolygon(pen, lower);
        g.DrawLine(pen, 10, 0, 8, 12);
        g.DrawLine(pen, 4, 7, 16, 9);
        return bitmap;
    }

    private static Bitmap RenderPumpkin(CollectionArt art)
    {
        Bitmap bitmap = NewCanvas();
        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.None;
        using SolidBrush primary = new(art.Primary);
        using SolidBrush secondary = new(art.Secondary);
        using SolidBrush accent = new(art.Accent);
        using Pen pen = new(art.Secondary);
        Rectangle body = new(1, 7, 18, 12);
        g.FillEllipse(primary, body);
        g.DrawEllipse(pen, body);
        Rectangle lobe = new(4, 7, 12, 12);
        g.FillEllipse(primary, lobe);
        g.DrawLine(pen, 5, 8, 5, 19);
        g.DrawLine(pen, 10, 8, 10, 19);
        g.DrawLine(pen, 15, 8, 15, 19);
        Rectangle stem = new(9, 3, 2, 5);
        g.FillRectangle(accent, stem);
        g.DrawRectangle(pen, stem);
        Point[] leftEye = [new(6, 11), new(9, 11), new(7, 14)];
        Point[] rightEye = [new(11, 11), new(14, 11), new(13, 14)];
        g.FillPolygon(secondary, leftEye);
        g.FillPolygon(secondary, rightEye);
        g.DrawLine(pen, 7, 16, 13, 16);
        return bitmap;
    }
}
