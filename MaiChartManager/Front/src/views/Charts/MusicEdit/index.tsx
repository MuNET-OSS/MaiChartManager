import { computed, defineComponent, onMounted, PropType, provide, ref, watch } from "vue";
import { addVersionList, genreList, globalCapture, selectedADir, selectedMusic as info, selectMusicId, updateAddVersionList, updateGenreList, updateMusicList, selectedLevel, disableSync, gameVersion, b15ver } from "@/store/refs";
import type { Chart, MusicXmlWithABJacket } from "@/client/apiGen";
import api from "@/client/api";

import JacketBox from "../../../components/JacketBox";
import dxIcon from "@/assets/dxIcon.png";
import stdIcon from "@/assets/stdIcon.png";
import ChartPanel from "./ChartPanel";
import { DIFFICULTY, isUtageId, LEVEL_COLOR, LEVEL_HUE, UTAGE_GENRE, UTAGE_ID_START } from "@/consts";
import ProblemsDisplay from "@/components/ProblemsDisplay";
import AcbAwb from "@/views/Charts/MusicEdit/AcbAwb";
import GenreInput from "@/components/GenreInput";
import VersionInput from "@/components/VersionInput";
import { captureException } from "@sentry/vue"
import noJacket from "@/assets/noJacket.webp";
import { getUrl } from "@/client/api";
import { t } from "@/locales";
import { CheckBox, NumberInput, TextInput, Tabs, TabPane, Popover, showTransactionalDialog } from "@munet/ui";
import { NTabPane, NTabs } from "naive-ui";
import styles from './index.module.sass';

const Component = defineComponent({
  setup() {

    provide('disabled', computed(() => selectedADir.value === 'A000'));

    const firstEnabledChart = info.value?.charts?.findIndex(chart => chart.enable);
    if (firstEnabledChart !== undefined && firstEnabledChart >= 0) {
      selectedLevel.value = firstEnabledChart;
    }
    const isUtage = computed(() => info.value !== undefined &&
      (info.value.genreId === UTAGE_GENRE || isUtageId(info.value.id)));
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

    // 已启用的难度，正常情况下应该只有一个
    const enabledChartIndexes = computed(() => (info.value?.charts ?? [])
      .slice(0, DIFFICULTY.length)
      .map((chart, index) => chart?.enable ? index : -1)
      .filter(index => index >= 0));

    // 取消或失败时，把下拉框里的流派恢复原样
    const revertGenre = (value?: number) => {
      if (!info.value || value === undefined) return;
      info.value.genreId = value;
    };

    // 宴会场只认 Basic（绿）谱面：切换到宴会场时顺带改 ID，并把唯一启用的难度谱面转成 Basic
    const changeGenreToUtage = async (oldGenreId?: number) => {
      if (!info.value) return;
      info.value.genreId = UTAGE_GENRE;
      const enabledIndexes = enabledChartIndexes.value;

      if (enabledIndexes.length !== 1) {
        await showTransactionalDialog(
          t('music.edit.utageConvertTitle'),
          enabledIndexes.length === 0
            ? t('music.edit.utageConvertNoChart')
            : t('music.edit.utageConvertMultipleCharts'),
          undefined, true
        );
        revertGenre(oldGenreId);
        return;
      }

      // 宴会场特有的双人谱（L/R）没法直接转换
      if (info.value.utagePlayStyle === 1) {
        await showTransactionalDialog(
          t('music.edit.utageConvertTitle'),
          t('music.edit.utageConvertDoublePlayerChart'),
          undefined, true
        );
        revertGenre(oldGenreId);
        return;
      }

      const oldId = info.value.id!;
      // 宴会场 ID 由后端挑选（后四位固定、序号顺延），前端只用来生成文案
      const needChangeId = !isUtageId(oldId);

      const enabledIndex = enabledIndexes[0];
      const difficulty = DIFFICULTY[enabledIndex];
      // 本来就只有 Basic 启用的话，不需要再提「把 Basic 转为 Basic」
      const needConvertChart = enabledIndex !== 0;
      const messageKey = needChangeId
        ? (needConvertChart ? 'utageConvertConfirmBoth' : 'utageConvertConfirmIdOnly')
        : (needConvertChart ? 'utageConvertConfirmChartOnly' : 'utageConvertConfirmPlain');
      const confirmed = await showTransactionalDialog(
        t('music.edit.utageConvertTitle'),
        t(`music.edit.${messageKey}`, {
          oldId,
          last4: String(oldId % 1e4).padStart(4, '0'),
          difficulty,
        }),
        [{ text: t('common.cancel'), action: false }, { text: t('common.confirm'), action: true }]
      );
      if (!confirmed) {
        revertGenre(oldGenreId);
        return;
      }

      await requestGenreChange(UTAGE_GENRE, oldGenreId, 'music.edit.utageConvertTitle',
        t('music.edit.utageConvertFailed'));
    };

    // 从宴会场切回其他流派：宴会场 ID 需要减 100000
    const revertGenreFromUtage = async (newGenreId: number, oldGenreId?: number) => {
      if (!info.value) return;
      info.value.genreId = newGenreId;

      // 含宴会场特有 L/R 双人谱的乐曲无法转成普通谱面
      if (info.value.utagePlayStyle === 1) {
        await showTransactionalDialog(
          t('music.edit.utageRevertTitle'),
          t('music.edit.utageConvertDoublePlayerChart'),
          undefined, true
        );
        revertGenre(oldGenreId);
        return;
      }

      const oldId = info.value.id!;

      // 流派是宴会场但 ID 不是宴会场 ID（历史数据、手动改过）时没有可恢复的 ID，直接改流派
      if (!isUtageId(oldId)) {
        info.value.modified = true;
        await api.EditMusicGenre(oldId, info.value.assetDir!, newGenreId);
        return;
      }

      const newId = oldId - UTAGE_ID_START;
      // 序号太大时推不出普通曲 ID，只能让用户手动改
      if (isUtageId(newId)) {
        await showTransactionalDialog(
          t('music.edit.utageRevertTitle'),
          t('music.edit.utageRevertNotRestorable', { oldId }),
          undefined, true
        );
        revertGenre(oldGenreId);
        return;
      }

      const genreName = genreList.value.find(genre => genre.id === newGenreId)?.genreName ?? '';
      const confirmed = await showTransactionalDialog(
        t('music.edit.utageRevertTitle'),
        t('music.edit.utageRevertConfirm', { genreName, oldId, newId }),
        [{ text: t('common.cancel'), action: false }, { text: t('common.confirm'), action: true }]
      );
      if (!confirmed) {
        revertGenre(oldGenreId);
        return;
      }

      await requestGenreChange(newGenreId, oldGenreId, 'music.edit.utageRevertTitle',
        t('music.edit.utageRevertFailed'));
    };

    // 后端返回的错误码 → 文案
    const genreChangeErrorKeys: Record<string, string> = {
      'music-not-found': 'music.edit.utageConvertMusicNotFound',
      'no-enabled-chart': 'music.edit.utageConvertNoChart',
      'multiple-enabled-charts': 'music.edit.utageConvertMultipleCharts',
      'double-player-chart': 'music.edit.utageConvertDoublePlayerChart',
      'target-id-exists': 'music.edit.utageConvertIdExists',
      'no-available-id': 'music.edit.utageConvertNoAvailableId',
      'utage-id-not-restorable': 'music.edit.utageRevertNotRestorable',
      'license-required': 'music.edit.utageConvertLicenseRequired',
      'invalid-music-directory': 'music.edit.utageConvertInvalidMusicDir',
      'invalid-chart-path': 'music.edit.utageConvertInvalidChartPath',
      'chart-file-not-found': 'music.edit.utageConvertChartFileNotFound',
      'swap-failed': 'music.edit.utageConvertSwapFailed',
      'unexpected': 'music.edit.utageConvertUnexpected',
    };

    // 谱面转换 + ID 变更 + 流派变更都由后端一次做完，前端只负责刷新
    const requestGenreChange = async (genreId: number, oldGenreId: number | undefined,
                                       titleKey: string, failMessage: string) => {
      const oldId = info.value!.id!;
      try {
        const result = (await api.ChangeMusicGenre(oldId, selectedADir.value, { genreId })).data;
        if (!result.success) {
          revertGenre(oldGenreId);
          const key = result.error ? genreChangeErrorKeys[result.error] : undefined;
          await showTransactionalDialog(
            t(titleKey),
            key
              ? t(key, { newId: oldId - UTAGE_ID_START, oldId })
              : `${failMessage}${result.error ? ` (${result.error})` : ''}`,
            undefined, true
          );
          return;
        }
        await updateMusicList();
        if (result.newId && result.newId !== oldId) selectMusicId.value = result.newId;
      } catch (e) {
        revertGenre(oldGenreId);
        await globalCapture(e, failMessage);
      }
    };
    // 流派改动的唯一入口。不能靠 watch 拦截：换歌、刷新列表同样会改变 info.value.genreId，
    // watch 会把「上一首曲子的流派」当成这次的旧值，误触发宴会场转换流程。
    const onGenreChange = async (value: number) => {
      if (!info.value || value === undefined || value === info.value.genreId) return;
      const oldGenreId = info.value.genreId;
      if (value === UTAGE_GENRE) {
        await changeGenreToUtage(oldGenreId);
        return;
      }
      if (oldGenreId === UTAGE_GENRE) {
        await revertGenreFromUtage(value, oldGenreId);
        return;
      }
      info.value.genreId = value;
      info.value.modified = true;
      await api.EditMusicGenre(info.value.id!, info.value.assetDir!, value);
    };
    watch(() => info.value?.name, sync('name', api.EditMusicName));
    watch(() => info.value?.artist, sync('artist', api.EditMusicArtist));
    watch(() => info.value?.bpm, sync('bpm', api.EditMusicBpm));
    watch(() => info.value?.version, sync('version', api.EditMusicVersion));
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
            <GenreInput options={genreList.value} value={info.value.genreId} onUpdate:value={onGenreChange}/>
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
        <NTabs type="line" animated barWidth={0} v-model:value={selectedLevel.value} class={["levelTabs", isUtage.value && styles.utageTabs]}
               style={{ '--n-tab-padding': 0, '--n-pane-padding-top': 0, '--n-tab-text-color-hover': '' }}>
          {chartIndexes.value.map(index =>
            <NTabPane key={index} name={index} tab={DIFFICULTY[index]}>
              {{
                tab: () => <Tab index={index} chart={info.value?.charts![index]!} selected={selectedLevel.value === index}/>,
              default: () => <ChartPanel chart={info.value?.charts![index]!} songId={info.value?.id!} chartIndex={index}
                                           doublePlayer={info.value?.utagePlayStyle === 1}
                                           class={["pxy pt-2", isUtage.value ? 'rounded-[.5em]' : 'rounded-[0_0_.5em_.5em]']}
                                           style={{ backgroundColor: `color-mix(in srgb, ${isUtage.value ? '#E434B4' : LEVEL_COLOR[index]}, transparent 90%)`, '--hue': isUtage.value ? '342' : LEVEL_HUE[index] }}/>
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
