# 置空策略与风险档位 / Blanking strategies and risk tiers

本文说明 `实验性` 开关现在分成三档之后各档到底动什么、为什么这么分、以及还没做掉的方案。
面向想改这块代码的人;用户向的一句话说明在 `README.md` / `README.en.md`。

证据标注:**[已证实]** = 有代码位置或真机只读测量支撑;**[推断]** = 从代码/格式性质推出,未实机验证。

## 1. 三档的语义与判定

档位不写进规则包 JSON(`RulePack.SchemaVersion` 仍为 1,`RulePack.cs:254-255`(`RulePackStore.Load`)对非 1 直接抛异常,
升版会砖掉现存所有 `rulepacks/default.json`),而是从策略派生:

| 档位 | 键 | 策略 | 写入内容 |
|---|---|---|---|
| `Safe` | `safe` / `off` | `StripAttachments`(`.ao`)、`TransparentDds`(`.dds`) | 删挂接调用 / 132 字节合法 1×1 DXT1 |
| `Fx` | `fx` | `NullPet`(`.pet`)、`NullEffectPack`(`.epk`)、`NullGraph`(`.fxgraph`)、`Empty`(`.aoc`) | **0 字节** |
| `Material` | `material` | `NullMaterial`(`.mat`) | **0 字节** |

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
- **[已证实]** 0 字节写入本身能过:`Write([])` → `Index.Save()` → 原子改名 → 重开 → 读出 0 字节,
  本机当前就处在这个状态。**[推断]** 游戏怎么解读这些 0 字节记录仍未验证(见第 5 节)。
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
- **[已证实]** 本机 73,581 条置空记录里**原始大小已经为 0 的有 0 条**,所以 H 的预览判空过滤
  不可能吞掉一次真实写入。
- 顺带修正:`RestoreResult.RestoredIndex` 现在只在「index 文件被备份整体替换」时为真;
  逐文件还原不再置真(此前两条路径都为真,调用方无法区分「整体回滚」和「按条指回」)。
- **H**:①写入后读回校验(在 `Save/Commit` 之后、写 `state.json` 之前;先全量免费比对
  size/bundle 坐标,再对每个自定义 bundle 的首尾记录做内容哈希——随机采样对「坐标算错」和
  「尾部截断」这两类真实故障都没用);②预览把已经是 0 字节的置空目标算进 `Unchanged`
  (`Apply` 会先回滚到基线所以不影响真实写入,但 `pscli plan` 和不带 `--yes` 的预览是在**已打补丁**的
  索引上算的,原样会把 0 字节覆盖 0 字节报成变更,连带污染退出码 6 与材质确认面板的数字)。

## 5. 剩余方案与一条已否决

优先级按「是否依赖我们拿不到的信息」排:

| 方案 | 内容 | 卡在哪 |
|---|---|---|
| **C 每扩展名最小合法空模板**(下一轮首选) | 用 42 字节级 JSON 骨架替代 0 字节(先例:`CreateTransparentDds`) | 格式已实测为文本,可做;需逐类实机验证 |
| **G 应用后日志体检**(C 的验收前提) | `<root>/logs/LatestClient.txt` 里带资源路径的 ERROR/WARN 与本次写入清单求交集 | **[已证实]** 该日志会打出 `Failed to create effect graph node … in graph "…fxgraph"` 这类**带路径的 WARN**(不是崩溃)。C/D/任何档位都需要它来拿实机凭据,否则「安全」只是推断 |
| D 间接抑制 | 材质/粒子本体不动,把它们引用的 `.dds` 换成已有透明 stub | 需要引用解析;效果最接近 C 且不动本体 |
| E 文件级灰度 | 列出将置空的路径/大小/引用者数,默认不选 | 数据现成(`PlanCommand.cs` 已逐条输出 `tier`),缺 UI |

C 排第一的理由是它**不依赖「谁在引用这个文件」这个信息**:让被置空的东西本身对所有引用者无害,
因此不会诱导使用者去删更多。这一点正是 B 被否决的核心(见下)。

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
