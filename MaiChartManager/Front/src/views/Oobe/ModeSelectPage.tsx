import { defineComponent, ref, PropType } from 'vue';
import { useI18n } from 'vue-i18n';
import { TransitionVertical, TextInput, CheckBox, Button } from '@munet/ui';

export default defineComponent({
  props: {
    completing: { type: Boolean, default: false },
    onComplete: { type: Function as PropType<(opts: { isRemote: boolean; useAuth: boolean; authUsername: string; authPassword: string; startupEnabled: boolean }) => void>, required: true },
  },
  setup(props) {
    const { t } = useI18n();

    const isRemote = ref(false);
    const useAuth = ref(false);
    const authUsername = ref('');
    const authPassword = ref('');

    const startupEnabled = ref(false);

    const handleComplete = () => {
      props.onComplete({
        isRemote: isRemote.value,
        useAuth: useAuth.value,
        authUsername: authUsername.value,
        authPassword: authPassword.value,
        startupEnabled: startupEnabled.value,
      });
    };

    return () => (
      // Issue #74: OOBE屏幕的内容允许滚动，这样用户可以自己想办法滚动，避免点不到确认按钮之类
      <div class="absolute inset-0 overflow-y-auto">
        <div class="flex flex-col items-center justify-center min-h-full px-6 py-6 pb-24 box-border">
          <div class="text-5 font-bold op-90">{t('oobe.selectMode')}</div>
          <div class="flex gap-4 w-full max-w-md mt-6 mb-3">
            <div
              class={['flex-1 p-5 rounded-xl cursor-pointer transition-all border-2 flex flex-col items-center gap-3',
                !isRemote.value ? 'border-[oklch(0.7_0.1_var(--hue))] bg-[oklch(0.95_0.03_var(--hue))]' : 'border-transparent bg-[oklch(0.96_0.01_var(--hue))] hover:bg-[oklch(0.94_0.02_var(--hue))]']}
              onClick={() => isRemote.value = false}
            >
              <div class="i-mdi-monitor text-3xl op-60" />
              <div class="font-bold">{t('oobe.localMode')}</div>
              <div class="text-xs op-60 text-center">{t('oobe.localModeDesc')}</div>
            </div>
            <div
              class={['flex-1 p-5 rounded-xl cursor-pointer transition-all border-2 flex flex-col items-center gap-3',
                isRemote.value ? 'border-[oklch(0.7_0.1_var(--hue))] bg-[oklch(0.95_0.03_var(--hue))]' : 'border-transparent bg-[oklch(0.96_0.01_var(--hue))] hover:bg-[oklch(0.94_0.02_var(--hue))]']}
              onClick={() => isRemote.value = true}
            >
              <div class="i-mdi-lan text-3xl op-60" />
              <div class="font-bold">{t('oobe.remoteMode')}</div>
              <div class="text-xs op-60 text-center">{t('oobe.remoteModeDesc')}</div>
            </div>
          </div>
          <TransitionVertical>
            {isRemote.value && (
              <div class="flex flex-col gap-3 w-full max-w-sm my-3">
                <CheckBox v-model:value={useAuth.value}>
                  {t('oobe.enableAuth')}
                </CheckBox>
                <TransitionVertical>
                  {useAuth.value && (
                    <div class="flex flex-col gap-2">
                      <TextInput
                        placeholder={t('oobe.username')}
                        v-model:value={authUsername.value}
                      />
                      <TextInput
                        type="password"
                        placeholder={t('oobe.password')}
                        v-model:value={authPassword.value}
                      />
                    </div>
                  )}
                </TransitionVertical>
                <CheckBox v-model:value={startupEnabled.value}>
                  {t('oobe.startupOnBoot')}
                </CheckBox>
              </div>
            )}
          </TransitionVertical>
          <Button
            onClick={handleComplete}
            ing={props.completing}
          >
            {t('oobe.complete')}
          </Button>
        </div>
      </div>
    );
  },
});
