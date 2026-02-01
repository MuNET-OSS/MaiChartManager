import { computed, defineComponent, onMounted, PropType, ref, watch } from "vue";
import { Chart, GenreXml, MusicXmlWithABJacket, ShiftMethod } from "@/client/apiGen";
import { addVersionList, genreList, globalCapture, selectedADir, selectedMusic as info, selectMusicId, updateAddVersionList, updateGenreList, updateMusicList, selectedLevel } from "@/store/refs";
import api from "@/client/api";
import { NButton, NFlex, NForm, NFormItem, NInput, NInputNumber, NPopover, NRadio, NSelect, NSwitch, NTabPane, NTabs, SelectOption, useDialog, useMessage } from "naive-ui";
import JacketBox from "../JacketBox";
import dxIcon from "@/assets/dxIcon.png";
import stdIcon from "@/assets/stdIcon.png";
import ChartPanel from "./ChartPanel";
import { DIFFICULTY, LEVEL_COLOR, UTAGE_GENRE } from "@/consts";
import ProblemsDisplay from "@/components/ProblemsDisplay";
import AcbAwb from "@/components/MusicEdit/AcbAwb";
import GenreInput from "@/components/GenreInput";
import VersionInput from "@/components/VersionInput";
import { captureException } from "@sentry/vue"
import noJacket from "@/assets/noJacket.webp";
import { getUrl } from "@/client/api";
import { t } from "@/locales";

const Component = defineComponent({
  setup() {
    const message = useMessage();

    const firstEnabledChart = info.value?.charts?.findIndex(chart => chart.enable);
    if (firstEnabledChart && firstEnabledChart >= 0) {
      selectedLevel.value = firstEnabledChart;
    }

    const sync = (key: keyof MusicXmlWithABJacket, method: Function) => async () => {
      if (!info.value) return;
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

    onMounted(()=>{
      if ('mediaSession' in navigator) {
        navigator.mediaSession.metadata = new MediaMetadata({
          title: info.value?.name || '',
          artist: info.value?.artist || '',
          album: genreList.value.find(genre => genre.id === info.value?.genreId)?.genreName || '',
          artwork: [
            { src: info.value?.hasJacket ? getUrl(`GetJacketApi/${selectedADir.value}/${info.value?.id}?${(info.value as any).updateTime}`) : noJacket,  type: 'image/png' },
          ]
        });
      }
    })

    return () => info.value && <NForm showFeedback={false} labelPlacement="top" disabled={selectedADir.value === 'A000'}>
        <div class="grid cols-[1fr_12em] gap-5">
            <NFlex vertical class="relative">
                <NFlex align="center" class="absolute right-0 top-0 mr-2 mt-2">
                    <ProblemsDisplay problems={info.value.problems!}/>
                </NFlex>
                <NFlex align="center">
                    <img src={info.value.id! >= 1e4 ? dxIcon : stdIcon} class="h-6"/>
                    <div class="c-gray-5">
                        <span class="op-70">ID: </span>
                        <span class="select-text">{info.value.id}</span>
                    </div>
                </NFlex>
                <NFormItem label={t('music.edit.name')}>
                    <div class="flex items-center gap-2 w-full">
                        <NInput v-model:value={info.value.name} class="w-0 grow"/>
                        <NSwitch v-model:value={info.value.longMusic}/>
                        <div class="shrink-0">{t('common.longMusic')}</div>
                    </div>
                </NFormItem>
                <NFormItem label={t('music.edit.artist')}>
                    <NInput v-model:value={info.value.artist}/>
                </NFormItem>
            </NFlex>
            <JacketBox info={info.value} class="h-12em w-12em"/>
        </div>
        <NFlex vertical>
            <NFormItem label={t('music.edit.bpm')}>
                <NInputNumber showButton={false} class="w-full" v-model:value={info.value.bpm} min={0}/>
            </NFormItem>
            <NFormItem label={t('music.edit.version')}>
                <VersionInput v-model:value={info.value.version}/>
            </NFormItem>
            <NFormItem label={t('music.edit.genre')}>
                <GenreInput options={genreList.value} v-model:value={info.value.genreId}/>
            </NFormItem>
            <NFormItem label={t('music.edit.versionCategory')}>
                <GenreInput options={addVersionList.value} v-model:value={info.value.addVersionId}/>
            </NFormItem>
          {info.value.genreId === UTAGE_GENRE && // 宴会场
              <>
                  <NFormItem label={t('music.edit.utageType')}>
                      <NInput v-model:value={info.value.utageKanji}/>
                  </NFormItem>
                  <NFormItem label={t('music.edit.utageComment')}>
                      <NInput v-model:value={info.value.comment}/>
                  </NFormItem>
              </>}
            <NFormItem label={t('music.edit.sortName')}>
              <NPopover trigger="hover">
                {{
                  trigger: () => <NInput v-model:value={info.value!.sortName} class="w-0 grow"/>,
                  default: () => <div>{t('music.edit.sortNameTips')}</div>
                }}
              </NPopover>
            </NFormItem>
            <AcbAwb song={info.value}/>
            <NTabs type="line" animated barWidth={0} v-model:value={selectedLevel.value} class="levelTabs"
                   style={{'--n-tab-padding': 0, '--n-pane-padding-top': 0, '--n-tab-text-color-hover': ''}}>
              {new Array(5).fill(0).map((_, index) =>
                <NTabPane key={index} name={index} tab={DIFFICULTY[index]}>
                  {{
                    tab: () => <Tab index={index} chart={info.value?.charts![index]!} selected={selectedLevel.value === index}/>,
                    default: () => <ChartPanel chart={info.value?.charts![index]!} songId={info.value?.id!} chartIndex={index}
                                               class="pxy pt-2 rounded-[0_0_.5em_.5em]" style={{backgroundColor: `color-mix(in srgb, ${LEVEL_COLOR[index]}, transparent 90%)`}}/>
                  }}
                </NTabPane>
              )}
            </NTabs>
        </NFlex>
    </NForm>;
  },
})

const Tab = defineComponent({
  props: {
    index: {type: Number, required: true},
    chart: {type: Object as PropType<Chart>, required: true},
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
