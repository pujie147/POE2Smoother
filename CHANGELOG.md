# 更新日志 / Changelog

发布说明的事实来源。版本号来自 git tag:推 `v1.1` → CI 构建并把下面对应小节作为 Release 正文发布。
本节格式被 `.github/workflows/release.yml` 解析,**小节名必须与 tag 完全一致**(`## v1.1`,不是 `## [1.1]`)。

This file is the source of truth for release notes. Pushing a `vX.Y.Z` tag makes CI build the AppImage
and publish the matching section below as the release body — keep the heading exactly equal to the tag.

## [Unreleased] / 未发布

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
