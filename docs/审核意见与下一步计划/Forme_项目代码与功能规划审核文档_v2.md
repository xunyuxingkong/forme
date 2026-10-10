# Forme 项目代码与工程质量审核文档

> 项目地址：`https://github.com/xunyuxingkong/forme/`  
> 审核分支：`main`  
> 初始审核基准提交：`b9ae725c815055c76a8bd47424d21be264c7fd6d`  
> 初始提交说明：`feat: add model-independent pet animations`  
> 最新复审提交：`b0374780b8077f6e47e73f18514630e2270a19ea`  
> 最新提交说明：`feat: add scene navigation desktop modes SiliconFlow and floating chat`  
> 审核日期：2026-10-09  
> 文档性质：工程质量审核 / 优化整改建议 / 后续迭代路线图

---

## 1. 审核目的

本次审核面向 Forme 当前 0.1.x 阶段代码，重点检查以下方面：

1. 架构与模块职责是否清晰；
2. 数据存储与版本升级是否具备长期维护能力；
3. AI 请求链路是否具备合理的成本、性能和故障边界；
4. WPF 3D 与宠物动画是否存在明显资源占用问题；
5. 测试、构建、发布流程是否具备持续迭代能力；
6. 安装、升级、卸载与版本管理是否可靠；
7. 隐私、密钥、导入导出设计是否存在明显风险；
8. 当前实现是否适合继续扩展，而不是停留在一次性 Demo。

本审核不要求推翻现有技术栈。当前 `WPF + SQLite + HttpClient + Win32 + 自包含发布` 的总体方向适合轻量桌面陪伴类软件，后续优化应优先围绕“降低资源占用、提高可维护性、增强数据兼容、完善测试发布链路”展开。

---

# 2. 总体结论

## 2.1 当前状态判断

Forme 已经超过普通原型或演示项目的完成度，当前代码已经具备以下较好的工程基础：

- 本地优先设计；
- AI 请求默认由用户主动触发；
- API Key 与普通数据分离存储；
- API Key 使用 Windows DPAPI 保护；
- SQLite 数据导入前存在恢复备份；
- AI 请求限制并发、超时和单次输出规模；
- 3D 宠物动作已经从具体角色模型中解耦；
- 具备本地核心测试、WPF 冒烟检查和资源采样；
- 安装器具备基础的目录归属保护和卸载逻辑；
- 对隐私、接口成本和本地能力边界有明确说明。

其中最新的宠物动画结构：

```text
PetMotion
    ↓
PetPose
    ↓
IPetModel
    ↓
SproutPetModel
    ↓
SproutMesh
```

方向正确，不建议推翻。

## 2.2 当前主要短板

当前主要问题集中在以下五类：

1. **资源占用出现明显回归迹象**
2. **数据库长期升级能力不足**
3. **测试与 CI 体系不够工程化**
4. **AI 流式处理存在可优化热点**
5. **发布、版本、升级链路尚未真正闭环**

因此，项目目前更适合定义为：

> **“完成度较高、具备良好边界意识的 0.1 阶段可运行产品原型”**

距离长期发布型桌面软件还需要完成一轮工程化整改。

---

# 3. 问题优先级总览

| 优先级 | 编号 | 问题 | 风险等级 | 建议状态 |
|---|---|---|---|---|
| P0 | F-001 | 动画版本内存占用明显升高 | 高 | 立即排查 |
| P0 | F-002 | SQLite 缺少正式版本迁移机制 | 高 | 立即整改 |
| P0 | F-003 | 导出文件可能无法重新导入 | 高 | 立即整改 |
| P0 | F-004 | AI 配置保存存在半成功状态 | 高 | 立即整改 |
| P0 | F-005 | 全局未处理异常被统一吞掉 | 高 | 立即整改 |
| P1 | F-006 | SQLite 读写与后台导出竞争边界不足 | 中高 | 本轮整改 |
| P1 | F-007 | AI 流式响应存在较高无效分配和 UI 调度 | 中 | 本轮优化 |
| P1 | F-008 | 3D 表情模型缓存方式仍有较大优化空间 | 中高 | 本轮优化 |
| P1 | F-009 | 测试框架为自制 Console Runner | 中 | 本轮整改 |
| P1 | F-010 | GitHub CI 缺失 | 中高 | 本轮新增 |
| P2 | F-011 | Smoke / Probe 测试代码进入正式 App | 中 | 后续拆分 |
| P2 | F-012 | 多屏 DPI 混用 WPF DIP 与 Win32 像素 | 中 | 后续整改 |
| P2 | F-013 | 无障碍与低对比度文本可改进 | 中低 | 后续优化 |
| P2 | F-014 | 版本号多处硬编码 | 中 | 后续整改 |
| P2 | F-015 | GitHub 发布下载链路未闭环 | 中 | 发布前整改 |

---

# 4. P0 级问题

## F-001 动画版本内存占用明显升高

### 现象

当前实现记录中，加入模型无关动画后，短时资源采样出现：

```text
3D 小屋：约 223.7 MB
桌面宠物：约 162.1 MB
仅托盘：约 145.0 MB
```

此前短时回归结果曾记录：

```text
3D 小屋：约 97.9 MB
桌面宠物：约 64.3 MB
仅托盘：约 55.8 MB
```

两次测试环境和预热条件不完全相同，因此不能直接认定为严格性能回归，但差异已经足够大，必须优先排查。

### 风险

- 桌面常驻应用长期占用过高；
- 频繁打开/关闭小屋可能积累 WPF 3D native 资源；
- 角色表达缓存、场景模型、DispatcherTimer 或引用链可能无法释放；
- 强制 GC 可能掩盖真实生命周期问题。

### 建议

优先建立以下诊断：

```text
Managed Heap
Native Private Bytes
Working Set
GC Gen0 / Gen1 / Gen2
LOH
Handle Count
GDI Objects
USER Objects
WPF composition / GPU
```

同时设计两类测试：

#### A. 独立进程状态测试

每种状态单独启动全新进程：

```text
house
pet
tray
```

推荐：

```text
预热 120 秒
采样 600 秒
采样间隔 1 秒
```

#### B. 生命周期循环测试

执行：

```text
house → pet → tray → house
```

至少循环 100 次。

观察：

- Private Bytes 是否持续上升；
- Handle 是否持续增加；
- Model3D / DispatcherTimer 是否残留；
- 切换后是否可稳定回落。

### 验收标准

- 同一状态重复进入 100 次后，内存不存在持续线性增长；
- 托盘状态稳定后内存回落到明确目标范围；
- 动画停止后无 DispatcherTimer 持续运行；
- 关闭视图后 3D 模型和角色实例可被 GC；
- 不使用周期性 `GC.Collect()` 作为主解决方案。

---

## F-002 SQLite 缺少正式版本迁移机制

### 当前问题

当前数据库版本主要通过：

```csharp
public const int Version = 1;
```

以及：

```sql
PRAGMA user_version
```

控制。

现阶段主要逻辑为：

```text
version == 0
    → 创建 v1 数据结构

version > 当前版本
    → 拒绝打开
```

但缺少：

```text
v1 → v2
v2 → v3
v3 → v4
```

的正式迁移路径。

### 风险

后续新增：

- 字段；
- 索引；
- 数据约束；
- 表；
- 状态字段；
- 本地统计数据；

时无法平滑升级历史用户数据。

### 建议架构

增加：

```text
DatabaseMigrator
    ├── Migrate1To2()
    ├── Migrate2To3()
    ├── Migrate3To4()
    └── ...
```

主流程：

```text
打开数据库
    ↓
读取 user_version
    ↓
判断是否支持
    ↓
升级前备份
    ↓
BEGIN TRANSACTION
    ↓
依次执行 migration
    ↓
更新 user_version
    ↓
COMMIT
```

### 验收标准

至少覆盖：

- v1 → v2 正常迁移；
- 迁移失败自动回滚；
- 原数据库可恢复；
- 高版本数据库拒绝被旧程序打开；
- Migration 可重复测试；
- 每个版本只负责一次确定性迁移。

---

## F-003 导出文件可能无法重新导入

### 当前问题

当前设计允许导出完整用户数据，但导入逻辑限制：

```text
最大 32 MB
```

因此可能出现：

```text
用户成功导出 50 MB
    ↓
后续无法导回应用
```

恢复备份也可能面临同类问题。

### 风险

这是数据可恢复性问题，不只是体验问题。

“能够导出”应基本意味着：

> 当前版本或明确支持的后续版本能够重新导回。

### 建议

优先采用：

```text
流式 JSON 读取
```

避免：

```csharp
File.ReadAllText(...)
```

完整加载文件。

可考虑：

```text
Utf8JsonReader
JsonSerializer.DeserializeAsync
Stream
```

同时限制：

- 单条消息最大长度；
- 最大记录数；
- 总体文件安全上限；
- 非法结构；
- 深度；
- 字段范围。

如果短期不改流式导入，则至少：

- `recovery.json` 使用独立恢复入口；
- 内部恢复备份不受 32MB 普通导入限制；
- 导出前提前警告“此文件可能无法重新导入”。

### 验收标准

- 100MB 测试导出可以重新导入；
- 导入过程不会一次性占用数百 MB 内存；
- 失败导入不修改原数据；
- 导入前恢复备份可正常使用。

---

## F-004 AI 配置保存存在半成功状态

### 当前风险路径

当前“保存 AI 配置”大致为：

```text
保存新密钥
    ↓
保存 Preferences
    ↓
Endpoint 变化
    ↓
创建新会话
```

如果前两个步骤成功，但最后创建 Session 失败，现有异常处理主要恢复密钥，而 Preferences 可能已经改变。

最终可能出现：

```text
旧会话 + 新 Endpoint + 已恢复旧 Key
```

形成状态不一致。

### 建议

将保存 AI 配置封装为一个完整业务操作：

```text
SaveAiConfiguration()
```

需要保存：

```text
OldPreferences
OldSecret
OldSession
```

失败后全部恢复。

建议顺序：

```text
Validate
    ↓
准备新状态
    ↓
保存 Preference / Session
    ↓
最后更新 Secret
```

或者实现应用层补偿事务。

### 验收标准

构造以下故障点：

- 写 Preferences 失败；
- 创建 Session 失败；
- 写 secret 文件失败；

每种情况下都必须保证：

```text
配置整体保持原状态
```

而不是半成功。

---

## F-005 全局未处理异常被统一吞掉

### 当前问题

当前 WPF 全局异常处理使用类似：

```csharp
DispatcherUnhandledException += ...
e.Handled = true;
```

对所有异常统一继续运行。

### 风险

如果异常属于：

- NullReferenceException
- ObjectDisposedException
- InvalidOperationException（内部状态错误）
- 数据结构不一致
- 资源生命周期错误

继续运行可能造成应用处于未知状态。

### 建议

区分：

#### 用户可恢复异常

例如：

- 输入错误；
- 网络失败；
- AI Key 错误；
- 导入文件错误；

由业务层捕获，并显示友好提示。

#### 未处理程序异常

全局异常只负责：

```text
记录本地错误信息
保存必要状态
提示用户
安全退出 / 重启
```

建议增加本地 crash log：

```text
%LOCALAPPDATA%\Forme\diagnostics\
```

日志中严禁记录：

- API Key；
- 完整私人聊天；
- 心情正文；
- 敏感导出数据。

### 验收标准

- 已知业务错误不会触发全局 handler；
- 真正未处理异常不会被静默吞掉；
- Crash 日志不含凭据和私人正文；
- 程序异常退出后用户数据库仍可正常打开。

---

# 5. P1 级问题

## F-006 SQLite 后台导出与写操作竞争边界不足

### 当前情况

应用使用 SQLite，本地聊天流式过程中可能持续写入消息。

同时后台导出会启动只读 Store。

当前需要关注：

- read-only 连接的 busy timeout；
- DELETE journal mode；
- 大量历史聊天读取时对写入影响；
- 导出事务持续时间。

### 建议方案

推荐使用 SQLite Online Backup：

```text
在线数据库
    ↓
快速复制到临时快照
    ↓
释放主数据库
    ↓
从临时快照生成 JSON
```

优点：

- 不需要长时间持有数据库读事务；
- 聊天写入受影响更小；
- 导出时数据库状态一致。

### 验收标准

在以下并发情况下：

```text
AI 正在流式写消息
+
导出全部数据
```

要求：

- UI 不冻结；
- AI 回复不中断；
- 导出内容结构一致；
- 不出现 `database is locked`；
- 导出可取消。

---

## F-007 AI 流式响应存在无效分配和 UI 调度

### 当前链路

目前整体接近：

```text
ReadAsync 1 char
    ↓
拼 SSE 行
    ↓
解析 fragment
    ↓
StringBuilder.ToString()
    ↓
Dispatcher.Invoke
    ↓
判断刷新时间
    ↓
UI 更新
```

### 问题

- 单字符读取效率低；
- 每次 fragment 都 `ToString()`；
- 每个 fragment 都同步切 UI Dispatcher；
- 高频调用造成额外分配；
- UI 更新节流发生得太晚。

### 建议

改为：

```text
4 KB / 8 KB buffer
    ↓
SSE 增量解析
    ↓
Append(fragment)
    ↓
只记录 dirty
    ↓
每 50~100 ms BeginInvoke
    ↓
更新 UI
```

建议 `progress` 回调传：

```text
新增 fragment
```

而不是：

```text
完整字符串
```

### 验收标准

模拟 512 token / 2000 token 流式回复：

- UI 保持流畅；
- Dispatcher 调用次数明显下降；
- 分配量下降；
- 不改变超时、取消、错误和部分内容保留语义。

---

## F-008 3D 表情模型缓存仍可优化

### 当前结构优点

动画层已经与模型解耦：

```text
PetMotion
PetPose
IPetModel
SproutPetModel
```

这一层设计应保留。

### 当前优化空间

当前不同表情可能构造完整角色模型：

```text
idle
happy
focus
rest
quiet
thinking
×
blink
```

即使 MeshGeometry / Material 可共享，仍会增加：

- GeometryModel3D；
- Transform；
- Model3DGroup；
- 引用结构；

数量。

### 建议进一步拆分

```text
SproutPetModel
    ├── StaticBody
    │   ├── Body
    │   ├── Hands
    │   ├── Feet
    │   ├── Leaves
    │   └── Blush
    │
    └── ExpressionLayer
        ├── Eyes
        ├── Mouth
        └── Accessory
```

只替换或显隐表情部件。

### 验收标准

- 切换 10000 次表情不增加缓存数量；
- 无每帧创建 Geometry；
- 静态网格全部 Freeze；
- 角色实例释放后缓存可正确释放；
- House 和 Desktop Pet 不共享可修改 Root。

---

## F-009 测试体系应迁移到标准测试框架

### 当前问题

当前 `Forme.Tests` 是：

```text
Console Exe
+
Check()
+
Throws()
```

优点是简单，但缺点明显：

- 失败定位不够结构化；
- 无标准 Test Explorer；
- 无 TRX；
- 无标准 coverage；
- 无分类；
- 无筛选；
- CI 集成能力一般。

### 建议

优先迁移到：

```text
xUnit
```

或：

```text
MSTest
```

建议目录：

```text
tests/
    Forme.Core.Tests/
    Forme.Storage.Tests/
    Forme.Ai.Tests/
    Forme.Ui.SmokeTests/
```

### 验收标准

CI 中可执行：

```powershell
dotnet test
```

并生成：

```text
TRX
coverage
失败用例名称
```

---

## F-010 GitHub CI 缺失

### 当前问题

当前仓库没有正式 GitHub Actions 验证链路。

### 建议最低配置

新增：

```text
.github/workflows/ci.yml
```

Windows Runner：

```text
checkout
setup-dotnet
restore --locked-mode
build Release
dotnet test
installer self-test
```

另外可增加：

```text
pull_request
push main
manual dispatch
```

UI Smoke 可以先保留在：

```text
workflow_dispatch
```

或者使用 self-hosted Windows Runner。

### 验收标准

main 每次提交都能看到：

```text
Build
Unit Tests
Installer Self Test
```

明确状态。

---

# 6. P2 级问题

## F-011 测试与性能工具不应进入正式 App

当前以下逻辑仍位于 App：

```text
Smoke
Probe
PetAnimationChecks
```

长期建议拆出：

```text
Forme.UiTests
Forme.Benchmarks
```

避免正式 exe 含大量仅开发期使用的检查代码。

---

## F-012 多屏 DPI 处理需要统一坐标系

当前存在：

```text
WPF Left / Top
Win32 GetWindowRect
WinForms Screen.WorkingArea
SetWindowPos
```

其中 WPF 使用 DIP，Win32/WinForms 多数使用物理像素。

在：

```text
100% + 150%
125% + 200%
```

混合 DPI 双屏环境中，可能发生：

- 位置漂移；
- Clamp 错误；
- 重启后位置不同；
- 靠边收起不准确。

建议统一抽象：

```text
ScreenCoordinateService
```

集中处理：

```text
PhysicalPixelToDip
DipToPhysicalPixel
Monitor DPI
WorkArea
```

---

## F-013 无障碍与视觉对比度

当前界面整体风格统一，但需要补充：

- 低对比文字检查；
- Keyboard Focus；
- Tab 顺序；
- AutomationProperties；
- Screen Reader 名称；
- 高对比模式验证。

建议至少做到：

```text
主要文字 ≥ WCAG AA
按钮具备清晰 Focus 状态
所有关键控件支持键盘导航
```

---

## F-014 版本号多处硬编码

当前版本号同时出现在：

```text
Forme.App.csproj
Forme.Setup.csproj
Installer Program.cs
安装器文件名
Registry DisplayVersion
README
```

建议根目录统一：

```xml
<FormeVersion>0.1.0</FormeVersion>
```

然后构建脚本统一读取。

目标：

```text
一次修改版本号
→ App
→ Installer
→ 文件名
→ 注册表
→ Release
全部同步
```

---

## F-015 GitHub 发布链路未闭环

当前 README 指向：

```text
artifacts/Forme-Setup-0.1.0.exe
```

但：

```text
artifacts/
```

又被 `.gitignore` 排除。

因此普通用户 Clone 仓库并不能直接获得安装包。

### 建议

使用 GitHub Releases：

```text
Forme-Setup-x.y.z.exe
Forme-x.y.z-win-x64.zip
SHA256SUMS.txt
```

README 只指向 Release 页面。

正式发布后再增加：

- Authenticode 签名；
- Release Notes；
- 校验值；
- Upgrade Notes。

---

# 7. 建议的目录演进

当前项目不建议引入大型框架。

推荐轻量拆分为：

```text
src/
├── Forme.App
│   ├── App
│   ├── Views
│   │   ├── HomePage
│   │   ├── ChatPage
│   │   ├── FocusPage
│   │   ├── MoodPage
│   │   ├── RelaxPage
│   │   ├── RoomPage
│   │   └── SettingsPage
│   ├── Desktop
│   ├── ThreeD
│   └── Services
│
├── Forme.Core
│   ├── Models
│   ├── Focus
│   ├── Pet
│   ├── Ai
│   └── Validation
│
├── Forme.Storage
│   ├── Database
│   ├── Migrations
│   ├── Repositories
│   └── ImportExport
│
└── Forme.Setup

tests/
├── Forme.Core.Tests
├── Forme.Storage.Tests
├── Forme.Ai.Tests
└── Forme.Ui.SmokeTests
```

注意：

> 此结构为长期演进目标，不要求一次性大重构。

应采用：

```text
每次只拆一个职责
每次保持行为不变
每次通过测试
```

---

# 8. Store 建议拆分

当前 `Store` 已包含：

- Preferences；
- Mood；
- Chat；
- Focus；
- Growth；
- Import；
- Export；
- Reset；
- Validation。

后续建议拆为：

```text
Store
    ↓
DatabaseContext

PreferencesRepository
ChatRepository
MoodRepository
FocusRepository
GrowthRepository

ImportExportService
DatabaseMigrator
```

目标不是增加抽象层，而是避免未来一个 `Store.cs` 不断扩大。

---

# 9. MainWindow 建议拆分

当前 MainWindow 已承担较多页面构建职责。

建议逐步拆：

```text
MainWindow
    ↓
Shell + Navigation
```

页面分别实现：

```text
HomePage
ChatPage
FocusPage
MoodPage
RelaxPage
PlantPage
RoomPage
SettingsPage
```

优先拆：

```text
SettingsPage
ChatPage
RoomPage
```

因为这几个模块逻辑复杂度最高。

---

# 10. 性能验收建议

## 10.1 正式性能基线

建议至少定义以下目标：

| 状态 | CPU 平均 | 私有内存目标 | GPU | 写盘 |
|---|---:|---:|---:|---:|
| Tray | < 0.1% | < 80MB | 接近 0 | 0 |
| Desktop Pet idle | < 0.5% | < 120MB | 低 | 接近 0 |
| House idle | < 1.5% | < 180MB | 低 | 接近 0 |
| Active animation | < 3% | < 200MB | 可控 | 0 |

以上数值可作为工程目标，不应在未实测前对用户承诺。

## 10.2 必测场景

```text
启动冷启动 × 5
House 10 分钟
Pet 10 分钟
Tray 10 分钟
House/Pet/Tray 循环 100 次
AI 流式回复 100 次
心情保存 1000 条
聊天消息 10000 条
8 小时常驻
锁屏恢复
睡眠恢复
多屏 DPI
```

---

# 11. 数据库验收建议

最低应覆盖：

```text
全新数据库创建
v1 → v2 Migration
Migration 中途失败
未来版本拒绝
数据库损坏提示
并发导出 + 写入
100MB 导出
100MB 导入
导入失败回滚
恢复备份恢复
删除后无法由 recovery 恢复
```

---

# 12. AI 模块验收建议

应保留现有好的边界：

```text
不自动重试
不自动后台调用
不无限上下文
不自动跟随重定向
Key 不进入 Body
只允许 HTTPS
Localhost 可 HTTP
```

同时补充：

```text
SSE chunk parser
UTF-8 分块
部分 JSON
超长 data 行
半包
多个 data 行
超时
Idle timeout
总超时
用户取消
Endpoint 切换失败回滚
服务返回 401/403/429/5xx
```

---

# 13. 发布流程建议

建议形成：

```text
main
  ↓
CI
  ↓
tag v0.x.y
  ↓
Release Build
  ↓
Tests
  ↓
Installer Self Test
  ↓
SHA256
  ↓
Code Sign
  ↓
GitHub Release
```

发布物：

```text
Forme-Setup-0.x.y.exe
Forme-0.x.y-win-x64.zip
SHA256SUMS.txt
CHANGELOG.md
```

---

# 14. 推荐整改顺序

## 第一阶段：必须先做

```text
1. 内存回归定位
2. 数据库 Migration
3. 大文件导入/恢复能力
4. AI 配置保存原子性
5. 未处理异常策略
```

目标：

> 修复可能影响用户数据和长期稳定性的高风险问题。

---

## 第二阶段：工程化

```text
6. 标准测试框架
7. GitHub CI
8. AI Streaming 优化
9. SQLite 导出快照
10. 3D 表情模型轻量化
```

目标：

> 让每次改动都可自动验证。

---

## 第三阶段：发布质量

```text
11. 多屏 DPI
12. 无障碍
13. 版本统一
14. Release 自动化
15. 签名
16. 正式长时间性能测试
```

目标：

> 从内部可用进入稳定发布。

---

# 15. 不建议现在做的事情

当前阶段不建议为了“架构漂亮”引入：

```text
Prism
ReactiveUI
大型 DI 容器
EF Core
Unity
Unreal
Electron
复杂微服务
后台常驻服务
```

原因：

- 增加依赖；
- 增加内存；
- 增加打包体积；
- 增加维护复杂度；
- 对当前产品价值帮助有限。

Forme 更适合：

> 小而稳、低资源、本地优先、依赖有限。

---

# 16. Codex 执行建议

后续交给 Codex 时，不建议一次让其执行全部优化。

应采用“一项一提交”。

推荐任务格式：

```text
任务：实现数据库 v1 → v2 Migration 框架

约束：
1. 不修改 UI；
2. 不修改现有表含义；
3. 不删除用户数据；
4. Migration 必须事务化；
5. Migration 前必须备份；
6. 增加单元测试；
7. 不顺带重构无关代码。

验收：
- 新数据库创建成功；
- v1 数据正常升级；
- 迁移失败不修改原库；
- 高版本数据库仍拒绝打开；
- dotnet test 全部通过。
```

每个 P0/P1 项目均采用这种方式。

---

# 17. 最终审核结论

Forme 当前实现方向总体合理，特别是以下方面值得继续保持：

```text
本地优先
AI 显式触发
凭据单独保护
数据可导出
动画与模型解耦
资源占用意识
隐私边界意识
```

当前不需要推翻架构。

接下来最重要的是：

```text
性能稳定性
数据库升级
数据恢复
测试工程化
发布工程化
```

如果完成本审核文档中的 P0 与 P1 项目，Forme 将从：

> “完成度较高的 0.1 产品原型”

提升到：

> “具备持续迭代能力的正式桌面软件工程”。

---

# 18. 审核整改清单

## P0

- [ ] F-001 动画内存回归定位
- [ ] F-002 SQLite Migration
- [ ] F-003 大文件导入与 Recovery
- [ ] F-004 AI 配置事务化
- [ ] F-005 全局异常策略

## P1

- [ ] F-006 SQLite 导出快照
- [ ] F-007 AI Streaming 优化
- [ ] F-008 3D 模型轻量化
- [ ] F-009 标准测试框架
- [ ] F-010 GitHub CI

## P2

- [ ] F-011 测试代码从正式 App 拆出
- [ ] F-012 多屏 DPI
- [ ] F-013 无障碍
- [ ] F-014 统一版本号
- [ ] F-015 GitHub Releases / 签名

---

**审核建议：P0 完成后再继续新增大型功能。**

在 P0 完成之前，优先避免继续增加：

- 新数据库字段；
- 大量新 3D 动画；
- 新后台任务；
- 自动联网功能；
- 新复杂数据格式。

先把当前基础打牢，再扩展功能，整体返工成本会明显更低。

---

# 19. 最新提交复审（基于 `b037478`）

## 19.1 复审范围

相对于初始审核基线 `b9ae725`，当前 `main` 已前进 13 个提交。主要新增和整改内容包括：

- SQLite v0 → v1 → v2 事务迁移；
- 大文件流式导入、SQLite staging 和恢复备份；
- SQLite 快照式导出；
- AI 配置保存补偿事务；
- 未处理异常脱敏诊断与安全退出；
- SSE 4KB 缓冲读取和 80ms UI 批量刷新；
- 静态宠物身体与表情模型拆分；
- MSTest、TRX、Cobertura 覆盖率；
- GitHub Actions Windows CI；
- 室内点击移动和绕障；
- 20m × 20m 户外场景；
- 桌面随机走动、奔跑、睡觉和舞蹈；
- 硅基流动 DeepSeek 预设；
- 桌面悬浮聊天窗口。

总体判断：

> 原审核中的 P0/P1 大部分已经实现，项目已经进入“稳定性整改 + 产品玩法扩展”并行阶段。

但当前 HEAD 仍存在几个需要优先处理的新问题。

---

## 19.2 新发现问题

| 编号 | 优先级 | 问题 | 当前判断 |
| --- | --- | --- | --- |
| R-001 | P0 | GitHub Actions 当前失败 | 必须先修 |
| R-002 | P0 | 已知 AI 服务商切换时可能沿用旧服务 API Key | 建议立即整改 |
| R-003 | P1 | 随机走动等待阶段仍持续 30 FPS | 建议优化 |
| R-004 | P1 | 桌宠拖动后存在被再次识别为点击的风险 | 建议真机验证并修复 |
| R-005 | P1 | 新增大量移动/户外功能后，旧性能基线已不足 | 需要重新测试 |
| R-006 | P2 | PetTravel 同网格单元短路径缺少一次直接 Clear 检查 | 小概率边界 |
| R-007 | P2 | 单提交混合过多功能，未来回滚困难 | 后续规范 |

---

## R-001 GitHub Actions 当前失败

### 现象

最新 `main` 已真实触发：

```text
Windows validation
```

实际结果：

```text
Build succeeded
0 warnings
0 errors

MSTest:
24 passed
0 failed
```

但最终工作流仍失败。

失败点是：

```text
Build, test, package and verify installer
```

具体错误：

```text
NU1004

Lock file's package references:
Microsoft.NETFramework.ReferenceAssemblies:[1.0.3, )

project's package references:
None
```

### 根因

当前：

```text
src/Forme.Setup/packages.lock.json
```

锁定了：

```text
Microsoft.NETFramework.ReferenceAssemblies 1.0.3
```

但是：

```text
src/Forme.Setup/Forme.Setup.csproj
```

没有对应的 `PackageReference`。

而构建脚本执行：

```powershell
dotnet restore src/Forme.Setup/Forme.Setup.csproj --locked-mode
```

因此干净的 GitHub Runner 会正确拒绝不一致的 lock 文件。

### 建议修改

在 `Forme.Setup.csproj` 中显式加入：

```xml
<ItemGroup>
  <PackageReference
      Include="Microsoft.NETFramework.ReferenceAssemblies"
      Version="1.0.3"
      PrivateAssets="All" />
</ItemGroup>
```

然后执行：

```powershell
dotnet restore src/Forme.Setup/Forme.Setup.csproj --force-evaluate
```

确认 lock file 后提交。

### 验收

CI 必须完整通过：

```text
Restore
Build
MSTest
Coverage
Publish
Installer Build
Installer Self Test
Crash Privacy Test
Artifact Upload
```

在 CI 变绿之前，不建议发布安装包。

---

## R-002 已知 AI Provider 切换不应复用旧服务 API Key

### 当前情况

当前从一个服务切换到另一个服务时：

```text
DeepSeek 官方
    ↓
硅基流动
```

界面会清除尚未保存的 Key 草稿，这是正确的。

但是如果本机已经保存了旧 Key，保存新 Endpoint 时仍允许用户选择：

```text
继续保留已有 Key
```

这可能造成：

```text
DeepSeek 官方 Key
    ↓
被作为 Bearer Token
    ↓
发送至 SiliconFlow
```

虽然用户存在确认操作，但对于“已知不同服务商”，不应设计成默认可复用凭据。

### 建议

增加 Provider Identity：

```text
deepseek
siliconflow
custom
```

规则：

```text
deepseek → siliconflow
siliconflow → deepseek
```

必须：

```text
重新输入 Key
```

不能保留旧 Key。

只有：

```text
custom → custom
```

且用户明确确认时，才允许保留旧凭据。

### 更稳妥的数据设计

可以将密钥按 provider 隔离：

```text
ai.secret.deepseek
ai.secret.siliconflow
ai.secret.custom.<hash>
```

但如果首版不希望增加多密钥管理，也可以保持“只保存一个密钥”，切服务商时强制重新输入。

### 验收

测试：

```text
DeepSeek → SiliconFlow
SiliconFlow → DeepSeek
Custom A → Custom B
同 Provider 修改 Model
同 Provider 修改 Endpoint 尾部 /
```

确保不会无提示跨服务发送旧凭据。

---

## R-003 随机走动等待阶段不应持续 30 FPS

### 当前问题

随机走动到达目标后会等待数秒再生成下一目标，但只要：

```text
ExternalActive = true
```

PetAnimator 就维持：

```text
30 FPS
```

因此可能出现：

```text
走 2 秒
等待 5 秒
```

其中等待 5 秒仍产生约：

```text
150 次 Dispatcher Tick
```

而窗口位置完全没有变化。

### 建议

拆成三个调度档：

```text
正在走/跑       30 FPS
普通呼吸        15 FPS
睡眠/等待目标    5 FPS 或一次性等待 Timer
```

更推荐：

```text
到达目标
    ↓
停止移动 30 FPS
    ↓
设置 3~8 秒一次性 DispatcherTimer
    ↓
生成新目标
    ↓
恢复移动帧率
```

### 验收

在 `walk` / `run` 模式连续运行 10 分钟：

- 等待阶段 CPU 明显下降；
- 不增加额外常驻高频 Timer；
- 切换安静模式、打开面板、拖动、聊天时立即停止移动；
- 恢复后行为正常。

---

## R-004 桌宠拖动后可能误触一次点击

当前拖动代码在 `DragMove()` 返回后，将：

```text
_dragged = false
```

重置。

但 WPF 的 `DragMove()` 通常在鼠标释放后才返回，因此后续 `MouseLeftButtonUp` 可能再次看到：

```text
_dragged == false
```

从而执行：

```text
弹出面板
+
Pat 动作
```

### 建议

不要在 `DragMove()` 的 `finally` 中清空 `_dragged`。

应保持：

```text
_dragged = true
```

直到 `MouseLeftButtonUp` 完成判断后，再统一：

```text
_dragged = false
```

### 验收

人工测试至少：

```text
拖动 20 次
短拖 20 次
快速拖动 20 次
跨屏拖动 20 次
```

要求：

- 拖动结束不自动弹面板；
- 不额外触发 Pat；
- 单击仍正常弹面板。

---

## R-005 新功能加入后需要重新建立性能基线

之前正式采样约为：

| 状态 | 平均私有内存 | 平均归一化 CPU |
| --- | ---: | ---: |
| 小屋空闲 | 143.44MB | 0.857% |
| 桌面宠物 | 58.39MB | 0.302% |
| 冷启动托盘 | 17.73MB | 0.076% |

但之后又新增：

- 室内扩大；
- 20m × 20m 户外；
- A* 路径搜索；
- 室内/户外角色移动；
- 桌面随机走/跑；
- 三种舞蹈；
- Floating Chat；
- SiliconFlow Provider。

因此旧数据不能代表当前完整版本。

### 建议新增 Probe 状态

```text
pet-idle
pet-sleep
pet-walk
pet-run
house-indoor-idle
house-indoor-moving
house-outdoor-idle
house-outdoor-moving
floating-chat-idle
floating-chat-streaming-mock
tray-cold
tray-after-100-switches
```

重点比较：

```text
Pet idle
vs
Pet walk
vs
Pet run
```

和：

```text
Indoor
vs
Outdoor
```

---

## R-006 PetTravel 同 cell 短路径边界

当前网格尺寸：

```text
0.35m
```

如果当前位置和目标点被量化到同一个 cell：

```text
start == end
```

A* 会直接判断已经找到目标。

严格来说仍应该检查：

```text
Clear(Position, target)
```

### 建议

加入：

```csharp
if (start == end)
{
    if (!Clear(Position, target))
        return false;
    ...
}
```

并增加单元测试。

---

## R-007 控制单次提交规模

最新一个功能提交同时包含：

```text
场景移动
户外
随机桌面移动
睡眠
舞蹈
SiliconFlow
Floating Chat
文档
测试
```

功能本身可以共存，但 Git 历史不利于：

- 回滚；
- 二分定位；
- Code Review；
- 发布说明；
- 缺陷追踪。

今后建议拆为：

```text
feat: add indoor and outdoor pet navigation
feat: add desktop idle and dance modes
feat: add SiliconFlow provider preset
feat: add floating desktop chat
```

原则：

> 一个 commit 尽量表达一个可独立验证的业务变化。

---

# 20. 产品层面诊断：为什么现在会感觉“功能少、单调、无聊”

当前 Forme 已经有不少“功能入口”：

```text
聊天
专注
心情
放松
植物
房间
户外
桌宠动作
```

但它们更像几个互相独立的小工具。

真正缺少的不是单纯“按钮数量”，而是：

```text
持续反馈
成长
收集
解锁
随机性
目标
内容变化
个性化
```

现在的典型体验是：

```text
打开
→ 点一下宠物
→ 看一个动作
→ 聊两句
→ 关闭
```

用户第二天回来时，世界与昨天几乎没有明显变化。

因此后续产品设计重点不应该只是继续加页面，而应该建立：

> **互动 → 获得反馈 → 解锁内容 → 改造空间 → 出现新事件 → 再次互动**

的长期循环。

---

# 21. 建议建立 Forme 的核心玩法循环

推荐主循环：

```text
专注 / 互动 / 心情记录 / 玩耍
                ↓
        获得陪伴值 / 星星
                ↓
     解锁家具 / 玩具 / 动作 / 装扮
                ↓
          布置小屋和户外
                ↓
      触发随机事件 / 收藏发现
                ↓
        宠物表现发生变化
                ↓
              再互动
```

这个循环应坚持：

```text
不签到惩罚
不掉好感
不制造焦虑
不强制联网
不靠广告奖励
不设计抽卡氪金
```

即使几天不打开：

```text
宠物也不会责怪用户
```

这会更符合 Forme 当前“轻陪伴”的产品气质。

---

# 22. 功能扩展总表

## 22.1 第一优先级：让产品马上“不无聊”

| 编号 | 功能 | 趣味提升 | 开发量 | 联网 | 推荐 |
| --- | --- | ---: | ---: | --- | --- |
| G-001 | 陪伴值 / 成长等级 | ★★★★★ | 中 | 否 | 强烈推荐 |
| G-002 | 星星货币与解锁系统 | ★★★★★ | 中 | 否 | 强烈推荐 |
| G-003 | 家具/装饰库存与布置 | ★★★★★ | 中高 | 否 | 强烈推荐 |
| G-004 | 喂食、玩具、抛球、泡泡互动 | ★★★★★ | 中 | 否 | 强烈推荐 |
| G-005 | 每日轻任务 | ★★★★☆ | 中 | 否 | 推荐 |
| G-006 | 成就与收藏图鉴 | ★★★★☆ | 中 | 否 | 推荐 |
| G-007 | 户外随机发现事件 | ★★★★★ | 中 | 否 | 强烈推荐 |
| G-008 | 更多随机待机动作 | ★★★★☆ | 低中 | 否 | 推荐 |

---

## 22.2 第二优先级：形成“游戏感”

| 编号 | 功能 | 说明 |
| --- | --- | --- |
| G-009 | 接球小游戏 | 用户点击/拖动丢球，宠物跑去捡 |
| G-010 | 找星星 | 小屋或户外随机出现星星，点击寻找 |
| G-011 | 记忆翻牌 | 轻量 2D 小游戏，可获得少量星星 |
| G-012 | 花园种植 | 种子 → 浇水 → 生长 → 收藏花朵 |
| G-013 | 钓鱼/捞叶子 | 户外场景简单互动小游戏 |
| G-014 | 宠物寻宝 | 给宠物发出探索指令，几秒后找到随机小物 |
| G-015 | 特殊事件 | 蝴蝶、落叶、流星、萤火虫、彩虹等 |

建议不要同时开发大量小游戏。

首批只做：

```text
接球
+
找星星
```

即可显著增加互动感。

---

## 22.3 第三优先级：增强个性化

| 编号 | 功能 | 说明 |
| --- | --- | --- |
| G-016 | 宠物帽子/围巾/眼镜 | 装扮库存 |
| G-017 | 房间家具替换 | 桌子、沙发、灯、床、书架 |
| G-018 | 墙纸/地板 | 低成本但视觉变化非常明显 |
| G-019 | 户外主题 | 春日、夏夜、秋叶、冬雪 |
| G-020 | 动作收藏 | 解锁不同舞蹈、打招呼、睡姿 |
| G-021 | 表情收藏 | 不同眼睛、嘴型和情绪表现 |
| G-022 | 名牌与称号 | “专注小助手”“花园伙伴”等 |

---

# 23. 最推荐增加的功能详解

## G-001 陪伴值 / 成长等级

建议添加：

```text
Lv.1 → Lv.20
```

不需要无限等级。

获得方式：

```text
完成一次专注       +3
记录一次心情       +1
浇水               +1
与宠物玩耍         +1
完成每日任务       +2~5
```

建议设置每日自然上限，避免刷操作。

例如：

```text
每日最多获得 20 点普通陪伴值
```

等级不掉。

### 解锁示例

```text
Lv.2  新动作
Lv.3  新地毯
Lv.4  小球玩具
Lv.5  新帽子
Lv.6  户外花坛
Lv.8  新舞蹈
Lv.10 夜景灯串
```

这种机制可以把现有各功能串起来。

---

## G-002 星星货币系统

推荐使用：

```text
⭐ 星星
```

它不是付费货币，只是本地游戏资源。

来源：

```text
每日任务
小游戏
专注完成
随机发现
成就
```

用途：

```text
家具
玩具
装饰
服装
动作
户外物件
```

这样用户获得的奖励有实际用途。

---

## G-003 家具库存与布置模式

当前房间只有少量主题项。

建议增加真正的：

```text
编辑房间
```

流程：

```text
进入布置模式
→ 点击家具
→ 移动 / 旋转 / 隐藏
→ 从库存放入家具
→ 保存
```

首版不需要完全自由建造。

可使用固定 slot：

```text
床位
桌面
墙面
地毯
灯具
书架
窗边
装饰位1
装饰位2
```

这样比完全自由拖拽容易很多，碰撞和保存也简单。

---

## G-004 玩具互动系统

这是最能提升“宠物感”的功能之一。

首批玩具：

```text
小球
毛线球
泡泡
逗猫棒式玩具
小纸飞机
```

例如小球：

```text
用户点击地面
    ↓
球抛出去
    ↓
PetTravel 寻路
    ↓
宠物追到球
    ↓
播放捡球动作
    ↓
带回来
```

可以直接复用已经实现的：

```text
PetTravel
PetAnimator
```

因此投入不算特别大。

---

## G-005 每日轻任务

建议每天本地生成 3 个。

例如：

```text
完成一次 10 分钟以上专注
和团团玩一次
记录一次今天的心情
给植物浇水
去户外走一走
完成一次小游戏
```

奖励：

```text
星星 + 陪伴值
```

重要：

> 不应该设计“连续签到断掉”的惩罚。

可以显示：

```text
今天想做就做
```

未完成不会扣东西。

---

## G-006 收藏图鉴

建议建立：

```text
收藏册
```

类别：

```text
家具
装扮
动作
植物
户外发现
成就
特殊事件
```

例如：

```text
发现过：
☑ 蒲公英
☑ 蓝蝴蝶
☐ 萤火虫
☐ 流星
☐ 四叶草
```

只要有几个随机稀有发现，户外就会马上比现在有意思很多。

---

## G-007 户外随机事件

当前户外最大的风险是：

> 很大，但没有事情发生。

建议增加完全本地的随机事件。

普通事件：

```text
落叶
蒲公英
蝴蝶
小鸟
风吹草动
```

较少见：

```text
彩虹
萤火虫
流星
四叶草
礼物盒
```

用户点击后：

```text
宠物走过去
→ 播放动作
→ 获得图鉴 / 少量星星
```

这会直接利用现有 20m 场景，而不是再开发新页面。

---

# 24. 让宠物本身更“活”

现在宠物虽然会移动和舞蹈，但行为还是偏机械。

建议建立：

```text
IdleBehaviorScheduler
```

根据本地状态随机选择行为：

```text
伸懒腰
东张西望
打哈欠
坐下
看窗外
摸叶子
趴下
转圈
原地蹦一下
观察家具
走到沙发边
走到植物边
```

不要随机频繁触发。

建议：

```text
20~90 秒随机一次
```

并根据状态区分。

### 白天

```text
走动
伸懒腰
观察家具
```

### 晚上

```text
打哈欠
坐下
睡觉概率增加
```

### 用户专注时

```text
安静坐在桌边
偶尔翻书/点头
```

### 用户休息时

```text
走到沙发
躺下
```

这些变化比继续增加很多按钮更能增强“陪伴感”。

---

# 25. 互动家具

推荐让房间现有物件不只是导航入口。

例如：

### 沙发

```text
宠物跳上去
→ 坐下 / 睡觉
```

### 床

```text
睡觉
```

### 书桌

```text
用户专注时宠物坐桌边
```

### 植物

```text
走过去闻一闻
```

### 窗户

```text
看窗外
```

### 音响

```text
播放本地环境音
+
宠物舞蹈
```

互动家具可以大量复用已有动作和寻路。

---

# 26. 环境变化

## 26.1 日夜系统

现在已有 day/night，可进一步增加：

```text
清晨
白天
黄昏
夜晚
```

不用真实太阳计算，只根据本地时间。

效果：

```text
光照
天空颜色
窗外颜色
室内灯
宠物行为
环境音
```

---

## 26.2 季节

按本地日期自动：

```text
春
夏
秋
冬
```

只改变：

```text
草地色
树叶
少量装饰
随机事件
```

避免重新创建大型资源系统。

---

## 26.3 天气（可选）

建议放到较后版本。

默认完全本地：

```text
晴天
```

用户主动开启后，才可连接天气 API。

不要默认读取定位。

如果做天气，应允许用户：

```text
手动选择城市
```

而不是后台定位。

---

# 27. 声音与氛围

目前声音反馈较少。

建议增加本地内置轻量声音：

```text
脚步
跳跃
呼噜/轻呼吸
泡泡
捡球
浇水
解锁
星星
```

房间环境音：

```text
雨声
风声
壁炉
夜晚虫鸣
咖啡馆白噪音
```

要求：

```text
默认较低音量
可单独关闭
安静模式全部关闭
不后台联网播放
```

---

# 28. “实用功能”也可以增加留存

不能只做游戏。

## 28.1 本地提醒

添加：

```text
提醒我 30 分钟后喝水
18:00 提醒我下班
每天 10:00 站起来活动
```

首版只做本地提醒，不依赖 AI。

数据全部 SQLite 保存。

---

## 28.2 今日清单

轻量 Todo：

```text
今天要做
□ SQL 优化
□ 看 30 分钟书
□ 锻炼
```

完成后：

```text
宠物庆祝
+
少量星星
```

不要做成复杂项目管理软件。

---

## 28.3 专注统计

增加：

```text
今天
本周
本月
```

显示：

```text
总专注时间
完成次数
最常专注时段
连续专注分布
```

推荐本地计算。

---

## 28.4 心情周报

现有心情记录可以升级为：

```text
一周心情趋势
```

默认本地统计：

```text
不同心情次数
记录天数
简单趋势
```

用户明确点击：

```text
让 AI 帮我总结
```

才发送选择的数据。

---

# 29. AI 功能不要只停留在普通聊天

普通聊天很容易无聊。

推荐 AI 更多与现有本地功能结合，但必须坚持“明确触发后才发送”。

## 29.1 AI 角色模式

允许选择：

```text
温柔陪伴
轻松搞笑
学习搭子
安静倾听
```

本质只是本地 Prompt 模板。

---

## 29.2 “聊聊今天”

用户主动选择：

```text
今天的心情记录
今天的专注记录
```

然后点击：

```text
和团团聊聊今天
```

界面明确展示“即将发送哪些数据”。

---

## 29.3 AI 生成每日一句

每天最多手动生成一次：

```text
今天想对我说什么？
```

生成结果本地缓存。

避免每次启动都产生 API 调用。

---

## 29.4 AI 不应控制核心游戏状态

不要让 AI 直接决定：

```text
扣星星
升级
奖励
解锁
修改数据库
```

核心玩法全部由本地确定性逻辑控制。

AI 只负责：

```text
文字内容
```

这样成本、稳定性和测试都更可控。

---

# 30. Photo Mode / 截图模式

这是低成本、传播性很强的功能。

增加：

```text
拍照模式
```

可：

```text
隐藏 UI
旋转镜头
调整宠物姿势
选择表情
选择时间效果
保存 PNG
```

未来可增加：

```text
相框
日期
宠物名字
```

所有截图只在用户点击保存时生成。

---

# 31. 成就系统建议

不需要太多。

首版 20~30 个即可。

例如：

```text
第一次见面
第一次完成专注
累计专注 60 分钟
累计专注 10 小时
记录 7 次心情
植物浇水 5 次
发现第一只蝴蝶
发现四叶草
第一次去户外
第一次完成接球
拥有 10 件家具
解锁 5 个动作
```

成就奖励：

```text
星星
装饰
称号
```

---

# 32. 推荐新增的数据模型

建议数据库升级到：

```text
v3
```

时增加独立游戏数据表。

不要继续全部塞进 `settings` JSON。

建议：

```text
pet_progress
wallet
inventory
catalog_unlock
daily_task
achievement
discovery
room_slot
pet_cosmetic
```

示意：

```text
pet_progress
------------
level
xp
updated

wallet
------
currency
amount

inventory
---------
item_id
quantity
unlocked_at

daily_task
----------
day
task_id
progress
target
completed
claimed

achievement
-----------
achievement_id
unlocked_at

discovery
---------
discovery_id
first_seen
seen_count
```

注意：

> 角色移动位置、动画帧、临时随机目标不要写数据库。

只在真正发生业务事件时写：

```text
完成任务
解锁物品
获得星星
完成成就
更换家具
```

---

# 33. 推荐代码结构

保持轻量，不引入大型游戏框架。

建议增加：

```text
Forme.Core/
├── Progression/
│   ├── ProgressionService
│   ├── RewardService
│   └── DailyTaskService
│
├── Inventory/
│   ├── ItemCatalog
│   └── InventoryService
│
├── Gameplay/
│   ├── InteractionService
│   ├── DiscoveryService
│   └── AchievementService
│
└── Content/
    └── ContentCatalog
```

App 层只负责：

```text
显示
点击
动画
声音
```

Core 决定：

```text
获得多少 XP
奖励什么
是否解锁
任务是否完成
```

这样所有玩法规则都可以 MSTest。

---

# 34. 内容数据驱动

家具、玩具、动作、成就不要全部硬编码到 C#。

建议使用本地 JSON：

```text
Content/
├── items.json
├── achievements.json
├── daily-tasks.json
└── discoveries.json
```

例如：

```json
{
  "id": "toy.ball.yellow",
  "type": "toy",
  "name": "黄色小球",
  "price": 30,
  "unlockLevel": 3
}
```

好处：

```text
增加内容时不需要改玩法代码
```

未来甚至可以支持：

```text
官方内容包
```

但首版不需要做完整 Mod 系统。

---

# 35. 推荐版本路线

## v0.1.x：先把当前工程问题收口

必须完成：

```text
CI 变绿
Provider Key 隔离
桌宠拖动回归修复
随机移动等待功耗优化
新性能基线
```

---

## v0.2：建立“有趣”的核心循环

建议只做六件事：

```text
1. 陪伴值 / 等级
2. 星星货币
3. 家具/装饰库存
4. 小球互动
5. 每日轻任务
6. 户外随机发现
```

这六项完成以后，Forme 才真正开始具有：

```text
目标
奖励
变化
收集
成长
```

---

## v0.3：增强玩法

```text
接球小游戏
找星星小游戏
收藏图鉴
成就
更多家具
更多动作
互动家具
```

---

## v0.4：增强氛围

```text
四时段日夜
季节
环境音
Photo Mode
装扮
特殊随机事件
```

---

## v0.5：增强实用性和 AI

```text
本地提醒
今日清单
专注统计
心情周报
AI角色模式
用户主动选择数据后的“聊聊今天”
```

---

# 36. 如果只做 5 个新功能，我最推荐什么

如果当前目标是：

> **用最少的开发量，让软件明显不再单调。**

优先级是：

### 第一：陪伴值 + 星星

把所有已有功能串起来。

### 第二：玩具互动——先做小球

直接利用已经做好的寻路和动画。

### 第三：户外随机发现

让 20m 户外不再只是空地。

### 第四：家具库存和解锁

让奖励有消费目标。

### 第五：每日 3 个轻任务

给用户一个每天打开应用的自然理由。

这五项形成：

```text
玩
↓
得到星星
↓
解锁家具
↓
房间变化
↓
发现新东西
↓
第二天有新的小目标
```

比单独增加十几个页面更有效。

---

# 37. 不建议增加的“伪丰富功能”

以下功能看起来很多，但容易让项目变重而没有真正提升体验：

```text
大型社交系统
好友排行榜
账号系统
云端强制同步
复杂商城
抽卡
宠物饥饿惩罚
连续签到断签惩罚
广告换奖励
后台监听正在使用的软件
默认屏幕识别
默认读取剪贴板
持续上传使用行为
```

这些功能会破坏当前：

```text
本地优先
轻量
低打扰
隐私边界清楚
```

的优势。

---

# 38. 新增功能的统一验收原则

以后每个玩法功能都必须回答：

```text
1. 是否需要联网？
2. 是否产生 API 费用？
3. 是否写数据库？
4. 写入频率是多少？
5. 是否增加常驻 Timer？
6. 隐藏窗口后是否停止？
7. 减少动效时如何表现？
8. 安静模式时如何表现？
9. 是否可单元测试？
10. 是否能回滚到旧数据？
```

尤其禁止：

```text
逐帧写数据库
逐帧保存位置
大量独立 DispatcherTimer
后台无限轮询
默认联网拉内容
```

---

# 39. 推荐 Codex 执行顺序

下一轮不要一次要求 Codex：

```text
“把 Forme 做得更好玩”
```

这很容易失控。

推荐严格按下面顺序：

```text
01 fix CI locked restore
02 isolate provider credentials
03 optimize desktop wander idle scheduling
04 fix drag click state
05 add latest performance probe modes

06 add progression schema and service
07 add star wallet
08 add inventory/catalog
09 add ball interaction
10 add daily task service
11 add outdoor discoveries
12 add room slots and decoration UI
13 add achievement service
14 add collection book
15 add first mini-game
```

每项：

```text
一个目标
一个提交
一组测试
一个验收结果
```

---

# 40. 最新综合结论

当前 Forme 的工程质量已经比初次审核时明显提高。

现在最大的产品问题已经逐渐从：

```text
“代码是否可靠”
```

转变为：

```text
“用户为什么明天还要再打开它”
```

下一阶段的重点不应是无限增加孤立页面，而应建设：

> **成长 + 奖励 + 收集 + 布置 + 随机事件 + 可重复互动**

其中最值得优先形成的产品闭环是：

```text
陪伴值
+
星星
+
小球互动
+
户外随机发现
+
家具解锁
+
每日轻任务
```

建议在当前 CI 和 Provider 凭据问题修复后，立即开始 v0.2 核心玩法，而不是继续只增加工具型页面。
