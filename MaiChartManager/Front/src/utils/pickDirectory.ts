import { ImportDirectory } from "@/utils/importDirectory";
import { buildDirectoryFromFileList } from "@/utils/webkitDirectoryAdapter";

// 通用选目录：
// - 若浏览器支持 window.showDirectoryPicker（Chromium / WebView2 / 远程 Chrome）→ 返回真实 handle，
//   行为与原先完全一致；
// - 否则（WebKitGTK / Photino）→ 用 <input type=file webkitdirectory multiple> 选目录，
//   把扁平 FileList 交给适配器，返回实现 ImportDirectory 接口的「句柄」。
// 用户取消时返回 null（与原先 showDirectoryPicker 抛 AbortError 的处理在 startProcess 里都被 catch）。
export function pickDirectory(
  options?: { id?: string; startIn?: string },
): Promise<ImportDirectory> {
  // 真实 File System Access API
  if (typeof window.showDirectoryPicker === 'function') {
    // 真实 FileSystemDirectoryHandle 在结构上满足 ImportDirectory
    return window.showDirectoryPicker(options as any) as unknown as Promise<ImportDirectory>;
  }

  // WebKitGTK 回退：<input webkitdirectory>
  return new Promise<ImportDirectory>((resolve, reject) => {
    const input = document.createElement('input');
    input.type = 'file';
    // webkitdirectory 不是标准 TS 属性，这里用 setAttribute 兼容
    input.setAttribute('webkitdirectory', '');
    input.multiple = true;
    input.style.display = 'none';
    document.body.appendChild(input);

    let settled = false;
    const cleanup = () => input.remove();

    input.addEventListener('change', () => {
      settled = true;
      const files = input.files;
      if (!files || files.length === 0) {
        cleanup();
        // 没选到任何文件，按取消处理：抛 AbortError，与 showDirectoryPicker 取消语义一致
        const err = new Error('用户取消选择目录');
        err.name = 'AbortError';
        reject(err);
        return;
      }
      const dir = buildDirectoryFromFileList(files);
      cleanup();
      resolve(dir);
    });

    // 取消时多数内核不触发 change；用 window focus 兜底判定取消
    window.addEventListener('focus', () => setTimeout(() => {
      if (!settled) {
        cleanup();
        const err = new Error('用户取消选择目录');
        err.name = 'AbortError';
        reject(err);
      }
    }, 500), { once: true });

    input.click();
  });
}
