# PoE2 Smoother —— SteamOS / Windows 上的 POE2 特效屏蔽工具

面向 SteamOS / Linux 掌机与 Windows 玩家的 Path of Exile 2 特效移除工具。
它把客户端里那些花里胡哨的技能特效、联赛特效、场景粒子"摘"掉，
把帧率和温度留给真正重要的东西——玩法本身。

**[English](README.en.md)** · 中文（当前）· 两份文件互为翻译，改一份请同步另一份

## 功能一览

- 自动定位 PoE2：Steam 多库、SD 卡安装、Windows 注册表 / 常见安装目录，以及 Steam 版与**独立客户端 `Content.ggpk`**（实验性）
- 特效按类别分组屏蔽：**技能 / 外观 / 怪物 / 联赛（深渊、迷雾、裂痕等 9 类）/ 环境 / 其他**，每行中英双语显示，支持按名称（中、英都行）、ID、路径搜索，整组一键勾选
  - 中文名优先用**你客户端自带的官方名字表**（ActiveSkills / WorldAreas 等，逐字照抄，见 [`docs/zh-labels.md`](docs/zh-labels.md)）；客户端只装了繁體覆盖表，所以**这一列是繁體**，界面其余部分仍是简体 —— 想换成官方简体名，在 Steam 里给 PoE2 下载"简体中文"语言后重跑一次挖掘脚本即可。官方表里没有对应的目录（变体、机制类、坐骑等）由我们按客户端术语表（[`docs/zh-terms.md`](docs/zh-terms.md)）译成繁體；只有含义不明的编号仍显示 `環境: 原名`
- 四个预设 **Safe / Clean / Performance / Full**，也可以自由组合类别、编辑忽略列表、保存为档案（profile）
- 应用、状态检测、游戏更新检测、一键还原；每次应用前自动备份原始 index
- **渲染压缩（实验性，默认全关）**：保留视觉、降渲染负载的另一条路 —— 贴图同分辨率 BC7→BC1、网格官方 LOD 替换、shader 精简、网格减面（强度滑条），另有降分辨率激进取向；与置空互斥，只动 `.dds`/`.fxgraph`/`.fmt`，`.pet` 配置永不触碰
- 中文 / English 双语界面，大字号、手柄/键盘可操作，适配 Steam Deck / Ally 的 1280×800（特效类别名列用的是客户端官方繁体名，见 [`docs/zh-labels.md`](docs/zh-labels.md)）
- 只修改本地客户端文件：不注入内存、不读写游戏进程

## 下载 / Download

预编译好的 AppImage 与 Windows 便携 ZIP 挂在 [Releases](https://github.com/pujie147/POE2Smoother/releases) 页上，
[最新发布版](https://github.com/pujie147/POE2Smoother/releases/latest) 永远指向正式版的最新 tag。
每次发布由 GitHub Actions 从 `v*` tag 自动构建（`.github/workflows/release.yml`：跑 `scripts/test-all.sh`
门禁 → 发布 GUI/CLI → 打包 AppImage 与 Windows ZIP → 挂上 Release），产物固定叫
**`PoE2Smoother-x86_64.AppImage`**、**`PoE2Smoother-win-x64.zip`**、**`pscli-win-x64.zip`**，所以下面这些链接长期有效：

```bash
curl -LO https://github.com/pujie147/POE2Smoother/releases/latest/download/PoE2Smoother-x86_64.AppImage
```

```powershell
# Windows：解压后用 poe2smoother.exe 启动
curl.exe -LO https://github.com/pujie147/POE2Smoother/releases/latest/download/PoE2Smoother-win-x64.zip
Expand-Archive .\PoE2Smoother-win-x64.zip .
.\PoE2Smoother\poe2smoother.exe
```

下载后务必核对一下 Release 正文里的 `SHA-256:`（GitHub 下载会丢掉可执行位，所以 Linux 上还要 `chmod +x`，见下一步）。

## 快速上手：用 GUI 屏蔽特效（推荐）

支持 **Steam 版**与**独立客户端（`Content.ggpk`，实验性）**。Linux 上以下操作在 SteamOS **桌面模式**完成；
Windows 上解压 ZIP 后直接运行 `poe2smoother.exe`（首次被 SmartScreen 拦截时选"更多信息 → 仍要运行"）。
游戏装在 `Program Files` 下时，**应用/还原**需要管理员权限——界面在写入被拒时会提示，点右上角"以管理员身份重启"即可。

1. **启动工具**——给 AppImage 加上执行权限后运行：

   ```bash
   chmod +x PoE2Smoother-x86_64.AppImage
   ./PoE2Smoother-x86_64.AppImage
   ```

   如果系统缺少 FUSE2 导致启动失败，改用解包模式运行：

   ```bash
   APPIMAGE_EXTRACT_AND_RUN=1 ./PoE2Smoother-x86_64.AppImage
   ```

2. **主页 → 检测**：自动找到游戏目录，显示 `Bundles2/_.index.bin` 或 `Content.ggpk` 的位置，并标注类型（Steam / 独立客户端）。找不到时用"浏览"手动选文件，或用环境变量 `POE2_INDEX` / `POE2_GGPK` / `POE2_ROOT` 指定（不要用 sudo / 管理员去跑检测）。
3. **生成规则包**：扫描**你自己当前客户端**的 index 生成可屏蔽清单——规则包不依赖别人维护的固定列表，游戏更新后重生成一次即可。
4. **特效页 → 选预设**：预设管的是**类别**（动哪些技能、联赛、环境），新手直接用 `safe`（默认），只处理玩家技能与外观特效，怪物、Boss、危险机制的特效全部保留。
   再多省一点渲染负载靠的是同一行的两个下拉框——**风险档**与**写法策略**。推荐两套组合：

   - **`safe` + 风险档「含特效资源置空」+ 写法策略「合法空资源」** —— 首选。置空的是特效自己的
     `.pet` / `.epk` / `.fxgraph`，而渲染开销的大头就在特效绘制上：这些文件换成最小合法空资源后不再产生绘制，
     预期比只做脚本手术更省帧。但地面、角色、UI 照常渲染，**收益有上限**，且尚未实机验证。
     注意风险档是**上限**不是开关——选了这一档，「安全:只动脚本」的 `.ao` 手术已经包含在内，不用重复设置。
   - **`safe` + 风险档「含材质置空(危险)」+ 写法策略「合法空资源」** —— 在上面基础上再置空 5 万余个 `.mat`，
     量级更大。材质被普通渲染（地面、角色、物体）共享，**可能连普通画面一起变黑**，帧率增量通常不如上一条明显，
     风险却实打实高一档：界面会要求二次确认，并列出将被丢弃的原始字节数。
   - 两套都保持「合法空资源」（它本来就是默认值），别退回「0 字节(旧行为)」：合法空资源的形状取自客户端自带的
     同形文件，「被置空的文件无害」不依赖「还有谁在引用它」这个我们拿不到的信息。
   - 后悔药在**主页 → 还原范围**：「只撤实验性(特效+材质)」或「只撤材质」，脚本手术照样保住。

   > ⚠ **这两套推荐都拿不到渲染压缩。** 写法策略停在「合法空资源」/「0 字节(旧行为)」时，特效页的「渲染压缩」面板
   > 根本不会出现；风险档停在「安全:只动脚本」时，即使面板打开、开关全勾，所有压缩目标也按设计被跳过（`plan` 里计入 `skippedExp`）。
   > 想走压缩那条路，得把写法策略换成「先压缩,无法压缩则合法空资源」或「只渲染压缩」，并把风险档同步提到对应档位
   > （贴图压缩属材质档，网格与 shader 压缩属特效档）；在那之前，上面两套就是当前可用的最优路径。
5. **（可选微调）**：搜索框查某个特效属于哪组、按组勾选/取消、把不想动的路径加进忽略列表（地面特效有逐项勾选树，
   其余类别靠列表里写路径前缀）、把这套选择存成档案下次直接加载。
6. **主页 → 应用**：先自动备份再写入（按钮需双击确认）。应用后游戏里对应特效即消失。如果显示"0 个文件变更"，工具会告诉你原因（规则包过期、全被忽略、当前风险档不含这些资源等），并提示下一步怎么做。
7. **进游戏验证**。想找回完整画面时：**主页 → 还原**（同样双击确认），从备份恢复原样，随时可再应用。

## 命令行等价流程

```bash
./pscli detect                      # 定位游戏
./pscli build-rules                 # 从当前 index 生成规则包
./pscli plan   --preset safe        # 预览将要变更多少文件（不写入）
./pscli apply  --preset safe --yes  # 应用（自动备份，= 风险档「安全:只动脚本」）
./pscli status                      # 查看当前状态
./pscli restore --yes               # 一键还原

# 上面两套推荐组合的等价写法（--blank-style template 就是默认值，写出来只为显式）
./pscli apply  --preset safe --experimental fx       --blank-style template --yes
./pscli apply  --preset safe --experimental material --blank-style template --yes
```

自动定位失败时，给任意命令附加 `--index <.../Bundles2/_.index.bin>`、`--game <游戏根目录>` 或
`--ggpk <.../Content.ggpk>`（Windows 上把 `pscli` 换成 `pscli.exe`）。

## 屏蔽原理：到底改了什么

- Steam 版：工具只改写 `Bundles2/_.index.bin`，并把替换后的资源追加进自己的自定义 bundle 目录（`Bundles2/LibGGPK3/`，基于 LibGGPK3）。**原始游戏文件一个字节都不动**。还原：index 没被外部动过时直接换回备份并删除自定义内容；若检测到 index 已变化（游戏更新），则**逐文件判断**——只有内容仍等于本工具当时写入版本的文件才把记录指回原 bundle，已被更新改过的文件直接跳过，绝不会把游戏降级回老版本（`--force` 仍可强制整档覆盖备份）。
- 独立客户端（实验性）：索引在 `Content.ggpk` 内部，写入是**原地修改、没有原子提交**——所以备份只导出内部索引（几十 MB，不是整个 40GB 容器），应用过程中请勿结束进程或断电；万一中断，重新"还原"会用备份索引写回。逐文件还原逻辑与 Steam 版完全一致。
- `.ao` 是 PoE2 的特效挂接脚本：工具删除其中的 `AddAttached` / `AddEffectPack` / `RemoveEffectPack` 调用，特效就不再挂到技能、物体上。相当于"脚本手术"，风险最低，也是 Safe 预设的主要内容。
- `.dds` 贴图：替换为 1×1 透明贴图。
- 把 `.pet` / `.epk` / `.fxgraph` / `.aoc`（特效资源）和 `.mat`（材质）**直接置空**属于实验性策略，默认不启用，
  并且分成两个独立档位：`fx` 只置空特效资源，`material` 才会动材质——材质被普通渲染（地面、角色、物体）共享，
  置空可能连普通画面一起变黑。量级要有概念：本机一次 `safe` + 全实验性实测动过 **73,581 个文件**
  （`.mat` 50,399 / `.pet` 15,556 / `.epk` 5,054 / `.fxgraph` 2,572）。置空写入的是该格式的**最小合法空资源**
  （2–42 字节，形状取自客户端自带的 `empty.epk` / `00_null.pet` / `empty.mat` 等，不是凭空发明），
  而不是 0 字节：这样「被置空的文件无害」不依赖「还有谁在引用它」这个我们拿不到的信息；
  想退回旧行为用「写法策略」下拉框 / `--blank-style empty`。GUI 是「风险档」下拉框（选到材质会在窗口内
  再确认一次并列出被丢弃的原始字节数），命令行是 `--experimental safe|fx|material`；旧的 `--include-experimental`
  等价于 `--experimental material`，语义没变。出错后可以只撤销材质、保住脚本手术：
  GUI 主页「还原范围」/ `pscli restore --undo-tiers material`；`pscli health`（只读）会把客户端日志里
  带资源路径的告警与本次写入的文件求交集。细节与未落地的替代方案见
  [`docs/experimental-risk-tiers.md`](docs/experimental-risk-tiers.md)。
- 默认忽略列表保护怪物、Boss 和危险机制相关资源，所以屏蔽之后画面反而更好读：你看得见自己站在哪些地板技能里。
- **地面效果是上面那句话的例外**：它们属于 `skill.*`，默认**会**被置空，而「冰緩地面」「點燃地面」「奉獻地面」
  这类脚下信息恰恰是要看的。想保住它们：**忽略列表 → 左栏「地面特效」→ 勾上**（整类勾，或只勾其中几个目录，
  比如只留奉獻地面）。勾一项 = 往右边那份列表写一行路径前缀，两边说的永远是同一件事，不另存一份状态。

## 渲染压缩（实验性，默认全关）

除了「置空」，工具还提供一条**保留视觉、降低渲染负载**的路：渲染压缩。它在 Effects tab 是独立开关
（外加减面强度滑条与降分辨率激进取向），**默认全部关闭**，需要风险档与开关同时满足才生效；
启用某类压缩后，该类资源就不再被置空（两者互斥）。

| 开关 | 作用 | 风险档 | 说明 |
|---|---|---|---|
| 贴图同分辨率 | `.dds` BC7→BC1 | material | 分辨率与 UV 不变，约 -50%；只压 opaque/cutout，**平滑 alpha（软粒子）跳过** |
| 降分辨率（激进） | 在贴图压缩之上再降一半分辨率 | material | 软粒子转 BC3 保留柔和边缘（-75%）、opaque 用 BC1（-87%）；**会改变图集 UV 切取，先实机 A/B** |
| 网格 LOD | `.fmt` 官方 LOD00→LOD1/2 | fx | 用同目录官方降模替换，最安全 |
| 网格减面 | `.fmt` 顶点聚类 decimation | fx | 强度滑条 0–100；billboard/quad 跳过 |
| shader 精简 | `.fxgraph` 白名单图换精简模板 | fx | 仍渲染可辨认效果，风险最高 |

CLI 等价：`--compress-tex` / `--tex-downscale` / `--compress-shader` / `--mesh-lod` / `--mesh-decimate <0-100>`。

只处理特效网格 / 粒子贴图 / 白名单 shader，**跳过怪物与 Gameplay 预告**（剪影与运动本身就是方向信息），
**`.pet` 配置永不改动**（配置影响技能范围/判定）。所有写入复用既有备份/校验/还原链路，还原零改动。
贴图 codec 已离线逐字节验证（BC7 解码对照独立 Python 移植在 26 个真实样本 / 246 mip 一致），
但**「客户端接受 + 视觉可辨认 + 技能范围不变」仍需实机 A/B 才能转正**，步骤见
[`docs/experimental-risk-tiers.md`](docs/experimental-risk-tiers.md) §7.3。

## 预设速查

| 预设 | 覆盖内容 |
|---|---|
| `safe`（默认） | 玩家技能 + 外观特效；怪物、联赛、环境全部保留 |
| `clean` | 技能 + 外观 + 环境 + 联赛特效 |
| `performance` | 联赛 + 环境特效（保留所有技能特效） |
| `full` | 范围内全部（忽略列表除外） |

## 注意事项 / FAQ

- **先关游戏再应用或还原**：检测到 PoE2 进程会拒绝执行（确有需要可 `--force`）。
- **Windows 写入被拒**：Steam / 独立客户端默认装在 `Program Files` 下，普通权限写不进去；点界面右上角"以管理员身份重启"，
  或在管理员终端里跑 `pscli.exe`。检测、生成规则包等只读操作不需要管理员。
- **游戏更新**后直接点"还原"是安全的：工具会逐个文件比对，只撤掉自己抑制过的文件，更新过的文件保留新版本，不会被旧备份覆盖；随后重新"生成规则包 → 应用"即可。
- Steam"验证游戏文件完整性"会清掉补丁，不影响游戏，重新应用一次就行。
- **如果「应用」报"存在 LibGGPK3 自定义 bundle 但没有状态文件"**：说明客户端里留着本工具早先写的 bundle，
  而 `state.json` 已经不在了（被改动、被清理，或客户端更新后对不上号）。这时「还原」也走不通——它不知道该用哪份备份。
  主页会出现一个**「清理无人引用的遗留 bundle」**面板：它先读当前 index，**只删没有任何记录引用的那些**，
  仍被引用的原地留着并告诉你，所以不会像 `--force` 那样可能留下悬空记录。删完直接重新「生成规则包 → 应用」即可。
  *If Apply says custom bundles exist but there is no state file, Home offers a cleanup that removes only
  the bundles no live index record references — unlike `--force`, it cannot leave dangling records.*
- **独立客户端（`Content.ggpk`）仍是实验性**：写入是原地修改，没有原子提交（详见上一节）。建议先用一份复制的 `Content.ggpk` 试一遍。
- **会封号吗？** 工具只修改本地客户端文件，不注入内存、不读进程数据。但客户端修改本身存在固有风险，重要账号请自行评估——建议只用 `safe`，并保留备份。
- 屏蔽粒度是类别级（如"火焰技能""迷雾联赛"）+ 忽略列表按路径前缀微调；**只有地面特效做到了逐项勾选**
  （忽略列表页左栏那棵树，类别 → 目录两级），其余类别仍是整类勾选。

> 使用本工具修改客户端文件的风险由使用者自行承担。本项目不复用 PoeSmoother 的授权规则数据。

## 赞助与感谢

感谢每一位赞助、反馈和帮忙测试的朋友——你们的支持是这个工具持续更新的全部理由。

如果它在后期副本里救回了你的帧率，欢迎扫一下支付宝请我一杯咖啡，我会继续跟进版本适配和新功能：

![支付宝收款码](docs/1794293378652.jpg)

## 源码与构建

```text
src/PsCore/     游戏定位、index 会话（Steam / Content.ggpk）、备份/状态、应用/还原
src/PsRules/    规则包模型、分类器、预设、忽略列表、替换策略
src/PsCli/      命令行 pscli
src/PsApp/      Avalonia GUI
tests/          核心测试 + GUI headless 冒烟测试
packaging/      .desktop 入口、图标、兜底运行库
scripts/        构建、AppImage、Windows ZIP、统一测试
```

需要 .NET SDK 8（`net8.0`，Linux / Windows x64 自包含发布）：

```bash
bash scripts/build-linux.sh        # GUI + CLI + tar.gz
bash scripts/build-appimage.sh     # AppImage（Linux 或 WSL 中执行）
pwsh scripts/build-windows.ps1     # Windows 便携 ZIP（Windows 本机或 Linux CI 交叉发布）
bash scripts/test-all.sh           # 构建 + 测试 + CLI 端到端（Windows 用 powershell -File scripts/test-all.ps1）
```

版本号只有一个来源：**git tag**。发布时 CI 把 `v1.1` 去掉前导 `v` 后用 `-p:Version=` 传进构建，
GUI 标题旁和 `pscli version` 显示的都是它；本地不带 tag 构建出来是 `0.0.0-dev`。
手动想指定版本：`PS_VERSION=1.2.3 bash scripts/build-linux.sh`。

已知限制：只支持 Steam 版与独立客户端 `Content.ggpk` 两种客户端形态（GGPK 路径为实验性）；
实验性置空策略默认关闭（材质档风险最高），且「合法空资源真的不影响画面与加载」目前只有客户端自带的同形文件作为依据、
尚未实机验证（`pscli health` 就是为此准备，见 `docs/experimental-risk-tiers.md`）；Windows 与 Linux 的产物都未做代码签名。分发或商用前请确认 LibGGPK3 / LibBundledGGPK3
（AGPL-3.0-or-later）的授权条款。
