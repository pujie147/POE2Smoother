# 特效类别中文名的来源、字形与重挖步骤

特效页每一行勾选的名字来自两张随包词表(嵌在 `PsRules.dll` 里),**分文件是因为来源可信度不同**:

| 文件 | 条数 | 来源 | 字形 |
|---|---:|---|---|
| `src/PsRules/Labels/labels-zh.json` | **270** | 客户端自带名字表里挖出的**官方名**,逐字照抄 | 繁體(客户端只有 zh-TW 覆盖表) |
| `src/PsRules/Labels/labels-zh-named.json` | **720** | 官方表里没有对应行时,**我们起的名字** | 繁體(刻意与上一行同字形) |
| 显示期框架 | 2 | `環境: 原名` —— 含义不明的编号,不硬凑 | 繁體 |

解析顺序:官方表 → 我们的表 → 框架兜底(`RulePack.LabelFor`)。**都在显示期解析,不写回规则包** ——
重新生成规则包要重扫整个 GGPK,不该为一个措辞让玩家付这个钱。

当前状态(2026-11-17,用户本机规则包 992 类):无汉字行 **0**;仍是"框架 + 英文原名"的 **2 行**
(`env.aow`、`env.map_uniquereactor_02`);整列里没有简体字形。

## 为什么不转成简体

官方名逐字照抄,**不做繁简转换**。转出来的字符串会被当成官方名,而官方简体译法并不总是逐字对应 ——
这正是上一版 68 条被官方名顶掉的原因(我把 `crossbow` 系列写成「弩·集束榴弹」,官方是「集束擲彈」;
`frost_wall` 我写「霜墙」,官方「冰牆」)。

界面其余部分(菜单、按钮、提示、日志)仍是**简体**;只有这一列是繁體,为的是列内一致、且不与官方数据打架。
想要官方简体名:**在 Steam 里给 PoE2 下载"简体中文"语言包**,然后用 `--language "simplified chinese"` 重跑下面的脚本。
本机验证过:`data/balance/languages.dat` 注册了 `Simplified Chinese (zh-CN)`,但 `data/balance/simplified chinese/` 目前 0 文件。

## 客户端里的数据形状(每条都踩过一次)

- 名字表在 `data/balance/`,共 3,588 个文件,绝大多数 `.datc64`;语言覆盖表在 `data/balance/<语言>/`,
  本机装了 `traditional chinese`(219 张表,66.7 MB)等 9 种语言,**没有简体**。
- **`.datc64` 不是压缩的** —— 列式容器 + **UTF-16LE** 字符串池。普通 `strings` 什么都看不到,必须 `-el`。
- 一行的字符串在池中连续排列:`Id, Name, Description, Icon, …`;Name 为空的行,下一格就是 Description,
  所以取名字要按**形状**判断(短句、无句号、非标识符),不能只看位置。
- 字符串之间有 **2 字节对齐填充**:"紧邻"要允许 0/2/4/6 字节的缝,按相等判断会整表落空。
- 覆盖表与基础表**行序一致、Id 列保持 ASCII**,所以按 Id 文本对齐就能拿到本地化名,不需要 schema
  (`vendor/libggpk3/LibDat2.dll` 是这类表的正式读法,但它要 `schema.min.json` 才能给列命名,只为取名字不值当引)。

## 重挖步骤

```bash
IDX="$HOME/.local/share/Steam/steamapps/common/Path of Exile 2/Bundles2/_.index.bin"
python3 -I scripts/mine-zh-names.py --index "$IDX" --pscli dist/cli-linux-x64/pscli all
#   names  → /tmp/pszh/out/names.tsv      官方类别名(技能 + 区域/幕/联赛)
#   terms  → /tmp/pszh/out/terms.tsv      官方 [英文|繁體] 术语表 → docs/zh-terms.md
#   areas  → /tmp/pszh/out/areas.json     acts / worldareas / leaguenames 全量官方名
# 装了简体中文语言包后加:--language "simplified chinese"
```

产物是**待审清单**,不是直接覆盖词表:脚本只报命中率,人来决定哪几条并入,并保留"官方/我们起名"的文件分界。

### join 规则:为什么不是"包含"

类别 Id 的技能段(`bow_tornado_shot`)与表里的行 Id(`tornado`、`tornado_shot`)用词顺序都不固定。
子串匹配出过三个真错:`cold_arcticarmour` 含 `arc` 被标成「電弧」、`bow_tornado_shot` 抓到德鲁伊的
`tornado` 被标成「龍捲風」、`flaming_wrath` 抓到 `wrath` 被标成「雷霆」。

现在只认**剥掉最多两个类别前缀后的词元集合相等**,前缀资格由"在我们自己的 Id 里作首词出现 ≥5 次"决定
(`bow`/`fire`/`reservation` 算,`shot` 从不打头所以不算),多义一律丢弃不猜。
**一个共有名词不足以认定同一个技能 —— 勾选项挂错名字会让人按错行下手,比挂英文更糟。**

区域类走另一条路:`env.*`/`terrain.*`/`league.*` 用 `worldareas`/`acts`/`leaguenames` 的键直接对齐
(`env.g3_5` ↔ `G3_5` = 「龍蜥濕地」)。带变体后缀的(`g3_3_new`、`act6_four`)另存 `zone-approx.tsv`,
**算我们起的名字**,不算官方。

## 第三张表:`ground-effects-zh.json`(键 = 目录路径前缀)

忽略列表页那棵地面特效树按**目录**逐项勾选,而类别 Id 装不下这件事:`skill.ground_effects_v3` 一个类别里就有
七个目录。所以这张表的键是 `metadata/effects/spells/ground_effects_v3/brittle` 这种路径前缀,值仍是官方繁體。
它不塞进 `labels-zh-named.json` —— 那个文件的含义是"我们起的名字",混进官方名就白拆了。

**收录资格比类别名更严**,两条路二选一:

1. 目录词与客户端某行的英文名**逐字对得上**:`brittle` ↔ Brittle Ground、`scorched` ↔ Scorched Ground、
   `despair_geyser` ↔ `ground_despair_geyser` 的行名 Weight of Despair。
2. 或者 `buffvisuals.datc64` 里那行**直接引用了这个目录里的资源** —— 这是最硬的一条,目录名与官方名不需要
   任何语言上的相似:

| buffvisuals 行 | 它引用的路径 | 官方名 |
|---|---|---|
| `ground_desecration` | `…/ground_effects/**evil**/evil_epk.epk` | 腐化地面 (Desecrated Ground) |
| `ground_consecration` | `…/ground_effects/**holy**/holy_epk.epk` | 奉獻地面 (Consecrated Ground) |
| `ground_profane` | `…/ground_effects_v3/**profane**/profane.epk` | 褻瀆地面 (Profane Ground) |

`evil`/`holy` 这两个目录名和官方名八竿子打不着,靠的就是这张表 —— 客户端自己把资源路径写在了名字旁边。

**两个官方名容易混,记一下**:`褻瀆地面` 是 **Profane** Ground;`腐化地面` 才是 **Desecrated** Ground
(主动技能 `desecrate` 本身叫「褻瀆」)。按字面反推会正好弄反。

**刻意不收的四个**(界面退回显示目录原名,和 `env.aow` 那条同一个规矩):

| 目录 | 为什么不收 |
|---|---|
| `ground_effects/fire` | 燃烧地面/點燃地面/熾炎地面三个都可能是它,多义丢弃 |
| `ground_effects/ice` | 冰緩地面(Chilled)与冰凍地面(Frozen)两个候选 |
| `ground_effects/invisground` | 四张表里查不到对应行 |
| `ground_effects/chimera_smoke` | 行名是 `[DNT-UNUSED] Corrupted Ground`,繁体行连名字格都没有 |

读法上有一条要记住:`buffdefinitions` 一行里**第二格才是短名**,第一格常是描述(名字为空的行尤其如此)。
`scripts/mine-zh-names.py` 目前挖的是类别名与术语对(`names`/`terms`/`areas`),**目录级这张表还没进脚本**,
重挖这三条证据要手工:`pscli read-file` 导出 `buffdefinitions` + `buffvisuals` 的基础版与
`traditional chinese/` 版,按行序对齐 ASCII Id,再看每行的第二格。

> **待核**:`labels-zh.json` 里 `skill.cold_snap_02` = 「迸裂」很可能是误配 —— 「迸裂」是 ActiveSkills 行
> `snap`(英文 *Snap*,消耗被冰凍/感電/點燃敵人的能量)的名字;Cold Snap 官方是 `new_cold_snap` =「霜暴」,
> 而那个目录里是 `grd_fx.ao` + `rig.ao`(grd = ground),它的描述说"產生**冰緩地面**"。
> 同类问题(`cold_snap_*` 这种"元素前缀 + 名字"的变体目录被剥前缀后撞上另一个技能)值得照这条路再扫一遍。

## 界面其它文案

日志/状态/异常里的纯英文句子已改成「中文 / 英文」双语(`MainWindow.axaml.cs`、`AppService.cs` 等);
**异常堆栈本身保持原样** —— 那是提 issue 时要贴的东西,翻译它只会让两边对不上。
命令行 `pscli` 的 72 条帮助/输出文本仍是英文,属有意范围外。

## 验证入口

门槛 `bash scripts/test-all.sh`;词表守卫在 `tests/PsTests/Program.cs` 的 `labels:` 段
(简体字形黑名单、两表不重叠、框架词与 `Classify` 一致、官方样本逐字不变);
看图用 `dotnet run -c Release --project tools/gui-shot`(见 [[headless-gui-screenshot]] 那类经验:
测试全绿也会漏掉行高/换行问题)。
