## ADDED Requirements

### Requirement: Mod 必须提供本地只读 bridge 健康检查接口
系统 MUST 提供本机 loopback 可访问的健康检查接口，用于返回 bridge 服务是否已启动、当前 mod 版本和协议版本。

#### Scenario: 本地 agent 检查 mod bridge 是否可用
- **WHEN** 本地 agent 对健康检查 endpoint 发起请求
- **THEN** mod 返回可解析的成功响应，其中包含 bridge 可用状态和版本信息

### Requirement: Mod 必须提供当前状态快照查询接口
系统 MUST 提供本机 loopback 可访问的状态快照查询接口，并返回与 `mod-state-export` 能力一致的结构化 observation。

#### Scenario: agent 读取当前实时游戏状态
- **WHEN** agent 对状态查询 endpoint 发起请求
- **THEN** mod 返回当前决策窗口的结构化快照，且字段命名与约定协议保持一致

### Requirement: Mod 必须提供合法动作查询接口
系统 MUST 提供本机 loopback 可访问的合法动作查询接口，并返回当前决策窗口下全部合法动作的结构化列表。

#### Scenario: agent 读取当前窗口下全部合法动作
- **WHEN** agent 对合法动作查询 endpoint 发起请求
- **THEN** mod 返回当前决策窗口下所有合法动作，并为每个动作附带稳定 `action_id` 与必要参数

### Requirement: 本地 bridge 必须在异常时安全退化
系统 MUST 在本地接口处理失败、状态提取失败或发生兼容性异常时返回明确错误信息，并 MUST 不干扰正常游戏流程。

#### Scenario: 状态提取过程中出现异常
- **WHEN** mod 在处理状态查询请求时遇到未预期异常
- **THEN** bridge 返回明确错误响应并记录诊断信息，同时游戏仍可继续正常运行
