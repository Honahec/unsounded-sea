# 基础框架开发指南

适用版本：Unity 6000.6.5f1 / URP。需求依据为 [概念设计](DESIGN_DOC_v0_1.md)。本文描述已实现的战斗基础、扩展接口和存档契约；新增机制提案见 [机制与素材设计](ASSET_MECHANICS_DESIGN.md)。

## 快速运行

打开 `Assets/UnsoundedSea/Scenes/MainMenu.unity`，Play 后选择「开始调查」进入 `OceanBattle.unity`，或「沉钟海域」进入 `SeaBossBattle.unity`。也可直接打开这两个场景 Play。Editor 运行当前场景；pull 不会自动切换入口。`Battle.unity` 保留为旧规则样例，`ModelImportReview.unity` 仅供美术审阅。

首场「雾海来客」：调查船上同时出现祷告者和空潜水服。每回合 3 行动、抽 5 张牌，点击模型、投影名牌或左侧目标按钮选敌，点击手牌出牌。调查工具每场次数有限。每回合消耗 1 行动刻一段封线，累计 4 段获胜；敌人可以仍然存活。生命或理智归零失败。右上「航海日志」查看证据、暂停、设置或返回标题；终局可重开。

第二场「沉钟海域」：船与「沉钟之母」中心相距约 260 m；32 m 基础模型通过舞台锚点放大至约 208 m 总高，下部浸入海面。斜侧外部镜头完整呈现 18 m 调查船，巨兽占据远处右侧和上方，二者之间留出海面动作空间。每轮公开巨浪（21 生命伤害）或钟鸣（11 理智冲击，可打断）；用格挡、理智防护或打断应对，撑过五轮后撤离。进度在敌方结算后推进，资源归零优先判败；不提供封线行动，伤害 Boss 不代替生存目标。

## 3D 战斗组合与扩展

```text
BattleScenario（遭遇 + 场景 Prefab + 角色覆写 + 规则模块）
    → BattleDirector（独立 Session、选敌、命令、反馈锁、重开）
        → OceanBattleHud（BattleView → UI；Command → Director）
        → BattleStage（船体、相机、灯光、海面、音响、生成锚点）
            → BattleActorView（模型实例、碰撞选敌、名牌锚点、静态网格反馈）
```

- **换场景**：复制或创建带 `BattleStage` 的 Prefab，配置 `battleCamera`、`vessel`（所有随船运动的物体父节点）、`actorAnchors` 和选中材质，再替换 Scenario 的 `stagePrefab`。海面与音响可选；当前舞台不支持角色移动/导航。
- **镜头构图**：海面 Boss 的相机独立于船体 rig，船体摇晃不带动镜头；位置 `(130,24,-35)`、看向 `(0,8,95)`、垂直 FOV 60°。换船或海怪后需一起检查尺度、距离、海面覆盖、名牌和 HUD 遮挡，不能只移动相机。
- **甲板 / 海面锚点**：普通锚点保持甲板表现；海面锚点挂 `BattleAnchor` 并设为 `Sea`，必须放在船体 rig 之外，校验会拒绝错误挂载。设置独立位置、浸没深度、名牌高度/偏移、轻微升沉与扰流半径；海面角色通常关闭脚底选中环。碰撞范围按模型局部包围盒计算，锚点旋转/缩放不重复放大范围。
- **换角色**：修改 Enemy 的 `presentationPrefab`，或在 Scenario 的 `actorOverrides` 按敌人槽位覆写。模型约定脚底 y=0、正面 +Z、真实米制；锚点决定位置和朝向。同种敌人可以重复引用，规则实例 ID 为 `id:槽位`。出生前校验相机、船体、锚点和模型引用。
- **换遭遇**：Scenario 指向另一个 Encounter；牌组、资源、敌人意图和工具仍属于规则数据。UI 按快照更新，不依赖两只敌人的名字。新增敌人需增加相应舞台锚点并检查名牌排布。
- **特殊战入口**：派生 `BattleRuleModule`，为每次会话创建新的 `EffectRegistry` / `IEncounterObjective`。`SealingRuleModule` 用于雾海封线；`SurvivalRuleModule` 用于海面撤离，按已结算轮数计数。两者均不靠敌人全部死亡判胜。模块资产只存配置，不能共享可变进度。需要新命令、敌人阶段策略或即时玩法时仍需扩展 Core 与测试；当前入口不等于这些玩法已实现。
- **反馈事件**：Director 的 `Resolved(before, after, command)` 通知舞台，`Changed` 通知 UI；短暂反馈期间拒绝连点，规则仍同步确定性结算。暂停冻结舞台运动与环境声；减少动效关闭船体摇晃、镜头震动和角色摆动。

「沉钟海域」使用 **Uber Stylized Water**：双向 Gerstner 波、细波法线、半分辨率平面反射、浅层折射、接触泡沫和海雾；甲板战保留原轻量水面。巨兽扰流与钟声只负责表现，不影响规则随机数。场景雾、月光、暖色船灯、Bloom/Vignette 构成海洋氛围；音轨为原创合成底噪和动作提示。三名角色仍是无骨骼静态网格，整体变换反馈不能替代正式战斗动画。

### 海面配置

- `BattleStage.water` 可选接入 `BattleWater`，`surface` 指向同一海面 Renderer；使用 `Art/SeaBoss/DeepOcean.mat` 和 `DeepOceanMesh.asset`。水面保持水平、单位缩放，放在 Water（4）层，反射层排除水面；一场战斗只启用一个平面反射水体。当前网格为固定战斗镜头设计，不是无限海洋或船舶浮力系统。
- `BattleWater` 在每场开始克隆材质并创建反射组件，停用时恢复原材质、清理相机和 RT。`_SeaTime` 驱动全部水面动画，暂停冻结时钟及反射；减少动效时以 1/4 速率连续推进，不跳变相位。`_Threat` 保留海面角色的扰流和冲击反馈。避免直接修改共享材质。
- PC 管线需要 **Depth Texture / Opaque Texture**；当前 Unity 6000.6.5f1、URP 17.6、Forward+ 已配置。`reflectionScale` 默认 0.5，降低可减少额外渲染成本。移动管线、XR、多相机/多水位组合尚未验收。
- 上游版本、MIT 许可、原始哈希与本地补丁集中记录在 [UPSTREAM.json](../Assets/ThirdParty/UberStylizedWater/UPSTREAM.json)。只引入运行依赖，不依赖上游 Shader Graph 编辑插件。升级需重新应用时钟、扰流和反射补丁，并验证所有 Shader pass；不要用上游重新导出的 Shader 直接覆盖本地版。

`OceanBattlePanel` 参考 1600×900；USS 位于 `Runtime/Battle/OceanBattleHud.uss`。四周工具与手牌让出中心 3D 空间；名牌和中央指针使用战斗相机视口与 UI 根节点换算，不依赖离屏反射渲染期间可能变化的 `Screen` 尺寸。UI 只读取 `BattleView`，未知意图保持候选说明；历史调查保存在日志中。手牌横向滚动支持超过五张；字体使用项目现有 TextCore SDF。

`MainMenu` 与 `SettingsPanel` 复用原菜单/设置。`GameSettings` 的音量、全屏、垂直同步、减少动效属于设备偏好，不进入规则或回放。旧 `BattleHud` 仅服务 `Battle.unity`，不参与新战斗。

场景已入库，无需生成脚本才能运行。需要有意重建本场样例时，用 Unity CLI 执行 `Tools/OceanBattleSetup.cs` 的 `OceanBattleSetup.Build`；它会覆盖本场场景与配置，并更新标题入口，运行前先保存改动。`Tools/SeaBossSetup.cs` 的 `ImportModel` / `Build` 分别归一化 Boss 并重建海面样例；仅重设海面战构图可调用 `SeaBossSetup.ApplyComposition`，不会重建遭遇或场景。重建后再执行 `Tools/UberWaterSetup.cs` 的 `UberWaterSetup.Build` 恢复新版海面（只更新水体、雾和相应 Prefab 配置）。`Tools/ImportSurveyVessel.cs` 负责船体归一化；模型制作和 LFS 规范见 [模型全流程](MODEL_IMPORT_WORKFLOW.md)。

## 层级与依赖

```text
UnsoundedSea.Core                 纯 C#；无 UnityEngine 引用
    ↑
UnsoundedSea.Runtime              ScriptableObject、UI Toolkit、文件存档适配器
    ↑
UnsoundedSea.Editor               内容校验、幂等样例初始化

UnsoundedSea.Tests.EditMode       NUnit 行为测试，引用 Core / Runtime
```

规则层不读取场景、时间、全局随机数或静态单例。每个 `BattleSession` 独立持有牌堆、资源、敌人、知识和操作记录，可以同时运行多个会话。外部依赖通过构造函数注入，不引入服务定位器或全局事件总线。

- `Definitions.cs`：不可变战斗规格。构造时复制集合；Unity 资产只负责创作静态数据。
- `BattleSession`：命令入口、回合状态机及规则协调；所有玩家操作调用 `Execute`。
- `BattleView`：只读快照，UI 不持有可变规则状态。敌人的真实行动只存在于 Session 私有状态中。
- `EffectRegistry` / `ICardEffectHandler`：按稳定字符串 ID 注册卡牌效果。
- `KnowledgeState`：证据带来源和可信级别，低级别记录不能覆盖已验证信息。
- `IEncounterObjective`：击退、封门、存活目标；可注入其他目标实现。
- `RunMap`：有向无环节点图，区分进入与完成节点，阻止跳点、回访、悬空连接和不可达节点。
- `BattleReplay` / `IBattleSaveStore`：规则回放与存储介质分离。
- `BattleDirector` / `BattleScenario`：运行时组合入口与可替换配置；`OceanBattleHud` 仅负责表现。

## 保证的规则

1. `TurnStart → IntentLocked → PlayerTurn → Resolve → TurnEnd`；终局进入 `Victory / Defeat` 后拒绝新操作。
2. 每回合重置行动、格挡和理智防护，默认抽 5、行动 3。手牌在结束回合进入弃牌堆；抽牌堆耗尽才洗入弃牌堆。
3. 卡牌拥有唯一实例 ID。出牌期间先离开手牌，效果结算后才进弃牌/消耗区，因此抽牌效果不能立即抽回自身。
4. 先验证费用、目标和可用性，再修改状态；失败命令不写入回放。效果处理器是受信任规则代码，应保持确定性、无异常、无外部 IO；目前没有针对自定义处理器异常的事务回滚。
5. 敌人行动在玩家回合前锁定。抽牌与敌方行动使用分离的固定算法随机序列；调查不访问随机数。两候选异常敌人共享征兆，而调查仅修改可见知识。
6. 提灯确认可观察动作；听筒只回答声源；贝壳只回答伤害资源类型。不可观察或已回答的问题不消耗道具。每场工具使用次数重置，最多装备两个不同道具。
7. 本回合意图确认在下回合失效；历史证据保留，键中包含目标和回合。日志只记录当时玩家可知信息，真实行动直到调查允许或实际结算时才显示。
8. 格挡仅减生命伤害，理智防护仅减被动冲击。主动支付绕过防护，并要求支付后至少剩 1 理智。耗尽生命或理智立即失败。
9. 封门任务独立于手牌，每回合最多一次、消耗 1 行动。击杀不自动完成封门/存活目标。存活目标在敌方结算后计数，资源耗尽优先失败。
10. 普通卡牌 `exhaust` 表示每张实例每场只能使用一次；「应急包扎」当前使用这一规则，同名多副本会分别计次。若设计改为同名牌共享次数，需要增加按卡牌 ID 的战斗限制器。

## 新增内容

### 新卡牌

Project 右键 **Create → Unsounded Sea → Card**。填写唯一 `id`、名称、完整规则说明、行动/理智费用和效果列表，再把资产加入遭遇的 `deck`。同一资产可重复引用形成多副本；不同资产不能共用一个卡牌 ID。

已注册效果：

| ID | 参数与含义 |
| --- | --- |
| `damage` | 对所选敌人造成 amount 伤害 |
| `block` / `ward` | 获得 amount 格挡 / 理智防护 |
| `draw` / `heal` | 抽 amount 张 / 恢复 amount 生命 |
| `observe` | 记录可见征兆；amount 不使用 |
| `investigate` | 提灯式调查，意图已知则抽 1；amount 不使用 |
| `conditional-damage` | 已确认时造成 amount 伤害，否则 5 |
| `interrupt` | 打断本回合可中断动作；amount 不使用 |
| `objective` | 推进允许任务行动的目标 amount 点 |

新规则实现 `ICardEffectHandler`，在组合入口创建默认注册表后调用 `Register("your-effect", handler)`，传给 Session。处理器通过 `EffectContext` 执行动作，保持无状态、确定性；不要直接操作 Unity 对象。`conditional-damage` 的未知伤害 5 是当前样例规则；如需通用多参数效果，应扩展 `EffectSpec` 并同步内容指纹和版本，而非用卡牌 ID 分支。

### 新敌人和遭遇

创建 Enemy 和 Encounter 资产。意图的 `clue` 与 `candidateDescription` 必须提供可推断信息；不要在隐藏行动间意外使用泄露答案的 UI 图标。当前敌人从意图列表等概率选择；后续可提取 `IIntentPolicy` 支持循环、权重和阶段策略，始终在 `IntentLocked` 前决策。

同一敌人资产可在遭遇中多次引用，编译时按资产 ID 和下标生成不同实例 ID。`presentationPrefab` 是表现接入点；新 `BattleStage` 会实例化该字段或 Scenario 覆写。已绑定样例、独立审阅场景和生产流程见 [模型全流程](MODEL_IMPORT_WORKFLOW.md)。

自定义目标实现 `IEncounterObjective` 并注入 Session；恢复回放时必须注入同样的目标与效果注册表。当前资产菜单提供击退、封门和存活三种配置。

### 更换界面

生产 UI 可以使用 uGUI 或 UI Toolkit，只需读取 `session.View`，提交 `BattleCommand.Play / Investigate / Task / EndTurn` 并处理 `CommandResult`。不要从敌人配置和随机种子反推当前隐藏意图。快照会复制集合，适合按命令刷新，不应每帧轮询分配。

## 存档与复现

存档包含格式版本、完整内容 SHA-256、初始种子和成功命令序列。恢复先验证版本与内容，再从头确定性重放，重建手牌、弃牌、消耗、工具次数、知识、目标和回合。非法操作序列会拒绝恢复，不会替换当前会话。

数据指纹覆盖卡组顺序、全部卡牌效果、敌人意图、工具和战斗参数；任何内容改变都会拒绝加载旧回放。修改效果处理算法、随机算法、自定义目标或其他规则时必须提升 `BattleReplay.CurrentVersion`，旧版本迁移尚未提供。自定义注册表/目标的实现本身不进入数据指纹，版本升级是调用方契约。该机制用于本地复现，不是防作弊方案。

文件存储先写临时文件并 flush，再原子替换已有文件，保留旧 `.bak`；读失败不会自动覆盖或清空。默认实现面向桌面文件系统，WebGL/云存档需另实现 `IBattleSaveStore`。

当前存档边界为单场战斗；完整航程、永久档案和多版本迁移尚未接入。后续航程状态应保存稳定内容 ID、访问路径、卡组、资源和已验证档案，不保存 Unity 对象引用。

## 验证与后续工作

Unity 菜单 **Unsounded Sea → Validate All Encounters** 可检查配置与未知效果；Test Runner 的 EditMode 中运行 `UnsoundedSea.Tests`。

使用已连接编辑器时：

```sh
export UNITY_PROJECT_PATH="$PWD" # 在仓库根目录执行
unity status --project-path "$UNITY_PROJECT_PATH" --format json
unity command run_tests --project-path "$UNITY_PROJECT_PATH" --format json -- --mode editor --filter UnsoundedSea.Tests
```

CI 中关闭本项目的交互式 Editor 后：

```sh
unity test . --mode EditMode --filter UnsoundedSea.Tests --output Logs/framework-tests.xml --timeout 600
```

本阶段不是完整 MVP：尚未实现完整 18 卡、显影粉/媒介、被注视与回视、跨局档案、事件和奖励内容、地图界面、完整 5–6 节点航程、正式骨骼动画与目标平台性能优化。下一步建议先接入 `RunMap` 的航程协调器，把现有遭遇、奖励选择和补给串联，再根据试玩扩充内容。

已有测试与编辑器验证的日期、覆盖范围和限制见 [验证记录](FRAMEWORK_VALIDATION.md)。修改后应重新运行相关检查，旧记录不是新修订的验收结论。
