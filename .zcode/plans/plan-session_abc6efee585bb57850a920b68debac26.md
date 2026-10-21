## 功能：快捷启用/禁用（按 ID 直达，无需在表格里找）

**交互形态**：顶栏（Row 0 的空闲 `*` 列）新增一个「快捷启用/禁用」按钮 → 点击弹出 ContentDialog：输入特性 ID（实时显示字典里的名称供确认）→ 点「启用」或「禁用」→ 对话框关闭，按当前存储选择器（Runtime/Boot/Both）执行写入，沿用现有静默成功/失败弹窗 + 自动刷新链路。

### 实现步骤

1. **`Services/FeatureService.cs`** — 新增 `LoadById(uint featureId)`：
   - 先查 Runtime，查不到再查 Boot，复用闲置的 `FeatureManager.QueryFeatureConfiguration(featureId, type)`；
   - 把查到的 `RTL_FEATURE_CONFIGURATION` 按 `LoadAll()` 同样的字段映射构造成临时 `FeatureItem`（含 IsWexpConfiguration / Variant / VariantPayload 等写入必需信息），并用 `FeatureNaming.GetName` + `FeatureCategory.Classify` 补名称与分类；
   - 两个存储都没有则返回 null。

2. **`ViewModels/MainViewModel.cs`** — 新增两个公开方法 `QuickEnableByFeatureIdAsync(string idText)` / `QuickDisableByFeatureIdAsync(string idText)`：
   - `uint.TryParse` 校验，非数字/超范围 → 提示「请输入有效的特性 ID」；
   - 先在 `_allItems` 按 ID 查（优先取当前视图存储中存在的行），查不到再调 `FeatureService.LoadById` 兜底，仍没有 → 提示「未找到 ID {0} 的配置」；
   - 把现有 `ApplyStateAsync` 抽出私有 `ApplyItemsAsync(targets, state, actionName, confirm)`（不含 SelectSingle），快捷操作直接传目标列表调用——**不改动表格选中状态**，行内按钮、批量按钮、单 ID 快捷走同一条写入链路；
   - 成功后 Summary 显示结果并 `RefreshAsync()`；写 Boot 沿用现有 `EnsureBootPending` + 重启提示。

3. **`MainWindow.xaml`** — Row 0 Column 2 空闲列加 `Button x:Uid="QuickOpButton"`，`IsEnabled="{x:Bind ViewModel.IsIdle, Mode=OneWay}"`，`VerticalAlignment="Bottom"` 与同行控件对齐。

4. **`MainWindow.xaml.cs`** — 新增 `QuickOpButton_Click`：仿 `ShowLanguagePickerAsync` 用代码构建 ContentDialog：
   - `TextBox`（数字输入）+ 名称预览 `TextBlock`（输入变化时 `uint.TryParse` + `FeatureNaming.GetName` 即时更新，无名称显示「未命名特性 {0}」，ID 不存在时显示「未找到该 ID」）；
   - PrimaryButtonText=「启用」、SecondaryButtonText=「禁用」、CloseButtonText=「取消」；
   - 点启用/禁用 → 记录意图并 `Hide()`，对话框关闭后调用对应 ViewModel 方法（失败时沿用现有错误弹窗）。

5. **`Strings/zh-CN/Resources.resw` + `en-US/Resources.resw`** — 新增键（双语占位符数量必须一致）：
   `QuickOpButton.Content`、`QuickOpTitle`、`QuickOpDescFormat`（含目标存储 {0}）、`QuickIdBox.PlaceholderText`、`QuickNameFormat`、`QuickEnableButton.Content`、`QuickDisableButton.Content`、`QuickInvalidId`、`QuickNotFoundFormat`。

6. **顺手修复（调研中发现的 bug）**：zh-CN 的 `ConfirmBodyEnableFormat`/`ConfirmBodyDisableFormat` 含 3 个占位符但调用只传 2 个实参，中文确认框会 FormatException 回退显示原始串——改为 2 个占位符。

### 验证

- `dotnet build -c Release -p:Platform=x64` 编译通过；
- 手动冒烟：输入已知 ID 看名称预览 → 启用/禁用后列表状态变化、写 Boot 时出现重启标记；输入非法 ID / 不存在 ID → 友好错误提示；确认表格选中状态不受快捷操作影响。

**不做**（保持范围）：不做批量 ID 文本输入、不做对话框内当前状态查询展示、不改快捷键行为。