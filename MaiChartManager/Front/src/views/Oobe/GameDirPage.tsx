import { defineComponent, ref, Transition } from 'vue';
import { useI18n } from 'vue-i18n';
import api from '@/client/api';
import TransitionOpacity from '@/components/TransitionOpacity';
import { TransitionVertical } from '@munet/ui';

export default defineComponent({
  props: {
    gamePath: { type: String, default: '' },
    pathValid: { type: Boolean, default: false },
    initializing: { type: Boolean, default: false },
  },
  emits: ['update:gamePath', 'update:pathValid', 'update:initializing'],
  setup(props, { emit }) {
    const { t } = useI18n();
    const pathError = ref('');

    const browseFolder = async () => {
      pathError.value = '';
      emit('update:pathValid', false);
      try {
        const res = await api.OpenFolderDialog();
        if (!res.data) return;
        emit('update:gamePath', res.data);
        await api.SetGamePath(res.data, {save: false});
        emit('update:pathValid', true);
      } catch (e: any) {
        pathError.value = t('oobe.pathNotValid');
      }
    };

    return () => (
      <div class="absolute inset-0 overflow-y-auto">
        <div class="relative flex flex-col items-center justify-center min-h-full gap-6 px-6 py-6 pb-24 box-border">
          <div class="i-mdi-folder-open text-5xl op-40" />
          <div class="text-5 font-bold op-90">{t('oobe.selectGameDir')}</div>
          <div class="text-sm op-60 text-center">{t('oobe.selectGameDirDesc')}</div>
          <button
            onClick={browseFolder}
          >
            {t('oobe.browse')}
          </button>
          <div>
            <TransitionVertical>
              {props.gamePath && (
                <div class="flex flex-col items-center gap-2">
                  <div class={['text-sm px-4 py-2 rounded-lg', props.pathValid ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800']}>
                    {props.pathValid ? t('oobe.selectedPath') : pathError.value}: {props.gamePath}
                  </div>
                </div>
              )}
            </TransitionVertical>
          </div>
          <Transition
            duration={500}
            leaveActiveClass="transition-opacity duration-500 transition-ease-in-out"
            enterActiveClass="transition-opacity duration-500 transition-ease-in-out"
            enterFromClass="opacity-0"
            enterToClass="opacity-100"
            leaveFromClass="opacity-100"
            leaveToClass="opacity-0"
          >
            {props.initializing && <div class="absolute-full flex items-center justify-center bg-[oklch(0.97_0.01_var(--hue)/0.9)] backdrop-blur-sm">
              <div class="flex items-center justify-center h-full text-sm op-60 animate-pulse">{t('oobe.initializingData')}</div>
            </div>}
          </Transition>
        </div>
      </div>
    );
  },
});
