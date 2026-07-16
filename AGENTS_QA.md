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
