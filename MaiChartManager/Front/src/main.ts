import '@unocss/reset/tailwind-compat.css';
import 'animate.css';
import 'virtual:uno.css';
import '@fontsource/noto-sans-sc';
import '@fontsource/quicksand';
import { createApp } from 'vue';
import App from './App';
import posthog from "@/plugins/posthog";
import sentry from "@/plugins/sentry";
import i18n from '@/locales';
import router from '@/router';
import './global.sass';
import { initThemeDefaults, selectedThemeName, UIThemes } from '@munet/ui';


// Listen for backendUrl injection from WebView2 host
if ((window as any).chrome?.webview) {
  (window as any).chrome.webview.addEventListener('message', (e: any) => {
    globalThis.backendUrl = e.data;
    import('./client/api').then(m => {
      m.apiClient.baseUrl = e.data;
    });
  });
}
initThemeDefaults({ hue: 353 });
selectedThemeName.value = UIThemes.DynamicLight;


createApp(App)
  .use(router)
  .use(i18n as any)
  .use(posthog)
  .use(sentry)
  .mount('#app');
