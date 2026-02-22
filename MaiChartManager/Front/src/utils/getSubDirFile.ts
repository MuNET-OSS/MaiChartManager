import { sanitizeFsSegment } from "@/utils/sanitizeFsName";

export default async (
  folderHandle: FileSystemDirectoryHandle,
  fileName: string,
) => {
  const pathParts = fileName
    .split("/")
    .filter((part) => part.length > 0 && part !== "." && part !== "..")
    .map((part) => sanitizeFsSegment(part));

  if (pathParts.length === 0) {
    throw new Error("Invalid file path");
  }

  let dirHandle = folderHandle;
  for (let i = 0; i < pathParts.length - 1; i++) {
    dirHandle = await dirHandle.getDirectoryHandle(pathParts[i], {
      create: true,
    });
  }
  return await dirHandle.getFileHandle(pathParts[pathParts.length - 1], {
    create: true,
  });
};
