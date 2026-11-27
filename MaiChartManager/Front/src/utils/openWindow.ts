import { isPhotino } from '@/client/api';

export interface OpenWindowOptions {
  /** 站内 hash 路由，例如 /fancy-tool */
  path?: string;
  /** 同域静态页面路径，例如 /touch-test/index.html */
  urlPath?: string;
  query?: Record<string, string>;
  title: string;
  width: number;
  height: number;
}

/**
 * 用独立窗口打开一个站内页面。
 * WebView2 宿主会走 NewWindowRequested 开一个新的 webview 窗口；
 * WebKitGTK(Photino) 不支持 window.open 弹新窗口，改为通知宿主开内置子窗口。
 */
export const openWindow = ({ path, urlPath, query, title, width, height }: OpenWindowOptions) => {
  const url = urlPath ? new URL(urlPath, location.origin) : new URL(location.href);
  if (urlPath) {
    if (query) {
      url.search = new URLSearchParams(query).toString();
    }
  } else {
    const hashPath = path ?? '/';
    url.hash = query ? `${hashPath}?${new URLSearchParams(query).toString()}` : hashPath;
  }

  if (isPhotino) {
    (window as any).external.sendMessage(JSON.stringify({
      type: 'open-window',
      url: url.toString(),
      title,
      width,
      height,
    }));
    return;
  }

  const left = (screen.width - width) / 2;
  const top = (screen.height - height) / 2;
  window.open(url, '_blank', `width=${width},height=${height},left=${left},top=${top},menubar=no,toolbar=no,location=no,status=no`);
};
