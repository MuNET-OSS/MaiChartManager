import { ImportDirectory } from "@/utils/importDirectory";
import { httpImportDirectory } from "@/utils/httpImportDirectory";
import { getUrl, isLocalHost } from "@/client/api";

// 抛一个 AbortError，与 showDirectoryPicker 取消时的语义一致（startProcess 里会 catch 掉）
function abort(message = '用户取消选择目录'): never {
  const err = new Error(message);
  err.name = 'AbortError';
  throw err;
}

// 通用选目录：
// - 浏览器支持 window.showDirectoryPicker（Chromium / WebView2 / 远程 Chrome）→ 返回真实 handle，
//   行为与原先完全一致；
// - 否则是本地桌面宿主（isLocalHost，含 Photino/WebKitGTK 的 loopback 与 WebView2 的 mcm.invalid）→
//   走后端：弹原生选文件夹对话框，再用 httpImportDirectory 适配器通过 HTTP 提供目录内容。
//   （WebKitGTK 没有 showDirectoryPicker，<input webkitdirectory> 又只能选单文件，所以一律走后端。）
// - 其余情况（远程浏览器且不支持 File System Access API）→ 不支持，按取消处理。
// 用户取消时抛 AbortError。
export async function pickDirectory(
  options?: { id?: string; startIn?: string },
): Promise<ImportDirectory> {
  // 真实 File System Access API
  if (typeof window.showDirectoryPicker === 'function') {
    // 真实 FileSystemDirectoryHandle 在结构上满足 ImportDirectory
    return window.showDirectoryPicker(options as any) as unknown as Promise<ImportDirectory>;
  }

  // 本地桌面宿主：后端原生选目录 + HTTP 提供目录内容
  if (isLocalHost) {
    const res = await fetch(getUrl('PickImportFolderApi'));
    if (!res.ok) abort('选择目录失败');
    // 后端返回 JSON：选中的绝对路径字符串，取消时为 null
    const path: string | null = await res.json();
    if (!path) abort();
    return httpImportDirectory(path);
  }

  // 远程浏览器且无 File System Access API：不支持，按取消处理
  abort('当前环境不支持选择目录');
}
