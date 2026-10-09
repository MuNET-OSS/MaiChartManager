// 注意：这里刻意用 .ts 而不是 .d.ts —— tsconfig 开了 skipLibCheck（三方 .d.ts 自身有问题，关不掉），
// 而 skipLibCheck 会连带跳过我们自己 .d.ts 里的语义错误，typings 写错会静默失效。
// 用 .ts 后这些声明照常参与类型检查。
/// <reference types="vite/client" />

// @fontsource 的包入口（如 @fontsource/nerko-one）只解析到包内的 css 文件，
// 包本身没有为这些入口提供类型声明，这里按副作用 import 处理，声明为空模块即可
declare module '@fontsource/*' {
}

// vite-svg-loader 把 .svg 编译成 Vue 组件，而 vite/client 把 *.svg 声明成 string（URL），
// 两者语义不符；这里给 loader 的 ?component 形式单独声明（vite/client 没有这个 pattern，不会冲突）
declare module '*.svg?component' {
  import type { DefineComponent, HTMLAttributes } from 'vue';
  const component: DefineComponent<HTMLAttributes>;
  export default component;
}
