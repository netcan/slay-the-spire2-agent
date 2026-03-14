## 1. 协议基础

- [x] 1.1 定义 STS2 agent 契约所需的 observation、legal action、action result、trace 等版本化数据模型
- [x] 1.2 为战斗、奖励选择、地图选择、终局等核心决策窗口补充 capability 级说明或 schema fixture
- [x] 1.3 实现 `session_id`、`decision_id`、`state_version`、`action_id` 等稳定标识字段的序列化与校验辅助逻辑

## 2. Bridge 层

- [x] 2.1 创建 bridge 接口，覆盖 session attach/start、snapshot 获取、legal action 获取、action 提交、stop/reset 控制等操作
- [x] 2.2 实现一个 mock 或 fixture 驱动的 bridge adapter，用于本地开发与测试时模拟实时决策窗口
- [x] 2.3 为 bridge 增加错误处理，覆盖 stale action、非法 payload、不支持的生命周期命令和 interrupted 状态

## 3. Autoplay 编排层

- [x] 3.1 实现 autoplay 主循环：读取当前决策窗口、调用 policy、提交单步动作，并持续推进直到终局或中断
- [x] 3.2 定义可插拔 policy 接口，并提供至少一个非 LLM 的基线 policy 用于联调与回归测试
- [x] 3.3 持久化逐决策 trace，记录 observation 元数据、合法动作、策略输出、选中动作、bridge 返回结果和中断原因
- [x] 3.4 增加 timeout、halt、manual stop 等控制逻辑，确保 bridge 或 policy 失败时 autoplay 采用 fail-closed 行为

## 4. 验证与集成

- [x] 4.1 增加测试，覆盖合法动作完整性、过期动作拒绝、autoplay 中断以及 trace 持久化等关键场景
- [x] 4.2 使用真实或原型 STS2 mod bridge 校验契约，并记录联调过程中需要修正的 schema 细节
- [x] 4.3 编写本地开发说明，明确 bridge 入口、适配方式以及首个端到端运行流程
