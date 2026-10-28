## MODIFIED Requirements

### Requirement: Settings service port and implementation residence
Core SHALL 定义 `ISettingsService` 作为设置读写的唯一端口，保持字符串键值与领域类型无关。文件实现 SHALL 位于 Platform.Windows 基础设施层，由 App 与 CLI 各自在入口组装并复用；Core SHALL 不依赖该文件实现。ViewModel 和功能设置适配 SHALL 仅依赖 Core 端口。

#### Scenario: Port resides in Core, implementation in infrastructure
- **WHEN** 架构测试检查设置依赖方向
- **THEN** `ISettingsService` 位于 `KotobaSenpai.Core`，文件实现位于 `KotobaSenpai.Platform.Windows`；UI 和 CLI 入口可以创建该实现，ViewModel 与功能设置适配只依赖接口

#### Scenario: Port is free of feature-specific types
- **WHEN** 检查 `ISettingsService`
- **THEN** 仅暴露字符串键值及其快照、条件批量操作，不引用主题、日志、本地化或其他功能专属类型

## ADDED Requirements

### Requirement: 原子字符串键值快照与条件批量提交
`ISettingsService` SHALL 提供领域类型无关的字符串键值批量快照和条件批量提交能力，空值继续表示删除键。接口 SHALL 仅声明契约，由具体实现使用私有同步机制保证一致性。条件提交 SHALL 在同一临界区比较指定相关键并应用全部变更。文件 SHALL 只由 Platform.Windows 基础设施层的共用 `SettingsService` 实现访问，UI 与 CLI SHALL 复用该实现。单键和批量保存均 SHALL 通过同目录临时文件与原子替换持久化，成功后再发布新内存状态；失败 SHALL 保留原内存和文件。无关设置与未知字段 SHALL 保留。本变更 SHALL 不要求通用设置变更事件或跨进程文件监听，CLI 写入后的界面通过重新启动读取最终值。

#### Scenario: 整体读取多个键
- **WHEN** 另一个操作正在替换完整 LLM 配置
- **THEN** 一次批量读取只得到替换前或替换后的完整快照，不混合两者的密钥、地址或模型

#### Scenario: 保存失败
- **WHEN** 临时文件写入或最终替换失败
- **THEN** 原配置文件仍有效，内存仍为原值，不返回保存成功

#### Scenario: 相关键发生冲突
- **WHEN** 条件提交发现任一预期 LLM 键已被改变
- **THEN** 整批变更均不应用，并返回冲突

#### Scenario: 无关偏好发生变化
- **WHEN** UI 内部检测期间只修改语言或主题，而 LLM 相关键仍匹配
- **THEN** 允许提交，并保留最新的语言、主题及未知字段

### Requirement: 跨进程独占写入与只读快照
GUI 与 CLI SHALL 共用按 Windows 用户区分的进程互斥，在创建可写设置实例和加载缓存之前取得；GUI 持有到退出，CLI 持有到配置操作结束。Mutex SHALL 由取得它的同一线程释放，包括异步配置路径。无法取得写入权 SHALL 有界返回忙碌，不加载可写旧缓存、不修改文件。只读命令 SHALL 允许在 GUI 运行时通过基础设施设置服务读取完整文件快照，但不保存任何设置、不依赖其他启动副作用写入，也不将此缓存用于后续写入。CLI SHALL 不自行实现文件读写或 JSON 持久化。

#### Scenario: 配置进程加载最新值
- **WHEN** GUI 正常退出后，CLI 取得写入互斥
- **THEN** 再由现有设置服务加载完整最新文件，不采用关闭 GUI 前读取的缓存

#### Scenario: 已有进程正在写入
- **WHEN** 另一个 GUI 或 CLI 已持有写入互斥
- **THEN** 新写入者返回忙碌，不产生同时拥有设置缓存的两个写入进程

#### Scenario: GUI 写入时查询状态
- **WHEN** 只读状态查询与 GUI 的单键或批量保存同时发生
- **THEN** 查询只读取原子替换前或替换后的完整 JSON，不观察半份文件、不写回读取的缓存

#### Scenario: 配置后重新打开设置页
- **WHEN** CLI 完成操作并释放互斥，GUI 再次启动
- **THEN** 新设置服务和 ViewModel 从最终持久化配置初始化，不需要外部变更通知或文件监听
