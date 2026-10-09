# Forme 项目代码与工程质量审核文档

> 项目地址：`https://github.com/xunyuxingkong/forme/`  
> 审核分支：`main`  
> 审核基准提交：`b9ae725c815055c76a8bd47424d21be264c7fd6d`  
> 提交说明：`feat: add model-independent pet animations`  
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
