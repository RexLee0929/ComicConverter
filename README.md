# ComicConverter

把 Mox / Kmoe 等站点下载的漫画 EPUB 批量整理成 **Kavita / Komga** 能正确识别的 CBZ 库。

单文件程序，**无需安装 Python 或 .NET**（依赖系统自带的 .NET Framework 4.8），约 1.4 MB。

作者：**Rex Lee**　·　联系邮箱：**rexlee@rexleepro.com**　·　MIT License

---

## 为什么需要它

这些站点导出的漫画 EPUB 有三个问题，直接丢给漫画服务器会踩坑：

1. **图片文件名是随机 ID**（`vol-005318.jpg`），与页码无关。按文件名排序会得到乱序漫画，实测错位率 **98%**，而且翻两页不一定发现。
2. **新旧两批生成器结构不同**：早期是 calibre 转的，缺 `fixed-layout` 声明；后期是自研生成器，每页包一层 HTML。KOReader 直接打开前者会白屏。
3. **元数据不可靠**：OPF 里的 `calibre:series_index` 全是 `1.0`，照搬会让 40 卷全排成第 1 卷。

本工具按 OPF 的 **spine** 取真实页序，重新计算卷号，写入 ComicInfo，打包成 CBZ。

---

## 功能

- **EPUB → CBZ**：按 spine 取页序，图片字节原样拷贝，**不重编码、不掉画质**
- **双服务器格式**：Kavita（按 Volume 排序，`系列 v01.cbz`）/ Komga（按 Number 排序，`系列 #001.cbz`）/ 双兼容
- **阅读方向自动判断**：先看页面比例认条漫（上到下），再按系列名与作者查证始发国，日本右开、中/港台/韩左开；结论写进 ComicInfo 并缓存，同样内容不再联网
- **格式互转**：已有 CBZ 可在两种命名规则间互换，图片不动
- **繁转简**：内置 OpenCC 词表（台湾正体 → 简体），含假名的日文标题整条保留
- **番外处理**：独立成系列，避免与正传卷序混淆
- **名称清洗**：过滤半角非法字符，归一化全角 `：？＊／` 等跨平台隐患字符
- **预演**：只读元数据、不写文件，先看命名结果
- **配置持久化**：所有设置保存在 `config.json`，退出重进自动恢复

---

## 安装

### 安装版

运行 `ComicConverter-Setup.exe`：

1. **欢迎页** → **选择安装模式**：
   - **仅为我安装（推荐）**：装到 `%LOCALAPPDATA%\Programs\ComicConverter`，不需要管理员权限
   - **为所有用户安装**：装到 `%ProgramFiles%\ComicConverter`，会请求一次 UAC 提权，本机所有用户都能用
2. **许可协议** → **选择安装位置**（可点「浏览」换到任意目录；选完会自动补上 `\ComicConverter` 一层）
3. **附加任务**：桌面快捷方式 / 开始菜单快捷方式 / 安装完成后立即运行
4. **准备安装** → 安装 → **完成**（完成页可勾选「查看使用说明」）

装完可在「设置 → 应用 → 已安装的应用」里正常卸载。

卸载会移除程序文件、快捷方式、注册表登记与 WebView2 缓存；**配置文件 `config.json` 默认保留**（取消勾选则一并删除），方便重装后沿用设置。

### 免安装版

直接双击 `ComicConverter.exe`。整个目录可拷贝到任意位置，`config.json` 会生成在 exe 同目录。

> 同一目录同时只能运行一个实例，避免两个进程互相覆盖 `config.json`。

### 系统要求

- Windows 10 / 11
- .NET Framework 4.8（Win10 1803 以后系统自带）
- WebView2 Runtime（Win11 自带；Win10 未装时程序会给出下载提示）

---

## 使用

主窗口自上而下是**菜单栏 / 内容区 / 状态栏**，整窗一屏放下、不需要滚动：

- **菜单栏**：文件（添加源、选输出目录、打开产物目录）、编辑（清空日志、清空预演、恢复默认）、
  查看（任务类型与阅读方向/目标服务器的勾选切换，和面板双向同步）、工具（配置目录、阅读方向缓存、错误日志）、
  帮助（关于 ComicConverter、作者邮箱）。
- **内容区**：三栏——任务与路径、目标与阅读设置、进度与预演结果；只有日志和列表区域内部滚动。
- **状态栏**：左边当前阶段与完成计数，右边当前生效的目标/方向/并发，一眼确认"点开始会发生什么"。
- 程序名、版本、作者、邮箱集中在「帮助 → 关于」；界面强调色由**程序图标**派生，换图标整套配色自动跟着变。

1. 左侧填**源目录**（可多个，递归查找 EPUB）和**输出目录**
2. 选目标服务器、阅读方向、繁简、番外策略
3. 点「预演」看命名是否符合预期
4. 点「开始」

转换支持断点续跑：目标已存在则跳过，中断后重跑同一条命令即可。

---

## 命名规则

| 原始文件名 | 输出 |
|---|---|
| `Y之樂園 - 卷01 - 高橋直樹.epub` | `Y之乐园 v01.cbz` |
| `一人之下 - 03馮寶寶03 - 米二.epub` | `一人之下 v03 (馮寶寶03).cbz` |
| `一人之下 - 話532-554 - 米二.epub` | `一人之下 v37 (話532-554).cbz` |
| `[Kox][○成]卷01.kepub.epub` | `○成 v01.cbz` |
| `WhatDoesTheFoxSay - 番外03.epub` | `WhatDoesTheFoxSay 番外/… 番外 v03.cbz` |

原卷标签放进**括号**，因为 Kavita 和 Komga 的解析器都把 `()` 视为附加信息，不会误当卷号。

无卷号的卷（`正傳Part1`、`連載前短篇`）按文件名自然序续编，不会全挤成 `v01` 互相覆盖。

---

## ComicInfo 字段

| 字段 | 来源 | 用途 |
|---|---|---|
| `Series` | 文件名解析 | 两边归并系列 |
| `Volume` | 计算得出 | **Kavita** 排序 |
| `Number` | 计算得出 | **Komga** 排序 |
| `Title` | OPF `dc:title` | 显示 |
| `Writer` / `Publisher` / `Year` | OPF | 元数据 |
| `PageCount` / `LanguageISO` / `Manga` | 实际内容 / 判定结果 | 阅读器行为 |
| `OriginCountry` | 自动判断 | 始发国（JP / KR / CN / TW / HK），下次读取时直接复用 |
| `ReadingMode` | 判定结果 | `rtl` / `ltr` / `ttb`，标明按什么方向翻 |
| `Notes` | MOXBID / ISBN / 版权 | 溯源 |

`Volume` 和 `Number` **必须都写**：只写一个会导致另一边乱序。

> `OriginCountry` 与 `ReadingMode` 不是 ComicInfo 标准字段。Kavita（`XmlSerializer`）与 Komga（Jackson `@JsonIgnoreProperties(ignoreUnknown = true)`）都会忽略未知元素，不影响刮削；写进去是为了让本程序下次换目录重跑时直接复用判定结果，不用再联网。

---

## 阅读方向自动判断

「阅读方向」有四个选项：**自动判断 / 右开本 / 左开本 / 上到下**。选「自动判断」时的判定顺序：

1. **页面比例**（离线）：抽样读页面尺寸算「高/宽」中位数。漫画单页约 1.4–1.6，条漫常到 3 以上，所以 ≥ 2.0 判为**上到下**，直接定案。
2. **源文件元数据**：`dc:language`（`ja`→日本、`ko`→韩国、`zh-TW`→台湾…），或自定义 `comicconverter:origin`。
3. **出版社**：集英社 / 講談社 → 日本，東立 / 尖端 → 台湾，玉皇朝 / 天下 → 香港，bilibili / 快看 → 中国。
4. **标题文字**：含假名 → 日本，含谚文 → 韩国。
5. **本地缓存**：`%LOCALAPPDATA%\ComicConverter\origin-cache.json` 里记着上次的结论。
6. **联网查证**（最后手段）：用系列名、作者名查 Wikidata，取「国家/地区（P495）」或「作品语言（P407）」；命中动漫/电影/游戏类目会被排除。

判定结果写进 `ComicInfo.xml`（`OriginCountry` + `ReadingMode`），并落到本地缓存，**同一本第二次不再联网**。

联网细节：单次超时 6 秒，一次扫描最多查 150 本、总耗时上限 120 秒，连续 3 次失败即判定「无网络」并停止联网，全部退回离线判定——断网时不会卡住转换，只会慢十秒左右。

手动选右开本 / 左开本 / 上到时**不联网**，只做离线判定来补 `OriginCountry`。

---

## 构建

需要 .NET SDK（或仅 .NET Framework 4.8 Developer Pack + MSBuild）。

```bash
# 一条命令：主程序 → payload.zip → 安装器 → 同步到发布区
python tools/build.py            # 只编译，不碰发布区
python tools/build.py --apply    # 编译并同步到发布区
python tools/build.py --clean    # 先清 bin/obj 再编译
```

也可以手工分别编译：

```bash
cd src/app && dotnet build -c Release
cd ../installer && dotnet build -c Release   # 需要先用主程序产物打成 payload.zip
```

目标框架 `net48`，产物不依赖 .NET Core / .NET 5+ 运行时。

### 版本号

版本号只在 `src/version.props` 里写一处，主程序和安装器都导入它；安装器运行时从自身程序集读取，界面与注册表用的是同一个值。**改版本只改这一个文件。**

### 图标

`src/app/app.ico` 与 `src/installer/app.ico` 由脚本生成（纯标准库，不依赖 Pillow）：

```bash
python tools/make_icon.py                    # 重新生成两份 .ico
python tools/make_icon.py --preview p.png    # 顺便导出 256px 预览
```

---

## 目录结构

```
src/app/          主程序（WPF + WebView2 + 本地 HTTP 服务）
  Theme.xaml      菜单 / 按钮 / 中性色资源（App 级合并）
  AppMenu.cs      菜单数据表（加菜单项只改这里 + MainWindow.Run）
  MenuBuilder.cs  把数据表变成 MenuItem（可离屏渲染验证）
  Icons.cs        24×24 线性图标路径表
  Accent.cs       从 app.ico 取主色，派生按钮/勾选用的强调色
  AboutDialog.xaml 「帮助 → 关于」（三段式）
  ABOUT.md        关于页文案（Markdown，占位符 {NAME}/{VERSION}/{AUTHOR}/{TAGLINE}）
  AboutDoc.cs     轻量 Markdown → FlowDocument 渲染器
src/installer/    安装 / 卸载向导（WinForms）
src/version.props 版本与作者信息的唯一来源
tools/            构建、图标生成、同步脚本
  ui_render/      界面离屏渲染器（开发用，验证菜单/关于框外观）
```

### 验证界面外观

本机（以及无显卡的会话）里 WPF 窗口用 `PrintWindow`、屏幕 `BitBlt` 抓出来全是纯黑，
**不要用屏幕截图验证**。用离屏渲染器直接出图：

```bash
dotnet run -c Release --project tools/ui_render -- tools/_shots/ui
# 产出 menu-bar.png / menu-file.png / menu-view.png / menu-help.png / about.png
```

它同时会断言菜单弹层真的能打开（`popup.IsOpen`），并打印从图标派生的强调色。

---

## 技术栈

- **UI**：WPF 窗口 + WebView2 渲染界面（同 Teams / VS Code 的架构）
- **后端**：C#，`TcpListener` 实现本地 HTTP（仅监听 127.0.0.1）
- **压缩**：`System.IO.Compression`
- **繁简**：内置 OpenCC 纯文本词表 + 最长匹配
- **依赖**：仅 WebView2 Runtime

---

## 已知限制

- 只能转换**元数据与文件名**，图片内的对白文字无法转简体
- 自动判断可能判错（同名作品、简繁混写、冷门作品查不到），判错就在界面上手动指定方向；已写入 `OriginCountry` 的文件重跑不会被改
- 中文漫画基本刮不到元数据（ComicVine / AniList 对台版漫画命中率极低），所以写入的字段就是最终形态
- WebView2 Runtime 缺失时界面无法加载，程序会提示下载入口

---

## 联系方式

- 作者：Rex Lee
- 邮箱：rexlee@rexleepro.com
- 邮箱**点一下即复制到剪贴板**（不会弹出邮件客户端），粘贴到任意地方即可。
  入口：菜单「帮助 → rexlee@rexleepro.com」，以及「帮助 → 关于」里作者旁边的邮箱胶囊。
  「关于」窗里的正文可以自由选中复制。
- 使用中遇到问题、想提需求，或希望适配新的站点结构，欢迎邮件联系。

## 第三方组件

| 组件 | 用途 | 许可 |
|---|---|---|
| [OpenCC](https://github.com/BYVoid/OpenCC) 词表 | `src/app/Data/` 下的繁简转换表（`TSCharacters` / `TSPhrases` / `TWVariants`），按纯文本词表内嵌 | Apache License 2.0 |
| [Microsoft Edge WebView2](https://learn.microsoft.com/microsoft-edge/webview2/) | 界面渲染运行时（不随程序分发，由系统提供） | Microsoft 软件许可条款 |

本程序自身的代码以 MIT License 分发；上表组件仍归其各自许可约束。

## License

MIT
