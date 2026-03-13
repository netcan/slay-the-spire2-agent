## ADDED Requirements

### Requirement: 构建流程必须产出可安装的 STS2 mod `.pck`
系统 MUST 为 `Sts2Mod.StateBridge` 提供自动化 `.pck` 打包流程，并在一次标准构建后产出可直接复制到 STS2 `mods/` 目录的 pack 文件。

#### Scenario: 执行标准打包命令后生成 `.pck`
- **WHEN** 开发者执行仓库约定的 mod 打包命令
- **THEN** 系统 MUST 生成 `Sts2Mod.StateBridge.pck`
- **THEN** 输出目录 MUST 同时包含与其配套的 `Sts2Mod.StateBridge.dll`

### Requirement: `.pck` 内容与目录布局必须满足 STS2 mod loader 约束
系统 MUST 确保 `.pck` 中包含 `res://mod_manifest.json`，并保证最终输出目录布局与 STS2 mod loader 的发现规则兼容，不依赖手工补文件才能被加载。

#### Scenario: 检查打包产物结构
- **WHEN** 开发者检查生成后的 mod 输出目录
- **THEN** 目录中 MUST 存在 `Sts2Mod.StateBridge.pck` 与 `Sts2Mod.StateBridge.dll`
- **THEN** `.pck` 内 MUST 包含 `mod_manifest.json`

### Requirement: 打包流程必须提供最小可验证反馈
系统 MUST 提供最小验证方式，用于确认 `.pck` 已生成、关键文件存在且输出路径明确，以便本地联调和后续安装脚本复用。

#### Scenario: 打包完成后输出验证结果
- **WHEN** 打包流程成功结束
- **THEN** 系统 MUST 输出生成文件路径或等效的结构化结果
- **THEN** 文档或脚本 MUST 说明如何验证产物可用于安装到游戏目录
