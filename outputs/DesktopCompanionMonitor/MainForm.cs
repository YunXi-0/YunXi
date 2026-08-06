using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PcCompanionMonitor;

internal sealed class MainForm : Form
{
    internal enum UiPage { Data, Performance, Stats, Settings, Leaderboard }

    private readonly MonitorEngine _engine;
    private readonly PerformanceSampler _performance;
    private readonly Label _title;
    private readonly Label[] _timeNames = new Label[3];
    private readonly Label[] _timeValues = new Label[3];
    private readonly Label[] _inputValues = new Label[4];
    private readonly Label[] _maxNames = new Label[3];
    private readonly Label[] _maxValues = new Label[3];
    private readonly Label[] _perfNames = new Label[3];
    private readonly Label[] _perfValues = new Label[3];
    private readonly Label _view1;
    private readonly Label _view2;
    private readonly Label _view3;
    private readonly ToolTip _toolTip;
    private readonly Label _period7;
    private readonly Label _period30;
    private readonly Label _period90;
    private readonly Label[] _kindButtons = new Label[4];
    private readonly Label _inputSummary;
    private readonly StatisticsChartPanel _chart;
    private readonly Label _dataButton;
    private readonly Label _perfButton;
    private readonly Label _statsButton;
    private readonly Label _settingsButton;
    private readonly Label _leaderboardButton;
    private readonly Label _hideButton;
    private readonly Label _changelogButton;
    private readonly Label _checkUpdateButton;
    private readonly Label _themeButton;
    private readonly CheckBox _autoStartCheckBox;
    private readonly Label _settingsStatus;
    private readonly Label _uuidLabel;
    private readonly Label _versionLabel;
    private readonly TextBox _leaderboardIdTextBox;
    private readonly Label _editIdButton;
    private readonly Label _leaderboardStatus;
    private readonly Label[] _leaderboardKindButtons = new Label[5];
    private readonly Label[] _leaderboardEntries = new Label[5];
    private readonly LeaderboardClient _leaderboardClient;
    private readonly DeviceIdentityService _deviceIdentity;
    private string _leaderboardMetric = "active";
    private readonly Dictionary<string, IReadOnlyList<LeaderboardEntry>> _leaderboardBoards = new();
    private bool _leaderboardBusy;
    private DateTimeOffset _lastLeaderboardRefresh;
    private DateTimeOffset _lastLeaderboardUploadUtc;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ContextMenuStrip _trayMenu;
    private readonly NotifyIcon _trayIcon;
    private Form? _changelogForm;

    private UiPage _page;
    private int _view = 1;
    private int _period = 7;
    private ChartKind _chartKind = ChartKind.Combined;
    private DateTimeOffset _lastStatsRefresh;
    private Point _dragOffset;
    private bool _dragging;
    private bool _darkMode;
    private DateTime _randomTextUntil;
    private int _noUpdateClickCount;

    private static readonly string[] RandomUpdateTexts =
    [
        "fufu~",
        "你戳咩啊",
        "吃你家大米啦？",
    ];

    private static readonly ChartKind[] TimeKinds = [ChartKind.Combined, ChartKind.Powered, ChartKind.Awake, ChartKind.Active];
    private static readonly ChartKind[] InputKinds = [ChartKind.MouseTotal, ChartKind.MouseLeft, ChartKind.MouseRight, ChartKind.Keyboard];

    public MainForm(
        ActivityStore store,
        UiPage initialPage = UiPage.Data,
        int initialView = 1,
        int initialPeriod = 7,
        ChartKind initialKind = ChartKind.Combined)
    {
        AppLog.Info("主界面初始化开始");
        Text = "云曦PC统计";
        Icon = CreateTrayIcon();
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(200, 200);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Microsoft YaHei UI", 9f);
        DoubleBuffered = true;

        _engine = new MonitorEngine(store);
        _performance = new PerformanceSampler(Process.GetCurrentProcess());
        _engine.StatsChanged += (_, s) =>
        {
            if (!IsDisposed) UpdateStats(s);
        };

        _title = new Label { Location = new Point(0, 5), Size = new Size(200, 22), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ForeColor = Color.FromArgb(25, 92, 167) };

        string[] timeNames = ["运行时间", "非睡眠时间", "高强度使用"];
        for (int i = 0; i < 3; i++)
        {
            _timeNames[i] = new Label { Text = timeNames[i], Location = new Point(14, 27 + i * 49), Size = new Size(156, 17), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9f), ForeColor = Color.FromArgb(92, 102, 115) };
            _timeValues[i] = new Label { Text = "--:--:--", Location = new Point(14, 44 + i * 49), Size = new Size(156, 32), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold) };
            FitLabelFont(_timeNames[i], 9f);
            FitLabelFont(_timeValues[i], 11.5f);
            Controls.Add(_timeNames[i]);
            Controls.Add(_timeValues[i]);
        }

        string[] inputNames = ["总点击：--", "左键：--", "右键：--", "键盘：--"];
        for (int i = 0; i < 4; i++)
        {
            _inputValues[i] = new Label { Text = inputNames[i], Location = new Point(14, 26 + i * 30), Size = new Size(156, 24), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), Visible = false };
            FitLabelFont(_inputValues[i], 9f);
            Controls.Add(_inputValues[i]);
        }

        string[] perfNames = ["CPU", "GPU", "组件内存"];
        for (int i = 0; i < 3; i++)
        {
            _perfNames[i] = new Label { Text = perfNames[i], Location = new Point(14, 27 + i * 49), Size = new Size(156, 17), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), Visible = false };
            _perfValues[i] = new Label { Text = "相对：--\r\n绝对：--", Location = new Point(14, 44 + i * 49), Size = new Size(156, 32), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 8.5f), Visible = false };
            FitLabelFont(_perfNames[i], 9f);
            FitLabelFont(_perfValues[i], 8.5f);
            Controls.Add(_perfNames[i]);
            Controls.Add(_perfValues[i]);
        }

        string[] maxNames = ["当日最大CPS", "当日最大KPS", "当日最大APS"];
        for (int i = 0; i < 3; i++)
        {
            _maxNames[i] = new Label { Text = maxNames[i], Location = new Point(14, 27 + i * 49), Size = new Size(156, 17), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), Visible = false };
            _maxValues[i] = new Label { Text = "--", Location = new Point(14, 44 + i * 49), Size = new Size(156, 32), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold), Visible = false };
            FitLabelFont(_maxNames[i], 9f);
            FitLabelFont(_maxValues[i], 11.5f);
            Controls.Add(_maxNames[i]);
            Controls.Add(_maxValues[i]);
        }

        _toolTip = new ToolTip();
        _toolTip.SetToolTip(_maxNames[0], "每秒鼠标点击数");
        _toolTip.SetToolTip(_maxNames[1], "每秒键盘操作数");
        _toolTip.SetToolTip(_maxNames[2], "每秒总操作数");

        Controls.Add(_title);

        _view1 = CreateSwitch("1");
        _view2 = CreateSwitch("2");
        _view3 = CreateSwitch("3");
        _view1.Click += (_, _) => SelectView(1);
        _view2.Click += (_, _) => SelectView(2);
        _view3.Click += (_, _) => SelectView(3);
        Controls.Add(_view1);
        Controls.Add(_view2);
        Controls.Add(_view3);

        _period7 = CreateTextButton("7天", new Point(14, 10), new Size(36, 20));
        _period30 = CreateTextButton("30天", new Point(54, 10), new Size(42, 20));
        _period90 = CreateTextButton("90天", new Point(100, 10), new Size(42, 20));
        _period7.Click += (_, _) => SelectPeriod(7);
        _period30.Click += (_, _) => SelectPeriod(30);
        _period90.Click += (_, _) => SelectPeriod(90);
        Controls.Add(_period7);
        Controls.Add(_period30);
        Controls.Add(_period90);

        for (int i = 0; i < 4; i++)
        {
            _kindButtons[i] = CreateTextButton("总", new Point(14 + i * 38, 34), new Size(34, 20));
            int index = i;
            _kindButtons[i].Click += (_, _) => SelectChartKind(index);
            Controls.Add(_kindButtons[i]);
        }

        _inputSummary = new Label { Location = new Point(14, 58), Size = new Size(352, 40), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 8.5f), Visible = false };
        Controls.Add(_inputSummary);

        _chart = new StatisticsChartPanel { Location = new Point(10, 58), Size = new Size(352, 266), BackColor = Color.White, Visible = false };
        Controls.Add(_chart);

        _dataButton = CreateSwitch("数");
        _perfButton = CreateSwitch("性");
        _statsButton = CreateSwitch("统");
        _dataButton.Click += (_, _) => ShowPage(UiPage.Data);
        _perfButton.Click += (_, _) => ShowPage(UiPage.Performance);
        _statsButton.Click += (_, _) => ShowPage(UiPage.Stats);
        _settingsButton = CreateSwitch("设");
        _settingsButton.Click += (_, _) => ShowPage(UiPage.Settings);
        _leaderboardButton = CreateSwitch("榜");
        _leaderboardButton.Click += (_, _) => ShowPage(UiPage.Leaderboard);
        _toolTip.SetToolTip(_dataButton, "数据");
        _toolTip.SetToolTip(_perfButton, "性能");
        _toolTip.SetToolTip(_statsButton, "统计");
        _toolTip.SetToolTip(_settingsButton, "设置");
        _toolTip.SetToolTip(_leaderboardButton, "排行榜");
        Controls.Add(_dataButton);
        Controls.Add(_perfButton);
        Controls.Add(_statsButton);
        Controls.Add(_settingsButton);
        Controls.Add(_leaderboardButton);

        _hideButton = CreateTextButton("隐藏主界面", new Point(30, 26), new Size(140, 24));
        _hideButton.Click += (_, _) =>
        {
            AppLog.Info("用户点击隐藏主界面");
            SetLabelText(_settingsStatus!, "已隐藏，托盘图标继续运行", 8f);
            Hide();
        };
        _autoStartCheckBox = new CheckBox
        {
            Text = "开机启动",
            Location = new Point(30, 52),
            Size = new Size(140, 22),
            Font = new Font("Microsoft YaHei UI", 7f),
            Visible = false,
        };
        _changelogButton = CreateTextButton("更新日志", new Point(30, 76), new Size(140, 24));
        _changelogButton.Click += (_, _) => ShowChangelog();
        _checkUpdateButton = CreateTextButton("检测最新", new Point(30, 102), new Size(140, 24));
        _checkUpdateButton.Click += async (_, _) => await CheckForUpdatesAsync(true);
        _themeButton = CreateTextButton("切", new Point(174, 26), new Size(20, 20));
        _themeButton.Click += (_, _) =>
        {
            _darkMode = !_darkMode;
            ApplyTheme();
            AppLog.Info(_darkMode ? "切换主题：深色" : "切换主题：浅色");
        };
        _toolTip.SetToolTip(_themeButton, "主题切换");
        _settingsStatus = new Label
        {
            Text = "",
            Location = new Point(14, 128),
            Size = new Size(172, 16),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = Color.FromArgb(92, 102, 115),
            Visible = false,
        };
        _versionLabel = new Label
        {
            Text = FormatCurrentVersionLabel(),
            Location = new Point(14, 146),
            Size = new Size(172, 16),
            Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(25, 92, 167),
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false,
        };
        SetLabelText(_versionLabel, _versionLabel.Text, 8f);
        _uuidLabel = new Label
        {
            Text = "--",
            Location = new Point(14, 164),
            Size = new Size(172, 16),
            Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(25, 92, 167),
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false,
        };
        _toolTip.SetToolTip(_uuidLabel, "此数字为本机UUid，安装后固定不变");
        Controls.Add(_hideButton);
        Controls.Add(_autoStartCheckBox);
        Controls.Add(_changelogButton);
        Controls.Add(_checkUpdateButton);
        Controls.Add(_themeButton);
        Controls.Add(_settingsStatus);
        Controls.Add(_versionLabel);
        Controls.Add(_uuidLabel);

        _leaderboardClient = new LeaderboardClient();
        _deviceIdentity = new DeviceIdentityService(_leaderboardClient);
        _lastLeaderboardUploadUtc = DateTimeOffset.UtcNow;
        _leaderboardIdTextBox = new TextBox
        {
            Location = new Point(14, 30),
            Size = new Size(120, 22),
            MaxLength = 10,
            Text = LeaderboardSettingsStore.LoadUserId(),
            Visible = false,
        };
        _leaderboardIdTextBox.TextChanged += (_, _) =>
        {
            string sanitized = LeaderboardSettingsStore.Sanitize(_leaderboardIdTextBox.Text);
            if (sanitized != _leaderboardIdTextBox.Text)
            {
                _leaderboardIdTextBox.Text = sanitized;
                _leaderboardIdTextBox.SelectionStart = sanitized.Length;
            }
            LeaderboardSettingsStore.SaveUserId(sanitized);
        };
        _toolTip.SetToolTip(_leaderboardIdTextBox, "正在加载UUID...");

        _editIdButton = CreateTextButton("修改ID", new Point(190, 28), new Size(90, 28));
        _editIdButton.Click += (_, _) => ShowEditIdDialog();
        Controls.Add(_editIdButton);

        string[] metrics = ["active", "mouse_total", "mouse_left", "mouse_right", "keyboard"];
        string[] labels = ["高强度", "总点击", "左键", "右键", "键盘"];
        for (int i = 0; i < 5; i++)
        {
            int index = i;
            _leaderboardKindButtons[i] = CreateTextButton(
                labels[i],
                new Point(14 + i * 37, 58),
                new Size(34, 16));
            _leaderboardKindButtons[i].Click += (_, _) =>
            {
                _leaderboardMetric = metrics[index];
                UpdateLeaderboardKindButtons();
                UpdateLeaderboardEntriesFromCache();
            };
            Controls.Add(_leaderboardKindButtons[i]);
        }

        _leaderboardStatus = new Label
        {
            Location = new Point(14, 78),
            Size = new Size(172, 16),
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = Color.FromArgb(92, 102, 115),
            Text = "正在同步排行榜...",
            Visible = false,
        };
        Controls.Add(_leaderboardStatus);

        for (int i = 0; i < 5; i++)
        {
            _leaderboardEntries[i] = new Label
            {
                Location = new Point(14, 96 + i * 16),
                Size = new Size(172, 16),
                Font = new Font("Microsoft YaHei UI", 8f),
                Text = $"{i + 1}. 暂无",
                Visible = false,
            };
            Controls.Add(_leaderboardEntries[i]);
        }

        UpdateAutoStartState();
        _autoStartCheckBox.CheckedChanged += (_, _) => ApplyAutoStart(_autoStartCheckBox.Checked);

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("退出", null, (_, _) => Close());
        AttachDrag(this);
        AttachDrag(_title);
        foreach (Label label in _timeNames.Concat(_timeValues).Concat(_inputValues).Concat(_perfNames).Concat(_perfValues)) AttachDrag(label);
        AttachDrag(_chart);

        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("打开界面", null, (_, _) => ShowMainWindow());
        _trayMenu.Items.Add("退出", null, (_, _) => Close());
        _trayIcon = new NotifyIcon { Icon = CreateTrayIcon(), Text = "云曦PC统计", ContextMenuStrip = _trayMenu, Visible = true };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        Shown += (_, _) =>
        {
            PositionLeftMiddle();
            AppLog.Info("窗口位置已设置，开始启动监测引擎");
            _engine.Start();
            _ = LoadUuidAsync();
            _ = CheckForUpdatesAsync(false);
            _ = Task.Run(() => _performance.WarmUp());
            AppLog.Info("监测引擎、UUID、更新检测和性能预热任务已启动");
        };
        FormClosing += (_, _) =>
        {
            AppLog.Info("组件开始关闭");
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _engine.Dispose();
            _performance.Dispose();
            AppLog.Info("组件资源已释放");
        };

        ApplyTheme();
        _view = initialView;
        _period = initialPeriod;
        _chartKind = initialKind;
        AppLog.Info($"主界面初始化完成，初始页面={initialPage}，视图={initialView}");
        ShowPage(initialPage);
    }

    private void UpdateStats(StatsSnapshot snapshot)
    {
        if (_page == UiPage.Data && _view == 1)
        {
            StatsSnapshot day = _engine.GetDaySnapshot(DateTime.Today);
            SetLabelText(_timeValues[0], Format(day.Powered), 11.5f);
            SetLabelText(_timeValues[1], Format(day.Awake), 11.5f);
            SetLabelText(_timeValues[2], Format(day.Active), 11.5f);
        }
        else
        {
            SetLabelText(_timeValues[0], Format(snapshot.Powered), 11.5f);
            SetLabelText(_timeValues[1], Format(snapshot.Awake), 11.5f);
            SetLabelText(_timeValues[2], Format(snapshot.Active), 11.5f);
        }

        if (_page == UiPage.Data && _view == 2) UpdateDataInput();
        if (_page == UiPage.Data && _view == 3) UpdateMaxValues();
        if (_page == UiPage.Stats && _view == 2) UpdateInputSummary();
        if (_page == UiPage.Performance) UpdatePerformance(_performance.Sample());
        if (_lastLeaderboardUploadUtc != default &&
            DateTimeOffset.UtcNow - _lastLeaderboardUploadUtc > TimeSpan.FromSeconds(60))
        {
            _ = UploadAndRefreshLeaderboardAsync();
        }
        if (_page == UiPage.Stats && DateTimeOffset.UtcNow - _lastStatsRefresh > TimeSpan.FromMinutes(1)) RefreshStats();
    }

    private void ShowPage(UiPage page)
    {
        if (page != UiPage.Settings)
        {
            _noUpdateClickCount = 0;
        }
        _page = page;
        AppLog.Info($"切换页面：{page}，视图：{_view}，周期：{_period}，图表类型：{_chartKind}");
        bool stats = page == UiPage.Stats;
        bool settings = page == UiPage.Settings;
        bool leaderboard = page == UiPage.Leaderboard;
        bool dataInput = page == UiPage.Data && _view == 2;
        bool dataMax = page == UiPage.Data && _view == 3;

        _title.Visible = !stats;
        SetLabelText(
            _title,
            page switch
            {
                UiPage.Data => _view switch { 1 => "当日", 2 => "输入统计", _ => "当日极值" },
                UiPage.Performance => "组件性能",
                UiPage.Settings => "设置",
                UiPage.Leaderboard => "每日排行榜",
                _ => "",
            },
            10f);

        for (int i = 0; i < 3; i++)
        {
            _timeNames[i].Visible = page == UiPage.Data && _view == 1;
            _timeValues[i].Visible = page == UiPage.Data && _view == 1;
            _perfNames[i].Visible = page == UiPage.Performance;
            _perfValues[i].Visible = page == UiPage.Performance;
        }
        for (int i = 0; i < 4; i++) _inputValues[i].Visible = dataInput;
        for (int i = 0; i < 3; i++)
        {
            _maxNames[i].Visible = dataMax;
            _maxValues[i].Visible = dataMax;
        }

        _period7.Visible = stats;
        _period30.Visible = stats;
        _period90.Visible = stats;
        foreach (Label kind in _kindButtons) kind.Visible = stats;
        _inputSummary.Visible = stats && _view == 2;
        _chart.Visible = stats;
        _hideButton.Visible = settings;
        _changelogButton.Visible = settings;
        _checkUpdateButton.Visible = settings;
        _themeButton.Visible = settings;
        _autoStartCheckBox.Visible = settings;
        _settingsStatus.Visible = settings;
        _versionLabel.Visible = settings;
        _uuidLabel.Visible = settings;
        _leaderboardIdTextBox.Visible = leaderboard;
        _editIdButton.Visible = leaderboard;
        _leaderboardStatus.Visible = leaderboard;
        foreach (Label kind in _leaderboardKindButtons) kind.Visible = leaderboard;
        foreach (Label entry in _leaderboardEntries) entry.Visible = leaderboard;

        _view1.Visible = page is UiPage.Data or UiPage.Stats;
        _view2.Visible = page is UiPage.Data or UiPage.Stats;
        _view3.Visible = page == UiPage.Data;
        if (page == UiPage.Data)
        {
            _view1.Location = new Point(174, 70);
            _view2.Location = new Point(174, 96);
            _view3.Location = new Point(174, 122);
        }
        else if (stats)
        {
            _view1.Location = new Point(370, 132);
            _view2.Location = new Point(370, 160);
        }

        ClientSize = stats || leaderboard ? new Size(400, 360) : new Size(200, 200);
        PositionLeftMiddle();

        int buttonY = stats || leaderboard ? 332 : settings ? 180 : 174;
        int baseX = stats || leaderboard ? (ClientSize.Width - 116) / 2 : 42;
        _dataButton.Location = new Point(baseX, buttonY);
        _perfButton.Location = new Point(baseX + 24, buttonY);
        _statsButton.Location = new Point(baseX + 48, buttonY);
        _settingsButton.Location = new Point(baseX + 72, buttonY);
        _leaderboardButton.Location = new Point(baseX + 96, buttonY);

        _dataButton.BackColor = page == UiPage.Data ? Active : Inactive;
        _perfButton.BackColor = page == UiPage.Performance ? Active : Inactive;
        _statsButton.BackColor = stats ? Active : Inactive;
        _settingsButton.BackColor = settings ? Active : Inactive;
        _leaderboardButton.BackColor = leaderboard ? Active : Inactive;
        UpdateViewButtons();

        if (stats) RefreshStats();
        if (page == UiPage.Performance) UpdatePerformance(_performance.Sample());
        if (dataInput) UpdateDataInput();
        if (dataMax) UpdateMaxValues();
        if (settings) UpdateAutoStartState();
        if (leaderboard)
        {
            _title.Size = new Size(400, 22);
            _leaderboardIdTextBox.Location = new Point(20, 30);
            _leaderboardIdTextBox.Size = new Size(160, 24);
            _editIdButton.Location = new Point(ClientSize.Width - _editIdButton.Width - 20, 28);
            _editIdButton.Size = new Size(90, 28);
            for (int i = 0; i < 5; i++)
            {
                _leaderboardKindButtons[i].Location = new Point(20 + i * 72, 70);
                _leaderboardKindButtons[i].Size = new Size(66, 24);
            }
            _leaderboardStatus.Location = new Point(20, 300);
            _leaderboardStatus.Size = new Size(360, 20);
            for (int i = 0; i < 5; i++)
            {
                _leaderboardEntries[i].Location = new Point(20, 108 + i * 38);
                _leaderboardEntries[i].Size = new Size(360, 34);
                _leaderboardEntries[i].Font = new Font("Microsoft YaHei UI", 10f);
                FitLabelFont(_leaderboardEntries[i], 10f);
            }
            UpdateLeaderboardKindButtons();
            _ = UploadAndRefreshLeaderboardAsync();
        }
        else
        {
            _title.Size = new Size(200, 22);
        }
    }

    private void SelectView(int view)
    {
        _view = view;
        AppLog.Info($"切换数据视图：{view}");
        ShowPage(_page);
    }

    private void SelectPeriod(int period)
    {
        _period = period;
        AppLog.Info($"切换统计周期：{period} 天");
        RefreshStats();
    }

    private void SelectChartKind(int index)
    {
        _chartKind = _view == 1 ? TimeKinds[index] : InputKinds[index];
        AppLog.Info($"切换图表类型：{_chartKind}");
        RefreshStats();
    }

    private void RefreshStats()
    {
        if (_page != UiPage.Stats) return;
        if (_view == 1 && _chartKind is not (ChartKind.Combined or ChartKind.Powered or ChartKind.Awake or ChartKind.Active)) _chartKind = ChartKind.Combined;
        if (_view == 2 && _chartKind is not (ChartKind.MouseTotal or ChartKind.MouseLeft or ChartKind.MouseRight or ChartKind.Keyboard)) _chartKind = ChartKind.MouseTotal;

        _chart.Location = new Point(10, _view == 2 ? 100 : 58);
        _chart.Size = new Size(352, _view == 2 ? 224 : 266);
        _chart.SetData(_period, _engine.GetDailyStats(_period), _chartKind);
        UpdateKindButtons();
        UpdateViewButtons();
        if (_view == 2) UpdateInputSummary();
        _lastStatsRefresh = DateTimeOffset.UtcNow;
    }

    private void UpdateKindButtons()
    {
        string[] labels = _view == 1 ? ["总", "运行", "非睡", "高强"] : ["总", "左键", "右键", "键盘"];
        ChartKind[] kinds = _view == 1 ? TimeKinds : InputKinds;
        for (int i = 0; i < 4; i++)
        {
            SetLabelText(_kindButtons[i], labels[i], 8f);
            _kindButtons[i].BackColor = _chartKind == kinds[i] ? Active : Inactive;
        }
    }

    private void UpdateViewButtons()
    {
        _view1.BackColor = _view == 1 ? Active : Inactive;
        _view2.BackColor = _view == 2 ? Active : Inactive;
        _view3.BackColor = _view == 3 ? Active : Inactive;
    }

    private void UpdateDataInput()
    {
        InputCounts c = _engine.GetInputDay(DateTime.Today);
        SetLabelText(_inputValues[0], $"总点击：{c.Total}", 9f);
        SetLabelText(_inputValues[1], $"左键：{c.Left}", 9f);
        SetLabelText(_inputValues[2], $"右键：{c.Right}", 9f);
        SetLabelText(_inputValues[3], $"键盘：{c.Keyboard}", 9f);
    }

    private void UpdateMaxValues()
    {
        InputMaxRates max = _engine.GetCurrentInputMax();
        SetLabelText(_maxValues[0], $"{max.Cps:F1} 次/秒", 11.5f);
        SetLabelText(_maxValues[1], $"{max.Kps:F1} 次/秒", 11.5f);
        SetLabelText(_maxValues[2], $"{max.Aps:F1} 次/秒", 11.5f);
    }

    private async Task UploadAndRefreshLeaderboardAsync()
    {
        if (_leaderboardBusy)
        {
            AppLog.Info("排行榜同步已在进行，跳过本次请求");
            return;
        }

        _leaderboardBusy = true;
        AppLog.Info("开始上传并刷新排行榜");
        try
        {
            string uuid = await _deviceIdentity.GetUuidAsync();
            string displayName = LeaderboardSettingsStore.Sanitize(_leaderboardIdTextBox.Text);
            if (string.IsNullOrEmpty(displayName))
            {
                displayName = LeaderboardSettingsStore.DefaultUserId();
            }

            IReadOnlyDictionary<string, double> values = _engine.GetDailyLeaderboardValues(DateTime.Today);
            bool ok = await _leaderboardClient.SubmitAllAsync(
                uuid,
                displayName,
                DateTime.Today,
                values);
            AppLog.Info($"排行榜用户数据上传结果：{ok}");
            Dictionary<string, IReadOnlyList<LeaderboardEntry>> boards =
                await _leaderboardClient.GetBoardsAsync(DateTime.Today);
            AppLog.Info($"排行榜读取完成：{boards.Count} 类榜单");
            foreach (KeyValuePair<string, IReadOnlyList<LeaderboardEntry>> board in boards)
            {
                _leaderboardBoards[board.Key] = board.Value;
            }

            UpdateLeaderboardEntriesFromCache();
            UpdateLeaderboardKindButtons();
            SetLabelText(_leaderboardStatus, ok ? "全部排行榜已同步" : "网络异常，显示本地数据", 8f);
        }
        catch
        {
            AppLog.Info("排行榜同步失败");
            SetLabelText(_leaderboardStatus, "排行榜同步失败", 8f);
        }
        finally
        {
            _leaderboardBusy = false;
            _lastLeaderboardRefresh = DateTimeOffset.UtcNow;
            _lastLeaderboardUploadUtc = DateTimeOffset.UtcNow;
        }
    }

    private void ShowEditIdDialog()
    {
        AppLog.Info("用户打开修改 ID 弹窗");
        using Form dialog = new()
        {
            Text = "修改用户ID",
            ClientSize = new Size(320, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            Font = new Font("Microsoft YaHei UI", 9f),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
        };
        if (_darkMode)
        {
            dialog.BackColor = Color.FromArgb(24, 27, 33);
            dialog.ForeColor = Color.FromArgb(226, 232, 240);
        }

        Label label = new()
        {
            Text = "请输入用户ID（仅英文和数字，最多10位）",
            Location = new Point(20, 16),
            Size = new Size(280, 24),
        };
        TextBox box = new()
        {
            Text = _leaderboardIdTextBox.Text,
            Location = new Point(20, 46),
            Size = new Size(280, 26),
            MaxLength = 10,
        };
        Button ok = new()
        {
            Text = "确定",
            Location = new Point(140, 100),
            Size = new Size(70, 28),
            DialogResult = DialogResult.OK,
        };
        Button cancel = new()
        {
            Text = "取消",
            Location = new Point(220, 100),
            Size = new Size(70, 28),
            DialogResult = DialogResult.Cancel,
        };

        ok.Click += (_, _) =>
        {
            string id = LeaderboardSettingsStore.Sanitize(box.Text);
            if (id.Length == 0)
            {
                MessageBox.Show(this, "用户ID不能为空，且只能包含英文和数字。", "修改用户ID");
                dialog.DialogResult = DialogResult.None;
                return;
            }

            _leaderboardIdTextBox.Text = id;
            LeaderboardSettingsStore.SaveUserId(id);
            dialog.DialogResult = DialogResult.OK;
        };

        dialog.Controls.Add(label);
        dialog.Controls.Add(box);
        dialog.Controls.Add(ok);
        dialog.Controls.Add(cancel);
        if (_darkMode)
        {
            label.ForeColor = Color.FromArgb(226, 232, 240);
            box.BackColor = Color.FromArgb(15, 18, 22);
            box.ForeColor = Color.FromArgb(226, 232, 240);
            ok.BackColor = Inactive;
            ok.ForeColor = Color.White;
            cancel.BackColor = Inactive;
            cancel.ForeColor = Color.White;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ = UploadAndRefreshLeaderboardAsync();
        }
    }

    private void UpdateLeaderboardEntries(IReadOnlyList<LeaderboardEntry> entries)
    {
        for (int i = 0; i < 5; i++)
        {
            SetLabelText(_leaderboardEntries[i], i < entries.Count
                ? $"{i + 1}. {entries[i].Name}  {FormatLeaderboardValue(entries[i].Value)}"
                : $"{i + 1}. 暂无", 10f);
            string? uuid = i < entries.Count && !string.IsNullOrEmpty(entries[i].Uuid)
                ? $"UUID：{entries[i].Uuid}"
                : null;
            _toolTip.SetToolTip(_leaderboardEntries[i], uuid);
        }
    }

    private void UpdateLeaderboardEntriesFromCache()
    {
        if (_leaderboardBoards.TryGetValue(_leaderboardMetric, out IReadOnlyList<LeaderboardEntry>? entries))
        {
            UpdateLeaderboardEntries(entries);
        }
    }

    private async Task LoadUuidAsync()
    {
        try
        {
            string uuid = await _deviceIdentity.GetUuidAsync();
            AppLog.Info($"UUID 加载成功：{uuid}");
            SetLabelText(_uuidLabel, $"UUid：{uuid}", 8f);
            _toolTip.SetToolTip(_leaderboardIdTextBox, $"UUID：{uuid}");
        }
        catch
        {
            AppLog.Info("UUID 加载失败");
            SetLabelText(_uuidLabel, "UUid：--", 8f);
            _toolTip.SetToolTip(_leaderboardIdTextBox, "UUID：--");
        }
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        AppLog.Info($"开始检测更新：interactive={interactive}");
        if (UpdateService.IsInstalling)
        {
            AppLog.Info("下载更新期间重复点击检测，显示随机提示");
            _randomTextUntil = DateTime.UtcNow.AddSeconds(2);
            string text = RandomUpdateTexts[Random.Shared.Next(RandomUpdateTexts.Length)];
            SetLabelText(_settingsStatus, text, 8f);
            _ = RestoreStatusAfterRandomDelayAsync("正在下载并校验更新...");
            return;
        }

        if (interactive && _noUpdateClickCount >= 5)
        {
            AppLog.Info("最新版本连续点击检测 5 次以上，显示随机提示");
            _randomTextUntil = DateTime.UtcNow.AddSeconds(2);
            string text = RandomUpdateTexts[Random.Shared.Next(RandomUpdateTexts.Length)];
            SetLabelText(_settingsStatus, text, 8f);
            _ = RestoreStatusAfterRandomDelayAsync("当前已是最新版本");
            return;
        }

        if (interactive && !IsDisposed)
        {
            SetLabelText(_settingsStatus, "正在检测最新版本...", 8f);
        }

        UpdateCheckResult check = await UpdateService.CheckForUpdateAsync();
        AppLog.Info($"检测更新结果：{check.Status} {check.Message}");

        if (check.Status == UpdateCheckStatus.Failed)
        {
            if (interactive && !IsDisposed)
            {
                SetLabelText(_settingsStatus, check.Message, 8f);
                MessageBox.Show(
                    this,
                    check.Message,
                    "云曦PC统计更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            return;
        }

        if (check.Status == UpdateCheckStatus.NoUpdate)
        {
            if (interactive)
            {
                _noUpdateClickCount++;
            }
            if (interactive && !IsDisposed)
            {
                SetLabelText(_settingsStatus, check.Message, 8f);
            }
            return;
        }

        _noUpdateClickCount = 0;

        if (check.Info is null)
        {
            return;
        }

        if (!interactive)
        {
            _trayIcon.ShowBalloonTip(
                5000,
                "云曦PC统计",
                $"发现新版本 {check.Info.Version}，请在设置中检测更新。",
                ToolTipIcon.Info);
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            $"发现新版本 {check.Info.Version}，是否下载并安装？",
            "云曦PC统计更新",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (answer != DialogResult.Yes || IsDisposed)
        {
            SetLabelText(_settingsStatus, "已取消更新", 8f);
            return;
        }

        SetLabelText(_settingsStatus, "正在下载并校验更新...", 8f);
        UpdateInstallResult install = await UpdateService.InstallUpdateAsync(
            check.Info,
            Environment.ProcessId,
            percent =>
            {
                if (!IsDisposed && DateTime.UtcNow >= _randomTextUntil)
                {
                    SetLabelText(_settingsStatus, $"正在下载更新... {percent}%", 8f);
                }
            });
        AppLog.Info($"安装结果：{install.Message}");

        if (!install.Started && !IsDisposed)
        {
            SetLabelText(_settingsStatus, install.Message, 8f);
            MessageBox.Show(
                this,
                install.Message,
                "云曦PC统计更新",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (install.Started && !IsDisposed)
        {
            SetLabelText(_settingsStatus, "更新程序已启动，正在退出当前版本...", 8f);
            BeginInvoke((Action)(() => Close()));
        }
    }

    private async Task RestoreStatusAfterRandomDelayAsync(string restoreText)
    {
        await Task.Delay(2000);
        if (!IsDisposed && DateTime.UtcNow >= _randomTextUntil)
        {
            SetLabelText(_settingsStatus, restoreText, 8f);
        }
    }

    private void ApplyTheme()
    {
        DarkTheme = _darkMode;
        Color background = _darkMode ? Color.FromArgb(24, 27, 33) : Color.FromArgb(245, 247, 250);
        Color foreground = _darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(32, 36, 42);
        Color titleColor = _darkMode ? Color.FromArgb(96, 165, 250) : Color.FromArgb(25, 92, 167);
        Color statusColor = _darkMode ? Color.FromArgb(148, 163, 184) : Color.FromArgb(92, 102, 115);

        BackColor = background;
        ForeColor = foreground;
        _chart.DarkMode = _darkMode;
        _chart.BackColor = _darkMode ? Color.FromArgb(30, 34, 42) : Color.White;

        ApplyControlTheme(this, background, foreground, titleColor, statusColor);

        UpdateViewButtons();
        UpdateKindButtons();
        UpdateLeaderboardKindButtons();

        _dataButton.BackColor = _page == UiPage.Data ? Active : Inactive;
        _perfButton.BackColor = _page == UiPage.Performance ? Active : Inactive;
        _statsButton.BackColor = _page == UiPage.Stats ? Active : Inactive;
        _settingsButton.BackColor = _page == UiPage.Settings ? Active : Inactive;
        _leaderboardButton.BackColor = _page == UiPage.Leaderboard ? Active : Inactive;

        _title.ForeColor = titleColor;
        _settingsStatus.ForeColor = statusColor;
        _leaderboardStatus.ForeColor = statusColor;
        _uuidLabel.ForeColor = titleColor;
        _versionLabel.ForeColor = titleColor;
    }

    private void ApplyControlTheme(
        Control parent,
        Color background,
        Color foreground,
        Color titleColor,
        Color statusColor)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is Label label)
            {
                if (label.Tag as string == "themeButton")
                {
                    label.BackColor = Inactive;
                    label.ForeColor = Color.White;
                }
                else
                {
                    label.BackColor = background;
                    label.ForeColor = foreground;
                }
            }
            else if (control is CheckBox checkBox)
            {
                checkBox.BackColor = background;
                checkBox.ForeColor = foreground;
            }
            else if (control is TextBox textBox)
            {
                textBox.BackColor = _darkMode ? Color.FromArgb(15, 18, 22) : Color.White;
                textBox.ForeColor = foreground;
                textBox.BorderStyle = BorderStyle.FixedSingle;
            }

            ApplyControlTheme(control, background, foreground, titleColor, statusColor);
        }
    }

    private void UpdateLeaderboardKindButtons()
    {
        string[] metrics = ["active", "mouse_total", "mouse_left", "mouse_right", "keyboard"];
        for (int i = 0; i < 5; i++)
        {
            _leaderboardKindButtons[i].BackColor = _leaderboardMetric == metrics[i] ? Active : Inactive;
        }
    }

    private string FormatLeaderboardValue(double value)
    {
        if (_leaderboardMetric == "active")
        {
            return Format(TimeSpan.FromSeconds(value));
        }

        return value.ToString("N0");
    }

    private void UpdateInputSummary()
    {
        InputCounts c = _engine.GetInputDay(DateTime.Today);
        SetLabelText(_inputSummary, $"总点击：{c.Total}  左键：{c.Left}\r\n右键：{c.Right}  键盘：{c.Keyboard}", 8.5f);
    }

    private void UpdateAutoStartState()
    {
        string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        _autoStartCheckBox.Checked =
            File.Exists(Path.Combine(startup, "云曦PC统计.lnk")) ||
            File.Exists(Path.Combine(startup, "PCCompanionMonitor.lnk"));
    }

    private void ApplyAutoStart(bool enabled)
    {
        try
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string path = Path.Combine(startup, "云曦PC统计.lnk");
            string oldPath = Path.Combine(startup, "PCCompanionMonitor.lnk");

            if (enabled)
            {
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }

                Type shellType = Type.GetTypeFromProgID("WScript.Shell")
                    ?? throw new InvalidOperationException("无法访问 Windows 脚本组件。");
                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    dynamic shortcut = shell.CreateShortcut(path);
                    shortcut.TargetPath = Application.ExecutablePath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                    shortcut.Save();
                    Marshal.FinalReleaseComObject(shortcut);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shell);
                }

                SetLabelText(_settingsStatus, "已开启开机启动", 8f);
                AppLog.Info("已开启开机启动");
            }
            else
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
                SetLabelText(_settingsStatus, "已关闭开机启动", 8f);
                AppLog.Info("已关闭开机启动");
            }
        }
        catch (Exception ex)
        {
            SetLabelText(_settingsStatus, "开机启动设置失败", 8f);
            AppLog.Info($"开机启动设置失败：{ex.Message}");
            _toolTip.SetToolTip(_settingsStatus, ex.Message);
            _autoStartCheckBox.Checked = !enabled;
        }
    }

    private void UpdatePerformance(PerformanceSnapshot p)
    {
        SetLabelText(_perfValues[0], $"相对：{p.CpuPercent:F1}%\r\n绝对：{FormatFrequency(p.CpuHz)}", 8.5f);
        SetLabelText(_perfValues[1], p.GpuAvailable ? $"相对：{p.GpuPercent:F1}%\r\n绝对：{FormatMemoryMb(p.GpuMemoryMb)}" : "相对：不可用\r\n绝对：不可用", 8.5f);
        SetLabelText(_perfValues[2], $"相对：{p.MemoryPercent:F1}%\r\n绝对：{FormatMemoryMb(p.MemoryMb)}", 8.5f);
    }

    private void AttachDrag(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
            }
        };
        control.MouseMove += (_, _) =>
        {
            if (_dragging) Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
        };
        control.MouseUp += (_, _) => _dragging = false;
        control.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) _contextMenu.Show(Cursor.Position);
        };
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        PositionLeftMiddle();
    }

    private void ShowChangelog()
    {
        AppLog.Info("用户打开更新日志窗口");
        if (_changelogForm is { IsDisposed: false })
        {
            _changelogForm.Activate();
            return;
        }

        _changelogForm = new Form
        {
            Text = "更新日志",
            ClientSize = new Size(400, 400),
            StartPosition = FormStartPosition.CenterScreen,
            Font = new Font("Microsoft YaHei UI", 9f),
            ShowInTaskbar = false,
        };
        if (_darkMode)
        {
            _changelogForm.BackColor = Color.FromArgb(24, 27, 33);
            _changelogForm.ForeColor = Color.FromArgb(226, 232, 240);
        }

        TextBox textBox = new()
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = Changelog.Text,
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = Color.White,
        };
        if (_darkMode)
        {
            textBox.BackColor = Color.FromArgb(15, 18, 22);
            textBox.ForeColor = Color.FromArgb(226, 232, 240);
        }
        _changelogForm.Controls.Add(textBox);
        _changelogForm.FormClosed += (_, _) => _changelogForm = null;
        _changelogForm.Shown += (_, _) =>
        {
            textBox.SelectionStart = 0;
            textBox.SelectionLength = 0;
        };
        _changelogForm.Show();
    }

    private void PositionLeftMiddle()
    {
        Screen? screen = Screen.PrimaryScreen;
        if (screen is null) return;
        Rectangle area = screen.WorkingArea;
        Left = area.Left;
        Top = area.Top + (area.Height - Height) / 2;
    }

    private static Label CreateSwitch(string text)
    {
        Label label = new()
        {
            Text = "",
            Size = new Size(20, 20),
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
            BackColor = Inactive,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Padding = new Padding(0),
            BorderStyle = BorderStyle.None,
            Tag = "themeButton",
        };
        label.Paint += (_, e) => DrawText(e.Graphics, text, label);
        return label;
    }

    private static Label CreateTextButton(string text, Point location, Size size)
    {
        Label label = new()
        {
            Text = text,
            Location = location,
            Size = size,
            Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold),
            BackColor = Inactive,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Padding = new Padding(0),
            BorderStyle = BorderStyle.None,
            Tag = "themeButton",
        };
        FitLabelFont(label, 8f);
        return label;
    }

    private static void DrawText(Graphics g, string text, Label label)
    {
        g.Clear(label.BackColor);
        TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        for (float size = label.Font.Size; size >= 5.5f; size -= 0.5f)
        {
            using Font font = new(label.Font.FontFamily, size, label.Font.Style);
            Size measured = TextRenderer.MeasureText(g, text, font, label.ClientSize, flags);
            if (measured.Width <= label.ClientSize.Width && measured.Height <= label.ClientSize.Height)
            {
                TextRenderer.DrawText(g, text, font, label.ClientRectangle, label.ForeColor, flags);
                return;
            }
        }

        using Font fallback = new(label.Font.FontFamily, 5.5f, label.Font.Style);
        TextRenderer.DrawText(g, text, fallback, label.ClientRectangle, label.ForeColor, flags);
    }

    private static void SetLabelText(Label label, string text, float baseSize)
    {
        label.Text = text;
        FitLabelFont(label, baseSize);
    }

    private static void FitLabelFont(Label label, float startSize, float minSize = 5.5f)
    {
        if (string.IsNullOrEmpty(label.Text))
        {
            return;
        }

        Font oldFont = label.Font;
        float bestSize = minSize;
        for (float size = startSize; size >= minSize - 0.01f; size -= 0.5f)
        {
            using Font probe = new(oldFont.FontFamily, size, oldFont.Style);
            if (Fits(label, probe))
            {
                bestSize = size;
                break;
            }
        }

        Font replacement = new(oldFont.FontFamily, bestSize, oldFont.Style);
        label.Font.Dispose();
        label.Font = replacement;
    }

    private static bool Fits(Label label, Font font)
    {
        string normalized = label.Text.Replace("\r\n", "\n");
        string[] lines = normalized.Split('\n');
        int maxWidth = 0;
        int height = 0;
        foreach (string line in lines)
        {
            Size measured = TextRenderer.MeasureText(line, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            maxWidth = Math.Max(maxWidth, measured.Width);
            height += measured.Height;
        }

        return maxWidth <= label.ClientSize.Width && height <= label.ClientSize.Height;
    }

    private static Icon CreateTrayIcon()
    {
        try
        {
            using Bitmap bitmap = new(32, 32);
            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(Color.FromArgb(25, 92, 167));
            using Font font = new("Microsoft YaHei UI", 15f, FontStyle.Bold);
            g.DrawString("云", font, Brushes.White, new RectangleF(0, 0, 32, 32), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            IntPtr h = bitmap.GetHicon();
            try
            {
                using Icon temp = Icon.FromHandle(h);
                return (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(h);
            }
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static bool DarkTheme;
    private static Color Active => DarkTheme ? Color.FromArgb(59, 130, 246) : Color.FromArgb(25, 92, 167);
    private static Color Inactive => DarkTheme ? Color.FromArgb(56, 63, 74) : Color.FromArgb(190, 198, 208);

    private static string Format(TimeSpan value)
    {
        int total = Math.Max(0, (int)value.TotalSeconds);
        return $"{total / 3600:D2}:{(total % 3600) / 60:D2}:{total % 60:D2}";
    }

    private static string FormatFrequency(double hz)
    {
        if (hz >= 1_000_000_000)
        {
            return $"{hz / 1_000_000_000:0.##} GHz";
        }
        if (hz >= 1_000_000)
        {
            return $"{hz / 1_000_000:0.##} MHz";
        }
        if (hz >= 1_000)
        {
            return $"{hz / 1_000:0.##} KHz";
        }
        return $"{hz:0} Hz";
    }

    private static string FormatMemoryMb(double mb)
    {
        if (mb >= 1024)
        {
            return $"{mb / 1024:0.##} GB";
        }
        return $"{mb:0.#} MB";
    }

    private static string FormatCurrentVersionLabel()
    {
        string productVersion = Application.ProductVersion;
        if (Version.TryParse(productVersion, out Version? version))
        {
            return $"当前版本：{version.Major}.{version.Minor}.{version.Build}";
        }
        return $"当前版本：{productVersion}";
    }
}
