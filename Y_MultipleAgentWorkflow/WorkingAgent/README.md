# WorkingAgent 临时租约注册表

本目录保存 Agent 运行期间的 `<AgentName>_<Hash>.txt` JSON 租约。租约文件、
`_registry.lock` 和临时文件均不进入 Git；只有本 README 被跟踪。

不要手工创建、改写或删除租约。统一使用：

```powershell
pwsh -NoProfile -File ..\Workflow\Scripts\WorkingAgent.ps1 -Action Status
```

完整协议见 `..\Workflow\Concurrency_Guide.md`。15 分钟无心跳只代表疑似
失活，不允许自动接管或清理。
