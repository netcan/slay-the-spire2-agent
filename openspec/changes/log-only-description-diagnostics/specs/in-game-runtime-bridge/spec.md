## MODIFIED Requirements

### Requirement: 文本解析失败时必须提供可诊断的降级信息
系统 MUST 在文本解析失败、只能拿到模板、或仅能部分解析时保持 fail-safe。bridge MUST NOT 因为个别文本字段解析失败而使整个 `snapshot` 或 `actions` 构建失败；同时，说明解析来源、变量提取结果、fallback 原因与 unresolved 线索 MUST 优先写入 mod 日志或等效本地调试输出，而不是进入面向 agent 的主响应字段。面向 agent 的 `name`、`label`、`description`、`relics`、`rewards` 等主字段 MUST 保持简洁、稳定和可读。

#### Scenario: 无法解析本地化文本时仍返回稳定快照
- **WHEN** 某个 runtime 对象的文本字段无法通过本地化、动态变量或约定字段解析
- **THEN** bridge MUST 仍然返回可序列化的 `snapshot` 或 `actions` 响应
- **THEN** mod MUST 在日志或等效本地调试输出中记录该字段使用了 fallback、partial 或 unresolved 语义

#### Scenario: 说明解析 diagnostics 不得污染面向 Agent 的主字段
- **WHEN** bridge 需要暴露文本解析来源、失败原因或变量提取结果
- **THEN** diagnostics MUST 优先进入 mod 日志、调试文件或等效本地排障通道
- **THEN** 公共 `snapshot` 与 `actions` 字段 MUST NOT 因此新增仅供诊断使用的说明结构

#### Scenario: 默认日志不得因成功路径而刷屏
- **WHEN** bridge 在 live runtime 中持续解析大量正常说明文本
- **THEN** mod MAY 提供显式 debug 开关输出逐条成功解析日志
- **THEN** 默认日志 MUST 优先覆盖失败、降级或异常路径，而不是无条件打印所有成功记录
