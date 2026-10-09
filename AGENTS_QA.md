# QA 经验

## Photino 静态资源

- `Front` 的 `pnpm build` 只更新 `MaiChartManager/wwwroot`；LinuxDebug 运行时的 ContentRoot 是 `bin/LinuxDebug/net10.0/linux-x64`。
- 浏览器 QA 前还要执行 `dotnet build MaiChartManager/MaiChartManager.csproj -c LinuxDebug`，否则后端仍会提供旧的 `bin/.../wwwroot`。
- 可用首页引用的 `assets/index-*.js` 哈希确认运行中的服务是否已加载最新前端。

## 受控 Radio

- `@munet/ui` 的 Radio 会在 click 中调用 `preventDefault()`；更新 Vue 状态后，浏览器可能在事件结束时回滚原生 `checked`，造成内容已切换但圆点仍显示旧状态。
- 关键模式选择应同时断言业务内容和 `input.isChecked()`；需要受控状态时使用原生 radio 的 `checked` + `onChange`，或先修复共享组件。
- Radio 有 250ms 颜色过渡，截图应在状态断言通过并等待过渡结束后采集。

## 弹窗滚动

- 限高滚动区不要让纵向 flex 子项默认收缩，否则 Select 等控件会被压成细条；使用普通块级流，或显式禁止子项收缩。
- 响应式弹窗应同时断言弹窗矩形、标题和操作按钮都位于视口内，不能只检查文档没有横向滚动。

## 无后端前端动效 QA

- 只验证前端动效且后端未启动时，初始化错误弹窗会遮住真实页面；可以仅在 QA 浏览器会话中关闭弹窗和根节点的 modal 状态，再采集页面证据，不要修改产品错误处理。
- 动效证据矩阵送审前要逐项检查文件存在且非空，尤其是反向切换的 settled 帧；运行时 transform 数据不能替代缺失的视觉帧。

## Linux 下验证 Windows 私有逻辑

- 临时测试程序直接项目引用主项目时，默认配置会把主项目解析成 Windows TFM；先构建 `LinuxDebugBackend`，再引用其输出 DLL。
- 引用主 Web 程序集的临时测试项目还需声明 `Microsoft.AspNetCore.App` FrameworkReference，否则运行时无法加载 MVC 程序集。

## 前端接入 tsc 类型检查（2026-10-09）

- 没有 `skipLibCheck` 时全项目 18 条报错里有 9 条来自三方 `.d.ts`（vueuse 引 `Bluetooth*`、naive-ui 引没装的 `katex`、vue-i18n 引 vue 里不存在的 `GenericComponentInstance`、`@f3ve/vue-markdown-it` 引 `markdown-it`），真正的源码错只有 9 条。**先开 `skipLibCheck` 把噪音降到 0 再看剩什么**，否则会把三方问题误判成自己的工作。
- 生成文件 `src/client/apiGen.ts` / `aquaMaiVersionConfigApiGen.ts` 头部的 `// @ts-nocheck` 来自 swagger-typescript-api 硬编码的 `FILE_PREFIX`，`generateApi` 没有开关能关掉。实测剥掉后这两份文件（约 3400 行）零报错，所以在 `genClient.ts` 里做了生成后剥离，并写成「找不到就抛错退出」，避免库升级后静默失效。
- `src/icons/*.svg` 当组件用时类型是错的：`vite/client` 声明 `*.svg` 是 `string`，而 vite-svg-loader 把它编译成组件。**不要**自己在 `env.d.ts` 里再声明一遍 `*.svg`——两份同名 ambient module 会合并，产生重复的 `export default`（`tsc` 报 TS2300），而 `skipLibCheck: true` 恰好把这条报错也吞掉，表现为 JSX 属性莫名不认，极难定位。正解是用 loader 支持的 `?component` 后缀：`vite/client` 没有这个 pattern，可以安全地单独声明。
- 判断一个 `@ts-ignore` 是不是承重不能靠读代码。临时把所有 `@ts-ignore` 一次失效再跑一次 `tsc` 最快；注意 **TS 认的是「以 `@ts-ignore` 开头的行注释」**，把 `@ts-ignore` 改成 `@ts-ignore-defused` 仍然是生效的指令，要让行号不变地把指令挪到注释中间（例如 `// DISABLED @ts-ignore`）。
- **最大的一条**：`skipLibCheck: true` 不只跳过 node_modules，它会跳过程序里**所有** `.d.ts`，包括我们自己写的 `env.d.ts` / `shims-vue.d.ts`。实测在 `.d.ts` 里写 `declare const x: ThisTypeDoesNotExist;` → `tsc` exit 0 无任何输出；同一行放进 `.ts` → TS2304。这次真的被这个坑咬到过：第一版把 `*.svg` 声明进 `env.d.ts`，与 `vite/client` 的同名 ambient module 合并产生重复 `export default`（TS2300），恰好被 skipLibCheck 吞掉，表现为 JSX 属性莫名不认、改 tsconfig 都没用。解法是把环境声明从 `.d.ts` 改名成 `.ts`（没有任何代码 import 它们，纯声明文件，改后缀不影响运行时），这样既保留 skipLibCheck 挡三方噪音，自家的声明又照常被检查。
- `Button` 这类组件补事件 props 有个坑：**声明过的 prop 会从 `$attrs` 里被摘掉，不再走 fallthrough**，所以补 props 时必须同时在组件内部的根元素上显式绑定，否则调用方的 handler 静默失效（不报错、也不生效）。用 `@munet/ui` 的 Button + `onMouseleave` 做过正反对照：只声明不绑定 → 事件不触发；补上显式绑定 → 触发。
- 验证「事件到底有没有绑上」，可以在 `MaiChartManager/Front` 下临时放一个 `verify-btn/index.html` + `main.tsx`，用项目自己的 `vite --port 5199 --strictPort` 起 dev server（能直接解析 `@munet/ui` 的 TSX 源码），再用 `chromium --headless --no-sandbox --disable-dev-shm-usage --virtual-time-budget=10000 --dump-dom <url>` 把页面内 `dispatchEvent` 的结果写进 `<pre>` 读出来；`--dump-dom` 会等模块执行完，`--virtual-time-budget` 足够。改完记得删临时目录并 kill 掉 dev server（`pkill -f vite` 会连带杀掉自己的 shell，用 PID kill 更稳）。

## 子模块 bump 之后必须重新同步 pnpm lockfile（2026-10-09）

- 只改 `MuNET-UI` 子模块指针、不跑 `pnpm install`，会让根 `pnpm-lock.yaml` 与 `MuNET-UI/package.json` 失同步。GitHub Actions 里 `CI=true` 会让 pnpm 的 `frozen-lockfile` 默认为 true，于是 `pnpm install` 直接 `ERR_PNPM_OUTDATED_LOCKFILE` 失败——**现有的 Build Canary 也会一起挂**，不只是新加的 CI。
- 只跑 `pnpm install --no-frozen-lockfile` 还不够：pnpm 会保留旧 lockfile 里已存在的解析结果，于是 Front 的 vue 停在 3.5.32，而 MuNET-UI 的 vue 按自己的 `^3.5.38` 升到 3.5.43 —— **workspace 里出现两份 vue**。因为 `@munet/ui` 是被 Front 以源码形式消费的，tsc 会同时看到两份 Vue 类型，症状是报错里出现 `VNodeRef & VNodeRef` 这种「类型和自己交叉」的怪东西（实测 `AquaMaiConfigurator.tsx` 的 `ref={searchRef}` 报 TS2322），改代码是修不好的。看到 `X & X` 这种交叉，先怀疑依赖有重复副本，别去改业务代码。
- 正解是让 workspace 内共享包只保留一个版本：`pnpm update <pkg> --filter <成员>` 把 Front 的 specifier 抬到与 MuNET-UI **解析出同一个版本**（注意不是"specifier 数值相等"——实际是让两者的 semver 范围相交、解析器收敛到同一版）。本次抬到 vue 3.5.43 / vite 8.3.4 / unocss 66.10.5 / sass 1.105.1 / typescript 6.0.3 / @vueuse/core 14.4.0 / @vitejs/plugin-vue 6.0.9 / @vitejs/plugin-vue-jsx 5.1.6。
- `pnpm update -r` 会连子模块里的 `MuNET-UI/package.json` 一起改，用 `--filter mcm-frontend` 可以只动 Front。
- 光靠 `pnpm update` 还不够：pnpm 会保留 lockfile 里已有的解析结果，所以像 `@vitejs/plugin-vue-jsx` 这种两边范围本来不相交的包会留下两份。收尾要跑一次 `pnpm dedupe`，然后逐包断言 `packages:` 段里只剩一个版本（`@babel/*` 之类被顺带清掉的重复也有 29 个）。
- 之后加 CI 时又发现两个覆盖盲区，都不是这次改动引入的，但要知道：① `pnpm typecheck` 只跑 `tsc`，不跑 `vite build`，而 `build.yml` 只在 push 到 main 触发、不在 PR 触发，所以 PR 可以类型全绿却把打包弄挂，要等合并后才由 Build Canary 暴露；② `pnpm -r run typecheck` 只会跑**定义了 typecheck 脚本**的成员，新增 workspace 成员时漏写脚本会静默跳过（日志里的 `Scope: N of M workspace projects` 不反映谁有脚本），没有任何内置开关能让它报错。
- `.vue` 的类型检查盲区是整个 workspace 的：Front 1 个 + MuNET-UI 2 个，共 3 个 `.vue` 组件没人检查（plain tsc 不解析 `.vue`，MuNET-UI tsconfig 里 `src/**/*.vue` 那条 include 是 no-op）。
- 判断「是不是真修好了」不能只看 lockfile：要同时确认 `install --frozen-lockfile` 通过、`pnpm typecheck` 通过、以及 `pnpm build` 仍然通过。本次 vite 从 8.0.8 跳到 8.3.4（打包器换成 rolldown），实测 build 12.1s 正常完成、只有 chunk 体积等既有警告。

