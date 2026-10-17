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

### 沉钟之母：分件与动画样例

当前采用 BANG 分件 → Blender 低模与支点 → Unity 刚性部件动画，未使用柔性骨骼蒙皮。整网格骨骼试验的取舍、当前生产步骤、根部约束与重量感设计，以及后续骨骼链方案，统一见[沉钟之母动画设计](BELL_MOTHER_ANIMATION_DESIGN.md)。规格以 [BANG manifest](../ArtSource/Bosses/BellMother_BANG-manifest.json) 为准，测试范围见[验证记录](FRAMEWORK_VALIDATION.md#沉钟之母-bang-分件与刚性动作流程)。

### M02–M05：BANG 分件后的解剖绑定

使用同学上传的 BANG 结果，没有重新生成造型或贴图。原生成来源仍在各 `*-manifest.json`；`*_BANG-manifest.json` 记录分件母版与当前运行时输出，`*_Rig-manifest.json` 记录关节、权重、输入版本、UV／图片校验与动画验收。四个绑定报告：[领唱者](../ArtSource/Bosses/TideBonesChoirmaster_Rig-manifest.json)、[司衡者](../ArtSource/Bosses/InvertedSurveyor_Rig-manifest.json)、[执旗者](../ArtSource/Bosses/UnlitFlagBearer_Rig-manifest.json)、[守望者](../ArtSource/Bosses/StarlessWatcher_Rig-manifest.json)。旧 [BANG 静态检查](../ArtSource/Bosses/BANG-unity-validation.json) 只代表绑定前状态。

1. **取输入**：从 `3430ecc` 恢复四份未绑定的 `Assets/UnsoundedSea/Art/Models/*_BANG_LOD0.glb` 到独立输入目录，通过 `git show <revision>:<path> | git lfs smudge` 取得真实二进制。不要拿当前蒙皮输出再次绑定，也不要直接覆盖运行时目录。`BossBangBuild.py` 是更上游的高模减面工具，不是日常动画调参入口。
2. **绑定**：在 arch 的 Blender 5.2.2 执行 `blender -b --python Tools/GiantBossRig.py -- INPUT_DIR OUTPUT_DIR`。脚本检查输入 SHA，按正侧视图识别部件，再建立解剖支点与短骨骼链；硬壳单骨刚性权重，囊体／悬摆／旗布／帷幕局部渐变权重。旗杆与旗布只按原面拆开，保留 UV，不重网格化。必须通过面与 UV 签名一致、权重归一、真实变形和归零恢复检查。
3. **导入**：复制输出到本机，退出 Play、保存编辑；Unity CLI 执行 `Tools/GiantBossSetup.cs` 的 `Import`（参数为输出目录；默认 `Logs/GiantBosses`），再执行 `Build`。保留 GLB `.meta`，重新计算归一尺寸与底部原点：增加 armature 会改变 glTF 子节点 ID，旧嵌套 Prefab 缩放覆盖可能失效，不能照抄。`Build` 会重设四个 Boss 的动作、构图和舞台参数；手调前先检查差异，不运行全遭遇重建工具。
4. **运行与归档**：动画由 `GiantBossMotion` 程序曲线驱动真实骨骼，GLB 没有内嵌 clips；不要把 `animations: 0` 当作静态模型。先验收 `GiantBossAnimationTests`，再在 Play 中运行 `GiantBossQA.Record(index)` / `Hit(index)`，index 为 0–3。材质图片按字节比较，最终镜头检查重量、接缝、波浪和船的响应。流程、动作设计与边界分别见[框架](FRAMEWORK.md#m02m05-巨兽骨骼表现)和[验证记录](FRAMEWORK_VALIDATION.md#m02m05-boss-战)。输出与母版按 LFS 归档，忽略的 Logs 只放临时帧和完整日志。

### M05 五守卫资产

五份守卫 `StarlessGuardAnchor`、`StarlessGuardLamp`、`StarlessGuardBell`、`StarlessGuardSail`、`StarlessGuardEye` 均获用户公开生成授权；前三份由 Hyper3D Agent 制作，后两份在 Agent 连续失败后按用户要求改用 Hyper3D API。该授权仅覆盖本批五守卫。每份保留原高模与真实 BANG 分件高模，运行时文件为 `Assets/UnsoundedSea/Art/Models/<name>_BANG_LOD0.glb`，Prefab 使用同名 `<name>.prefab`。部件数、三角面、来源与哈希记录在各自 `*-manifest.json`／`*_BANG-manifest.json`，本文不复制汇总计数；认证密钥不进入仓库或 manifest。

五份原高模与 BANG 高模均已下载归档。运行时由 `Tools/BossBangBuild.py` 保留组装位置，建立 `Body/Part_XX` 独立中心支点并减面至每只约 30k 三角面、2K 贴图；这不是骨骼绑定。`Tools/StarlessGuardSetup.Import` 负责导入守卫资产；五份 Prefab 齐备后，`Tools/GiantBossSetup.BuildM05` 配置运行时引用及左二／中一／右二海面槽位。水下上升、精神脉冲和最终构图的验收范围见[验证记录](FRAMEWORK_VALIDATION.md#m02m05-boss-战)。

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

**普通敌人运行时静态接入：**25 种归档源已通过 `Assets/UnsoundedSea/Editor/Tools/OrdinaryEnemyStaticSetup.cs` 导入 `Art/Characters/OrdinaryStatic`，表现 Prefab 位于 `Art/Prefabs/OrdinaryStatic`，作为静态源和回溯样例保留，普通遭遇现已引用下方 CuratedRigs。静态导入入口保留；`VoyageCampaignSetup.Setup` 现配置骨骼表现和遭遇目录，仅保存本次资产，不全局保存无关编辑。导入文件与归档源逐字节匹配，完整运行时路径／SHA／规格维护在 [运行时 manifest](../ArtSource/runtime-manifest.json)。当前战斗驱动与接入入口见[框架指南](FRAMEWORK.md#3d-战斗组合与扩展)。

**普通敌人绑定保留集：**25 个普通人形敌人已按同角度诊断从两批绑定中择优，统一到 `Assets/UnsoundedSea/Art/{Models,Prefabs}/CuratedRigs/`：旧版 10 个、新版 10 个、合并版 5 个，每个角色只保留一个模型；来源、选择理由、SHA 和延后问题集中在 [保留集 manifest](../ArtSource/Characters/CuratedRigs/manifest.json)。`ArtSource/Characters/CuratedRigs/` 仅保存元数据与验收依据，不重复存放模型。旧 `RigTrials`、`Hyper3DRigs` 的原始返回、Blender 文件、运行时候选、独立测试动画和旧审阅场景已从当前树清理；25 个保留集 Prefab 已接入普通关卡并配置 `DeckActorMotion`；Prayer／Echo 使用下方复用绑定。

5 个合并版保留新版上肢，迁移旧版下肢关节／权重，并在骨盆平滑过渡；修正新版权重中把靴部固定到 Root 的情况，屈膝牵拉减少。依据见 [质量审阅](../ArtSource/Characters/CuratedRigs/quality-review.json)、[合并版诊断图](ArtReferences/curated-rig-hybrids.jpg) 和 [Unity 检查](../ArtSource/Characters/CuratedRigs/unity-validation.json)。复现工具为 `Tools/CompareHumanoidRigs.py`、`Tools/CombineHumanoidRigs.py`；恢复当前独立 Prefab 可通过 Unity CLI 执行 `Tools/CurateHumanoidRigs.cs` 的 `CurateHumanoidRigs.Consolidate`，参数为 `ArtSource/Characters/CuratedRigs/manifest.json`。历史比较输入只留 SHA 与历史修订；远端永久清理后，不保证淘汰版本可恢复。

绑定筛选阶段未制作战斗动画；现已接入有限幅度的站桩 idle／攻击／受击。10 个旧版 GLB 为复用已有 LFS 对象而保留原内嵌测试片段，内嵌片段不参与战斗，由 Prefab 上的 `DeckActorMotion` 驱动；不以旧测试片段作为验收依据。Unity 检查涵盖真实导入网格的胸部、肘部、膝部可恢复变形、材质、尺度和脚底；大幅动作、持物刚性、衣摆、藏手约束与设备性能继续延后。glTFast 当前导入每顶点四权重，GLB 保留完整权重。

**LFS 额度：**删除当前文件、历史改写和本地 `git lfs prune` 都不会直接清除 GitHub 远端存储。实际清除需 [GitHub Support 或删除重建仓库](https://docs.github.com/en/repositories/working-with-files/managing-large-files/removing-files-from-git-large-file-storage)。本次的保留对象与历史清理候选见 [LFS 清理清单](../ArtSource/Characters/CuratedRigs/lfs-cleanup.json)；未改写共享分支历史、未删除远端仓库，后续推送前检查额度，并单独验证远端回读。

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
| Prayer | 500,000 → 60,000 | Unity 材质、1.8 m、脚底、Prefab 引用、正面 Play 画面 | 袖内刀/铃分件、大幅动作、全视角与目标设备验收 |
| Echo | 800,000 → 90,000 | Unity 材质、2.1 m、脚底、Prefab 引用、战斗场景 | 更明显的空壳塌陷、独立铜铃、大幅动作、全视角与目标设备验收 |
| BellMother | 1,500,000 → 119,992 | BANG 12 部件、6 触手 pivot、32 m、独立海面锚点、Unity 刚性战斗反馈 | 主体贴图平台压缩、LOD1/2、最终部件动画与性能验收 |
| SurveyVessel | 800,000 → 80,000 | Agent 参考制作、保留 UV 减面、18 m 船体、开放甲板、战斗舞台 | 平台贴图压缩、LOD1/2、驾驶玩法 |
| M02–M05 Boss 候选 | 具体规格见各自 manifest | BANG 分件、解剖蒙皮、idle／攻击／受击、四场镜头与浪／雾反馈 | 美术定稿、完整多视角、大幅动作精修、纹理平台压缩与性能验收 |
| M05 五守卫 | 规格只见各自 manifest | Anchor 高模与真实 BANG 高模已归档；其余四份生产中 | 全部导入、Prefab 引用、六槽构图、水下上升、精神脉冲与目标设备验收 |

Prayer 与 Echo 原静态源各 1 网格/材质、3 张 4K 纹理；当前 Prefab 已改用归档绑定生成的蒙皮副本。新副本来源、哈希与 UV 恢复检查集中在 [DeckRigs manifest](../ArtSource/Characters/DeckRigs-manifest.json)，复现使用 `PrepareHyper3DRigs.py`（`skip_preview` 可省去诊断渲染）；祷告者再用 `PrayerSkirtRig.py` 添加独立的八组裙摆骨骼和渐变权重，最后执行 `DeckAnimationSetup.Apply`。未改原始 GLB；尚无 LOD1/2 或 LODGroup。内嵌纹理为 ARGB32、13 级 mip，两角色完整纹理链理论约 512 MiB（非 Profiler 实测），尚未 GPU 压缩。后续需提取/重映射为可配置 TextureImporter 资产，按平台选择压缩与尺寸后复核；普通纹理平台覆写不能直接套用到这些内嵌子资产。

Prayer／Echo 的首轮 Hyper3D 自动绑定审阅分别记录于 [Prayer 候选 manifest](../ArtSource/Characters/Prayer_Rigging-manifest.json) 和 [Echo 候选 manifest](../ArtSource/Characters/Echo_Rigging-manifest.json)；[Prayer 三姿势](ArtReferences/prayer-rigging-review.jpg)／[Echo 三姿势](ArtReferences/echo-rigging-review.jpg) 是当时的 Blender 诊断，不是当前战斗验收。服务重展 UV 后，通过重合三角面角点恢复原 UV／材质。Prayer 的大幅弯臂曾牵拉袍身，因此当前保持藏手，以专用裙摆骨骼的妖风强摆表达攻击，胸背只作小幅跟随；Echo 使用较慢的前压与受击回稳。当前动作曲线见[机制文档](ASSET_MECHANICS_DESIGN.md#甲板敌人的战斗动作设计与当前实现)，Unity 证据见[验证记录](FRAMEWORK_VALIDATION.md#甲板敌人战斗动画与普通战海面)。大幅肢体动作、头盔／领圈刚性、多视角与目标设备性能仍需另验。

M02–M05 原始高低模和 BANG 母版保留在 `ArtSource/Bosses/`；当前 `Assets/UnsoundedSea/Art/Models/*_BANG_LOD0.glb` 是绑定后的运行时模型，不能再与旧未绑定低模比较文件哈希。原生成来源：[M02](../ArtSource/Bosses/TideBonesChoirmaster-manifest.json)、[M03](../ArtSource/Bosses/InvertedSurveyor-manifest.json)、[M04](../ArtSource/Bosses/UnlitFlagBearer-manifest.json)、[M05](../ArtSource/Bosses/StarlessWatcher-manifest.json)；当前规格与检查见上方分件／绑定报告。M05 轮廓仍偏人形，尚未美术定稿。四场复用现有水体与调查船。

`OceanBattle.unity` 已同时实例化两名角色与船体；替换约定见 [框架指南](FRAMEWORK.md)。船体来源、实测规格与哈希见 [船体 manifest](../ArtSource/Vessels/manifest.json)，独立发光图保留在母版目录，当前船舷照明由舞台灯具承担。角色原四份高低模的远端 LFS 回读已在 `ffe264e` 验证；船体高低模也已在 `56a27ff` 完成独立远端回读。海面 Boss 的来源、哈希与导入规格单独维护在 [Boss manifest](../ArtSource/Bosses/manifest.json)，使用独立材质降低金属感并接入 2K 压缩发光图；原始 GLB 与母版未修改。新战斗的检查范围见 [验证记录](FRAMEWORK_VALIDATION.md)。

![Unity 中的 LOD0 审阅画面](ArtReferences/unity-lod0-review.jpg)

视觉依据：[高模同机位](ArtReferences/unity-high-reference.jpg)、[Prayer 概念](ArtReferences/prayer-concept.jpg)、[Prayer 三视图](ArtReferences/prayer-views.jpg)、[Echo 三视图](ArtReferences/echo-views.jpg)、[Agent 减面交付](ArtReferences/deemos-lod0-proof.jpg)。

听筒与铜铃仍为服务端探索样例，未纳入 Unity/LFS：[听筒](https://hyper3d.ai/workspace/rodin/6bfe629a-c08a-46a7-a802-4fcc2a004711)、[铜铃](https://hyper3d.ai/workspace/rodin/d8f99115-9a17-4963-84a2-9adea98eb29f)。已淘汰角色候选、旧提示词和逐轮错误记录见 Git 历史（整理前 `ffe264e:Docs/ASSET_MECHANICS_DESIGN.md`），不在当前指南重复保留。
