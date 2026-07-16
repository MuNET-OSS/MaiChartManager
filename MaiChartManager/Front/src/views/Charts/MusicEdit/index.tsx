import { computed, defineComponent, onMounted, PropType, provide, ref, watch } from "vue";
import { addVersionList, genreList, globalCapture, selectedADir, selectedMusic as info, selectMusicId, updateAddVersionList, updateGenreList, updateMusicList, selectedLevel, disableSync, gameVersion, b15ver } from "@/store/refs";
import type { Chart, MusicXmlWithABJacket } from "@/client/apiGen";
import api from "@/client/api";

import JacketBox from "../../../components/JacketBox";
import dxIcon from "@/assets/dxIcon.png";
import stdIcon from "@/assets/stdIcon.png";
import ChartPanel from "./ChartPanel";
import { DIFFICULTY, LEVEL_COLOR, LEVEL_HUE, UTAGE_GENRE } from "@/consts";
import ProblemsDisplay from "@/components/ProblemsDisplay";
import AcbAwb from "@/views/Charts/MusicEdit/AcbAwb";
import GenreInput from "@/components/GenreInput";
import VersionInput from "@/components/VersionInput";
import { captureException } from "@sentry/vue"
import noJacket from "@/assets/noJacket.webp";
import { getUrl } from "@/client/api";
import { t } from "@/locales";
import { CheckBox, NumberInput, TextInput, Tabs, TabPane, Popover } from "@munet/ui";
import { NTabPane, NTabs } from "naive-ui";

const Component = defineComponent({
  setup() {

    provide('disabled', computed(() => selectedADir.value === 'A000'));

    const firstEnabledChart = info.value?.charts?.findIndex(chart => chart.enable);
    if (firstEnabledChart !== undefined && firstEnabledChart >= 0) {
      selectedLevel.value = firstEnabledChart;
    }
    const isUtage = computed(() => info.value !== undefined &&
      (info.value.genreId === UTAGE_GENRE || (info.value.id ?? 0) >= 1e5));
    const chartIndexes = computed(() => isUtage.value ? [0] : [0, 1, 2, 3, 4]);

    const sync = (key: keyof MusicXmlWithABJacket, method: Function) => async () => {
      if (disableSync.value || !info.value) return;
      info.value!.modified = true;
      const value = (info.value as any)[key];
      const result = (await method(info.value.id!, info.value.assetDir, value)).data;
      if (key === "sortName" && typeof result === "string" && result !== value) {
        // 如果调用的是sortName接口，且返回的字符串（经过格式化后的实际内容）和传过去的值不同的话，则覆盖之
        info.value!.sortName = result;
      }
    }

    watch(() => info.value?.name, sync('name', api.EditMusicName));
    watch(() => info.value?.artist, sync('artist', api.EditMusicArtist));
    watch(() => info.value?.bpm, sync('bpm', api.EditMusicBpm));
    watch(() => info.value?.version, sync('version', api.EditMusicVersion));
    watch(() => info.value?.genreId, sync('genreId', api.EditMusicGenre));
    watch(() => info.value?.addVersionId, sync('addVersionId', api.EditMusicAddVersion));
    watch(() => info.value?.utageKanji, sync('utageKanji', api.EditMusicUtageKanji));
    watch(() => info.value?.comment, sync('comment', api.EditMusicComment));
    watch(() => info.value?.longMusic, sync('longMusic', api.EditMusicLong));
    watch(() => info.value?.sortName, sync('sortName', api.EditMusicSortName))

    onMounted(() => {
      if ('mediaSession' in navigator) {
        navigator.mediaSession.metadata = new MediaMetadata({
          title: info.value?.name || '',
          artist: info.value?.artist || '',
          album: genreList.value.find(genre => genre.id === info.value?.genreId)?.genreName || '',
          artwork: [
            { src: info.value?.hasJacket ? getUrl(`GetJacketApi/${selectedADir.value}/${info.value?.id}?${(info.value as any).updateTime}`) : noJacket, type: 'image/png' },
          ]
        });
      }
    })

    return () => info.value && <div class="flex flex-col gap-2">
      <div class="grid cols-[1fr_12em] gap-5">
        <div class="flex flex-col gap-2 relative">
          <div class="flex items-center gap-2 absolute right-0 top-0 mr-2 mt-2">
            <ProblemsDisplay problems={info.value.problems!}/>
          </div>
          <div class="flex items-center gap-2">
            <img src={info.value.id! >= 1e4 ? dxIcon : stdIcon} class="h-6"/>
            <div class="c-gray-5">
              <span class="op-70">ID: </span>
              <span class="select-text">{info.value.id}</span>
            </div>
          </div>

          <div class="ml-1 text-sm">{t('music.edit.name')}</div>
          <div class="flex items-center gap-4 w-full">
            <TextInput v-model:value={info.value.name} class="w-0 grow"/>
            <CheckBox v-model:value={info.value.longMusic}>{t('common.longMusic')}</CheckBox>
          </div>
          <div class="ml-1 text-sm">{t('music.edit.artist')}</div>
          <TextInput v-model:value={info.value.artist}/>
        </div>
        <JacketBox info={info.value} class="h-12em w-12em"/>
      </div>
      <div class="flex flex-col gap-2">
        <div class="flex items-center gap-1">
          <div class="ml-1 text-sm">{t('music.edit.sortName')}</div>
          <Popover trigger="hover">
            {{
              trigger: () => <div class="i-material-symbols:info-outline-rounded op-50"/>,
              default: () => <div>{t('music.edit.sortNameTips')}</div>
            }}
          </Popover>
        </div>
        <TextInput v-model:value={info.value!.sortName} class="w-full"/>
        <div class="flex gap-4">
          <div class="flex flex-col gap-2 w-0 grow">
            <div class="ml-1 text-sm">{t('music.edit.genre')}</div>
            <GenreInput options={genreList.value} v-model:value={info.value.genreId}/>
          </div>
          <div class="flex flex-col gap-2 w-0 grow">
            <div class="ml-1 text-sm">{t('music.edit.versionCategory')}</div>
            <GenreInput options={addVersionList.value} v-model:value={info.value.addVersionId}/>
          </div>
        </div>
        <div class="flex gap-4">
          <div class="flex flex-col gap-2 w-0 grow">
            <div class="ml-1 text-sm">{t('music.edit.bpm')}</div>
            <NumberInput min={0} v-model:value={info.value.bpm}/>
          </div>
          <div class="flex flex-col gap-2 w-0 grow">
            <div class="flex items-center gap-1">
              <div class="ml-1 text-sm">{t('music.edit.version')}</div>
              <Popover trigger="hover">
                {{
                  trigger: () => <div class="i-material-symbols:info-outline-rounded op-50"/>,
                  default: () => <div>{t('music.edit.versionHint', { gameVersion: gameVersion.value, b15ver: b15ver.value })}</div>
                }}
              </Popover>
            </div>

            <VersionInput v-model:value={info.value.version}/>
          </div>
        </div>
        {isUtage.value &&
          <div class="flex gap-4">
            <div class="flex flex-col gap-2 w-0 grow">
              <div class="ml-1 text-sm">{t('music.edit.utageType')}</div>
              <TextInput v-model:value={info.value.utageKanji}/>
            </div>
            <div class="flex flex-col gap-2 w-0 grow-3">
              <div class="ml-1 text-sm">{t('music.edit.utageComment')}</div>
              <TextInput v-model:value={info.value.comment}/>
            </div>
          </div>}
        <AcbAwb song={info.value}/>
        <NTabs type="line" animated barWidth={0} v-model:value={selectedLevel.value} class="levelTabs"
               style={{ '--n-tab-padding': 0, '--n-pane-padding-top': 0, '--n-tab-text-color-hover': '' }}>
          {chartIndexes.value.map(index =>
            <NTabPane key={index} name={index} tab={DIFFICULTY[index]}>
              {{
                tab: () => <Tab index={index} chart={info.value?.charts![index]!} selected={selectedLevel.value === index}/>,
              default: () => <ChartPanel chart={info.value?.charts![index]!} songId={info.value?.id!} chartIndex={index}
                                           doublePlayer={info.value?.utagePlayStyle === 1}
                                           class="pxy pt-2 rounded-[0_0_.5em_.5em]" style={{ backgroundColor: `color-mix(in srgb, ${LEVEL_COLOR[index]}, transparent 90%)`, '--hue': LEVEL_HUE[index] }}/>
              }}
            </NTabPane>
          )}
        </NTabs>
      </div>
    </div>;
  },
})

const Tab = defineComponent({
  props: {
    index: { type: Number, required: true },
    chart: { type: Object as PropType<Chart>, required: true },
    selected: Boolean,
  },
  setup(props) {
    return () => <div class={`w-full py-3 flex justify-center rounded-[.5em_.5em_0_0] pos-relative of-hidden ${props.selected && 'c-white font-500 pb-4'}`}
                      style={{
                        backgroundColor: `color-mix(in srgb, ${LEVEL_COLOR[props.index]}, transparent ${props.selected ? 0 : 40}%)`,
                        transition: 'background-color 0.3s, padding-bottom 0.3s'
                      }}>
      {
        !props.chart.enable &&
        <div class="pos-absolute top-0 bottom-0 left-0 right-0" style={{
          backgroundPosition: '0 0',
          background: `repeating-linear-gradient(-45deg,
                        rgba(255, 255, 255, .3) 0, rgba(255, 255, 255, .3) 5%, rgba(255, 255, 255, .05) 5%, rgba(255, 255, 255, .05) 10%)`
        }}/>
      }
      <span class="z-1">{DIFFICULTY[props.index]}</span>
    </div>
  }
})


export default defineComponent({
  setup() {
    // 加载时销毁，防止 watch 被执行
    return () => <Component key={selectMusicId.value}/>;
  }
})
