# 官方繁體术语表(从客户端挖的)

`[英文|繁體]` 形式写在客户端的 `keywordpopups` / `activeskills` / `buffdefinitions` / `clientstrings2`
里 —— 那是 GGG 自己在遊戲内给术语配的繁体名。共抽到 **334** 个术语对,下面按出现次数列出前 110 个
(`次数`=出现次数,`写法`=同一个英文词见到的不同繁体写法数,>1 说明有并列、需人工定)。

**用途**:词表里凡是我们自己起的名字(`labels-zh-named.json`),用词都要对齐这张表 —— 官方叫「冰冷」就不要写「冰霜」,
叫「十字弓」就不要写「弩」,叫「捷光環」就不要写「冰之旗帜」。上一版正是因为跳过了这一步,68 条被官方名顶掉。

重跑:`python3 -I scripts/mine-zh-names.py --index <_.index.bin> terms`。

| 英文 | 官方繁體 | 次数 | 写法数 |
|---|---|---:|---:|
| `hit` | 擊中 | 190 | 5 |
| `attack` | 攻擊 | 162 | 2 |
| `projectile` | 投射物 | 101 | 1 |
| `lightning` | 閃電 | 93 | 4 |
| `minion` | 召喚物 | 87 | 2 |
| `spell` | 法術 | 85 | 3 |
| `charges` | 暴擊球 | 33 | 5 |
| `fire` | 火焰 | 64 | 2 |
| `ignite` | 點燃 | 62 | 3 |
| `shock` | 感電 | 61 | 2 |
| `debuff` | 減益效果 | 61 | 2 |
| `chaos` | 混沌 | 58 | 2 |
| `energy` | 能量 | 61 | 1 |
| `physical` | 物理 | 60 | 1 |
| `buff` | 增益效果 | 47 | 2 |
| `consume` | 消耗 | 57 | 1 |
| `strike` | 打擊 | 55 | 1 |
| `resistances` | 抗性 | 31 | 6 |
| `freeze` | 冰凍 | 53 | 1 |
| `armour` | 護甲 | 28 | 2 |
| `slam` | 重擊 | 47 | 2 |
| `curse` | 詛咒 | 43 | 3 |
| `energyshield` | 能量護盾 | 48 | 1 |
| `command` | 號令 | 45 | 1 |
| `cold` | 冰冷 | 44 | 1 |
| `channelling` | 引導 | 43 | 1 |
| `slow` | 緩速 | 31 | 5 |
| `evasion` | 閃避 | 27 | 2 |
| `shapeshift` | 變形 | 39 | 1 |
| `trigger` | 觸發 | 36 | 3 |
| `heavystun` | 沉重暈眩 | 38 | 1 |
| `poison` | 中毒 | 21 | 6 |
| `ward` | 符文保護 | 37 | 2 |
| `block` | 格擋 | 33 | 2 |
| `bleeding` | 流血 | 37 | 1 |
| `empowered` | 強化 | 36 | 1 |
| `rage` | 盛怒 | 35 | 1 |
| `corpse` | 屍體 | 34 | 1 |
| `elementalinfusion` | 灌注 | 21 | 5 |
| `electrocute` | 觸電 | 32 | 2 |
| `allies` | 友方 | 31 | 2 |
| `critical` | 暴擊 | 27 | 3 |
| `reviving` | 復甦 | 29 | 2 |
| `elementaldamage` | 元素 | 19 | 3 |
| `chain` | 連鎖 | 29 | 1 |
| `mark` | 印記 | 25 | 3 |
| `presence` | 存在範圍 | 23 | 2 |
| `spear` | 長矛 | 29 | 1 |
| `remnant` | 痕跡 | 25 | 2 |
| `glory` | 榮耀 | 27 | 1 |
| `shield` | 盾牌 | 26 | 1 |
| `cullingstrike` | 撲殺 | 20 | 3 |
| `rarity` | 稀有 | 13 | 5 |
| `melee` | 近戰 | 24 | 1 |
| `armourbreak` | 完全破甲 | 6 | 8 |
| `stun` | 暈眩 | 18 | 3 |
| `flask` | 藥劑 | 23 | 1 |
| `grenade` | 擲彈 | 22 | 1 |
| `aura` | 光環 | 21 | 1 |
| `ammunition` | 裝填你的十字弓 | 14 | 3 |
| `gain` | 獲得 | 20 | 2 |
| `knockback` | 擊退 | 20 | 1 |
| `crossbow` | 十字弓 | 20 | 1 |
| `frozen` | 冰凍 | 18 | 2 |
| `chill` | 冰緩 | 19 | 1 |
| `warcry` | 戰吼 | 19 | 1 |
| `elementalailments` | 元素異常狀態 | 13 | 2 |
| `martialweapon` | 武器 | 12 | 3 |
| `icecrystals` | 冰晶 | 16 | 2 |
| `totem` | 圖騰 | 17 | 1 |
| `itemrarity` | 物品稀有度 | 7 | 7 |
| `chilledground` | 冰緩地面 | 14 | 2 |
| `detonator` | 引爆 | 8 | 3 |
| `companion` | 盟友 | 16 | 1 |
| `waystone` | 換界石 | 16 | 1 |
| `bear` | 巨熊型態 | 13 | 2 |
| `werewolf` | 狼人型態 | 13 | 2 |
| `skillspeed` | 技能速度 | 13 | 2 |
| `wyvern` | 飛龍型態 | 12 | 2 |
| `pierce` | 穿透 | 14 | 1 |
| `mageslegacy` | 魔遺 | 14 | 1 |
| `repeat` | 重複 | 9 | 3 |
| `orb` | 球體 | 13 | 1 |
| `bloodloss` | 失血 | 13 | 1 |
| `quarterstaff` | 細杖 | 13 | 1 |
| `moltenfissure` | 熔岩裂縫 | 11 | 2 |
| `ignitedground` | 點燃地面 | 9 | 2 |
| `hinder` | 阻礙 | 12 | 1 |
| `primedstun` | 可暈眩 | 10 | 3 |
| `esrecharge` | 充能 | 9 | 3 |
| `augment` | 增幅 | 12 | 1 |
| `bow` | 弓 | 11 | 1 |
| `maim` | 癱瘓 | 11 | 1 |
| `monstermodifiers` | 詞綴 | 8 | 2 |
| `monstercategory` | 野獸 | 6 | 3 |
| `ailments` | 異常狀態 | 11 | 1 |
| `aftershock` | 餘震 | 10 | 1 |
| `perfecttiming` | 完美時機 | 10 | 1 |
| `icefragment` | 寒冰碎片 | 10 | 1 |
| `thorns` | 荊棘 | 10 | 1 |
| `guard` | 守護 | 9 | 2 |
| `banner` | 鬥旗 | 7 | 2 |
| `maximumresistances` | 最大元素抗性 | 2 | 5 |
| `detonationtime` | 引爆 | 8 | 2 |
| `jaggedground` | 碎裂地面 | 9 | 1 |
| `whirlwind` | 旋風 | 9 | 1 |
| `accuracy` | 命中 | 7 | 2 |
| `buffmagnitude` | 幅度 | 8 | 2 |
| `pack` | 怪物群 | 7 | 2 |
| `markactivate` | 啟動 | 8 | 1 |

## 与旧手写名对照过的几个纠正

| 类别 | 我原先写的 | 官方/更正规 |
|---|---|---|
| `crossbow_*` | 弩·… | 十字弓(术语表 `crossbow=十字弓`) |
| `cold_*` | 冰霜·… | 冰冷(术语表 `cold=冰冷`) |
| `herald_*` | …之旗帜 | 捷光環(`herald=捷光環`) |
| `shock_*` | 震擊/震撼 | 感電(`shock=感電`) |
| `freeze/frozen` |  freezing/冰冻 | 冰凍(`freeze=冰凍`) |
| `ignite` | 点燃 | 點燃(`ignite=點燃`) |
| `warcry` | 战吼 | 戰吼(`warcry=戰吼`) |
| `totem` | 图腾 | 圖騰(`totem=圖騰`) |
| `slam` | 猛击 | 重擊(`slam=重擊`) |
| `strike` | 打击 | 打擊(`strike=打擊`) |
| `debuff/buff` | 减益/增益 | 減益效果 / 增益效果 |
