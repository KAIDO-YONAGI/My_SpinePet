# Resources Load Developer Log

## 2026-08-24：导入规范收敛为指定清单

- 全量核对 zip 入库、应用 Add、Battle catalog、头像下载、运行时路径和
  附属文档，确认耗时主因是多入口各自扫描、头像后补及已移除 DB 入口残留。
- Battle catalog 与头像工具均改为必须提供精确选择；头像工具每次只接受
  一个 ResourceId 和一个目标 Skin，不再存在省略参数后扫描整个 `res`。
- `AppPaths` 不再把 `.git` 祖先当项目根，也不再从源码树寻找运行工具；
  运行时只使用验证过的 `SpinePet` 项目 `res` 或发布目录的 `res/Tools`。
- 权威流程固定为“指定清单 → 入库 → 本体与头像预检 → 导入登记 → 验证”；
  nikkedb 仅供离线自动化按指定项取证，应用运行时不得访问。
- 项目外路径扫描复核无残留；全量测试 `339/339`，Release Build、Publish、
  Run 均通过，启动后的 `SpinePet.exe` 持续运行。
- 本次实际影响资源导入，维护计数：`4/5 -> 5/5`。已对照当前实现复核
  Load Guide、Favorite Guide、工具 README 和 StateSupport 交叉引用，
  修正全量扫描及 DB 残留后计数归零：`5/5 -> 0/5`。

## 2026-08-24：Favorite 互动资源导入

- 将 13 套 `favorite_cNNN_00` 资源移动到 `resources\Characters`，并按
  `<Name> Favorite\00\standing` 写入运行时资源；不生成 Battle、
  Aim 或 Cover。
- 原编号用于 nikkedb 姓名与图标匹配，本地编号统一为在三位原编号前加
  `9` 的独立 `9xxx` 编号，atlas、纹理、图标和名称表同步规范化。
- 显示名以 ` Favorite` 结尾时，用户显式动画配置仍优先，否则默认常驻
  精确 `idle_merged`；点击保留常规名称优先级，单交互资源落到
  `expression_merged`。普通角色继续使用原有 `idle` 规则。
- 新增 `Favorite_Interactive_Import_Guide.md`，固化适用边界、编号、
  图标、目录、动画与验证规则。
- 本次实际影响资源导入与点击交互，维护计数：`3/5 -> 4/5`。

## 2026-08-23：nikkedb 编号与三状态导入

- 历史实现曾新增 DB 入口，按 `rename-map.json` 精确匹配资源编号并从本地
  `resources\nikkedb\l2d` 导入；该入口已于 2026-08-24 移除，不是现行能力。
- 统一创建 `standing/aim/cover/icons` 布局；standing 必须完整，
  Aim/Cover 只有双方完整时才成对复制，单边缺失时全部跳过。
- 延续事务冲突不覆盖、失败回滚和多页 atlas 验证规则；普通 Add 继续只导入
  standing。
- 新增 `battle-catalog-importer`，完整重查 `resources\Characters` 的 67 个
  顶层目录：44 套文件表面完整，43 套通过 Spine 4.1 兼容性和实际解析，
  已全部导入；`Dolla Dark Rose` 的 Aim/Cover 为 Spine `4.0.47`，按规则跳过。
- 实际 `SpinePet\res` 已归一为 63 套 standing、43 套 aim、43 套 cover，
  用户配置同步为 63 个角色、43 个 Battle。重复导入结果为 43 套
  AlreadyPresent，确认幂等。
- 真实 `c017_01` 和未知编号、单边缺失、多页贴图、冲突测试通过；全量测试
  `240/240`，Release Build、Publish、Run 验证通过。
- 本次实际影响资源导入，维护计数：`2/5 -> 3/5`。

## 2026-08-22：导入事务与并发幂等

- 将 Skeleton 和 UnityFS bundle 导入整理为发现与验证、冲突预检、提交、
  回滚清理四个阶段，并按规范化目标目录串行化并发导入。
- 目标已存在时保持原有报错语义；即使内容相同或再次选择目标中的源文件，
  也不覆盖、不视为成功。
- 失败或取消时保留全部既有目标，删除本次创建的文件、空目录和
  `.SpinePet-Import-*` 临时目录。
- 资源同步连续执行两次时，第二次不保存、不通知，也不移除渲染资源。
- 验证证据：全量测试 `210/210`，Release Build 0 警告、0 错误，
  Publish 成功；Run 按验证指南补齐子进程 `WINDIR` 后出现
  `startup-complete`，新实例持续运行。
- 本次实际影响资源导入与配置同步，维护计数：`0/5 -> 1/5`。

## 2026-08-22：维护周期提前复核并清零

- 按用户要求提前复核当前 `2/5` 周期，逐项对照导入工具路径推导、
  `CharacterNames.json` 检查、资源扫描和默认动画初始化代码。
- 确认 zip 入库工具从脚本位置推导项目根，角色首次加载和重置按
  `idle -> idle* -> 第一动画` 选择默认常驻动画。
- 修正验证步骤中的旧表述：点击动画结束后恢复播放前状态，而不是固定回到
  idle；同步更新指南最后核验日期。
- 复核结论：导入规范与当前实现一致，维护计数：`2/5 -> 0/5`。

## 2026-08-22：全角色重新登记 idle 默认动画

- 用 SkeletonInspector 解析 `res` 下 45 套骨骼，45/45 均存在精确 `idle`。
- 用户配置保留位置、缩放和可见性，45 个角色的默认动画重新登记为 `idle`。
- 验证证据：Build/Publish/Run 通过，全量测试 `191/191`，运行后配置仍为
  `45/45 idle`。
- 本次实际影响资源导入默认状态，维护计数：`1/5 -> 2/5`。

## 2026-08-22：导入工具路径去硬编码

- `Import-ResourceZip.ps1` 默认从脚本位置推导项目根、资源目录和角色名称表。
- 指南与 nikkedb 入口统一使用项目相对路径，项目迁移后无需人工改盘符。
- 本次实际影响资源导入工具，维护计数：`0/5 -> 1/5`。

## 2026-08-21：导入指南迁移

- 将旧中文文件名迁为 `SpinePet_Resources_Load_Guide.md`。
- 具体硬规则从 Skill 回收到权威指南，Skill 只保留触发和导航。
- 纯文档迁移，不增加维护计数。
