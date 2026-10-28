import { Button, TextInput, theme } from '@munet/ui';
import { defineComponent, computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { FancyEntry, MusicXmlWithABJacket } from '@/client/apiGen';
import JacketBox from '@/components/JacketBox';
import { fancyEntries, musicName, openSearch, selectedId, selectMusic, showSearch } from './refs';
import { matchMusicKeyword } from '@/utils/musicSearch';
import { musicListAll } from '@/store/refs';

export default defineComponent({
  setup() {
    const { t } = useI18n();
    const filter = ref('');

    const musicMap = computed(() => {
      const map = new Map<number, MusicXmlWithABJacket>();
      for (const music of musicListAll.value) {
        if (music.id === undefined || map.has(music.id)) continue;
        map.set(music.id, music);
      }
      return map;
    });

    const rows = computed(() => {
      const keyword = filter.value.trim();
      if (!keyword) return fancyEntries.value;

      return fancyEntries.value.filter(entry => {
        const music = musicMap.value.get(entry.id!);
        return (music ? matchMusicKeyword(music, keyword) : false)
          || (entry.camouflageName ?? '').toLowerCase().includes(keyword.toLowerCase())
          || (entry.camouflageArtist ?? '').toLowerCase().includes(keyword.toLowerCase())
          || (entry.id ?? 0).toString().includes(keyword);
      });
    });

    const renderBadges = (entry: FancyEntry) => <div class="self-start flex items-center gap-1 shrink-0">
      {(entry.hasSingle || entry.hasLeft || entry.hasRight || entry.hasAudio) &&
        <span class="i-mdi-movie-open-play text-base text-[var(--link-color)]"
              title={t('tools.fancy.badgeIntro')}/>}
      {entry.hasCamouflage &&
        <span class="i-mdi-incognito text-base text-[var(--link-color)]"
              title={t('tools.fancy.badgeCamouflage')}/>}
    </div>;

    return () => <div class="flex flex-col h-full gap-2 p-4 border-r-1 border-r-solid border-r-gray-200">
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
          : rows.value.map(entry => {
            const music = musicMap.value.get(entry.id!);
            return <div
              key={entry.id}
              class={[
                'flex items-center gap-2 px-2 py-1.5 rounded cursor-pointer transition-colors',
                selectedId.value === entry.id && !showSearch.value ? theme.value.listItemSelect : theme.value.listItemHover,
              ]}
              onClick={() => selectMusic(entry.id!)}
            >
              <div class="h-10 w-10 shrink-0 rounded-lg overflow-hidden bg-black/5">
                {music ? <JacketBox info={music} upload={false} class="h-10 w-10"/> : null}
              </div>
              <div class="w-0 grow flex flex-col justify-center">
                <span class="text-xs op-50 tabular-nums leading-tight">#{entry.id}</span>
                <span class="text-sm truncate leading-snug">{musicName(entry.id) || t('tools.fancy.none')}</span>
              </div>
              {renderBadges(entry)}
            </div>;
          })}
      </div>
    </div>;
  },
});
