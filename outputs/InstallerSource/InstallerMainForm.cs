namespace CloudXiPcMonitor.Installer;

internal sealed class InstallerMainForm : Form
{
    private readonly TextBox _installPathTextBox;
    private readonly CheckBox _autoStartCheckBox;
    private readonly CheckBox _desktopCheckBox;
    private readonly CheckBox _runCheckBox;
    private readonly Label _statusLabel;
    private readonly Button _installButton;
    private readonly System.Windows.Forms.Timer _closeTimer;
    private int _closeSeconds = 5;

    public InstallerMainForm()
    {
        Text = "云曦PC统计 安装程序";
        ClientSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(245, 247, 250);
        ForeColor = Color.FromArgb(32, 36, 42);

        Label title = new()
        {
            Text = "云曦PC统计 安装程序",
            Location = new Point(24, 20),
            Size = new Size(512, 34),
            Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(25, 92, 167),
        };

        Label pathLabel = new()
        {
            Text = "安装目录：",
            Location = new Point(28, 78),
            Size = new Size(120, 24),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _installPathTextBox = new TextBox
        {
            Location = new Point(118, 78),
            Size = new Size(330, 24),
            Text = "D:\\",
        };

        Button browseButton = new()
        {
            Text = "浏览...",
            Location = new Point(458, 76),
            Size = new Size(78, 28),
            Cursor = Cursors.Hand,
        };
        browseButton.Click += (_, _) =>
        {
            using FolderBrowserDialog dialog = new()
            {
                Description = "选择安装目录",
                SelectedPath = _installPathTextBox.Text,
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true,
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _installPathTextBox.Text = dialog.SelectedPath;
            }
        };

        _autoStartCheckBox = new CheckBox
        {
            Text = "开机启动",
            Location = new Point(32, 132),
            Size = new Size(200, 26),
            Checked = true,
        };

        _desktopCheckBox = new CheckBox
        {
            Text = "创建桌面快捷方式",
            Location = new Point(32, 166),
            Size = new Size(220, 26),
            Checked = true,
        };

        _runCheckBox = new CheckBox
        {
            Text = "安装完成后立即运行",
            Location = new Point(32, 200),
            Size = new Size(240, 26),
            Checked = true,
        };

        _statusLabel = new Label
        {
            Text = "请选择安装目录和安装选项。",
            Location = new Point(28, 250),
            Size = new Size(508, 26),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(92, 102, 115),
        };

        Label noteLabel = new()
        {
            Text = "本组件安装完成后仅会存在一个.exe文件以及两个文件夹",
            Location = new Point(28, 302),
            Size = new Size(508, 20),
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = Color.FromArgb(120, 126, 134),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _installButton = new Button
        {
            Text = "安装",
            Location = new Point(340, 360),
            Size = new Size(96, 32),
            BackColor = Color.FromArgb(25, 92, 167),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        _installButton.FlatAppearance.BorderSize = 0;
        _installButton.Click += async (_, _) => await InstallAsync();

        Button cancelButton = new()
        {
            Text = "取消",
            Location = new Point(444, 360),
            Size = new Size(92, 32),
            Cursor = Cursors.Hand,
        };
        cancelButton.Click += (_, _) => Close();

        _closeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _closeTimer.Tick += (_, _) =>
        {
            _closeSeconds--;
            if (_closeSeconds <= 0)
            {
                _closeTimer.Stop();
                Close();
            }
            else
            {
                _statusLabel.Text = $"安装完成，该窗口将在 {_closeSeconds} 秒后关闭";
            }
        };

        Controls.Add(title);
        Controls.Add(pathLabel);
        Controls.Add(_installPathTextBox);
        Controls.Add(browseButton);
        Controls.Add(_autoStartCheckBox);
        Controls.Add(_desktopCheckBox);
        Controls.Add(_runCheckBox);
        Controls.Add(_statusLabel);
        Controls.Add(noteLabel);
        Controls.Add(_installButton);
        Controls.Add(cancelButton);
    }

    private async Task InstallAsync()
    {
        string installDirectory = _installPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            MessageBox.Show(this, "请先选择安装目录。", "云曦PC统计 安装程序");
            return;
        }

        _installButton.Enabled = false;
        _statusLabel.Text = "正在安装...";

        Progress<string> progress = new(message => _statusLabel.Text = message);
        try
        {
            bool runStarted = await Task.Run(() => InstallerCore.Install(
                installDirectory,
                _autoStartCheckBox.Checked,
                _desktopCheckBox.Checked,
                _runCheckBox.Checked,
                progress));

            _statusLabel.Text = runStarted
                ? "安装完成，该窗口将在 5 秒后关闭"
                : "安装完成，自动启动失败，请手动启动；该窗口将在 5 秒后关闭";
            _closeSeconds = 5;
            _closeTimer.Start();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "安装失败。";
            MessageBox.Show(this, ex.Message, "云曦PC统计 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _installButton.Enabled = true;
        }
    }
}
