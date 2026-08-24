# Resources Load Router

文档 ID：`RES-LOAD-ROUTER`  
状态：`Active`  
维护计数：`3/5`
最后更新：`2026-08-24`

zip/文件夹入库、资源身份与新 ID、`res` 布局、图标、配置同步和导入验证，
完整读取 `SpinePet_Resources_Load_Guide.md`。

清理不是导入默认步骤；用户要求背景或特效处理时同时读取
`..\MatchClean\Router.md`。涉及应用运行、用户配置或构建时声明相应
`runtime:SpinePet`、`config:SpinePetUser`、`pipeline:BuildPublishRun`。
更新文档时短时使用 `workflow:Resources.Load`。

## nikkedb 证据入口

| 文档 | 职责 |
|---|---|
| `resources\nikkedb\README.md` | nikkedb 当前目录、索引、证据与回滚入口 |
| `resources\nikkedb\NIKKE服装ID对照表.md` | 现行服装名与上游 ID 查询表 |
| `resources\nikkedb\NIKKE资源日期索引.md` | 当前日期索引与结构化索引路径 |
| `resources\nikkedb\NIKKE资源核验报告.md` | 已冻结的历史核验报告 |

需要更深层证据时先沿这些入口给出的索引定位，不把子目录文档逐项复制进
工作流 Router。
