# WorkingAgent 临时租约注册表

本目录保存 Agent 运行期间的 `<AgentName>_<Hash>.txt` JSON 租约。租约文件、
`_registry.lock` 和临时文件均不进入 Git；只有本 README 被跟踪。

不要手工创建、改写或删除租约。统一使用：

```powershell
pwsh -NoProfile -File ..\Workflow\Scripts\WorkingAgent.ps1 -Action Status
```

完整协议见 `..\Workflow\Concurrency_Guide.md`。15 分钟无心跳只代表疑似
失活，不允许自动接管或清理。

Codex 任务可以使用原生任务消息互相通知重叠租约或已释放范围，但消息不替代
注册表。修改前仍须核验租约和实际工作区，完成时先释放租约，再通知等待任务。
