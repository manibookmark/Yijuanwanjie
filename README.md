# 万界长卷 · Yijuanwanjie

《万界长卷》是一款国风回合制卡牌 Roguelite。你扮演执卷人，与西游英灵同行，在受侵蚀的河洛卷中探路、战斗，逐步构筑牌组。

[![开场 CG，远山宫阙与云海上的地球](docs/media/opening-cg.jpg)](https://github.com/manibookmark/Yijuanwanjie/releases/download/v2026.11.25/WanjieScroll-Opening-CG.mp4)

[观看开场 CG（3 分 32 秒）](https://github.com/manibookmark/Yijuanwanjie/releases/download/v2026.11.25/WanjieScroll-Opening-CG.mp4) · [下载 Windows 试玩（EXE）](https://github.com/manibookmark/Yijuanwanjie/releases/download/v2026.11.25/WanjieScroll-Play.exe)

目前做了三个主关卡 **双叉岭、鹰愁涧、高老庄**，预计共 **15 关**。花果山是上手教程。

## 核心玩法

- **回合制战斗。** 观察敌人意图，分配有限的灵力，决定出牌顺序。
- **叠势与相生。** 按费用逐张加 1，可以接续数字叠势并强化相关牌效。也可以利用五行相生组织连锁。
- **探索与构筑。** 选择路线，经历战斗和地图事件，用新卡、灵印和英灵能力调整牌组，再迎战关底首领。

作者库已登记 **97 张卡牌设计**，其中 **50 张已实现并通过功能与界面验收**。最终卡牌总量待定。

## Windows 游玩

下载后双击 `WanjieScroll-Play.exe`。首次启动会自动下载并校验约 1.6 GB 的游戏数据，之后会复用已安装的数据。

## 接着开发

1. 安装团结引擎 1.10.2（`2022.3.62t14`）。
2. 克隆本仓库，双击根目录的 `Setup.cmd`。首次会下载约 1.9 GB 素材、恢复引擎依赖并打开工程。
3. 导入完成后就可以编辑场景和代码，也可以按 Play 从 Loading 场景试玩。以后直接从团结 Hub 打开这个目录。

改卡牌等内容配置时，编辑 `ContentSource/cards/`，再点团结引擎菜单「万界长卷 / 内容 / 生成运行时内容包」。

<details>
<summary>手动打开、离线恢复与构建</summary>

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Setup-Project.ps1 -OpenEditor
```

- 若准备完成后工程没有自动打开，可用团结 Hub 打开克隆目录，再打开 `Assets/Scenes/Loading.unity`。引擎安装在自定义位置时，可以用 `-EditorPath` 指定 `Tuanjie.exe`。
- 已有完整素材 ZIP 时，可用 `-AssetArchivePath` 和 `-RuntimeMediaArchivePath` 从本地恢复。准备脚本会把清单里的素材恢复到发布版本，修改这些素材后请通过 Hub 开工程。
- 构建 Windows x64 玩家版需要安装对应的构建支持。批处理入口为 `WanjieScroll.EditorTools.PublicPlayerBuilder.BuildWindows`，输出目录是 `Builds/Public`。

</details>

## 源码与素材

- `Assets/Scenes/`、`Assets/Prefabs/`、`Assets/Scripts/` 提供正式游戏场景、预制件与源码。
- `ContentSource/cards/` 是卡牌、敌人等内容的作者源，`Assets/Resources/Content/Generated/` 是生成后的运行时包。
- 克隆仓库后也可以双击 `Play.cmd` 启动。Windows 试玩启动器的源码和构建脚本位于 `tools/launcher/`。
- 美术、音频、字体和视频以原始运行格式随同版本 Release 发布。素材及校验值逐项列在 `runtime-media.manifest.json` 和 `external-assets.manifest.json`。脚本将它们恢复到原始路径，Git 中的 `.meta` 保留引擎资源身份。

软件源码和内容配置采用 [MIT](LICENSE)。项目自有游戏美术、音乐、音效、配音和视频采用 CC BY 4.0。字体遵循上游许可，团结引擎及其下载依赖有独立许可。详见 [素材与依赖许可](ASSET-LICENSES.md)。
