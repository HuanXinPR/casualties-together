<div align="center">

[English](README.md) | 简体中文

<a href="https://www.nexusmods.com/scavprototype/mods/67">
<img alt="Casualties: Together" src="https://github.com/creaturefeaturelarry/casualties-together/raw/refs/heads/main/.github/ct6.png">
</a>

[![Latest Release](https://img.shields.io/github/v/release/HuanXinPR/casualties-together?style=flat-square&include_prereleases&logo=github&label=Release)](https://github.com/HuanXinPR/casualties-together/releases)
[![Build and Release](https://img.shields.io/github/actions/workflow/status/HuanXinPR/casualties-together/build-and-release.yml?branch=main&style=flat-square&logo=github&label=Build)](https://github.com/HuanXinPR/casualties-together/actions/workflows/build-and-release.yml)
[![Upstream](https://img.shields.io/badge/Upstream-creaturefeaturelarry%2Fcasualties--together-blue?style=flat-square&logo=github)](https://github.com/creaturefeaturelarry/casualties-together)
[![Game Version](https://img.shields.io/badge/Game_Version-v1.0.2-darkgreen?style=flat-square&logo=steam)](https://store.steampowered.com/app/4576510)

</div>

### 《未知伤亡》(Casualties: Unknown) 在线合作联机模组（补丁维护版）

[![Casualties: Together](https://img.shields.io/badge/Casualties%3A%20Together%20Mod%20Discord-%235865F2.svg?logo=discord&logoColor=white)](https://discord.gg/SA6H7mA6De)
[![Target Planet](https://img.shields.io/badge/Official%20Casualties%3A%20Unknown%20Discord-%235865F2.svg?logo=discord&logoColor=white)](https://discord.com/channels/955738554129063947/1470206122844618802)

> **仓库说明**：本仓库跟进上游 [Casualties: Together](https://github.com/creaturefeaturelarry/casualties-together)（兼容 `v5.5.4` 线上通信协议），移除了对 `CUCoreLib` 与 `Skins` 模块的耦合依赖，集成物品/医疗/武器同步、单机直通保护、Unicode 昵称与动态字体回退、简繁俄本地化补全等多项修复，并提供零 Secret 云端自动构建与发布。
>
> 支持上游原作者：[Buy Me A Coffee](https://buymeacoffee.com/casualtiestogether)

---

## 安装指南

### 前置条件

- 游戏本体：[Casualties: Unknown](https://store.steampowered.com/app/4576510)（`v1.0.2`）
- 模组加载器：[BepInEx 5.4.23.5 (win_x64)](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip)
- 联机模组包：前往本仓库 [GitHub Releases](https://github.com/HuanXinPR/casualties-together/releases) 下载 `CasualtiesTogether-v*.zip`

### 安装步骤

1. 在 Steam 库中右键《Casualties: Unknown》，点击 `管理 > 浏览本地文件`，打开游戏根目录。
2. 将 `BepInEx_win_x64_5.4.23.5.zip` 直接解压到游戏根目录。
3. 将 `CasualtiesTogether-v*.zip` 直接解压到游戏根目录（压缩包顶层已经是 `BepInEx/plugins/Together/`，会自动合并到 `BepInEx` 目录中）。
4. 解压完成后，游戏根目录结构如下：

```text
Casualties Unknown/
├── CasualtiesUnknown.exe
├── CasualtiesUnknown_Data/
├── winhttp.dll                       <- 来自 BepInEx 压缩包
├── doorstop_config.ini               <- 来自 BepInEx 压缩包
└── BepInEx/
    ├── core/                         <- 来自 BepInEx 压缩包
    └── plugins/
        └── Together/                 <- 来自 CasualtiesTogether-v*.zip
            ├── CasualtiesMP.dll
            ├── K4os.Compression.LZ4.dll
            ├── LiteEntitySystem.dll
            ├── LiteNetLib.dll
            ├── OpusSharp.Core.dll
            ├── opus.dll
            ├── RefMagic.dll
            └── System.Runtime.CompilerServices.Unsafe.dll
```

5. **通过 Steam 库**启动游戏，主菜单下方出现联机按钮即代表安装成功。
   - 若想暂时回到纯净单人模式，在 `设置 > 通用` 中点击 `停用联机模组`（`Deactivate MP Mod`），或在 Steam 启动项添加 `--mp-disable`。
   - **Linux / Proton 用户**：在 Steam 启动选项中填入 `WINEDLLOVERRIDES="winhttp=n,b" %command%`，让 Proton 正常加载 BepInEx。

---

## 发布包类型与每日构建（Nightly）

每个 [GitHub Release](https://github.com/HuanXinPR/casualties-together/releases) 包含 4 类构建产物与 `SHA256SUMS.txt` 校验清单：

| 文件名 | 用途说明 |
| --- | --- |
| `CasualtiesTogether-v<版本>.zip` | **标准完整包**（顶层 `BepInEx/plugins/Together/`，内含 Release 版 `CasualtiesMP.dll` 与 7 个运行时依赖库，共 8 个文件）。首次安装或完整更新时直接解压覆盖游戏根目录即可。 |
| `CasualtiesMP.dll` | **Release 单文件 DLL**。已经装好完整包与依赖库时，可直接覆盖 `BepInEx/plugins/Together/CasualtiesMP.dll` 快速升级。 |
| `CasualtiesTogether-v<版本>+debug.zip` | **Debug 调试完整包**。开启 `DEBUG`/`TESTING` 诊断日志并附带便携式符号文件 `CasualtiesMP.pdb`（共 9 个文件），用于排查崩溃堆栈与同步异常。 |
| `CasualtiesMP+debug.dll` | **Debug 单文件 DLL**。排查问题时可将其重命名为 `CasualtiesMP.dll` 替换至 `BepInEx/plugins/Together/`。 |
| `v<版本>-nightly.<YYYYMMDD>` *(Pre-release)* | **每日定时构建（Nightly）**。当 `main` 分支距上次发版或上次每日构建有新提交时，GitHub Actions 会在每日固定时间自动打包发布预发布测试版。 |

---

## 房间哈希检查说明（验证游戏哈希 / `Verify Game Hash`）

客机进房握手时，房主端默认开启 **`验证游戏哈希`（`Verify Game Hash`）** 开关，它会校验双方本地 `Assembly-CSharp.dll` 与 `CasualtiesMP.dll` 的二进制文件哈希是否完全相同：

- 本仓库构建产物与上游原版 `v5.5.4` 保持相同的联机协议版本号（`1.0.2_MPv5.5.4`），网络数据包与规则布局完全兼容。
- 但由于本仓库补丁版、上游原版、`+debug` 调试版及各期 `nightly` 构建编译出的 `CasualtiesMP.dll` 二进制内容不同，文件哈希并不相同。
- **同包联机**：若所有玩家安装的都是同一份发布包，保持默认的「验证游戏哈希」开启即可。
- **跨构建互通联机**：若房间内混用了上游原版 `v5.5.4`、本仓库补丁版或 `+debug` 版，请房主在建房配置界面取消勾选 **`验证游戏哈希`（`Verify Game Hash`）**，即可避免出现「游戏文件哈希不匹配（Game file hashes do not match）」拒连提示。

---

## 注意事项

- 如果 Steam 大厅列表无法刷新或创建失败，请确认是从 Steam 库直接启动游戏，而非双击桌面/文件夹里的 `CasualtiesUnknown.exe`。
- 本模组没有客户端预测、没有网络所有权、没有确定性随机数、也没有反作弊，建议使用稳定的网络环境游玩。
- 安装模组后若遇到闪退或同步故障，请在模组仓库或联机模组 Discord 反馈，请勿打扰游戏原作者。

---

## 主要功能

- Steam 大厅列表、密码房、IPv4/IPv6 直连以及房主断线迁移（Host Transfer）
- 根据角色语言能力与听力损伤实时失真的游戏内语音聊天和文字聊天
- 鼠标中键标记位置（双击发送高亮警报）
- 右键队友打开交互菜单：查看健康面板与包扎治疗、心肺复苏（CPR）、查看/拿取背包物品、喂食/使用物品、推开、搬运与背人叠罗汉
- 靠近队友共享体温、减缓理智下滑；独自远离队伍时加快健康衰减
- 死亡后进入旁观模式，支持在商人处招募复活或队友进入下一层后重生（开启 `Permadeath` 时禁用重生）
- MP3 播放器变为外放音响、未植入芯片（Unchipped）按个人生效，支持专用服务器模式（详见 `FOR-NERDS.txt`）

### 控制台命令

房主在游戏内控制台输入以下命令可解锁规则与作弊指令：

```text
rule sv_cheats 1
```

修改房间规则使用 `rule <规则名> <新值>`；房主可通过 `adminpriv <玩家名>` 或设置 `adminpriv-password` 授予客户端远程执行服务端控制台命令的权限。

---

## 源码构建指南

### 云端 CI / 零本地游戏构建（基于 `references/` 脱壳桩）

仓库已在 `references/*.dll` 中提交了由 `BepInEx.AssemblyPublicizer` 提取的精简元数据引用桩（剥离全部方法体 IL 指令与内嵌资源，完整保留类型、字段、方法与特性签名），并在 `libs/dlls/` 中提供全部 7 个运行时依赖库。无需安装 Steam 游戏、无需配置任何 GitHub Secret，克隆仓库即可直接编译打包：

```powershell
# 编译正式版 Fullgame 配置
dotnet build CasualtiesTogether.sln -c ReleaseFullgame

# 编译 Release + Debug 并生成全部 5 个标准发布产物到 build/release-assets
pwsh -NoProfile -File ./scripts/package-release.ps1
```

### 本地游戏联调与更新引用桩

- 若本机装有游戏（可通过环境变量 `CASUALTIESTOGETHER_GAME_PATH`、`CasualtiesTogether.Local.props`、`-p:BaseGamePath=...` 或解决方案同级 `CU_Fullgame` 目录指定），MSBuild 会优先引用真实游戏的 `CasualtiesUnknown_Data/Managed` 目录，并在编译完成后自动将 DLL 复制到游戏的 `BepInEx/plugins/Together/` 目录。
- 也可运行 `python scripts/prepare_workspace.py` 自动拷贝测试目录并配置 BepInEx。
- 当游戏本体更新版本后，可在装有游戏的机器上用 PowerShell 7 重新生成脱壳元数据桩：

```powershell
pwsh -NoProfile -File ./scripts/generate-ref-stubs.ps1 -GamePath "C:\Path\To\Casualties Unknown"
```

---

## 致谢

完整致谢名单可在游戏内模组的「关于（About）」菜单查看。

- [Creature](https://github.com/creaturefeaturelarry) — 上游维护者
- [Krokosha666](https://github.com/Krokosha666) — 原始开发者（已隐退）
- [Dune](https://github.com/Jyan-sei) — 同步与性能优化
- [VtLoader](https://github.com/vtloader) — 缺陷修复与本地化
- [Gary the Cat](https://github.com/Garythecat89)、[Dannad](https://github.com/danxnader)、[Meow](https://github.com/SprightlyHawk8)、[Jimmyking](https://github.com/jimmyking9999999)、[Kidihh](https://github.com/Kidihh)、[Dawn](https://github.com/hehdawn)、[mayzmae](https://github.com/mayzmae)、ItsVoidSK 以及所有参与测试和支持的玩家

## 开源协议

LGPL-2.1，详见 [LICENSE](LICENSE)。
