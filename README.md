# FeatureDeck

**给 Windows 的隐藏开关装一个控制台。**

Windows 内部藏着几千个通过 A/B 灰度下发的特性开关（Feature Staging）——微软用它们分批推送新界面和新功能，而你在「设置」里永远找不到它们。FeatureDeck 把这 2800+ 条配置摊到一张表上：查得到名字、看得懂含义、改得动状态，改错了还能一键还原。

它是 [ViVe](https://github.com/thebookisclosed/ViVe) 的 WinUI 3 图形界面：沿用原版的内核调用，补上了官方名称字典、搜索筛选、批量操作和一套防误触保护，并修复了原版在 Windows 11 24H2/25H2 上必现的访问违规崩溃。

<small>原名 ViVe 图形化工具（ViVeTool.GUI），fork 自 [thebookisclosed/ViVe](https://github.com/thebookisclosed/ViVe)。</small>

| | |
|---|---|
| **上游项目** | [thebookisclosed/ViVe](https://github.com/thebookisclosed/ViVe)（原作者 @thebookisclosed） |
| **本 fork 的改动** | 在 ViVe 内核层之上增加 WinUI 3 图形界面、特性名称字典、搜索筛选、双存储切换、批量操作等 |
| **技术栈** | C# / WinUI 3 / .NET 8（WindowsAppSDK 1.8，unpackaged 自包含部署） |
| **许可证** | GPLv3（与上游一致，见 [LICENSE](LICENSE)） |

> 本 fork 基于 ViVe（GPLv3）的公开源码移植内核层，同样以 GPLv3 授权。**仅供研究用途**，修改系统特性配置存在风险，请自行判断后果。

## 功能（当前版本 v0.1，里程碑 M0–M4）

| 功能 | 说明 |
|---|---|
| 特性总览 | 展示系统全部特性配置（ID、名称、优先级、状态、类型、变体），本机实测 2800+ 条 |
| 名称字典 | 内置官方 `FeatureDictionary.pfs`，自动把数字 ID 翻译成可读名称 |
| 搜索 / 筛选 | 按名称或 ID 实时搜索；按「被改过 / 可修改 / 实验配置 / 有订阅」筛选 |
| 双存储 | 在 运行时（立即生效）与 启动时（重启生效）之间切换或同时操作 |
| 启用 / 禁用 | 单行或批量操作；写入启动存储后自动设置待重启标记 |
| 还原 | 单条还原用户覆盖；「全部还原」需二次确认 |
| 保护机制 | 系统镜像管理（Immutable）的条目置灰不可改，防止写入被拒绝 |
| 中英双语 | 界面支持简体中文/英文，默认跟随系统语言；系统语言不受支持时自动弹出语言选择界面，也可随时手动切换（重启生效） |
| 启动存储修复 | 一键修复被系统写坏的 Last Known Good 存储 |

## 系统要求

- Windows 10 build 18963 及以上（Windows 11 均可）
- 需要**管理员权限**运行（写入配置、修改注册表所必需）

## 构建与运行

方式一：双击根目录的 `build_and_run.cmd`（自动构建并启动）。

方式二：手动构建

```bash
cd src/FeatureDeck
dotnet build -c Release -p:Platform=x64
```

启动（会弹 UAC 提权确认）：

```
src\FeatureDeck\bin\x64\Release\net8.0-windows10.0.19041.0\FeatureDeck.exe
```

已启用 WindowsAppSDK 自包含部署，**无需预先安装任何运行时**，拷贝输出目录即可绿色运行。

## 项目结构

```
src/
  FeatureDeck/          主程序（WinUI 3）
    Native/              内核层：ntdll P/Invoke、结构体位域、FeatureManager（从 ViVe 移植）
    Services/            服务层：查询合并、字典映射、NTSTATUS 翻译
    Models/              数据模型
    ViewModels/          主界面视图模型
    Converters/          XAML 转换器
    Assets/              官方特性名称字典
  ViVeProbe/             控制台探针（诊断工具，验证内核层工作是否正常）
```

## 已知注意事项

1. **25H2 兼容性修正**：原版 ViVe 的「先传 null 缓冲区再取数量」调用方式在 Windows 11 24H2/25H2 上会触发访问违规崩溃，本项目已改为「预分配缓冲区 + 容量 in/out」的标准用法，实测正常。
2. 受保护优先级（ImageDefault / EKB / ImageDefaultEditionOverride / Security / ImageOverride）不可写入，界面已置灰。
3. 写入「启动时」存储后需重启系统才生效。
4. 查询不需要管理员权限，但写入操作必须有。
5. 同一个功能 ID 可能对应多个优先级条目，表格按 (ID, 优先级) 分行展示。

## 命令行诊断

内核层出问题时，可用探针快速定位：

```bash
cd src/ViVeProbe
dotnet run -c Debug -p:Platform=x64
```
