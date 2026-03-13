# Codex Switch (WinUI)

在 Windows 上快速切换 Codex 的 `~/.codex/auth.json` 与 `~/.codex/config.toml`，支持替换到 Windows、默认 WSL，切换前自动备份，并支持一键恢复。

## 快速使用
1. 先运行一次 Codex，确保存在 `~/.codex/`（里面通常有 `auth.json` / `config.toml`）
2. 打开本工具，点击“添加”
3. 按提示新增一个“组合”
   - `OpenAI`：选择 `auth.json`（`config.toml` 会由模板生成）
   - `APIKEY`：可选择导入 `auth.json` 或直接输入 Key；`config.toml` 可选择输入 Base URL（由模板生成）或直接导入
4. 右侧选择“替换目标”
   - `Windows`：替换当前 Windows 用户目录下的 `~/.codex`
   - `WSL`：替换默认 WSL 发行版、默认用户家目录下的 `~/.codex`
5. 如需指定其它 WSL 发行版或用户，点击工具栏里的“设置”
6. 选中组合 → 点击“切换到此组合”
7. 需要回滚时 → 点击“恢复备份”

## 可选功能
- 会话迁移：切换时自动修改最近 N 天 sessions 中的首行 `model_provider`
- WSL 识别：启用 `WSL` 后自动识别默认发行版、默认用户与家目录
- 设置：可手动指定 WSL 发行版和用户名；留空时继续自动识别
- 本地缓存：默认 WSL 信息会缓存到本地，窗口打开后再后台刷新
- 模板：可编辑两套 `config.toml` 模板（OpenAI / APIKEY），支持重置默认；APIKEY 模板支持变量 `{base_url}`

## 文件位置
- Codex 配置：`%USERPROFILE%\.codex\`
- WSL Codex 配置：`/home/<用户名>/.codex/`（程序内部通过 `\\wsl$` 访问默认发行版）
- 备份目录：`%USERPROFILE%\codex-switch-backups\`
- 本工具数据：`%LOCALAPPDATA%\codex-switch\`（`profiles.json` / `profiles\...` / `templates\...`）
