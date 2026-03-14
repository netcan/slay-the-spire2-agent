## 为什么

当前仓库已经把 agent 侧的协议、mock bridge、autoplay 和 trace 骨架搭起来了，但还没有任何真实的 Slay the Spire 2 游戏接入能力。要让后续 agent 或大模型真正自动打牌，第一步必须先在游戏侧实现一个 mod，把实时局面和合法动作稳定导出出来，避免整个系统长期停留在 fixture 驱动的原型阶段。

## 变更内容

- 新增 STS2 游戏侧 mod 工程，用于读取实时对局状态、识别当前决策窗口，并生成结构化 observation。
- 在 mod 中实现合法动作枚举能力，覆盖首批关键窗口：战斗回合、奖励选牌、地图选择和终局状态。
- 在 mod 中提供本地 `loopback` bridge 入口，向外部 agent 暴露只读状态查询与合法动作查询接口，为后续动作提交接口预留契约。
- 统一游戏侧导出 JSON 与现有 `sts2-agent` 仓库中的协议字段，减少后续真实 bridge 接入时的适配成本。
- 补充 mod 本地联调、版本兼容和失败保护约束，确保游戏状态读取失败时不会破坏正常游玩流程。

## Capabilities

### New Capabilities
- `mod-state-export`: 在 Slay the Spire 2 mod 中导出结构化游戏状态与决策窗口元数据。
- `mod-local-api`: 在 Slay the Spire 2 mod 中提供本地桥接接口，向外部 agent 暴露状态快照与合法动作查询能力。

### Modified Capabilities

None.

## Impact

- 增加一个面向 STS2 的游戏侧 mod 实现层，而不再只停留在 agent 侧 mock 数据。
- 为后续 `sts2-agent` 中的真实 `HttpGameBridge` 或其他 bridge adapter 提供对接目标。
- 会涉及 C# mod 工程结构、游戏程序集引用、本地接口定义、状态序列化和联调文档。
- 为后续动作执行、自动打牌和模型接入提供真实环境入口。
