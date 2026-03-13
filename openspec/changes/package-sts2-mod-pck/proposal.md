## Why

当前仓库已经能生成真实 STS2 mod 所需的 DLL 与 `mod_manifest.json`，但还缺少可直接安装到游戏 `mods/` 目录的 `.pck` 打包产物。每次联调都要手工准备 pack，流程易错、重复且阻碍后续 agent 迭代。

现在补齐自动打包能力，可以把“构建 bridge”推进到“产出可安装 mod”，让本地联调、真实游戏验证和后续安装脚本都有稳定输入。

## What Changes

- 新增面向 `Sts2Mod.StateBridge` 的 `.pck` 打包流程，自动产出与 DLL 同目录的可安装 mod 产物。
- 约束打包结果至少包含 `mod_manifest.json`，并保证文件布局满足 STS2 mod loader 的发现规则。
- 增加本地验证与文档，说明如何构建、检查和安装 `.pck`。
- 为后续一键安装或发布脚本预留稳定的输出目录和命令入口。

## Capabilities

### New Capabilities
- `mod-pck-packaging`: 定义 STS2 bridge mod 的 `.pck` 构建、输出结构与最小可安装产物要求。

### Modified Capabilities

## Impact

- 影响 `mod/Sts2Mod.StateBridge/` 的构建流程与产物输出。
- 可能新增打包脚本、模板资源目录或 MSBuild 集成逻辑。
- 影响 `docs/sts2-mod-local-development.md` 中的安装与联调步骤。
- 为后续自动安装到 `F:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\` 提供基础。
