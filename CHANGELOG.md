# 更新日志 / Changelog

发布说明的事实来源。版本号来自 git tag:推 `v1.1` → CI 构建并把下面对应小节作为 Release 正文发布。
本节格式被 `.github/workflows/release.yml` 解析,**小节名必须与 tag 完全一致**(`## v1.1`,不是 `## [1.1]`)。

This file is the source of truth for release notes. Pushing a `vX.Y.Z` tag makes CI build the AppImage
and publish the matching section below as the release body — keep the heading exactly equal to the tag.

## v1.4 – 未发布 / unreleased

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
- 已知不足:客户端自带的中文只有繁體、且以压缩的 `.datc64` 存放(`data/balance/traditional chinese/`,
  219 张表),本期未接入,技能长尾仍是英文原名。
  *The client's own Chinese is Traditional-only and stored as compressed `.datc64` tables; mining it is
  still ahead of us, so the skill long tail keeps its English folder name.*

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
