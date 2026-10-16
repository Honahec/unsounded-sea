# 基础框架开发指南

适用版本：Unity 6000.6.5f1 / URP。需求依据为 `DESIGN_DOC_v0_1.md`，本阶段落实其开发顺序 1–5 的战斗基础，并预留航程图基础类型。

## 快速运行

1. 打开工程，等待脚本编译完成。
2. 打开 `Assets/UnsoundedSea/Scenes/MainMenu.unity`（构建列表第 0 位）点击 Play：标题界面提供「开始调查 / 设置 / 退出」。也可直接打开 `Battle.unity` 跳过标题。
3. 默认进入蒙面祷告者遭遇。其他遭遇通过 `BattleHud` Inspector 的 `Initial Encounter` 选择：0 公开意图、1 有限调查、2 封门、3 卡牌实验；游戏 HUD 不提供开发用场景切换。
4. 点击目标名牌选择敌人，再点击底部手牌或右侧调查工具；右下角结束回合。封门遭遇提供独立的「绘制封线」，四次即可完成，不需要击杀。
5. 右上角「调查档案」（或 J 键）展开证据与行动记录，Esc 收起；无档案打开时 Esc 打开暂停菜单（继续调查 / 设置 / 返回标题）。回放与文件存档接口仍保留在底层；不在游戏 HUD 中放置调试用保存、读取按钮。

初始化菜单 **Unsounded Sea → Create or Open Battle Scene** 只创建缺失资产，不覆盖已有配置或场景；它不会创建标题场景，标题场景已入库。

界面使用 `Assets/Fonts/default_zh.ttf`（香萃积雪宋）生成的动态 TextCore SDF 字体 `default_zh.asset`，通过 `Interface Font` 绑定。该字体缺少 ✓、箭头、几何符号与「」，状态一律用文字标签或 USS 绘制的形状表达；新增界面文字后应检查字形覆盖。发布前需确认字体商用授权。

## 战斗 HUD 与菜单

`BattlePanel` 以 1440×900 为参考、match 0.5 缩放，16:9 下逻辑画布约 1518×854（1280×720 与 1920×1080 布局一致，720p 时 1 逻辑像素 ≈ 0.84 物理像素）。正文不低于 16，辅助标签不低于 13。

- **构图**：中央留给固定相机的 3D 场景，空白区域 `pickingMode = Ignore`（手牌 ScrollView 本身也不拦截）。左上生命/理智；上中遭遇名、回合、目标（封门显示 4 段进度）；名牌悬于场景上方；左侧「现场观察」只显示征兆、可能行动或已确认意图及本回合调查所得；右侧两件调查工具（次数圆点、问题、代价）；下中手牌；左下行动、格挡/理智防护与牌堆；右下独立的「绘制封线」与结束回合。
- **视觉语言**：冷色深海半透明底板承载世界信息；纸张卡牌、黄铜标签的工具、纸质档案代表调查员自己的物件。黄铜 = 可用/选中，铜绿 = 已确认/已验证，锈红 = 危险或主动支付理智，海蓝 = 理智。
- **状态不只靠颜色**：已观察/已验证/传闻/未知、已确认/未确认、当前目标/点击选为目标、行动不足/理智不足、已用尽、本回合已绘制都有文字。定向听筒等局部答案列入「调查所得」，不会把意图标为已确认。
- **焦点**：刷新后按名称恢复焦点；出牌后焦点落在同位置的下一张可用牌，工具用尽后转到另一件工具，否则落到结束回合。
- **菜单**：`MainMenu`（标题）与 `SettingsPanel`（设置纸页，标题与暂停共用）样式在 `GameMenu.uss`。`BattleHud` 先加载菜单样式再加载 HUD 样式，以免同优先级的标签重置覆盖 HUD。设置项（主音量、显示模式、垂直同步、界面动效）由 `GameSettings` 存于 PlayerPrefs，属于本机表现偏好，不进入战斗规则或存档；「减少」动效会关闭 HUD 过渡。暂停时遮罩拦截全部场景与 HUD 输入，回合不推进。

HUD 只读取 `BattleView`（含只读的 `ObjectiveProgress / ObjectiveTarget`）并提交 `BattleCommand`；不会读取敌人的真实隐藏意图。阶段切换日志（`阶段：PlayerTurn` 等）只在 HUD 展示层过滤，底层日志不变。

两个场景中的 `Greybox … (placeholder)` 物体只是基础体块、雾与灯光，用于检查构图和中央可见性，不是正式美术；材质在 `Settings/Greybox/`。名牌目前是固定位置的一行，尚未锚定到 3D 敌人坐标；接入正式模型（按下文用 Hyper3D 与 Unity CLI 制作）后应改为投影锚点。

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
- `BattleHud`：游戏内 UI Toolkit 组合入口，创建 Session、提交操作并按快照刷新四周 HUD；遭遇配置在 Inspector 中选择。

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

同一敌人资产可在遭遇中多次引用，编译时按资产 ID 和下标生成不同实例 ID。`presentationPrefab` 是后续表现资产接入点，本阶段验证 UI 不实例化模型。需要生成正式模型时使用 Hyper3D MCP，再通过 Unity CLI 完成导入、Prefab 和表现绑定。

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
unity status --project-path /Users/honahec/workspace/unsounded-sea --format json
unity command run_tests --project-path /Users/honahec/workspace/unsounded-sea --format json -- --mode editor --filter UnsoundedSea.Tests
```

CI 中关闭本项目的交互式 Editor 后：

```sh
unity test . --mode EditMode --filter UnsoundedSea.Tests --output Logs/framework-tests.xml --timeout 600
```

本阶段不是完整 MVP：尚未实现完整 18 卡、显影粉/媒介、被注视与回视、跨局档案、事件和奖励内容、地图界面、完整 5–6 节点航程、音效与 3D 美术。下一步建议先接入 `RunMap` 的航程协调器，把现有遭遇、奖励选择和补给串联，再根据试玩扩充内容。
