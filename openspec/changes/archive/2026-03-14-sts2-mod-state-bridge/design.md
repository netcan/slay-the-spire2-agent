## 背景

当前仓库已经有了 agent 侧协议模型与原型 orchestrator，但真实游戏输入仍然依赖 fixture。要真正把 Slay the Spire 2 接到 agent 上，必须先补上游戏侧 mod：它既要读到游戏内部对象，又要把这些对象稳定转换成 agent 能消费的结构化数据。

这个 change 的重点不是自动出牌本身，而是先打通“从 STS2 运行时提取状态并通过本地桥接暴露”的链路。设计上要兼顾两个现实约束：一是 STS2 仍可能在 EA 期间频繁更新，mod 不能把外部契约绑死在内部对象结构上；二是 mod 应尽量不影响人工游玩，读取或接口失败时需要安全退化。

## Goals / Non-Goals

**Goals:**
- 建立一个可编译、可加载的 STS2 mod 工程，用于读取实时对局状态。
- 在 mod 中识别统一的决策窗口，并输出结构化 observation、合法动作和元数据。
- 提供本地 loopback bridge 入口，供外部 agent 查询当前状态与合法动作。
- 让导出的字段结构尽量贴合现有 `sts2-agent` 的 `DecisionSnapshot` / `LegalAction` 契约。
- 在 mod 读取失败、接口异常或版本不兼容时保持 fail-safe，不破坏正常游戏流程。

**Non-Goals:**
- 本次变更不实现外部 agent 向游戏提交动作的完整执行链路。
- 本次变更不覆盖所有事件、商店、遗物交互和特殊 UI 分支，只先覆盖核心决策窗口。
- 本次变更不构建远程网络服务，bridge 只面向本机 loopback 使用。
- 本次变更不在第一版追求无头训练或批量并发环境。

## Decisions

### 1. 先把 mod 侧拆成“状态提取层 + 本地接口层”
mod 内部应分成两个主要模块：一个负责从游戏对象提取和归一化状态，另一个负责把这些结果通过本地接口暴露给外部进程。

这样做可以把游戏内部适配与通信协议解耦。后续如果游戏版本更新导致内部对象变化，只需调整提取层；外部 API 尽量保持稳定。

备选方案：
- 在接口处理代码里直接访问游戏对象：放弃，因为会让逻辑耦合过深、难测试。
- 直接把游戏对象序列化输出：放弃，因为结构不稳定、外部语言无法可靠消费。

### 2. 首版 bridge 只开放只读 loopback HTTP/JSON 接口
第一版优先提供本机可访问的只读 HTTP/JSON 接口，例如 `GET /health`、`GET /snapshot`、`GET /actions`。这样最利于和现有 Python agent 联调，也便于手工调试与抓包检查。

备选方案：
- Named pipe：后续可作为更贴近本机 IPC 的增强方案，但首版调试门槛更高。
- WebSocket：当前场景不需要持续推送，首版 request/response 更简单。

### 3. observation 以决策窗口统一建模
mod 需要把不同游戏界面统一映射到相同的决策窗口模型，至少包括 `combat`、`reward`、`map`、`terminal`。每个窗口都必须生成 `session_id`、`decision_id`、`state_version`、`phase` 等元数据。

这能保证 agent 侧不必为每一种游戏界面写完全不同的数据读取分支，并为后续动作提交、防 stale action 打下基础。

备选方案：
- 每个界面各自输出一套 JSON：放弃，因为后续 agent 适配成本会快速膨胀。

### 4. 合法动作枚举由 mod 作为真值源
即使本次变更暂不做动作执行，mod 也应同步枚举当前窗口下的合法动作集合，并为每个动作分配稳定 `action_id`。后续 agent 侧必须以这里输出的合法动作作为真值源。

这样可以避免后续外部 agent 自己“猜”哪些动作可点，减少 UI/逻辑不一致问题。

备选方案：
- 只输出 observation，不输出 action：放弃，因为后续还得重构 observation pipeline，而且无法尽早验证动作建模是否合理。

### 5. 版本与兼容性信息必须显式暴露
bridge 响应中应显式包含 mod 版本、协议版本、游戏版本或兼容性元数据。这样在 STS2 更新后，外部 agent 能快速判断当前连接是否仍可用。

备选方案：
- 不输出版本字段：放弃，因为联调和故障排查成本过高。

## Risks / Trade-offs

- [EA 更新导致游戏内部类型变化] -> 将提取逻辑集中封装，并在接口中暴露兼容性信息，快速发现问题。
- [mod 线程与游戏主线程交互不安全] -> 读取状态时避免在错误线程做有副作用操作，仅做必要快照拷贝。
- [本地 HTTP 服务影响游戏稳定性] -> 限制为 loopback，只做轻量只读查询，并为异常请求加保护。
- [状态导出字段过少导致 agent 不够用] -> 先对齐现有 `sts2-agent` 协议，后续再逐步增补 metadata。
- [合法动作枚举实现复杂] -> 第一阶段只覆盖核心窗口，先证明建模成立，再扩展边角分支。

## Migration Plan

1. 在仓库中新增 STS2 mod 工程和基础项目结构。
2. 补齐对游戏程序集、mod loader 和本地接口运行所需依赖配置。
3. 实现状态提取层，先覆盖 `combat`、`reward`、`map`、`terminal` 四类窗口。
4. 实现 loopback HTTP/JSON 接口，并导出 `health`、`snapshot`、`actions`。
5. 用手工请求和后续 `sts2-agent` 对接验证字段一致性与兼容性信息。

回滚策略：关闭或移除该 mod，不影响外部 agent 原型；若接口异常，mod 默认不拦截游戏流程。

## Open Questions

- STS2 当前推荐的 mod loader 与项目模板应该采用哪套最稳妥的方案？
- 首版 `snapshot` 与 `actions` 是否要拆成两个 endpoint，还是提供一个聚合 endpoint 以减少轮询次数？
- 是否需要在第一版就加入 session token，还是先依赖 loopback 范围控制？
