import { computed, defineComponent, ref } from 'vue';
import { DataTableColumns, NDataTable } from 'naive-ui';
import { Button, TextInput } from '@munet/ui';
import { useI18n } from 'vue-i18n';
import { MusicXmlWithABJacket } from '@/client/apiGen';
import JacketBox from '@/components/JacketBox';
import { GenreOption } from '@/components/GenreInput';
import { addVersionList, genreList, musicListAll } from '@/store/refs';
import { matchMusicKeyword } from '@/utils/musicSearch';
import { fancyEntries, selectMusic, showSearch, selectedId } from './refs';

export default defineComponent({
  setup() {
    const { t } = useI18n();
    const keyword = ref('');

    // LocalAssets 是按曲目 id 生效的，和 A 目录无关，所以同一 id 只留一条
    const musics = computed(() => {
      const seen = new Set<number>();
      const result: MusicXmlWithABJacket[] = [];
      for (const music of musicListAll.value) {
        if (music.id === undefined || seen.has(music.id)) continue;
        seen.add(music.id);
        result.push(music);
      }
      return result;
    });

    const rows = computed(() => musics.value.filter(music => matchMusicKeyword(music, keyword.value)));

    const entryMap = computed(() => new Map(fancyEntries.value.map(entry => [entry.id!, entry])));

    // 已经有内容的曲目在搜索页里也标一下，免得重复添加
    const renderStatus = (row: MusicXmlWithABJacket) => {
      const entry = entryMap.value.get(row.id!);
      if (!entry) return null;
      return <div class="flex items-center gap-1">
        {entry.hasSingle || entry.hasLeft || entry.hasRight || entry.hasAudio
          ? <span class="i-mdi-movie-open-play text-base text-[var(--link-color)]" title={t('tools.fancy.badgeIntro')}/>
          : null}
        {entry.hasCamouflage
          ? <span class="i-mdi-incognito text-base text-[var(--link-color)]" title={t('tools.fancy.badgeCamouflage')}/>
          : null}
      </div>;
    };

    const kindOptions = computed(() => [
      { label: t('music.list.filterStandard'), value: 'standard' },
      { label: t('music.list.filterDX'), value: 'dx' },
      { label: t('music.list.filterUtage'), value: 'utage' },
    ]);

    const columns = computed(() => [
      {
        title: t('music.list.tableCover'),
        key: 'jacket',
        width: '6rem',
        render: (row: MusicXmlWithABJacket) => <JacketBox info={row} upload={false} class="h-14"/>,
      },
      {
        title: 'ID',
        key: 'id',
        width: '8em',
        sorter: 'default',
        filterOptions: kindOptions.value,
        filter: (value: any, row: MusicXmlWithABJacket) => {
          const id = row.id ?? 0;
          if (value === 'standard') return id < 1e4;
          if (value === 'dx') return id >= 1e4 && id < 1e5;
          return id >= 1e5;
        },
      },
      {
        title: t('music.list.tableTitle'),
        key: 'name',
        render: (row: MusicXmlWithABJacket) => <div class="flex flex-col min-w-0">
          <span class="truncate">{row.name}</span>
          <span class="text-xs op-60 truncate">{row.artist}</span>
        </div>,
      },
      {
        title: t('music.list.tableVersion'),
        key: 'addVersionId',
        width: '11em',
        filterOptions: addVersionList.value.map(it => ({ label: it.genreName!, value: it.id! })),
        filter: (value: any, row: MusicXmlWithABJacket) => row.addVersionId === value,
        render: (row: MusicXmlWithABJacket) => <GenreOption genre={addVersionList.value.find(it => it.id === row.addVersionId)}/>,
      },
      {
        title: t('music.list.tableGenre'),
        key: 'genreId',
        width: '13em',
        filterOptions: genreList.value.map(it => ({ label: it.genreName!, value: it.id! })),
        filter: (value: any, row: MusicXmlWithABJacket) => row.genreId === value,
        render: (row: MusicXmlWithABJacket) => <GenreOption genre={genreList.value.find(it => it.id === row.genreId)}/>,
      },
      { title: '', key: 'status', width: '4em', render: renderStatus },
    ] satisfies DataTableColumns<MusicXmlWithABJacket>);

    const pick = (row: MusicXmlWithABJacket) => {
      if (row.id !== undefined) selectMusic(row.id);
    };

    return () => <div class="flex flex-col gap-2 h-full p-4">
      <div class="flex items-center gap-2">
        <TextInput class="grow-1" v-model:value={keyword.value} placeholder={t('music.list.searchPlaceholder')} typeFocus/>
        {!!selectedId.value &&
          <Button variant="secondary" onClick={() => showSearch.value = false}>{t('common.cancel')}</Button>}
      </div>
      <div class="text-xs op-50">{t('tools.fancy.searchHint')}</div>
      {/* 父级给死高度，表格才不会把底部的分页/表体挤出可视区 */}
      <div class="grow-1 h-0">
        <NDataTable
          flexHeight
          style="height: 100%"
          columns={columns.value}
          data={rows.value}
          virtualScroll
          minRowHeight={72}
          rowKey={(row: MusicXmlWithABJacket) => row.id!}
          rowProps={(row: MusicXmlWithABJacket) => ({
            style: 'cursor: pointer',
            onClick: () => pick(row),
          })}
        />
      </div>
    </div>;
  },
});
