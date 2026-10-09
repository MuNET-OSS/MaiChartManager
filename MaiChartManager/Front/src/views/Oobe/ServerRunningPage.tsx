import api from '@/client/api';
import { defineComponent, PropType } from 'vue';
import { useI18n } from 'vue-i18n';

export default defineComponent({
  props: {
    lanAddresses: { type: Array as PropType<string[]>, default: () => [] },
  },
  setup(props) {
    const { t } = useI18n();

    return () => (
      <div class="absolute inset-0 overflow-y-auto">
        <div class="flex flex-col items-center justify-center min-h-full gap-6 px-6 py-6 pb-24 box-border">
          <div class="i-material-symbols:check-circle-rounded text-5xl text-green-600" />
          <div class="text-xl font-bold op-90">{t('oobe.remoteReady')}</div>
          <div class="text-sm op-60">{t('oobe.lanAddresses')}</div>
          <div class="flex flex-col gap-2 items-center">
            {props.lanAddresses.map(addr => (
              <div class="px-4 py-2 rounded-lg bg-[oklch(0.92_0.02_var(--hue))] text-sm font-mono">https://{addr}:5001</div>
            ))}
          </div>
          <button onClick={() => api.OpenMainUI()}>
            {t('oobe.openMainUI')}
          </button>
        </div>
      </div>
    );
  },
});
