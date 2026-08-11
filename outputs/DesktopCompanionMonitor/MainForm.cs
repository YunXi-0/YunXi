using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

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
    private readonly Label _lockButton;
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
    private readonly Label _featuresButton;
    private readonly Label _aboutButton;
    private readonly CheckBox _autoStartCheckBox;
    private readonly Label _settingsStatus;
    private readonly Label _uuidLabel;
    private readonly Label _versionLabel;
    private readonly TextBox _leaderboardIdTextBox;
    private readonly Label _editIdButton;
    private readonly Label _refreshLeaderboardButton;
    private readonly Label _leaderboardStatus;
    private readonly Label[] _leaderboardKindButtons = new Label[7];
    private readonly Label[] _leaderboardPeriodButtons = new Label[4];
    private readonly Label[] _leaderboardEntries = new Label[5];
    private readonly Label _leaderboardAllButton;
    private readonly Label _drawLuckButton;
    private readonly Label _collectionEmptyLabel;
    private readonly CollectionBallControl _collectionBall;
    private readonly System.Windows.Forms.Timer _collectionTimer;
    private readonly System.Windows.Forms.Timer _resizeLayoutTimer;
    private readonly System.Windows.Forms.Timer _placementSaveTimer;
    private readonly LeaderboardClient _leaderboardClient;
    private readonly DeviceIdentityService _deviceIdentity;
    private string _leaderboardMetric = "active";
    private int _leaderboardPeriod = 1;
    private readonly Dictionary<string, IReadOnlyList<LeaderboardEntry>> _leaderboardBoards = new();
    private bool _leaderboardBusy;
    private DateTimeOffset _lastLeaderboardRefresh;
    private DateTimeOffset _lastLeaderboardUploadUtc;
    private DateTimeOffset _lastManualLeaderboardRefresh;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ContextMenuStrip _trayMenu;
    private readonly NotifyIcon _trayIcon;
    private Form? _changelogForm;
    private Form? _leaderboardAllForm;
    private Form? _featuresForm;
    private Form? _aboutForm;
    private Form? _luckPopupForm;

    private UiPage _page;
    private int _view = 1;
    private int _period = 7;
    private ChartKind _chartKind = ChartKind.Combined;
    private DateTimeOffset _lastStatsRefresh;
    private Point _dragOffset;
    private bool _dragging;
    private AppPositionStore? _appPosition;
    private System.Windows.Forms.Timer? _snapDelayTimer;
    private System.Windows.Forms.Timer? _snapAnimTimer;
    private Rectangle _snapOriginal;
    private bool _isSnapped;
    private int _snapEdge;
    private int _snapAnimStep;
        private bool _layoutReady;
    private bool _applyingPageSize;
    private Size _compactClientSize = new(200, 200);
    private Size _expandedClientSize = new(400, 360);
    private readonly Dictionary<Control, ControlLayout> _designLayout = new();
    private Dictionary<Control, ControlLayout> _pageLayout = new();
    private readonly Dictionary<Control, Font> _ownedLayoutFonts = new();
    private float _currentUiScale = 1f;
    private bool _scaleLayoutPending;
    private Size _sizingStartSize;
    private bool _sizingAxisLocked;
    private bool _sizingWidthDriven;
    private bool _darkMode;
    private bool _locked;
    private DateTime _randomTextUntil;
    private int _noUpdateClickCount;
    private DateTime _collectionCycleStart;
    private DateTime _collectionCooldownUntil;
    private int _lastCollectionRollMinute = -1;
    private UiPage _collectionPage;
    private bool _collectionBallVisible;
    private Color _collectionBallColor = Color.Red;
    private Point _collectionBallBaseLocation;

    private static readonly string[] RandomUpdateTexts =
    [
        "fufu~",
        "你戳咩啊",
        "吃你家大米啦？",
    ];

    private static readonly ChartKind[] TimeKinds = [ChartKind.Combined, ChartKind.Powered, ChartKind.Awake, ChartKind.Active];
    private static readonly ChartKind[] InputKinds = [ChartKind.MouseTotal, ChartKind.MouseLeft, ChartKind.MouseRight, ChartKind.Keyboard];

    private const int ResizeGrip = 8;
    private const int WmSetRedraw = 0x000B;
    private const int WmNcHitTest = 0x0084;
    private const int WmSizing = 0x0214;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private readonly record struct ControlLayout(
        Rectangle Bounds,
        string FontFamily,
        float FontSize,
        FontStyle FontStyle,
        GraphicsUnit FontUnit,
        Padding Padding);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public MainForm(
        ActivityStore store,
        UiPage initialPage = UiPage.Data,
        int initialView = 1,
        int initialPeriod = 7,
        ChartKind initialKind = ChartKind.Combined,
        string initialLeaderboardMetric = "active")
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
        _lockButton = CreateSwitch("锁");
        _lockButton.Location = new Point(174, 148);
        _lockButton.Size = new Size(20, 20);
        _lockButton.Click += (_, _) => ToggleLock();
        _toolTip.SetToolTip(_lockButton, "锁定按钮，再次点击以解锁");
        _view1.Click += (_, _) => SelectView(1);
        _view2.Click += (_, _) => SelectView(2);
        _view3.Click += (_, _) => SelectView(3);
        Controls.Add(_view1);
        Controls.Add(_view2);
        Controls.Add(_view3);
        Controls.Add(_lockButton);

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
        _featuresButton = CreateTextButton("功能", new Point(30, 102), new Size(40, 24));
        _featuresButton.Click += (_, _) => ShowFeatures();
        _checkUpdateButton = CreateTextButton("检测最新", new Point(72, 102), new Size(60, 24));
        _checkUpdateButton.Click += async (_, _) => await CheckForUpdatesAsync(true);
        _aboutButton = CreateTextButton("关于", new Point(128, 102), new Size(42, 24));
        _aboutButton.Click += (_, _) => ShowAbout();
        _toolTip.SetToolTip(_featuresButton, "功能设置");
        _toolTip.SetToolTip(_checkUpdateButton, "检测是否为最新版本");
        _toolTip.SetToolTip(_aboutButton, "关于本软件");
        
        
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
        Controls.Add(_featuresButton);
        Controls.Add(_checkUpdateButton);
        Controls.Add(_aboutButton);
        Controls.Add(_settingsStatus);
        Controls.Add(_versionLabel);
        Controls.Add(_uuidLabel);

        _leaderboardClient = new LeaderboardClient();
        _deviceIdentity = new DeviceIdentityService(_leaderboardClient);
        _appPosition = new AppPositionStore(_engine.DataDirectory);
        if (_appPosition is { TopMost: true }) TopMost = true;
        RestoreSavedScale();
        _placementSaveTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _placementSaveTimer.Tick += (_, _) =>
        {
            _placementSaveTimer.Stop();
            SaveWindowPlacement();
        };
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
        _refreshLeaderboardButton = CreateTextButton("刷新", new Point(200, 28), new Size(80, 28));
        _refreshLeaderboardButton.Click += async (_, _) => await RefreshLeaderboardAsync();
        Controls.Add(_refreshLeaderboardButton);

        string[] metrics = ["active", "mouse_total", "mouse_left", "mouse_right", "keyboard", "luck", "collections"];
        string[] labels = ["高强度", "总点击", "左键", "右键", "键盘", "运气", "藏品"];
        for (int i = 0; i < 7; i++)
        {
            int index = i;
            _leaderboardKindButtons[i] = CreateTextButton(
                labels[i],
                new Point(14 + i * 37, 58),
                new Size(34, 16));
            _leaderboardKindButtons[i].Click += (_, _) =>
            {
                _leaderboardMetric = metrics[index];
                ApplyLeaderboardPeriodToMetric();
                UpdateLeaderboardKindButtons();
                UpdateLeaderboardPeriodButtons();
                UpdateLeaderboardEntriesFromCache();
            };
            if (i >= 5)
            {
                _toolTip.SetToolTip(
                    _leaderboardKindButtons[i],
                    i == 6 ? "该榜单的刷新频率为：永久" : "该榜单的刷新频率为：每日");
            }
            Controls.Add(_leaderboardKindButtons[i]);
        }

        (string label, int period, string tooltip)[] periodDefs = [
            ("1", 1, "该榜单的刷新频率为：每日"),
            ("7", 7, "该榜单的刷新频率为：每周"),
            ("30", 30, "该榜单的刷新频率为：30天"),
            ("总", 0, "该榜单的刷新频率为：永久"),
        ];
        for (int i = 0; i < 4; i++)
        {
            var (label, period, tooltip) = periodDefs[i];
            _leaderboardPeriodButtons[i] = CreateTextButton(
                label,
                new Point(374, 140 + i * 30),
                new Size(22, 24));
            _leaderboardPeriodButtons[i].Click += (_, _) =>
            {
                _leaderboardPeriod = period;
                ApplyLeaderboardPeriodToMetric();
                UpdateLeaderboardKindButtons();
                UpdateLeaderboardPeriodButtons();
                UpdateLeaderboardEntriesFromCache();
            };
            _toolTip.SetToolTip(_leaderboardPeriodButtons[i], tooltip);
            Controls.Add(_leaderboardPeriodButtons[i]);
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

        _leaderboardAllButton = CreateTextButton("全部", new Point(330, 298), new Size(56, 24));
        _leaderboardAllButton.Click += (_, _) => ShowAllLeaderboard();
        _leaderboardAllButton.Visible = false;
        _toolTip.SetToolTip(_leaderboardAllButton, "显示该榜单全部排名");
        Controls.Add(_leaderboardAllButton);

        _drawLuckButton = CreateTextButton("抽取今日运气值", new Point(130, 180), new Size(140, 32));
        _drawLuckButton.Click += async (_, _) => await DrawTodayLuckAsync();
        _drawLuckButton.Visible = false;
        Controls.Add(_drawLuckButton);

        _collectionEmptyLabel = new Label
        {
            Text = "你必须先找到至少一个藏品",
            Location = new Point(30, 180),
            Size = new Size(340, 32),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(92, 102, 115),
            TextAlign = ContentAlignment.MiddleCenter,
            Visible = false,
        };
        Controls.Add(_collectionEmptyLabel);

        _collectionBall = new CollectionBallControl
        {
            Visible = false,
        };
        _collectionBall.Click += async (_, _) => await AcquireCollectionAsync();
        Controls.Add(_collectionBall);
        _collectionBall.BringToFront();

        _collectionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _collectionTimer.Tick += (_, _) => CollectionTimerTick();

        _resizeLayoutTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _resizeLayoutTimer.Tick += (_, _) => ProcessPendingScaleLayout();

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
            RestoreWindowPosition();
            AppLog.Info("窗口位置已设置，开始启动监测引擎");
            _engine.Start();
            _ = LoadUuidAsync();
            _ = CheckForUpdatesAsync(false);
            _ = Task.Run(() => _performance.WarmUp());
            _collectionCycleStart = DateTime.UtcNow;
            _lastCollectionRollMinute = -1;
            _collectionTimer.Start();
            AppLog.Info("监测引擎、UUID、更新检测和性能预热任务已启动");
        };
        FormClosing += (_, _) =>
        {
            AppLog.Info("组件开始关闭");
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _engine.Dispose();
            _performance.Dispose();
            _collectionTimer.Stop();
            _collectionTimer.Dispose();
            _resizeLayoutTimer.Stop();
            _resizeLayoutTimer.Dispose();
            _placementSaveTimer.Stop();
            _placementSaveTimer.Dispose();
            AppLog.Info("组件资源已释放");
        };

        ApplyTheme();
        _view = initialView;
        _period = initialPeriod;
        _chartKind = initialKind;
        _leaderboardMetric = initialLeaderboardMetric;
        AppLog.Info($"主界面初始化完成，初始页面={initialPage}，视图={initialView}");
        CaptureLayout(_designLayout);
        _layoutReady = true;
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
        if (!_leaderboardBusy &&
            _lastLeaderboardUploadUtc != default &&
            DateTimeOffset.UtcNow - _lastLeaderboardUploadUtc > TimeSpan.FromSeconds(60))
        {
            _ = UploadAndRefreshLeaderboardAsync();
        }
        if (_page == UiPage.Stats && DateTimeOffset.UtcNow - _lastStatsRefresh > TimeSpan.FromMinutes(1)) RefreshStats();
    }

    private void ShowPage(UiPage page)
    {
        bool suppressRedraw = IsHandleCreated && Visible;
        if (suppressRedraw)
        {
            SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        }

        SuspendLayout();
        try
        {
            ShowPageCore(page);
        }
        finally
        {
            ResumeLayout(false);
            if (suppressRedraw && IsHandleCreated)
            {
                SendMessage(Handle, WmSetRedraw, (IntPtr)1, IntPtr.Zero);
                Invalidate(true);
                Update();
            }
        }
    }

    private void ShowPageCore(UiPage page)
    {
        if (page != UiPage.Settings)
        {
            _noUpdateClickCount = 0;
        }

        bool expandedPage = page is UiPage.Stats or UiPage.Leaderboard;
        Size baseClientSize = expandedPage ? new Size(400, 360) : new Size(200, 200);
        Size savedClientSize = expandedPage ? _expandedClientSize : _compactClientSize;
        float savedScale = Math.Max(0.5f, Math.Min(
            savedClientSize.Width / (float)baseClientSize.Width,
            savedClientSize.Height / (float)baseClientSize.Height));

        _applyingPageSize = true;
        _resizeLayoutTimer.Stop();
        _scaleLayoutPending = false;
        try
        {
            MinimumSize = Size.Empty;
            RestoreLayout(_designLayout);
            _currentUiScale = 1f;
        }
        finally
        {
            _applyingPageSize = false;
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
                UiPage.Leaderboard => "排行榜",
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
        _featuresButton.Visible = settings;
        _checkUpdateButton.Visible = settings;
        _aboutButton.Visible = settings;
        _autoStartCheckBox.Visible = settings;
        _settingsStatus.Visible = settings;
        _versionLabel.Visible = settings;
        _uuidLabel.Visible = leaderboard;
        _leaderboardIdTextBox.Visible = leaderboard;
        _editIdButton.Visible = leaderboard;
        _refreshLeaderboardButton.Visible = leaderboard;
        _leaderboardStatus.Visible = leaderboard;
        foreach (Label kind in _leaderboardKindButtons) kind.Visible = leaderboard;
        foreach (Label period in _leaderboardPeriodButtons) period.Visible = leaderboard && IsFirstFiveMetric(_leaderboardMetric);
        foreach (Label entry in _leaderboardEntries) entry.Visible = leaderboard;
        _leaderboardAllButton.Visible = leaderboard;

        _view1.Visible = page is UiPage.Data or UiPage.Stats;
        _view2.Visible = page is UiPage.Data or UiPage.Stats;
        _view3.Visible = page == UiPage.Data;
        _lockButton.Visible = page == UiPage.Data;
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

        int buttonY = stats || leaderboard ? 332 : settings ? 180 : 174;
        int baseX = stats || leaderboard ? (baseClientSize.Width - 116) / 2 : 42;
        _dataButton.Location = new Point(baseX, buttonY);
        _statsButton.Location = new Point(baseX + 24, buttonY);
        _leaderboardButton.Location = new Point(baseX + 48, buttonY);
        _perfButton.Location = new Point(baseX + 72, buttonY);
        _settingsButton.Location = new Point(baseX + 96, buttonY);

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
            _editIdButton.Location = new Point(baseClientSize.Width - _editIdButton.Width - 20, 28);
            _editIdButton.Size = new Size(90, 28);
            for (int i = 0; i < 7; i++)
            {
                _leaderboardKindButtons[i].Location = new Point(10 + i * 56, 70);
                _leaderboardKindButtons[i].Size = new Size(50, 24);
            }
            _uuidLabel.Location = new Point(140, 30);
            _uuidLabel.Size = new Size(120, 16);
            _leaderboardStatus.Location = new Point(20, 300);
            _leaderboardStatus.Size = new Size(300, 20);
            _leaderboardAllButton.Location = new Point(baseClientSize.Width - 70, 296);
            _leaderboardAllButton.Size = new Size(56, 24);
            _drawLuckButton.Location = new Point(130, 180);
            _drawLuckButton.Size = new Size(140, 32);
            _collectionEmptyLabel.Location = new Point(30, 180);
            _collectionEmptyLabel.Size = new Size(340, 32);
            for (int i = 0; i < 4; i++)
            {
                _leaderboardPeriodButtons[i].Location = new Point(374, 140 + i * 30);
                _leaderboardPeriodButtons[i].Size = new Size(22, 24);
            }
            _refreshLeaderboardButton.Location = new Point(20, 28);
            _refreshLeaderboardButton.Size = new Size(80, 28);
            for (int i = 0; i < 5; i++)
            {
                _leaderboardEntries[i].Location = new Point(20, 108 + i * 38);
                _leaderboardEntries[i].Size = new Size(330, 34);
                _leaderboardEntries[i].Font = new Font("Microsoft YaHei UI", 10f);
                FitLabelFont(_leaderboardEntries[i], 10f);
            }
            UpdateLeaderboardKindButtons();
            UpdateLeaderboardPeriodButtons();
            _ = UploadAndRefreshLeaderboardAsync();
            UpdateLuckBoardUi();
            UpdateCollectionBoardUi();
            UpdateCollectionBallVisibility();
        }
        else
        {
            _title.Size = new Size(200, 22);
        }

        _collectionBall.Location = _collectionBallBaseLocation;
        _pageLayout = new Dictionary<Control, ControlLayout>();
        CaptureLayout(_pageLayout);
        _applyingPageSize = true;
        try
        {
            ClientSize = new Size(
                Math.Max(1, (int)Math.Round(baseClientSize.Width * savedScale)),
                Math.Max(1, (int)Math.Round(baseClientSize.Height * savedScale)));
            MinimumSize = new Size(baseClientSize.Width / 2, baseClientSize.Height / 2);
        }
        finally
        {
            _applyingPageSize = false;
        }
        ApplyPageScale();
        UpdateCollectionBallVisibility();
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

            Dictionary<string, double> values = _engine.GetDailyLeaderboardValues(DateTime.Today)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (DateTime.Now.DayOfWeek == DayOfWeek.Monday)
            {
                foreach (KeyValuePair<string, double> pair in _engine.GetDailyLeaderboardValues7Day())
                {
                    values[pair.Key] = pair.Value;
                }
            }
            foreach (KeyValuePair<string, double> pair in _engine.GetDailyLeaderboardValues30Day())
            {
                values[pair.Key] = pair.Value;
            }
            foreach (KeyValuePair<string, double> pair in _engine.GetDailyLeaderboardValuesAllTime())
            {
                values[pair.Key] = pair.Value;
            }
            if (LeaderboardSettingsStore.LoadLuckValue(DateTime.Today) is int luckValue)
            {
                values["luck"] = luckValue;
            }
            bool includeLuck = values.ContainsKey("luck");
            int collectionCount = LeaderboardSettingsStore.LoadCollectionCount();
            if (collectionCount > 0)
            {
                values["collections"] = collectionCount;
            }
            bool includeCollections = collectionCount > 0;
            bool ok = await _leaderboardClient.SubmitAllAsync(
                uuid,
                displayName,
                DateTime.Today,
                values);
            AppLog.Info($"排行榜用户数据上传结果：{ok}");
            Dictionary<string, IReadOnlyList<LeaderboardEntry>> boards =
                await _leaderboardClient.GetBoardsAsync(DateTime.Today, includeLuck, includeCollections);
            AppLog.Info($"排行榜读取完成：{boards.Count} 类榜单");
            foreach (KeyValuePair<string, IReadOnlyList<LeaderboardEntry>> board in boards)
            {
                _leaderboardBoards[board.Key] = board.Value;
            }

            UpdateLeaderboardEntriesFromCache();
            UpdateLeaderboardKindButtons();
            UpdateLuckBoardUi();
            SetLabelText(_leaderboardStatus, ok ? "全部排行榜已同步" : "网络异常，显示本地数据", 8f);
        }
        catch
        {
            AppLog.Info("排行榜同步失败");
            SetLabelText(_leaderboardStatus, "排行榜同步失败", 8f);
            UpdateLuckBoardUi();
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

    private async Task RefreshLeaderboardAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_leaderboardMetric == "collections" && LeaderboardSettingsStore.LoadCollectionCount() == 0)
        {
            SetLabelText(_leaderboardStatus, "你必须先找到至少一个藏品", 8f);
            return;
        }

        if (now - _lastManualLeaderboardRefresh < TimeSpan.FromSeconds(5))
        {
            AppLog.Info("排行榜手动刷新被频率限制");
            SetLabelText(_leaderboardStatus, "刷新太快，请 5 秒后再试", 8f);
            return;
        }

        if (_leaderboardBusy)
        {
            SetLabelText(_leaderboardStatus, "正在同步，请稍后再试", 8f);
            return;
        }

        _lastManualLeaderboardRefresh = now;
        SetLabelText(_leaderboardStatus, "正在刷新排行榜...", 8f);
        await UploadAndRefreshLeaderboardAsync();
    }

    private void UpdateLeaderboardEntriesFromCache()
    {
        if (_leaderboardBoards.TryGetValue(_leaderboardMetric, out IReadOnlyList<LeaderboardEntry>? entries))
        {
            UpdateLeaderboardEntries(entries);
        }
        UpdateLuckBoardUi();
        UpdateCollectionBoardUi();
    }

    private bool IsLuckDrawnToday => LeaderboardSettingsStore.LoadLuckValue(DateTime.Today) is not null;

    private void UpdateLuckBoardUi()
    {
        bool luckBoard = _leaderboardMetric == "luck";
        bool blank = luckBoard && !IsLuckDrawnToday;

        UpdateLeaderboardBoardVisibility();

        if (blank)
        {
            for (int i = 0; i < _leaderboardEntries.Length; i++)
            {
                SetLabelText(_leaderboardEntries[i], "", 10f);
                _toolTip.SetToolTip(_leaderboardEntries[i], null);
            }
            return;
        }

        if (luckBoard)
        {
            if (_leaderboardBoards.TryGetValue("luck", out IReadOnlyList<LeaderboardEntry>? entries))
            {
                UpdateLeaderboardEntries(entries);
            }
            else
            {
                for (int i = 0; i < _leaderboardEntries.Length; i++)
                {
                    SetLabelText(_leaderboardEntries[i], $"{i + 1}. 暂无", 10f);
                }
            }
        }
    }

    private async Task DrawTodayLuckAsync()
    {
        int value = DrawLuckValue();
        LeaderboardSettingsStore.SaveLuckValue(DateTime.Today, value);
        AppLog.Info($"抽取今日运气值：{value}");
        ShowLuckPopup(value);
        await UploadAndRefreshLeaderboardAsync();
        UpdateLeaderboardKindButtons();
        UpdateLuckBoardUi();
    }

    private void ShowLuckPopup(int value)
    {
        if (_luckPopupForm is { IsDisposed: false })
        {
            _luckPopupForm.Close();
            _luckPopupForm.Dispose();
            _luckPopupForm = null;
        }

        Color background = _darkMode ? Color.FromArgb(24, 27, 33) : Color.White;
        Color foreground = _darkMode ? Color.FromArgb(226, 232, 240) : Color.FromArgb(32, 36, 42);
        Color status = _darkMode ? Color.FromArgb(148, 163, 184) : Color.FromArgb(92, 102, 115);

        Form popup = new()
        {
            Text = "今日运气",
            ClientSize = new Size(220, 132),
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            ShowIcon = false,
            Owner = this,
            BackColor = background,
            ForeColor = foreground,
            Font = new Font("Microsoft YaHei UI", 9f),
        };

        Label title = new()
        {
            Text = "今日运气值",
            Location = new Point(10, 6),
            Size = new Size(200, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = Active,
            BackColor = background,
        };
        Label valueLabel = new()
        {
            Text = value.ToString(),
            Location = new Point(10, 26),
            Size = new Size(200, 48),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 26f, FontStyle.Bold),
            ForeColor = foreground,
            BackColor = background,
        };
        Label hint = new()
        {
            Text = "已为你锁定，次日零点重置",
            Location = new Point(10, 76),
            Size = new Size(200, 16),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 7.5f),
            ForeColor = status,
            BackColor = background,
        };
        Label closeButton = CreateTextButton("确定", new Point(78, 96), new Size(64, 28));
        closeButton.Click += (_, _) => popup.Close();

        popup.Controls.Add(title);
        popup.Controls.Add(valueLabel);
        popup.Controls.Add(hint);
        popup.Controls.Add(closeButton);
        popup.Paint += (_, e) =>
        {
            using Pen pen = new(Active);
            e.Graphics.DrawRectangle(pen, 0, 0, popup.ClientSize.Width - 1, popup.ClientSize.Height - 1);
        };
        popup.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_luckPopupForm, popup))
            {
                _luckPopupForm = null;
            }
        };

        Rectangle ui = new(Point.Empty, ClientSize);
        int x = ui.Left + (ui.Width - popup.Width) / 2;
        int y = ui.Top + (ui.Height - popup.Height) / 2;
        x = Math.Max(ui.Left, Math.Min(x, ui.Right - popup.Width));
        y = Math.Max(ui.Top, Math.Min(y, ui.Bottom - popup.Height));
        popup.Location = PointToScreen(new Point(x, y));

        _luckPopupForm = popup;
        popup.Show();
    }

    private static int DrawLuckValue()
    {
        const int min = 0;
        const int max = 100;
        double[] weights = new double[max - min + 1];
        double total = 0;
        for (int value = min; value <= max; value++)
        {
            weights[value - min] = 1 + (value - 50) * 0.01;
            total += weights[value - min];
        }

        double roll = Random.Shared.NextDouble() * total;
        double cumulative = 0;
        for (int value = min; value <= max; value++)
        {
            cumulative += weights[value - min];
            if (roll <= cumulative)
            {
                return value;
            }
        }
        return max;
    }

    private void UpdateCollectionBoardUi()
    {
        bool collectionBoard = _leaderboardMetric == "collections";
        bool blank = collectionBoard && LeaderboardSettingsStore.LoadCollectionCount() == 0;

        UpdateLeaderboardBoardVisibility();

        if (blank)
        {
            foreach (Label entry in _leaderboardEntries)
            {
                SetLabelText(entry, "", 10f);
                _toolTip.SetToolTip(entry, null);
            }
            return;
        }

        if (collectionBoard)
        {
            if (_leaderboardBoards.TryGetValue("collections", out IReadOnlyList<LeaderboardEntry>? entries))
            {
                UpdateLeaderboardEntries(entries);
            }
            else
            {
                for (int i = 0; i < _leaderboardEntries.Length; i++)
                {
                    SetLabelText(_leaderboardEntries[i], $"{i + 1}. 暂无", 10f);
                }
            }
        }
    }

    private void UpdateLeaderboardBoardVisibility()
    {
        bool leaderboardVisible = _page == UiPage.Leaderboard;
        bool blank = (_leaderboardMetric == "luck" && !IsLuckDrawnToday) ||
                     (_leaderboardMetric == "collections" && LeaderboardSettingsStore.LoadCollectionCount() == 0);
        _drawLuckButton.Visible = leaderboardVisible && _leaderboardMetric == "luck" && blank;
        _collectionEmptyLabel.Visible = leaderboardVisible && _leaderboardMetric == "collections" && blank;
        _leaderboardStatus.Visible = leaderboardVisible && !blank;
        foreach (Label entry in _leaderboardEntries)
        {
            entry.Visible = leaderboardVisible && !blank;
        }
        _leaderboardAllButton.Visible = leaderboardVisible && !blank;
    }

    private void UpdateCollectionBallVisibility()
    {
        _collectionBall.Visible = _collectionBallVisible && _collectionPage == _page;
        if (_collectionBall.Visible)
        {
            _collectionBall.BringToFront();
        }
    }

    private void CollectionTimerTick()
    {
        DateTime now = DateTime.UtcNow;
        if (_collectionCooldownUntil > now)
        {
            _lastCollectionRollMinute = -1;
            return;
        }

        if (_collectionCooldownUntil != default && _collectionCooldownUntil <= now)
        {
            _collectionCycleStart = _collectionCooldownUntil;
            _collectionCooldownUntil = default;
            _lastCollectionRollMinute = -1;
        }

        if (_collectionBallVisible)
        {
            return;
        }

        int elapsedMinutes = (int)(now - _collectionCycleStart).TotalMinutes;
        if (elapsedMinutes == _lastCollectionRollMinute)
        {
            return;
        }

        _lastCollectionRollMinute = elapsedMinutes;
        int probability = Math.Min(100, 1 + elapsedMinutes);
        if (Random.Shared.Next(1, 101) <= probability)
        {
            SpawnCollection();
        }
    }

    private void SpawnCollection()
    {
        _collectionBallColor = CreateCollectionColor();
        _collectionPage = (UiPage)Random.Shared.Next(5);
        _collectionBallVisible = true;
        Size pageSize = _collectionPage is UiPage.Stats or UiPage.Leaderboard
            ? new Size(400, 360)
            : new Size(200, 200);
        _collectionBallBaseLocation = GetRandomCollectionPosition(pageSize);
        if (_pageLayout.TryGetValue(_collectionBall, out ControlLayout layout))
        {
            _pageLayout[_collectionBall] = layout with
            {
                Bounds = new Rectangle(_collectionBallBaseLocation, layout.Bounds.Size),
            };
        }
        ApplyPageScale();
        _collectionBall.BallColor = _collectionBallColor;
        _collectionBall.Invalidate();
        UpdateCollectionBallVisibility();
        AppLog.Info($"藏品已生成，页面={_collectionPage}");
    }

    private async Task AcquireCollectionAsync()
    {
        int count = LeaderboardSettingsStore.LoadCollectionCount() + 1;
        LeaderboardSettingsStore.SaveCollectionCount(count);
        _collectionBallVisible = false;
        UpdateCollectionBallVisibility();
        _collectionCooldownUntil = DateTime.UtcNow.AddMinutes(10);
        _collectionCycleStart = _collectionCooldownUntil;
        _lastCollectionRollMinute = -1;
        AppLog.Info($"获取藏品，总数={count}");
        for (int i = 0; i < 50 && _leaderboardBusy; i++)
        {
            await Task.Delay(100);
        }
        await UploadAndRefreshLeaderboardAsync();
        UpdateLeaderboardKindButtons();
        UpdateCollectionBoardUi();
    }

    private Point GetRandomCollectionPosition(Size clientSize)
    {
        const int margin = 28;
        Size ballSize = _designLayout.TryGetValue(_collectionBall, out ControlLayout layout)
            ? layout.Bounds.Size
            : new Size(20, 20);
        int minX = margin;
        int minY = margin;
        int maxX = Math.Max(minX, clientSize.Width - ballSize.Width - margin);
        int maxY = Math.Max(minY, clientSize.Height - ballSize.Height - margin);
        return new Point(
            Random.Shared.Next(minX, maxX + 1),
            Random.Shared.Next(minY, maxY + 1));
    }

    private static Color CreateCollectionColor()
    {
        Color theme = Color.FromArgb(25, 92, 167);
        while (true)
        {
            double hue = Random.Shared.NextDouble() * 360;
            Color color = ColorFromHsv(hue, 1.0, 1.0);
            if (color.R < 20 && color.G < 20 && color.B < 20)
            {
                continue;
            }
            if (color.R > 235 && color.G > 235 && color.B > 235)
            {
                continue;
            }
            int distance = Math.Abs(color.R - theme.R) + Math.Abs(color.G - theme.G) + Math.Abs(color.B - theme.B);
            if (distance < 120)
            {
                continue;
            }
            return color;
        }
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        int hi = (int)Math.Floor(hue / 60.0) % 6;
        double f = hue / 60.0 - Math.Floor(hue / 60.0);
        double p = value * (1 - saturation);
        double q = value * (1 - f * saturation);
        double t = value * (1 - (1 - f) * saturation);
        return hi switch
        {
            0 => Color.FromArgb((int)(value * 255), (int)(t * 255), (int)(p * 255)),
            1 => Color.FromArgb((int)(q * 255), (int)(value * 255), (int)(p * 255)),
            2 => Color.FromArgb((int)(p * 255), (int)(value * 255), (int)(t * 255)),
            3 => Color.FromArgb((int)(p * 255), (int)(q * 255), (int)(value * 255)),
            4 => Color.FromArgb((int)(t * 255), (int)(p * 255), (int)(value * 255)),
            _ => Color.FromArgb((int)(value * 255), (int)(p * 255), (int)(q * 255)),
        };
    }

    private async Task LoadUuidAsync()
    {
        try
        {
            string uuid = await _deviceIdentity.GetUuidAsync();
            AppLog.Info($"UUID 加载成功：{uuid}");
            _uuidLabel.Font = new Font("Microsoft YaHei UI", 7f, FontStyle.Bold);
            _uuidLabel.Text = $"UUid：{uuid}";
            if (_designLayout.ContainsKey(_uuidLabel)) _designLayout[_uuidLabel] = _designLayout[_uuidLabel] with { FontSize = 7f };
            if (_pageLayout.ContainsKey(_uuidLabel)) _pageLayout[_uuidLabel] = _pageLayout[_uuidLabel] with { FontSize = 7f };
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
                ShowUpdateDialog(check.Message, false);
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

        DialogResult answer = ShowUpdateDialog(
            $"发现新版本 {check.Info.Version}，是否下载并安装？", true);
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
            },
            msg =>
            {
                if (!IsDisposed)
                {
                    SetLabelText(_settingsStatus, msg, 8f);
                }
            });
        AppLog.Info($"安装结果：{install.Message}");

        if (!install.Started && !IsDisposed)
        {
            SetLabelText(_settingsStatus, install.Message, 8f);
            ShowUpdateDialog(install.Message, false);
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
        string[] allMetrics = ["active", "mouse_total", "mouse_left", "mouse_right", "keyboard", "luck", "collections"];
        for (int i = 0; i < 7; i++)
        {
            bool active = i < 5
                ? StripPeriodSuffix(_leaderboardMetric) == allMetrics[i]
                : _leaderboardMetric == allMetrics[i];
            _leaderboardKindButtons[i].BackColor = active ? Active : Inactive;
        }
    }

    private void UpdateLeaderboardPeriodButtons()
    {
        bool visible = _page == UiPage.Leaderboard && IsFirstFiveMetric(_leaderboardMetric);
        int[] periods = [1, 7, 30, 0];
        for (int i = 0; i < 4; i++)
        {
            _leaderboardPeriodButtons[i].Visible = visible;
            _leaderboardPeriodButtons[i].BackColor = _leaderboardPeriod == periods[i] ? Active : Inactive;
        }
    }

    private void ApplyLeaderboardPeriodToMetric()
    {
        if (!IsFirstFiveMetric(_leaderboardMetric))
        {
            return;
        }

        string baseMetric = StripPeriodSuffix(_leaderboardMetric);
        _leaderboardMetric = _leaderboardPeriod switch
        {
            7 => baseMetric + "7",
            30 => baseMetric + "30",
            0 => baseMetric + "_total",
            _ => baseMetric,
        };
    }

    private static bool IsFirstFiveMetric(string metric)
    {
        string baseMetric = StripPeriodSuffix(metric);
        return baseMetric is "active" or "mouse_total" or "mouse_left" or "mouse_right" or "keyboard";
    }

    private static readonly string[] PeriodSuffixes = ["_total", "30", "7"];
    private static readonly string[] BaseMetrics = ["active", "mouse_total", "mouse_left", "mouse_right", "keyboard"];

    private static string StripPeriodSuffix(string metric)
    {
        foreach (string suffix in PeriodSuffixes)
        {
            if (metric.EndsWith(suffix, StringComparison.Ordinal))
            {
                string candidate = metric[..^suffix.Length];
                if (BaseMetrics.Contains(candidate))
                    return metric[..^suffix.Length];
            }
        }
        if (BaseMetrics.Contains(metric))
            return metric;
        return metric;
    }

    private string FormatLeaderboardValue(double value)
    {
        if (StripPeriodSuffix(_leaderboardMetric) == "active")
        {
            return Format(TimeSpan.FromSeconds(value));
        }

        return value.ToString("N0");
    }

    private static string FormatBoardValue(string metric, double value)
    {
        if (StripPeriodSuffix(metric) == "active")
        {
            return Format(TimeSpan.FromSeconds(value));
        }

        return value.ToString("N0");
    }

    private static string LeaderboardDisplayName(string metric)
    {
        return StripPeriodSuffix(metric) switch
        {
            "active" => "高强度榜",
            "mouse_total" => "总点击榜",
            "mouse_left" => "左键榜",
            "mouse_right" => "右键榜",
            "keyboard" => "键盘输入榜",
            "luck" => "运气榜",
            "collections" => "藏品榜",
            _ => "排行榜",
        };
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!_layoutReady || _applyingPageSize || WindowState != FormWindowState.Normal)
        {
            return;
        }

        if (_page is UiPage.Stats or UiPage.Leaderboard)
        {
            _expandedClientSize = ClientSize;
        }
        else
        {
            _compactClientSize = ClientSize;
        }

        QueuePageScale();
        QueueWindowPlacementSave();
        
    }

    private void QueuePageScale()
    {
        _scaleLayoutPending = true;
        if (!_resizeLayoutTimer.Enabled)
        {
            _resizeLayoutTimer.Start();
        }
    }

    private void ProcessPendingScaleLayout()
    {
        if (!_scaleLayoutPending)
        {
            _resizeLayoutTimer.Stop();
            return;
        }

        _scaleLayoutPending = false;
        ApplyPageScale();
    }

    private void FlushPageScale()
    {
        _resizeLayoutTimer.Stop();
        _scaleLayoutPending = false;
        ApplyPageScale();
    }

    private void CaptureLayout(Dictionary<Control, ControlLayout> destination)
    {
        destination.Clear();
        foreach (Control control in GetScalableControls(this))
        {
            destination[control] = new ControlLayout(
                control.Bounds,
                control.Font.FontFamily.Name,
                control.Font.Size,
                control.Font.Style,
                control.Font.Unit,
                control.Padding);
        }
    }

    private void RestoreLayout(IReadOnlyDictionary<Control, ControlLayout> source)
    {
        SuspendLayout();
        try
        {
            foreach ((Control control, ControlLayout layout) in source)
            {
                control.Bounds = layout.Bounds;
                control.Padding = layout.Padding;
                ReplaceFont(control, layout.FontFamily, layout.FontSize, layout.FontStyle, layout.FontUnit);
            }
        }
        finally
        {
            ResumeLayout(false);
        }
    }

    private void ApplyPageScale()
    {
        if (_pageLayout.Count == 0)
        {
            return;
        }

        Size baseSize = GetBaseClientSize();
        float scale = Math.Max(0.5f, Math.Min(
            ClientSize.Width / (float)baseSize.Width,
            ClientSize.Height / (float)baseSize.Height));
        _currentUiScale = scale;

        SuspendLayout();
        try
        {
            foreach ((Control control, ControlLayout layout) in _pageLayout)
            {
                control.Bounds = ScaleRectangle(layout.Bounds, scale);
                control.Padding = ScalePadding(layout.Padding, scale);
                if (control != _chart)
                {
                    ReplaceFont(
                        control,
                        layout.FontFamily,
                        QuantizeFontSize(Math.Max(1f, layout.FontSize * scale)),
                        layout.FontStyle,
                        layout.FontUnit);
                }
            }

            _chart.UiScale = scale;
        }
        finally
        {
            ResumeLayout(false);
            Invalidate(true);
        }
    }

    private static IEnumerable<Control> GetScalableControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (Control child in GetScalableControls(control))
            {
                yield return child;
            }
        }
    }

    private Size GetBaseClientSize() =>
        _page is UiPage.Stats or UiPage.Leaderboard ? new Size(400, 360) : new Size(200, 200);

    private static Rectangle ScaleRectangle(Rectangle value, float scale) => new(
        (int)Math.Round(value.X * scale),
        (int)Math.Round(value.Y * scale),
        Math.Max(1, (int)Math.Round(value.Width * scale)),
        Math.Max(1, (int)Math.Round(value.Height * scale)));

    private static Padding ScalePadding(Padding value, float scale) => new(
        (int)Math.Round(value.Left * scale),
        (int)Math.Round(value.Top * scale),
        (int)Math.Round(value.Right * scale),
        (int)Math.Round(value.Bottom * scale));

    private static float QuantizeFontSize(float value) => MathF.Round(value * 4f) / 4f;

    private void ReplaceFont(
        Control control,
        string family,
        float size,
        FontStyle style,
        GraphicsUnit unit)
    {
        if (control.Font.FontFamily.Name == family &&
            Math.Abs(control.Font.Size - size) < 0.05f &&
            control.Font.Style == style &&
            control.Font.Unit == unit)
        {
            return;
        }

        Font replacement = new(family, size, style, unit);
        control.Font = replacement;
        if (_ownedLayoutFonts.Remove(control, out Font? owned))
        {
            owned.Dispose();
        }
        _ownedLayoutFonts[control] = replacement;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmEnterSizeMove)
        {
            _sizingStartSize = Size;
            _sizingAxisLocked = false;
            base.WndProc(ref m);
            return;
        }

        if (m.Msg == WmSizing && WindowState == FormWindowState.Normal)
        {
            ConstrainSizing(m.WParam.ToInt32(), m.LParam);
            m.Result = (IntPtr)1;
            return;
        }

        if (m.Msg == WmExitSizeMove)
        {
            base.WndProc(ref m);
            _sizingAxisLocked = false;
            FlushPageScale();
            return;
        }

        base.WndProc(ref m);
        if (m.Msg != WmNcHitTest || WindowState != FormWindowState.Normal || _isSnapped)
        {
            return;
        }

        Point cursor = PointToClient(Cursor.Position);
        bool left = cursor.X <= ResizeGrip;
        bool right = cursor.X >= ClientSize.Width - ResizeGrip;
        bool top = cursor.Y <= ResizeGrip;
        bool bottom = cursor.Y >= ClientSize.Height - ResizeGrip;

        m.Result = (left, right, top, bottom) switch
        {
            (true, false, true, false) => (IntPtr)HtTopLeft,
            (false, true, true, false) => (IntPtr)HtTopRight,
            (true, false, false, true) => (IntPtr)HtBottomLeft,
            (false, true, false, true) => (IntPtr)HtBottomRight,
            (true, false, false, false) => (IntPtr)HtLeft,
            (false, true, false, false) => (IntPtr)HtRight,
            (false, false, true, false) => (IntPtr)HtTop,
            (false, false, false, true) => (IntPtr)HtBottom,
            _ => m.Result,
        };
    }

    private void ConstrainSizing(int edge, IntPtr rectanglePointer)
    {
        WindowRectangle rectangle = Marshal.PtrToStructure<WindowRectangle>(rectanglePointer);
        Size baseSize = GetBaseClientSize();
        float ratio = baseSize.Width / (float)baseSize.Height;
        int width = rectangle.Right - rectangle.Left;
        int height = rectangle.Bottom - rectangle.Top;

        bool widthDriven;
        if (edge is 1 or 2)
        {
            widthDriven = true;
        }
        else if (edge is 3 or 6)
        {
            widthDriven = false;
        }
        else
        {
            if (!_sizingAxisLocked)
            {
                float widthChange = Math.Abs(width - _sizingStartSize.Width) / (float)baseSize.Width;
                float heightChange = Math.Abs(height - _sizingStartSize.Height) / (float)baseSize.Height;
                _sizingWidthDriven = widthChange >= heightChange;
                _sizingAxisLocked = true;
            }

            widthDriven = _sizingWidthDriven;
        }

        if (widthDriven)
        {
            int adjustedHeight = Math.Max(1, (int)Math.Round(width / ratio));
            if (edge is 3 or 4 or 5)
            {
                rectangle.Top = rectangle.Bottom - adjustedHeight;
            }
            else if (edge is 6 or 7 or 8)
            {
                rectangle.Bottom = rectangle.Top + adjustedHeight;
            }
            else
            {
                rectangle.Bottom = rectangle.Top + adjustedHeight;
            }
        }
        else
        {
            int adjustedWidth = Math.Max(1, (int)Math.Round(height * ratio));
            if (edge is 1 or 4 or 7)
            {
                rectangle.Left = rectangle.Right - adjustedWidth;
            }
            else if (edge is 2 or 5 or 8)
            {
                rectangle.Right = rectangle.Left + adjustedWidth;
            }
            else
            {
                rectangle.Right = rectangle.Left + adjustedWidth;
            }
        }

        Marshal.StructureToPtr(rectangle, rectanglePointer, false);
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
        control.MouseUp += (_, _) => { if (_dragging) { _dragging = false; SnapToNearestEdge(); } };
        control.MouseMove += (_, _) =>
        {
            if (_dragging && !_locked) Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y); else if (_locked) _dragging = false;
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
        EnsureWindowVisible();
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
            StartPosition = FormStartPosition.Manual,
            Font = new Font("Microsoft YaHei UI", 9f),
            ShowInTaskbar = false,
        };
        if (_darkMode)
        {
            _changelogForm.BackColor = Color.FromArgb(24, 27, 33);
            _changelogForm.ForeColor = Color.FromArgb(226, 232, 240);
        }

        RichTextBox textBox = new()
        {
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = Changelog.Text,
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = Color.White,
            DetectUrls = false,
        };
        if (_darkMode)
        {
            textBox.BackColor = Color.FromArgb(15, 18, 22);
            textBox.ForeColor = Color.FromArgb(226, 232, 240);
        }
        _changelogForm.Controls.Add(textBox);
        ToolTip changelogToolTip = new();
        textBox.MouseMove += (_, e) =>
        {
            int index = textBox.GetCharIndexFromPosition(e.Location);
            string text = textBox.Text;
            int start = Math.Max(0, index - 2);
            int length = Math.Min(7, text.Length - start);
            if (length > 0 && text.Substring(start, length).Contains("1.3.1"))
            {
                changelogToolTip.SetToolTip(textBox, "我将开启大娱乐时代！");
            }
            else
            {
                changelogToolTip.SetToolTip(textBox, "");
            }
        };
        _changelogForm.FormClosed += (_, _) =>
        {
            _changelogForm = null;
            changelogToolTip.Dispose();
        };
        _changelogForm.Shown += (_, _) =>
        {
            textBox.SelectionStart = 0;
            textBox.SelectionLength = 0;
        };
        _changelogForm.Show();
    }

    private void ShowAbout()
    {
        AppLog.Info("用户打开关于窗口");
        if (_aboutForm is { IsDisposed: false })
        {
            _aboutForm.Activate();
            return;
        }

        string version = Application.ProductVersion;
        if (Version.TryParse(version, out Version? v))
        {
            int rev = v.Revision;
            version = rev >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}.{rev}" : $"{v.Major}.{v.Minor}.{v.Build}";
        }

        StringBuilder content = new();
        content.AppendLine("软件名称：云曦PC统计");
        content.AppendLine($"版本号：{version}");
        content.AppendLine($"更新日期：{Changelog.CurrentReleaseDate}");
        content.AppendLine("开发人员：Yun_Xi  ahuai");
        content.AppendLine("git地址：https://github.com/YunXi-0/YunXi");

        Form form = new()
        {
            Text = "关于",
            ClientSize = new Size(310, 210),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        if (_darkMode)
        {
            form.BackColor = Color.FromArgb(24, 27, 33);
            form.ForeColor = Color.FromArgb(226, 232, 240);
        }

        RichTextBox textBox = new()
        {
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.None,
            Dock = DockStyle.Fill,
            Text = content.ToString(),
            Font = new Font("Microsoft YaHei UI", 10f),
            BackColor = Color.White,
            DetectUrls = true,
            BorderStyle = BorderStyle.None,
        };
        if (_darkMode)
        {
            textBox.BackColor = Color.FromArgb(15, 18, 22);
            textBox.ForeColor = Color.FromArgb(226, 232, 240);
        }

        form.Controls.Add(textBox);
        form.Location = new Point(
            Left + (Width - form.Width) / 2,
            Top + (Height - form.Height) / 2);
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_aboutForm, form))
            {
                _aboutForm = null;
            }
        };
        _aboutForm = form;
        form.Show();
    }

    private DialogResult ShowUpdateDialog(string message, bool yesNo)
    {
        Form dialog = new()
        {
            Text = "云曦PC统计更新",
            ClientSize = new Size(320, yesNo ? 140 : 120),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        if (_darkMode)
        {
            dialog.BackColor = Color.FromArgb(24, 27, 33);
            dialog.ForeColor = Color.FromArgb(226, 232, 240);
        }

        Label msgLabel = new()
        {
            Text = message,
            Location = new Point(20, 20),
            Size = new Size(280, 40),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        dialog.Controls.Add(msgLabel);

        if (yesNo)
        {
            Button yesBtn = new()
            {
                Text = "是",
                Location = new Point(80, 80),
                Size = new Size(70, 28),
                DialogResult = DialogResult.Yes,
            };
            Button noBtn = new()
            {
                Text = "否",
                Location = new Point(170, 80),
                Size = new Size(70, 28),
                DialogResult = DialogResult.No,
            };
            dialog.Controls.Add(yesBtn);
            dialog.Controls.Add(noBtn);
            dialog.AcceptButton = yesBtn;
            dialog.CancelButton = noBtn;
        }
        else
        {
            Button okBtn = new()
            {
                Text = "确定",
                Location = new Point(125, 70),
                Size = new Size(70, 28),
                DialogResult = DialogResult.OK,
            };
            dialog.Controls.Add(okBtn);
            dialog.AcceptButton = okBtn;
            dialog.CancelButton = okBtn;
        }

        // Position over the main form
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(
            Left + (Width - dialog.Width) / 2,
            Top + (Height - dialog.Height) / 2);
        return dialog.ShowDialog(this);
    }

    private void ShowAllLeaderboard()
    {
        AppLog.Info($"用户打开全部榜单窗口：{_leaderboardMetric}");
        if (_leaderboardAllForm is { IsDisposed: false })
        {
            _leaderboardAllForm.Activate();
            return;
        }

        string metric = _leaderboardMetric;
        _leaderboardBoards.TryGetValue(metric, out IReadOnlyList<LeaderboardEntry>? entries);
        entries ??= [];
        string displayName = LeaderboardDisplayName(metric);
        string periodSuffix = StripPeriodSuffix(metric) != metric
            ? (_leaderboardPeriod switch { 7 => "（7天）", 30 => "（30天）", 0 => "（永久）", _ => "" })
            : "";
        string title = $"全部 · {displayName}{periodSuffix}";

        Form form = new()
        {
            Text = title,
            ClientSize = new Size(400, 400),
            StartPosition = FormStartPosition.Manual,
            Font = new Font("Microsoft YaHei UI", 9f),
            ShowInTaskbar = false,
        };
        if (_darkMode)
        {
            form.BackColor = Color.FromArgb(24, 27, 33);
            form.ForeColor = Color.FromArgb(226, 232, 240);
        }

        RichTextBox textBox = new()
        {
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 9f),
            BackColor = Color.White,
            DetectUrls = false,
        };
        if (_darkMode)
        {
            textBox.BackColor = Color.FromArgb(15, 18, 22);
            textBox.ForeColor = Color.FromArgb(226, 232, 240);
        }

        StringBuilder content = new();
        content.AppendLine(title);
        content.AppendLine();
        if (entries.Count == 0)
        {
            content.AppendLine("暂无数据");
        }
        else
        {
            for (int i = 0; i < entries.Count; i++)
            {
                content.AppendLine($"{i + 1}. {entries[i].Name}  {FormatBoardValue(metric, entries[i].Value)}");
            }
        }
        textBox.Text = content.ToString();

        form.Controls.Add(textBox);
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_leaderboardAllForm, form))
            {
                _leaderboardAllForm = null;
            }
        };
        form.Shown += (_, _) =>
        {
            textBox.SelectionStart = 0;
            textBox.SelectionLength = 0;
        };
        _leaderboardAllForm = form;
        form.Location = FindPopupPosition(form.Size);
        form.Show();
    }

    private void RestoreSavedScale()
    {
        if (_appPosition is not { HasSavedScale: true })
        {
            return;
        }

        float scale = Math.Clamp(_appPosition.Scale, 0.5f, 2f);
        _compactClientSize = ScaleClientSize(new Size(200, 200), scale);
        _expandedClientSize = ScaleClientSize(new Size(400, 360), scale);
    }

    private void RestoreWindowPosition()
    {
        if (_appPosition is { HasSavedPosition: true })
        {
            Rectangle savedBounds = new(
                _appPosition.X,
                _appPosition.Y,
                Width,
                Height);
            if (IsWindowPlacementVisible(savedBounds))
            {
                Location = savedBounds.Location;
                return;
            }
        }

        PositionLeftMiddle();
    }

    private void EnsureWindowVisible()
    {
        if (_isSnapped && !_snapOriginal.IsEmpty)
        {
            _snapAnimTimer?.Stop();
            _isSnapped = false;
            Bounds = _snapOriginal;
        }

        if (!IsWindowPlacementVisible(Bounds))
        {
            PositionLeftMiddle();
        }
    }

    private static bool IsWindowPlacementVisible(Rectangle bounds)
    {
        foreach (Screen screen in Screen.AllScreens)
        {
            Rectangle visible = Rectangle.Intersect(bounds, screen.WorkingArea);
            if (visible.Width >= 32 && visible.Height >= 32)
            {
                return true;
            }
        }
        return false;
    }

    private void QueueWindowPlacementSave()
    {
        if (!_layoutReady || _appPosition is null || WindowState != FormWindowState.Normal)
        {
            return;
        }

        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private void SaveWindowPlacement()
    {
        if (_appPosition is null || WindowState != FormWindowState.Normal)
        {
            return;
        }

        Point location = _isSnapped && !_snapOriginal.IsEmpty
            ? _snapOriginal.Location
            : Location;
        Size baseSize = GetBaseClientSize();
        float scale = Math.Clamp(Math.Min(
            ClientSize.Width / (float)baseSize.Width,
            ClientSize.Height / (float)baseSize.Height), 0.5f, 2f);
        _appPosition.SavePlacement(location, scale);
    }

    private static Size ScaleClientSize(Size size, float scale) => new(
        Math.Max(1, (int)Math.Round(size.Width * scale)),
        Math.Max(1, (int)Math.Round(size.Height * scale)));

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

    private Label CreateTextButton(string text, Point location, Size size)
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

    private void SetLabelText(Label label, string text, float baseSize)
    {
        label.Text = text;
        FitLabelFont(
            label,
            baseSize * _currentUiScale,
            Math.Max(1f, 5.5f * _currentUiScale));
        if (_pageLayout.TryGetValue(label, out ControlLayout layout))
        {
            _pageLayout[label] = layout with
            {
                FontSize = label.Font.Size / Math.Max(0.01f, _currentUiScale),
            };
        }
    }

    private void FitLabelFont(Label label, float startSize, float minSize = 5.5f)
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
        label.Font = replacement;
        if (_ownedLayoutFonts.Remove(label, out Font? owned))
        {
            owned.Dispose();
        }
        else
        {
            oldFont.Dispose();
        }
        _ownedLayoutFonts[label] = replacement;
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

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

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
            string ver = productVersion;
            if (Version.TryParse(productVersion, out Version? v))
            {
                int rev = v.Revision;
                ver = rev >= 0
                    ? $"{v.Major}.{v.Minor}.{v.Build}.{rev}"
                    : $"{v.Major}.{v.Minor}.{v.Build}";
            }
            return $"当前版本：{ver}";
        }
        return $"当前版本：{productVersion}";
    }

    private void ToggleLock()
    {
        _locked = !_locked;
        if (_locked)
        {
            BackColor = Color.FromArgb(1,2,3);
            TransparencyKey = Color.FromArgb(1,2,3);
            foreach (Control ctrl in Controls)
            {
                if (ctrl == _lockButton || ctrl == _title) continue;
                ctrl.Enabled = false;
            }
            _lockButton.Enabled = true;
            SetLabelsTransparent(this);
        }
        else
        {
            BackColor = _darkMode ? Color.FromArgb(24,27,33) : Color.FromArgb(245,247,250);
            TransparencyKey = Color.Empty;
            foreach (Control ctrl in Controls) ctrl.Enabled = true;
            ApplyTheme();
        }
        _lockButton.BackColor = _locked ? Active : Inactive;
        AppLog.Info(_locked ? "页面已锁定" : "页面已解锁");
    }

    private static void SetLabelsTransparent(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is Label) c.BackColor = Color.Transparent;
            SetLabelsTransparent(c);
        }
    }

    private void RestoreDefaultSize()
    {
        _compactClientSize = new Size(200, 200);
        _expandedClientSize = new Size(400, 360);
        if (_appPosition is not null)
        {
            _appPosition.ResetScale();
        }
        AppLog.Info("恢复默认尺寸");
        ShowPage(_page);
    }

    private void ShowFeatures(){AppLog.Info("用户打开功能设置");if(_featuresForm is{IsDisposed:false}){_featuresForm.Activate();return;}Form f=new(){Text="功能设置",ClientSize=new Size(300,170),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.Manual,ShowInTaskbar=false,MaximizeBox=false,MinimizeBox=false,Font=new Font("Microsoft YaHei UI",9f)};CheckBox cb=new(){Text="贴边自动隐藏",Location=new Point(20,30),AutoSize=true,Checked=_appPosition?.SnapToEdge??false,Font=new Font("Microsoft YaHei UI",10f)};cb.CheckedChanged+=(_,_)=>{if(_appPosition is not null)_appPosition.SnapToEdge=cb.Checked;};f.Controls.Add(cb);CheckBox topCb=new(){Text="组件置顶",Location=new Point(20,60),AutoSize=true,Checked=_appPosition?.TopMost??false,Font=new Font("Microsoft YaHei UI",10f)};topCb.CheckedChanged+=(_,_)=>{if(_appPosition is not null){_appPosition.TopMost=topCb.Checked;TopMost=topCb.Checked;}};f.Controls.Add(topCb);Button rstBtn=new(){Text="恢复默认尺寸",Location=new Point(20,100),Size=new Size(120,28),Cursor=Cursors.Hand};rstBtn.Click+=(_,_)=>{RestoreDefaultSize();};f.Controls.Add(rstBtn);Button themeBtn=new(){Text="切换主题",Location=new Point(150,100),Size=new Size(120,28),Cursor=Cursors.Hand};themeBtn.Click+=(_,_)=>{_darkMode=!_darkMode;ApplyTheme();};f.Controls.Add(themeBtn);f.Location=FindPopupPosition(f.Size);f.FormClosed+=(_,_)=>{if(ReferenceEquals(_featuresForm,f))_featuresForm=null;};_featuresForm=f;f.Show();}    private Point FindPopupPosition(Size s){Screen? sc=Screen.FromControl(this);Rectangle a=sc?.WorkingArea??Screen.PrimaryScreen!.WorkingArea;int x=Right,y=Top;if(x+s.Width<=a.Right&&y+s.Height<=a.Bottom)return new Point(x,y);x=Left-s.Width;if(x>=a.Left&&y+s.Height<=a.Bottom)return new Point(x,y);x=Left;y=Bottom;if(x+s.Width<=a.Right&&y+s.Height<=a.Bottom)return new Point(x,y);y=Top-s.Height;if(x+s.Width<=a.Right&&y>=a.Top)return new Point(x,y);return new Point(Math.Clamp(Left,a.Left,a.Right-s.Width),Math.Clamp(Top,a.Top,a.Bottom-s.Height));}
    protected override void OnMove(EventArgs e){base.OnMove(e);if(_appPosition is not null&&WindowState==FormWindowState.Normal){if(!_isSnapped)QueueWindowPlacementSave();SnapToScreenEdge();}}
    private void SnapToNearestEdge(){
        Screen? sc=Screen.FromControl(this);Rectangle a=sc?.WorkingArea??Screen.PrimaryScreen!.WorkingArea;
        if(!a.Contains(Bounds))return;const int d=20;
        int dl=Left-a.Left,dr=a.Right-Right,dt=Top-a.Top,db=a.Bottom-Bottom;
        int mh=Math.Min(Math.Abs(dl),Math.Abs(dr)),mv=Math.Min(Math.Abs(dt),Math.Abs(db));
        if(mh<=d&&mh<=mv){if(Math.Abs(dl)<=Math.Abs(dr)&&Math.Abs(dl)<=d)Left=a.Left;else if(Math.Abs(dr)<=d)Left=a.Right-Width;}
        if(mv<=d&&mv<=mh){if(Math.Abs(dt)<=Math.Abs(db)&&Math.Abs(dt)<=d)Top=a.Top;else if(Math.Abs(db)<=d)Top=a.Bottom-Height;}
        
    }
    private void SnapToScreenEdge(){if(_locked||!(_appPosition?.SnapToEdge??false)||_isSnapped)return;Screen? sc=Screen.FromControl(this);Rectangle a=sc?.WorkingArea??Screen.PrimaryScreen!.WorkingArea;const int p=20;if(Left<=a.Left+p||Right>=a.Right-p)StartSnapDelay(Left<=a.Left+p?-1:1,a);else CancelSnapDelay();}
    private void StartSnapDelay(int edge,Rectangle a){_snapEdge=edge;if(_snapDelayTimer is null){_snapDelayTimer=new System.Windows.Forms.Timer{Interval=2000};_snapDelayTimer.Tick+=(_,_)=>{_snapDelayTimer.Stop();AnimateSnap(edge,a);};}_snapDelayTimer.Stop();_snapDelayTimer.Start();}
    private void CancelSnapDelay(){_snapEdge=0;_snapDelayTimer?.Stop();}
    private void AnimateSnap(int edge,Rectangle a){_snapOriginal=Bounds;_isSnapped=true;int tx=edge<0?a.Left-Width+3:a.Right-3;_snapAnimStep=0;if(_snapAnimTimer is not null)_snapAnimTimer.Stop();_snapAnimTimer=new System.Windows.Forms.Timer{Interval=10};int sx=_snapOriginal.X;_snapAnimTimer.Tick+=(_,_)=>{_snapAnimStep++;int pr=Math.Min(_snapAnimStep*4,100);Left=sx+(tx-sx)*pr/100;if(pr>=100){_snapAnimTimer.Stop();Top=_snapOriginal.Y;if(Bounds.Contains(Cursor.Position))RestoreFromSnap();}};_snapAnimTimer.Start();}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);if(_isSnapped&&_snapAnimTimer is{Enabled:false})RestoreFromSnap();else CancelSnapDelay();}
        protected override void OnResizeEnd(EventArgs e){base.OnResizeEnd(e);}
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(_isSnapped)RestoreFromSnap();}
    private void RestoreFromSnap(){_isSnapped=false;_snapEdge=0;CancelSnapDelay();_snapAnimStep=0;if(_snapAnimTimer is not null)_snapAnimTimer.Stop();_snapAnimTimer=new System.Windows.Forms.Timer{Interval=10};int sx=Left,tx=_snapOriginal.X;_snapAnimTimer.Tick+=(_,_)=>{_snapAnimStep++;int pr=Math.Min(_snapAnimStep*3,100);Left=sx+(tx-sx)*pr/100;if(pr>=100){_snapAnimTimer.Stop();Location=_snapOriginal.Location;}};_snapAnimTimer.Start();}
    protected override void OnFormClosing(FormClosingEventArgs e){_placementSaveTimer.Stop();SaveWindowPlacement();base.OnFormClosing(e);}

}

