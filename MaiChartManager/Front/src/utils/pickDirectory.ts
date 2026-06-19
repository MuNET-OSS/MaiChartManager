import { ImportDirectory } from "@/utils/importDirectory";
import { httpImportDirectory } from "@/utils/httpImportDirectory";
import { getUrl, isLocalHost, isPhotino } from "@/client/api";

// 抛一个 AbortError，与 showDirectoryPicker 取消时的语义一致（startProcess 里会 catch 掉）
function abort(message = '用户取消选择目录'): never {
  const err = new Error(message);
  err.name = 'AbortError';
  throw err;
}

// 走后端：弹原生选文件夹对话框，再用 httpImportDirectory 适配器通过 HTTP 提供目录内容。
async function pickViaBackend(): Promise<ImportDirectory> {
  const pickUrl = getUrl('PickImportFolderApi');
  console.log('[imp] pickViaBackend origin=' + location.origin + ' href=' + location.href + ' pickUrl=' + pickUrl);
  const res = await fetch(pickUrl);
  console.log('[imp] PickImportFolder responded ok=' + res.ok + ' status=' + res.status + ' ct=' + res.headers.get('content-type'));
  if (!res.ok) abort('选择目录失败');
  // 后端返回 JSON：选中的绝对路径字符串，取消时为 null
  const text = await res.text();
  console.log('[imp] PickImportFolder raw body=' + JSON.stringify(text));
  const path: string | null = text ? JSON.parse(text) : null;
  console.log('[imp] picked path=' + JSON.stringify(path));
  if (!path) abort();
  return httpImportDirectory(path);
}

// 通用选目录：
// - Photino(WebKitGTK)：一律走后端原生对话框。WebKitGTK 即使暴露了 window.showDirectoryPicker，
//   其实现也有问题（调用后访问 handle 会抛 "The string did not match the expected pattern"），
//   所以**不能**用 typeof 检测来决定，必须在 showDirectoryPicker 之前优先判 isPhotino。
// - 其它有真实 File System Access API 的环境（WebView2 / 远程 Chrome）：用真实 handle，行为不变。
// - 其余本地宿主但无可用 picker：兜底走后端。
// - 都不满足（远程浏览器且无 File System Access API）：不支持，按取消处理。
export async function pickDirectory(
  options?: { id?: string; startIn?: string },
): Promise<ImportDirectory> {
  // Photino/WebKitGTK：优先后端，绕开有问题的 webkit showDirectoryPicker
  if (isPhotino) {
    return pickViaBackend();
  }

  // 真实 File System Access API（WebView2 / Chromium / 远程 Chrome）
  if (typeof window.showDirectoryPicker === 'function') {
    // 真实 FileSystemDirectoryHandle 在结构上满足 ImportDirectory
    return window.showDirectoryPicker(options as any) as unknown as Promise<ImportDirectory>;
  }

  // 其它本地桌面宿主（无 showDirectoryPicker）：后端原生选目录
  if (isLocalHost) {
    return pickViaBackend();
  }

  // 远程浏览器且无 File System Access API：不支持，按取消处理
  abort('当前环境不支持选择目录');
}
