## 1. 收敛公共协议

- [ ] 1.1 更新 C# contracts、window extractors 与 JSON 导出，移除 cards、powers、preview 等对象上的 `description_quality`、`description_source`、`description_vars` 公共字段。
- [ ] 1.2 更新 Python `models.py`、HTTP/mock bridge 与 LLM payload，总结逻辑只消费 canonical `description`，去掉对旧 diagnostics 的依赖。

## 2. 补齐日志化诊断

- [ ] 2.1 在 `Sts2RuntimeReflectionReader` 或相关 runtime provider 中补充说明解析日志，覆盖 template fallback、变量未解析、glossary 规范化失败与异常路径。
- [ ] 2.2 约束默认日志只记录失败/降级路径，并提供显式 debug 开关或等效机制用于逐条成功解析排障。

## 3. 更新验证与联调工具

- [ ] 3.1 调整 fixtures、unit tests 与 bridge validation，改为断言精简后的公共 schema，不再检查说明 diagnostics 字段。
- [ ] 3.2 更新 live validation / debug artifacts，记录对应日志文件位置或提取日志摘要，确保移除公共 diagnostics 后仍可定位解析问题。

## 4. 完成回归验证

- [ ] 4.1 运行 `dotnet test mod/Sts2Mod.StateBridge.Tests/Sts2Mod.StateBridge.Tests.csproj`、`$env:PYTHONPATH='src'; python -m unittest discover -s tests -v` 与 `python tools/validate_mod_bridge.py`。
- [ ] 4.2 完成至少一次 live runtime 验证，确认客户端响应不再暴露说明 diagnostics，同时日志中能够定位 description fallback 或 unresolved 问题。
