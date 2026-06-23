import { ImportDirectory } from "@/utils/importDirectory";

// 取目录下指定文件，找不到返回 undefined。
// 参数类型用 ImportDirectory 而非写死 FileSystemDirectoryHandle，
// 这样真实 handle 和 WebKitGTK 适配器都能传进来。
export default async (dir: ImportDirectory, file: string): Promise<File | undefined> => {
  try {
    const handle = await dir.getFileHandle(file);
    return await handle.getFile();
  } catch {
    return undefined;
  }
};
