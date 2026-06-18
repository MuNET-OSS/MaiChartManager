// 导入流程用的「目录句柄」抽象接口。
// 真实的浏览器 FileSystemDirectoryHandle（Chromium / WebView2 / 远程 Chrome）和
// WebKitGTK 下基于 <input webkitdirectory> 的适配器都要实现它，
// 这样 startProcess / prepareFolder / tryGetFile 就不用写死 FileSystemDirectoryHandle，
// 避免在不支持 File System Access API 的内核上类型/运行时出错。

// 文件项：能拿到底层 File
export interface ImportFileHandle {
  readonly kind: 'file';
  readonly name: string;
  getFile(): Promise<File>;
}

// 目录项：能按名取文件、能迭代子项
export interface ImportDirectory {
  readonly kind: 'directory';
  readonly name: string;
  // 取目录下某个文件的句柄；不存在时按 File System Access API 的语义抛错（由 tryGetFile 兜住）
  getFileHandle(name: string): Promise<ImportFileHandle>;
  // 迭代子项（文件或子目录），与 FileSystemDirectoryHandle.values() 形状一致
  values(): AsyncIterableIterator<ImportFileHandle | ImportDirectory>;
}
