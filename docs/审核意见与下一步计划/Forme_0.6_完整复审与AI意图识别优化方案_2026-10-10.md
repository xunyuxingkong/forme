# Forme 0.6 完整复审与 AI 意图识别优化方案

> 项目：`https://github.com/xunyuxingkong/forme/`  
> 分支：`main`  
> 最新审核提交：`aa521b45a8bab128034df477d7f35782f68890fb`  
> 提交说明：`feat: expand 0.6 content and improve chat presentation`  
> 当前版本：`0.6.0`  
> 审核日期：2026-10-10  
> 文档性质：代码复审 / 架构优化 / AI 意图识别改造 / 产品玩法优化 / 发布前验收计划

---

# 1. 文档目标

本次文档在前几轮审核基础上，重新以当前 `0.6.0` 代码为基线进行整理。

重点不再是简单增加功能，而是解决以下核心问题：

1. 当前 AI 控制仍然过度依赖“规则文件中的固定文字匹配”；
2. 用户说法稍有变化，即使动作语义完全一样，也可能无法执行；
3. AI 可能在文字上承诺动作，但程序没有真正执行；
4. 场景目标、动作、权限和结果反馈仍缺少统一语义层；
5. 当前系统数量已经比较丰富，但玩法之间还需要形成更自然的闭环；
6. 0.6.0 虽然 CI 已经稳定，但正式发布前仍缺少 WPF Smoke、长时性能、安装升级和多 DPI 等完整验收。

本轮最重要的架构目标是：

> **让 Forme 从“匹配用户说了哪句话”升级为“理解用户想做什么”。**

也就是从：

```text
文字短语匹配
    ↓
Command
```

升级为：

```text
自然语言
    ↓
Intent
    ↓
Target
    ↓
ActionPlan
    ↓
执行
    ↓
ActionResult
```

---

# 2. 当前项目状态

## 2.1 最新工程状态

当前主分支已经进入 `0.6.0`。

GitHub Actions `Windows validation` 最新一次已经完整通过：

```text
Build                         ✅
Unit Tests                    ✅
Package                       ✅
Fatal crash diagnostics       ✅
Test artifact upload          ✅
Installer artifact upload     ✅
```

最新远程结果：

```text
MSTest:
57 passed
0 failed

Build:
0 warnings
0 errors

Installer:
Forme-Setup-0.6.0.exe
```

这意味着此前几个核心工程阻塞已经解决：

- `ActionRules.UpgradeDefaults()` 历史默认规则升级测试已修复；
- CI 不再硬编码 `.tools/dotnet/dotnet.exe`；
- Setup 锁定还原问题已经修复；
- 崩溃诊断步骤已能在 GitHub Hosted Runner 上正常执行；
- 安装器可正常构建并上传 CI artifact。

当前主分支已经从“工程基础不稳定”进入：

> **功能体验、自然交互和发布完整性优化阶段。**

---

# 3. 当前功能规模

当前 Forme 已经不再属于“功能很少”的阶段。

截至 0.6.0，核心内容大致包括：

| 模块 | 当前规模 |
|---|---:|
| 每日任务模板 | 14 |
| 家具 / 装饰 | 30 |
| 玩具 | 6 |
| 户外发现 | 16 |
| 成就 | 30 |
| 生活组合事件 | 20 |
| 装饰位置 | 10 |
| 宠物模型 | 4 |
| 核心单元测试 | 57 |

当前已经具备：

- 桌面宠物；
- 小屋与户外；
- 桌宠随机走动 / 奔跑 / 睡眠 / 舞蹈；
- 多宠物模型；
- AI 聊天；
- DeepSeek / SiliconFlow / Custom Endpoint；
- AI Memory / Chat Note；
- 本地动作规则；
- AI 动作协议；
- 点击寻路；
- 户外互动；
- 纸船玩法；
- 藏物玩法；
- 玩具互动；
- 家具摆放；
- 生活组合事件；
- XP / 等级；
- 星星；
- 每日任务；
- 收藏图鉴；
- 成就；
- 导入导出；
- 数据库迁移；
- GitHub CI；
- Installer；
- Crash Privacy Test。

因此：

> 当前项目的主要问题已经不是“功能数量不足”，而是“自然语言控制是否可靠、系统之间是否真正联动、内容是否持续有变化”。

---

# 4. 当前最核心的问题：本地动作依然偏“文字匹配”

当前 `ActionRules` 的工作方式仍然接近：

```text
用户输入
    ↓
Normalize
    ↓
和 Phrases 做精确匹配
    ↓
匹配成功
    → CompanionCommand

匹配失败
    → 交给 AI
```

例如规则文件里已经写有：

```text
走两步
走几步
散散步
```

所以这些表达可以识别。

但下面这些用户表达虽然含义几乎完全一样：

```text
出去溜达一下
活动活动
到附近走走
你自己转悠一会儿
在周围逛一下
别一直站着
```

当前本地规则就可能无法识别。

同样：

```text
去池塘边
去池塘旁边
走到水边
到木桥旁
去池子那里
```

这些都属于同一个意图：

```text
GoTo(Pond)
```

但目前系统需要依赖具体 Phrase，或者希望 AI 恰好输出正确 `forme-actions`。

这就是当前 AI 控制最需要改造的地方。

---

# 5. 为什么继续扩充 Phrase 文件不是正确方向

如果继续按照现有方式扩展，最终很容易变成：

```json
[
  "走两步",
  "走几步",
  "散散步",
  "溜达一下",
  "出去转转",
  "到处走走",
  "活动一下",
  "出去活动活动",
  "转悠一会儿",
  "随便逛逛"
]
```

问题是自然语言没有穷举边界。

用户还可以说：

```text
出去活动活动腿脚
别一直待着，转一圈吧
你自己随便晃悠会儿
在附近看看
四处走一下
```

所以：

> **Phrase List 永远只能覆盖部分说法。**

继续无限追加 Phrase 会带来：

- 规则文件膨胀；
- 重复短语越来越多；
- 容易出现冲突；
- 测试成本上升；
- 自定义规则越来越难维护；
- 新功能每加一个目标，就要再补大量自然语言变体；
- Prompt、Rules、Scene、Command 四处重复维护同一个语义。

因此新的设计原则应该是：

> **规则文件用于“快捷命中”和“用户自定义”，不能承担完整 NLP。**

---

# 6. 核心改造：从文字匹配升级为“意图识别”

## 6.1 总体结构

推荐改成：

```text
用户自然语言
        ↓
┌───────────────────────────┐
│ 1. Fast Path             │
│ ActionRules 高频确定短语  │
└───────────────────────────┘
        ↓ 未命中
┌───────────────────────────┐
│ 2. Local Intent Resolver │
│ 动作词 + 目标词 + 否定等 │
└───────────────────────────┘
        ↓ 仍不确定
┌───────────────────────────┐
│ 3. AI Intent Fallback    │
│ AI 只做结构化意图识别     │
└───────────────────────────┘
        ↓
CompanionIntent
        ↓
TargetResolver
        ↓
ActionPlanner
        ↓
Preflight
        ↓
Executor
        ↓
ActionResult
        ↓
UI 状态 / AI 回复
```

这套架构的重点是：

> AI 只负责“理解用户想干什么”，程序负责“决定怎么执行、能不能执行、是否成功”。

---

# 7. 第一层：保留 ActionRules，但降级为 Fast Path

现有 ActionRules 不需要删除。

它仍然非常有价值，适合处理：

```text
停下
回来
睡觉
跳舞
关灯
浇水
去户外
回小屋
```

优点：

```text
0 Token
0 网络
极低延迟
100% 可预测
```

因此建议将它重新定义为：

> **Fast Path + 用户自定义规则系统**

而不是：

> **主自然语言理解系统**

这能够保留当前已有能力，也不会产生一次性大重构风险。

---

# 8. 第二层：本地 Intent Resolver

这是本轮最值得开发的核心模块。

不再保存整句话，而是分别识别：

```text
动作语义
目标语义
方式
数量
方向
否定
讨论语气
条件
```

例如：

## 8.1 动作词

```text
走
走走
散步
溜达
逛
转悠
活动
```

统一：

```text
Intent = Stroll
```

## 8.2 移动目标词

```text
池塘
池塘边
水池
水边
木桥
池子
```

统一：

```text
Target = Pond
```

## 8.3 书架目标

```text
书架
书柜
书架边
书架那里
看书的地方
```

统一：

```text
Target = BookShelf
```

## 8.4 组合结果

用户输入：

```text
你去池塘边转悠一下吧
```

可以解析为：

```text
Intent = GoTo
Target = Pond
Motion = Walk
```

用户输入：

```text
走到水边去
```

仍然：

```text
Intent = GoTo
Target = Pond
```

用户输入：

```text
去书柜那边待一会
```

解析：

```text
Intent = GoTo
Target = BookShelf
```

不需要完整句子存在于 Rule JSON。

---

# 9. 统一 CompanionIntent

建议增加真正的语义对象。

```csharp
public enum CompanionIntentType
{
    GoTo,
    Interact,
    Stroll,
    Dance,
    Idle,
    Recall,
    Stop,
    ChangeScene,
    ChangeWeather,
    UseToy,
    StartPlay,
    ChangeModel,
    ToggleDevice
}
```

对应统一对象：

```csharp
public sealed record CompanionIntent
{
    public CompanionIntentType Type { get; init; }
    public string? Target { get; init; }
    public string? Mode { get; init; }
    public int? Count { get; init; }
    public double Confidence { get; init; }
    public string Source { get; init; } = "local";
}
```

例如：

```json
{
  "type": "GoTo",
  "target": "pond",
  "mode": "walk",
  "confidence": 0.96,
  "source": "local"
}
```

---

# 10. 统一 TargetCatalog

当前一个重要问题是：

```text
go:book
go:fish
```

和：

```text
interact:pond
interact:picnic
```

是不同的命令语义。

用户自然语言却不会区分：

```text
“去池塘”
```

和：

```text
“和池塘互动”
```

因此必须把“动作”和“目标”拆开。

建议建立：

```csharp
public sealed record CompanionTarget(
    string Id,
    SceneKind Scene,
    IReadOnlyList<string> Aliases,
    TargetCapabilities Capabilities
);
```

示例：

```text
Target: pond
Scene: outdoor

Aliases:
  池塘
  池塘边
  水边
  水池
  木桥
  池子旁

Capabilities:
  GoTo
  LookAt
  Interact
```

这样：

```text
GoTo(Pond)
Interact(Pond)
LookAt(Pond)
```

语义非常明确。

---

# 11. TargetCatalog 应成为场景目标唯一来源

当前不应该继续出现：

```text
Prompt 维护一份目标
ActionRules 维护一份目标
SceneDetails 维护一份坐标
CompanionCommands 又维护一份白名单
```

推荐：

```text
TargetCatalog
    ↓
ActionRules
AI Instructions
TargetResolver
Scene Navigation
Validation
Tests
```

统一引用。

例如 `pond` 只在一个地方定义：

```text
Scene = Outdoor
ApproachPoint = (-3.7, -2.8)
Aliases = [...]
Capabilities = [...]
RequiredPermission = ...
```

避免以后新增：

```text
风铃
凉亭
野餐毯
花园
书架
床
鱼缸
```

时四处重复修改。

---

# 12. 第三层：AI 只作为“语义兜底”

本地 Intent Resolver 仍然不可能覆盖无限自然语言。

例如用户说：

```text
团团今天别一直待屋里了，去有水的地方待一会吧
```

本地词法规则未必足够稳定。

此时可以交给 AI。

但不要像现在这样：

```text
AI 正常聊天
    ↓
希望它顺便生成 forme-actions
```

推荐改成：

```text
AI Intent Classification
```

要求返回：

```json
{
  "intent": "go_to",
  "target": "pond"
}
```

而不是让 AI 返回：

```json
{
  "action": "move",
  "x": -3.7,
  "z": -2.8
}
```

坐标、路径和场景属于程序知识，不应该交给 AI 猜。

---

# 13. AI 不应该直接控制底层动作

## AI 负责

```text
理解用户语义
识别 intent
识别 target
识别简单 mode
```

## 程序负责

```text
场景切换
坐标
寻路
碰撞
权限
时序
取消
超时
真实执行结果
数据库变化
奖励
```

这样可以避免：

```text
AI 猜错坐标
AI 执行未授权动作
AI 说成功但程序失败
Prompt 中重复维护所有场景细节
```

---

# 14. ActionPlanner：把意图转换成执行计划

例如：

```text
Intent:
GoTo(Pond)
```

如果当前：

```text
Scene = indoor
```

Planner 自动生成：

```text
1. ChangeScene(outdoor)
2. MoveTo(Target.Pond)
```

如果已经在户外：

```text
1. MoveTo(Target.Pond)
```

如果用户说：

```text
去池塘边看看水
```

可计划：

```text
1. ChangeScene(outdoor)
2. MoveTo(Pond)
3. LookAt(Pond)
```

AI 不需要知道这些步骤。

---

# 15. 增加 Preflight

在真正执行前，统一检查整个计划。

例如用户：

```text
去池塘边
```

计划：

```text
ChangeScene(outdoor)
MoveTo(pond)
```

Preflight 应一次判断：

```text
是否允许场景控制？
是否允许宠物控制？
目标是否存在？
目标当前是否可用？
场景是否能打开？
导航是否有入口？
```

而不是：

```text
第一步执行成功
第二步才发现没有权限
```

避免出现：

```text
已经切户外
但没走过去
```

这种“半执行”状态。

---

# 16. ActionResult 必须结构化

当前部分逻辑还会通过中文字符串判断：

```text
对应动作权限未开启
超过30秒
打断
已停止
未执行
```

这种设计随着功能增多会越来越脆弱。

建议：

```csharp
public enum ActionResultCode
{
    Started,
    Completed,
    NotRecognized,
    Unauthorized,
    TargetUnavailable,
    SceneUnavailable,
    PathBlocked,
    Interrupted,
    Cancelled,
    Timeout,
    Failed
}
```

并统一返回：

```csharp
public sealed record ActionResult(
    ActionResultCode Code,
    string Action,
    string? Target,
    string UserMessage,
    TimeSpan Elapsed
);
```

---

# 17. UI 显示真实执行结果

用户真正关心的是：

```text
现在做了吗？
做到哪一步了？
有没有成功？
为什么没成功？
```

UI 可以显示：

```text
正在去池塘边…
```

完成：

```text
已经到池塘边啦
```

失败：

```text
没走成：前面的路被挡住了
```

未授权：

```text
没有执行：请先开启场景控制权限
```

取消：

```text
动作已经停止
```

不需要显示：

```text
forme-actions
JSON
internal report
```

---

# 18. 解决“AI 说了但没做”的关键机制

当前最影响体验的问题之一是：

```text
AI：
“好呀，我去池塘边。”
```

但：

```text
实际 Commands = []
```

这时程序必须明确知道：

```text
IntentDetected = true
ActionExecuted = false
```

因此规则：

> 只要模型正文包含动作承诺，但没有对应合法意图 / 命令，就不能显示“动作已完成”的语气。

更好的方式：

```text
用户动作请求
    ↓
IntentResolver 先识别
    ↓
程序执行
    ↓
拿到 ActionResult
    ↓
AI / UI 再生成最终自然语言反馈
```

例如：

```text
ActionResult = Completed
```

才说：

```text
我已经到池塘边啦。
```

如果：

```text
ActionResult = PathBlocked
```

则：

```text
我没走过去，前面的路被挡住了。
```

---

# 19. 是否需要第二次 AI 请求

理论上可以：

```text
第一次 AI：
解析意图

程序执行

第二次 AI：
根据结果组织回复
```

但这会增加：

```text
成本
延迟
网络依赖
```

所以推荐：

## 默认

由本地模板生成结果文字：

```text
Completed → 已经到了
Blocked → 没走成
Unauthorized → 没执行
```

## 可选增强

只在用户明确启用：

```text
自然 AI 动作反馈
```

时，再进行第二次 AI 请求。

这样不会强制把一次操作变成两次 API 调用。

---

# 20. IntentResolver 第一阶段不需要大模型

第一版完全可以使用轻量解析。

建议：

```text
IntentLexicon
TargetLexicon
NegationDetector
DiscussionDetector
QuantityParser
IntentResolver
```

---

# 21. IntentLexicon

例如：

```text
GoTo:
  去
  到
  走到
  过去
  前往
  去找
  到那边

Stroll:
  走
  散步
  溜达
  转悠
  逛
  活动

Dance:
  跳舞
  舞
  转圈
  蹦

Stop:
  停
  停下
  别动
  不要动
```

注意：

> 不再保存完整句子，而是保存语义片段。

---

# 22. TargetLexicon

例如：

```text
pond:
  池塘
  水池
  水边
  木桥
  池子

book:
  书架
  书柜
  看书的地方

fish:
  鱼缸
  小鱼
  看鱼的地方

rest:
  凉亭
  亭子
  休息亭

picnic:
  野餐毯
  野餐区
```

然后组合：

```text
去 + 池塘边
走到 + 水边
过去 + 木桥
```

全部得到：

```text
GoTo(Pond)
```

---

# 23. 必须统一处理否定

本地解析一定不能只找关键词。

否则：

```text
别去池塘
```

可能被误识别为：

```text
GoTo(Pond)
```

建议建立：

```text
NegationDetector
```

识别：

```text
不要
别
别去
不用
不要动
停止
取消
```

---

# 24. 必须识别“讨论动作”而不是执行动作

例如：

```text
“去池塘边”是什么意思？
你能不能解释一下“绕屏幕跑一圈”？
刚才你为什么没有去池塘？
如果我让你跳舞会怎样？
```

这些不是操作指令。

应建立：

```text
DiscussionDetector
```

识别：

```text
什么意思
解释
分析
为什么
是否
如果
假如
上次
刚才
引用
他说
功能
支持
```

当前 `ScreenLap()` 已经存在部分这种思路，可以抽成统一模块，不应该只用于“跑一圈”。

---

# 25. Intent Confidence

建议本地 IntentResolver 给出 confidence。

例如：

```text
“去池塘边”
confidence = 0.99

“去水边看看”
confidence = 0.93

“今天想出去透透气”
confidence = 0.55
```

策略：

```text
>= 0.85
    → 本地直接执行

0.55 ~ 0.85
    → AI 意图兜底

< 0.55
    → 按普通聊天处理
```

这样可以减少误执行。

---

# 26. 自定义规则仍然值得保留

用户自定义规则可以继续存在。

例如用户自定义：

```text
“回窝”
→ GoTo(Sleep)
```

但是内部不建议再保存：

```text
Phrase → Command
```

长期可以升级成：

```text
Phrase → Intent
```

例如：

```json
{
  "phrase": "回窝",
  "intent": {
    "type": "GoTo",
    "target": "sleep"
  }
}
```

这样用户规则也跟新架构兼容。

---

# 27. AI Intent Fallback 的安全格式

如果走 AI 兜底，要求输出严格 JSON：

```json
{
  "type": "go_to",
  "target": "pond",
  "mode": "walk"
}
```

不允许 AI 输出：

```text
PowerShell
文件路径
URL
操作系统命令
任意坐标
SQL
代码
```

Target 和 Intent 必须来自本地白名单。

---

# 28. 原生 Tool Calling 可以后续适配，但不是前置条件

当前系统采用正文中的 `forme-actions` 结构化块作为协议。

这套机制可以继续兼容。

未来可针对支持 tools/tool_choice 的服务增加原生 Tool Calling，但不能假设所有兼容接口都支持。

推荐：

```text
ProviderCapabilities
```

例如：

```text
SupportsStreaming
SupportsTools
SupportsReasoningControl
```

如果支持 tools：

```text
AI → Tool Intent
```

如果不支持：

```text
AI → JSON Intent
```

两种最终都进入同一个：

```text
CompanionIntent
```

---

# 29. 推荐新增代码结构

```text
Forme.Core/
├── Intents/
│   ├── CompanionIntent.cs
│   ├── CompanionIntentType.cs
│   ├── IntentResolver.cs
│   ├── IntentLexicon.cs
│   ├── TargetCatalog.cs
│   ├── NegationDetector.cs
│   ├── DiscussionDetector.cs
│   └── IntentConfidence.cs
│
├── Actions/
│   ├── ActionPlan.cs
│   ├── ActionPlanner.cs
│   ├── ActionPreflight.cs
│   ├── ActionResult.cs
│   └── ActionResultCode.cs
```

App 层：

```text
Forme.App/
├── CompanionExecution/
│   ├── CompanionExecutor.cs
│   ├── SceneActionExecutor.cs
│   ├── PetActionExecutor.cs
│   └── PlayActionExecutor.cs
```

---

# 30. 推荐完整调用链

```text
UserMessage
    ↓
ActionRules Fast Path
    ↓ 未命中
Local IntentResolver
    ↓ 低置信度
AI Intent Resolver
    ↓
CompanionIntent
    ↓
TargetResolver
    ↓
ActionPlanner
    ↓
ActionPreflight
    ↓
CompanionExecutor
    ↓
ActionResult
    ↓
ChatDisplay / FloatingChat
```

这条链路应该成为以后所有宠物控制的唯一入口。

---

# 31. 示例一：去池塘边

用户：

```text
团团你去水边溜达一下吧
```

解析：

```text
GoTo
Target = Pond
Mode = Walk
```

当前在室内：

```text
ActionPlan:
1. Scene(outdoor)
2. MoveTo(Pond)
```

执行：

```text
Scene → success
Move → success
```

返回：

```text
Completed
```

UI：

```text
已经到池塘边啦。
```

---

# 32. 示例二：否定

用户：

```text
别去池塘
```

解析：

```text
Negated = true
```

不执行。

按聊天回答：

```text
好，我不去。
```

---

# 33. 示例三：讨论

用户：

```text
“去池塘边”这个指令支持吗？
```

解析：

```text
Discussion = true
```

不能触发移动。

---

# 34. 示例四：权限不足

用户：

```text
去池塘边
```

解析成功。

Preflight：

```text
SceneControl = false
```

返回：

```text
Unauthorized
```

UI：

```text
没有执行：需要先开启场景控制权限。
```

---

# 35. 示例五：路径受阻

用户：

```text
去书架旁
```

Planner：

```text
MoveTo(BookShelf)
```

寻路失败：

```text
PathBlocked
```

UI：

```text
没走成：书架附近没有可达位置。
```

不能回答：

```text
“我已经过去啦”
```

---

# 36. 当前 AI 控制以外的主要优化空间

下面部分整理当前最新提交中其他仍值得处理的问题。

---

# 37. ActionRules 版本升级机制仍然脆弱

当前虽然已经修好此前测试失败，但 `UpgradeDefaults()` 仍通过完整 `Serialize()` 与多个历史默认 JSON 快照比较。

现在已有：

```text
previous
earlier
spinPrevious
```

未来可能继续出现：

```text
previous4
previous5
previous6
```

建议引入：

```text
SchemaVersion
BuiltinRevision
```

例如：

```json
{
  "schemaVersion": 1,
  "builtinRevision": 4
}
```

职责：

```text
SchemaVersion
→ JSON 结构版本

BuiltinRevision
→ 官方默认规则版本
```

用户自定义规则只做兼容迁移，不做全量覆盖。

---

# 38. 每日任务虽然有 14 个，但轮换方式仍然重复

当前：

```text
rotation = day.DayNumber % Count
```

每天拿连续三个：

```text
今天：
A B C

明天：
B C D

后天：
C D E
```

所以连续两天会重复 2/3。

建议改成：

```text
日期确定性随机
```

但加入约束：

```text
每天 3 个
同一个 Action 不重复
和昨天最多重复 1 个
尽量包含不同玩法类型
```

例如：

```text
陪伴类
探索类
实用类
```

这样仍然完全本地、可复现、可测试，但体验不会那么机械。

---

# 39. 等级系统目前“等级意义”仍偏弱

当前：

```text
每 100 XP 1 级
最高 Lv20
```

但物品购买主要判断：

```text
星星够不够
是否已经拥有
```

Level 对玩法影响有限。

建议：

```text
Lv2  新动作
Lv3  新家具系列
Lv4  新玩具
Lv5  新表情
Lv7  新灯光
Lv10 特殊户外装饰
Lv15 特殊待机动作
Lv20 纪念装饰
```

基础功能不要锁。

等级应该是：

```text
内容解锁
```

不是：

```text
功能门槛
```

---

# 40. 增加 ContentCatalogValidator

当前游戏内容越来越依赖 JSON：

```text
game-catalog.json
game-tasks.json
game-achievements.json
```

应建立统一内容校验。

检查：

```text
ID 唯一
Kind 合法
Price >= 0
MaxOwned 合法
Color = #RRGGBB
Visual 合法
Action 合法
Condition 合法
Rarity 合法
Task ID 唯一
Achievement ID 唯一
LifeEvent ID 唯一
所有引用目标存在
```

建议：

```text
测试启动时校验
应用启动时轻量校验
```

CI 发现内容错误直接失败。

---

# 41. 内容 ID 已经属于用户数据协议

当前备份里会持久化：

```text
item_id
discovery_id
slot item_id
achievement_id
```

因此从 0.6 开始：

> **内容 ID 不能随便改名。**

例如：

```text
lamp-paper
```

未来如果改成：

```text
paper-lamp
```

旧备份就可能无法通过验证。

规则：

```text
显示名可以改
ID 不改
```

确实需要变更时：

```text
ContentAlias
```

例如：

```text
lamp-paper → paper-lamp
```

导入或迁移时统一映射。

---

# 42. 户外发现系统需要防止快速刷空

现在发现系统已经支持：

```text
天气
季节
昼夜
稀有度
```

这是好的。

但不建议让用户连续点击就把可获得发现刷完。

建议增加：

```text
DiscoveryCooldown
```

但不要设计成惩罚型限制。

更好的方式：

第一次：

```text
获得收藏物
```

短时间内继续探索：

```text
蝴蝶飞过
树叶落下
风吹草地
宠物闻花
池塘冒泡
小鸟飞过
```

这些不一定给收藏，但让场景持续有反馈。

---

# 43. 生活事件应统一 EnvironmentContext

当前 `LifeRules.Evaluate()` 主要输入：

```text
weather
night
lamp
```

以后继续扩展：

```text
早晨
午后
黄昏
季节
温度
天气
```

不应该继续追加 bool。

建议：

```csharp
public sealed record EnvironmentContext
{
    public string Weather { get; init; }
    public int Hour { get; init; }
    public string Season { get; init; }
    public bool IsNight { get; init; }
    public bool LampOn { get; init; }
}
```

然后：

```text
LifeRules.Evaluate(world, context)
```

例如当前“晨间点心”实际上只判断 `clear && !night`，中午也可能触发。

有 Hour 以后，才能真正限定到清晨时段。

---

# 44. 宠物差异化还可以继续深化

当前四种宠物已经有：

```text
FavoriteFurniture
FavoriteToy
FavoriteActivity
ToyReaction
```

这是正确方向。

下一步建议增加：

```text
IdleWeights
FurniturePreference
DiscoveryReaction
SleepStyle
CelebrateStyle
WeatherPreference
WanderPreference
```

例如：

### 小猫

```text
更爱鱼缸
更爱羽毛
白天更喜欢窗边
```

### 狐狸

```text
更爱纸飞机
更容易探索
发现稀有物时转圈
```

### 企鹅

```text
更爱池塘
更爱飞盘
雨雪天气更活跃
```

### 芽芽

```text
更爱小球
更喜欢坐垫
更偏安静花园
```

这样：

> 换宠物 = 换行为个性，而不只是换模型皮肤。

---

# 45. WPF Smoke 状态需要统一

当前文档存在一次不一致：

一份记录说：

```text
完整 WPF Smoke 通过
```

另一份最新分析说：

```text
最近一次完整 WPF Smoke 非零退出
```

因此发布判断应该以最保守原则：

> **WPF Smoke 仍需重新明确验证。**

建议：

```text
Smoke 本地通过
+
CI / Self-hosted Windows 上重复通过
```

再把状态改为稳定。

---

# 46. Main CI 与 Release CI 应统一

当前 main Windows validation 已经包含：

```text
Build
Unit Test
Package
Crash Test
Installer
```

Release workflow 主要还是：

```text
-Test -Package
```

建议统一成一个：

```text
ValidateRelease.ps1
```

或 reusable workflow。

发布前必须完整执行：

```text
Build
Unit Tests
Coverage
WPF Smoke
Crash Privacy Test
Package
Installer Self Test
Version Match
SHA256
```

避免：

```text
main 验证一套
release 验证另一套
```

---

# 47. 正式性能矩阵仍应执行

当前生命周期测试结果已经比较积极：

```text
house → pet → tray × 100
```

结束后：

```text
跟踪对象存活数 = 0
活动 DispatcherTimer = 0
后段 Handle/GDI/USER 未继续增长
```

说明目前没有明显线性生命周期泄漏。

但 0.6 已经加入大量内容和 3D 元素，因此发布前仍建议正式采样：

```text
预热 120 秒
采样 600 秒
```

重点状态：

```text
pet-idle
pet-sleep
pet-walk
pet-run

house-indoor-idle
house-indoor-moving

house-outdoor-idle
house-outdoor-moving

house-boat-idle
house-boat-playing

floating-chat-idle
floating-chat-streaming-mock

tray-cold
tray-after-100-switches
```

并补：

```text
8 小时常驻
```

---

# 48. 多 DPI / 多显示器仍然需要真人机验证

代码层已经有坐标服务，但仍需要实际设备验证：

```text
100% + 150%
125% + 200%
横向双屏
上下双屏
主屏左侧有副屏
主屏右侧有副屏
不同 DPI 拖动
睡眠 / 锁屏后恢复
```

重点验证：

```text
桌宠位置
拖动
工作区边界
右键面板位置
小屋位置
Floating Chat
```

---

# 49. 安装升级链路仍然需要正式测试

当前 Installer 已能构建、自检。

但正式发布前应实际测试：

```text
0.5.7 → 0.6.0
全新安装 0.6.0
覆盖安装
卸载
保留用户数据
重新安装读取旧数据
启动项
桌面快捷方式
开始菜单
```

以及：

```text
旧数据库迁移
旧 JSON 导入
新版 JSON 导出再导入
```

---

# 50. GitHub Release 尚未真正发布

当前源码版本已经是：

```text
0.6.0
```

CI 也能生成：

```text
Forme-Setup-0.6.0.exe
```

但正式 GitHub Release 尚未创建。

建议：

> 先完成本文件 P0 和发布验收，再打 `v0.6.0` tag。

---

# 51. 下一阶段产品方向

当前不建议继续增加大量孤立系统。

下一阶段应该围绕：

```text
理解用户
真实执行
明确反馈
持续变化
宠物差异
世界反馈
```

而不是：

```text
继续增加更多页面
```

---

# 52. 推荐优先级

## P0

```text
1. AI 控制改为 Intent，而不是 Phrase
2. TargetCatalog 统一场景目标
3. ActionPlan / Preflight
4. ActionResult 结构化
5. 解决 AI 承诺动作但实际未执行
6. 重新确认 WPF Smoke
```

## P1

```text
7. ActionRules 增加 BuiltinRevision
8. 每日任务改约束随机
9. Level 增加内容解锁意义
10. ContentCatalogValidator
11. Content ID 永久兼容策略
12. Discovery Cooldown + 环境随机事件
13. EnvironmentContext
14. 正式性能矩阵
15. Main / Release CI 统一
```

## P2

```text
16. 四种宠物进一步行为差异化
17. 丰富环境随机反馈
18. 增加更多非奖励型世界事件
19. Photo Mode
20. 更完整的环境音与动作反馈
```

---

# 53. AI 意图系统验收矩阵

必须加入自动化测试。

## GoTo Pond

```text
去池塘边
去池塘旁边
到水边
走去木桥那里
过去池子那边
你去水边待会
```

全部：

```text
Intent = GoTo
Target = Pond
```

## GoTo Book

```text
去书架旁
走到书柜那里
过去看看书架
到看书的地方
```

全部：

```text
GoTo(Book)
```

## Stroll

```text
出去走走
溜达一下
转悠会儿
活动活动
在附近逛一下
```

全部：

```text
Stroll(Walk)
```

## 否定

```text
别去池塘
不要散步
别动
不用过去
```

不得执行对应正向动作。

## 讨论

```text
“去池塘边”支持吗？
刚才为什么没去池塘？
如果让你散步会怎样？
解释一下“绕屏幕跑一圈”
```

不得执行。

## 多步骤

```text
先去户外，再到池塘边
去池塘边然后看看水
出去走一圈再回来
```

ActionPlan 必须按顺序执行。

## 权限

覆盖：

```text
AllowPetControl = false
AllowSceneControl = false
AllowPlayControl = false
```

不能半执行。

## 场景

覆盖：

```text
当前室内
当前户外
小屋未打开
桌宠模式
托盘模式
Floating Chat
```

## 路径

覆盖：

```text
可到达
路径受阻
目标不存在
移动中被用户打断
30秒超时
```

---

# 54. 推荐实现顺序

不要一次重写整个动作系统。

建议按小提交推进：

```text
01 add CompanionIntent models
02 add TargetCatalog
03 add Local IntentResolver
04 add negation/discussion detection
05 convert common GoTo intents
06 add ActionPlan
07 add Preflight
08 add ActionResult
09 make UI consume ActionResult
10 add AI structured-intent fallback
11 migrate ActionRules to intent fast-path
12 add BuiltinRevision
13 add intent E2E tests
```

每一步：

```text
一个功能点
一个提交
一组测试
不同时大改 UI
```

---

# 55. 不建议的实现方式

不要：

```text
给 ActionRules 再塞几百个句子
```

不要：

```text
让 AI 自己直接生成任意坐标
```

不要：

```text
AI 说成功就当成功
```

不要：

```text
让 AI 直接修改数据库奖励
```

不要：

```text
把真实执行状态继续塞在中文字符串里再 parse
```

不要：

```text
为这个问题引入大型本地 LLM
```

当前阶段：

> **规则 Fast Path + 本地轻量 Intent + AI 结构化兜底**

已经足够。

---

# 56. 后续可选：本地 Embedding 语义识别

如果未来希望进一步提高脱网自然语言能力，可以增加：

```text
Sentence Embedding
```

对用户输入和 canonical intent 做相似度匹配。

例如：

```text
用户：
出去活动活动腿脚
```

和：

```text
canonical:
让宠物散步
```

如果：

```text
cosine similarity > threshold
```

识别：

```text
Stroll
```

但这一项应该放后面。

原因：

```text
模型体积
安装包
内存
启动速度
维护成本
```

当前阶段没有必要。

---

# 57. 推荐最终架构图

```text
                    User Message
                         │
                         ▼
                 Command Request?
                         │
              ┌──────────┴──────────┐
              │                     │
             No                    Yes
              │                     │
              ▼                     ▼
         Normal Chat       ActionRules Fast Path
                                    │
                              not matched
                                    ▼
                           Local IntentResolver
                                    │
                              low confidence
                                    ▼
                           AI Intent Resolver
                                    │
                                    ▼
                             CompanionIntent
                                    │
                                    ▼
                              TargetResolver
                                    │
                                    ▼
                               ActionPlanner
                                    │
                                    ▼
                                Preflight
                                    │
                                    ▼
                                Executor
                                    │
                                    ▼
                              ActionResult
                                    │
                     ┌──────────────┴─────────────┐
                     │                            │
                     ▼                            ▼
               UI Status                  Chat Feedback
```

---

# 58. 当前综合评价

| 维度 | 当前评价 |
|---|---:|
| 核心架构 | 8.5/10 |
| 工程化 | 8.5/10 |
| 数据安全与备份 | 8.5/10 |
| 功能丰富度 | 8.5/10 |
| 内容丰富度 | 7.5/10 |
| AI 与宠物融合 | 6.5/10 |
| 发布成熟度 | 7.5/10 |

当前短板已经非常明确：

> **AI 能聊天，但还没有真正稳定地“理解并控制宠物”。**

如果完成本轮 Intent 架构改造：

```text
Phrase Matching
        ↓
Intent Understanding
```

那么 Forme 会从：

> “有 AI 聊天功能的桌宠”

真正迈向：

> **“能理解自然语言并真实执行动作的 AI 桌面伙伴”。**

---

# 59. 最终建议

下一阶段不要优先增加更多大功能。

先集中完成：

```text
AI Intent
TargetCatalog
ActionPlan
ActionResult
自然语言 E2E
WPF Smoke
性能 / 发布验收
```

然后再继续扩展：

```text
宠物个性
环境事件
等级解锁
内容收藏
```

当前最值得投入的功能，不是再增加一个新小游戏，而是让用户可以直接自然地说：

```text
“去水边转悠一下吧。”
```

系统能够真正理解：

```text
GoTo(Pond)
```

然后：

```text
自动切换户外
自动找到池塘目标
自动寻路
真实移动
反馈成功或失败
```

用户不需要记：

```text
规则文件里到底写了哪一句
```

也不需要理解：

```text
go
interact
scene
forme-actions
坐标
```

这就是下一阶段 Forme 最应该完成的一次核心升级。
