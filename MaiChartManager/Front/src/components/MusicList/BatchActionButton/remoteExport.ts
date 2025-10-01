import { STEP } from "@/components/MusicList/BatchActionButton/index";
import { currentProcessItem, progressAll, progressCurrent } from "@/components/MusicList/BatchActionButton/ProgressDisplay";
import { MusicXmlWithABJacket } from "@/client/apiGen";
import { ZipReader } from "@zip.js/zip.js";
import getSubDirFile from "@/utils/getSubDirFile";
import { OPTIONS } from "@/components/MusicList/BatchActionButton/ChooseAction";
import { useNotification } from "naive-ui";
import { getUrl } from "@/client/api";

export default async (setStep: (step: STEP) => void, musicList: MusicXmlWithABJacket[], action: OPTIONS, notify: ReturnType<typeof useNotification>) => {
  let folderHandle: FileSystemDirectoryHandle;
  try {
    folderHandle = await window.showDirectoryPicker({
      id: 'copyToSaveDir',
      mode: 'readwrite'
    });
  } catch (e) {
    console.log(e)
    return;
  }

  progressCurrent.value = 0;
  progressAll.value = musicList.length;
  currentProcessItem.value = '';

  setStep(STEP.ProgressDisplay);

  for (let i = 0; i < musicList.length; i++) {
    const music = musicList[i];

    progressCurrent.value = i;
    currentProcessItem.value = music.name!;

    let url = '';
    switch (action) {
      case OPTIONS.CreateNewOpt:
        url = `ExportOptApi/${music.assetDir}/${music.id}`;
        break;
      case OPTIONS.CreateNewOptCompatible:
        url = `ExportOptApi/${music.assetDir}/${music.id}?removeEvents=true`;
        break;
      case OPTIONS.CreateNewOptMa2_103:
        url = `ExportOptApi/${music.assetDir}/${music.id}?removeEvents=true&legacyFormat=true`;
        break;
      case OPTIONS.ConvertToMaidata:
        url = `ExportAsMaidataApi/${music.assetDir}/${music.id}`;
        break;
      case OPTIONS.ConvertToMaidataIgnoreVideo:
        url = `ExportAsMaidataApi/${music.assetDir}/${music.id}?ignoreVideo=true`;
        break;
    }
    url = getUrl(url);
    const zip = await fetch(url);
    const zipReader = new ZipReader(zip.body!);
    const entries = zipReader.getEntriesGenerator();
    for await (const entry of entries) {
      try {
        console.log(entry.filename);
        if (entry.filename.endsWith('/')) {
          continue;
        }
        let filename = entry.filename;
        if (action === OPTIONS.ConvertToMaidata || action === OPTIONS.ConvertToMaidataIgnoreVideo) {
          filename = `${music.id}/${filename}`;
        }
        const fileHandle = await getSubDirFile(folderHandle, filename);
        const writable = await fileHandle.createWritable();
        await entry.getData!(writable);
      } catch (e) {
        console.error(e);
        notify.error({
          title: '导出失败',
          content: music.name!,
        })
      }
    }
  }

  setStep(STEP.None);
}
