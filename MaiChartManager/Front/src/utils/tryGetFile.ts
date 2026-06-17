export default async (dir: FileSystemDirectoryHandle, file: string): Promise<File | undefined> => {
  try {
    const handle = await dir.getFileHandle(file);
    return await handle.getFile();
  } catch {
    return undefined;
  }
};
