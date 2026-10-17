# 项目工作指引

## 先按任务阅读

开始工作先读本文件，再读下表相关文档；跨模块任务合并阅读范围。不再默认遍历所有文档、历史日志或模型文件。首次了解项目先读概念设计；实现前检查对应代码与资产。

| 任务 | 阅读入口与顺序 |
| --- | --- |
| 产品方向、世界观、卡池、MVP 范围 | [概念设计](Docs/DESIGN_DOC_v0_1.md) |
| 卡牌/敌人/Boss/道具/事件机制，视觉与信息边界 | [机制与素材设计](Docs/ASSET_MECHANICS_DESIGN.md) → [框架指南](Docs/FRAMEWORK.md) 中相关接口 |
| 航海海景、船体行进、镜头与尾流 | [框架指南](Docs/FRAMEWORK.md) 的「航海选路（局外）」→ `VoyageSailingPresentation` / `VoyageSailingPath` / `VoyageWake`；节点外观读 `VoyageScenes.asset` / `VoyageDestinationPresentation`，重建工具 `Tools/VoyageNodeSetup.cs`；复用船与水体，勿改航程规则 |
| 3D 战斗场景、角色替换、特殊战扩展 | [框架指南](Docs/FRAMEWORK.md) 的「3D 战斗组合与扩展」→ `Runtime/Battle/`；场景重建会覆盖配置，先检查现有编辑 |
| 界面视觉、布局、文字与交互 | [UI 设计规范](Docs/UI_DESIGN_GUIDE.md) → [框架指南](Docs/FRAMEWORK.md) 中相关界面 → 对应 `.uss`／HUD 代码 |
| 规则、UI、存档、内容配置和代码修复 | [框架指南](Docs/FRAMEWORK.md) → 相关代码；涉及验收时再读 [验证记录](Docs/FRAMEWORK_VALIDATION.md) |
| 模型生成、减面、Unity 导入、模型归档或 LFS | [模型全流程](Docs/MODEL_IMPORT_WORKFLOW.md) → 机制文档中对应资产要求 → 相关 manifest/脚本 |
| 沉钟之母分件、支点/骨骼取舍、攻击与受击动画 | [动画设计](Docs/BELL_MOTHER_ANIMATION_DESIGN.md) → `BellMotherRigidMotion` / `BellMotherBangSetup`；复用模型不必重跑生成流程 |
| M02–M05 巨兽分件绑定、动画与特效 | [模型流程](Docs/MODEL_IMPORT_WORKFLOW.md#m02m05bang-分件后的解剖绑定) → [骨骼表现](Docs/FRAMEWORK.md#m02m05-巨兽骨骼表现) → `GiantBossRig.py` / `GiantBossSetup.cs`；仅改动作不重跑生成或全场景重建 |
| 确定使用 Deemos Agent | 在模型流程基础上，再读 [网页操作说明](DEEMOS_AGENT.md) |
| 文档整理 | 阅读本次涉及的文档和链接目标，检查职责重叠、过时状态与断链 |

## 文档职责与事实依据

- `DESIGN_DOC_v0_1.md`：产品概念、世界观、完整原型卡池与范围；不复制技术手册。
- `ASSET_MECHANICS_DESIGN.md`：具体机制提案、素材职责、美术与玩法验收标准；区分“现有”和“提案”。
- `FRAMEWORK.md`：已实现架构、接口、扩展方法和回放契约；事实以当前代码/配置为准，冲突时修正文档。
- `UI_DESIGN_GUIDE.md`：界面视觉、布局适配、文字、按钮与验收清单的统一规范；不复制接口实现。
- `FRAMEWORK_VALIDATION.md`：带日期和范围的验证结果；历史通过不代表当前修改通过。
- `MODEL_IMPORT_WORKFLOW.md`：模型制作全流程与简短当前状态；`DEEMOS_AGENT.md` 只维护网页操作。
- `BELL_MOTHER_ANIMATION_DESIGN.md`：沉钟之母分件与动画的设计取舍、复现入口及未实施的骨骼升级方案；不复制规格或验证流水账。
- `ArtSource` 下 manifest 和检查 JSON：模型来源、哈希与实测规格的唯一数据记录；不在多篇 Markdown 复制完整表。

概念目标、细化提案与实现现状分别描述不同层次，不能仅因文档较新就声称功能已实现。出现冲突先核对对应事实来源，再更新负责该内容的文档。

## 执行约定

1. 模型使用 Hyper3D MCP，或按模型流程选择 Deemos Agent。复杂定向修订优先 Agent，简单资产无需强制中转；每个 Part 先做 1–2 个样例，记录实际操作和未满足项。
2. 使用 **Unity CLI** 操作 Unity，按 `unity-cli` skill 检查版本与连接。场景、Prefab、导入设置和资产引用经 Editor/Unity API 修改，不手改其 YAML；保留 `.meta` GUID，不覆盖未保存工作。
3. 高模母版在 `ArtSource/`，运行时模型在 `Assets/`，二进制按 `.gitattributes` 走 LFS。制作、导入、验收和远端回读按模型流程执行，不把本地 Logs 当团队交付。
4. 分阶段、可回溯地 commit，只暂存当前任务文件。验证与改动相称：规则改动运行相关测试，模型改动做引擎检查，纯文档改动检查事实、链接及 diff。
5. 生成完成、减面完成、静态展示通过、动画可用和目标设备性能通过是不同状态；只报告已取得证据的结论。

## 控制文档体积

- 每项内容只维护一个主出处，其余文档链接引用；优先修改现有段落，不追加“本轮更新”重复叙述。
- 通用流程保留决策、输入/输出、操作和验收门槛；当前状态只留简表与必要限制。
- 逐轮提示词、完整工具输出、失败尝试和过时状态留在 Git 历史或被忽略的 Logs；必要参数/来源归入 manifest。不要新建流水账式 Markdown 归档。
- 保留独有机制、复现入口和未完成项。删改后检查相对链接、图片与脚本路径；无独立职责时不新增文档。
- 文档改动不触发模型重生成、Unity 资产重建或历史重写；确需改变实现时另按对应流程处理。
