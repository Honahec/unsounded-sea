# 框架与战斗 HUD 验证记录

验证日期：2026-10-16。环境：macOS、Unity 6000.6.5f1、Unity CLI 1.0.0-beta.13、Pipeline 0.8.0-exp.1。

## 美术指导与菜单（当前）

在已连接 Editor 中通过 Unity CLI 验证，Game View 固定为 1280×720 与 1920×1080。按钮操作通过面板拾取发送 UI Toolkit Pointer/Key/Navigation 事件（Input System 的 `simulate_pointer`/`simulate_key` 在本环境未到达 UI Toolkit）；悬停伪类无法用合成事件驱动，悬停外观只做了代码审查，焦点外观已截图确认。

- EditMode 回归 **30 通过 / 0 失败**；最终 Play 运行 Console 错误 0、警告 0；所有 HUD/菜单/内容字符串均在字体字形内。
- 中央场景点 `panel.Pick` 为空；名牌、工具、手牌、按钮均可被拾取。
- 提灯：意图变为「已确认」，次数 1 → 0、行动不变，工具显示「已用尽」。听筒：只增加「调查所得 · 声源」，意图仍为「未确认」。
- 出牌：左轮 30 → 24、寻找掩体两次格挡 20；行动 0 时其余手牌显示「行动不足」且焦点落到结束回合；结束回合后挥刃被格挡完全抵消。
- J 打开档案、Esc 收起；档案不再遮挡名牌。
- 封门：每回合一次「绘制封线」，使用后显示「本回合已绘制」；第 6 回合 4/4 进入 Victory，敌人仍 999/999。
- 菜单：标题 → 设置（音量 80% → 70%、减少动效生效，恢复默认还原）→ Esc → 开始调查进入 Battle → Esc 暂停（遮罩拦截结束回合，回合保持 1）→ 设置 → 返回标题 → 退出（Editor 中结束 Play）。
- 截图（Logs 被 Git 忽略）：`Logs/HudRedesign/final_battle_1280.png`、`v9_1080.png`、`v3_confirmed.png`、`v6_partial_focus.png`、`v4_unaffordable.png`、`v5_archive.png`、`v8_seal_victory.png`、`final_title_1080.png`、`m2_settings.png`、`m3_pause.png`、`m4_pause_settings.png`。
- 未验证：独立 Player 打包中的全屏切换与 `Application.Quit`、真实鼠标悬停、非 16:9 分辨率、多敌人遭遇的名牌排布（现有遭遇均为单敌）。

## 游戏内 HUD 第一版（历史）

旧测试面板已替换为 `BattleHud`，入口为 `Assets/UnsoundedSea/Scenes/Battle.unity`。底层规则和存档接口保留；场景切换、种子和保存/读取调试按钮不进入游戏 HUD。

- EditMode 回归：**30 通过 / 0 失败 / 0 跳过**。删除旧场景引用布线测试，不用新 HUD 的字段/默认值重新绑定该测试。
- Play 模式真实按钮链路：提灯确认意图、次数归零且不扣行动；左轮使敌人生命 30 → 24，行动 3 → 2，手牌 5 → 4；结束回合后行动恢复 3，意图重新隐藏。
- 档案支持按需展开；通过聚焦后的导航提交事件验证展开，避免把焦点仍在其他按钮时的测试输入当作目标按钮操作。
- 封门按钮跨四个真实布局帧执行，第四回合达到 4/4 并进入 Victory；敌人仍为 999 生命，玩家 52 生命 / 22 理智。
- 1280×720 实际 Game View 已检查：资源在左上、目标在顶部、情报在左侧、工具在右侧、手牌在底部、回合控制在右下，档案默认关闭。
- HUD 根背景透明；中央区域 `panel.Pick` 返回空，不吞掉场景指针。全部当前显示文字可由项目字体覆盖，无缺失字形；字体来源为 `Assets/Fonts/default_zh.ttf`。
- 本轮不制作 3D 美术，场景仅包含相机和 HUD；不把中央空白误记为已完成的敌人/场景表现。
- 重编译期间一次测试请求因连接中断未执行；确认编辑器恢复且编译失败标记为 false 后，重新执行的完整回归通过。

最终从磁盘重开战斗场景后再次进入 Play，确认字体、样式与 PanelSettings 引用有效，档案展开/收起正常，当前 Console 错误与警告均为 0。截图证据：`Logs/Framework/battle-hud.png`、`battle-hud-journal.png`、`battle-hud-victory.png`；截图不作为游戏资产导入。

以下为重设计前测试场的历史验证，不代表旧测试 UI 仍保留。

## 自动验证

通过 Unity CLI 调用运行中编辑器的 EditMode Test Runner，结果 **31 通过 / 0 失败 / 0 跳过**。

覆盖范围：

- 5 张起手 / 3 行动、意图锁定、调查不改变当前或后续行动序列。
- 听筒只暴露声源、提灯无法观察时不扣次数、回合情报失效。
- 无效操作零扣费、主动理智支付绕过防护、生命/理智耗尽失败。
- 抽弃牌守恒、实例 ID 唯一、抽牌卡不能即时抽回自身、消耗牌不洗回。
- 格挡与理智防护、打断、击杀、四次封门、存活目标。
- 回放恢复后继续操作与原会话一致；内容/版本/历史不匹配时拒绝读取。
- JSON 文件往返和旧文件备份、外部不可修改已记录命令。
- 地图分支约束、环和悬空边检查、知识可信度不可降级。
- 四组实际 Encounter 资产可创建隔离会话；从磁盘加载验证场景后所有配置和 PanelSettings 引用有效。

本地详细报告：`Logs/Framework/editmode-results.json`（Logs 被 Git 忽略，重新运行测试可再生成）。

## Play 模式验证

全部通过 Unity CLI 操作本工程 Editor；使用 `wait_for(Time.frameCount changed)` 证明玩家循环持续推进。按钮验证使用 UI Toolkit PointerDown/PointerUp 事件，经过实际按钮回调到 Session；未声称覆盖所有真实设备输入。

| 场景 | 结果 |
| --- | --- |
| 启动验证场 | 中文标签、资源、五张手牌、征兆和日志显示正常 |
| 蒙面祷告者 → 提灯 → 左轮射击 | 意图确认，工具剩余 0，敌人生命 30 → 24，行动 3 → 2 |
| 保存 → 结束回合 → 读取 | 从回合 2 恢复至回合 1，行动 2、敌人生命 24、已确认意图和工具 0 次均正确 |
| 封门场景 → 每回合一次绘制封线 | 第 4 回合完成 4/4，进入 Victory；敌人生命仍 999，玩家生命 52 / 理智 22；无需调查或伤害 |
| 最终 Console 检查 | 编译失败标记 false；运行时错误 0、警告 0 |

本地画面证据：`Logs/Framework/investigation.png`、`Logs/Framework/seal-victory.png`。

## 验证边界

已验证规则与编辑器内沙盒；未执行独立 Player 打包、目标设备测试或完整航程试玩。UI 检查使用 Toolkit 指针事件，未覆盖所有设备输入；正式发行仍需提供已授权中文字体。既有修复过程保留在 Git 历史。

重跑命令见 [框架指南](FRAMEWORK.md)；模型审阅属于独立范围，见 [模型全流程](MODEL_IMPORT_WORKFLOW.md)。新规则提案不能引用本文作为已通过证明。
