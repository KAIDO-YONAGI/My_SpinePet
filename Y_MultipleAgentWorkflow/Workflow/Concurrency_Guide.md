# WorkingAgent 并发租约指南

## 1. 取得租约的时点

Agent 可先进行轻量只读探索以确定任务类型。在详细分析、创建子 Agent、
首次修改文件或启动会改变状态的工具前，必须取得租约。

租约文件为 `<AgentName>_<Hash>.txt`。Hash 使用 Agent 名、模型、会话 ID、
UTC 启动时间和随机 nonce 的 SHA-256 前 12 位。文件是 schemaVersion 1
的 JSON，登记任务类别、访问模式、资源范围、心跳和覆盖记录。

## 2. 基本命令

```powershell
$script = Join-Path (Get-Location) 'Y_MultipleAgentWorkflow\Workflow\Scripts\WorkingAgent.ps1'

pwsh -NoProfile -File $script -Action Status

pwsh -NoProfile -File $script -Action Acquire `
  -AgentName Codex -Model GPT-5 -SessionId '<thread-id>' `
  -TaskType GUI -Categories GUI -Summary '调整角色卡片布局' `
  -AccessMode write -Resources 'path:SpinePet/src/SpinePet/Views'

pwsh -NoProfile -File $script -Action Heartbeat `
  -LeaseId '<lease-id>' -SessionId '<thread-id>'

pwsh -NoProfile -File $script -Action UpdateScope `
  -LeaseId '<lease-id>' -SessionId '<thread-id>' `
  -Resources 'path:SpinePet/src/SpinePet/Views','workflow:GUI'

pwsh -NoProfile -File $script -Action Release `
  -LeaseId '<lease-id>' -SessionId '<thread-id>'
```

所有输出均为 JSON。退出码：`0` 成功，`2` 活动冲突，`3` 疑似失活冲突，
`4` 参数/所有权/注册表错误，`5` 等待超时。

## 3. 冲突规则

- `read + read` 可并行。
- `path:` 资源相交时，任一方为 `write` 即冲突；父子目录视为相交。
- `exclusive` 与相同资源上的所有访问冲突。
- 非路径共享资源按不区分大小写的完整名称匹配。
- 写任务未提供资源范围时，同一业务根默认冲突。
- 扩大范围必须先 `UpdateScope`，检测成功后才能修改。
- `pipeline:BuildPublishRun` 是全局屏障：与其他活动写/独占租约冲突。
- Router/Log 写入短时使用 `workflow:<业务根>`；根结构使用 `workflow:root`。

## 4. 心跳与疑似失活

活动或等待中的 Agent 每 5 分钟、长操作前后和扩大范围时更新心跳。超过
15 分钟无心跳仅标记 `suspectedStale`，绝不自动删除。非冲突的疑似失活
记录只提示；冲突记录必须向用户报告 Agent、任务、资源、最后心跳和风险。

确认旧 Agent 已停止后，可执行：

```powershell
pwsh -NoProfile -File $script -Action Release `
  -LeaseId '<old-lease-id>' -ConfirmStaleRemoval
```

该操作只允许删除已超过 15 分钟的租约。

## 5. 等待与覆盖

等待会写入 `waiting` 标记并按间隔重新原子检测：

```powershell
pwsh -NoProfile -File $script -Action Wait <Acquire 参数> `
  -PollSeconds 60 -WaitTimeoutMinutes 30
```

冲突解除后同一次注册表锁内切换为 `active`。超时返回退出码 5，但保留
waiting 标记；停止当前任务时用正常 Release 清理。

用户明确接受当前声明范围的并发后，可在 Acquire 或 UpdateScope 使用
`-OverrideConflict`。租约状态变为 `override`，记录冲突 ID 与确认时间。
普通覆盖不自动允许 Build/Publish/Run；构建屏障冲突必须单独再次确认。

## 6. 子 Agent 与释放

只读子 Agent 无法登记时，父 Agent 代为 Acquire，设置 `parentLeaseId`，
并使用独立 sessionId。Lease ID 是唯一租约标识；Heartbeat、UpdateScope 和
普通 Release 还必须匹配原 sessionId。任务完成、失败或取消均应在 finally
路径释放。崩溃遗留由后续 Status 标记，不自动接管。
