# 开发说明

这份文档写编译、代码结构和几个踩过的坑。想直接用程序的话看 [README.md](README.md) 就够了。

## 环境

- .NET SDK 6 以上（够编译 `net48` 就行），或者 .NET Framework 4.8 Developer Pack + MSBuild
- Python 3（只有构建和打包脚本用得到，程序本身不依赖）
- Windows

## 编译

一条命令跑完主程序、`payload.zip` 和安装器：

```bash
python tools/build.py            # 编译，产物复制到 artifacts/，不碰发布区
python tools/build.py --apply    # 编译并把源码同步到发布区（不含构建产物）
python tools/build.py --clean    # 先清 bin/obj 再编译
```

手工分步也可以，注意安装器依赖主程序产物打成的 `payload.zip`：

```bash
cd src/app && dotnet build -c Release
cd ../installer && dotnet build -c Release
```

构建产物统一复制到编辑区根的 **`artifacts/`**（每次构建整目录重建，不残留旧版本）：安装包是 `artifacts/ComicConverter-<版本>-setup.exe`（文件名带版本号，构建时从 `version.props` 自动取），旁边是主程序运行文件。`src/*/bin/` 只是编译中间目录。发布区仓库不放任何构建产物，安装包直接从 `artifacts/` 传 GitHub Releases。`payload.zip` 在 `.gitignore` 里。

## 改动范围

- 菜单项在 `src/app/AppMenu.cs` 的数据表里加，命令实现在 `MainWindow.Run` 里加一个 `case`
- 「帮助 → 关于」的文案在 `src/app/ABOUT.md`，Markdown 格式，占位符 `{NAME}` `{VERSION}` `{AUTHOR}` `{TAGLINE}` 启动时替换
- 界面本体是 `src/app/Web/index.html`，一个文件装完 HTML、CSS 和 JS，经本地 HTTP 服务加载
- 加界面开关时三处一起改：`Web/index.html` 的控件 + `getCfg()` + `loadCfg()`，`Api.cs` 的 `DefaultConfig()` 加默认值，`Core.cs` 里用 `GetBool` / `GetStr` 读（`t2s_label` 就是一个例子：控制括号里的原卷标签要不要跟着繁转简）
- 版本号只改 `src/version.props`，主程序和安装器都导入它；安装器文件名（`ComicConverter-<版本>-setup.exe`）跟着自动变

**README.md 和 LICENSE 会被编进安装器**（`src/installer/Installer.csproj` 里的 `EmbeddedResource`），装完写到安装目录，完成页可以打开。所以改完 README 要重新编译安装器，否则新编出来的安装包带的还是旧文案。也因为这个，README 要能脱离 GitHub 单独读懂，别放相对路径的图片。

## 图标与配色

两份 `app.ico` 由脚本生成，纯标准库，不依赖 Pillow：

```bash
python tools/make_icon.py                    # 重新生成 src/app 和 src/installer 下的 .ico
python tools/make_icon.py --preview p.png    # 顺便导出 256px 预览
```

强调色没有写死。`Accent.cs` 在启动时读 `app.ico` 的像素，取主色再派生出浅底、深底、边框几个值，塞进 `Application.Resources`。网页那边是另一套：`Page.cs` 把 `index.html` 里三个占位色值替换成实际颜色再发出去。换图标之后两边一起变，但要注意网页里的色值是**故意留的占位符**，不是漏改的硬编码。

## 代码结构

```
src/app/           主程序
  MainWindow.xaml  菜单栏 / WebView2 / 状态栏
  Theme.xaml       菜单、按钮、中性色
  Accent.cs        从图标取主色，派生强调色
  AppMenu.cs       菜单数据表
  MenuBuilder.cs   数据表 → MenuItem
  WindowChrome.cs  原生标题栏的 DWM 配色（主窗口 / 关于框 / 错误框共用）
  MsgDialog.xaml   程序自己的错误框（唯一允许的弹窗），别再用系统 MessageBox
  Icons.cs         24×24 线性图标路径
  Core.cs          扫描、页序、命名、ComicInfo、打包
  Origin.cs        阅读方向判断、联网查证、缓存
  Repack.cs        CBZ 互转
  Chinese.cs       OpenCC 词表繁转简（Data/ 下三张表）
  Api.cs           本地 HTTP 接口与任务调度
  Page.cs          下发 index.html，替换品牌色
  AboutDialog.xaml 关于窗
  AboutDoc.cs      Markdown → FlowDocument
  Web/index.html   界面
src/installer/     安装 / 卸载向导（WinForms）
  WizardShell.cs   向导外壳（页面切换、按钮布局）
  Controls.cs      自绘控件（标题、正文、进度条）
  InstallForm.cs   安装流程各页
  UninstallForm.cs 卸载页
  Program.cs       payload 解包、快捷方式、注册表（卸载项固定写 64 位视图）
  MsgDialog.cs     自绘对话框（Info / Ask），安装器里唯一允许的弹窗
  Theme.cs         从图标派生整套配色
src/version.props  版本与作者信息
tools/
  build.py         构建入口
  make_icon.py     生成图标
  sync_to_code.py  把当前目录同步到另一个发布目录（按哈希比较，目标被外部改过就跳过）
  ui_render/       界面离屏渲染器（含菜单结构自检）
  _shot_page.py    用无头 Chrome 渲染 Web/index.html（确认条 / 提示条）；下划线开头，只在本机用
```

> `tools/` 下划线开头的脚本（`_uicheck_*.py`、`_shot_page.py`、`_stub_102.cs`）是本机的回归 / 截图工具，
> 不同步到发布区；`_shots/` 是它们的输出目录。

主程序是 WPF 外壳加 WebView2 渲染界面，后端用 `TcpListener` 起一个只监听 `127.0.0.1` 的 HTTP 服务承接界面请求，相当于把一个本地网页包在原生窗口里。压缩用 `System.IO.Compression` 的 `ZipArchive`，繁简用 OpenCC 的纯文本词表做最长匹配，除此之外没有第三方依赖。

## 看界面长什么样

本机和无显卡的会话里，WPF 窗口用 `PrintWindow` 或者屏幕 `BitBlt` 抓出来都是纯黑，所以别用屏幕截图验证界面，用离屏渲染器：

```bash
dotnet run -c Release --project tools/ui_render -- tools/_shots/ui
```

会输出 `menu-bar.png`、`menu-convert.png`、`menu-output.png`、`menu-tools.png`、`menu-help.png`、`about.png`、`about-copied.png`、`dlg-error.png`，同时自检：

- 菜单结构（一级 ≥ 2 栏、每栏至少一项可用、有「帮助」、其下有 `about` 命令）—— 任一条不满足会打印 `!` 并以退出码 1 结束；
- 每个弹层真的打开了（`popup.IsOpen`），按标题找一级菜单，不用 `Items[n]` 索引（加一栏就错位）；
- 关于窗的富文本没有溢出（`ExtentHeight` 与 `ViewportHeight` 对比）、`PagePadding` 仍是 `0,0,0,0`。

页面（Web/index.html）用无头 Chrome 看：`python tools/_shot_page.py confirm` 会打桩 `fetch`、调起内联确认条再截图，`toast` 同理。

改主程序之后要先把 `ui_render` 重新编译再跑，否则它引用的还是旧程序集，看出来的效果和改动前一样，很容易误判成「改了没生效」。

## 几个坑

- **`VisualBrush` 不含 Margin**。离屏渲染按元素内容区截的话，截出来会像是「按钮贴底、卡片右边框没了」，其实是渲染器的问题。`ui_render` 里已经改成按 Margin 偏移绘制。
- **`RichTextBox.Document` 赋值后 `PagePadding` 会被强制改回 `5,0,5,0`**，赋值时设一次没用 —— 必须在 `Doc.Loaded` 之后再压一次，并且每次自适应高度都顺手压（`AboutDialog.ZeroPadding`）。漏掉的现象是正文整体右移 5px，跟卡片内边距对不齐。
- **`FlowDocument` 不计首段上边距和末段下边距**，所以关于窗卡片的上下留白只能放在 Border 的内边距里，放段落 Margin 上不会生效。
- **`SizeToContent` 量不准含富文本的窗口**，会量偏小把底部按钮挤出窗口。关于窗改成在 `ContentRendered` 里按 `ExtentHeight − ViewportHeight` 自动校正。
- **弹窗不要用系统 MessageBox，页面里不要用 `alert`**。前者是灰底直角方块、后者是 Chromium 自带样式，放到卡片界面里一眼就不搭。而且先问一句「能不能不弹」：成功 / 已复制 / 已保存走状态栏或页面 `toast()`；清缓存、恢复默认这类不可逆操作走页面底部的内联确认条（主按钮语义红、焦点先落「取消」）；只有未捕获异常这种「必须中断」的才用 `MsgDialog.Error`（写 `error.log` + 状态栏红字 + 带「打开日志」的错误框）。安装器同理走 `src/installer/MsgDialog.cs`；路径非法、权限不足做成页内一行警示色提示 + 主按钮置灰（`InstallForm.ValidateDir`），比弹窗少一次打断。
- **菜单里的快捷键由页面执行**：键盘焦点几乎总在 WebView2 里，宿主窗口的 `PreviewKeyDown` 收不到，所以 `index.html` 里监听 `keydown`，宿主动作（如打开输出目录）用 `post({act:'open-out'})` 回传。窗口级全局热键仍然不注册。
- **卸载登记项必须显式写 64 位注册表视图**：安装器是 AnyCPU + Prefer32Bit（32 位进程），`RegistryView.Default` 会写到 `HKCU\Software\WOW6432Node\…`，64 位的「设置 → 应用」读不到，卸载项等于没写。读取时两个视图都看，清理由此也覆盖旧版本留下的 32 位项。
- **WinForms 的 TextBox 只认 CRLF**。内嵌的 LICENSE 常是 LF 行尾，直接塞进许可页会整篇挤成一段。`Program.cs` 的 `ToCrlf` 在展示前统一行尾。
- **逐文件 `TryDelete` 会吞掉失败**。一个文件被占用或权限不足，卸载照样往下走，最后报「已从您的计算机上移除」——用户打开目录发现东西还在。删完必须用 `Program.RemainingFiles` 复查安装目录（豁免勾了保留的 `config.json` 和 `uninstall.exe` 自己），有残留就走「卸载未完全成功」页。别用「抛自定义异常」表达残留：那是预期内的正常分支，返回值 + 专用页更清楚。
- **卸载向导一进入就检测占用会挡住信息**。程序开着时点卸载，向导直接落在「正在运行」页，用户连卸载内容、安装位置、「保留配置文件」都没看见。检测放在点「卸载」那一刻；`RemoveAll` 开头只复查占用并抛 `StillRunningException`，由向导切回占用页，绝不静默杀进程。
- 端口 `8765` 被上一个没退干净的实例占着的话，本地服务起不来。测试前先 `taskkill`。
- **行尾**：仓库里的文本文件是 LF。用 Windows 上的编辑器改完记得确认没被转成 CRLF。

## 同步到发布目录

如果你习惯在一个干净目录里维护对外仓库，`tools/sync_to_code.py` 可以按白名单（`README.md`、`CHANGELOG.md`、`DEVELOPMENT.md`、`LICENSE`、`.gitignore`、`src`、`tools`、`releases`）同步过去：

```bash
python tools/sync_to_code.py            # 预演，只打印计划
python tools/sync_to_code.py --apply    # 写入（不另存备份，回滚靠目标仓库的 git 历史）
```

默认不删目标里多出来的文件，要删得显式加 `--prune`。目标文件在两次同步之间被外部改过会报冲突并跳过：先 diff 目标版本，有手动改过的地方先拉回来源目录作基底，确认无手改才加 `--force` 覆盖。新增根目录 md 或新目录要先加进脚本顶部的 `SOURCE_FILES` / `SOURCE_DIRS` 白名单，否则永远同步不过去。
