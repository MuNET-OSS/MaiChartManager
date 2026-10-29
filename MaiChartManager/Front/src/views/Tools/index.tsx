import api from '@/client/api';
import { LicenseStatus } from '@/client/apiGen';
import { addToast } from '@munet/ui';
import { defineComponent, ref } from 'vue';
import ImageToAbModal from '@/views/Tools/ImageToAbModal';
import PvConvertDropMenu from '@/views/Tools/PvConvertDropMenu';
import ResourceJunctionModal from '@/views/Tools/ResourceJunctionModal';
import { showNeedPurchaseDialog, version } from '@/store/refs';
import { openWindow } from '@/utils/openWindow';
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
    const resourceJunctionRef = ref<{ trigger: () => void }>();
    const { t } = useI18n();

    // 开场视频要把上传的视频重新编码，属于付费功能；在打开工具时统一校验，
    // 工具内部就不再逐个接口判断了
    const openFancyTool = () => {
      if (version.value?.license !== LicenseStatus.Active) {
        showNeedPurchaseDialog.value = true;
        return;
      }
      openWindow({
        path: '/fancy-tool',
        title: t('tools.fancy.label'),
        width: 1280,
        height: 820,
      });
    };

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
      {
        icon: 'i-mdi-link-variant',
        labelKey: 'tools.resourceJunction.label',
        action: () => resourceJunctionRef.value?.trigger(),
      },
      {
        icon: 'i-mdi-movie-open-star',
        labelKey: 'tools.fancy.label',
        action: openFancyTool,
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
          {renderToolCard(tools[2])}
          {renderToolCard(tools[3])}
        </div>
        <ImageToAbModal ref={imageToAbRef as any} />
        <ResourceJunctionModal ref={resourceJunctionRef as any} />
      </div>
    );
  },
});
