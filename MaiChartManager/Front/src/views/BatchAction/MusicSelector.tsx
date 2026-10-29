import { computed, defineComponent, PropType, reactive, ref } from "vue";
import { DataTableBaseColumn, DataTableColumns, NDataTable } from "naive-ui";
import { Button, TextInput } from "@munet/ui";
import { addVersionList, b15ver, genreList, musicList, musicListAll, selectedADir, selectMusicId, sidebarActive, version } from "@/store/refs";
import { MusicXmlWithABJacket } from "@/client/apiGen";
import JacketBox from "@/components/JacketBox";
import { GenreOption } from "@/components/GenreInput";
import { LEVEL_COLOR, LEVELS } from "@/consts";
import _ from "lodash";
import { matchMusicKeyword, MusicIdKind, musicIdKind } from "@/utils/musicSearch";
import { watchDebounced } from "@vueuse/core";
import { scrollToMusic } from "@/views/Charts/MusicList";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  props: {
    selectedMusicIds: Array as PropType<MusicXmlWithABJacket[]>,
    continue: { type: Function, required: true },
    cancel: { type: Function, required: true },
  },
  setup(props, { emit }) {
    const filter = ref('')
    const { t } = useI18n();

    const nameColumn = reactive({
      title: t('music.list.tableTitle'), key: 'name',
      filterOptionValue: null as string | null,
      filter: (value, row) => matchMusicKeyword(row, value?.toString() ?? ''),
    } satisfies DataTableBaseColumn<MusicXmlWithABJacket>)
    const columns = computed(() => [
      { type: 'selection' },
      { title: 'Opt', key: 'assetDir', width: '8em', filter: "default", filterOptions: _.uniq(musicListAll.value.map(it => it.assetDir!)).map(it => ({ label: it, value: it })) },
      {
        title: 'ID',
        key: 'id',
        width: '7em',
        sorter: 'default',
        filterOptions: [
          { label: t('music.list.filterStandard'), value: MusicIdKind.Standard },
          { label: t('music.list.filterDX'), value: MusicIdKind.DX },
          { label: t('music.list.filterUtage'), value: MusicIdKind.Utage },
        ],
        // 顺便修掉之前用翻译后的文案当筛选值来比较的写法
        filter: (value, row) => musicIdKind(row.id) === value,
      },
      {
        title: t('music.list.tableCover'),
        key: 'jacket',
        render: (row) => <JacketBox info={row} upload={false} class="h-20"/>,
        width: '8rem'
      },
      nameColumn,
      {
        title: t('music.list.tableVersion'),
        key: 'version',
        width: '9.5em',
        sorter: 'default',
        filterOptions: ['B35', 'B15'].map(it => ({ label: it, value: it })),
        filter: (value, row) => {
          const type = row.version! < b15ver.value ? 'B35' : 'B15';
          return value === type;
        }
      },
      {
        title: t('music.list.tableAddVersion'),
        key: 'addVersionId',
        render: (row) => <GenreOption genre={addVersionList.value.find(it => it.id === row.addVersionId)}/>,
        filter: (value, row) => row.addVersionId === value,
        filterOptions: addVersionList.value.map(it => ({ label: it.genreName!, value: it.id! }))
      },
      {
        title: t('music.list.tableGenre'),
        key: 'genreId',
        render: (row) => <GenreOption genre={genreList.value.find(it => it.id === row.genreId)}/>,
        filter: (value, row) => row.genreId === value,
        filterOptions: genreList.value.map(it => ({ label: it.genreName!, value: it.id! }))
      },
      {
        title: t('music.list.tableCharts'),
        key: 'charts',
        render: (row) => <div class="flex gap-1 pt-1 text-sm">
          {
            (row.charts || []).map((chart, index) =>
              chart.enable && <div key={index} class="c-white rounded-full px-2" style={{ backgroundColor: LEVEL_COLOR[index!] }}>{LEVELS[chart.levelId!]}</div>)
          }
        </div>,
        width: '20em',
        filterOptions: [t('music.list.filterBasic'), t('music.list.filterAdvanced'), t('music.list.filterExpert'), t('music.list.filterMaster'), t('music.list.filterReMaster')].map((label, value) => ({ label, value })),
        filter: (value, row) => row.charts![value as number].enable!
      },
      {
        title: t('music.list.tableJump'),
        key: 'jump',
        width: '5em',
        render: (row) => <Button variant="ghost" class="p-2" onClick={() => {
          selectedADir.value = row.assetDir!;
          selectMusicId.value = row.id!;
          sidebarActive.value = 'charts';
          props.cancel();
          setTimeout(() => scrollToMusic(row.id!), 100);
        }}>
          <span class="i-tabler:external-link c-neutral-5"/>
        </Button>,
      },
    ] satisfies DataTableColumns<MusicXmlWithABJacket>)

    watchDebounced(
      filter,
      () => {
        nameColumn.filterOptionValue = filter.value
      },
      { debounce: 500 },
    )

    const selectedMusicIds = computed<string[]>({
      get: () => props.selectedMusicIds!.map(it => `${it.assetDir}:${it.id}`),
      set: (value) => emit('update:selectedMusicIds', value.map(it => {
        const [assetDir, id] = it.split(':');
        return musicListAll.value.find(music => music.assetDir === assetDir && music.id === Number(id))!;
      })),
    });

    return () => <div class="flex flex-col gap-3 h-full">
      {/*<NFlex>*/}
      {/*  <NButton onClick={() => {*/}
      {/*    emit('update:selectedMusicIds', musicListAll.value.filter(it => !props.selectedMusicIds!.includes(it)));*/}
      {/*  }}>反选</NButton>*/}
      {/*</NFlex>*/}
      <TextInput placeholder={t('music.list.searchPlaceholder')} v-model:value={filter.value}/>
      <NDataTable
        columns={columns.value}
        data={musicListAll.value}
        virtualScroll
        maxHeight="calc(100dvh - 12rem)"
        minRowHeight={104}
        rowKey={row => `${row.assetDir}:${row.id}`}
        v-model:checkedRowKeys={selectedMusicIds.value}
      />
      <div class="flex justify-end gap-2">
        <Button onClick={() => props.continue()} disabled={!selectedMusicIds.value.length}>{t('purchase.continue')}</Button>
      </div>
    </div>;
  }
})
