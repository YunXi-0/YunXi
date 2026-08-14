using System.Drawing;

namespace PcCompanionMonitor;

internal sealed record ThemePalette(
    string Name,
    Color Accent,
    bool Dark);

internal static class ThemeCatalog
{
    public static readonly IReadOnlyList<ThemePalette> All =
    [
        new("经典", Color.FromArgb(25, 92, 167), false),
        new("深色", Color.FromArgb(59, 130, 246), true),
        new("霜蓝", Color.FromArgb(135, 215, 255), false),
        new("樱粉", Color.FromArgb(255, 150, 205), false),
        new("薄荷", Color.FromArgb(120, 235, 190), false),
        new("柠檬", Color.FromArgb(255, 225, 80), false),
        new("珊瑚", Color.FromArgb(255, 130, 105), false),
        new("靛青", Color.FromArgb(90, 120, 240), false),
        new("葡萄", Color.FromArgb(180, 110, 240), false),
        new("海盐", Color.FromArgb(110, 210, 230), false),
        new("蜜桃", Color.FromArgb(255, 185, 140), false),
        new("青柠", Color.FromArgb(175, 230, 90), false),
        new("玫瑰", Color.FromArgb(245, 95, 140), false),
        new("天蓝", Color.FromArgb(80, 185, 250), false),
        new("暖阳", Color.FromArgb(255, 195, 65), false),
        new("紫藤", Color.FromArgb(170, 120, 235), false),
        new("抹茶", Color.FromArgb(150, 205, 100), false),
        new("赤金", Color.FromArgb(235, 155, 45), false),
        new("冰晶", Color.FromArgb(150, 235, 245), false),
        new("梅子", Color.FromArgb(215, 90, 175), false),
        new("湖绿", Color.FromArgb(70, 185, 165), false),
        new("琥珀", Color.FromArgb(255, 170, 70), false),
    ];
}
