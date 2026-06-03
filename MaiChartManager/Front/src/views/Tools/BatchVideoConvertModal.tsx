import api, { getUrl } from '@/client/api';
import { Button, Modal, Progress, Radio, addToast } from '@munet/ui';
import { computed, defineComponent, reactive, ref } from 'vue';
import { LicenseStatus } from '@/client/apiGen';
import { globalCapture, showNeedPurchaseDialog, version } from '@/store/refs';
import { fetchEventSource } from '@microsoft/fetch-event-source';
import { handleSseOpen } from '@/utils/sseOpen';
import { useI18n } from 'vue-i18n';

enum STEP {
  None,
  Configure,
  Progress,
  Done,
}

enum Direction {
  UsmToMp4 = 'UsmToMp4',
  Mp4ToUsm = 'Mp4ToUsm',
}

enum FinishKind {
  Success,
  Cancelled,
}

interface ProgressPayload {
  processed: number;
  total: number;
  fileProgress: number;
  fileName: string;
  failed: number;
}

interface BatchErrorPayload {
  code: string;
  message: string;
}

interface BatchError extends Error {
  code?: string;
}

const createBatchError = (data: string): BatchError => {
  try {
    const payload = JSON.parse(data) as Partial<BatchErrorPayload>;
    if (payload.code && payload.message) {
      const error = new Error(payload.message) as BatchError;
      error.code = payload.code;
      return error;
    }
  } catch {
    // fallback to legacy plain-text error data
  }
  return new Error(data);
};

export default defineComponent({
  setup(_, { expose }) {
    const { t } = useI18n();
    const step = ref(STEP.None);
    const direction = ref<Direction>(Direction.UsmToMp4);
    const folderPath = ref('');
    const picking = ref(false);

    const state = reactive({
      completed: 0,
      total: 0,
      fileProgress: 0,
      fileName: '',
      failed: 0,
    });

    const fileErrors = ref<string[]>([]);
    const finishKind = ref<FinishKind>(FinishKind.Success);
    const finishSummary = ref('');
    const cancelling = ref(false);

    let controller: AbortController | null = null;

    const overallPercent = computed(() =>
      state.total === 0 ? 0 : Math.floor((state.completed / state.total) * 100),
    );

    const friendlyErrorLocaleKeys: Record<string, string> = {
      NEED_LICENSE: 'tools.batchPv.needLicense',
      NO_FILES: 'tools.batchPv.noFiles',
      FOLDER_NOT_FOUND: 'tools.batchPv.folderNotFound',
    };

    const show = computed({
      get: () => step.value !== STEP.None,
      set(v: boolean) {
        if (!v && step.value !== STEP.Progress) {
          step.value = STEP.None;
        }
      },
    });

    const resetProgressState = () => {
      state.completed = 0;
      state.total = 0;
      state.fileProgress = 0;
      state.fileName = '';
      state.failed = 0;
      fileErrors.value = [];
      finishSummary.value = '';
      cancelling.value = false;
    };

    const start = async () => {
      if (!folderPath.value) return;
      resetProgressState();
      step.value = STEP.Progress;

      controller = new AbortController();
      const url = `${getUrl('BatchConvertPvToolApi')}?folderPath=${encodeURIComponent(folderPath.value)}&direction=${direction.value}`;

      let succeeded = false;
      let cancelled = false;
      try {
        await new Promise<void>((resolve, reject) => {
          fetchEventSource(url, {
            signal: controller!.signal,
            method: 'POST',
            onopen: handleSseOpen,
            onerror(e) {
              reject(e);
              controller?.abort();
              throw new Error('disable retry onerror');
            },
            onclose() {
              if (succeeded || cancelled) {
                resolve();
              } else {
                reject(new Error('EventSource Close'));
              }
              throw new Error('disable retry onclose');
            },
            openWhenHidden: true,
            onmessage: (e) => {
              switch (e.event) {
                case 'Progress': {
                  try {
                    const payload = JSON.parse(e.data) as ProgressPayload;
                    state.completed = payload.processed;
                    state.total = payload.total;
                    state.fileProgress = payload.fileProgress;
                    state.fileName = payload.fileName;
                    state.failed = payload.failed;
                  } catch {
                    // ignore malformed payload
                  }
                  break;
                }
                case 'FileError': {
                  fileErrors.value.push(e.data);
                  break;
                }
                case 'Success': {
                  succeeded = true;
                  finishKind.value = FinishKind.Success;
                  finishSummary.value = e.data;
                  controller?.abort();
                  resolve();
                  break;
                }
                case 'Cancelled': {
                  cancelled = true;
                  finishKind.value = FinishKind.Cancelled;
                  finishSummary.value = e.data;
                  controller?.abort();
                  resolve();
                  break;
                }
                case 'Error': {
                  controller?.abort();
                  reject(createBatchError(e.data));
                  break;
                }
              }
            },
          });
        });

        step.value = STEP.Done;
      } catch (e: any) {
        if (e?.name === 'AbortError') {
          // 用户点了取消：HTTP 已断开，后端的 Cancelled 帧大概率收不到，所以前端自己进入 Done
          step.value = STEP.Done;
          if (!finishSummary.value) {
            finishKind.value = FinishKind.Cancelled;
            finishSummary.value = `${state.completed}/${state.total}`;
          }
          return;
        }
        // 已知的友好错误（无文件 / 需要赞助 / 文件夹不存在）：toast 提示并回到 Configure，不上报
        const friendlyErrorLocaleKey = friendlyErrorLocaleKeys[e?.code as string];
        if (friendlyErrorLocaleKey) {
          addToast({ message: t(friendlyErrorLocaleKey), type: 'warning' });
          step.value = STEP.Configure;
          return;
        }
        console.log(e);
        globalCapture(e, t('tools.batchPv.error'));
        step.value = STEP.None;
      } finally {
        controller = null;
      }
    };

    const cancel = () => {
      if (cancelling.value) return;
      cancelling.value = true;
      controller?.abort();
    };

    const closeDone = () => {
      step.value = STEP.None;
    };

    const pickFolder = async () => {
      if (picking.value) return false;
      picking.value = true;
      try {
        const res = await api.OpenFolderDialog();
        if (res.data) {
          folderPath.value = res.data;
          return true;
        }
        return false;
      } catch (e) {
        console.log(e);
        globalCapture(e, t('tools.batchPv.error'));
        return false;
      } finally {
        picking.value = false;
      }
    };

    const trigger = async () => {
      if (version.value?.license !== LicenseStatus.Active) {
        showNeedPurchaseDialog.value = true;
        return;
      }
      const selected = await pickFolder();
      if (!selected) return;
      resetProgressState();
      direction.value = Direction.UsmToMp4;
      step.value = STEP.Configure;
    };

    expose({ trigger });

    const renderConfigure = () => (
      <div class="flex flex-col gap-3">
        <div class="font-medium mt-2">{t('tools.batchPv.direction')}</div>
        <Radio k={Direction.UsmToMp4} v-model:value={direction.value}>
          <div class="flex flex-col">
            <span>{t('tools.batchPv.directionUsmToMp4')}</span>
            <span class="text-xs op-60">{t('tools.batchPv.directionUsmToMp4Hint')}</span>
          </div>
        </Radio>
        <Radio k={Direction.Mp4ToUsm} v-model:value={direction.value}>
          <div class="flex flex-col">
            <span>{t('tools.batchPv.directionMp4ToUsm')}</span>
            <span class="text-xs op-60">{t('tools.batchPv.directionMp4ToUsmHint')}</span>
          </div>
        </Radio>
        <div class="flex justify-end gap-2 mt-2">
          <Button onClick={() => (step.value = STEP.None)}>{t('tools.batchPv.cancel')}</Button>
          <Button variant="primary" onClick={start} disabled={!folderPath.value}>{t('tools.batchPv.start')}</Button>
        </div>
      </div>
    );

    const renderProgress = () => (
      <div class="flex flex-col gap-3">
        <div class="flex flex-col gap-1">
          <div class="flex justify-between text-sm">
            <span>{t('tools.batchPv.overall')}</span>
            <span>{state.completed}/{state.total}{state.failed > 0 ? ` (${state.failed} ✗)` : ''}</span>
          </div>
          <Progress status="success" percentage={overallPercent.value} showIndicator />
        </div>
        <div class="flex flex-col gap-1">
          <div class="flex justify-between text-sm">
            <span>{t('tools.batchPv.currentFile')}</span>
            <span class="truncate ml-2 op-70">{state.fileName}</span>
          </div>
          <Progress status="success" percentage={state.fileProgress} showIndicator />
        </div>
        {fileErrors.value.length > 0 && (
          <details class="text-xs">
            <summary class="cursor-pointer">{t('tools.batchPv.fileErrors')} ({fileErrors.value.length})</summary>
            <ul class="mt-1 max-h-40 overflow-auto m-0 pl-4">
              {fileErrors.value.map((err, i) => <li key={i}>{err}</li>)}
            </ul>
          </details>
        )}
        <div class="flex justify-end items-center gap-3">
          <span class="text-xs op-60">{t('tools.batchPv.cancelHint')}</span>
          <Button onClick={cancel} disabled={cancelling.value}>
            {cancelling.value ? t('tools.batchPv.cancelling') : t('tools.batchPv.cancel')}
          </Button>
        </div>
      </div>
    );

    const renderDone = () => {
      const summary = (() => {
        if (finishKind.value === FinishKind.Cancelled) {
          const [doneStr, totalStr] = finishSummary.value.split('/');
          return t('tools.batchPv.cancelledSummary', {
            completed: parseInt(doneStr ?? '0', 10),
            total: parseInt(totalStr ?? '0', 10),
          });
        }
        const [doneStr, failedStr] = finishSummary.value.split('|');
        const [doneVal, totalVal] = doneStr.split('/').map(v => parseInt(v, 10));
        const failedVal = parseInt(failedStr ?? '0', 10);
        return t('tools.batchPv.completedSummary', {
          success: doneVal - failedVal,
          total: totalVal,
          failed: failedVal,
        });
      })();

      return (
        <div class="flex flex-col gap-3">
          <div>{summary}</div>
          {fileErrors.value.length > 0 && (
            <details class="text-xs" open>
              <summary class="cursor-pointer">{t('tools.batchPv.fileErrors')} ({fileErrors.value.length})</summary>
              <ul class="mt-1 max-h-60 overflow-auto m-0 pl-4">
                {fileErrors.value.map((err, i) => <li key={i}>{err}</li>)}
              </ul>
            </details>
          )}
          <div class="flex justify-end">
            <Button variant="primary" onClick={closeDone}>{t('tools.batchPv.close')}</Button>
          </div>
        </div>
      );
    };

    return () => (
      <Modal
        width="min(50vw,42em)"
        title={t('tools.batchPv.title')}
        v-model:show={show.value}
        esc={step.value !== STEP.Progress}
      >
        {step.value === STEP.Configure && renderConfigure()}
        {step.value === STEP.Progress && renderProgress()}
        {step.value === STEP.Done && renderDone()}
      </Modal>
    );
  },
});
