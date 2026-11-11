# 关于页与应用更新

## 用户怎么使用

1. 点击左侧“关于”。本次运行第一次打开时会自动检查 GitHub 最新正式版，也可以点击“检查更新”。
2. 有新版时，页面显示版本、安装包大小和更新说明。点击“下载更新”后才会下载，可以随时取消。
3. 下载后会检查大小和 SHA-256。通过校验后，点击“更新并重启”。
4. 应用正常停止识别并退出，安装器显示进度，安装成功后自动打开新版。不会自动重启 Windows。

设置、API 配置和已安装词典保存在原来的用户数据目录，升级不会清空它们。网络失败不会影响识别。

从源码或未登记的目录运行时，可以检查和下载，但不允许自动覆盖开发目录。请使用“查看发布页”手动安装。

## 发布新版本

默认版本保存在仓库根目录的 `Directory.Build.props`。修改其中的三段版本号后打包，例如：

```powershell
pwsh -File scripts/Build-Installer.ps1
```

也可以在本次打包时指定版本。脚本会把同一个版本传给应用和安装器：

```powershell
pwsh -File scripts/Build-Installer.ps1 -Version 1.0.2
```

发布 GitHub Release 时需要保持以下信息一致：

| 项目 | 示例 |
| --- | --- |
| 构建版本 | `1.0.2` |
| Release 标签 | `v1.0.2` 或 `1.0.2` |
| 必须上传的安装包 | `KotobaSenpai-1.0.2-win-x64-Setup.exe` |
| GitHub 资产摘要 | `digest` 字段为 `sha256:` 加 64 位十六进制摘要 |

先上传全部资产再发布正式 Release。应用只检查 `/releases/latest`，不接收草稿和预览版。安装包缺失、名字不匹配或没有有效摘要时，会提示该版本暂不可用，禁止安装。

脚本仍生成不带版本号的 `KotobaSenpai-win-x64-Setup.exe` 供网站链接使用。应用内更新使用带版本号的资产，避免下载到与检查结果不一致的版本。

首次提供此功能的版本仍需要用户通过现有安装包安装。后续版本必须继续使用 `scripts/KotobaSenpai.iss`，保留现有 AppId 和更新参数：`/APPUPDATE=1 /UPDATEPID=旧进程ID`。更新模式等待旧进程最多 60 秒；普通安装继续提供原有向导。

## 代码分工

- `Core/Updates`：更新数据、更新来源和安装接口。没有 HTTP、文件或 WPF 依赖。
- `Platform.Windows/Updates`：GitHub 查询、下载、文件校验、安装目录登记检查、启动 Inno Setup。
- `AboutPageViewModel`：按钮状态、进度、取消、错误提示和语言变化。
- `AboutPage`：页面展示和打开固定网站。复用共享样式和主窗口 FluentLightHost。
- `DesktopComposition`：装配依赖。`DesktopSession`：收到安装交接后走原来的正常关闭流程。

## 本地验证

常规测试包含网络替身、取消、下载校验、安装交接、架构方向、本地化和 FluentLight 检查。

```powershell
dotnet test KotobaSenpai.slnx -c Release --filter 'FullyQualifiedName!~PhrasePopupThemeTests'
dotnet test tests/KotobaSenpai.App.Tests -c Release --filter FullyQualifiedName~PhrasePopupThemeTests
pwsh -File tests/Check-AppUpdateInstaller.ps1 -InnoSetupCompiler 'C:\Tools\Inno Setup 7\ISCC.exe'
```

最后一个脚本使用独立 AppId、临时程序和 `artifacts` 下的安装目录，实际检查等待旧进程、安装后只启动一次、保留已有文件，以及超时后安全中止。不会修改真实的 KotobaSenpai 安装目录或用户配置。日志保留在脚本输出的目录。
