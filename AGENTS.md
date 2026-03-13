# Repository Guidelines

## Project Structure & Module Organization

- `src/sts2_agent/`: Python package for the agent prototype, including bridge abstractions, fixtures, policies, tracing, and the autoplay orchestrator.
- `tests/`: Python unit tests (`test_bridge.py`, `test_orchestrator.py`).
- `mod/Sts2Mod.StateBridge/`: C# bridge library for STS2 state export and in-game control.
- `mod/Sts2Mod.StateBridge.Host/`: local host used for fixture and runtime-host validation.
- `tools/validate_mod_bridge.py`: end-to-end bridge smoke test.
- `openspec/`: change proposals, archived changes, and synced specs. Keep proposal/spec/task updates here with implementation work.
- `docs/`: local development and bridge integration notes.

## Build, Test, and Development Commands

- `dotnet build mod/Sts2Mod.StateBridge.sln`: build the C# bridge and host with the pinned .NET 9 SDK.
- `dotnet build mod/Sts2Mod.StateBridge.sln -p:Sts2ManagedDir="...data_sts2_windows_x86_64" -p:Sts2ModLoaderDir="...data_sts2_windows_x86_64"`: build against a real STS2 install.
- `$env:PYTHONPATH='src'; python -m unittest discover -s tests -v`: run Python tests.
- `python tools/validate_mod_bridge.py`: validate `health`, `snapshot`, `actions`, and `POST /apply` in fixture mode.
- `openspec validate --changes --json` / `openspec validate --specs --json`: validate OpenSpec artifacts before archiving.

## Coding Style & Naming Conventions

- Use UTF-8 without BOM for docs and OpenSpec files.
- Python: 4-space indentation, `snake_case` for functions/modules, `PascalCase` for dataclasses and classes.
- C#: 4-space indentation, `PascalCase` for types/methods, `camelCase` for locals/fields, one public type per file when practical.
- Keep comments sparse and only for non-obvious logic.

## Testing Guidelines

- Add or update unit tests for Python behavior changes in `tests/`.
- For bridge protocol or runtime changes, run both Python tests and `tools/validate_mod_bridge.py`.
- Prefer test names like `test_<behavior>` and keep fixtures deterministic.

## Commit & Pull Request Guidelines

- Follow the existing history: imperative, scoped subjects such as `feat: add sts2 in-game control bridge` or `chore: normalize document encoding`.
- Keep commits focused; separate feature work, spec sync, and archive steps when possible.
- PRs should summarize user-visible changes, list validation commands run, and mention any STS2 runtime path or mod packaging assumptions.

## Security & Configuration Tips

- Do not commit local machine paths, secrets, or write-enabled runtime settings.
- Real write control is gated by `STS2_BRIDGE_ENABLE_WRITES`; leave it disabled unless actively testing in-game actions.
