# 特效类别中文名的来源与重挖步骤

`src/PsRules/Labels/labels-zh.json` 是特效勾选行的中文名来源(类别 Id → 显示名,嵌入 `PsRules.dll`)。
它有两种来源,**来源不同,写法就不同,这点是刻意的**:

| 来源 | 写法 | 说明 |
|---|---|---|
| 客户端自带名字表挖出来的 | **官方繁體原文** | 逐字照抄,不做繁简转换 |
| 手写的(客户端没有对应行时) | 简体 | 与界面其余部分一致 |

所以同一列目前繁体/简体并存。为什么不把官方繁體转成简体:转换后的字符串会被当成官方名,而官方简体译法
并不总是逐字对应(术语层面会有差别)。**宁可让玩家看到真的官方名,也不看到我们猜的简体名。**
装上官方简体中文语言包后重跑一遍下面的流程,整列可换成官方简体名,混排随即消失。

## 客户端里到底有什么

- 名字表在 `data/balance/`,共 3,588 个文件,绝大多数是 `.datc64`;语言覆盖表在
  `data/balance/<语言>/`,本机装了 `french`、`german`、`japanese`、`korean`、`portuguese`、
  `russian`、`spanish`、`thai`、`traditional chinese`(219 张表,66.7 MB)。**没有 `simplified chinese`**
  ——`data/balance/languages.dat` 里注册了 `Simplified Chinese (zh-CN)`,但要靠 Steam 的语言下载才落地。
- **`.datc64` 不是压缩的**。它是列式容器 + **UTF-16LE** 字符串池;用普通 `strings` 去看会以为它是二进制,
  必须 `strings -el`。这一条曾经把我带偏,别第二次踩。
- 表内一行的字符串在池中**按列顺序连续排列**:`Id, Name, Description, Icon, …`。
  Name 为空的行,其后紧跟的就是 Description(靠长度/句号/大小写形状区分)。
- **覆盖表与基础表的行序一致**,且 `Id` 列保持 ASCII 原文 —— 所以按 Id 文本对齐即可拿到本地化名,不需要 schema。
  (`vendor/libggpk3/LibDat2.dll` 是这类表的正式读法,但它要 `schema.min.json` 才能给列命名;
  只为取名字而不必引入它。)

## 重挖步骤

脚本不进仓库(读的是第三方二进制,产物要人过一遍)。工作目录各自分开:

```bash
IDX="$HOME/.local/share/Steam/steamapps/common/Path of Exile 2/Bundles2/_.index.bin"
mkdir -p /tmp/pszh/data /tmp/pszh/bin        # 数据与脚本分目录
pscli read-file --index "$IDX" --path "data/balance/activeskills.datc64" \
                --out /tmp/pszh/data/activeskills_en.datc64
pscli read-file --index "$IDX" --path "data/balance/traditional chinese/activeskills.datc64" \
                --out /tmp/pszh/data/activeskills_tw.datc64        # 官方简体到位后换成 simplified chinese/
python3 -I …        # 解析 + join,产物是 id → 官方名 的待审清单
```

解析要点(踩过的坑):
1. 字符串以 `0000` 结束,但池里还有 2 字节对齐填充 —— “紧邻”要允许 0/2/4/6 字节的缝,按等式判断会全表落空。
2. `Id` 判定用 `[a-z0-9_]+` 形状即可,但 Name 槽要排除形状像描述的(含 `。`/句号、超长)。

## join 规则(为什么不是"包含")

类别 Id 的技能段(`crossbow_stormblast`、`bow_tornado_shot`)和 ActiveSkills 的行 Id(`stormblast_bolts`、
`tornado`)用词顺序都不固定,所以 join 只做**词元集合相等**,并允许剥掉最多两个**类别前缀**——
前缀资格由"在我们自己的 Id 里作为首词出现 ≥5 次"决定(`bow`、`fire`、`cold`…;`shot` 不算,它从不打头)。

曾经用"子串包含",结果:`cold_arcticarmour` 因为含 `arc` 被标成「電弧」,`bow_tornado_shot` 抓到德鲁伊的
`tornado` 被标成「龍捲風」,`flaming_wrath` 抓到 `wrath` 被标成「雷霆」。**一个共有名词不足以认定是同一个技能。**
现在:多义/多条命中一律丢弃,不猜。丢弃与漏配的行由显示期框架兜底(`RulePack.LabelFor` → 「技能: 原名」),
所以覆盖率低只是名字粗,不会再出现整行纯英文。

## 当前状态(2026-11-17)

| 指标 | 数 |
|---|---|
| 类别总数(本机规则包) | 992 |
| 词表条目 | 427 |
| 其中来自客户端官方名(逐字繁体) | 190 —— 新增 122、改写我原先手写 68 |
| ActiveSkills 可挖行 / 我们技能段命中 | 1,146 行 / 190 条(歧义 0) |
| 显示后无任何汉字的行数 | **0**(改前 464) |

改写那 68 条的原因:官方用词和我造的不是一回事。例:`crossbow` 官方是「十字弓射擊」不是「弩」,
`frost_wall` 是「冰牆」不是「霜墙」,`contagion` 是「瘟疫」不是「传染」,`bow_snipe` 是「狙擊」不是「弓·狙击」。
这条教训值得记住:**能给官方名就别自己起名。**
