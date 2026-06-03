import { LicenseStatus } from '@/client/apiGen';
import { showNeedPurchaseDialog, version } from '@/store/refs';
import BatchVideoConvertModal from '@/views/Tools/BatchVideoConvertModal';
import VideoConvertModal from '@/views/Tools/VideoConvertModal';
import { DropMenu } from '@munet/ui';
import { computed, defineComponent, ref } from 'vue';
import { useI18n } from 'vue-i18n';

const toolCardClass = [
  'flex flex-col items-center justify-center gap-3 p-6',
  'rounded-xl cursor-pointer transition-all duration-200',
  'border border-solid border-gray-200',
  'bg-white hover:bg-[oklch(0.97_0.01_var(--hue))]',
  'hover:border-[var(--link-color)]/40 hover:shadow-sm',
];

export default defineComponent({
  setup() {
    const { t } = useI18n();
    const videoConvertRef = ref<{ trigger: () => void }>();
    const batchVideoConvertRef = ref<{ trigger: () => void }>();

    const ensureLicense = () => {
      if (version.value?.license === LicenseStatus.Active) return true;
      showNeedPurchaseDialog.value = true;
      return false;
    };

    const options = computed(() => [
      {
        label: t('tools.pvConvert.single'),
        desc: t('tools.pvConvert.singleHint'),
        action: () => {
          if (!ensureLicense()) return;
          videoConvertRef.value?.trigger();
        },
      },
      {
        label: t('tools.pvConvert.batch'),
        desc: t('tools.pvConvert.batchHint'),
        action: () => {
          if (!ensureLicense()) return;
          batchVideoConvertRef.value?.trigger();
        },
      },
    ]);

    return () => (
      <>
        <DropMenu options={options.value} align="center">
          {{
            trigger: (toggle: (val?: boolean) => void) => (
              <div
                class={toolCardClass}
                onClick={() => {
                  if (!ensureLicense()) return;
                  toggle();
                }}
              >
                <span class="i-mdi-video text-8 text-[var(--link-color)]" />
                <span class="flex items-center justify-center gap-1 text-sm text-center font-medium">
                  {t('tools.pvConvert.label')}
                </span>
              </div>
            ),
          }}
        </DropMenu>
        <VideoConvertModal ref={videoConvertRef as any} />
        <BatchVideoConvertModal ref={batchVideoConvertRef as any} />
      </>
    );
  },
});
