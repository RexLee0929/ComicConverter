using System.Collections.Generic;

namespace ComicConverter
{
    /// <summary>
    /// 菜单命令表。菜单结构完全由这里的数据生成，XAML 里不写死任何菜单项：
    /// 加功能 = 在表里加一条 + 在 MainWindow.Run 里加一个分支。
    /// 括号里的 (_X) 是助记符，WPF 会显示成「文件(F)」并把 F 画上下划线。
    /// 图标名对应 Icons.Paths 里的键，用程序图标的 Fluent 线性风格。
    /// </summary>
    public static class AppMenu
    {
        public sealed class Item
        {
            /// <summary>显示文本；"-" 表示分隔线。</summary>
            public string Text = "-";

            /// <summary>命令 id，交给 MainWindow.Run 分发。</summary>
            public string Command;

            /// <summary>右侧快捷键提示（只显示，不注册）。</summary>
            public string Gesture;

            /// <summary>可勾选项所属组：mode / reading / target。</summary>
            public string Group;

            /// <summary>组内取值。</summary>
            public string Value;

            /// <summary>图标名（Icons.Paths 的键），可勾选项不需要。</summary>
            public string Icon;
        }

        /// <summary>一条分隔线。</summary>
        public static Item Sep()
        {
            return new Item { Text = "-" };
        }

        /// <summary>普通菜单项。</summary>
        public static Item Of(string text, string command, string icon, string gesture = null)
        {
            return new Item { Text = text, Command = command, Icon = icon, Gesture = gesture };
        }

        /// <summary>可勾选菜单项（同组内互斥，选中显示强调色勾）。</summary>
        public static Item Check(string text, string command, string group, string value)
        {
            return new Item { Text = text, Command = command, Group = group, Value = value };
        }

        /// <summary>整棵菜单树：标题 → 子项。</summary>
        public static List<KeyValuePair<string, List<Item>>> Build()
        {
            var tree = new List<KeyValuePair<string, List<Item>>>();

            // 一级菜单不是「文件 / 编辑 / 查看 / 工具」模板，而是程序自己的词汇：
            // 转换（干活）→ 输出（产出什么）→ 工具（排障）→ 帮助（强制）。

            tree.Add(Pair("转换(_C)", new List<Item>
            {
                Of("添加源目录(_A)…", "add-source", "plus", "Ctrl+O"),
                Sep(),
                Of("预演命名(_P)", "preview", "eye"),
                Of("开始转换(_S)", "start", "play"),
                Of("停止转换(_T)", "stop", "stop"),
                Sep(),
                Of("清空日志(_L)", "clear-log", "clean", "Ctrl+L"),
                Of("清空预演结果(_K)", "clear-preview", "list", "Ctrl+Shift+L"),
                Sep(),
                Of("退出(_X)", "exit", "close", "Alt+F4")
            }));

            tree.Add(Pair("输出(_O)", new List<Item>
            {
                Of("更换输出目录(_D)…", "pick-out", "folder", "Ctrl+Shift+O"),
                Of("打开输出目录(_E)", "open-out", "open", "Ctrl+E"),
                Sep(),
                Check("EPUB → CBZ(_C)", "mode:convert", "mode", "convert"),
                Check("格式互转(_F)", "mode:repack", "mode", "repack"),
                Sep(),
                Check("阅读方向：自动判断(_A)", "reading:auto", "reading", "auto"),
                Check("阅读方向：右开本(_R)", "reading:rtl", "reading", "rtl"),
                Check("阅读方向：左开本(_L)", "reading:ltr", "reading", "ltr"),
                Check("阅读方向：上到下(_B)", "reading:ttb", "reading", "ttb"),
                Sep(),
                Check("目标：双兼容(_O)", "target:both", "target", "both"),
                Check("目标：Kavita(_K)", "target:kavita", "target", "kavita"),
                Check("目标：Komga(_G)", "target:komga", "target", "komga"),
                Sep(),
                Of("恢复默认设置(_S)", "reset-config", "refresh")
            }));

            tree.Add(Pair("工具(_T)", new List<Item>
            {
                Of("打开程序目录(_P)", "open-appdir", "window"),
                Of("打开配置目录(_C)", "open-config-dir", "settings"),
                Sep(),
                Of("打开阅读方向缓存(_K)", "open-cache", "db"),
                Of("清除阅读方向缓存(_X)", "clear-cache", "trash"),
                Sep(),
                Of("查看错误日志(_L)", "open-log", "file"),
                Of("刷新界面(_F)", "reload", "refresh", "F5"),
                Sep(),
                Of("获取 WebView2 运行时(_W)", "webview2", "download")
            }));

            // 帮助菜单与 txtFormat 同构：关于 + 作者邮箱（点一下即复制）。
            // 使用说明不进菜单——文档归仓库，程序内不放。
            tree.Add(Pair("帮助(_H)", new List<Item>
            {
                Of("关于 " + Brand.Name, "about", "info"),
                Of(Brand.Email, "copy-email", "mail")
            }));

            return tree;
        }

        static KeyValuePair<string, List<Item>> Pair(string header, List<Item> items)
        {
            return new KeyValuePair<string, List<Item>>(header, items);
        }
    }
}