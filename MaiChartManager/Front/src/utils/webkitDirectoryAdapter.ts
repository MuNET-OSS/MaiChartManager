import { ImportDirectory, ImportFileHandle } from "@/utils/importDirectory";

// WebKitGTK 没有 window.showDirectoryPicker，只能用 <input type=file webkitdirectory multiple>，
// 拿到的是扁平的 FileList，每个 File 带 webkitRelativePath（形如 "顶层目录/子目录/文件名"）。
// 这里把扁平列表重建成目录树，并包装成实现 ImportDirectory 接口的对象，
// 让现有导入流程（startProcess / prepareFolder / tryGetFile）无需改动即可消费。

// 内部目录树节点
interface DirNode {
  name: string;
  files: Map<string, File>;        // 直接子文件：文件名 -> File
  dirs: Map<string, DirNode>;      // 直接子目录：目录名 -> 节点
}

// 文件句柄适配器
class FileHandleAdapter implements ImportFileHandle {
  readonly kind = 'file' as const;
  constructor(readonly name: string, private readonly file: File) {}
  async getFile(): Promise<File> {
    return this.file;
  }
}

// 目录句柄适配器
class DirectoryAdapter implements ImportDirectory {
  readonly kind = 'directory' as const;
  constructor(private readonly node: DirNode) {}

  get name(): string {
    return this.node.name;
  }

  async getFileHandle(name: string): Promise<ImportFileHandle> {
    const file = this.node.files.get(name);
    if (!file) {
      // 对齐 File System Access API 的语义：找不到就抛 NotFoundError，由 tryGetFile 的 try/catch 兜住
      const err = new Error(`未找到文件: ${name}`);
      err.name = 'NotFoundError';
      throw err;
    }
    return new FileHandleAdapter(name, file);
  }

  async *values(): AsyncIterableIterator<ImportFileHandle | ImportDirectory> {
    for (const [name, file] of this.node.files) {
      yield new FileHandleAdapter(name, file);
    }
    for (const child of this.node.dirs.values()) {
      yield new DirectoryAdapter(child);
    }
  }
}

// 创建空节点
const makeNode = (name: string): DirNode => ({ name, files: new Map(), dirs: new Map() });

// 把扁平 FileList（带 webkitRelativePath）重建为目录树，返回根目录适配器。
// 选目录时浏览器会把所选目录名作为 webkitRelativePath 的第一段，因此根节点名取该第一段。
export function buildDirectoryFromFileList(files: FileList | File[]): ImportDirectory {
  const list = Array.from(files);
  // 用一个虚拟根承载，最终若只有单一顶层目录则把它作为返回根
  const virtualRoot = makeNode('');

  for (const file of list) {
    // webkitRelativePath 形如 "topDir/sub/file.txt"；个别实现可能为空，退回用文件名
    const relPath = (file as any).webkitRelativePath as string || file.name;
    const parts = relPath.split('/').filter(Boolean);
    if (parts.length === 0) continue;

    const fileName = parts[parts.length - 1];
    const dirParts = parts.slice(0, -1);

    let cursor = virtualRoot;
    for (const part of dirParts) {
      let next = cursor.dirs.get(part);
      if (!next) {
        next = makeNode(part);
        cursor.dirs.set(part, next);
      }
      cursor = next;
    }
    cursor.files.set(fileName, file);
  }

  // 通常 webkitdirectory 选择后只有一个顶层目录，直接返回它，
  // 这样根目录名 = 用户所选目录名，与 showDirectoryPicker 的行为一致。
  if (virtualRoot.files.size === 0 && virtualRoot.dirs.size === 1) {
    const only = virtualRoot.dirs.values().next().value as DirNode;
    return new DirectoryAdapter(only);
  }

  return new DirectoryAdapter(virtualRoot);
}
