# 第三方组件与授权清单 / Third-Party Notices

本文件列出 SpinePet 使用的全部第三方组件、各自的权利人、许可证，以及**是否随发行包分发**。
所有许可证原文存放在 `licenses/` 目录（发行包的根目录下同样带一份）。

> 说明：本项目自身代码按 GPL-3.0-or-later 授权（见 `LICENSE` / `NOTICE`）。
> 下表组件**不在**该授权范围内，各自适用其原始许可证，本项目不对它们做再授权。

## 组件清单

| 组件 | 版本 | 权利人 | 许可证 | 是否随发行包分发 |
| --- | --- | --- | --- | --- |
| Spine Runtimes（spine-csharp） | 4.1 | Esoteric Software LLC | Spine Runtimes License Agreement | **是**（编译进 `SpineRuntime41.dll`） |
| Vortice.Windows（`Vortice.D3DCompiler` / `Vortice.Direct3D11` / `Vortice.DirectComposition` / `Vortice.DXGI`） | 3.8.3 | Amer Koleci and contributors | MIT | **是**（随 app 目录分发的 DLL） |
| .NET 9 Runtime（`win-x64` 自包含发布） | 9.x | .NET Foundation and Contributors | MIT | **是**（自包含发布内含运行时） |
| UnityPy | 1.24.1 | K0lb3 | MIT | 否（由使用者自行 `pip install`） |
| Pillow | 12.0.0 | Secret Labs AB / Fredrik Lundh / Jeffrey A. Clark and contributors | MIT-CMU (HPND) | 否（由使用者自行 `pip install`） |
| xunit | 2.9.3 | xUnit.net contributors | Apache-2.0 | 否（仅测试工程） |
| xunit.runner.visualstudio | 3.1.5 | xUnit.net contributors | Apache-2.0 | 否（仅测试工程） |
| Microsoft.NET.Test.Sdk | 17.14.1 | Microsoft Corporation | MIT | 否（仅测试工程） |
| SpineSkeletonDataConverter | 本地内置 | wang606（https://github.com/wang606/SpineSkeletonDataConverter） | **PolyForm Noncommercial License 1.0.0** | 否（仅 `tools/` 下的本地工具） |

许可证原文位置：

```text
licenses/GPL-3.0.txt                        本项目自身代码（GNU GPL v3）
licenses/spine-runtimes-license.txt          Spine Runtimes License Agreement
licenses/vortice-windows-MIT.txt             MIT
licenses/dotnet-runtime-MIT.txt              MIT
licenses/unitypy-MIT.txt                     MIT
licenses/pillow-MIT-CMU.txt                  MIT-CMU (HPND)
licenses/polyform-noncommercial-1.0.0.txt    PolyForm Noncommercial 1.0.0
```

xunit / Microsoft.NET.Test.Sdk 仅用于 `tests/`，不进入任何发行产物，其许可证以各自的
NuGet 包与上游仓库为准。

## 需要特别注意的两个组件

### 1. Spine Runtimes License（非开源许可证）

`SpinePet/third_party/spine-csharp-4.1/LICENSE` 原文的关键条款：

- 允许把 Spine Runtimes 集成进软件、或基于它创建衍生作品；
- **任何形式的再分发都必须附带该许可证与版权声明**（本项目已在 `licenses/` 与发行包内附上）；
- **每一个使用者都必须自行取得自己的 Spine Editor 授权**；
- 软件按 “AS IS” 提供，Esoteric Software LLC 不承担任何责任。

也就是说：**本项目开源，并不等于你可以免费使用 Spine 运行时去构建并发布你自己的应用。**
是否需要在 Esoteric Software 购买 Spine Editor 授权，请自行向其确认。

本项目对该组件的处理方式：把它当作**独立的第三方组件**——源码单独放在
`SpinePet/third_party/spine-csharp-4.1/`，编译为独立程序集 `SpineRuntime41.dll`
（`SpinePet/src/SpineRuntime41/SpineRuntime41.csproj` 只是把该目录的源码收集进独立程序集）。
本项目的 GPL-3.0-or-later 授权**不覆盖**该目录及其编译产物。

### 2. PolyForm Noncommercial License 1.0.0（禁止商业用途）

`tools/SpineSkeletonDataConverter/` 内置的上游工具使用 PolyForm Noncommercial 1.0.0，
该许可证只允许**非商业用途**（个人研究、实验、测试、私人娱乐、爱好者项目，
以及非营利组织、教育机构等）。该组件不随发行包分发，但它位于本仓库中，
因此**本仓库整体的商业使用是不被许可的**（至少就含该组件的部分而言）。
再分发该目录时，必须一并传递该许可证条款或它的 URL。

## 如果你要再分发本项目的构建产物

1. 保留发行包根目录下的 `LICENSE`、`NOTICE`、`THIRD_PARTY_NOTICES.md`、
   `ASSETS.md` 与整个 `licenses/` 目录，不要删除或改写其中的第三方许可证原文。
2. 不要把第三方组件（尤其 Spine 运行时）单独抽出当成独立产品分发，也不要给它换许可证。
3. 不要往包里添加任何游戏角色素材；素材不属于本项目，本项目无权为你授权（见 `ASSETS.md`）。
4. 若你修改并分发本项目的二进制，GPL-3.0 要求你提供对应的完整源码：
   源码地址 https://gitee.com/KAIDOYONAGI/my_-spine-pet

---

本文件是授权事实的汇总，**不是法律意见**。涉及具体分发、商用或授权判断时，
请咨询你自己的法律顾问，并与各上游权利人确认。
