# PoE2 Smoother —— SteamOS / Windows 上的 POE2 特效屏蔽工具

面向 SteamOS / Linux 掌机与 Windows 玩家的 Path of Exile 2 特效移除工具。
它把客户端里那些花里胡哨的技能特效、联赛特效、场景粒子"摘"掉，
把帧率和温度留给真正重要的东西——玩法本身。

**[English](README.en.md)** · 中文（当前）· 两份文件互为翻译，改一份请同步另一份

## 功能一览

- 自动定位 PoE2：Steam 多库、SD 卡安装、Windows 注册表 / 常见安装目录，以及 Steam 版与**独立客户端 `Content.ggpk`**（实验性）
- 特效按类别分组屏蔽：**技能 / 外观 / 怪物 / 联赛（深渊、迷雾、裂痕等 9 类）/ 环境 / 其他**，支持按名称、ID、路径搜索，整组一键勾选
- 四个预设 **Safe / Clean / Performance / Full**，也可以自由组合类别、编辑忽略列表、保存为档案（profile）
- 应用、状态检测、游戏更新检测、一键还原；每次应用前自动备份原始 index
- 中文 / English 双语界面，大字号、手柄/键盘可操作，适配 Steam Deck / Ally 的 1280×800
- 只修改本地客户端文件：不注入内存、不读写游戏进程

## 下载 / Download

预编译好的 AppImage 与 Windows 便携 ZIP 挂在 [Releases](https://github.com/pujie147/mySmoother2/releases) 页上，
[最新发布版](https://github.com/pujie147/mySmoother2/releases/latest) 永远指向正式版的最新 tag。
每次发布由 GitHub Actions 从 `v*` tag 自动构建（`.github/workflows/release.yml`：跑 `scripts/test-all.sh`
门禁 → 发布 GUI/CLI → 打包 AppImage 与 Windows ZIP → 挂上 Release），产物固定叫
**`PoE2Smoother-x86_64.AppImage`**、**`PoE2Smoother-win-x64.zip`**、**`pscli-win-x64.zip`**，所以下面这些链接长期有效：

```bash
curl -LO https://github.com/pujie147/mySmoother2/releases/latest/download/PoE2Smoother-x86_64.AppImage
```

```powershell
# Windows：解压后用 poe2smoother.exe 启动
curl.exe -LO https://github.com/pujie147/mySmoother2/releases/latest/download/PoE2Smoother-win-x64.zip
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
4. **特效页 → 选预设**：新手直接用 `safe`（默认），只处理玩家技能与外观特效，怪物、Boss、危险机制的特效全部保留。
5. **（可选微调）**：搜索框查某个特效属于哪组、按组勾选/取消、把不想动的路径加进忽略列表、把这套选择存成档案下次直接加载。
6. **主页 → 应用**：先自动备份再写入（按钮需双击确认）。应用后游戏里对应特效即消失。如果显示"0 个文件变更"，工具会告诉你原因（规则包过期、全被忽略、实验性未启用等），并提示下一步怎么做。
7. **进游戏验证**。想找回完整画面时：**主页 → 还原**（同样双击确认），从备份恢复原样，随时可再应用。

## 命令行等价流程

```bash
./pscli detect                      # 定位游戏
./pscli build-rules                 # 从当前 index 生成规则包
./pscli plan   --preset safe        # 预览将要变更多少文件（不写入）
./pscli apply  --preset safe --yes  # 应用（自动备份）
./pscli status                      # 查看当前状态
./pscli restore --yes               # 一键还原
```

自动定位失败时，给任意命令附加 `--index <.../Bundles2/_.index.bin>`、`--game <游戏根目录>` 或
`--ggpk <.../Content.ggpk>`（Windows 上把 `pscli` 换成 `pscli.exe`）。

## 屏蔽原理：到底改了什么

- Steam 版：工具只改写 `Bundles2/_.index.bin`，并把替换后的资源追加进自己的自定义 bundle 目录（`Bundles2/LibGGPK3/`，基于 LibGGPK3）。**原始游戏文件一个字节都不动**。还原：index 没被外部动过时直接换回备份并删除自定义内容；若检测到 index 已变化（游戏更新），则**逐文件判断**——只有内容仍等于本工具当时写入版本的文件才把记录指回原 bundle，已被更新改过的文件直接跳过，绝不会把游戏降级回老版本（`--force` 仍可强制整档覆盖备份）。
- 独立客户端（实验性）：索引在 `Content.ggpk` 内部，写入是**原地修改、没有原子提交**——所以备份只导出内部索引（几十 MB，不是整个 40GB 容器），应用过程中请勿结束进程或断电；万一中断，重新"还原"会用备份索引写回。逐文件还原逻辑与 Steam 版完全一致。
- `.ao` 是 PoE2 的特效挂接脚本：工具删除其中的 `AddAttached` / `AddEffectPack` / `RemoveEffectPack` 调用，特效就不再挂到技能、物体上。相当于"脚本手术"，风险最低，也是 Safe 预设的主要内容。
- `.dds` 贴图：替换为 1×1 透明贴图。
- 把 `.pet` / `.epk` / `.mat` / `.fxgraph` 直接置空属于**实验性**策略，默认不启用（需要显式勾选 / `--include-experimental`）。
- 默认忽略列表保护怪物、Boss 和危险机制相关资源，所以屏蔽之后画面反而更好读：你看得见自己站在哪些地板技能里。

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
- **独立客户端（`Content.ggpk`）仍是实验性**：写入是原地修改，没有原子提交（详见上一节）。建议先用一份复制的 `Content.ggpk` 试一遍。
- **会封号吗？** 工具只修改本地客户端文件，不注入内存、不读进程数据。但客户端修改本身存在固有风险，重要账号请自行评估——建议只用 `safe`，并保留备份。
- 屏蔽粒度是类别级（如"火焰技能""迷雾联赛"）+ 忽略列表按路径前缀微调，暂不支持逐个技能勾选。

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
实验性置空策略默认关闭；Windows 与 Linux 的产物都未做代码签名。分发或商用前请确认 LibGGPK3 / LibBundledGGPK3
（AGPL-3.0-or-later）的授权条款。
