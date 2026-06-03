import api from '@/client/api';
import { addToast } from '@munet/ui';
import { defineComponent, ref } from 'vue';
import ImageToAbModal from '@/views/Tools/ImageToAbModal';
import PvConvertDropMenu from '@/views/Tools/PvConvertDropMenu';
import { useI18n } from 'vue-i18n';

interface ToolCard {
  icon: string;
  labelKey: string;
  descKey?: string;
  action: () => void;
}

export default defineComponent({
  setup() {
    const imageToAbRef = ref<{ trigger: () => void }>();
    const { t } = useI18n();

    const handleAudioConvert = async () => {
      try {
        const res = await api.AudioConvertTool();
        if (res.status === 200) {
          addToast({ message: t('tools.convertSuccess'), type: 'success' });
        } else {
          addToast({ message: t('tools.convertFailed'), type: 'error' });
        }
      } catch {
        addToast({ message: t('tools.convertFailed'), type: 'error' });
      }
    };

    const tools: ToolCard[] = [
      {
        icon: 'i-mdi-music-note',
        labelKey: 'tools.audioConvert',
        action: handleAudioConvert,
      },
      {
        icon: 'i-mdi-image',
        labelKey: 'tools.imageToAb',
        action: () => imageToAbRef.value?.trigger(),
      },
    ];

    const renderToolCard = (tool: ToolCard) => (
      <div
        key={tool.labelKey}
        class={[
          'flex flex-col items-center justify-center gap-3 p-6',
          'rounded-xl cursor-pointer transition-all duration-200',
          'border border-solid border-gray-200',
          'bg-white hover:bg-[oklch(0.97_0.01_var(--hue))]',
          'hover:border-[var(--link-color)]/40 hover:shadow-sm',
        ]}
        onClick={tool.action}
      >
        <span class={[tool.icon, 'text-8 text-[var(--link-color)]']} />
        <span class="text-sm text-center font-medium">{t(tool.labelKey)}</span>
      </div>
    );

    return () => (
      <div class="flex flex-col h-100dvh p-6 overflow-auto">
        <h2 class="text-2xl font-bold mb-6">{t('tools.title')}</h2>
        <div class="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
          {renderToolCard(tools[0])}
          <PvConvertDropMenu />
          {renderToolCard(tools[1])}
        </div>
        <ImageToAbModal ref={imageToAbRef as any} />
      </div>
    );
  },
});
