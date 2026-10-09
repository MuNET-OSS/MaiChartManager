// 同 env.ts：用 .ts 而不是 .d.ts，否则 skipLibCheck 会把这里的错误一起吞掉
declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<{}, {}, any>
  export default component
}
