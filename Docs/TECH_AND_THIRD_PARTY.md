# 《未测之海》技术文档与第三方资源说明

版本日期：2026-10-18。本文面向提交审阅，概述技术方案并逐项说明项目使用的外部资源及其授权。项目简介与功能见 [项目说明文档](PROJECT_README.md)；接口与扩展契约的详细说明以 [框架指南](FRAMEWORK.md) 为准，测试记录见 [验证记录](FRAMEWORK_VALIDATION.md)。

## 一、技术文档

### 1.1 开发环境

| 项目 | 说明 |
| --- | --- |
| 引擎 | Unity 6000.6.5f1，通用渲染管线 URP 17.6 |
| 语言 | C#（游戏与编辑器）；Python（音频合成、Blender 绑定与资产检查脚本） |
| 界面 | UI Toolkit（UXML／USS）与运行时中文字体图集 |
| 输入 | Unity Input System |
| 目标平台 | Windows x64（Mono）、macOS |
| 版本管理 | Git + Git LFS（模型、贴图、音频等二进制） |
| 外部工具 | Blender 5.2（分件骨骼绑定，`Tools/*Rig*.py`）、ffmpeg 与 numpy／scipy／soundfile（音频构建） |

### 1.2 代码架构

```text
UnsoundedSea.Core        纯 C# 规则层，不引用 UnityEngine
    ↑
UnsoundedSea.Runtime     ScriptableObject 内容、3D 战斗与航海表现、UI、音频、文件存档
    ↑
UnsoundedSea.Editor      内容校验、场景与资产的幂等生成工具

UnsoundedSea.Tests.EditMode   NUnit 行为测试（引用 Core / Runtime）
```

- **规则与表现分离**：`BattleSession` 是战斗命令入口与回合状态机（`TurnStart → IntentLocked → PlayerTurn → Resolve → TurnEnd`），UI 只读取 `BattleView` 只读快照；敌人的真实行动只保存在会话私有状态中，避免界面泄露隐藏信息。
- **确定性**：抽牌与敌人行动使用分离的固定种子随机序列，调查不访问随机数；规则层不读取场景、时间或全局单例，依赖通过构造函数注入。
- **内容驱动**：卡牌、敌人、遭遇以 `CardCatalog`／ScriptableObject 定义，`EffectRegistry` 提供自定义效果扩展点，`IEncounterObjective` 支持击退、封线、存活等目标。
- **航程**：`VoyageMapGenerator` 生成有向无环分支海图，`VoyageSession` 管理三幕航程、奖励、整备、船员与物资。
- **存档与回放**：存档记录内容 SHA-256 指纹、初始种子与成功命令序列，恢复时从头重放；先写临时文件再原子替换并保留 `.bak`，损坏或旧版本存档明确拒绝而不覆盖。
- **3D 表现**：`BattleDirector`／`BattleScenario` 组合战斗舞台；巨兽 Boss 使用 Blender 分件骨骼与刚体动画，海面使用 Uber Stylized Water 与平面反射。
- **音频**：`SeaAudio` 跨场景管理 29 个声部，三分轨配乐以 DSP 时钟同步并交叉淡化，音乐／环境／音效分类调节音量。

### 1.3 资产生产流程

- **3D 模型**：Hyper3D Rodin 生成（MCP 或 Deemos Agent）→ 减面至运行时预算 → BANG 分件与 Blender 绑定 → Unity CLI 导入 → LFS 归档。高模母版存放于 `ArtSource/`，运行时模型在 `Assets/`。详见 [模型全流程](MODEL_IMPORT_WORKFLOW.md)。
- **2D 插画**：按统一风格提示词生成无文字底图，卡名、费用、规则文字全部由代码渲染。
- **音频**：`Tools/build_audio.py` 合成原创配乐与音效并处理 CC0 素材，`Tools/check_audio.py` 做离线检查，`Tools/AudioSetup.cs` 写入 Unity 导入设置。
- **可追溯性**：每类资产的来源、提示词、生成模型、文件哈希与实测规格记录在 `ArtSource/**/manifest.json` 中。

### 1.4 测试与发布

- EditMode 测试 28 个文件，覆盖回合规则、调查与知识、Boss 目标、航程推进、存档回放等；Unity 菜单 **Unsounded Sea → Validate All Encounters** 检查内容配置。
- `Tools/*QA.cs` 提供界面、音频、航海等编辑器内自动验收脚本。
- 推送 tag 时，[发布工作流](../.github/workflows/release.yml) 在 GitHub Actions 上构建 Windows x64 与 macOS 版本并发布 Release。

## 二、第三方资源说明

### 2.1 代码与着色器

| 资源 | 用途 | 来源／作者 | 授权 | 位置 |
| --- | --- | --- | --- | --- |
| Uber Stylized Water | 海面着色器、材质编辑器界面、平面反射 | MatrixRex | MIT | `Assets/ThirdParty/UberStylizedWater/`（附 `LICENSE.txt`） |
| URP ShaderGraph Custom Lighting | 上述水体着色器内含的自定义光照函数 | Cyanilux | MIT | `Assets/ThirdParty/UberStylizedWater/Third Party/URP_ShaderGraphCustomLighting-main/`（附 `LICENSE`） |
| Unity 官方包 | URP、Input System、UI Toolkit、Timeline、Test Framework、glTFast（GLB 导入）、AI Navigation 等，版本见 `Packages/manifest.json` | Unity Technologies | Unity Companion License；glTFast 为 Apache-2.0 | Unity Package Manager |
| GameCI CLI v0.1.72 | CI 中激活 Unity Personal 授权 | GameCI | MIT | 仅用于构建流程，不进入游戏包 |
| GitHub Actions 官方动作 | `actions/checkout`、`upload-artifact`、`download-artifact` | GitHub | MIT | 仅用于构建流程 |

### 2.2 音频素材

以下素材均为 CC0 1.0（公有领域贡献），允许商用、修改，无需署名；项目仍保留署名与原始许可文件。素材经重采样、剪辑、滤波、增益调整或混合后使用，下载地址与文件哈希见 [音频 manifest](../ArtSource/Audio/manifest.json)。

| 素材包 | 作者 | 来源 | 位置 |
| --- | --- | --- | --- |
| Impact Sounds 1.0 | Kenney | <https://kenney.nl/assets/impact-sounds> | `ArtSource/Audio/Sources/kenney-impact/` |
| RPG Audio | Kenney | <https://kenney.nl/assets/rpg-audio> | `ArtSource/Audio/Sources/kenney-rpg/` |
| Water Waves | transitking 录制，qubodup 剪辑 | <https://opengameart.org/content/water-waves>（原始录音 <https://freesound.org/people/transitking/sounds/11505/>） | `ArtSource/Audio/Sources/water-waves/` |

配乐、枪声、巨兽声层、风声与尾流纹理、界面反馈音为团队原创合成，不属于 CC0 素材，汇总说明见 [CREDITS.txt](../ArtSource/Audio/CREDITS.txt)。

### 2.3 字体

| 资源 | 用途 | 来源 | 授权 | 位置 |
| --- | --- | --- | --- | --- |
| 香萃积雪宋（XIANGCUIJIXUESONG）Version 1.000 | 全部界面中文文字 | 香萃字体 | SIL Open Font License 1.1：可免费商用、嵌入游戏并随游戏分发；不得单独售卖字体文件，分发时须附带版权声明与许可证全文 | `Assets/Fonts/default_zh.ttf`（游戏内使用由其生成的字形图集 `default_zh.asset`） |

### 2.4 AI 生成工具及其产出

以下内容由团队撰写提示词、筛选结果并做后期整合，生成记录逐项存于对应 manifest。生成内容的使用权以各平台用户协议为准，提交前请对照当前账号的服务条款确认。

| 工具／服务 | 产出 | 记录 |
| --- | --- | --- |
| OpenAI 图像生成（gpt-image-2，经 Codex 调用） | 90 张卡面插画（1024×1536）、4 张补给场景插画（1536×1024）、游戏图标 | [卡牌 manifest](../ArtSource/Cards/manifest.json)、[补给插画 manifest](../ArtSource/SupportArt/manifest.json)、[品牌 manifest](../ArtSource/Branding/manifest.json) |
| Canva AI（generate_image） | 图标备选方案“深渊之眼”（未接入游戏） | [图标 manifest](../ArtSource/Branding/icon-manifest.json) |
| Hyper3D Rodin（Deemos，含 Agent／API／BANG 分件／自动绑定） | 主角、25 个普通敌人、Boss 与守卫、船只、航海地标 3D 模型 | `ArtSource/Characters/`、`ArtSource/Bosses/`、`ArtSource/Landmarks/` 下 manifest |
| AI 编程助手 | 辅助编写与审阅代码、文档 | Git 提交记录 |

其中无星守卫等部分模型在 Hyper3D 以公开模式生成，模型在该平台公开可见；详见 [模型全流程](MODEL_IMPORT_WORKFLOW.md)。

### 2.5 待确认事项

1. 将香萃积雪宋的 OFL-1.1 许可证全文（`OFL.txt`）放入 `Assets/Fonts/` 并随发布包附带。
2. Hyper3D Rodin、OpenAI、Canva 当前账号套餐下生成内容的商用与署名要求。
