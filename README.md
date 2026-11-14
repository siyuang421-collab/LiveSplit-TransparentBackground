# LiveSplit 透明背景插件

适用：官方原版 LiveSplit 1.8.37 便携版（Windows 10/11 x64）。本文不写死任何人的安装目录或 `.lss` 路径。

## 这个插件解决什么

- 每次启动 LiveSplit 后**自动**把计时器窗口背景抠成透明，不需要每次手动改布局；
- 分段列表每一行也透明，不再出现“一行黑、一行透明”；
- 分段文字保持 LiveSplit 默认的白色（`Color.White`）；
- 保留左键拖动计时器、右键弹出 LiveSplit 菜单。

## 安装

1. 先完全退出 LiveSplit（任务管理器里确认没有 `LiveSplit.exe`）。
2. 把 `LiveSplit.TransparentBackground.dll` 复制到你自己 LiveSplit 目录下的：
   `<你的 LiveSplit 安装目录>\Components\`
3. 启动该目录下的 `LiveSplit.exe`，打开你自己的 `.lss` 文件。
4. 插件会自动生效；如果之前已经开着 LiveSplit，必须重启一次。

正常安装只需要这一个 DLL，不需要手动把组件加进布局。  
如果启动后没有透明效果，先确认 `Components` 目录里 DLL 没有被改名或删除；仍无效时，右键计时器打开 `Edit Layout`，检查组件列表里是否出现“透明背景”。

卸载：退出 LiveSplit，删除 `<你的 LiveSplit 安装目录>\Components\LiveSplit.TransparentBackground.dll`，重启即可。

## 原理（实测结论）

- 用 `WS_EX_LAYERED` + `SetLayeredWindowAttributes` 的颜色键做透明，键色是专用色 `#010203`，避免和布局背景/文字颜色撞色。
- 布局背景被强制为 `SolidColor`，背景色、`form.BackColor`、分段行四个背景色都设为同一个键色，保证整块背景能被抠掉。
- 只改 `SplitsSettings` 颜色不够：已经创建的分段行还在用旧画刷。插件会在 UI 线程调用 `RebuildVisualSplits` 重建行组件。
- 行与行之间的深色带来自 `SplitsComponent` 的 `ShadowImages` 阴影缓存（字典），不是背景色；插件在重建后清空这个字典，并清掉每个 `SplitComponent.ShadowImage`，行才真正全透明。
- 文字不是被颜色键抠掉：日志确认文字颜色原本是白色；透明后白字在雪地上对比偏低，这是原版配色本身的问题。当前版本按要求保持默认白色，并在重建后刷新。
- 分段行之间仍会看到很细的黑色分隔线，那是 LiveSplit 的行分隔线/标题栏，不是残留的半透明行背景。

## 左键拖动 / 右键菜单

颜色键透明会让透明像素在系统层面鼠标穿透，所以插件用一个几乎不可见的鼠标代理窗盖在计时器上：

- 左键：代理窗捕获鼠标，按光标位移用 `SetWindowPos` 移动计时器窗口；
- 右键：向计时器句柄投递 `WM_RBUTTONDOWN`、`WM_RBUTTONUP`、`WM_CONTEXTMENU`，弹出原生菜单；
- 代理窗每 250ms 重新置顶，避免被聊天等置顶窗口压住；
- 如果 LiveSplit 自己给计时器加了 `WS_EX_TRANSPARENT`（跑动时鼠标穿透），代理窗会自动隐藏，避免打架。

## 已验证

- 游戏内 1920×1080 截图：分段行透出雪地，行与行之间没有整行黑底；默认白色分段名/时间可见。
- 日志关键行：
  - `apply key=#010203 ... ok=True`
  - `cleared split shadow images`
  - `rebuild splits done`
- 左键拖动、右键菜单在代理窗版本上已实测成功（旧截图 `右键菜单.jpg` 可对照）。
- 官方 1.8.37 原版与现有安装做过逐文件比对：89 个程序文件相同，唯一差异是 `speedruncom_game_names.json` 这个游戏名数据文件；现有安装的透明、拖动、右键功能已实测正常。

## 兼容性 / 分发说明

- 已验证：官方原版 LiveSplit 1.8.37 便携版，Windows 10/11 x64。
- **汉化版未实测**：如果只是替换界面文本/语言资源，通常可以使用；如果改了组件类型名、程序版本或打包结构，不保证可用，需要按对应版本重新编译。
- 不依赖具体 `.lss` 文件名或盘符；只要 LiveSplit 能正常加载、并且 `.lss` 里确实有分段，插件就会对这些分段生效。
- **空 `.lss` 不会有分段文字**：如果文件里只有一个空 `<Segment><Name></Name>`，LiveSplit 自己就只显示标题和 `Previous Segment`，任何插件都无法显示不存在的分段名。
- 布局里必须包含 `Splits` 组件；只有计时器、没有分段列表的布局，自然没有分段文字可显示。
- 其他 LiveSplit 大版本/改过类型名的版本不保证可用：插件按 `TimerForm`、`SplitsComponent`、`SplitsSettings` 反射，类型名变化时会静默不处理，不会崩溃，但也不会透明。
- 分发时只需要 `Components\LiveSplit.TransparentBackground.dll` 和本说明；源码可选。别人电脑上的 LiveSplit 安装路径不同不影响，放进对应 `Components` 目录即可。

## 已知限制

- LiveSplit 的“New updates are available”更新提示会反复弹出并遮挡计时器，这是 LiveSplit 自身行为，不是插件；关掉提示即可，插件不负责禁用更新检查。
- 同一时间只能开一个 LiveSplit 实例；先杀光 `LiveSplit.exe`，再用 `LiveSplit.exe -s "<你的 .lss>"` 启动，否则可能报端口占用崩溃。
- 当前布局的分段行背景会被插件强制成透明；文字保持默认白色。如果以后想让文字更亮，把源码里的 `SplitTextColor` 改成 `Color.FromArgb(255, 255, 215, 0)` 重新编译即可。

## 源码/重新编译

源码：`TransparentBackground.cs`。在源码目录用 PowerShell 执行：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library `
  /out:LiveSplit.TransparentBackground.dll `
  /reference:"<你的 LiveSplit 安装目录>\LiveSplit.Core.dll" `
  /reference:"<你的 LiveSplit 安装目录>\UpdateManager.dll" `
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll TransparentBackground.cs
```

程序集特性必须保留，否则 LiveSplit 根本不会加载这个组件：

```csharp
[assembly: LiveSplit.UI.Components.ComponentFactory(typeof(LiveSplit.TransparentBackground.TransparentBackgroundFactory))]
```

日志：`%TEMP%\LiveSplitTransparent.log`。

## 许可证

本项目采用 MIT License，详见 [LICENSE](LICENSE)。
