# llm-protocol-abstraction Delta

## ADDED Requirements

### Requirement: 语言感知的共享 prompt
在协议信封包装之前构建的共享语义 prompt SHALL 针对当前激活的 UI 文化本地化，且所选中的任一协议 SHALL 传输同一份本地化 prompt。结构化输出 schema SHALL 声明语言中性的 `meaning` 与 `grammar` 字段，而非旧的 `meaningZh`/`grammarZh` 字段名。

#### Scenario: 本地化 prompt 跨协议复用
- **WHEN** 当前 UI 文化为 `en` 且通过任一协议发送一个短语请求
- **THEN** 每个协议 SHALL 传输由共享 prompt 构建器构建的同一份英文 prompt。

#### Scenario: schema 使用语言中性字段名
- **WHEN** 任一协议构建其结构化输出声明
- **THEN** group schema SHALL 要求 `meaning` 与 `grammar` 字段，而非 `meaningZh`/`grammarZh`。

#### Scenario: 本地化后各协议产出一致的分组
- **WHEN** 同一份本地化的短语请求通过三种协议中任一发送
- **THEN** 每个协议的结构化 group 数组由同一个 group 解析器校验并得到相同的 groups。