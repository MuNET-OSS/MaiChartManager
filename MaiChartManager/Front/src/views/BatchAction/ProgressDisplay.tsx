import { defineComponent, PropType, ref } from "vue";
import { Button, Progress } from "@munet/ui";
import { useI18n } from 'vue-i18n';

export type ExportFailedItem = {
  id: number;
  name: string;
};

export const progressCurrent = ref(0);
export const progressAll = ref(100);
export const currentProcessItem = ref('');
export const exportFailedItems = ref<ExportFailedItem[]>([]);
export const exportSuccessCount = ref(0);
export const exportFinished = ref(false);

export const resetExportProgress = (total: number) => {
  progressCurrent.value = 0;
  progressAll.value = total;
  currentProcessItem.value = '';
  exportFailedItems.value = [];
  exportSuccessCount.value = 0;
  exportFinished.value = false;
};

export default defineComponent({
  props: {
    continue: { type: Function as PropType<() => void>, required: true },
  },
  setup(props) {
    const { t } = useI18n();

    const finish = () => {
      props.continue();
    };

    return () => <div class="flex flex-col gap-2">
      {exportFinished.value ? (
        <div>
          {t('music.batch.exportCompletedSummary', {
            success: exportSuccessCount.value,
            total: progressAll.value,
          })}
        </div>
      ) : (
        <>
          <div>{t('music.batch.currentProgress')}：{progressCurrent.value}/{progressAll.value}</div>
          <div>{t('music.batch.currentProcessing')}：{currentProcessItem.value}</div>
          <Progress
            status="success"
            percentage={progressAll.value === 0 ? 100 : Math.floor(progressCurrent.value / progressAll.value * 100)}
            showIndicator
          />
        </>
      )}

      {exportFailedItems.value.length > 0 && (
        <div class="flex flex-col gap-1">
          <div class="text-sm">{t('music.batch.exportFailedList')}（{exportFailedItems.value.length}）</div>
          <ul class="m-0 pl-4 max-h-60 overflow-auto text-sm">
            {exportFailedItems.value.map((item) => (
              <li key={`${item.id}-${item.name}`}>
                {item.name}（{item.id}）
              </li>
            ))}
          </ul>
        </div>
      )}

      {exportFinished.value && (
        <div class="flex justify-end">
          <Button variant="primary" onClick={finish}>{t('common.done')}</Button>
        </div>
      )}
    </div>;
  }
})
