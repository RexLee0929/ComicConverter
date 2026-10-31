using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace ComicConverter
{
    /// <summary>
    /// 「帮助 → 关于」的内容源：src/app/ABOUT.md（EmbeddedResource）。
    /// 文案在 Markdown 里维护，排版参数逐项对齐 txtFormat 的 about（QTextBrowser 渲染的
    /// ABOUT_TEXT 那段 HTML），实测像素换算成 DIP 后的结果：
    ///
    ///   ## **{NAME}** **{VERSION}**   标题：18px 半粗，程序名正文色，版本号强调色
    ///   紧随的一个段落                  副标题：12px 灰字
    ///   **小节**                       小节标题：12px 半粗；若下一段是普通行（中间可隔一个空行），
    ///                                  与其合并为同一段（= HTML 的 &lt;b&gt;标题&lt;/b&gt;&lt;br&gt;行…）
    ///   - 条目                         项目符号：圆点用强调色，
    ///                                  悬挂缩进——圆点距正文左边 31.3、条目文字 40.5，回行对齐文字
    ///   普通行                         同一空行块内的相邻行合并为一段（硬换行），行内支持 **加粗**
    ///   ©  开头的行                     所在段落判定为页脚：11px #8A8A8A
    ///
    /// 间距：所有块只给「上边距 12」，块间基线距 = 行距 + 12（= QTextBrowser 里 p 的 12px 外边距
    /// 折叠后的效果）；列表项之间 0，只有列表首项带 12。行距一律用字体自然行距，不写死 LineHeight
    /// ——写死 18 会让每行比 txtFormat 松 2.4px，整页看起来"间距都一样、就是不对"。
    /// 占位符 {NAME}/{VERSION}/{AUTHOR}/{TAGLINE} 启动时由 Brand 替换。
    /// </summary>
    public static class AboutDoc
    {
        // ---- 排版常量：源自 txtFormat 关于页的实测值（DIP，96dpi） ----
        const double BlockGap = 12;      // 块间额外留白（QTextBrowser 的 p 外边距）
        const double ListIndent = 40.5;  // 列表文字左边界（相对正文左边界）
        const double BulletHang = 9.2;   // 圆点相对条目文字的悬挂量（圆点起点 = 40.5 - 9.2）
        const double TitleSize = 19;
        // 注意：FlowDocument 会把首段的上边距和末段的下边距吃掉（实测 extent 完全不变），
        // 所以卡片上下留白放在 AboutDialog.xaml 的卡片内边距里，不在这里加。= 0 保留字段做说明用。
        const double FooterSize = 11;

        static Brush _accent, _ink, _muted, _dim;

        public static FlowDocument Parse(string md, Brush accent, Brush ink, Brush muted, Brush dim)
        {
            _accent = accent; _ink = ink; _muted = muted; _dim = dim;

            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontSize = 12,
                Foreground = ink
            };

            string[] lines = md.Replace("\r\n", "\n").Split('\n');
            var pending = new List<string>();          // 当前空行块里攒的普通行
            bool footerBlock = false;                  // 当前块里出现过 © 行 → 页脚样式
            bool subtitleDone = false;
            bool listStarted = false;                  // 上一块是列表项

            Action flush = delegate
            {
                if (pending.Count == 0) return;

                var p = new Paragraph { Margin = new Thickness(0, BlockGap, 0, 0) };
                for (int i = 0; i < pending.Count; i++)
                {
                    if (i > 0) p.Inlines.Add(new LineBreak());
                    AddInline(p, pending[i]);
                }

                if (footerBlock)
                {
                    p.FontSize = FooterSize;
                    p.Foreground = _dim;
                    p.Margin = new Thickness(0, BlockGap, 0, 0);   // 段尾留白由卡片内边距兜（末段下边距会被 FlowDocument 吃掉）
                }
                else if (!subtitleDone && doc.Blocks.Count > 0)
                {
                    // 标题后的第一个段落固定当副标题：灰字、紧贴标题（12px 灰）
                    p.Foreground = _muted;
                    p.Margin = new Thickness(0);
                    subtitleDone = true;
                }

                doc.Blocks.Add(p);
                pending.Clear();
                footerBlock = false;
                listStarted = false;
            };

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();

                if (line.Length == 0)
                {
                    flush();
                    continue;
                }

                // 标题：#+ 空格（# 与 ## 都认），去掉 ** 加粗标记后交给 TitlePara 上色
                if (line.StartsWith("#") && line.TrimStart('#').Trim().Length > 0)
                {
                    flush();
                    doc.Blocks.Add(TitlePara(line.TrimStart('#').Trim().Replace("**", "")));
                    continue;
                }

                // 小节标题：整行被 ** 包住
                if (line.StartsWith("**") && line.EndsWith("**") && line.Length > 4)
                {
                    flush();
                    string head = line.Substring(2, line.Length - 4).Trim();

                    // 小节标题后若跟着普通行（允许隔一个空行），合并进同一段 ——「快捷键」的样子；
                    // 后面直接是列表/标题/另一个小节则各走各的（「主要功能」的样子）。
                    int j = i + 1;
                    if (j < lines.Length && lines[j].TrimEnd().Length == 0) j++;   // 只跳过一个空行
                    var follow = new List<string>();
                    while (j < lines.Length)
                    {
                        string nxt = lines[j].TrimEnd();
                        if (nxt.Length == 0) break;
                        if (nxt.StartsWith("#") || nxt.StartsWith("- ")
                            || (nxt.StartsWith("**") && nxt.EndsWith("**") && nxt.Length > 4)) break;
                        follow.Add(nxt);
                        j++;
                    }

                    var p = new Paragraph { Margin = new Thickness(0, BlockGap, 0, 0) };
                    p.Inlines.Add(new Run(head) { FontWeight = FontWeights.SemiBold });
                    for (int k = 0; k < follow.Count; k++)
                    {
                        p.Inlines.Add(new LineBreak());
                        AddInline(p, follow[k]);
                    }
                    if (follow.Count > 0) i = j - 1;     // 被合并的行直接跳过

                    doc.Blocks.Add(p);
                    listStarted = false;
                    continue;
                }

                // 项目符号列表：圆点强调色（与头注释一致），悬挂缩进（回行对齐条目文字，不对齐圆点）
                if (line.StartsWith("- "))
                {
                    flush();
                    var p = new Paragraph
                    {
                        Margin = new Thickness(ListIndent, listStarted ? 0 : BlockGap, 0, 0),
                        TextIndent = -BulletHang
                    };
                    p.Inlines.Add(new Run("• ") { Foreground = _accent });
                    AddInline(p, line.Substring(2).Trim());
                    doc.Blocks.Add(p);
                    listStarted = true;
                    continue;
                }

                // © 行：所在段落按页脚处理（对齐 txtFormat 的 #888 小字）
                if (line.StartsWith("© ")) footerBlock = true;
                pending.Add(line);
            }

            flush();
            return doc;
        }

        /// <summary>行内 **加粗** 解析；不支持嵌套与斜体（关于页用不到）。</summary>
        static void AddInline(Paragraph p, string text)
        {
            var parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                var r = new Run(parts[i]);
                if (i % 2 == 1) r.FontWeight = FontWeights.SemiBold;
                p.Inlines.Add(r);
            }
        }

        /// <summary>标题段：程序名正文色，版本号（最后一个空格后、数字开头的 token）用强调色。</summary>
        static Paragraph TitlePara(string text)
        {
            var p = new Paragraph
            {
                Margin = new Thickness(0),
                FontSize = TitleSize,
                FontWeight = FontWeights.SemiBold
            };

            int sp = text.LastIndexOf(' ');
            string version = sp >= 0 ? text.Substring(sp + 1) : "";
            string name = sp >= 0 ? text.Substring(0, sp) : text;

            if (version.Length > 0 && char.IsDigit(version[0]))
            {
                p.Inlines.Add(new Run(name + " ") { Foreground = _ink });
                p.Inlines.Add(new Run(version) { Foreground = _accent });
            }
            else
            {
                p.Inlines.Add(new Run(text) { Foreground = _ink });
            }
            return p;
        }
    }
}
