## 1. 打包入口与资源准备

- [x] 1.1 确认可用的 `.pck` 打包工具链与命令入口，约定仓库内的调用方式
- [x] 1.2 准备最小打包输入资源，确保 `mod_manifest.json` 能以 `res://mod_manifest.json` 进入 `.pck`
- [x] 1.3 约定统一输出目录，保证 `.pck`、DLL 与 manifest 的邻接关系稳定

## 2. 构建流程集成

- [x] 2.1 将 `.pck` 打包接入 `Sts2Mod.StateBridge` 构建流程或仓库脚本
- [x] 2.2 让标准构建后自动产出 `Sts2Mod.StateBridge.pck` 与配套 DLL
- [x] 2.3 为缺少打包依赖或命令失败的情况补齐明确诊断信息

## 3. 验证与文档

- [x] 3.1 增加最小验证脚本或检查步骤，确认 `.pck`、DLL 与关键资源都已生成
- [x] 3.2 更新 `docs/sts2-mod-local-development.md`，说明如何构建、验证和安装 `.pck`
- [x] 3.3 记录后续自动安装脚本可复用的输出路径与约束
