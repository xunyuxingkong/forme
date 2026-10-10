# Forme · 陪伴小屋

Windows 上的 3D 桌面伙伴与陪伴小屋。可以和伙伴互动、在小屋中专注和休息、记录心情，或使用自己配置的 AI 服务聊天。

## 启动与安装

正式版安装包和 SHA-256 校验值请从 [GitHub Releases](https://github.com/xunyuxingkong/forme/releases/latest) 下载。双击安装到当前用户目录，创建桌面及开始菜单入口，无需管理员权限。卸载保留个人记录，可在应用设置中先清除。

也可直接运行免安装版本：`artifacts/publish/Forme.exe`。此版本包含所需 .NET 运行时；首次启动无需安装 SDK。

当前目标为 Windows 11 x64。安装包未做商业代码签名；其他系统和架构未列为已验证支持。

## 如何使用

- 首次打开为伙伴命名，进入小屋；AI 配置可跳过。
- 在 3D 房间中拖动调整视角，滚轮调整距离，点击“复位视角”恢复。
- 点击伙伴聊天、书桌专注、心情本记录、沙发放松、植物浇水；底部文字导航也可使用。
- 关闭小屋后伙伴回到桌面。拖动伙伴移动，点击显示快捷入口，右键打开菜单。
- 通过系统托盘恢复小屋或伙伴，也可收起到仅托盘状态。退出需选择“退出”。

本地功能：专注与休息、暂停恢复、历史记录、心情编辑、揉揉立体伙伴、泡泡、呼吸节奏、植物成长、装饰和数据导入导出。心情无需天天填，植物不会枯萎。

0.2 新增芽芽、小猫、小狐狸和企鹅四种3D形象，以及室内书架/鱼缸/零食/睡垫/灯/壁炉和户外池塘/木桥/野餐/凉亭/风铃互动。设置支持角色预设和自定义背景、费用预算、省电模式；聊天支持搜索、会话摘要、用户确认的长期记忆及实际发送内容预览。AI 宠物和场景控制默认关闭，可分别启用受限动作。详见 [0.2 功能与验证记录](docs/0.2功能与验证记录.md)。

## AI 连接和费用

0.3 首批玩法升级：从“玩纸船”进入池塘近景，选择船形、颜色和风向，拖动叶片改变水流，预览并运行路线。伙伴会沿岸陪伴；旅行可命名、收藏、重玩并陈列到小屋。全程离线，详见 [0.3 玩法升级与验证记录](docs/0.3玩法升级与验证记录.md)。

0.4 新增家具拖拽/旋转/复制/撤销和通道检查、8条布置组合事件、双向伙伴藏物、物件高亮与右键菜单、抛球反馈、叫回/停止和可收起侧栏，并局部精修芽芽与小猫。入口为“玩耍与生活”和“布置→进入家具编辑”。详见 [0.4 功能与玩法升级记录](docs/0.4功能与玩法升级记录.md)。性能专项验收按计划后置。

0.5 AI共同创作：独立开启玩法调用和家具预览权限后，聊天可发起组合事件、藏物、抛球、纸船配置／命名和家具位置／旋转预览；宠物权限可切换四种模型。每次最多3步、30秒，移动和短动作按顺序完成，可停止。家具须手动保存，旅行须手动收藏，无自动AI循环。详见 [0.5 AI共同创作与验证记录](docs/0.5AI共同创作与验证记录.md)。

0.5.1：自然表达“走两步”“跑几步”“去书架旁／窗边”即可请求小屋移动，无需坐标。本地寻找可达目标并按走／跑模式完成短途动作；提示词明确AI代表当前宠物，未授权时提示开启权限。

0.5.2：悬浮聊天也能请求短途走/跑、睡眠、舞蹈、随机模式和玩法。设置支持编辑本地 `interaction-rules.json`；明确短句命中规则不调用AI、不需密钥，未命中才进入AI聊天。权限与执行边界保持一致，本地记录排除出AI历史。详见[悬浮动作与本地规则](docs/0.5.2悬浮动作与本地规则.md)。

在设置中填写你自己的 API Key，点击测试连接，再保存。预设地址及模型依据 [DeepSeek 官方文档](https://api-docs.deepseek.com/zh-cn/)，高级设置允许修改；并非所有兼容接口都经过验证。

连接测试和聊天可能计费。启动、装饰、专注、放松、模型切换和记忆编辑不会调用 AI。一次操作最多一个推理请求，默认输出最多512 tokens，可在128–1024之间配置；上下文使用2048/4096/8192的保守字节预算，默认4096。不会自动重试、总结记忆或切换服务。该限制无法保证月账单上限，请在服务商侧管理额度。

没有真实密钥时，只能验证模拟服务行为，不能宣称真实付费接口已经完成联调。输入为中文或较复杂文字时，UTF-8 保守预算可能在 2,000 字符之前拦截内容，会保留草稿并明确提示。

## 个人数据

数据在 `%LOCALAPPDATA%\Forme`，普通记录为本地 SQLite，未额外加密。密钥单独使用 Windows 当前用户凭据保护，不进入导出文件。详见 [隐私说明](docs/隐私说明.md)。

导入只替换文件明确包含的类别；导入前保存一个恢复备份。删除会处理应用管理备份，但无法删除你保存在其他位置的导出文件或服务商收到的数据。

导入和导出共用 512MB、100万条记录上限，逐记录处理；导入前备份，可在设置中恢复上次导入前的数据。

## 开发

遵守根目录 [AGENTS.md](AGENTS.md)。设计见 [初版产品设计](docs/初版产品设计.md)，实际证据与尚未验证事项见 [实现与验证记录](docs/实现与验证记录.md)。

使用 .NET SDK 10.0.401；可安装到项目 `.tools/dotnet`，或使用匹配的系统 SDK。SDK 校验信息保存在本次工作区 `.tools/sdk-download.json`；开发工具和测试输出不提交。

```powershell
# 构建、标准测试及覆盖率报告
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Test

# 在交互式Windows桌面执行WPF冒烟检查
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Smoke

# 生成自包含程序及安装包
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Package

# 资源采样：默认预热2分钟、采样10分钟；比较时每个状态单独启动进程
.tools/dotnet/dotnet.exe build src/Forme.App/Forme.App.csproj -c Release --no-restore '-p:FormeDevelopmentTools=true'
.tools/dotnet/dotnet.exe src/Forme.App/bin/Release/net10.0-windows/Forme.dll --probe

# 可选状态：pet-idle/pet-sleep/pet-walk/pet-run、house-indoor-idle/house-indoor-moving、
# house-outdoor-idle/house-outdoor-moving、floating-chat-idle/floating-chat-streaming-mock、
# tray-cold/tray-after-100-switches。流式模拟不会调用AI服务。
.tools/dotnet/dotnet.exe src/Forme.App/bin/Release/net10.0-windows/Forme.dll --probe --probe-state house-outdoor-moving --probe-warmup 120 --probe-seconds 600
```

检查数据与截图在 `artifacts`，与正式个人数据隔离。自动化检查不向真实 AI 服务发送请求。不要将私人的数据库、密钥或导出文件放入仓库。

运行结构：WPF 展示与 Windows 集成、独立本地活动逻辑、SQLite 存储、独立流式 AI 接口。3D 几何由代码生成并复用，不加载外部模型，不使用常驻后端。

标准测试可直接运行 dotnet test，按 TestCategory 筛选；TRX 和覆盖率在 artifacts/test-results。Windows CI 执行相同构建、测试、打包和安装包自检，界面与GPU/混合DPI检查在本机交互式桌面执行。审核整改进度见 [整改实施记录](docs/审核意见与下一步计划/整改实施记录.md) 和 [v2 执行记录](docs/审核意见与下一步计划/v2执行记录.md)。
