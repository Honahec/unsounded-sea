# 模型制作全流程

本文件是模型生产、验收与归档的唯一流程入口。造型、机制和暂定面数预算见 [机制与素材设计](ASSET_MECHANICS_DESIGN.md)；Deemos 网页操作见 [Agent 操作说明](../DEEMOS_AGENT.md)。下文区分通用流程与 2026-10-16 已验证样例。

## 1. 先定资产任务，再选生成方式

每个任务先写清：内容 ID、用途与镜头距离、尺寸、轮廓/材质参考、必须隐藏的信息、需要独立活动的部件、静态或动画用途、目标平台与预算。每个 Part 先做 1–2 个样例；同一设计逐次修订，不批量增加变体。

| 情况 | 使用方式 | 进入下一步的条件 |
| --- | --- | --- |
| 简单道具、轮廓探索、一次生成即可检查的资产 | Hyper3D MCP 直接生成 | 实际输出满足造型、分件和材质要求，可直接评估运行时网格 |
| 主角/Boss、复杂造型约束、多视图参考、已有结果需定向修正 | 优先 Deemos Agent，使用当前页面提供的相关技能 | 锁定参考与修订目标，检查真实输出；Agent 不保证自动达到成品标准 |
| MCP 结果造型已合格，但面数过高 | 直接进入减面阶段；可用 Agent 处理已有资产 | 不为“走完整流程”重新生成造型 |
| 需要可变形角色，自动结果缺少合理关节拓扑/部件 | Agent 可辅助；补充 DCC 重拓扑、分件、烘焙与绑定 | 动作变形通过后才能标记为动画可用 |

Deemos Agent 是可选生产工具，不是每件资产的必经步骤。高模同样按需制作：合格且预算内的网格可直接导入；高密度母版用于精修、烘焙和后续修订，运行时使用验收后的低面数网格。“低模”不要求棱面美术风格。

```text
机制/镜头/参考 → MCP 生成 或 Agent 定向制作 → 本地核验
                         ↑ 造型不合格返回参考修订
本地核验 → 预算内直接使用 / 减面 / 动画重拓扑与烘焙
        → Unity 材质、尺寸和画面验收 → 按用途验收动画/性能
        → 归档 + Git LFS 提交与远端回读
```

## 2. 生成与修订

**MCP 路线：**依据当前工具 schema 提交参考、提示词、格式和几何目标；记录任务 ID 与实际请求参数；查询同一任务直至成功或失败，再下载真实模型。目标 Quad 数、档位名称或服务完成状态都不是质量验收。未暴露的参数（例如贴图分辨率）不能声称已经设置。

**Agent 路线：**在同一项目/聊天继续修订，引用准确资产，必要时上传参考图与原模型；先检查概念和正侧背一致性，再制作一个候选。要求明确本次属于原网格编辑、参考重建还是减面/重拓扑，不能把重新生成称作无损局部修改。

共同要求：角色与场景分开；铜铃、刀、绳索等交互部件单独规划；中性照明检查 PBR，不把戏剧灯光烘进颜色纹理。祷告者须藏手、藏持物，Boss 须体现空潜水服的软布塌陷；形体不合格时先改参考，不靠增加面数补救。

### Rodin 与 Agent 资产不互通时

本项目曾遇到 Agent 按 Rodin 任务 ID 查询返回 `Task not found`；这只证明当时的直接引用失败，不能推断平台所有资产均不互通。使用以下中转：

1. 从 Rodin 下载带贴图的真实 GLB，暂存至 `Logs/AssetDesign/<任务>/`。
2. 检查文件完整性与规格，再通过 Agent 的文件上传控件附加 GLB；本地路径或结果页文字不等于附件。
3. 等附件上传完成再提交，确认 Agent 可读取网格和贴图，且聊天中出现对应模型预览。
4. 在同一聊天完成修订并下载结果。若掉线，先检查原任务状态，避免重复创建任务。

### 沉钟之母：BANG 分件与刚性战斗动画样例（2026-10-17）

Deemos 的 BANG 适合把单体 Boss 拆成可独立控制的部件，但不是自动绑定器。原始整块网格的自动权重在大幅动作下出现拉伸，因此当前运行时采用“BANG 分件 → Blender 低模与 pivot → Unity 刚性部件动画”，保留旧 Generic Rig 资产作为回溯样例，不再作为默认 Prefab。

1. 使用原始 Hyper3D 生成记录 `020d73c8-7721-581f-ae65-3f8fd8c93d1d`，通过 BANG 任务 `a69dfe2b-089e-4ca1-9987-0d36ec94312c` 将沉钟之母拆为 12 个网格，其中 6 个作为触手部件，保留 BANG 输出材质。
2. 在 `arch` 运行 [BellMotherBangBuild.py](../Tools/BellMotherBangBuild.py)：建立身体、冠部和 6 个触手 pivot，保留部件层级，按 0.08 比例减面。实际三角面从 1,500,000 降至 119,992，输出 `BellMother_BANG_LOD0.glb` 和 manifest；源 GLB 不覆盖。
3. 在 Unity 通过 [BellMotherBangSetup.cs](../Tools/BellMotherBangSetup.cs) 导入 GLB、归一化至 32 m，生成 `BellMother.prefab`，并把 `bell-mother.asset` 指向它。Prefab 不含 `SkinnedMeshRenderer`，由 [BellMotherRigidMotion.cs](../Assets/UnsoundedSea/Runtime/Battle/BellMotherRigidMotion.cs) 驱动身体、冠部和触手的局部刚性运动。
4. `BattleActorView` 只接收规则结算结果：攻击直接进入短前摇、冲击和恢复；命中、死亡使用同一部件 presenter。没有骨骼的旧模型仍走兼容的简化反馈。
5. 验收顺序：BANG 预览 → Blender 低模预览 → Unity 部件/三角面检查 → Play Mode 实际触发攻击并截图 → EditMode 规则回归。当前样例已通过 3 项 BellMother 动画测试和 6 项 SeaBoss 测试；最终平台纹理压缩、LOD1/2 和目标硬件性能仍需单独验收。

### 下载后的最低检查

检查 GLB 声明长度与文件长度、SHA-256、网格/primitive、索引拓扑与实际三角面、顶点、材质槽、UV/法线、图片尺寸和通道，以及 skins/animations。只有 TRIANGLES 拓扑才能直接按索引数 ÷ 3 计面；其它拓扑需转换统计。外部纹理/缓冲依赖也必须归档，不能只下载壳文件。

实际查看正侧背、缺面、穿插、错误附件和材质。生成器的文字总结与文件不符时，以文件和引擎检查为准；不把预览图片当作模型交付。

## 3. 制作运行时低模

| 资产用途 | 处理路径 | 验收重点 |
| --- | --- | --- |
| 静态展示、硬表面道具 | 保留 UV/材质的减面；必要时重烘焙法线 | 轮廓、孔洞、薄边、法线接缝和材质分区 |
| 蒙皮角色、明显布料形变 | 先拆部件、做关节布线，再烘焙、绑定与权重检查 | 肩肘膝等变形、袖口遮挡、附件独立活动；减面不等于动画拓扑 |
| 多距离/多实例场景 | 根据屏幕占比制作 LOD，并配置/检查切换 | 近景质感、远景闪烁、轮廓跳变和实例总成本 |

给 Agent 的减面任务必须包含：源工件、**三角面**预算、保留轮廓与 UV/PBR、是否允许重烘焙、保留母版、输出稳定文件名、报告实际面数/贴图尺寸/骨骼状态。结果再次做本地核验，并在同机位对比母版；纹理经过重新导出或烘焙时，不宣称逐字节继承。

当前桌面近景样例采用 Prayer 60k / Echo 90k，来源于设计预算；不作为移动端或所有角色的默认标准。贴图分辨率、压缩、材质数、显存与动画成本分别检查，不能用 GLB 文件大小代替性能结论。

## 4. Unity 导入与表现绑定

通过 Unity CLI 操作本项目 Editor；场景、Prefab、导入器和 `.meta` 使用 Unity API 管理。项目已锁定 glTFast，clone 后等待包解析即可，无需重复安装。2026-10-16 实跑环境为 Unity 6000.6.5f1 / URP、CLI 1.0.0-beta.13、Pipeline 0.8.0-exp.1、glTFast 6.20.0；后续以项目配置和 CLI 发现结果为准。

在仓库根目录执行：

```sh
export UNITY_PROJECT_PATH="$PWD"
unity self-update --check --format json
unity status --project-path "$UNITY_PROJECT_PATH" --format json
```

glTFast 将 GLB 导入为模型 Prefab 与网格、材质、纹理子资产。另建表现 Prefab 处理尺度、原点和附件，不修改原模型来迁就场景。约定 1 单位 = 1 米、脚底 y=0、正面 +Z；本例身高 1.8 / 2.1 米，审阅场景实例转 Y=180° 面向位于 -Z 的相机。

本例材质为 `Shader Graphs/glTF-pbrMetallicRoughness`，当前 URP 可用。glTF metallic/roughness 通道由导入器解释；换 Shader 时另验色彩空间、法线方向和 smoothness 转换，不能直接把 roughness 当 smoothness。

### 复现现有 Prayer / Echo 样例

[ModelImportWorkflow.cs](../Tools/ModelImportWorkflow.cs) 是**这两个固定资产的脚本**，不是通用导入器。clone 已归档模型后，跳过 `ImportSources`，直接打开/检查已有场景。首次从下载暂存重建才执行：

```sh
unity command run_script --project-path "$UNITY_PROJECT_PATH" --timeout 180 --format json -- \
  --file Tools/ModelImportWorkflow.cs --entry ModelImportWorkflow.ImportSources \
  --args '["Logs/AssetDesign/agent"]' --timeout_ms 150000
```

源文件必须为 `Prayer_LOD0.glb`、`Echo_LOD0.glb`，SHA 与脚本一致。然后执行：

```sh
unity command run_script --project-path "$UNITY_PROJECT_PATH" --timeout 180 --format json -- \
  --file Tools/ModelImportWorkflow.cs --entry ModelImportWorkflow.CreateReviewScene --timeout_ms 150000
unity command run_script --project-path "$UNITY_PROJECT_PATH" --timeout 180 --format json -- \
  --file Tools/ModelImportWorkflow.cs --entry ModelImportWorkflow.Validate --timeout_ms 150000
```

`CreateReviewScene` 拒绝未保存场景；已有场景只打开，不重新创建或绑定。`Validate` 检查面数、材质、4K 纹理、Shader 支持、Prefab/Enemy 引用及尺寸，不能替代视觉、动画和性能验收。

**新增或替换模型：**先更新导入配置/脚本的名称、路径、SHA、预算和验收预期；当前 `ImportSources` 会拒绝覆盖不同哈希文件。应通过单独的 Unity CLI/AssetDatabase 更新步骤替换目标内容、保留 `.meta` GUID、重导入并检查子资产引用及 Prefab。不要删掉已编辑的场景来强迫初始化脚本重建；已有场景的重新绑定需要显式处理。同步更新 manifest 和验收结果。

**普通敌人绑定检查点：**25 种静态源已归档；其中 22 种已有绑定 GLB/Blend，并导入独立 `Assets/UnsoundedSea/Scenes/EnemyRigReview.unity`，各含 Idle、Action、Hit、Death、RigValidation 五段动作；未替换战斗模型。02/03/05 已按用户要求在原 Chrome Agent 项目重新生成、下载并核验，尚未绑定。来源、实测规格及限制见 [普通敌人 manifest](../ArtSource/Characters/ordinary-enemies-manifest.json)。复现使用 `Tools/rig_enemy_trials.py`（拟合数据为 `ArtSource/Characters/rig-profiles.json`）、`Tools/validate_enemy_rigs.py`；Unity CLI 执行 `Tools/EnemyRigReviewSetup.cs` 的 `EnemyRigReviewSetup.Build`，Play 后执行 `Tools/EnemyAnimationQA.cs` 的 `EnemyAnimationQA.Run`。现有 Unity 审阅归一化使用扩大的蒙皮包围盒，已测到死亡姿势离地约 0.14 米，未修复；最新持物侧拟合修订尚未全部重新导出并导入。道具/衣摆未独立分件、无手指骨骼，不能视为全部动画或目标设备性能验收通过。日常游戏入口仍为 `MainMenu.unity`，审阅结束后恢复打开该场景。

## 5. 引擎验收

先检查缺材质粉色、法线/接缝、穿插、脚底、尺度、轮廓与铜/布/皮革区分，再检查游戏镜头下的信息遮挡。需要动画的资产必须验证实际动作；只有展示用静态网格可以以静态标准验收。正式使用前在目标设备测帧耗、显存、加载卡顿，并检查最终 Player 的 Shader 和纹理。

Play 模式验证须证明帧数推进，再截图。以下沿用本例已验证命令；不同 Pipeline 版本先查命令 schema。记录原 `runInBackground` 值，结束后恢复：

```sh
unity command set_autotick --project-path "$UNITY_PROJECT_PATH" -- --persist false
unity command eval --project-path "$UNITY_PROJECT_PATH" -- --code 'return UnityEngine.Application.runInBackground;'
unity command eval --project-path "$UNITY_PROJECT_PATH" -- --code 'UnityEngine.Application.runInBackground=true;'
unity command editor_play --project-path "$UNITY_PROJECT_PATH"
unity status --until-ready --project-path "$UNITY_PROJECT_PATH" --format json
unity command wait_for --project-path "$UNITY_PROJECT_PATH" --format json -- \
  --condition '{"member":"UnityEngine.Time.frameCount","op":"changed"}' --timeout_s 10
unity command capture_game_view --project-path "$UNITY_PROJECT_PATH" --format json -- \
  --source camera --width 1920 --height 1080 --save_path Assets/Logs/AssetDesign/review.png
unity command console --project-path "$UNITY_PROJECT_PATH" --format json -- --level error
unity command editor_stop --project-path "$UNITY_PROJECT_PATH"
# 本例原值为 false；其他环境恢复其记录值
unity command eval --project-path "$UNITY_PROJECT_PATH" -- --code 'UnityEngine.Application.runInBackground=false;'
```

当前截图命令将相对路径解析至 Assets 下，故使用明确的 `Assets/Logs/...`。挑选必要证据归档后，经 AssetDatabase 删除临时截图资产。大导入使用明确超时的 `run_script`；超时后先检查实际状态，不能假定未执行而重复操作。规则/配置回归测试入口见 [框架指南](FRAMEWORK.md)。

## 6. 归档、提交与 Git LFS 同步

| 路径 | 唯一职责 |
| --- | --- |
| `ArtSource/{Characters,Vessels,Bosses}/*_High.glb` | 已选高模母版，位于 Assets 外，LFS 保存 |
| [母版 manifest](../ArtSource/Characters/manifest.json) / [运行时 manifest](../ArtSource/runtime-manifest.json) | 来源、任务/工件、文件路径、SHA、实测规格；后续修订补充父修订、操作类型、生成参数/提示词或其引用 |
| `Assets/UnsoundedSea/Art/Models/` / `Prefabs/` | 引擎内低模与表现 Prefab，提交关联 `.meta` |
| `Assets/UnsoundedSea/Scenes/ModelImportReview.unity` | 可重复检查模型的独立场景 |
| [Unity 检查结果](../ArtSource/unity-import-validation.json) / `Docs/ArtReferences/` | 当前导入结果与少量关键视觉证据 |
| `Logs/AssetDesign/` | 被忽略的下载暂存与完整日志，不作为团队唯一交付来源 |

生成参数、SHA 和逐文件规格只在 manifest 维护；正文保留决策、步骤和简短当前状态。后续替换更新当前记录，旧版本由 Git/LFS 历史追溯，不另写一篇操作流水账。签名下载 URL 不入库。

`.gitattributes` 已跟踪 GLB/glTF/bin 和相关图片/FBX；`.meta`、Prefab、Scene、Markdown 保持普通 Git 文本。Library/Temp/Logs 不提交。已有文件匹配 LFS 规则但仍为普通 Git blob 时，仅对该文件 `git add --renormalize <路径>`，不为此重写历史。

```sh
# 首次 clone 前安装 Git LFS；已有 checkout 在拉取后执行
git lfs install --local
git lfs pull
git lfs ls-files
git lfs fsck
```

仅需运行时模型时可用 `git lfs pull --include='Assets/UnsoundedSea/Art/Models/*.glb'`；使用 `GIT_LFS_SKIP_SMUDGE=1 git clone <仓库地址>` 可避免 clone 时先下载全部母版。协作分支按当前任务选择，不把示例分支写死到流程。

按“工具/包配置 → 制作母版 → 低模与 Unity 接入 → 文档与验收”分阶段 commit；已有环境不产生空提交。每阶段暂存明确路径，检查 `git check-attr filter -- <模型>` 为 lfs、`git show :<模型>` 为 pointer，并运行 `git lfs fsck`。正常 `git push -u origin HEAD` 通过 pre-push hook 上传 LFS 对象。

推送后，在独立临时 clone 中执行远端 LFS fetch/pull，比对模型 SHA 和 manifest；只检查当前 checkout 的 pointer 不足以证明远端可下载。

## 当前样例与已知缺口

2026-10-17 航海节点新增货驳与救生艇：通过 Hyper3D MCP 直接生成预算内静态网格，分别 35,000／18,000 三角面，未另制高模或使用 Agent。原始 PBR GLB 保留在运行时目录；提示词、哈希与贴图规格集中在 [节点资产 manifest](../ArtSource/Landmarks/manifest.json)。14 个组合场景与替换入口见 [框架指南](FRAMEWORK.md#航海选路局外)，不逐场复制一套生成文档。

2026-10-16：MCP 探索 → Rodin GLB 下载/Agent 上传 → Agent 参考重建 → 同聊天减面 → Unity CLI 导入 → LFS 同步已跑通。[Agent 聊天](https://hyper3d.ai/workspace/agent?project=5d2ea8e0-e8dc-4bb9-8437-3309d6f1c6ab&thread=3fab207e-1239-4613-9d04-68e5799e8edf&lang=zh)。

| 当前资产 | 母版 → LOD0 三角面 | 已验证 | 未完成 |
| --- | --- | --- | --- |
| Prayer | 500,000 → 60,000 | Unity 材质、1.8 m、脚底、Prefab 引用、正面 Play 画面 | 袖内刀/铃分件、动画、全视角与目标设备验收 |
| Echo | 800,000 → 90,000 | Unity 材质、2.1 m、脚底、Prefab 引用、战斗场景 | 更明显的空壳塌陷、独立铜铃、动画、全视角与目标设备验收 |
| BellMother | 1,500,000 → 119,992 | BANG 12 部件、6 触手 pivot、32 m、独立海面锚点、Unity 刚性战斗反馈 | 主体贴图平台压缩、LOD1/2、最终部件动画与性能验收 |
| SurveyVessel | 800,000 → 80,000 | Agent 参考制作、保留 UV 减面、18 m 船体、开放甲板、战斗舞台 | 平台贴图压缩、LOD1/2、驾驶玩法 |
| M02–M05 Boss 候选 | 具体规格见下方各自 manifest | Agent 生成、高低模归档、LOD0 Unity 导入、Prefab／四场战斗镜头、来源哈希一致 | 美术定稿、完整多视角、骨骼动画、纹理平台压缩与性能验收 |

Prayer 与 Echo 各 1 网格/材质、3 张 4K 纹理，无蒙皮/动画；尚无 LOD1/2 或 LODGroup。内嵌纹理为 ARGB32、13 级 mip，两角色完整纹理链理论约 512 MiB（非 Profiler 实测），尚未 GPU 压缩。后续需提取/重映射为可配置 TextureImporter 资产，按平台选择压缩与尺寸后复核；普通纹理平台覆写不能直接套用到这些内嵌子资产。

Prayer 的首个 Hyper3D API 绑定候选已完成，但未通过变形审阅，未替换上述静态运行时资产。上传模型的 UV 被服务重新展开；审阅脚本通过重合三角面的角点恢复原 UV，再复用原材质，未修改自动骨骼与权重。弯臂会牵拉长袍下摆，转身出现衣摆穿插；下一步需修正骨骼定位与权重，再制作正式动作。参数、来源、规格与检查结果见 [候选 manifest](../ArtSource/Characters/Prayer_Rigging-manifest.json)，[三姿势对照](ArtReferences/prayer-rigging-review.jpg) 为 Blender 诊断渲染，不代表 Unity 动画或性能验收。Echo 也已完成首个绑定候选：同样的弯臂、转身诊断未见 Prayer 的大面积衣摆牵拉，但尚未验证大幅动作、头盔／领圈刚性、多视角和 Unity 播放；未替换运行时模型。来源与实测见 [Echo 候选 manifest](../ArtSource/Characters/Echo_Rigging-manifest.json)，画面见 [三姿势对照](ArtReferences/echo-rigging-review.jpg)。

M02–M05 的高模与减面版保留在 `ArtSource/Bosses/`；减面版另存 `Assets/UnsoundedSea/Art/Models/` 并接入 `BossM02`–`BossM05`，导入副本与归档 SHA-256 一致。来源、实测规格和 Prefab 路径：[M02](../ArtSource/Bosses/TideBonesChoirmaster-manifest.json)、[M03](../ArtSource/Bosses/InvertedSurveyor-manifest.json)、[M04](../ArtSource/Bosses/UnlitFlagBearer-manifest.json)、[M05](../ArtSource/Bosses/StarlessWatcher-manifest.json)。M03 已重生成，M04 使用新项目重试；M05 轮廓仍偏人形，尚未美术定稿。没有重新生成水面，四场复用现有水体与调查船。具体运行范围见 [验证记录](FRAMEWORK_VALIDATION.md#m02m05-boss-战)。

`OceanBattle.unity` 已同时实例化两名角色与船体；替换约定见 [框架指南](FRAMEWORK.md)。船体来源、实测规格与哈希见 [船体 manifest](../ArtSource/Vessels/manifest.json)，独立发光图保留在母版目录，当前船舷照明由舞台灯具承担。角色原四份高低模的远端 LFS 回读已在 `ffe264e` 验证；船体高低模也已在 `56a27ff` 完成独立远端回读。海面 Boss 的来源、哈希与导入规格单独维护在 [Boss manifest](../ArtSource/Bosses/manifest.json)，使用独立材质降低金属感并接入 2K 压缩发光图；原始 GLB 与母版未修改。新战斗的检查范围见 [验证记录](FRAMEWORK_VALIDATION.md)。

![Unity 中的 LOD0 审阅画面](ArtReferences/unity-lod0-review.jpg)

视觉依据：[高模同机位](ArtReferences/unity-high-reference.jpg)、[Prayer 概念](ArtReferences/prayer-concept.jpg)、[Prayer 三视图](ArtReferences/prayer-views.jpg)、[Echo 三视图](ArtReferences/echo-views.jpg)、[Agent 减面交付](ArtReferences/deemos-lod0-proof.jpg)。

听筒与铜铃仍为服务端探索样例，未纳入 Unity/LFS：[听筒](https://hyper3d.ai/workspace/rodin/6bfe629a-c08a-46a7-a802-4fcc2a004711)、[铜铃](https://hyper3d.ai/workspace/rodin/d8f99115-9a17-4963-84a2-9adea98eb29f)。已淘汰角色候选、旧提示词和逐轮错误记录见 Git 历史（整理前 `ffe264e:Docs/ASSET_MECHANICS_DESIGN.md`），不在当前指南重复保留。
