# 置空策略与风险档位 / Blanking strategies and risk tiers

本文说明 `实验性` 开关现在分成三档之后各档到底动什么、为什么这么分、以及还没做掉的方案。
面向想改这块代码的人;用户向的一句话说明在 `README.md` / `README.en.md`。

证据标注:**[已证实]** = 有代码位置或真机只读测量支撑;**[推断]** = 从代码/格式性质推出,未实机验证。

## 1. 三档的语义与判定

档位不写进规则包 JSON(`RulePack.SchemaVersion` 仍为 1,`RulePack.cs:254-255`(`RulePackStore.Load`)对非 1 直接抛异常,
升版会砖掉现存所有 `rulepacks/default.json`),而是从策略派生:

| 档位 | 键 | 策略 | 写入内容 |
|---|---|---|---|
| `Safe` | `safe` / `off` | `StripAttachments`(`.ao`)、`TransparentDds`(`.dds`) | 删挂接调用 / 136 字节合法 1×1 DXT1 |
| `Fx` | `fx` | `NullPet`(`.pet`)、`NullEffectPack`(`.epk`)、`NullGraph`(`.fxgraph`)、`Empty`(`.aoc`) | 该格式的最小合法空资源(§6);`.aoc` 无已知模板 → 0 字节 |
| `Material` | `material` | `NullMaterial`(`.mat`) | 同上(`.mat` 的合法空材质) |

派生入口 `RiskTiers.Of(strategy, path)`(`src/PsRules/RulePack.cs`),`Auto` 规则先经
`RulePackBuilder.TryGetStrategy(path)` 解析——**[已证实]** 策略解析只在写时发生
(`ReplacementFactory.cs:43`),所以档位判定必须自己解一次,否则手写包里的 `Auto` 规则会被当成安全档。

手写包取 **max**:

```
effectiveTier = max(TierOf(strategy, path), rule.Experimental ? Fx : Safe)
```

**[已证实]** 若不取 max,`StripAttachments + Experimental:true` 这类手写规则在升级后会变成 `Safe`
从而**默认开始写文件**——旧程序会跳过它。反向(`NullMaterial + Experimental:false` 变严)是可接受的,
已在测试里钉住(`tier: a material rule the old pack forgot to mark is still material`)。

旧布尔的迁移方向:`IncludeExperimental == true` 且没有 `ExperimentalTier` 字符串时 → `material`。
**[已证实]** 旧布尔同时管着 `.mat` 和 `.pet`,把它映射成 `fx` 会静默减少用户已经选择过的屏蔽范围。
保存时两个字段一起写(字符串 + 镜像布尔),降级到旧程序不会丢 opt-in。

CLI 侧 `--include-experimental` 保留为 `--experimental material` 的别名,同理不改旧脚本的语义。

## 2. 为什么材质必须单独一档

**[已证实]** 本机(Steam 版,`Bundles2/_.index.bin`)2026-11-12 只读探测:
一次 `safe` + `--include-experimental` 的应用共写 73,714 条记录,其中 **73,581 条 `AppliedSize == 0`**,
按策略为 `NullMaterial` 50,399 / `NullPet` 15,556 / `NullEffectPack` 5,054 / `NullGraph` 2,572;
另有 133 条 `StripAttachments`。与 `doc-bk/PRESET-0-ACTIVE.md:45-46` 采集的旧快照
(73,486 条,`.mat` 50,321)一致,差异来自 index 版本。

也就是说旧的一个勾选实际是「把五万多个材质文件抹掉」。`doc-bk/PLAN.md:346-351` 给每种扩展名标过风险,
`.dds` 的 1×1 透明是唯一标「最安全」的,`.ot` 明写「置空必崩」——`TryGetStrategy` 也确实不认 `.ot`。

### 目标函数:不是最大化删除

评判标准必须先说清,否则任何「让置空更安全」的方案都会得出方向相反的结论:
**本工具的目标是在降噪的同时保住玩法可读信息,不是把能删的都删掉。**

一个攻击预告即使被剥掉材质与特效,它的**剪影与运动本身就是方向信息**;彻底抹掉它是净损失。
这正是当前架构已经在做的事:安全档的 `.ao` 手术只摘挂接调用、不动实体本体,怪物与 Boss 由忽略列表
默认保护(`src/PsRules/IgnoreList.cs:21-28`)。

由此得到一条硬约束:**任何提高「置空可信度」的改动,都必须同时说明它会不会让人敢删掉本该保留的东西。**
方案 B(引用门控)就是在这条上被判掉的,见 §5。

## 3. 真机格式实测(改变了下一步方向的事实)

对同一份客户端只读抽取(`pscli read-file`,不改任何文件):

- **[已证实]** `.pet` / `.epk` / `.fxgraph` / `.mat` 全部是 **UTF-16LE 带 `FF FE` BOM 的文本**:
  `.pet` 是 `version 4` + JSON 体(`{ "gpu_emitters": [...] }`,内部引用 `"…X.mat"`);
  `.fxgraph` 是 JSON(`{ "version": 6, "shader_group": ["Material"], "nodes": [...] }`);
  `.mat` 是 JSON(`{ "version": 4, … "graphinstances":[{"parent":"Metadata/Effects/Graphs/….fxgraph"}]}`);
  `.epk` 是纯文本 `AttachedObject "<lock>" "Metadata/…/X.ao"`。
- **[已证实]** `.aoc` 在整份索引的路径表里出现 **0 次** → `Empty` 策略是死代码
  (`RulePackBuilder.cs:131`;`doc-bk/PLAN.md:352` 当年就写了「需样本确认」)。
- **[已证实]** GGG 自己**从不发 0 字节文件**:同批被置空文件里原始大小的最小值是
  `.epk` 2 B(只有 BOM)、`.pet` 4 B、`.mat` 42 B、`.fxgraph` 42 B(42 字节就是 JSON 骨架)。
  即「空资源」在生产里的表达方式是**极小的合法文件**,不是空流。
- **[已证实]** 0 字节写入本身能过容器这一关:`Write([])` → `Index.Save()` → 原子改名 → 重开 → 读出 0 字节
  (本机 11 月 11 日那次应用就是这种状态,73,581 条)。**[推断]** 游戏怎么解读 0 字节记录,以及现在怎么解读
  §6 的合法空资源,都仍未在客户端验证 —— 这条只能由 §6 的 `pscli health` 加实机跑一局来回答。
- **[已证实]** 生产 `.ao` 的调用语法比原正则宽:`AddEffectPack( "…epk" )` / `RemoveEffectPack( "…epk" )`
  用**双引号**,`AddAttached( '…ao', <lock>)` 的第二参是 `<lock>` 而非 `<root>`;
  原正则(旧实现里的单个 `EffectCall`,`0d758ea` 版 `ReplacementFactory.cs:9-11`)只认 `'…'` + `<root>`,所以这些调用一条都没被剥掉,
  「脚本手术」在真机上大面积空转。修法见第 4 节 I1。
- **[已证实]** 跨根引用真实存在:`IgnoreList.cs:26-28` 保护的 `metadata/terrain/worldmaps/…` 下的 `.ao`
  就在规则包候选范围(`IndexSession.cs:163-167` 三个根)之外,但它们会引用范围内的资源。

## 4. 本轮落地的四件事

- **I1 `.ao` 剥离正则**:两条规则先赋值后独立语句。赋值整体右值降级成 `nil`
  (`this.attached = nil;`),独立语句把 `接收者 + 调用 + 分号` 一起删掉——原实现只吞函数调用本身,
  留下 `this.attached = parent_ao.` 这种语法残骸(由正则范围直接推出)。双引号、任意 `<占位符>` 均覆盖;
  `RemoveAttached( this.attached )` 仍不动。
- **A 三档**:GUI 用 `TierBox` 取代复选框(选到 `material` 会在窗口内弹确认面板并报「将抹掉 N 个 .mat /
  M 字节原始内容」——注意 `PatchPlan.TotalBytes` 累加的是**原始大小**,即被抹掉多少,不是写入多少);
  CLI `--experimental safe|off|fx|material`(值选项,`--experimental=fx` 这种写法
  `ParsedArgs` 不认);四秒双击确认被材质面板取代,因为面板本身是更强的确认。
- **F 分范围还原**:`--undo-tiers <集合>`(不是上限,`material` 不会连带撤 `fx`),GUI 主页「还原范围」。
  过滤后仍生效的条目保留在 `state.json` 里并新增 `PartialRestoreIndexSha256`,`Apply` / `GetStatus`
  把它认作「本工具留下的索引」——否则它会被判成游戏更新,而 `--force` 会删掉这些条目仍在引用的
  自定义 bundle(`AppService.cs:139-151` 那条分支),留下悬空记录。
  覆盖全部条目的过滤条件等价于不加过滤,仍走整档还原,保住「还原后字节与原索引相同」这条既有保证。
  无法归类的条目(旧 state 没有 `Tier`,且 `Strategy` 也解析不出)**永不进入任何过滤范围**:
  「撤销一个无法归类的东西」不可恢复,「让它继续屏蔽」不是。
- **[已证实]** 本机 73,581 条置空记录里**原始大小已经为 0 的有 0 条**,所以「客户端里根本没有 0 字节的
  这类文件」——预览判空过滤(§6)因此不可能吞掉一次真实写入。
- 顺带修正:`RestoreResult.RestoredIndex` 现在只在「index 文件被备份整体替换」时为真;
  逐文件还原不再置真(此前两条路径都为真,调用方无法区分「整体回滚」和「按条指回」)。
- **H**:①写入后读回校验(在 `Save/Commit` 之后、写 `state.json` 之前;先全量免费比对
  size/bundle 坐标,再对每个自定义 bundle 的首尾记录做内容哈希——随机采样对「坐标算错」和
  「尾部截断」这两类真实故障都没用);②预览把「已经是本工具写的空资源」算进 `Unchanged`
  (`Apply` 会先回滚到基线所以不影响真实写入,但 `pscli plan` 和不带 `--yes` 的预览是在**已打补丁**的
  索引上算的,不判就会把重复写同一内容报成变更,连带污染退出码 6 与材质确认面板的数字)。
  ②的判定条件在 §6 里随模板一起改了写法。

## 5. 剩余方案与一条已否决

优先级按「是否依赖我们拿不到的信息」排:

| 方案 | 内容 | 卡在哪 |
|---|---|---|
| ~~C 每扩展名最小合法空模板~~ | **已实现,见 §6** | 仍需实机验证(§6 末尾) |
| ~~G 应用后日志体检~~ | **已实现为 `pscli health`**(只读,见 §6) | 带路径的行很稀疏:本机一份完整日志 662 CRIT / 7 WARN 里只有 **1 行**提到资源路径 —— 「没有告警」不等于「安全」 |
| D 间接抑制 | 材质/粒子本体不动,把它们引用的 `.dds` 换成已有透明 stub | 需要引用解析;效果最接近 C 且不动本体 |
| E 文件级灰度 | 列出将置空的路径/大小/引用者数,默认不选 | 数据现成(`PlanCommand.cs` 已逐条输出 `tier`),缺 UI |
| 贴图压缩(已落地 M4,见 §7) | `.dds` 两条杠杆:**同分辨率降格式** BC7→BC1(实测 1024² 1,398,276→699,212 B,约 -50%,分辨率不变);**降分辨率** 2×(平滑 alpha 转 BC3 -75%、opaque/cutout BC1 -87%)更省,但会改变图集 UV 切取坐标 → **默认保分辨率**,降分辨率仅作可选激进档 | BC7/BC1/BC3 codec 已入库并离线逐字节验证(§7.1);候选根 `art/particles/`,法线跳过;**平滑 alpha 同分辨率拒绝、降分辨率转 BC3,绝不硬化软粒子**;画质与实机 A/B 未验证 |
| 特效压缩(待开发) | `.pet` v4/v5(JSON `gpu_emitters`)按字段白名单(见下)削减预算,特效保留但更省;v3 为 legacy 文本(`PointEmitter`/`CylinderEmitter`)需另处理 | **配置非纯视觉:改动可能影响技能效果范围/判定等玩法属性**(用户指出,待实机证实);字段语义与降幅上限需实机验证;一律跳过 `culling_prio=Gameplay` |

C 当初排第一的理由是它**不依赖「谁在引用这个文件」这个信息**:让被置空的东西本身对所有引用者无害,
因此不会诱导使用者去删更多。这一点正是 B 被否决的核心(见下)。

**压缩方向(已落地 M0–M4,实验性,见 §7)**:与置空相反,压缩保留视觉,故通过 §2 的方向性检验(不诱导删掉本该留的)。
**[已证实]** 单文件原型(纯 Python BC1 编解码,未入库)实测:1024² BC1 降 2×/4× 体积 -75%/-94%,2× 视觉几乎无损;
同分辨率 BC7→BC1 约 -50%(由客户端真实文件大小推得)。**分辨率决策**:降分辨率会改变图集 UV 切取坐标、可能切错岛块,
因此定为「保分辨率优先」(默认同分辨率降格式),降分辨率只作可选激进档。该约束已落实为默认同分辨率 + 显式降分辨率开关(§7.1);上表 `.pet` 特效压缩仍待开发(永不触碰配置)。

**特效压缩字段白名单(待开发,未实现)**:静态值域取证(36 文件 / 75 emitter)分三级 ——
**A 级**(官方广泛取值,高置信可改):`particle_duration_min/max`、`emitter_duration_min/max`、
`particles_count(_min/max)`、`particles_per_second` 曲线、`emit_burst`、`group_size`、`emitters_per_sec`、
`interval` 活跃段、`bounding_size`;**B 级**(开关/枚举,官方两态都出现):`enable_cpu_culling2`、
`culling_prio`(限 {Gameplay,Important,Cosmetic});**C 级**(官方恒定、未经行使,先不碰):`emit_chance`(恒 100)、
`cull_oldest`(恒 True)、`is_persistent`(恒 False)、`independent_group_culling`。结构受限:`render_passes` 至少留 1
且 `render_material` 引用有效。**一律跳过 `culling_prio=Gameplay`**(玩法关键)。
**玩法风险(用户指出,待实机证实)**:`.pet` 配置并非纯视觉 —— 改动可能影响游戏中技能效果的**范围/判定**等玩法属性,
故特效压缩属玩法相关改动,不能只按「省粒子」评估;实现前必须实机 A/B 确认范围不变或可接受。静态分级只是证据,
「加载器接受 + 视觉/范围符合预期」的最终确认仍依赖实机(同 §6.5)。

### 已否决:B 引用门控

内容:建规则包时扫文本文件建 `资源 → 引用者` 反向表,只有「引用者全在本次屏蔽集内」的资源才允许置空。
**决定不做**,两个理由各独立成立:

1. **方向性有害(决定性理由)。** B 抬高的是「敢开 `material` 档」的上限,而那个上限提高的直接后果,
   是把攻击预告一起抹掉 —— 见 §2「目标函数」:剪影与运动本身就是方向信息,删干净是净损失。
   B 优化的量(置空的可信度)恰恰是**不该被最大化**的那个。
2. **在最需要它的类别上做不成。** **[已证实,本机 2026-11-12 `pscli dump-paths` 实测]**
   全客户端 `.ao` 共 74,241 个,其中落在可屏蔽三根之内的只有 20,710 个 ——
   **53,531 个(72%)是永远不会被屏蔽的引用者**(怪物/道具/世界地图/UI 脚本),它们让严格门通过的集合大幅缩小;
   而 `.mat`(三根内 122,889 个候选)的引用者还包括**我们无法解析**的 `.ot`(全客户端 8,324,三根内 337)
   与 mesh 类二进制,索引格式本身又**没有任何依赖表**(`FileRecord` 只有 Path/PathHash/Offset/Size/BundleRecord)。
   于是只剩两种收场:按「读不到 = 可能被引用」失败关闭 → 材质档基本全被挡死,门控形同虚设;
   按「读不到 = 没引用」放行 → **假绿灯**,比现在诚实的不确定更危险。

三根内的候选 census(同一份 dump,`metadata/` 下共 333,047 条路径,三根内 190,982 个文件):

| 扩展名 | 三根内 | 全客户端 |
|---|---|---|
| `.mat` | 122,889 | 128,374 |
| `.pet` | 27,324 | 37,473 |
| `.ao` | 20,710 | 74,241 |
| `.epk` | 7,993 | 8,583 |
| `.fxgraph` | 6,729 | 12,453 |
| `.ot` | 337 | 8,324 |
| `.aoc` | 0 | 0 |

**什么情况下重新考虑 B**:只有当出现一个既不依赖引用信息、又能降低危害的替代(即 C)被证实不够,
且实机证明「保留剪影」其实没有玩法价值时。否则不要把 B 当作「下一步该做的安全优化」重新提出 ——
它的安全收益是负的。

另外仍然成立的两条边界:还原不受档位影响(它只读 `state.json` + 备份);`.ot` 永远不进规则包。

## 6. 已落地:合法空资源 + 退路开关 + 日志体检

### 6.1 写进去的字节(`src/PsRules/BlankTemplates.cs`)

| 格式 | 模板 | 字节 | 依据 |
|---|---|---|---|
| `.epk` | BOM only | 2 | 与 `…/general_fx/epks/empty.epk` 同形 |
| `.pet` | BOM + `0` | 4 | 与 `…/hit_and_blood/fx/00_null.pet` 同形 |
| `.mat` | BOM + `{⏎····"version": 4⏎}` | 42 | 与 `…/monk_hand_of_chayula/mat/empty.mat` 同形 |
| `.fxgraph` | 同上,`version: 3` | 42 | 与 `…/graphs/general/renderorder_02.fxgraph` 同形(两份不同目录副本同哈希) |
| `.aoc` | 0 字节 | 0 | 无已知模板可参照(本客户端 0 个 `.aoc`) |

模板是**按观察到的形状自己写的等价内容**,不是从客户端里搬字节 —— 这些内容是空 JSON 文档,
同形即可,仓库继续不携带游戏数据。`ReplacementFactory.BlankReplacement(strategy, style)` 是唯一的
真值来源:apply 写的字节与「是不是已经空了」的判定共用它,两者不可能对不上长度。
`TransparentDds` 与四个模板一律返回**副本**(原来把缓存数组本体交出去,谁改一字节就污染后续所有写入)。

### 6.2 退路:`--blank-style empty`

`BlankStyle { Template, EmptyBytes }`,默认 `Template`;GUI 是特效页「写法策略」下拉框,
CLI 是 `--blank-style template|empty`(**值选项**),档案与 `settings.json` 都带这个键。
设置文件里没有该键时按 `Template` 处理 —— 把「没有值」解释成「回到写 0 字节」会静默降级已有安装。
还原与写后校验**不需要**知道风格:`PatchedFileEntry.AppliedSize/AppliedSha256` 已经逐条记了实际写入的字节。

### 6.3 两个必须知道的实现细节

- **「已经置空过」不能只看字节长度。** 预览(`pscli plan`、不带 `--yes` 的 `apply`、材质确认面板)在
  已打补丁的索引上算,判据是「长度等于该格式模板 **且** 该记录落在我们自己的自定义 bundle 里」
  (`BackupManager.IsCustomBundlePath`)。只看长度的话,一个官方目录里恰好 42 字节的 `.mat`
  会被误判为「已处理」而**永不**被屏蔽;用「谁写的」判定则不会。测试
  `template: an official file the same length as the template is not skipped` 钉住了这一点。
- **旧版留下的 0 字节补丁会自动升级。** 那些记录长度不匹配模板,所以预览仍算作待写,再应用一次就变成合法空资源,
  不需要迁移代码。`--blank-style empty` 与 `Template` 之间来回切换同理(互相都被视为待重写)。
- 门禁里 `.pet` 的模板长度写成常量而不是「非 0」断言(`test-all.sh`/`.ps1` 的 `$PET_TEMPLATE`/`PET_TEMPLATE=4`):
  模板改动必须让这一条**响亮地失败**,而不是被「反正不是 0」蒙过去。测试夹具的伪造原文也一律放大到
  明显超过任何模板(`BlankAssert.Fixture`),否则「置空后的文件」会比它的原始文件还大,尺寸判别力就没了。

### 6.4 `pscli health`(只读)

把 `<游戏根>/logs/LatestClient.txt`(也探 `Client.txt` 与旧的 `Bundles2/logs/`)里**带资源路径**的
CRIT/WARN/ERROR 行,与 `state.json` 本次写入的文件求交集,列出可疑项并提示可 `--undo-tiers` 只撤该档。
退出码 7 = 有可疑项,0 = 无。**它不能证明安全**:本机一份完整日志里 662 条 CRIT、7 条 WARN,
只有 1 条提到资源路径 —— 命令在无交集时会自己打印这句限定语。

### 6.5 仍未验证

模板「被加载器接受、且不画任何东西」目前是**有依据的推断**(依据:GGG 自己就是这么表达空资源的;
且本机这些类型的原始文件里没有一个是 0 字节)。要变成事实,需要:用 `fx` 档应用 → 进游戏跑一局 →
`pscli health` → 还原;再用 `--blank-style empty` 走同一趟做 A/B 对照。在那之前,不要把 §6 写成「已解决」。

## 7. 已落地:渲染压缩子系统(M0–M4,实验性,默认全关)

与「置空」并列但**相互独立**的另一条路:保留视觉、降渲染负载。四个独立开关 + 减面强度滑条 + 降分辨率激进取向,
**默认全关**;双重门控(`MaxTier ≥ 策略档位` **且** 对应开关打开)才产生变更;启用某类压缩则该类不再置空(互斥)。
所有写入复用既有 `FileRecord.Write`→custom bundle→`VerifyApplied`→`state.json`→`RestorePerFile` 全链路,
**还原零改动、不受档位影响**。代码:`src/PsRules/Compression/`、`src/PsCore/CompressionPlanner.cs`。

| 功能 | StrategyKind | 文件 | 档位 | 杠杆 | 状态 |
|---|---|---|---|---|---|
| 网格 LOD | `SwapMeshLod` | `.fmt` | Fx | LOD00→LOD1/2 官方降模(同目录 sibling,无需懂顶点布局) | M1 已落地 |
| 减面 | `DecimateMesh` | `.fmt` | Fx | 顶点聚类 + 去退化三角 + 头部回写,强度滑条 0–100 | M2 已落地 |
| shader 简化 | `SimplifyGraph` | `.fxgraph` | Fx | 白名单图换精简模板(仍渲染可辨认效果) | M3 已落地 |
| 分辨率 | `CompressDds` | `.dds` | Material | 同分辨率 BC7→BC1(UV 安全);可选降分辨率激进档 | M4 已落地 |

**候选根(只压缩,不动置空语义)**:网格 `art/models/effects/`、`art/particles/`、`art/microtransactions/`;
贴图 `art/particles/`(法线 `_n/_normal/_norm` 跳过);shader `metadata/effects/`、`metadata/terrain/leagues/`、
`metadata/materials/environment/`。`IsGameplayProtected`(路径含 `monster`)+ IgnoreList 一律跳过。
**绝不改 `.pet`**(配置影响技能范围/判定,§5);压缩策略永不 targeting `.pet`(有测试断言)。

### 7.1 贴图 codec(M4,本会话补齐并验证)
- **BC7 解码** = 移植 Mesa `texcompress_bptc_tmp.h` UNORM 路径(`Bc7Decoder.cs`,8 mode + 分区/anchor/p-bit/
  rotation/双索引/插值权重)。**ffmpeg n8.1.2 不解码 BC7**(`Unsupported DXGI format 98`),不能作参考。
  **[已证实]** C# 解码 vs 独立 Python 同源移植,在 26 个真实 `art/particles/` BC7 样本(246 mip,31,784,944 RGBA 字节)
  **逐字节一致**;每 mode 手工已知块断言通过。
- **BC1 编码**(`Bc1Encoder.cs`,alpha 感知:opaque 4-color、cutout 3-color + 透明 index3)。
  **[已证实]** 与纯 Python 原型在真实 opaque 样本 mip0 **逐字节一致**(65,536 B)。
- **alpha 策略(按解码后的值判定,不看 mode)**:opaque(全 255)/cutout(仅 0/255)→ BC1;
  **平滑 alpha(任一 0<a<255)同分辨率拒绝**(BC1 仅 1-bit alpha,BC3 与 BC7 同尺寸无收益)。
  实测 26 个样本 25 个是平滑 alpha(软粒子)、1 个 opaque → 同分辨率只压 opaque/cutout(本样本 1/26)。
- **降分辨率激进档(M4b)**:box-filter 2× + 重建 mip 链;**平滑 alpha 用 BC3**(保留柔和边缘,-75%),
  opaque/cutout 用 BC1(-87.5%);**分类在降分辨率之后**(cutout 经 box-filter 会产生中间 alpha → 转 BC3);
  **绝不把软粒子硬化成 BC1 cutout**(改变视觉角色,违反 §2)。非 2 的幂尺寸 mip 链收敛到 1×1(有回归测试)。
- **[已证实]** 26 个样本降分辨率全部产出合法 DX10 头(BC3=77 / BC1=71),尺寸减半、mip 链正确,
  对同分辨率参考 PSNR 21–55 dB;体积 -75%(BC3)/ -87%(BC1)。DX10 头 98→71/77,重算 pitch/linear size。
- 只处理单 array 2D BC7(DXGI 98);legacy fourcc / 已是 BC1/BC3 / cubemap / array / 截断 / 1×1 降分辨率 → 拒绝。

### 7.2 网格 / shader(M1–M3,前序会话已验证)
- **[已证实]** fmt 27/47 解析、LOD swap -41% 字节、减面与 Python 原型逐字节一致;billboard/quad 与非 DOLm 跳过。
- **[已证实]** fxgraph `MASK_FresnelStatic` 244KB→32KB、`MASK_TextureStatic` 390KB→47KB,0 悬空链接;白名单 pass-through。

### 7.3 仍未验证(实机 A/B,交用户)
codec 正确性已离线证实,但「客户端加载器接受 + 视觉可辨认 + 技能范围不变 + 无黑块/报错」仍需实机:
1. 备份后,GUI Effects tab 勾选「贴图同分辨率」(material 档)→ apply → 进游戏跑一局 → `pscli health` → 还原。
2. 再勾「降分辨率(激进)」走同一趟做 A/B:重点看软粒子边缘是否仍柔和(BC3)、图集 UV 是否切错岛块。
3. 网格 LOD / 减面(fx 档):看特效轮廓/剪影是否仍可辨认(§2 方向性),怪物预告一律不动。
4. shader 简化(fx 档,风险最高):看白名单效果是否仍可辨认、有无渲染报错。
5. 每趟都用 `pscli health` 交叉客户端日志;还原后核对 index SHA 与原索引一致。
在那之前,四类压缩均标「实验性,需实机确认」,不要写成「已解决」。
