using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace PcCompanionMonitor;

internal enum ChartKind
{
    Combined,
    Powered,
    Awake,
    Active,
    MouseTotal,
    MouseLeft,
    MouseRight,
    Keyboard,
}

internal sealed record DailyStatsPoint(
    DateTime Date,
    TimeSpan Powered,
    TimeSpan Awake,
    TimeSpan Active,
    long MouseTotal = 0,
    long MouseLeft = 0,
    long MouseRight = 0,
    long Keyboard = 0);

internal sealed class StatisticsChartPanel : Panel
{
    private IReadOnlyList<DailyStatsPoint> _points = [];
    private ChartKind _kind;
    private int _days = 7;
    private RectangleF _plot;
    private double _max;
    private bool _count;
    private int _hoverIndex = -1;
    private Color _background;
    private Color _textColor;
    private Color _mutedColor;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DarkMode { get; set; }

    public StatisticsChartPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.White;
    }

    public void SetData(int days, IReadOnlyList<DailyStatsPoint> points, ChartKind kind)
    {
        _days = days;
        _points = points;
        _kind = kind;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color background = DarkMode ? Color.FromArgb(30, 34, 42) : Color.White;
        Color textColor = DarkMode ? Color.FromArgb(226, 232, 240) : Color.Black;
        Color mutedColor = DarkMode ? Color.FromArgb(148, 163, 184) : Color.FromArgb(120, 126, 134);
        Color gridColor = DarkMode ? Color.FromArgb(55, 62, 72) : Color.FromArgb(225, 228, 232);
        Color axisColor = DarkMode ? Color.FromArgb(148, 163, 184) : Color.FromArgb(80, 88, 98);
        _background = background;
        _textColor = textColor;
        _mutedColor = mutedColor;
        using SolidBrush textBrush = new(textColor);
        using SolidBrush mutedBrush = new(mutedColor);
        g.Clear(background);
        if (_points.Count == 0)
        {
            g.DrawString("暂无数据", Font, mutedBrush, ClientRectangle, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            return;
        }

        bool count = _kind is ChartKind.MouseTotal or ChartKind.MouseLeft or ChartKind.MouseRight or ChartKind.Keyboard;
        double max = Math.Max(1, _points.Max(p => GetValue(p)));
        if (!count) max = Math.Max(3600, max);
        if (!count) max = Math.Ceiling(max / 3600.0);
        else max = Math.Ceiling(max);

        RectangleF plot = new(58, 46, Math.Max(1, Width - 74), Math.Max(1, Height - 82));
        _plot = plot;
        _max = max;
        _count = count;
        g.DrawString($"过去{_days}天 {Title()}", new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), textBrush, new RectangleF(10, 8, Width - 20, 22), new StringFormat { Alignment = StringAlignment.Center });

        using Pen grid = new(gridColor) { DashStyle = DashStyle.Dot };
        using Pen axis = new(axisColor);
        for (int i = 0; i <= 4; i++)
        {
            float ratio = i / 4f;
            float y = plot.Bottom - ratio * plot.Height;
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            g.DrawString(FormatAxis(max * ratio, count), new Font("Microsoft YaHei UI", 8f), textBrush, plot.Left - 6, y, new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });
        }

        int interval = Math.Max(1, (int)Math.Ceiling(_points.Count / 6.0));
        for (int i = 0; i < _points.Count; i++)
        {
            if (i != 0 && i != _points.Count - 1 && i % interval != 0) continue;
            float x = plot.Left + i / (float)(_points.Count - 1) * plot.Width;
            g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
            g.DrawString(_points[i].Date.ToString("MM-dd"), new Font("Microsoft YaHei UI", 8f), textBrush, x, plot.Bottom + 4, new StringFormat { Alignment = StringAlignment.Center });
        }
        g.DrawRectangle(axis, plot.Left, plot.Top, plot.Width, plot.Height);

        if (_kind == ChartKind.Combined)
        {
            DrawLine(g, plot, max, count, Color.FromArgb(25, 92, 167), p => p.Powered.TotalSeconds);
            DrawLine(g, plot, max, count, Color.FromArgb(46, 158, 107), p => p.Awake.TotalSeconds);
            DrawLine(g, plot, max, count, Color.FromArgb(217, 83, 79), p => p.Active.TotalSeconds);
        }
        else
        {
            (Color Color, Func<DailyStatsPoint, double> Selector) data = _kind switch
            {
                ChartKind.Powered => (Color.FromArgb(25, 92, 167), p => p.Powered.TotalSeconds),
                ChartKind.Awake => (Color.FromArgb(46, 158, 107), p => p.Awake.TotalSeconds),
                ChartKind.Active => (Color.FromArgb(217, 83, 79), p => p.Active.TotalSeconds),
                ChartKind.MouseTotal => (Color.FromArgb(25, 92, 167), p => p.MouseTotal),
                ChartKind.MouseLeft => (Color.FromArgb(46, 158, 107), p => p.MouseLeft),
                ChartKind.MouseRight => (Color.FromArgb(217, 83, 79), p => p.MouseRight),
                ChartKind.Keyboard => (Color.FromArgb(142, 68, 173), p => p.Keyboard),
                _ => (Color.Gray, p => 0d),
            };
            DrawLine(g, plot, max, count, data.Color, data.Selector);
        }

        DrawHover(g);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_kind == ChartKind.Combined || _points.Count < 2)
        {
            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                Invalidate();
            }
            return;
        }

        int nearest = -1;
        double best = 12;
        for (int i = 0; i < _points.Count; i++)
        {
            double distance = Math.Abs(e.X - GetPoint(_plot, i).X);
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        if (nearest != _hoverIndex)
        {
            _hoverIndex = nearest;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Invalidate();
        }
    }

    private void DrawHover(Graphics g)
    {
        if (_kind == ChartKind.Combined || _hoverIndex < 0 || _hoverIndex >= _points.Count)
        {
            return;
        }

        PointF point = GetPoint(_plot, _hoverIndex);
        using Pen dash = new(Color.FromArgb(120, 80, 88, 98))
        {
            DashStyle = DashStyle.Dash,
        };
        g.DrawLine(dash, point.X, point.Y, point.X, _plot.Bottom);
        using SolidBrush backgroundBrush = new(_background);
        using Pen textPen = new(_textColor);
        g.FillEllipse(backgroundBrush, point.X - 4, point.Y - 4, 8, 8);
        g.DrawEllipse(textPen, point.X - 4, point.Y - 4, 8, 8);

        double value = GetValue(_points[_hoverIndex]);
        string text = $"{_points[_hoverIndex].Date:MM-dd}\n{Title()}：{FormatValue(value, _count)}";
        using Font font = new("Microsoft YaHei UI", 8f);
        SizeF size = g.MeasureString(text, font);
        RectangleF box = new(point.X + 10, point.Y - 20, Math.Max(120, size.Width + 12), size.Height + 8);
        if (box.Right > Width - 4)
        {
            box.X = point.X - box.Width - 10;
        }
        if (box.Bottom > Height - 4)
        {
            box.Y = Height - box.Height - 4;
        }
        if (box.X < 2)
        {
            box.X = 2;
        }
        if (box.Y < 2)
        {
            box.Y = 2;
        }

        g.FillRectangle(backgroundBrush, box);
        using Pen mutedPen = new(_mutedColor);
        g.DrawRectangle(mutedPen, box.X, box.Y, box.Width, box.Height);
        using SolidBrush hoverTextBrush = new(_textColor);
        g.DrawString(text, font, hoverTextBrush, box);
    }

    private PointF GetPoint(RectangleF plot, int index)
    {
        double value = GetValue(_points[index]);
        double normalized = _count ? value / _max : (value / 3600.0) / _max;
        float x = plot.Left + index / (float)(_points.Count - 1) * plot.Width;
        return new PointF(x, plot.Bottom - (float)normalized * plot.Height);
    }

    private void DrawLine(Graphics g, RectangleF plot, double max, bool count, Color color, Func<DailyStatsPoint, double> selector)
    {
        if (_points.Count < 2) return;
        PointF[] pts = new PointF[_points.Count];
        for (int i = 0; i < _points.Count; i++)
        {
            double v = selector(_points[i]);
            double normalized = count ? v / max : (v / 3600.0) / max;
            pts[i] = new PointF(plot.Left + i / (float)(_points.Count - 1) * plot.Width, plot.Bottom - (float)normalized * plot.Height);
        }
        using Pen pen = new(color, 1.6f);
        g.DrawLines(pen, pts);
    }

    private double GetValue(DailyStatsPoint p) => _kind switch
    {
        ChartKind.Powered => p.Powered.TotalSeconds,
        ChartKind.Awake => p.Awake.TotalSeconds,
        ChartKind.Active => p.Active.TotalSeconds,
        ChartKind.MouseTotal => p.MouseTotal,
        ChartKind.MouseLeft => p.MouseLeft,
        ChartKind.MouseRight => p.MouseRight,
        ChartKind.Keyboard => p.Keyboard,
        _ => Math.Max(Math.Max(p.Powered.TotalSeconds, p.Awake.TotalSeconds), p.Active.TotalSeconds),
    };

    private string Title() => _kind switch
    {
        ChartKind.Powered => "运行时间",
        ChartKind.Awake => "非睡眠时间",
        ChartKind.Active => "高强度使用",
        ChartKind.MouseTotal => "鼠标点击总数",
        ChartKind.MouseLeft => "左键点击",
        ChartKind.MouseRight => "右键点击",
        ChartKind.Keyboard => "键盘敲击",
        _ => "综合折线图",
    };

    private static string FormatAxis(double value, bool count)
    {
        if (count)
        {
            long v = (long)Math.Round(value);
            if (v >= 100_000_000) return $"{v / 100_000_000d:0.#}亿";
            if (v >= 10_000) return $"{v / 10_000d:0.#}万";
            return v.ToString("N0");
        }

        int total = (int)Math.Round(value * 60);
        return $"{total / 60}:{total % 60:D2}";
    }

    private static string FormatValue(double value, bool count)
    {
        if (count)
        {
            return ((long)Math.Round(value)).ToString("N0");
        }

        int total = (int)Math.Round(value / 60);
        return $"{total / 60}:{total % 60:D2}";
    }
}
