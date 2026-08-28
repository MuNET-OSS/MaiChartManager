import { defineComponent, ref } from "vue";
import { Button } from "@munet/ui";
import SelectFileTypeTip from "./SelectFileTypeTip";
import { LicenseStatus, MessageLevel } from "@/client/apiGen";
import CheckingModal from "./CheckingModal";
import api, { getUrl } from "@/client/api";
import { globalCapture, selectedADir, selectMusicId, updateMusicList, version as appVersion } from "@/store/refs";
import { appSettings } from "@/store/settings";
import ErrorDisplayIdInput from "./ErrorDisplayIdInput";
import ImportStepDisplay from "./ImportStepDisplay";
import { useStorage } from "@vueuse/core";
import { captureException } from "@sentry/vue";
import { fetchEventSource } from "@microsoft/fetch-event-source";
import { handleSseOpen } from "@/utils/sseOpen";
import { defaultSavedOptions, defaultTempOptions, dummyMeta, IMPORT_STEP, ImportChartMessageEx, ImportMeta, STEP } from "./types";
import getNextUnusedMusicId from "@/utils/getNextUnusedMusicId";
import { useI18n } from 'vue-i18n';
import { createImportFatal, createVideoConvertWarning, getCaptureTarget, isAbortError } from "./importErrors";
import tryGetFile from "@/utils/tryGetFile";
import { ImportDirectory } from "@/utils/importDirectory";
import { pickDirectory } from "@/utils/pickDirectory";

export let startProcess = (_dir?: ImportDirectory | ImportDirectory[]) => { }

export default defineComponent({
  setup() {
    const savedOptions = useStorage('importMusicOptions', defaultSavedOptions, undefined, { mergeDefaults: true });
    const tempOptions = ref({ ...defaultTempOptions });
    const step = ref(STEP.none);
    const errors = ref<ImportChartMessageEx[]>([]);
    const modalResolve = ref<(qwq?: any) => any>(() => {
    });
    const modalReject = ref<Function>();
    const meta = ref<ImportMeta[]>([]);
    const currentProcessing = ref<ImportMeta>(dummyMeta);
    const currentMovieProgress = ref(0);
    const { t } = useI18n();

    const closeModal = () => {
      step.value = STEP.none;
      modalReject.value && modalReject.value({ name: 'AbortError' });
    }

    const prepareFolder = async (dir: ImportDirectory, id: number) => {
      let reject = false;

      const maidata = await tryGetFile(dir, 'maidata.txt');
      if (!maidata) {
        reject = true;
        errors.value.push({ level: MessageLevel.Fatal, message: t('chart.import.error.noMaidata'), name: dir.name });
      }
      const track = await tryGetFile(dir, 'track.mp3') || await tryGetFile(dir, 'track.wav') || await tryGetFile(dir, 'track.ogg');
      if (!track) {
        reject = true;
        errors.value.push({ level: MessageLevel.Fatal, message: t('chart.import.error.noAudio'), name: dir.name });
      }
      const bg = await tryGetFile(dir, 'bg.jpg') || await tryGetFile(dir, 'bg.png') || await tryGetFile(dir, 'bg.jpeg');
      if (!bg) {
        errors.value.push({ level: MessageLevel.Warning, message: t('chart.import.error.noBackground'), name: dir.name });
      }
      let movie = await tryGetFile(dir, 'pv.mp4') || await tryGetFile(dir, 'mv.mp4') || await tryGetFile(dir, 'bg.mp4');
      if (movie && appVersion.value?.license !== LicenseStatus.Active) {
        movie = undefined;
        errors.value.push({ level: MessageLevel.Warning, message: t('chart.import.error.convertPaidFeature'), name: dir.name, isPaid: true });
      }

      let first = 0, chartPaddings, name = dir.name, isDx = false, previewTime = undefined;
      let maidataLevels: number[] = [];
      if (maidata) {
        const checkRet = (await api.ImportChartCheck({ file: maidata })).data;
        reject = reject || !checkRet.accept;
        errors.value.push(...(checkRet.errors || []).map(it => ({ ...it, name: dir.name })));
        first = checkRet.first!;
        chartPaddings = checkRet.chartPaddings!;
        errors.value.push({ first, chartPaddings, name: dir.name });
        // 为了本地的错误和远程的错误都显示本地的名称，这里在修改 name
        name = checkRet.title!;
        if (checkRet.isDx) id += 1e4;
        isDx = checkRet.isDx!;
        if (checkRet.previewTime) previewTime = checkRet.previewTime
        maidataLevels = checkRet.maidataLevels ?? [];
      }

      if (!reject) {
        const utageMapping = maidataLevels.length >= 2 ? {
          isDoublePlayer: false,
          basicLevel: maidataLevels[0],
          leftLevel: maidataLevels[0],
          rightLevel: maidataLevels[1],
        } : undefined;
        meta.value.push({
          id, maidata, bg, track, chartPaddings, name, first, movie, isDx, previewTime,
          maidataLevels, utageMapping,
          importStep: IMPORT_STEP.start,
        })
      }
      return !reject;
    }

    const uploadMovie = (id: number, movie: File, offset: number) => new Promise<void>((resolve, reject) => {
      currentMovieProgress.value = 0;
      const body = new FormData();
      body.append('padding', offset.toString());
      body.append('file', movie);
      body.append('file', movie);
      const controller = new AbortController();
      fetchEventSource(getUrl(`SetMovieApi/${selectedADir.value}/${id}`), {
        signal: controller.signal,
        method: 'PUT',
        body,
        onopen: handleSseOpen,
        onerror(e) {
          reject(e);
          controller.abort();
          throw new Error("disable retry onerror");
        },
        onclose() {
          reject(new Error("EventSource Close"));
          controller.abort();
          throw new Error("disable retry onclose");
        },
        openWhenHidden: true,
        onmessage: (e) => {
          switch (e.event) {
            case 'Progress':
              currentMovieProgress.value = parseInt(e.data);
              break;
            case 'Success':
              resolve();
              controller.abort();
              currentMovieProgress.value = 0;
              break;
            case 'Error':
              reject(new Error(e.data));
              controller.abort();
              currentMovieProgress.value = 0;
              break;
          }
        }
      });
    })

    const processMusic = async (music: ImportMeta) => {
      try {
        music.importStep = IMPORT_STEP.create;

        const createRet = (await api.AddMusic(music.id, selectedADir.value)).data;
        if (createRet) throw new Error(createRet);

        music.importStep = IMPORT_STEP.chart;
        const res = (await api.ImportChart({
          file: music.maidata,
          id: music.id,
          ignoreLevelNum: tempOptions.value.ignoreLevel,
          genreId: savedOptions.value.genreId,
          addVersionId: savedOptions.value.addVersionId,
          version: savedOptions.value.version,
          shift: tempOptions.value.shift,
          ...(music.utageMapping ? {
            utageDoublePlayer: music.utageMapping.isDoublePlayer,
            utageBasicLevel: music.utageMapping.basicLevel,
            utageLeftLevel: music.utageMapping.leftLevel,
            utageRightLevel: music.utageMapping.rightLevel,
          } : {}),
          debug: import.meta.env.DEV,
          assetDir: selectedADir.value,
        })).data;

        errors.value.push(...res.errors!.map(it => ({ ...it, name: music.name })));
        if (res.fatal) {
          try {
            await api.DeleteMusic(music.id, selectedADir.value);
          } catch {
          }
          return;
        }

        music.importStep = IMPORT_STEP.music;
        let chartPadding = music.chartPaddings?.[tempOptions.value.shift]!;
        // 参见Services/MaidataImportService.cs:CalcChartPadding 中的注释，
        // 音频上应该应用的延迟audioPadding = 谱面上应用的延迟chartPadding - &first
        let audioPadding = chartPadding - music.first;

        await api.SetAudio(music.id, selectedADir.value, { file: music.track, padding: audioPadding, ignoreGapless: !!tempOptions.value.ignoreGapless });
        if (music.previewTime) {
          // 因为&demo_seek和&demo_len都是相对于原始音源的，所以这里的时间必须要扣掉上面SetAudio时应用的padding。
          // 根据SetAudioApi的定义，padding为正表示歌曲前面被加了空白，因此原本的预览段在新的裁剪后的音频中的位置也要相应后移；padding为负反之。
          // 因此，直接给startTime和endTime都加上audioPadding即可。
          music.previewTime.startTime! += audioPadding;
          music.previewTime.endTime! += audioPadding
          await api.SetAudioPreview(music.id, selectedADir.value, music.previewTime);
        }

        if (music.movie && !tempOptions.value.disableBga) {
          currentMovieProgress.value = 0;
          music.importStep = IMPORT_STEP.movie;
          try {
            await uploadMovie(music.id, music.movie, audioPadding);
          } catch (e) {
            errors.value.push(createVideoConvertWarning(e, music.name, t('chart.import.error.videoConvertFailed'), t('error.unknown')));
          }
        }

        music.importStep = IMPORT_STEP.jacket;
        if (music.bg) await api.SetMusicJacket(music.id, selectedADir.value, { file: music.bg });

        music.importStep = IMPORT_STEP.finish;
      } catch (e) {
        console.log(music, e)
        captureException(getCaptureTarget(e), {
          tags: {
            context: t('chart.import.error.importError'),
            step: music.importStep,
          }
        })
        errors.value.push(createImportFatal(e, music.name));
        if (music.importStep !== IMPORT_STEP.create) {
          // 如果是在创建乐曲这步就挂了，说明乐曲XML没有创建成功，则不需要删除乐曲。
          // 否则，在ID冲突的情况下，会把原本的乐曲给删除掉，见 https://github.com/MuNET-OSS/MaiChartManager/issues/34
          try {
            await api.DeleteMusic(music.id, selectedADir.value);
          } catch {
          }
        }
      }
    }

    startProcess = async (dir?: ImportDirectory | ImportDirectory[]) => {
      let id = getNextUnusedMusicId();
      const usedIds = [] as number[];
      errors.value = [];
      tempOptions.value = { ...defaultTempOptions, ignoreLevel: appSettings.value.ignoreLevel, disableBga: appSettings.value.disableBga };
      step.value = STEP.selectFile;
      meta.value = [];
      currentProcessing.value = dummyMeta;
      try {
        if (!dir) {
          // pickDirectory：支持 showDirectoryPicker 时返回真实 handle，否则用 webkitdirectory 适配器
          dir = await pickDirectory({
            id: 'maidata-dir',
            startIn: 'downloads',
          });
        }
        step.value = STEP.checking;

        // 不再依赖 instanceof FileSystemDirectoryHandle（适配器不是它）。
        // 统一逻辑：单个目录句柄时，先看根目录有没有 maidata.txt，有就当作单首谱面导入；
        // 没有（或传入的是数组）就遍历子目录。真实 handle 与适配器都走得通。
        if (!Array.isArray(dir) && await tryGetFile(dir, 'maidata.txt')) {
          await prepareFolder(dir, id);
        } else {
          // 数组（拖拽多个）时直接用这些句柄；单目录时遍历其子项。两种都只取目录项。
          const entries: (ImportDirectory)[] = [];
          if (Array.isArray(dir)) {
            for (const entry of dir) {
              if (entry.kind === 'directory') entries.push(entry);
            }
          } else {
            for await (const entry of dir.values()) {
              if (entry.kind === 'directory') entries.push(entry);
            }
          }
          for (const entry of entries) {
            if (await prepareFolder(entry, id)) {
              usedIds.push(id);
              id = getNextUnusedMusicId(usedIds);
            }
          }
        }

        if (!meta.value.length && !errors.value.length)
          throw new Error(t('chart.import.error.notFoundImportable'));

        step.value = STEP.showWarning;

        await new Promise((resolve, reject) => {
          modalResolve.value = resolve;
          modalReject.value = reject;
        });

        step.value = STEP.importing;
        errors.value = [];

        for (const music of meta.value) {
          currentProcessing.value = music;
          // 自带 try 了
          await processMusic(music);
        }

        await updateMusicList();
        selectMusicId.value = meta.value[0].id;

        if (errors.value.length) {
          step.value = STEP.showResultError
        }
      } catch (e) {
        if (isAbortError(e)) return
        // WebKit 的 console 直接 log 异常对象时无法正确转文本，这里显式打印字符串便于定位
        const err = e as any;
        console.log('[imp] FAILED step=' + step.value + ' message=' + String(err?.message ?? err) + ' stack=' + String(err?.stack ?? '(无栈)'));
        globalCapture(e, t('chart.import.error.importErrorGlobal'))
      } finally {
        if (step.value !== STEP.showResultError)
          step.value = STEP.none
      }
    }

    return () => <Button onClick={() => startProcess()} variant="secondary">
      {t('chart.import.title')}
      <SelectFileTypeTip show={step.value === STEP.selectFile} closeModal={closeModal} />
      <CheckingModal title={t('chart.import.checkingTitle')} show={step.value === STEP.checking} closeModal={closeModal} />
      <ErrorDisplayIdInput show={step.value === STEP.showWarning} closeModal={closeModal} proceed={modalResolve.value!} meta={meta.value} errors={errors.value}
        savedOptions={savedOptions.value} tempOptions={tempOptions.value} />
      <ImportStepDisplay show={step.value === STEP.importing} closeModal={closeModal} current={currentProcessing.value} movieProgress={currentMovieProgress.value} />
      <ErrorDisplayIdInput show={step.value === STEP.showResultError} closeModal={closeModal} proceed={() => {
      }} meta={[]} savedOptions={savedOptions.value} tempOptions={tempOptions.value} errors={errors.value} />
    </Button>;
  }
})
