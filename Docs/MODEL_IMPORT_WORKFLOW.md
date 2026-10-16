# 模型交付、Unity 导入与 Git LFS 同步

2026-10-16 实跑记录。运行时采用减面网格，精细感由轮廓、PBR 材质和灯光共同提供。高模仅作为制作母版保存在 Unity 的 `Assets/` 外。当前是桌面近景展示用 LOD0 候选，不是最终性能预算，也不是刻意采用棱面感的 low-poly 美术风格。

## 本次交付

| 资产 | 高模三角面 | Unity LOD0 三角面 | 减少 | LOD0 顶点 | GLB 大小 | 展示高度 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 祷告者 Prayer | 500,000 | 60,000 | 88% | 64,861 | 42.16 MiB | 1.8 m |
| 空潜水服 Boss Echo | 800,000 | 90,000 | 88.75% | 116,379 | 52.36 MiB | 2.1 m |

每份均为 1 个网格、1 个 PBR 材质、3 张 4096 × 4096 内嵌贴图，无骨骼、蒙皮和动画。减面输出贴图与高模的文件哈希不同，不宣称逐字节继承；实际验证了贴图尺寸、材质映射和 Unity 中的可见结果。

![Unity Play 模式下的减面模型](ArtReferences/unity-lod0-review.jpg)

[同一相机的高模参考](ArtReferences/unity-high-reference.jpg)；[Deemos 交付记录截图](ArtReferences/deemos-lod0-proof.jpg)。截图为中性灯光下的模型检查，不代表成品场景美术。

目录职责：

- `ArtSource/Characters/*_High.glb`：高模母版，通过 LFS 保存，不进入 Unity 导入或构建依赖。
- `ArtSource/Characters/manifest.json`：高模来源、大小、面数、SHA-256。
- `ArtSource/runtime-manifest.json`：低模任务、工件 ID、文件哈希和实测规格。
- `ArtSource/unity-import-validation.json`：Unity 导入后的网格、贴图、尺寸与 GUID 检查结果。
- `Assets/UnsoundedSea/Art/Models/*_LOD0.glb`：当前运行时展示网格，通过 LFS 保存。
- `Assets/UnsoundedSea/Art/Prefabs/Prayer.prefab`、`Echo.prefab`：标准化尺寸和原点的表现 Prefab。
- `Assets/UnsoundedSea/Scenes/ModelImportReview.unity`：独立模型审阅场景。
- `Tools/ModelImportWorkflow.cs`：由 Unity CLI 编译执行的导入与检查脚本；不进入玩家代码。
- `Logs/AssetDesign/agent/`：被 Git 忽略的下载暂存和原始验证输出，不是团队同步来源。

`prayer.asset` / `echo.asset` 的 `presentationPrefab` 已绑定。现有 `BattleSandbox` UI 尚不实例化这个字段，所以此交付没有把模型接入战斗画面。审阅场景未添加到构建场景清单。

## 1. Rodin → 本地文件 → Deemos Agent

本项目实际遇到 Rodin 任务链接无法被 Agent 读取的问题。可靠路径是下载真正的 GLB，再通过 Agent 附件上传；仅粘贴任务 UUID 或结果页不等于资产已导入。

1. 在 Rodin 结果中下载带 PBR 贴图的 GLB，保留原文件。
2. 在本地核对文件长度、SHA-256、三角面、材质、内嵌图片尺寸和 skins/animations，避免将生成请求的 Quad 目标当作实际三角面。
3. 上传真实 GLB 至 Agent，要求明确确认可读取网格及贴图。
4. 锁定概念参考后做一次定向精修；记录实际属于原网格编辑还是参考重建。本轮高模是参考重建。
5. 在同一聊天指定已交付高模工件，要求只做保留轮廓和 UV/PBR 的减面，不重新生成造型。本轮目标为 Prayer 60,000 / Echo 90,000 三角面。
6. 下载减面 GLB，重做本地核验，按稳定名称归档。签名下载链接会过期，不把它写进仓库。

[本轮 Agent 聊天](https://hyper3d.ai/workspace/agent?project=5d2ea8e0-e8dc-4bb9-8437-3309d6f1c6ab&thread=3fab207e-1239-4613-9d04-68e5799e8edf&lang=zh)。工件与 SHA 详见 manifest；Agent 文本曾错误报告 2K，高低模文件实际均为 4K，以文件和引擎解析结果为准。

## 2. Unity 导入

实跑环境：Unity 6000.6.5f1、URP、Unity CLI 1.0.0-beta.13、Pipeline 0.8.0-exp.1、官方 `com.unity.cloud.gltfast@6.20.0`。包版本和锁文件已提交。glTFast 自动导入 GLB 为模型 Prefab 与内嵌网格、材质、纹理子资产。

以下命令在仓库根目录执行，`UNITY_PROJECT_PATH` 设为本机此仓库绝对路径：

```sh
export UNITY_PROJECT_PATH="$PWD"
unity self-update --check --format json
unity status --project-path "$UNITY_PROJECT_PATH" --format json
```

已有包随项目解析，无需重复安装。新项目首次安装可使用已连接 Editor 的 `package_add`，并用 `package_status` 确认安装、编译结束：

```sh
unity command package_add --project-path "$UNITY_PROJECT_PATH" --format json -- \
  --identifier com.unity.cloud.gltfast@6.20.0 --confirm true
unity command package_status --project-path "$UNITY_PROJECT_PATH" --format json
```

新资产准备在暂存目录，文件名为 `Prayer_LOD0.glb` / `Echo_LOD0.glb`。本轮脚本固定校验已交付修订的 SHA；后续替换模型必须先更新 manifest 和脚本的预期值，不能静默覆盖不同修订。

```sh
unity command run_script --project-path "$UNITY_PROJECT_PATH" --timeout 180 --format json -- \
  --file Tools/ModelImportWorkflow.cs --entry ModelImportWorkflow.ImportSources \
  --args '["Logs/AssetDesign/agent"]' --timeout_ms 150000
unity command run_script --project-path "$UNITY_PROJECT_PATH" --timeout 180 --format json -- \
  --file Tools/ModelImportWorkflow.cs --entry ModelImportWorkflow.CreateReviewScene --timeout_ms 150000
unity command run_script --project-path "$UNITY_PROJECT_PATH" --timeout 180 --format json -- \
  --file Tools/ModelImportWorkflow.cs --entry ModelImportWorkflow.Validate --timeout_ms 150000
```

团队 clone 后模型已在 `Assets/`，不需要本地 Logs 文件或重新执行 `ImportSources`，直接打开已有审阅场景并运行 `Validate` 即可。脚本遇到未保存场景会停止；已有审阅场景只打开，不覆盖编辑。

Prefab 根节点保持单位缩放，子节点负责按身高缩放、横向居中与脚底归零。约定 1 Unity 单位 = 1 米，模型面向 +Z；审阅场景实例绕 Y 轴旋转 180° 面向位于 -Z 的相机。保存 `.meta` 保持 GUID 稳定，移动资产使用 Unity AssetDatabase。

材质使用 `Shader Graphs/glTF-pbrMetallicRoughness`，已验证当前 URP 支持。glTF metallic/roughness 通道由导入器解释，不把 roughness 原图直接塞入 URP smoothness。需要换自定义 Shader 时，单独验证色彩空间、法线方向及通道转换。

## 3. 验证流程与本次结果

1. `Validate` 检查实际三角面、1 材质、3 张 4K 纹理、Shader 支持、Prefab/EnemyDefinition 引用、1.8 / 2.1 m 高度和脚底 y=0。
2. 打开 `ModelImportReview.unity`，进入 Play 模式，确认 `Time.frameCount` 确实推进，再截取 1920 × 1080 的相机画面。本轮计数从 2055 到 2068，检查 met=true。
3. 观察缺材质的粉色、错法线、悬空、轮廓破损、铜/布/皮革分区。当前正面视角可正常展示；复杂动作、背面遮挡及目标设备性能仍需后续专项验收。
4. 退出 Play 后恢复临时 `Application.runInBackground` 设置。
5. 运行现有测试：31/31 通过，无编译失败。低模导入之后无新增控制台错误。

```sh
unity command set_autotick --project-path "$UNITY_PROJECT_PATH" -- --persist false
# 当前 Pipeline 无 editor_set_run_in_background；先记录原值，再临时开启
unity command eval --project-path "$UNITY_PROJECT_PATH" -- --code 'return UnityEngine.Application.runInBackground;'
unity command eval --project-path "$UNITY_PROJECT_PATH" -- --code 'UnityEngine.Application.runInBackground=true;'
unity command editor_play --project-path "$UNITY_PROJECT_PATH"
unity status --until-ready --project-path "$UNITY_PROJECT_PATH" --format json
unity command wait_for --project-path "$UNITY_PROJECT_PATH" --format json -- \
  --condition '{"member":"UnityEngine.Time.frameCount","op":"changed"}' --timeout_s 10
unity command capture_game_view --project-path "$UNITY_PROJECT_PATH" --format json -- \
  --source camera --width 1920 --height 1080 --save_path Assets/Logs/AssetDesign/review.png
unity command editor_stop --project-path "$UNITY_PROJECT_PATH"
# 本次原值为 false；其他机器恢复其先前值
unity command eval --project-path "$UNITY_PROJECT_PATH" -- --code 'UnityEngine.Application.runInBackground=false;'
unity command run_tests --project-path "$UNITY_PROJECT_PATH" --timeout 180 --format json -- \
  --mode editor --filter UnsoundedSea.Tests --timeout 150
```

本次遇到并处理的工具问题：

- 安装包时 Unity SceneView 内部出现一次 `ArgumentNullException`，之后编译成功、导入与测试通过。保留为工具异常记录，不算模型错误。
- 清理临时高模时一次 `eval` 超过默认 5 秒；先查 Editor ready 和资产实际状态，确认删除已完成，没有重复删除。大操作改用 `run_script` 的明确超时。
- 当前版本 `capture_game_view` 将相对路径视为 Assets 下路径；用明确 `Assets/Logs/...`，将所需截图归档到 `Docs/ArtReferences/` 后，经 AssetDatabase 删除临时截图资产。

## 4. 性能和制作边界

6 万 / 9 万是本轮桌面近景 LOD0 的起点，适合先验证视觉，不是所有设备、所有屏幕占比的通用目标。尚未生成 LOD1/LOD2，也未配置 LODGroup。

本次 glTFast 内嵌贴图实际为 ARGB32、13 级 mip，尚未做 GPU 压缩。每角色三张 4K 完整 mip 链理论约 256 MiB，两角色约 512 MiB，仅为纹理数据估算，不是 Profiler 实测。GLB 文件变小不等于显存按比例下降。正式投放需导出/重映射纹理为可配置的 TextureImporter 资产，按目标平台设置压缩及最大尺寸，并对比法线、粗糙度和铜材质后测显存与帧耗；不能声称内嵌贴图已有普通 TextureImporter 的平台覆写。

动画角色还需针对肩肘、袖口和膝盖重拓扑、分件、绑定与变形检查。当前袖中刀/铃、Boss 独立铜铃等机制部件没有制作完成。包围盒验收不代表可动画，也不代表成品美术已验收。

## 5. Git LFS 与协作

`.gitattributes` 已为 `*.glb`、`*.gltf`、`*.bin` 配置 LFS，现有图片/FBX 规则沿用。GLB 和预览图存大文件对象；`.meta`、Prefab、Scene、配置和 Markdown 保持普通 Git 文本。禁止提交 Library/Temp/Logs，也不需要重写历史。

每位协作者先安装 Git LFS，再执行：

```sh
git lfs install --local
git fetch origin
git switch dev/asset-mechanism-design
git lfs pull
git lfs ls-files
git lfs fsck
```

首次 clone 可直接 `git clone --branch dev/asset-mechanism-design <仓库地址>`，然后 `git lfs pull`。只想拉运行时模型时可用 `git lfs pull --include='Assets/UnsoundedSea/Art/Models/*.glb'`，高模不必进入每个人的 Unity 工作集。

提交时按阶段保留包配置、母版、Unity 低模接入、文档验收四个边界。先检查 `git check-attr filter -- <模型路径>` 返回 lfs，再暂存模型与对应 `.meta`。`git show :<模型路径>` 应显示 LFS pointer 的 oid/size，而不是二进制。

```sh
git lfs fsck
git push -u origin dev/asset-mechanism-design
```

正常 push 的 pre-push hook 同步 LFS 对象。验证应包含一次独立临时 clone 的远端 LFS fetch，并对四份 GLB 的 SHA-256 与 manifest 比对，而不只检查本地指针。
