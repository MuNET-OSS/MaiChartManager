# PROJECT KNOWLEDGE BASE

**Generated:** 2026-04-26
**Commit:** 232b5cb
**Branch:** pr/Starrah/58

## OVERVIEW

MaiChartManager (Sitreamai) — maimai 音游谱面管理工具。C# ASP.NET Core (net10.0-windows) 后端 + WinForms 桌面壳 + Vue 3 前端 SPA，内嵌本地 Kestrel web server。

## STRUCTURE

```
Sitreamai/
├── MaiChartManager/          # 主程序：ASP.NET Core + WinForms 混合桌面应用
│   ├── Controllers/          # REST API 控制器（按领域分 7 个子目录）
│   ├── Front/                # Vue 3 + TypeScript 前端 SPA（pnpm workspace 成员）
│   ├── Models/               # DTO / XML 数据模型（纯数据结构，无业务逻辑）
│   ├── Services/             # 业务逻辑服务
│   ├── Utils/                # 音频/视频/图片转换、CRI 工具
│   ├── Attributes/           # 自定义 Attribute
│   ├── Locale/               # i18n 资源（resx）
│   ├── Libs/                 # 打包进来的第三方库
│   ├── Python/               # ⚠️ 嵌入式 Python 运行时（勿修改）
│   ├── FFMpeg/               # ⚠️ 嵌入式 FFmpeg（勿修改）
│   ├── WannaCRI/             # ⚠️ CRI 音频工具（勿修改）
│   ├── Resources/            # 应用资源
│   └── wwwroot/              # ⚠️ 前端构建产物（勿手动修改）
├── MaiChartManager.CLI/      # 命令行工具 (mcm)，Spectre.Console
├── MaiChartManager.GenClient/ # API client 生成辅助项目
├── AquaMai/                  # [submodule] BepInEx/MelonLoader 游戏 mod
├── MaiLib/                   # [submodule] maimai 谱面解析库
├── MuConvert/                # [submodule] 谱面格式转换工具
├── MuNET-UI/                 # [submodule] @munet/ui 通用 UI 组件库（pnpm workspace 成员）
├── SimaiSharp/               # [submodule] Simai 谱面格式解析
├── SonicAudioTools/          # [submodule] CRI 音频工具库
├── XV2-Tools/                # [submodule] ACB/HCA 音频格式处理
└── Packaging/                # MSIX/Appx 打包脚本和资源
```

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| 添加/修改 API 接口 | `MaiChartManager/Controllers/` | 按领域分目录，见子目录 AGENTS.md |
| 前端 UI 修改 | `MaiChartManager/Front/src/` | Vue 3 + Naive UI + UnoCSS，见子目录 AGENTS.md |
| 音频处理逻辑 | `MaiChartManager/Utils/Audio*.cs`, `CriUtils.cs` | FFmpeg + CRI SDK |
| 谱面导入/解析 | `Controllers/Charts/Services/`, `MaiLib/`, `SimaiSharp/` | MaiLib 和 SimaiSharp 是 submodule |
| 应用启动流程 | `Program.cs` → `AppMain.cs` → `ServerManager.cs` | 单实例 + Kestrel + WebView2 |
| 桌面窗口 | `Browser.cs`, `OobeBrowser.cs`, `Launcher.cs` | Browser=主窗口, OobeBrowser=引导页, Launcher=无WebView2时的旧壳 |
| IAP/授权 | `IapManager.cs`, `OfflineReg.cs` | Windows Store IAP |
| CLI 工具 | `MaiChartManager.CLI/` | `makeusm`, `makeacb`, `makemp4`, `makeab` 命令 |
| AquaMai mod 配置 | `Controllers/Mod/`, `Models/AquaMaiConfigDto.cs` | 管理游戏 mod 的安装和配置 |
| UI 组件库 | `MuNET-UI/` | @munet/ui, defineComponent + JSX，submodule |
| 谱面格式转换 | `MuConvert/` | ANTLR 解析，submodule |
| 打包发布 | `Packaging/Build.ps1` | PowerShell, dotnet publish → makeappx |

## CONVENTIONS

- **目标框架**：主程序 net10.0-windows，AquaMai net472，MaiLib net9.0
- **构建配置**：Debug / Release / Crack（Crack 启用 `CRACK` 宏，Canary 发布用）
- **Nullable + ImplicitUsings**：主程序开启，子项目不一定
- **平台**：仅 x64
- **7 个 git submodule**，各自有独立仓库，勿直接修改 submodule 内代码
- **pnpm workspace**：根 `pnpm-workspace.yaml` 包含 `MaiChartManager/Front` 和 `MuNET-UI`
- **前端 API client** 由 `genClient.ts` 使用 swagger-typescript-api 自动生成，勿手动编辑 `apiGen.ts`
- **后端路由**：`[Route("MaiChartManagerServlet/[action]Api")]` 风格
- **控制器按领域分子目录**：App / AssetDir / Catagory / Charts / Mod / Music / Tools
- **`Catagory` 是 typo**（应为 Category），但已是既定命名，保持一致

## ANTI-PATTERNS

- 不要修改 submodule 内的代码（AquaMai/MaiLib/MuConvert/MuNET-UI/SimaiSharp/SonicAudioTools/XV2-Tools）
- 不要手动编辑 `Front/src/client/apiGen.ts` 和 `aquaMaiVersionConfigApiGen.ts`
- 不要修改 `*.Designer.cs` 文件（WinForms 自动生成）
- 不要手动编辑 `wwwroot/`（前端构建产物，通过 `pnpm build` 生成）
- 不要修改 `Python/`、`FFMpeg/`、`WannaCRI/` 目录（嵌入式运行时）
- 不要在 `Models/` 里写业务逻辑，只放纯数据结构
- 不要用 npm 或 yarn，统一使用 pnpm

## COMMANDS

```bash
# 构建
dotnet build Sitreamai.slnx -c Release

# 前端开发（dev server 端口 5182，代理后端 5181）
cd MaiChartManager/Front && pnpm install && pnpm dev

# 前端构建（输出到 ../wwwroot）
cd MaiChartManager/Front && pnpm build

# 生成 API client（需先启动后端 localhost:5181）
cd MaiChartManager/Front && pnpm genClient

# 打包（Release 或 Canary）
powershell Packaging/Build.ps1
powershell Packaging/Build.ps1 -Mode Canary
```

## NOTES

- 非常规混合架构：WinForms 桌面壳 + ASP.NET Core 本地 API server + Vue 3 SPA + WebView2
- Browser.cs 通过 WebView2 加载前端，注入 `globalThis.backendUrl`，前端通过 localhost API 通信
- Python 和 FFmpeg 是嵌入式运行时，通过 csproj CopyToOutputDirectory 打包
- MaiChartManager.CLI 共享主项目部分代码，但是独立 csproj
- CI 构建分两步：Linux 上 pnpm build 前端 → self-hosted Windows 上 dotnet publish + makeappx
- Canary 发布使用 Crack 配置，产物上传到 Alist 而非 GitHub Release
- 前端 `genClient.ts` 从两个 OpenAPI 源生成：本地后端 swagger + 远端 AquaMai 版本配置
