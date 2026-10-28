import { computed, defineComponent, onMounted, ref } from 'vue';
import { useTitle } from '@vueuse/core';
import { WhateverNaviBar } from '@munet/ui';
import { useI18n } from 'vue-i18n';
import Splash from '@/components/Splash';
import { globalCapture } from '@/store/refs';
import { ensureBackendUrl } from '@/utils/ensureBackendUrl';
import MusicSidebar from './MusicSidebar';
import MusicSearch from './MusicSearch';
import IntroTab from './IntroTab';
import CamouflageTab from './CamouflageTab';
import { FancyTab, activeTab, initFancyTool, musicName, selectedId, showSearch } from './refs';

export default defineComponent({
  setup() {
    const { t } = useI18n();
    useTitle(t('tools.fancy.label'));

    const loaded = ref(false);

    onMounted(async () => {
      // 独立窗口是另一份 SPA 实例，backendUrl 要等宿主注入
      await ensureBackendUrl();
      try {
        await initFancyTool();
      } catch (e) {
        globalCapture(e, t('tools.fancy.errorLoad'));
      }
      loaded.value = true;
    });

    const naviItems = computed(() => [
      {
        name: t('tools.fancy.introTab'),
        icon: 'i-mdi-movie-open-play',
        selected: activeTab.value === FancyTab.Intro,
        onClick: () => { activeTab.value = FancyTab.Intro; },
      },
      {
        name: t('tools.fancy.camouflageTab'),
        icon: 'i-mdi-incognito',
        selected: activeTab.value === FancyTab.Camouflage,
        onClick: () => { activeTab.value = FancyTab.Camouflage; },
      },
    ]);

    return () => <div class="flex h-100dvh of-hidden">
      <Splash show={!loaded.value}/>
      <div class="w-72 shrink-0 h-full">
        <MusicSidebar/>
      </div>
      <div class="grow-1 w-0 h-full flex flex-col">
        {showSearch.value
          ? <MusicSearch/>
          : <>
            <div class="flex items-end gap-3 px-4 pt-3 shrink-0">
              <div class="flex flex-col min-w-0">
                <span class="text-lg font-500 truncate">{musicName(selectedId.value) || t('tools.fancy.none')}</span>
                <span class="text-xs op-50 tabular-nums">#{selectedId.value}</span>
              </div>
              <div class="grow-1"/>
              <span class="text-xs op-50 flex items-center gap-1 shrink-0">
                <span class="i-mdi-information-outline"/>
                {t('tools.fancy.restartHint')}
              </span>
            </div>
            <div class="px-4 shrink-0">
              <WhateverNaviBar items={naviItems.value}/>
            </div>
            <div class="grow-1 of-y-auto cst px-4">
              {activeTab.value === FancyTab.Intro ? <IntroTab/> : <CamouflageTab/>}
            </div>
          </>}
      </div>
    </div>;
  },
});
