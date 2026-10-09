# FRONTEND KNOWLEDGE BASE

## OVERVIEW

Vue 3 + TypeScript 前端 SPA，通过 localhost API 与 ASP.NET Core 后端通信，内嵌于 WebView2 桌面窗口。

## STRUCTURE

```
Front/
├── src/
│   ├── client/         # API 客户端层
│   │   ├── api.ts                          # 手写 API 封装（baseUrl 判断、WebView2 适配）
│   │   ├── apiGen.ts                       # ⚠️ 自动生成，禁止手动编辑
│   │   └── aquaMaiVersionConfigApiGen.ts   # ⚠️ 自动生成，禁止手动编辑
│   ├── components/     # 可复用组件：DragDropDispatcher/, Sidebar/, Splash/, VersionInfo/
│   ├── hooks/          # Vue composables（如 useAsync）
│   ├── icons/          # 图标组件
│   ├── locales/        # i18n 翻译文件
│   ├── plugins/        # Vue 插件（posthog, sentry, i18n）
│   ├── store/          # 状态管理：refs.ts（核心全局状态）+ appUpdate.ts（更新/更新日志）
│   ├── utils/          # 工具函数
│   ├── views/          # 页面视图（按域划分）
│   │   ├── BatchAction/
│   │   ├── Charts/
│   │   ├── GenreVersionManager/
│   │   ├── ModManager/
│   │   ├── Settings/
│   │   ├── Tools/
│   │   └── Oobe/
│   └── assets/         # 静态资源
├── genClient.ts        # API client 代码生成器（swagger-typescript-api）
├── vite.config.ts      # Vite 配置（输出到 ../wwwroot, dev 代理 5181）
├── uno.config.ts       # UnoCSS 配置
└── tsconfig.json       # TypeScript 配置（strict，@ → ./src，jsxImportSource: vue）
```

## WHERE TO LOOK

| 任务 | 位置 |
|------|------|
| 调用后端 API | `src/client/api.ts`（手写封装），默认导出 `apiClient.maiChartManagerServlet` |
| 添加新页面 | `src/views/` 对应子目录，路由在 `src/router.ts` |
| 全局状态 | `src/store/refs.ts`（核心状态、数据拉取方法）、`src/store/appUpdate.ts`（更新相关）|
| 复用组件 | `src/components/` |
| 国际化文本 | `src/locales/` |
| 重新生成 API client | `pnpm genClient`（需先启动后端 localhost:5181）|
| 前端入口 | `src/main.ts` → `App.vue` → router/i18n/posthog/sentry |
| 类型检查 | 仓库根目录 `pnpm typecheck`，见下方「类型检查」 |

## DATA FLOW

1. 页面/store 调用 `api.ts`（手写封装层）
2. `api.ts` 调用 `apiGen.ts` 生成的客户端方法
3. 请求到后端 controller → 读写 XML/DTO → 返回
4. 前端通过 `refs.ts` 中的 `updateMusicList()` / `updateAll()` 刷新状态
5. 错误由 `globalCapture()` 统一捕获 → Sentry + PostHog 上报

WebView2 集成：
- `location.hostname === 'mcm.invalid'` 判断是否在 WebView2 内
- 后端通过 `PostWebMessageAsString(url)` 推送 backendUrl
- 前端监听 `chrome.webview` message 并动态更新 `apiClient.baseUrl`

## 类型检查

```bash
pnpm typecheck                        # 仓库根目录：pnpm -r 依次检查 MaiChartManager/Front 与 MuNET-UI
pnpm --filter mcm-frontend typecheck  # 只检查前端
pnpm --filter @munet/ui typecheck     # 只检查 UI 包
```

前端拆成两个 tsconfig，缺一不可：

| 配置 | 范围 | 说明 |
|------|------|------|
| `tsconfig.json` | `src/**` | 主应用。开启 `skipLibCheck`：vueuse 引 `Bluetooth*`、naive-ui 引没装的 `katex`、vue-i18n 引 vue 里不存在的 `GenericComponentInstance` 等，都是三方 `.d.ts` 自身的问题，关掉会淹掉真正的源码错误 |
| `src/env.ts`、`src/shims-vue.ts` | 环境声明 | **刻意用 `.ts` 而不是 `.d.ts`**，原因见下 |
| `tsconfig.node.json` | `Front/*.ts` | `vite.config.ts` / `uno.config.ts` / `genClient.ts` 等构建脚本，用 `@types/node` |

已知盲区：`tsc` 不解析 `.vue`。全 workspace 有 3 个 `.vue` 落在盲区里：Front 的 `src/components/TransitionVertical.vue`，以及 MuNET-UI 的 `TransitionVertical.vue` / `Range.vue`（它 tsconfig 里的 `src/**/*.vue` 那条 include 对 plain tsc 是 no-op）。要覆盖得上 `vue-tsc`。

修类型错误时禁止用 `any` / `as any` / `@ts-ignore` / `@ts-expect-error` 消音，也不要靠改 tsconfig 放宽检查——要改到类型真正成立。

两处容易踩的坑：

- `src/client/apiGen.ts` 与 `aquaMaiVersionConfigApiGen.ts` 的 `// @ts-nocheck` 由 `genClient.ts` 在生成后剥掉（swagger-typescript-api 的 `FILE_PREFIX` 里硬编码了它，没有开关可关）。这两份生成代码必须始终能过类型检查，不要手工加回去。
- 环境声明写成 `env.ts` / `shims-vue.ts` 而不是 `.d.ts`：`skipLibCheck` 会连带跳过**我们自己** `.d.ts` 里的语义错误（写错类型名不报错、静默退化成 any），实测 `declare const x: ThisTypeDoesNotExist;` 放 `.d.ts` 里 exit 0、放 `.ts` 里才是 TS2304。用 `.ts` 后这些声明照常参与检查，且不影响运行时（没有任何代码 import 它们）。
- `src/icons/*.svg` 当组件用时必须写 `?component` 后缀：`vite/client` 把 `*.svg` 声明成 URL 字符串，与 vite-svg-loader 编译成组件的真实行为不符；`*.svg?component` 才是我们自己在 `env.d.ts` 里声明的组件类型。不要试图覆盖 `*.svg`——会和 `vite/client` 的声明合并成重复的 `export default`。

## CONVENTIONS

- 包管理器：pnpm（统一，不用 npm/yarn）
- UI 组件库：Naive UI（部分页面迁移到 @munet/ui）
- 样式方案：UnoCSS（原子化 CSS）
- 路径别名：`@` → `./src`
- 状态管理极简，核心全局 ref 集中在 `src/store/refs.ts`
- API client 由 `genClient.ts` 从两个 OpenAPI 源生成：本地 swagger + 远端 AquaMai 版本配置
- 代码注释使用中文
- `.editorconfig`：2 空格、LF、UTF-8

## ANTI-PATTERNS

- 禁止手动编辑 `src/client/apiGen.ts` 和 `aquaMaiVersionConfigApiGen.ts`，修改会在下次生成时被覆盖
- 需要新增 API 调用时，在后端添加控制器后运行 `pnpm genClient` 重新生成，再在 `api.ts` 中封装
- 不要用 npm 或 yarn，统一使用 pnpm
