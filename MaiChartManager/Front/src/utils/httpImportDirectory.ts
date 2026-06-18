import { ImportDirectory, ImportFileHandle } from "@/utils/importDirectory";
import { getUrl } from "@/client/api";

// 后端返回的子项结构，与 ImportBrowseController.ImportDirEntry 对应
interface BackendEntry {
  name: string;
  path: string;
  isDirectory: boolean;
}

// 取路径最后一段作为显示名；同时按 '/' 和 '\\' 切分，兼容 Windows 风格路径
function basename(p: string): string {
  const parts = p.split(/[/\\]/).filter(Boolean);
  return parts.length ? parts[parts.length - 1] : p;
}

// 读文件：
// - 只传 path 时，后端直接读该完整路径（用于 values() 里已知绝对路径的文件项）
// - 传 path(目录) + name 时，后端 Path.Combine(path, name)（用于 getFileHandle，避免前端跨平台拼路径）
async function readFile(path: string, displayName: string, name?: string): Promise<File> {
  let url = getUrl('ReadImportFileApi') + '?path=' + encodeURIComponent(path);
  if (name !== undefined) {
    url += '&name=' + encodeURIComponent(name);
  }
  const res = await fetch(url);
  if (!res.ok) throw new Error('文件不存在: ' + displayName);
  return new File([await res.blob()], displayName);
}

// 基于后端 3 个接口实现的 ImportDirectory 适配器。
// 供 WebKitGTK / Photino 等没有 File System Access API 的本地宿主使用。
// absPath：目录绝对路径；name：显示名（默认取 absPath 最后一段）
export function httpImportDirectory(absPath: string, name?: string): ImportDirectory {
  return {
    kind: 'directory',
    name: name ?? basename(absPath),

    // 按名取目录下文件的句柄。不在这里拼路径，交给后端 Path.Combine（传 absPath + name）。
    // 不存在时 getFile 会抛错，由 tryGetFile 兜住。
    async getFileHandle(fileName: string): Promise<ImportFileHandle> {
      return {
        kind: 'file',
        name: fileName,
        getFile: () => readFile(absPath, fileName, fileName),
      };
    },

    // 迭代目录直接子项：目录递归构造适配器，文件构造文件句柄
    async *values(): AsyncIterableIterator<ImportFileHandle | ImportDirectory> {
      const res = await fetch(getUrl('ListImportDirApi') + '?path=' + encodeURIComponent(absPath));
      if (!res.ok) return;
      const entries: BackendEntry[] = await res.json();
      for (const child of entries) {
        if (child.isDirectory) {
          yield httpImportDirectory(child.path, child.name);
        } else {
          yield {
            kind: 'file',
            name: child.name,
            // 已知子项绝对路径，只传 path 即可
            getFile: () => readFile(child.path, child.name),
          } satisfies ImportFileHandle;
        }
      }
    },
  };
}
