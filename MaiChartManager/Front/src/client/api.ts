import { Api } from "@/client/apiGen";
import { Api as AquaMaiVersionConfigApi } from "@/client/aquaMaiVersionConfigApiGen";

declare global {
  const backendUrl: string | undefined;
}
// 在 WebView2 环境中，域名是 mcm.invalid，backendUrl 会通过 PostWebMessageAsString 注入
// 在远程浏览器（export 模式）中，直接用相对路径（当前 origin）
export const isWebView = location.hostname === 'mcm.invalid';
// 本地桌面宿主：Windows WebView2(mcm.invalid) 或 Photino/本机浏览器(loopback)。
// 这些情况下后端在同机，文件/目录操作走后端原生对话框，而不是浏览器的 File System Access API
//（WebKitGTK 不支持后者）。远程浏览器（局域网 IP 访问 export 模式）则为 false。
export const isLocalHost = isWebView || ['127.0.0.1', 'localhost', '[::1]'].includes(location.hostname);
const getBaseUrl = () => (globalThis as any).backendUrl ?? (isWebView ? undefined : '');

export const apiClient = new Api({
  // @ts-ignore
  baseUrl: getBaseUrl(),
  baseApiParams: {
    headers: {
      accept: 'application/json',
    },
  },
})

export default apiClient.maiChartManagerServlet

export const aquaMaiVersionConfig = new AquaMaiVersionConfigApi({
  baseUrl: 'https://aquamai-version-config.mumur.net',
  baseApiParams: {
    headers: {
      accept: 'application/json',
    },
  },
}).api

export const getUrl = (suffix: string) => {
  // @ts-ignore
  return `${globalThis.backendUrl ?? ''}/MaiChartManagerServlet/${suffix}`;
}

// 本地宿主（Photino/WebKitGTK、WebView2）专用的 maidata 导出：
// 后端弹原生选目录对话框，并把每首歌的 maidata 写进所选目录（每首一个子目录）。
// 这里手写 fetch 而不是用生成的 apiGen，是因为本环境无法连接后端重新生成 client。
// 接口：POST /MaiChartManagerServlet/RequestExportMaidataApi
//   body: { music: [{ id, assetDir }], ignoreVideo?: boolean }
export const requestExportMaidata = async (
  music: { id: number; assetDir: string }[],
  ignoreVideo = false,
) => {
  const res = await fetch(getUrl('RequestExportMaidataApi'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ music, ignoreVideo }),
  });
  if (!res.ok) {
    throw new Error(`RequestExportMaidata 失败: ${res.status} ${res.statusText}`);
  }
  return res;
}
