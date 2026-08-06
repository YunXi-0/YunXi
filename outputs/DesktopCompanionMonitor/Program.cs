using System.Drawing.Imaging;
using System.Windows.Forms;

namespace PcCompanionMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (args.Length >= 3 && args[1] == "--render-stats")
        {
            RunRender(args[2], MainForm.UiPage.Stats, 1);
            return;
        }

        if (args.Length >= 3 && args[1] == "--render-stats-input")
        {
            RunRender(args[2], MainForm.UiPage.Stats, 2);
            return;
        }

        if (args.Length >= 3 && args[1] == "--render-settings")
        {
            RunRender(args[2], MainForm.UiPage.Settings, 1);
            return;
        }

        if (args.Length >= 3 && args[1] == "--render-leaderboard")
        {
            RunRender(args[2], MainForm.UiPage.Leaderboard, 1);
            return;
        }

        if (args.Length >= 3 && args[1] == "--render-data1")
        {
            RunRender(args[2], MainForm.UiPage.Data, 1);
            return;
        }

        if (args.Length >= 3 && args[1] == "--render-data2")
        {
            RunRender(args[2], MainForm.UiPage.Data, 2);
            return;
        }

        if (args.Length >= 3 && args[1] == "--render-data3")
        {
            RunRender(args[2], MainForm.UiPage.Data, 3);
            return;
        }

        if (args.Length >= 3 && args[1] == "--render-perf")
        {
            RunRender(args[2], MainForm.UiPage.Performance, 1);
            return;
        }

        if (args.Length >= 3 && args[1] == "--render-stats-awake")
        {
            RunRender(args[2], MainForm.UiPage.Stats, 1, 7, ChartKind.Awake);
            return;
        }

        if (args.Length >= 3 && args[1] == "--render-stats-active90")
        {
            RunRender(args[2], MainForm.UiPage.Stats, 1, 90, ChartKind.Active);
            return;
        }

        using Mutex mutex = new(true, @"Local\PcCompanionMonitor.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        AppLog.Info("应用启动");
        ActivityStore store = new();
        Application.Run(new MainForm(store));
        AppLog.Info("应用退出");
        mutex.ReleaseMutex();
    }

    private static void RunRender(string path, MainForm.UiPage page, int view, int period = 7, ChartKind kind = ChartKind.Combined)
    {
        ApplicationConfiguration.Initialize();
        ActivityStore store = new();
        using MainForm form = new(store, page, view, period, kind);
        form.Show();
        for (int i = 0; i < 20; i++)
        {
            Application.DoEvents();
            Thread.Sleep(100);
        }
        using Bitmap bitmap = new(form.ClientSize.Width, form.ClientSize.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bitmap.Save(path, ImageFormat.Png);
    }
}
