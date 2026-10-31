## MODIFIED Requirements

### Requirement: CLI 写入前正常退出应用并在结束后恢复 UI
`llm configure` SHALL 在完成静态输入校验后，请求当前 Windows 用户、与本次安装路径匹配的已运行 GUI 正常退出，并最多等待 10 秒。无法退出 SHALL 返回 `app_busy`，不强杀、不写入。CLI 取得配置文件写入互斥后 SHALL 才能加载最新设置并在本进程执行；GUI 与 CLI SHALL 不能同时持有写入权。操作成功、失败或取消后，CLI SHALL 在本进程操作结束并释放互斥后恢复本次关闭的 GUI；原本未打开 GUI 时 SHALL 不主动打开。此流程 SHALL 不增加额外的用户保存确认。

#### Scenario: GUI 正在运行时配置
- **WHEN** Agent 提交资料完整的配置请求
- **THEN** 正常退出应用并等待其完成资源清理，CLI 再独占执行配置；操作结束并释放互斥后重新打开 GUI，从磁盘显示最终配置

#### Scenario: 正常退出超时
- **WHEN** 已运行 GUI 在 10 秒内没有完成退出
- **THEN** 返回忙碌且不发供应商验证请求、不写设置、不强杀或重复启动进程，不把尚未完成的退出报告为成功

#### Scenario: 配置失败或尚未提交时取消
- **WHEN** GUI 已退出，而候选验证、保存或提交前取消导致操作失败
- **THEN** 保留原配置，在本进程操作结束并释放互斥后尝试恢复原先打开的 GUI，并向 Agent 如实报告结果

#### Scenario: 已保存但界面恢复失败
- **WHEN** 配置已成功原子提交，但 GUI 无法重新启动
- **THEN** 返回退出码 `0`、`applied` 为真和 UI 恢复警告，提供手动启动建议，不回滚或重复保存

#### Scenario: 写入期间又打开 GUI 或执行其他配置命令
- **WHEN** CLI 已持有写入互斥，另一个 GUI 或 CLI 尝试写入
- **THEN** 后者有界返回忙碌，不创建第二个写入者，不转交界面激活，也不让一个 CLI 结束另一个 CLI

#### Scenario: 退出后尚未取得互斥
- **WHEN** CLI 已正常关闭 GUI，但另一个进程先取得写入互斥
- **THEN** 本次操作返回忙碌，不使用旧缓存写入，也不在互斥仍被占用时另起可写 GUI

#### Scenario: 无法确认提交结果
- **WHEN** 未预期异常导致无法确认配置是否已提交
- **THEN** 返回 `applied` 为空，不声称配置必然未变，释放本进程写入互斥后处理 GUI 恢复，并给出先查询状态的建议

#### Scenario: Restore a GUI that was showing model onboarding
- **WHEN** 本次正常关闭的 GUI 原本位于首次引导的模型配置步骤，且配置操作已经结束并释放互斥
- **THEN** CLI 按既有规则尝试重新打开同一安装的 GUI；GUI 依据持久化引导检查点显示第二步及最终配置结果，不将已具备词典和模型验证视为完成引导
- **AND** CLI 不修改引导检查点，不新增命令、强杀进程、文件监听或额外保存确认

#### Scenario: Restore onboarding after an unsuccessful configuration
- **WHEN** 首次引导中的 GUI 已正常退出，但外部配置失败或在提交前取消
- **THEN** 恢复后的 GUI 仍在模型步骤显示保存的原配置及其实际验证状态，不虚构配置成功或绕过后续选窗
