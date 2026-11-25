# 万界长卷 · Yijuanwanjie

万界长卷是一款中国风回合制卡牌与地牢探索 Roguelite。这里收录游戏的核心玩法源码与内容配置，可用于阅读战斗和探索规则，或在团结引擎项目中集成。

| 路径 | 内容 |
| --- | --- |
| `Assets/Scripts/Core` | 战斗结算、卡牌、敌方行为与基础规则 |
| `Assets/Scripts/Content` | 内容模型与运行时加载 |
| `Assets/Scripts/Dungeon` | 地牢节点、路线与探索 |
| `Assets/Scripts/Meta`、`Progression`、`Persistence` | 局外系统、进度与存档 |
| `ContentSource/cards` | 卡牌、敌人等内容的作者源 JSON |

## 集成说明

原项目使用团结引擎 1.10.2（Unity 2022.3.62t14）。集成时将六个程序集及其 `.meta` 文件一同导入；内容加载器需要生成后的运行时内容包。此源码快照没有配套的游戏场景、运行时内容包或美术资源，克隆后无法直接启动游戏。

## 许可证

本仓库收录的源码、内容配置及随附文档使用 [MIT 许可证](LICENSE)。
