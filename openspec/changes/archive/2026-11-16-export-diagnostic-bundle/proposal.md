## Why

用户遇到错误后很难手动找到并整理日志与诊断文件，导致问题反馈缺少上下文。需要提供一键生成诊断 ZIP 的入口，并让用户在生成前清楚看到本次收录内容。

## What Changes

- 新增诊断包导出能力：确认后将当前保留的日志和已有诊断文件写入 ZIP。
- 生成前显示醒目的确认对话框，逐项列出本次实际会收录的内容；用户取消时不生成文件。
- 将没有显式配置时的最低日志级别改为 Debug；用户已经设置的级别继续生效。

## Capabilities

### New Capabilities
- `diagnostic-bundle-export`: 从设置页确认并生成包含本地日志与现有诊断文件的 ZIP。

### Modified Capabilities
- `logging`: 未设置最低日志级别时默认记录 Debug 及以上内容。

## Impact

- 影响 App 设置页、日志配置、资源本地化和诊断包文件生成。
- ZIP 保存到用户选择的位置；不自动上传，不包含 `settings.json` 或凭据。
- 复用 .NET ZIP API，不新增依赖。
