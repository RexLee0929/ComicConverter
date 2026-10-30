# Changelog

本项目的所有显著变更都记录在这个文件里。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.0.3] - 2026-10-30

### 变更

- 标题栏在程序名后显示版本号（`ComicConverter 1.0.3`），版本号仍只从 `version.props` 读，随构建自动更新
- 安装包文件名带上版本号（`ComicConverter-1.0.3-setup.exe`），从 `version.props` 自动取，Releases 页里一眼能对上是哪个版本

### 修复

- 安装向导许可页的 MIT 原文不再整篇挤成一段（内嵌 LICENSE 是 LF 行尾，展示前统一转成 CRLF）

## [1.0.2] - 2026-10-30

### 新增

- 「卷名 / 话名也繁转简」开关：关掉可保留括号里的原始话名（`馮寶寶03`），系列名照旧转换
- 「帮助 → 关于」窗口：文案写在 `src/app/ABOUT.md`，宽 640 固定、高度按内容自动拟合

### 变更

- 菜单改为数据表驱动，白色圆角卡片样式，图标换成 24×24 线性图标
- 帮助菜单精简为「关于」和「邮箱」两项
- 界面强调色改为从 `app.ico` 派生，代码里不再写死色值
- 状态栏只保留运行态：当前阶段、完成计数、当前生效的目标 / 方向 / 并发
- 面向开发者的内容从 README 拆出，单独放在 `DEVELOPMENT.md`
- 版本号与作者信息统一到 `src/version.props` 一处
- README 与 LICENSE 编入安装器，装完可在安装目录查看
- 文档不再提及免安装版，只提供安装包

## [1.0.1] - 2026-10-30

### 新增

- **EPUB → CBZ**：按 OPF 的 `spine` 取真实页序，图片字节原样拷贝，不重编码、不降低画质
- **双服务器格式**：Kavita（`Volume` 排序）/ Komga（`Number` 排序）/ 双兼容，两个字段都写
- **阅读方向自动判断**：页面比例先认条漫，再按元数据 → 出版社 → 标题文字 → 本地缓存 → Wikidata 查始发国；结论写进 ComicInfo 并缓存，同一本不再联网
- **CBZ 格式互转**：已有 CBZ 在 Kavita / Komga 两套命名与元数据之间互换，图片不动
- **繁转简**：内置 OpenCC 词表（台湾正体 / 通用繁体），含假名的日文标题整条保留
- **番外处理**：独立成系列，或作小数号接在正传后
- **名称清洗与命名模板**：非法字符统一替换，文件名可用 `{series}` `{vol}` `{vol3}` 自定义
- **预演**：只读元数据、不写文件，先确认命名和页数再转换
- **断点续跑**：目标已存在记「跳过」，默认 6 并发（可 1–32）
- **安装 / 卸载向导**：可选「仅为我安装」（无需管理员）或「为所有用户安装」（请求一次 UAC）

[1.0.3]: https://github.com/RexLee0929/ComicConverter/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/RexLee0929/ComicConverter/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/RexLee0929/ComicConverter/releases/tag/v1.0.1
