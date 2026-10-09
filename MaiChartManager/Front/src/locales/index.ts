import { createI18n } from 'vue-i18n';
import zh from './zh.yaml';
import zhTW from './zh-TW.yaml';
import en from './en.yaml';
import api from '@/client/api';

// 定义支持的语言列表
export const availableLocales = ['zh', 'zh-TW', 'en'] as const;
export type Locale = typeof availableLocales[number];

// 语言文件
const localeMessages: Record<string, any> = {
  zh,
  'zh-TW': zhTW,
  en,
};

// 根据浏览器语言自动匹配
const detectLocale = (): Locale => {
  const lang = navigator.language;
  if (lang.startsWith('zh')) {
    return lang.includes('TW') || lang.includes('HK') || lang.includes('Hant') ? 'zh-TW' : 'zh';
  }
  return 'en';
};

// 创建 i18n 实例，初始语言先设为 zh，会在应用启动后从后端获取
const i18n = createI18n({
  legacy: false, // 使用 Composition API
  locale: detectLocale(),
  fallbackLocale: 'en',
  messages: localeMessages,
  globalInjection: true, // 全局注入 $t
});

// 切换语言的辅助函数
export const setLocale = async (locale: Locale) => {
  i18n.global.locale.value = locale;
  document.documentElement.lang = locale;

  // 调用后端 API 保存语言设置
  try {
    await api.SetLocale(locale);
  } catch (error) {
    console.error('Failed to save locale to backend:', error);
  }
};

// 从后端加载当前语言
export const loadLocaleFromBackend = async () => {
  try {
    const currentLocaleReq = await api.GetCurrentLocale();
    const currentLocale = currentLocaleReq.data;
    if (availableLocales.includes(currentLocale as Locale)) {
      i18n.global.locale.value = currentLocale as Locale;
      document.documentElement.lang = currentLocale;
    }
  } catch (error) {
    console.error('Failed to load locale from backend:', error);
  }
};

// 导出全局 t 函数
export const t = i18n.global.t;

// 导出 locale，可以直接访问当前语言
export const locale = i18n.global.locale;

export default i18n;

