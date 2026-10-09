import { isWebView } from "@/client/api";

/**
 * WebView2 环境下等待 backendUrl 注入后再继续
 * 非 WebView2 环境（远程浏览器）直接 resolve
 */
export const ensureBackendUrl = () => new Promise<void>(resolve => {
  if (!isWebView) {
    resolve();
    return;
  }
  if (globalThis.backendUrl) {
    resolve();
    return;
  }
  const interval = setInterval(() => {
    if (globalThis.backendUrl) {
      clearInterval(interval);
      resolve();
    }
  }, 50);
});
