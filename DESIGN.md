# MaiChartManager Design System

## 1. Atmosphere & Identity

面向高频谱面管理的安静工作台。信息密度优先，颜色只表达难度、状态和当前操作；界面特征是随主题色变化的轻量表面层级，以及始终可扫描的表单与列表。

## 2. Color

颜色由 `@munet/ui` 主题和 UnoCSS 语义色提供，不在业务组件中建立第二套色板。

| 角色 | Token | 用途 |
|---|---|---|
| 正文 | `--text-color` | 标题、正文 |
| 强调 | `--link-color` | 主操作、选中态、焦点 |
| 主题色相 | `--hue` | 动态亮色主题的表面与边框 |
| 信息/警告/错误 | UnoCSS `blue` / `yellow` / `red` | 导入消息与状态反馈 |
| 难度 | `LEVEL_COLOR` | Basic 至 Re:Master 的固定识别色 |

业务界面优先使用主题变量、组件 variant 和语义色，避免新增硬编码颜色。

## 3. Typography

- 正文：Noto Sans SC，回退到系统无衬线字体。
- 拉丁文字与数字：Quicksand，回退到系统无衬线字体。
- 正文基准为 16px；辅助标签使用 14px；紧凑元数据可使用 12px。
- 字重以 400 和 500/600 为主；字距固定为 0。

## 4. Spacing & Layout

- 基础间距单位为 4px，业务组件使用 UnoCSS 的 `gap-1`、`gap-2`、`gap-3`、`gap-4` 等既有步进。
- 表单采用纵向 stack，相关选项采用可换行 cluster；窄视口时必须退化为单列且不能横向溢出。
- 桌面壳负责主滚动，Modal 内的长列表显式拥有自己的纵向滚动区域。
- 固定格式控件必须有稳定宽度或 `minmax` 约束，动态文案不能推动相邻操作按钮。

## 5. Components

### Form Controls

- **结构**：标签与 `@munet/ui` 的 `TextInput`、`NumberInput`、`Select`、`CheckBox` 或 `Radio`。
- **状态**：default、hover、focus、disabled、invalid 由组件库主题负责。
- **布局**：字段使用 stack，二元选择使用 radio cluster，多项选择使用 select。
- **可访问性**：保留原生 input/select 语义，标签必须描述其值而不是布局。

### Modal Workflow

- **结构**：标题、可滚动正文、固定 actions。
- **状态**：checking、warning、processing、success、fatal。
- **布局**：正文 stack；操作按钮等宽，主操作在输入不完整时 disabled。
- **动效**：只沿用组件库的开合与状态切换，不增加装饰动画。

### Difficulty Tabs

- **结构**：难度标签、启用状态、对应编辑面板。
- **变体**：标准五难度；宴会场仅显示 Basic；双人宴谱在 Basic 面板内分 L/R 操作区。
- **状态**：selected、disabled chart、problem。
- **可访问性**：标签文本不能只依赖颜色；左右谱面必须同时显示文字标识。

## 6. Motion & Interaction

- 微交互 100-150ms，面板或标签切换 200-300ms。
- 仅动画化 `transform`、`opacity` 和现有主题允许的颜色过渡。
- 动效只用于操作反馈和状态切换，并遵守 `prefers-reduced-motion`。
- 主 tab 按导航顺序切换：侧边导航使用上下位移，窄屏底部导航使用左右位移；前进和后退方向互为镜像。

## 7. Depth & Surface

采用混合策略：普通内容依靠主题色的 tonal shift，输入框和 Modal 使用组件库既有边框与表面。业务组件不新增阴影层级，也不嵌套装饰性卡片。

不要用 border，一般不画分割线。不要给 button 设置透明度、边框、动画之类自定义样式，因为全局 css 里已经有了

## 8. Accessibility Constraints & Accepted Debt

- 目标为 WCAG 2.2 AA；正文对比度不低于 4.5:1，大字不低于 3:1。
- 所有交互必须可键盘到达并有可见焦点。
- 中文标签保持完整语义短语，375px 宽度下不允许单字孤行或控件覆盖。
