## 1. Mod 工程与依赖准备

- [x] 1.1 创建 STS2 mod 工程结构，并补齐基础构建配置与输出目录约定
- [x] 1.2 配置游戏程序集、mod loader 以及本地 HTTP 所需依赖引用
- [x] 1.3 明确 mod 初始化入口、日志输出方式和版本信息注入机制

## 2. 状态提取层

- [x] 2.1 定义游戏侧 observation、legal action、compatibility metadata 的 C# 数据模型
- [x] 2.2 实现 `combat`、`reward`、`map`、`terminal` 四类窗口的状态提取逻辑
- [x] 2.3 为 `session_id`、`decision_id`、`state_version`、`action_id` 实现稳定生成与推进逻辑
- [x] 2.4 为核心窗口实现合法动作枚举与目标约束导出

## 3. 本地 Bridge 接口

- [x] 3.1 实现 loopback 范围内的 `health` 接口，返回 bridge 可用状态与版本信息
- [x] 3.2 实现 `snapshot` 接口，返回当前决策窗口的结构化状态
- [x] 3.3 实现 `actions` 接口，返回当前决策窗口下的全部合法动作
- [x] 3.4 增加异常保护、错误响应和诊断日志，确保接口失败时不影响正常游戏流程

## 4. 联调与文档

- [x] 4.1 编写本地运行与联调说明，明确 mod 安装方式、接口端口和调试步骤
- [x] 4.2 使用手工请求或简单脚本验证 `health`、`snapshot`、`actions` 三个接口的输出
- [x] 4.3 对照现有 `sts2-agent` 协议检查字段一致性，并记录需要补充的适配点
