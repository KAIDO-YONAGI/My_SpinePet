# Aim / Cover 未支持原因与解决方案

## 当前结论

`Anis PaidSkin` 中的以下两套资源本身是完整的 Spine `4.1.20`
资源，但 SpinePet 当前不会导入、扫描或切换它们：

```text
aim/
  c017_01_aim_00.skel
  c017_01_aim_00.atlas
  c017_aim_01.png

cover/
  c017_01_cover_00.skel
  c017_01_cover_00.atlas
  c017_cover_01.png
```

这里的“不支持”指应用资源模型不支持，不代表 Spine 文件损坏，也不
代表 Spine 4.1 运行时一定无法解析这些文件。

## 为什么当前不支持

### 1. 可渲染类型只有 standing

`src/SpinePet/Models/CharacterResourceTypes.cs` 将 `standing` 定义为
唯一的可渲染资源类型。`icons` 只用于角色卡片，`aim` 和 `cover`
不在支持类型集合中。

### 2. 资源扫描明确排除 aim 和 cover

`CharacterResourceDiscoveryService` 只扫描：

```text
res/<角色名>/<皮肤代码>/standing/
```

代码将名为 `aim` 或 `cover` 的目录视为已退役目录。即使手动复制到
`res`，执行 **Scan** 时也不会创建对应的可用资源。

### 3. 导入入口会主动拒绝

`UnityBundleImportService` 只接受 standing 骨骼资源。选择位于
`aim` 或 `cover` 目录中的 `.skel` 文件时，导入会在复制前停止并
提示改选 standing 资源。

### 4. 角色管理只维护一个活动资源状态

当前角色配置保存一套活动的骨骼、图集、贴图和动画。角色管理器按
“角色 + 皮肤”同步 standing 资源，没有独立的
`standing / aim / cover` 状态层，也没有状态切换事件。

旧配置如果指向 `aim` 或 `cover`，会被标记为需要迁移到 standing，
并清除原动画选择。这说明它们是有意退役，而不是扫描遗漏。

### 5. 界面没有状态选择或自动切换入口

现有皮肤菜单用于选择皮肤，动画列表来自当前 standing 骨骼。界面
没有姿态状态选择器，也没有用于进入瞄准或掩体状态的外部事件接口。

## 为什么不能只把文件复制进去

直接复制会遇到以下问题：

- 扫描器忽略 `aim` 和 `cover` 目录。
- 放进 `standing` 会让同一角色和皮肤出现多套竞争资源。
- 三套骨骼拥有不同的动画列表，当前配置只能保存一套动画选择。
- 切换骨骼时需要重新加载图集、纹理、边界和动画状态。
- 文件存在不等于应用知道何时从 standing 切到 aim 或 cover。

因此，单纯复制只能归档文件，不能形成可操作的三状态角色。

## 推荐解决方案：正式增加角色状态层

这是保持当前皮肤模型并完整支持三套骨骼的方案。

### 1. 扩展资源类型

在 `CharacterResourceTypes` 中增加：

```text
standing
aim
cover
```

目录继续采用统一结构：

```text
res/
  Anis Star/
    01/
      standing/
      aim/
      cover/
      icons/
```

### 2. 扩展资源发现与导入

- 扫描每个皮肤目录下的三种可渲染状态。
- 允许导入器识别 `aim` 和 `cover`。
- 分别验证每套 `.skel`、`.atlas` 和全部图集页面。
- 保持禁止覆盖和角色目录归属检查。
- 允许某个皮肤只有 standing；aim 和 cover 应作为可选状态。

### 3. 调整角色配置模型

将“选择了哪个皮肤”和“当前处于哪个状态”分开保存，并为每个状态
保存自己的动画选择。建议至少增加：

```text
SelectedSkinCode
ActiveResourceState
ConfiguredAnimationByState
```

不应继续用一个 `ResourceType` 字段同时表达皮肤与姿态。

### 4. 调整角色管理器

资源目录应按以下键组织：

```text
角色代码 + 皮肤代码 + 资源状态
```

切换状态时需要：

1. 保留角色位置、缩放、速度和显示层级。
2. 卸载当前 Spine 资源。
3. 加载目标状态的骨骼、图集和纹理。
4. 刷新动画列表。
5. 选择该状态保存的动画，或使用明确的默认动画。
6. 目标状态缺失时回退到 standing。

### 5. 增加界面与触发方式

最小界面可以在皮肤菜单旁增加一个三段式状态选择器：

```text
Standing | Aim | Cover
```

如果状态需要由外部程序控制，还应增加明确的命令或 IPC 接口，例如：

```text
SetCharacterState(characterId, state)
```

状态触发方式必须先确定，否则应用只能提供手动切换。

### 6. 配置迁移

读取旧配置时：

- 普通角色默认设为 `standing`。
- 旧路径位于 `aim` 或 `cover` 时，识别原状态并尝试迁移。
- 找不到对应状态资源时回退到 standing。
- 保留位置、缩放、动画速度和可见性。

### 7. 测试范围

至少需要覆盖：

- 三种目录的发现与缺失文件处理。
- standing-only 皮肤的兼容性。
- 三状态导入和禁止覆盖。
- 状态切换后的资源释放与重新加载。
- 每个状态的动画记忆。
- aim/cover 缺失时回退到 standing。
- 旧配置迁移。
- 删除皮肤时三种状态一起进入回收站。

## 较小但受限的方案

可以只增加手动状态选择，把每个状态当成同一皮肤下的候选骨骼，
暂不实现外部自动切换。这个方案改动较少，但仍然必须修改资源类型、
扫描、配置、角色管理和界面，不能通过重命名或复制文件替代。

不建议把 `aim` 和 `cover` 伪装成额外皮肤。这样会造成错误的皮肤
代码、图标匹配和配置语义，后续升级为正式状态模型时还需要再次迁移。

## 当前资源的处理方式

本次只将 standing 三件套复制到 SpinePet 的活动 `res` 目录。
原始 `aim` 和 `cover` 文件继续保留在：

```text
D:\SpineTools\resources\Anis PaidSkin
```

在正式实现状态支持前，它们作为原始资源归档，不会影响当前应用扫描
和运行。
