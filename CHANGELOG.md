# 更新日志 / Changelog

发布说明的事实来源。版本号来自 git tag:推 `v1.1` → CI 构建并把下面对应小节作为 Release 正文发布。
本节格式被 `.github/workflows/release.yml` 解析,**小节名必须与 tag 完全一致**(`## v1.1`,不是 `## [1.1]`)。

This file is the source of truth for release notes. Pushing a `vX.Y.Z` tag makes CI build the AppImage
and publish the matching section below as the release body — keep the heading exactly equal to the tag.

## v1.4 – 2026-11-17

### 新增 / Added

- **特效页的类别勾选改成中英双语**:每行现在是「中文 / English」,顺序跟设置页「语言」走
  (选 en 则英文名在前)。之前每行只显示一种语言,而中文模式下大多数技能行显示的其实是英文目录名
  ——真机 992 个类别里 656 个是技能目录,旧代码只在 `SkillLabels` 里备了 9 个元素名,其余一律中英文同串。
  *Every category row on the Effects tab now shows both languages, ordered by the Language setting.
  Previously one language per row, and for most skill folders the "Chinese" side was the English
  folder name: 656 of the 992 live categories are spell folders while only 9 element names were known.*
- 中文名来自新增的随包词表 `src/PsRules/Labels/labels-zh.json`(305 条,以类别 Id 为键,嵌进 DLL)。
  词表在**显示期**解析、不写回规则包,所以**旧规则包不用重新生成**就能看到新名字(重新生成要重扫整个 GGPK)。
  没收录的技能走中文前缀兜底:「技能: Crossbow Ballistas」。松散 shader 的名字不再带 `.fxgraph` 尾巴
  (类别 Id 保持不变,已有预设与档案照旧匹配)。搜索框两种语言都命中(输 `迷雾` 或 `Delirium` 同一个结果),
  行数统计也跟着语言:`128 个文件` / `128 files`。`pscli list-rules` 同步双语输出,并打印
  「词表已收录 N/M」——补词表时就看这个数。
  *Shipped `labels-zh.json` (305 ids, embedded) resolves at display time, so existing rule packs show
  the new names without a rebuild; unknown skills fall back to a Chinese prefix, and the CLI prints
  table coverage so the list can keep growing.*
- **技能名改成从客户端自己挖**,并且旧规则包不用重扫也全部有中文。上一版词表只写了 305 条、技能段只覆盖
  191 个,而本机规则包 992 个类别里有 **464 行一个汉字都没有**(全是 `skill.*`):一半是我漏写,另一半是因为
  「技能: X」前缀加在生成期,11-11 那份旧包根本没重扫过。
  *464 of 992 categories rendered with no Chinese at all — half missing table entries, half a stale pack
  that never got the prefix the builder now writes.*
  - 前缀移到**显示期**(`RulePack.LabelFor`):词表 → 包内标签 → 若结果不含汉字且命名空间已知,按
    「技能: / 环境: / 地形: / 外观: / 联赛: 」补框架。改完对同一份旧包量过一次:无汉字行 **464 → 0**。
  - 名字改为**直接读客户端的 ActiveSkills 表**:`.datc64` 其实是列式容器 + UTF-16LE 字符串池(**不是压缩的** —
    上一版报告说它压缩,是我用 `strings` 没加 `-el` 看错了),语言覆盖表与基础表行序一致、`Id` 列保持 ASCII,
    所以不需要 schema 就能按 Id 对齐取到本地化名。挖到 190 条官方名(新增 122、**改写我原先手写的 68**),
    词表到 427 条。
  - join 只做「剥掉最多两个类别前缀后的词元集合相等」,歧义一律丢弃。曾经的子串匹配把
    `cold_arcticarmour` 标成「電弧」(含 `arc`)、`bow_tornado_shot` 标成「龍捲風」(抓到德鲁伊的 `tornado`)、
    `flaming_wrath` 标成「雷霆」——一个共有名词不足以认定同一个技能,勾选项挂错名字会让人按错行下手。
  - 官方繁體名**逐字照抄,不转简体**:转出来的字符串会被当成官方名,而官方简体译法不总是逐字对应。
    于是这一列暂时繁體(官方)+ 简体(手写)并存 —— 装上 Steam 的简体中文语言包后按 `docs/zh-labels.md`
    再跑一遍,整列可换成官方简体名,混排自然消失。
  - 被官方名改写的 68 条说明了我自造名字的错处,尤其是**官方名不带武器/元素前缀**:`frost_wall` 官方是
    「冰牆」(我写「冰霜·霜墙」)、`contagion`「瘟疫」(我写「混沌·传染」)、`bow_snipe`「狙擊」(我写「弓·狙击」)、
    `crossbow_cluster_grenade`「集束擲彈」(我写「弩·集束榴弹」)。**能给官方名就别自己起名。**
  - 挖掘脚本与格式细节、重跑步骤记在 `docs/zh-labels.md`。
- **剩下没汉化的选项做完,整列统一到繁體**(上一版遗留:553 类只有「技能: 原名」框架,另有 229 条是我写的简体,
  同一列两种字体并存)。
  - 词表**拆成两个文件**,因为来源不同、可信度不同:`labels-zh.json`(270 条,客户端官方名逐字照抄)与
    `labels-zh-named.json`(720 条,我们起的名字)。分文件而不是加一个"来源字段",是为了让两者不可能各说各话;
    `pscli list-rules --json` 现在逐条给 `labelSource`(official/ours/framed)。
  - 官方名从 190 涨到 **270**:又挖了 `worldareas`/`acts`/`leaguenames`。**这推翻了上一版那条"开发编号没有官方名"** ——
    `env.g3_5` 其实是「龍蜥濕地」、`env.g2_town` 是「阿杜拉車隊」,只是那些表的 Id 形如 `G3_5`(CamelCase + 数字),
    用只认小写下划线的读法会整张表看成空。
  - 我们起的名字一律**对齐客户端自带的术语表**(`docs/zh-terms.md`,334 个 `[英文|繁體]` 对):官方 `crossbow` 是
    「十字弓」不是"弩"、`cold` 是「冰冷」不是"冰霜"、`herald` 是「捷光環」、`slam` 是「重擊」、`buff/debuff` 是
    「增益效果/減益效果」。上一版跳过这一步自造词,才有那 68 条被官方名顶掉。
  - **简体字形守卫**:测试用一份"简体专属字"黑名单扫两张表 + 显示期框架 + `Classify` 用词。黑名单刻意不含简繁同形字
    —— 过宽的清单会把客户端自己的「污垢頂峰」「平頂荒漠」判成违规,那种清单只会让人把它关掉。
  - 旧规则包里存的**简体框架**(`环境: Aow`)在显示期改写为繁體(`環境: Aow`):那一行已经有汉字,不走加框架的分支,
    不改就会一行一种字体。
  - 结果(用户本机 992 类):无汉字行 **0**、简体字形行 **0**,仅 2 类含义不明的编号仍是「環境: 原名」
    (`env.aow`、`env.map_uniquereactor_02`)。
- **界面其余纯英文文案改成「中文 / English」双语**:日志与状态里的 10 条(`已定位游戏…`、`规则包已生成…`、
  `应用后 0 个文件变更…`、规则包载入/读取失败、权限被拒)、`AppService`/定位器会显示到状态栏的 20 余条
  (游戏在运行、备份缺失、写后校验失败、按档位还原的几种拒绝理由)、文件选择器的筛选名,以及预设与语言下拉框的
  裸标签(现在写作「安全(默认)/ safe」「中文在前 / Chinese first」——**底层 token 仍是 `safe`/`zh`**,
  档案与规则包匹配不受影响,读取处做了两种形态的兼容)。异常堆栈保持原文,前面只加一句中文说明错在哪一步:
  那是提 issue 时要直接贴的东西,翻译它只会让两边对不上。范围外:`pscli` 的 72 条帮助/输出文本仍是英文。
- 挖掘脚本(`scripts/mine-zh-names.py`)与 GUI 出图工具(`tools/gui-shot/`,不在 sln 内、不参与发布门槛)**进仓库**了:
  前两轮的脚本都放在 `/tmp`,被清掉两次,`docs/zh-labels.md` 一度指向不存在的文件。
- 修一个显示缺陷:shader 行的英文长名把勾选行撑成两行(`TextTrimming` 在这种布局下要配 `MaxLines`+`NoWrap` 才生效)。
- 已知不足:技能段 656 个里仍有 466 个在 ActiveSkills 表里没有对应行(变体 `_02`/`_03`、机制类
  `attack_trails`/`ascension`、坐骑等)。其中约 370 个由我们按术语表译了名(仍标为 `ours`,不是官方名),
  其余含义不明的(`skill.grd_zones`、`skill.greybox`、`env.aow`)继续显示框架 + 英文原名,不硬凑。
- **忽略列表页左边新增「地面特效」逐项勾选树**(三层:地面特效 → 三个 `skill.ground_effects*` 类别 →
  具体效果目录)。动机:`safe` 预设含 `skill.*`,而**地面效果恰恰是默认就会被置空的那一类** —— 本机规则包里
  三行共 74 个文件(37 + 21 + 16),其中 55 个 `.ao` 属安全档,每次应用都会被剥掉 attachment;而「冰緩地面」
  「點燃地面」「奉獻地面」是要看的,不是要省的。之前只能手抄三行长路径,现在勾一下。
  - **树不存自己的状态**:勾选全部从右边那份列表的文本读回来(一个目录 = 一行路径前缀),所以手改文本、
    载入档案、恢复默认之后树都跟着变,不会出现"屏幕上说保护着、列表里没这行"。反过来按「保存」或真正应用时
    才落盘 —— 半途放弃的应用不许留下忽略行。
  - **目录从规则包现算**(`GroundEffectTree.Build`),不是写死的清单:客户端改版新增/改名一个地面效果时,
    写死的列表会让新目录悄悄落在保护之外,还会继续提供已经不存在的行。树里出现的必然是真会被写的文件
    (`ground_effects/crown_of_thorns/` 只有一个 `.ot`,规则包不含它,树里就没有它)。
    `skill.ground_slam_*`(裂地重擊)与 `skill.target_ground`(地面目標指示)名字里也带 ground,
    但那是技能特效与落点指示器,不进这棵树。
  - 勾父项只写**一行**(它的目录前缀),名下子项靠"被这行覆盖"显示为已勾。被覆盖的子项**勾上且灰掉**,
    tooltip 点名是上面哪一行在护着它 —— 子项没法单独取消,因为取消意味着删别人的行,一次点击不该假装做得到。
    顶层是唯一例外:`…/ground_effects` 覆盖不到 `…/ground_effects_v2`(前缀匹配要求后面跟 `/`),
    所以它一次写三行,取消也是删这三行。
  - 名字走**新增的第三张随包词表** `src/PsRules/Labels/ground-effects-zh.json`(25 条,键=路径前缀):
    只收客户端官方繁體,查得到的显示「奉獻地面 · holy」这种双写(目录名不许藏,它就是你写进列表的那串);
    官方给不出唯一名字的四个目录(`fire`、`ice`、`invisground`、`chimera_smoke`)**刻意不收**,
    显示目录原名。见 `docs/zh-labels.md`。
  - 连带修两处会吃掉这份存储的行为:窗口启动时**读 `ignore.txt`**(以前只在点「重新载入」时才读,
    重启后忽略列表回到内置默认、界面上看不见);`pscli build-rules` 与 GUI「生成规则包」以前会用规则包里那份
    **内置默认快照覆盖 `ignore.txt`**(`AppService.BuildRules` 从不传自定义列表),现在改成**合并进去**。
    否则点一次"生成规则包"就把刚勾好的保护静默抹掉,连磁盘一起。
    *A rule-pack rebuild used to rewrite ignore.txt from the built-in defaults; now that the ground
    tree keeps its state there, the defaults are merged in instead, and a fresh window reads the file
    back so the ticks survive a restart.*
  - 测试:单元 22 条(覆盖语义、注释/空行不许被动、幂等、合并、建树过滤)+ headless GUI 回归
    (含"勾一下不许直接写 ignore.txt"与重启往返:勾 → 保存 → 新窗口读回 → 树还是勾的);`tools/gui-shot` 多出 `ignore-zh` / `ignore-ticked-zh` 两张图,
    半选的顶层、勾着的类别行、灰掉的子目录在同一屏里同时成立。

- **主页新增「清理无人引用的遗留 bundle」面板 —— 补上一条 GUI 里走不通的死路**。形状是:自定义 bundle 还在
  `Bundles2/LibGGPK3/`,但 `state.json` 没了(本机是 11-15 那次写坏的:同分钟留下的 0 字节
  `state.json.corrupt-…` 与那个 bundle 目录)。于是两头都被堵:「应用」的守卫拒绝写入(`AppService.Apply`,
  它分不清这些 bundle 是当前补丁还是一份**旧客户端**留下的残骸 —— 客户端今天被 Steam 更新过,index 哈希
  `b5cca8e0…` 与 11 份备份共同的 `a8e4b382…` 已经不是一个东西),「还原」也拒绝(没有状态文件就不知道该用
  哪份备份,`没有已知备份路径`)。而报错文案给的出路只有命令行的 `--force` —— **GUI 里根本不传 Force**,
  玩家只能去开终端。
  - 面板只在"bundle 在、状态不在"时出现,判断只看 `GetStatus` 已经算出来的两个布尔值;文件名直接写在那行
    说明里,结果另起一行 —— 状态刷新**不许**覆盖结果行,否则"这些还被人引用所以我没删"这句最需要看见的话
    会在刷新完成的那一刻消失。
  - 清理**不是** `--force` 的别名:它先把当前 index 里所有记录指向的 bundle 路径收齐,只删没人引用的那些
    (复用每档还原一直在用的 `BackupManager.DeleteOrphanCustomBundles`),仍被引用的留在原地并明说。
    换句话说它永远不会制造悬空记录 —— 那正是 `--force` 会做的事。
  - 有状态文件时它一个都不动(那种情况「还原」才是正路),并照实回报 `StateExists`。
  - 测试:单元 8 条(有状态不动、仍被引用的不删且屏蔽照常生效、真残骸删掉且**不碰 index**、清完守卫放行、
    以及"没状态时还原确实走不通"这条前提本身)+ headless GUI 走真按钮(含后台扫描的等待条件用的是
    "结果行变了"而不是"按钮能点了"——后者只能证明点到了)。
  *The Home tab now offers a way out of the bundles-without-state dead end: it removes only the custom
  bundles no live index record references, so unlike `--force` it cannot leave dangling records — and
  it is the only route that does not require a terminal, since the GUI never passes Force.*

## v1.3 – 2026-11-16

### 变更 / Changed

- **「实验性资源」勾选拆成三档**:`safe`(默认,只做 `.ao` 脚本手术与 `.dds` 透明化)、
  `fx`(再把 `.pet`/`.epk`/`.fxgraph`/`.aoc` 置空)、`material`(再置空 `.mat`)。
  一次 `safe` + 全实验性在本机实测动过 73,581 个文件,其中 `.mat` 50,399 —— 旧版把这五类塞在同一个勾选里。
  GUI 换成「风险档」下拉框,选到材质时会在窗口内再确认并列出将被丢弃的原始字节数;CLI 是
  `--experimental safe|fx|material`,旧写法 `--include-experimental` 仍等于 `material`(语义没变)。
  *The single "experimental" tick became three tiers — `safe`, `fx`, `material` — because one tick
  bundled 50,399 shared materials with the effect-only blanking. The GUI is a risk-tier drop-down
  (material asks for an in-window confirmation listing the bytes to be discarded); the CLI gained
  `--experimental`, and `--include-experimental` still means `material`.*
- **置空不再写 0 字节,改写该格式的最小合法空资源**(2–42 字节,形状取自客户端自带的
  `empty.epk` / `00_null.pet` / `empty.mat` / `renderorder_02.fxgraph`)。「被置空的文件无害」因此不再依赖
  「还有谁在引用它」这个信息。想退回旧行为:GUI「写法策略」下拉框 / `--blank-style empty`。
  *Blanked files now get a minimal valid empty resource of their own format instead of 0 bytes, so
  safety no longer depends on knowing who references them; `--blank-style empty` restores the old bytes.*
- `RestoreResult.RestoredIndex` 只在「整个 index 被备份替换」时为真;逐文件还原改由
  `FilesRestored` / `FilesKept` 报告。

### 新增 / Added

- **按档位撤销**:`pscli restore --undo-tiers material` / 主页「还原范围」可以只撤材质、保住脚本手术;
  未撤销的条目继续被跟踪,部分还原后的索引会被认作「本工具留下的」而不会被误判成游戏更新。
  *Scoped restore: undo one tier and keep the rest suppressed; the partially restored index is still
  recognised as this tool's own work.*
- **`pscli health`(只读)**:把客户端日志里带资源路径的 CRIT/WARN 与本次写入的文件求交集,列出可疑项。
  日志里带路径的行很稀疏,所以「无告警」只是弱证据 —— 命令自己会这么说明。
  *`pscli health` crosses asset-path complaints in the client log with the files this run wrote.*
- **应用后读回校验**:提交完成后、写 `state.json` 之前,重新打开目标核对每条记录的 size/bundle 坐标,
  并对每个自定义 bundle 的首尾记录做内容哈希;不一致则还原备份且不留下描述不存在补丁的 state。
  *Post-apply verification re-opens the committed target and checks every record, restoring the
  backup on a mismatch instead of writing state describing a patch that is not there.*

- **渲染压缩(实验性,默认全关)**:与「置空」并列的另一条路 —— 保留视觉、降渲染负载。Effects tab 新增四个开关
  (贴图同分辨率 BC7→BC1、网格官方 LOD 替换、shader 精简、网格减面)+ 减面强度滑条 + 降分辨率激进取向;
  CLI `--compress-tex` / `--tex-downscale` / `--compress-shader` / `--mesh-lod` / `--mesh-decimate <0-100>`。
  双重门控(风险档 + 开关),启用某类压缩则该类不再置空;只动 `.dds`/`.fxgraph`/`.fmt`,跳过怪物/Gameplay 预告,
  **`.pet` 配置永不触碰**。贴图 codec 离线逐字节验证(BC7 解码 vs 独立 Python 移植在 26 样本/246 mip 一致;
  BC1 编码 vs 原型一致);同分辨率只压 opaque/cutout、平滑 alpha 拒绝,降分辨率档对软粒子用 BC3 保留柔和边缘
  (-75%)、opaque 用 BC1(-87%)。全部走既有备份/校验/还原链路,还原零改动。**仍需实机 A/B 才能转正**(见 docs §7.3)。
  *Render compression (experimental, off by default): another path beside blanking that keeps visuals but lowers
  render load — four toggles (same-res BC7→BC1 textures, official mesh LOD swap, shader simplification, mesh
  decimation) plus a decimation-strength slider and an aggressive downscale option. Dual-gated by risk tier and
  switch; a compressed class is no longer blanked. Only `.dds`/`.fxgraph`/`.fmt` are touched, monsters and gameplay
  telegraphs are skipped, and `.pet` configs are never modified. Texture codecs are verified byte-for-byte offline;
  same-res compresses only opaque/cutout (smooth alpha refused), while the downscale tier keeps soft particles soft
  via BC3 (-75%) and opaque via BC1 (-87%). Reuses the existing backup/verify/restore chain with zero-change
  restore. Still requires in-game A/B before promotion (docs §7.3).*

### 修复 / Fixed

- **`.ao` 脚本手术漏掉生产写法**:真机里 `AddEffectPack( "…epk" )` 用双引号、
  `AddAttached( '…', <lock>)` 第二参不是 `<root>`,旧正则一种都不匹配 —— 这些挂接调用从未被剥掉。
  现在引号与占位符两种形态都覆盖;赋值语句的右值降级成 `nil`,不再留下 `parent_ao.` 这种悬空残骸。
  *Script surgery missed the shapes production actually uses (double-quoted `AddEffectPack`,
  `<lock>` second arguments), and left `x = parent_ao.` residue behind; both are fixed.*
- 重启后「实验性资源」的选择不再被静默清掉(改档位后同理,恢复赋值全部排在 `_loading` 保护区内)。
  *The tier choice survives a restart; restoring it runs inside the construction guard.*

## v1.2 – 2026-11-12

### 新增 / Added

- **Windows 支持**:便携 ZIP(`PoE2Smoother-win-x64.zip`、`pscli-win-x64.zip`)与 AppImage 一起发布;
  自动识别 Steam(注册表、多库、盘符兜底)与独立客户端;写入被拒时 GUI 提供「以管理员身份重启」。
  *Windows support: portable ZIPs ship next to the AppImage; Steam (registry, multiple libraries,
  per-drive fallback) and the standalone client are detected automatically; the GUI offers
  "Run as admin" when a write is denied.*
- **独立客户端 `Content.ggpk`(实验性)**:vendor LibGGPK3 / LibBundledGGPK3,索引读写沿用同一套 bundle 逻辑;
  备份只导出容器内的索引,写入前有日志标记与磁盘空间检查。
  *Standalone `Content.ggpk` support (experimental): LibGGPK3 / LibBundledGGPK3 are vendored and the
  same bundle logic now runs over the in-container index; backups export only that index, and writes
  are journalled and free-space checked.*
- GUI 主页新增「浏览」按钮手动选择 `_.index.bin` / `Content.ggpk`,并显示目标类型。
  *The GUI gained a "Browse" button to pick `_.index.bin` / `Content.ggpk` by hand, and the home tab
  shows the target type.*
- 测试新增合成 GGPK 夹具,覆盖独立客户端的 apply / status / restore 全流程(CI 也会跑)。
  *Tests build a synthetic GGPK fixture and cover the standalone apply / status / restore cycle (CI included).*

### 修复 / Fixed

- 交叉发布 `-r win-x64` 会挑到 linux-x64 的 vendored 原生库;RID 选择现在先看 RID,再用宿主 OS 兜底。
  *Cross-publishing with `-r win-x64` used to pick the linux-x64 vendored natives; RID selection now
  prefers the RID and only falls back to the host OS.*

## v1.1 – 2026-11-11

### 新增 / Added

- **游戏更新后重新应用更安全**:还原改为逐文件一致性判断,只有内容仍等于本工具当时写入版本的文件才会被撤掉,
  已被游戏更新改过的文件直接跳过,不会把游戏降级回老版本。
  *Restore is now file-by-file aware: after a game update, only files still matching what this tool
  wrote are reverted, so restoring can no longer downgrade updated game files.*
- 新增「赞助」标签页,附支付宝收款码。
  *New Sponsor tab with an Alipay QR code.*
- 发布全自动化:推送 `vX.Y.Z` tag 即由 GitHub Actions 跑测试、构建 AppImage、创建 GitHub Release 并挂上产物,
  不再手工上传;README 也补上了下载入口(二进制从 v1.0 起就在 Releases 页上,但 README 从来没给过链接)。
  *Releases are now cut by pushing a `vX.Y.Z` tag: CI runs the gate, builds the AppImage and publishes it,
  replacing the manual upload. The README now links to the releases — the binaries were there since v1.0,
  but nothing pointed at them.*
- 版本号只有一个来源(git tag),GUI 标题旁新增版本显示,`pscli version` 与之保持一致。
  *The version now comes solely from the git tag; the GUI title shows it and `pscli version` agrees.*
- 英文 README([README.en.md](README.en.md))与本更新日志。
  *English README ([README.en.md](README.en.md)) and this changelog.*

### 变更 / Changed

- AppImage 打包工具(`appimagetool` 与其内嵌 runtime)纳入仓库,干净 clone 无需手动下载即可打包 AppImage。
  *The AppImage packer and its embedded runtime are now vendored, so a fresh clone can build the
  AppImage without a manual download.*
- 本地无 tag 构建的版本号由谎报 `1.0.0` 改为 `0.0.0-dev`。
  *Local builds without a tag now report `0.0.0-dev` instead of a stale `1.0.0`.*

### 修复 / Fixed

- README 删除「为什么要做这个工具」一节,安装步骤与下载链接保持一致。
  *README cleanup: the "why this tool" section is gone, and install steps match the download link.*
