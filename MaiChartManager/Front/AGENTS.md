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
