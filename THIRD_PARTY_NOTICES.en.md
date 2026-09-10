# Third-Party Notices

[中文](THIRD_PARTY_NOTICES.md) | **English**

This file lists every third-party component used by SpinePet, its rights holder,
its license, and **whether it is distributed with release packages**. Verbatim
license texts live in `licenses/` (release packages ship a copy of that folder too).

> The application's own code is licensed GPL-3.0-or-later (see `LICENSE` and
> `NOTICE`). The components below are **not** covered by that grant; each keeps its
> own license, and this project does not relicense them.

## Component inventory

| Component | Version | Rights holder | License | Distributed with releases? |
| --- | --- | --- | --- | --- |
| Spine Runtimes (spine-csharp) | 4.1 | Esoteric Software LLC | Spine Runtimes License Agreement | **Yes** (compiled into `SpineRuntime41.dll`) |
| Vortice.Windows (`Vortice.D3DCompiler` / `Vortice.Direct3D11` / `Vortice.DirectComposition` / `Vortice.DXGI`) | 3.8.3 | Amer Koleci and contributors | MIT | **Yes** (DLLs inside `app\`) |
| .NET 9 Runtime (`win-x64` self-contained publish) | 9.x | .NET Foundation and Contributors | MIT | **Yes** (self-contained publish embeds the runtime) |
| UnityPy | 1.24.1 | K0lb3 | MIT | No (installed by the user with `pip`) |
| Pillow | 12.0.0 | Secret Labs AB / Fredrik Lundh / Jeffrey A. Clark and contributors | MIT-CMU (HPND) | No (installed by the user with `pip`) |

| xunit | 2.9.3 | xUnit.net contributors | Apache-2.0 | No (test project only) |
| xunit.runner.visualstudio | 3.1.5 | xUnit.net contributors | Apache-2.0 | No (test project only) |
| Microsoft.NET.Test.Sdk | 17.14.1 | Microsoft Corporation | MIT | No (test project only) |


License texts:

```text
licenses/GPL-3.0.txt                         this project's own code (GNU GPL v3)
licenses/spine-runtimes-license.txt          Spine Runtimes License Agreement
licenses/vortice-windows-MIT.txt             MIT
licenses/dotnet-runtime-MIT.txt              MIT
licenses/unitypy-MIT.txt                     MIT
licenses/pillow-MIT-CMU.txt                  MIT-CMU (HPND)
```

For components that are never redistributed (xunit, Microsoft.NET.Test.Sdk,
UnityPy, Pillow), the license shown is taken from the respective package metadata
or upstream repository; obtain the authoritative text from those projects.
UnityPy and Pillow are installed by the user with `pip` and are not bundled.

## Two components that need particular attention

### 1. Spine Runtimes License (not an open-source license)

The key clauses of `SpinePet/third_party/spine-csharp-4.1/LICENSE`:

- integrating the Spine Runtimes into software, or creating derivative works of
  them, is permitted;
- **redistribution of the products in any form must include this license and the
  copyright notice** (this project ships both in `licenses/` and inside release
  packages);
- **each user of the products must obtain their own Spine Editor license**;
- the software is provided "AS IS", without warranty, and Esoteric Software LLC
  accepts no liability.

In other words: **this project being open source does not mean you may freely use
the Spine runtime to build and release your own application.** Whether you need to
buy a Spine Editor license from Esoteric Software is a question for them.

How this project handles it: the runtime is treated as a **separate third-party
component** — its sources stay in `SpinePet/third_party/spine-csharp-4.1/` and are
compiled into the separate assembly `SpineRuntime41.dll`
(`SpinePet/src/SpineRuntime41/SpineRuntime41.csproj` merely collects those sources
into an isolated assembly). This project's GPL-3.0-or-later grant does **not** cover
that directory or its build output.

### 2. Development-time external tool (not in this repository)

Earlier revisions of this repository bundled
[SpineSkeletonDataConverter](https://github.com/wang606/SpineSkeletonDataConverter)
(a C++ CLI tool, **PolyForm Noncommercial License 1.0.0**) together with Spine's
official sample projects under its `data/` directory. That content has been removed
from the repository and from its history.

It was a development-time aid only: it is not part of the application, is not built
by the solution, and is not included in any release package. If you obtain and use
it yourself, its noncommercial terms apply to you, and the license text must be
passed on to anyone who receives it from you. Because it is no longer part of this
repository, its license text is no longer shipped in `licenses/`.

## If you redistribute a build of this project

1. Keep `LICENSE`, `NOTICE`, `THIRD_PARTY_NOTICES.md`, `ASSETS.md` and the entire
   `licenses/` directory at the root of the package, and do not remove or rewrite
   the verbatim third-party licenses inside it.
2. Do not extract a third-party component (especially the Spine runtime) and
   distribute it as a standalone product, and do not relicense it.
3. Do not add any game character assets to the package; such assets are not owned
   by this project and this project cannot license them to you (see
   [ASSETS.en.md](ASSETS.en.md)).
4. If you modify and redistribute the binaries, GPL-3.0 requires you to provide the
   complete corresponding source: https://gitee.com/KAIDOYONAGI/my_-spine-pet

---

This file summarises licensing facts and is **not legal advice**. For decisions
about redistribution, commercial use or licensing, consult your own legal advisor
and confirm with the upstream rights holders.
