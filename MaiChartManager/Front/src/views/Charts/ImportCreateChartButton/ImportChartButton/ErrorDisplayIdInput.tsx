import { computed, defineComponent, PropType, useId, watch } from "vue";
import { Button, CheckBox, Modal, NumberInput, Popover, Section, Select } from "@munet/ui";
import type { ImportChartMessageEx, ImportMeta, SavedOptions, TempOptions } from "./types";
import noJacket from '@/assets/noJacket.webp';
import { addVersionList, genreList, showNeedPurchaseDialog } from "@/store/refs";
import GenreInput from "@/components/GenreInput";
import VersionInput from "@/components/VersionInput";
import { UTAGE_GENRE } from "@/consts";
import MusicIdConflictNotifier from "@/components/MusicIdConflictNotifier";
import { useI18n } from 'vue-i18n';
import ImportAlert from "@/views/Charts/ImportCreateChartButton/ImportChartButton/ImportAlert";
import ShiftModeSelector from "@/views/Charts/ImportCreateChartButton/ImportChartButton/ShiftModeSelector";

export default defineComponent({
  props: {
    show: {type: Boolean, required: true},
    meta: {type: Array as PropType<ImportMeta[]>, required: true},
    tempOptions: {type: Object as PropType<TempOptions>, required: true},
    savedOptions: {type: Object as PropType<SavedOptions>, required: true},
    closeModal: {type: Function, required: true},
    proceed: {type: Function as PropType<() => any>, required: true},
    errors: {type: Array as PropType<ImportChartMessageEx[]>, required: true}
  },
  setup(props, {emit}) {
    const { t } = useI18n();

    const show = computed({
      get: () => props.show,
      set: (val) => props.closeModal()
    })

    watch([() => props.savedOptions.genreId, () => show.value], ([val]) => {
      for (const meta of props.meta) {
        meta.id = meta.id % 1e5 + (val === UTAGE_GENRE ? 1e5 : 0);
      }
    })

    const hasInvalidUtageMapping = computed(() => props.meta.some(item => {
      const isUtage = props.savedOptions.genreId === UTAGE_GENRE || item.id >= 1e5;
      if (!isUtage || item.maidataLevels.length < 2) return false;
      const mapping = item.utageMapping;
      return !mapping || mapping.isDoublePlayer && mapping.leftLevel === mapping.rightLevel;
    }));

    return () => <Modal
      width="min(90vw,50em)"
      innerClass="max-h-[90dvh] of-hidden"
      title={t('chart.import.importPrompt')}
      v-model:show={show.value}
    >{{
      default: () => <div class="max-h-[calc(90dvh-10rem)] of-y-auto cst pr-1 space-y-3">
        <ImportAlert errors={props.errors} tempOptions={props.tempOptions}></ImportAlert>
        {!!props.meta.length && <>
            <div>{t('chart.import.assignId')}</div>
            <div class="of-y-auto cst max-h-[50dvh]">
                <div class="flex flex-col gap-3">
                  {props.meta.map((meta, i) => <MusicIdInput key={i} meta={meta} utage={props.savedOptions.genreId === UTAGE_GENRE}/>)}
                </div>
            </div>
            <div>
              <div class="ml-1 text-sm">{t('music.edit.genre')}</div>
              <GenreInput options={genreList.value} v-model:value={props.savedOptions.genreId}/>
            </div>
            <div>
              <div class="ml-1 text-sm">{t('music.edit.versionCategory')}</div>
              <GenreInput options={addVersionList.value} v-model:value={props.savedOptions.addVersionId}/>
            </div>
            <div>
              <div class="ml-1 text-sm">{t('music.edit.version')}</div>
              <VersionInput v-model:value={props.savedOptions.version}/>
            </div>
            <CheckBox v-model:value={props.tempOptions.ignoreLevel}>
                {t('settings.ignoreLevel')}
            </CheckBox>
            <CheckBox v-model:value={props.tempOptions.disableBga}>
                {t('settings.disableBga')}
            </CheckBox>
            <Section title={t('chart.import.option.advancedOptions')}>
                <ShiftModeSelector tempOptions={props.tempOptions}></ShiftModeSelector>
                <div class="flex items-center gap-1" style="margin-top: 0.25rem">
                  <CheckBox v-model:value={props.tempOptions.ignoreGapless}>{t('chart.import.option.ignoreGapless')}</CheckBox>
                  <Popover trigger="hover">
                    {{
                      trigger: () => <div class="i-material-symbols:info-outline-rounded op-50"/>,
                      default: () => <div class="max-w-60">{t('chart.import.option.ignoreGaplessTip')}</div>
                    }}
                  </Popover>
                </div>
            </Section>
        </>}
      </div>,
        actions: () => <>
          <Button class="w-0 grow" onClick={() => show.value = false}>{props.meta.length ? t('common.cancel') : t('common.close')}</Button>
          {!!props.meta.length && <Button class="w-0 grow" disabled={hasInvalidUtageMapping.value} onClick={props.proceed}>{t('purchase.continue')}</Button>}
        </>
    }}</Modal>;
  }
})

const MusicIdInput = defineComponent({
  props: {
    meta: {type: Object as PropType<ImportMeta>, required: true},
    utage: {type: Boolean, required: true},
  },
  setup(props) {
    const { t } = useI18n();
    const dxBase = computed(() => {
      const dx = props.meta.isDx ? 1e4 : 0
      const utage = props.utage ? 1e5 : 0
      return dx + utage;
    });
    const img = computed(() => props.meta.bg ? URL.createObjectURL(props.meta.bg) : noJacket);
    const isUtage = computed(() => props.utage || props.meta.id >= 1e5);
    const levelOptions = computed(() => props.meta.maidataLevels.map(level => ({
      label: `inote_${level}`,
      value: level,
    })));
    const playStyleInputName = useId();

    return () => <div class="flex flex-col gap-3 px of-hidden">
      <div class="flex flex-wrap gap-3 items-center">
        <img src={img.value} class="h-16 w-16 object-fill shrink-0"/>
        <div class="min-w-28 grow">{props.meta.name}</div>
        <MusicIdConflictNotifier id={props.meta.id}/>
        <NumberInput v-model:value={props.meta.id} min={dxBase.value + 1} max={119999} step={1} class="w-full sm:w-auto shrink-0"/>
      </div>
      {isUtage.value && props.meta.maidataLevels.length >= 2 && props.meta.utageMapping &&
        <div class="flex flex-col gap-3 border-t border-t-solid border-gray/20 pt-3 sm:ml-19">
          <div class="flex flex-wrap gap-x-5 gap-y-2">
            <label class="flex gap-2 items-center">
              <input type="radio" name={playStyleInputName} checked={!props.meta.utageMapping.isDoublePlayer}
                     onChange={() => props.meta.utageMapping!.isDoublePlayer = false}/>
              <span>{t('chart.import.utage.singlePlayer')}</span>
            </label>
            <label class="flex gap-2 items-center">
              <input type="radio" name={playStyleInputName} checked={props.meta.utageMapping.isDoublePlayer}
                     onChange={() => props.meta.utageMapping!.isDoublePlayer = true}/>
              <span>{t('chart.import.utage.doublePlayer')}</span>
            </label>
          </div>
          {props.meta.utageMapping.isDoublePlayer
            ? <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div class="flex flex-col gap-1">
                  <div class="text-sm">{t('chart.import.utage.leftChart')}</div>
                  <Select options={levelOptions.value} v-model:value={props.meta.utageMapping.leftLevel}/>
                </div>
                <div class="flex flex-col gap-1">
                  <div class="text-sm">{t('chart.import.utage.rightChart')}</div>
                  <Select options={levelOptions.value} v-model:value={props.meta.utageMapping.rightLevel}/>
                </div>
                {props.meta.utageMapping.leftLevel === props.meta.utageMapping.rightLevel &&
                  <div class="sm:col-span-2 text-sm text-red">{t('chart.import.utage.sameChartError')}</div>}
              </div>
            : <div class="flex flex-col gap-1">
                <div class="text-sm">{t('chart.import.utage.basicChart')}</div>
                <Select options={levelOptions.value} v-model:value={props.meta.utageMapping.basicLevel}/>
              </div>}
        </div>}
    </div>
  }
})
