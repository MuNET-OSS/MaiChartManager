import { defineComponent, PropType, ref, computed, watch } from 'vue';
import { ConfigDto, Section } from "@/client/apiGen";
import { TextInput, theme, WhateverNaviBar } from '@munet/ui';
import _ from "lodash";
import configSortStub from './configSort.yaml'
import { useMagicKeys, useStorage, whenever } from "@vueuse/core";
import { getBigSectionName } from './utils';
import { useI18n } from 'vue-i18n';
import ConfigSection from './ConfigSection';

const FAVORITES_TAB_KEY = '__favorites__';

export default defineComponent({
  props: {
    config: { type: Object as PropType<ConfigDto>, required: true },
    useNewSort: { type: Boolean, default: false },
  },
  setup(props, { emit }) {
    const search = ref('');
    const searchRef = ref();
    const activeTab = ref<string | null>(null);
    const scrollContainerRef = ref<HTMLElement>();
    const configSort = computed(() => props.config?.configSort || configSortStub)
    const communityList = computed(() => configSort.value['社区功能'] || []);
    const { t } = useI18n();
    const favoriteSectionPaths = useStorage<string[]>('aquamai-config-favorite-sections', []);

    const configSections = computed(() => props.config?.sections || []);
    const favoritePathList = computed(() => Array.isArray(favoriteSectionPaths.value) ? favoriteSectionPaths.value : []);
    const favoritePathSet = computed(() => new Set(favoritePathList.value));
    const sectionByPath = computed(() => new Map(configSections.value
      .filter((section): section is Section & { path: string } => !!section.path)
      .map(section => [section.path, section])));

    const { ctrl_f } = useMagicKeys({
      passive: false,
      onEventFired(e) {
        if (e.ctrlKey && e.key === 'f' && e.type === 'keydown')
          e.preventDefault()
      },
    })
    whenever(ctrl_f, () => searchRef.value?.select());

    const sectionMatchesSearch = (section: Section, keyword: string) =>
      section.path?.toLowerCase().includes(keyword) ||
      section.attribute?.comment?.nameZh?.toLowerCase().includes(keyword) ||
      section.attribute?.comment?.commentZh?.toLowerCase().includes(keyword) ||
      section.attribute?.comment?.commentEn?.toLowerCase().includes(keyword) ||
      section.entries?.some(entry => entry.name?.toLowerCase().includes(keyword) || entry.path?.toLowerCase().includes(keyword) ||
        entry.attribute?.comment?.commentZh?.toLowerCase().includes(keyword) || entry.attribute?.comment?.commentEn?.toLowerCase().includes(keyword) ||
        entry.attribute?.comment?.nameZh?.toLowerCase().includes(keyword));

    const filteredSections = computed(() => {
      if (!search.value) return configSections.value;
      const s = search.value.toLowerCase();
      return configSections.value.filter(it => sectionMatchesSearch(it, s));
    })

    const favoriteSections = computed(() => {
      const seen = new Set<string>();
      return favoritePathList.value
        .filter(path => {
          if (typeof path !== 'string' || seen.has(path)) return false;
          seen.add(path);
          return true;
        })
        .map(path => sectionByPath.value.get(path))
        .filter((section) => !!section && !section.attribute?.exampleHidden);
    });

    const toggleFavoriteSection = (path: string) => {
      if (favoritePathSet.value.has(path)) {
        favoriteSectionPaths.value = favoritePathList.value.filter(item => item !== path);
        return;
      }
      favoriteSectionPaths.value = [...favoritePathList.value.filter(item => typeof item === 'string'), path];
    };

    const bigSections = computed(() => {
      if (props.useNewSort) {
        return Object.keys(configSort.value).filter(it => it !== '社区功能').filter(it => filteredSections.value?.some(s => configSort.value[it].includes(s.path!)) ?? false);
      }
      return _.uniq((filteredSections.value ?? []).filter(it => !it.attribute?.exampleHidden).map(s => s.path?.split('.')[0]));
    });

    const otherSection = computed(() => {
      if (!props.useNewSort) return [];
      const knownSections = _.flatten(Object.values(configSort.value) as string[][]);
      return filteredSections.value?.filter(it => !knownSections.includes(it.path!) && !it.attribute!.exampleHidden) || [];
    });

    // 所有可选 tab，包括 "其他"
    const allTabs = computed(() => {
      const tabs = bigSections.value.map(key => ({ key: key!, label: getBigSectionName(key!) }));
      if (favoriteSections.value.length > 0) {
        tabs.unshift({ key: FAVORITES_TAB_KEY, label: t('mod.favorite') });
      }
      if (otherSection.value.length > 0) {
        tabs.push({ key: '__other__', label: t('mod.other') });
      }
      return tabs;
    });

    // 默认选中第一个 tab
    watch(() => allTabs.value, (tabs) => {
      if (tabs.length > 0 && (!activeTab.value || !tabs.some(t => t.key === activeTab.value))) {
        activeTab.value = tabs[0].key;
      }
    }, { immediate: true });

    watch(activeTab, () => {
      scrollContainerRef.value?.scrollTo(0, 0);
    });

    // 当前要显示的 sections：搜索时显示所有匹配结果，否则只显示当前 tab
    const currentSections = computed(() => {
      if (search.value) {
        // 搜索模式：显示所有匹配的 sections（不限 tab）
        return filteredSections.value?.filter(it => !it.attribute?.exampleHidden) || [];
      }
      if (!activeTab.value) return [];
      if (activeTab.value === FAVORITES_TAB_KEY) return favoriteSections.value;
      if (activeTab.value === '__other__') return otherSection.value;
      return filteredSections.value?.filter(it => {
        if (props.useNewSort) {
          return configSort.value[activeTab.value!]?.includes(it.path!);
        }
        return it.path!.split('.')[0] === activeTab.value && !it.attribute!.exampleHidden;
      }).sort((a, b) => {
        if (!props.useNewSort) return 0;
        return configSort.value[activeTab.value!].indexOf(a.path!) - configSort.value[activeTab.value!].indexOf(b.path!);
      }) || [];
    });

    return () => <div class="grid cols-[15em_auto] rows-1 max-[900px]:cols-1 flex-1 min-h-0">
      {/* 左侧导航 */}
      <div class="flex flex-col gap-0.5 max-[900px]:hidden of-y-auto h-full">
        {allTabs.value.map(tab =>
          <div
            key={tab.key}
            class={[
              'px-3 py-1.5 rd cursor-pointer text-sm transition-colors',
              activeTab.value === tab.key && theme.value.listItemSelect, theme.value.listItemHover,
            ]}
            onClick={() => activeTab.value = tab.key}
          >
            {tab.label}
          </div>
        )}
      </div>
      <div class="flex flex-col h-full">
        <div class="min-[900px]:hidden shrink-0">
          <WhateverNaviBar items={allTabs.value.map(tab => ({
            name: tab.label,
            onClick: () => activeTab.value = tab.key,
            selected: activeTab.value === tab.key,
          }))}/>
        </div>
        <div class="flex gap-2 p-2 shrink-0">
          <TextInput v-model:value={search.value} placeholder={t('mod.searchPlaceholder')} ref={searchRef} innerClass="h-42px!" class="flex-1"/>
        </div>
        <div ref={scrollContainerRef} class="of-y-auto cst flex-1 p-2 pt-0 text-14px">
          <div class="flex flex-col gap-1">
            {currentSections.value.map((section) => {
              if (!section) return null;
              const entryStates = props.config?.entryStates;
              const sectionStates = props.config?.sectionStates;
              const sectionState = sectionStates?.[section.path!];
              if (!entryStates || !sectionStates || !sectionState) return null;
              return <ConfigSection key={section.path!} section={section}
                                    entryStates={entryStates}
                                    isCommunity={communityList.value.includes(section.path!)}
                                    isFavorite={favoritePathSet.value.has(section.path!)}
                                    toggleFavorite={() => toggleFavoriteSection(section.path!)}
                                    sectionState={sectionState}
                                    allSectionStates={sectionStates}/>;
            })}
          </div>
        </div>
      </div>
    </div>;
  },
});
