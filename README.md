# 云曦 PC 统计

云曦 PC 统计是一款桌面时间与键鼠使用统计工具，提供数据、统计、排行榜、性能和设置页面。当前支持 Windows 与 Linux GNOME 桌面。

## Windows

### 安装

从 [Releases](https://github.com/YunXi-0/YunXi/releases/latest) 下载 `YunXiStatistician.exe` 并运行。安装时可以选择安装目录、开机启动、桌面快捷方式以及安装完成后立即运行。

### 使用

- `数 / 统 / 榜 / 性 / 设` 用于切换数据、统计、排行榜、性能和设置页面。
- 未锁定时可以拖动组件；锁定后组件固定在桌面位置。
- 设置页的“功能”可配置贴边、主题、计时器和其他组件行为。
- 排行榜页面可修改 ID、刷新数据，并切换榜单和统计周期。
- 右键组件可以打开菜单并退出程序。

### 更新

程序会在后台检测新版本，也可以在设置页手动检测。确认更新后会下载并校验安装程序的 GitHub Release SHA-256，然后自动完成更新；启动失败时会恢复旧版本。

### 数据

Windows 端的每日统计、键鼠数据、设置、窗口位置、UUID 和电源会话保存在安装目录的 `data` 文件夹：

```text
<安装目录>\data\
```

运行日志保存在安装目录的 `log` 文件夹。自动更新下载缓存位于：

```text
%LOCALAPPDATA%\CloudXiPcMonitor\updates\
```

删除 `data` 文件夹会清除 Windows 端本地统计和设置，不会删除排行榜中已经上传的记录。

## Linux

Linux 端以 GNOME Shell 扩展形式运行，支持 GNOME 45 至 50。

### 安装

从 [Releases](https://github.com/YunXi-0/YunXi/releases/latest) 下载 `YunXiStatistician-Linux-GNOME.zip`，在文件所在目录打开终端后执行：

```bash
gnome-extensions install --force ./YunXiStatistician-Linux-GNOME.zip
gnome-extensions enable YunXiStatistician
```

安装完成后，桌面会显示云曦组件。若扩展没有立即出现，注销并重新登录一次。

### 使用

- `数 / 统 / 榜 / 性 / 设` 用于切换数据、统计、排行榜、性能和设置页面。
- 未锁定时，按住标题、数据文字或空白区域可以拖动组件，拖动任意边缘或角落可以等比缩放。
- 缩放范围为 75% 至 200%，位置和比例会自动保存。
- 数据页右侧的 `锁` 用于锁定位置。锁定后组件不拦截桌面鼠标，只有蓝色 `锁` 可以点击解锁。
- 设置中的“隐藏主界面”会隐藏组件；通过 GNOME 顶栏的“云”菜单可以重新打开。
- 右键组件可以打开菜单并退出扩展。

### 更新

扩展会在登录后自动检查新版本，也可以在设置页选择“检测最新”。确认下载后会自动完成镜像下载、GitHub SHA-256 校验、安装和重载；更新失败时会恢复旧版本。

自动更新无法使用时，可以下载新版 ZIP 并重新执行安装命令覆盖旧版本。

### 数据

Linux 端的键鼠统计、运行时间、排行榜数据和设置保存在：

```text
~/.local/share/yunxi/gnome-statistics.json
```

删除该文件会清除 Linux 端本地统计和设置，不会删除排行榜中已经上传的记录。性能页显示的是整个 GNOME Shell 进程数据，其中 `GNOME Shell 内存` 不是扩展单独占用的内存。

### 卸载

```bash
gnome-extensions disable YunXiStatistician
gnome-extensions uninstall YunXiStatistician
```
