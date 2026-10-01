import { STEP } from "@/views/BatchAction/index";
import {
  currentProcessItem,
  exportFailedItems,
  exportFinished,
  exportSuccessCount,
  progressCurrent,
  resetExportProgress,
} from "@/views/BatchAction/ProgressDisplay";
import { MaidataSubdirMode, MusicXmlWithABJacket } from "@/client/apiGen";
import { BlobWriter, ZipReader } from "@zip.js/zip.js";
import getSubDirFile from "@/utils/getSubDirFile";
import { OPTIONS } from "@/views/BatchAction/ChooseAction";
import { getUrl } from "@/client/api";
import { addVersionList, genreList } from "@/store/refs";
import { t } from "@/locales";
import { sanitizeFsSegment } from "@/utils/sanitizeFsName";

export default async (
  setStep: (step: STEP) => void,
  musicList: MusicXmlWithABJacket[],
  action: OPTIONS,
  dirOption: MaidataSubdirMode,
) => {
  let folderHandle: FileSystemDirectoryHandle;
  try {
    folderHandle = await window.showDirectoryPicker({
      id: "copyToSaveDir",
      mode: "readwrite",
    });
  } catch (e) {
    console.log(e);
    return;
  }

  const getMaidataExportDir = (music: MusicXmlWithABJacket) => {
    let parentDir = "";
    switch (dirOption) {
      case MaidataSubdirMode.Genre:
        parentDir =
          genreList.value.find((genre) => genre.id === music.genreId)
            ?.genreName || t("music.list.unknown");
        break;
      case MaidataSubdirMode.Version:
        parentDir =
          addVersionList.value.find(
            (version) => version.id === music.addVersionId,
          )?.genreName || t("music.list.unknown");
        break;
    }

    if (parentDir) {
      parentDir = sanitizeFsSegment(parentDir, t("music.list.unknown"));
    }

    if (action === OPTIONS.ConvertToMaidataById) {
      return parentDir ? `${parentDir}/${music.id}` : `${music.id}`;
    }

    const suffix = music.id! > 1e4 && music.id! < 2e4 ? " [DX]" : "";
    const safeTitle = sanitizeFsSegment(
      music.name || t("music.list.unknown"),
      t("music.list.unknown"),
    );
    const targetDir = `${safeTitle}${suffix}`;
    return parentDir ? `${parentDir}/${targetDir}` : targetDir;
  };

  resetExportProgress(musicList.length);
  setStep(STEP.ProgressDisplay);

  const recordFailure = (music: MusicXmlWithABJacket) => {
    exportFailedItems.value.push({
      id: music.id!,
      name: music.name || t("music.list.unknown"),
    });
  };

  const getExportUrl = (music: MusicXmlWithABJacket) => {
    switch (action) {
      case OPTIONS.CreateNewOpt:
        return `ExportOptApi/${music.assetDir}/${music.id}`;
      case OPTIONS.CreateNewOptCompatible:
        return `ExportOptApi/${music.assetDir}/${music.id}?removeEvents=true`;
      case OPTIONS.CreateNewOptMa2_103:
        return `ExportOptApi/${music.assetDir}/${music.id}?removeEvents=true&legacyFormat=true`;
      case OPTIONS.ConvertToMaidata:
        return `ExportAsMaidataApi/${music.assetDir}/${music.id}`;
      case OPTIONS.ConvertToMaidataIgnoreVideo:
        return `ExportAsMaidataApi/${music.assetDir}/${music.id}?ignoreVideo=true`;
      case OPTIONS.ConvertToMaidataById:
        return `ExportAsMaidataApi/${music.assetDir}/${music.id}`;
      default:
        throw new Error(`Unsupported export action: ${action}`);
    }
  };

  const getMaxParallelExports = () => {
    const cpuThreads = Math.max(1, navigator.hardwareConcurrency || 4);

    switch (action) {
      case OPTIONS.ConvertToMaidata:
        return Math.max(1, Math.floor(cpuThreads / 4));
      case OPTIONS.ConvertToMaidataIgnoreVideo:
        return Math.max(1, Math.floor(cpuThreads / 3));
      case OPTIONS.ConvertToMaidataById:
        return Math.max(1, Math.floor(cpuThreads / 4));
      default:
        return Math.max(1, Math.floor(cpuThreads / 2));
    }
  };

  const exportOne = async (music: MusicXmlWithABJacket): Promise<boolean> => {
    const musicName = music.name || t("music.list.unknown");
    currentProcessItem.value = musicName;

    const maidataRootDir =
      action === OPTIONS.ConvertToMaidata ||
        action === OPTIONS.ConvertToMaidataIgnoreVideo ||
        action === OPTIONS.ConvertToMaidataById
        ? getMaidataExportDir(music)
        : "";

    try {
      const response = await fetch(getUrl(getExportUrl(music)));
      if (!response.ok || !response.body) {
        throw new Error(
          `Export request failed: ${response.status} ${response.statusText}`,
        );
      }

      const zipReader = new ZipReader(response.body);
      try {
        let hasEntryError = false;
        const entries = zipReader.getEntriesGenerator();
        for await (const entry of entries) {
          try {
            if (entry.filename.endsWith("/")) {
              continue;
            }

            if (!('getData' in entry)) {
              continue;
            }

            const filename = maidataRootDir
              ? `${maidataRootDir}/${entry.filename}`
              : entry.filename;

            const fileHandle = await getSubDirFile(folderHandle, filename);
            const writable = await fileHandle.createWritable();
            try {
              const blob = await entry.getData(new BlobWriter());
              await writable.write(blob);
            } finally {
              await writable.close();
            }
          } catch (e) {
            if (e instanceof TypeError && String(e.message).includes('Name is not allowed')) {
              // Chromium blocks certain file extensions (e.g. .manifest, .ini, .dll)
              // via File System Access API. Skip silently.
              continue;
            }
            hasEntryError = true;
            console.error("Failed to export zip entry", {
              musicName,
              sourceFile: entry.filename,
              error: e,
            });
          }
        }

        if (hasEntryError) {
          recordFailure(music);
          return false;
        }
        return true;
      } finally {
        await zipReader.close();
      }
    } catch (e) {
      console.error(e);
      recordFailure(music);
      return false;
    }
  };

  let nextIndex = 0;
  let completedCount = 0;
  let successCount = 0;
  const workerCount = Math.min(musicList.length, getMaxParallelExports());

  const worker = async () => {
    while (true) {
      const currentIndex = nextIndex++;
      if (currentIndex >= musicList.length) {
        return;
      }

      const ok = await exportOne(musicList[currentIndex]);
      if (ok) {
        successCount += 1;
        exportSuccessCount.value = successCount;
      }
      completedCount += 1;
      progressCurrent.value = completedCount;
    }
  };

  try {
    await Promise.all(Array.from({ length: workerCount }, () => worker()));
  }
  catch (e) {
    console.error(e);
  }
  finally {
    currentProcessItem.value = "";
    exportSuccessCount.value = successCount;
    exportFinished.value = true;
  }
};
