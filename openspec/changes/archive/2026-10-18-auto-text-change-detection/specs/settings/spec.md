## MODIFIED Requirements

### Requirement: 设置服务中的自动识别开关

系统 SHALL 在 `ISettingsService` 中支持 `AutoRecognitionEnabled` 布尔设置项，用于控制自动轮询识别功能的开关状态。该设置项 SHALL 遵循与既有设置相同的存储和读写约定：通过 `GetValue`/`SetValue` 以字符串形式读写（值 `"true"` 表示启用，其他值或缺省表示禁用）。

#### Scenario: 读取自动识别设置
- **WHEN** 调用方读取 `AutoRecognitionEnabled` 设置
- **THEN** 返回 `"true"` 或 `"false"` 字符串，SHALL NOT 抛出异常

#### Scenario: 写入自动识别设置
- **WHEN** 调用方写入 `AutoRecognitionEnabled = "true"`
- **THEN** 设置值持久化到 settings.json，后续读取 SHALL 返回 `"true"`

#### Scenario: 未设置时返回默认值
- **WHEN** 用户从未设置过 `AutoRecognitionEnabled`
- **THEN** 读取返回 null，系统 SHALL 默认禁用自动识别

*(以上为新增要求，不影响既有 requirements)*

## ADDED Requirements

### Requirement: 自动识别设置开关持久化
`AutoRecognitionEnabled` 设置项 SHALL 遵循现有 settings 服务的持久化约束：与其他设置共存于同一文件，写入时 SHALL NOT 擦除其他键。

#### Scenario: 写入自动识别不擦除其他设置
- **WHEN** settings.json 中包含 `{"Language":"zh-CN","Theme":"Dark"}`，设置 `AutoRecognitionEnabled` 为 `"true"`
- **THEN** 持久化后的文件 SHALL 同时包含 `Language`、`Theme` 和 `AutoRecognitionEnabled`
