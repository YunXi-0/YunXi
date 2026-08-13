# AGENT.md

## 编码约定

- C# 源码文件：使用 UTF-8 BOM，本地工作区统一 CRLF，Git 索引统一 LF（由 `.gitattributes` 约束）。
- `Changelog.cs`：使用 UTF-8 BOM，本地工作区 CRLF。
- `.csproj`、`.manifest`、`.ico`：不要重新编码，保持原样。
- PowerShell 脚本：尽量使用 ASCII；如果必须包含中文，脚本文件使用 UTF-8 BOM。
- Python 脚本：通过 PowerShell heredoc 执行时，中文一律写成 `\uXXXX` 转义；写文件时明确使用 `utf-8` 或 `utf-8-sig`。
- GitHub API JSON：使用 `ensure_ascii=False` 并编码为 UTF-8，同时设置 `Content-Type: application/json`。

## 文件修改

- 手动改代码使用 `apply_patch`。
- 批量修改换行或 BOM 使用 Python，不要用 PowerShell heredoc 直接写入中文。
- 更新 `Changelog.cs` 时，在现有顶部插入新版本条目，使用 Python + Unicode 转义，避免中文乱码。
- 不要使用旧的 `changelog_raw.txt` 重建 `Changelog.cs`。

## 发布

- Release 更新时间必须读取 GitHub API 的 `published_at` 并转换为 UTC+8，禁止估算。
- 创建或修改 Release body 使用 Python `urllib`，JSON 使用 `ensure_ascii=False`；不要用 PowerShell 的 `ConvertTo-Json` 处理中文发布说明。
- `dotnet build` 后、执行 `publish-installer.ps1` 前，必须重新执行：
  - `dotnet restore -r win-x64 .../PcCompanionMonitor.csproj`
  - `dotnet restore -r win-x64 .../CloudXiInstaller.csproj`
