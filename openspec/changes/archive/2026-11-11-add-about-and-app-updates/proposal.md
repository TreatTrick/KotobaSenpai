## Why

用户现在需要自己去 GitHub 找新版本、下载安装包。增加“关于”页后，用户可以在应用内查看版本、下载更新，并点击一次按钮完成安装和重启。

## What Changes

- 左侧导航增加“关于”，展示应用简介、当前版本、官网、GitHub 和问题反馈入口。
- 打开关于页时自动检查 GitHub 最新正式版，也可以手动重新检查。
- 发现新版后显示版本和更新说明；用户点击后下载，支持进度、取消、失败重试。
- 安装包校验通过后显示“更新并重启”。点击后正常结束识别并退出，由现有 Inno Setup 显示安装进度，安装成功后启动新版。
- 更新保留设置、API 配置和已安装词典。网络异常不影响识别功能。
- 统一应用和安装包的版本来源；新页面支持中英文和 FluentLight。

## Capabilities

### New Capabilities

- `about-and-app-updates`：关于页、正式版检查、安装包下载校验、安装和重启。

### Modified Capabilities

- `core-slice-architecture`：将 Updates 登记为第 1 层能力，继续遵守现有依赖方向，并让分层表与现有架构测试一致。

## Impact

- Core 定义更新所需的数据和外部操作接口；Windows 层负责 GitHub、下载文件和启动安装器；App 负责页面状态和退出协调。
- 在 DesktopComposition 中统一装配，保持 ViewModel 不依赖 Windows 实现、WPF 或服务容器。
- 复用现有本地化、日志、共享样式、关闭流程和 Inno Setup；不增加第三方更新框架。
- 修改打包脚本，增加更新模式以及明确的版本来源。
