# SpineTools 项目验证指南

文档 ID：`WF-PROJECT-VALIDATION`  
状态：`Active`  
最后更新：`2026-08-21`  
最后核验：`2026-08-21`

## 适用边界

本文件只记录 SpineTools 已确认的构建、发布和运行验证方式，不属于多 Agent
工作流的通用强制规则。初始化其他项目时必须先询问用户是否需要项目验证
指南；未确认时不得从本文件推断命令或进程操作。

SpineTools 当前选择：完成代码相关、非纯文档改动后执行本指南。纯文档维护
不触发该流程。

## 并发屏障

执行前先确认没有其他活动写租约，并在当前租约中取得
`pipeline:BuildPublishRun` 独占资源。普通并发覆盖不包含该屏障授权。

## 验证步骤

1. 如果存在 `SpinePet.exe`，终止所有现有实例。
2. 严格依次运行：
   - `D:\SpineTools\tools\Build.bat`
   - `D:\SpineTools\tools\Publish.bat`
   - `D:\SpineTools\tools\Run.bat`
3. 确认新的 `SpinePet.exe` 已启动并持续运行。

## 环境回退

若调用 Agent 的进程环境缺少 `WINDIR`，只允许在启动 Run 子进程前设置：

```powershell
$env:WINDIR = $env:SystemRoot
```

该回退不写入项目代码，不修改系统配置，也不扩展为其他项目的默认规则。
