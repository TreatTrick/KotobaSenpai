## MODIFIED Requirements

### Requirement: Preserve local fallback
识别 SHALL 始终执行云端短语分析流程，分析器作为识别服务的必需依赖，不提供独立本地分析的禁用分支。等待云端响应时 SHALL 先显示本地读音，不提前显示成功分析的下划线。缺少配置、取消、超时、传输失败、服务拒绝或无效响应 SHALL 返回可重试状态，不使应用崩溃，也不将本地结果伪装成完整分析成功。只有成功句段 SHALL 获得分析下划线和服务商解释。历史启用开关 SHALL 不影响该流程。

#### Scenario: Provider timeout
- **WHEN** 云端请求超时
- **THEN** 保留本地读音且不渲染无效组合组，显示可重试失败状态，不进入独立本地模式

#### Scenario: Missing API key
- **WHEN** 没有配置 API Key
- **THEN** 分析流程返回需要配置的状态，保留读音预览而不显示分析成功的下划线，不发送缺少凭据的请求

#### Scenario: Historical disabled value
- **WHEN** 历史设置中的启用值为 false 或缺失
- **THEN** 仍进入相同云端分析流程，不保留绕过分析的本地分支

#### Scenario: Partial sentence failure
- **WHEN** 一个句段成功而另一个句段失败
- **THEN** 最终只对成功句段显示下划线，保留所有句段的读音，并报告可重试警告
