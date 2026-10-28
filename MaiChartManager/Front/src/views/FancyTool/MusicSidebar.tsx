import { Button, TextInput, theme } from '@munet/ui';
import { defineComponent, computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { FancyEntry } from '@/client/apiGen';
import { fancyEntries, musicName, openSearch, selectedId, selectMusic, showSearch } from './refs';
import { matchMusicKeyword } from '@/utils/musicSearch';
import { musicListAll } from '@/store/refs';

export default defineComponent({
  setup() {
    const { t } = useI18n();
    const filter = ref('');

    // 曲名按 id 回查，命中伪装的曲名时两条都显示
    const rows = computed(() => {
      const keyword = filter.value.trim();
      if (!keyword) return fancyEntries.value;

      return fancyEntries.value.filter(entry => {
        const music = musicListAll.value.find(it => it.id === entry.id);
        return (music ? matchMusicKeyword(music, keyword) : false)
          || (entry.camouflageName ?? '').toLowerCase().includes(keyword.toLowerCase())
          || (entry.camouflageArtist ?? '').toLowerCase().includes(keyword.toLowerCase())
          || (entry.id ?? 0).toString().includes(keyword);
      });
    });

    const renderBadges = (entry: FancyEntry) => <div class="flex items-center gap-1 shrink-0">
      {(entry.hasSingle || entry.hasLeft || entry.hasRight || entry.hasAudio) &&
        <span class="i-mdi-movie-open-play text-base text-[var(--link-color)]"
              title={t('tools.fancy.badgeIntro')}/>}
      {entry.hasCamouflage &&
        <span class="i-mdi-incognito text-base text-[var(--link-color)]"
              title={t('tools.fancy.badgeCamouflage')}/>}
    </div>;

    return () => <div class="flex flex-col h-full gap-2 p-xy border-r-1 border-r-solid border-r-gray-200">
      <Button class="w-full" variant={showSearch.value ? 'secondary' : 'primary'} onClick={openSearch}>
        <span class="i-mdi-plus mr-1"/>
        {t('tools.fancy.pickMusic')}
      </Button>
      <TextInput v-model:value={filter.value} placeholder={t('tools.fancy.filterPlaceholder')}/>

      <div class="grow-1 of-y-auto cst -mx-1 px-1">
        {rows.value.length === 0
          ? <div class="text-sm op-60 px-2 py-4">
            {fancyEntries.value.length === 0 ? t('tools.fancy.noEntries') : t('tools.fancy.noMatch')}
          </div>
          : rows.value.map(entry => <div
            key={entry.id}
            class={[
              'flex items-center gap-2 px-2 py-1.5 rounded cursor-pointer transition-colors',
              selectedId.value === entry.id && !showSearch.value ? theme.value.listItemSelect : theme.value.listItemHover,
            ]}
            onClick={() => selectMusic(entry.id!)}
          >
            <span class="text-xs tabular-nums op-50 w-10 shrink-0">{entry.id}</span>
            <div class="w-0 grow flex flex-col">
              <span class="text-sm truncate">{musicName(entry.id) || `#${entry.id}`}</span>
              {/* 伪装后的曲名和真实曲名不一样时并排显示，方便一眼看出改了名 */}
              {!!entry.camouflageName && entry.camouflageName !== musicName(entry.id) &&
                <span class="text-xs op-60 truncate">{entry.camouflageName}</span>}
            </div>
            {renderBadges(entry)}
          </div>)}
      </div>
    </div>;
  },
});
