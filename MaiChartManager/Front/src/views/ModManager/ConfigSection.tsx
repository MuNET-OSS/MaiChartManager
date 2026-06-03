import { IEntryState, ISectionState, Section } from '@/client/apiGen';
import { defineComponent, PropType, ref, computed, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { getNameForPath, getSectionPanelOverride } from './utils';
import comments from "./modComments.yaml";
import { locale } from '@/locales';
import { CheckBox, Popover, TransitionVertical } from '@munet/ui';
import ProblemsDisplay from '@/components/ProblemsDisplay';
import ConfigEntry from './ConfigEntry';
import { ENTRY_GROUP_PADDING } from './constants';

export default defineComponent({
  props: {
    section: { type: Object as PropType<Section>, required: true },
    entryStates: { type: Object as PropType<Record<string, IEntryState>>, required: true },
    sectionState: { type: Object as PropType<ISectionState>, required: true },
    allSectionStates: { type: Object as PropType<Record<string, ISectionState>> },
    isCommunity: Boolean,
    isFavorite: Boolean,
    toggleFavorite: Function as PropType<() => void>,
  },
  setup(props, { emit }) {
    const { t, te } = useI18n();

    const CustomPanel = getSectionPanelOverride(props.section.path!)
    const customPanelPosition: 'top' | 'bottom' | 'override' = comments.customPanelPosition[props.section.path!] || 'override';
    const comment = computed(() => {
      const localeKey = 'mod.commentOverrides.' + props.section.path!.replace(/\./g, '_');
      if (te(localeKey)) {
        return t(localeKey);
      }
      if (locale.value.startsWith('zh')) {
        return props.section.attribute?.comment?.commentZh;
      }
      return props.section.attribute?.comment?.commentEn;
    })

    const resetSection = () => {
      props.section.entries?.forEach(entry => {
        const state = props.entryStates[entry.path!];
        if (state) {
          state.value = state.defaultValue;
          state.isDefault = true;
        }
      });
    };

    const isEnabled = computed(() => props.section.attribute!.alwaysEnabled || props.sectionState.enabled);

    const favoriteIcon = () => (
      <div
        class={[
          props.isFavorite ? 'i-material-symbols:star-rounded c-yellow-4 opacity-90 hover:opacity-100' : 'i-material-symbols:star-outline-rounded opacity-0 group-hover:opacity-50 hover:opacity-80',
          'text-lg cursor-pointer transition-opacity shrink-0',
        ]}
        title={props.isFavorite ? t('mod.removeFavorite') : t('mod.addFavorite')}
        onClick={props.toggleFavorite}
      />
    );

    const resetIcon = () => isEnabled.value ? (
      <div
        class="i-carbon:reset text-lg cursor-pointer opacity-0 group-hover:opacity-50 hover:opacity-80 transition-opacity shrink-0"
        title={t('mod.resetToDefault')}
        onClick={resetSection}
      />
    ) : null;

    return () => <div class={["flex flex-col p-1 border-transparent border-solid border-1px rd hover:border-[oklch(0.68_0.17_var(--hue))] group"]}>
      {!props.section.attribute!.alwaysEnabled && <div class="flex gap-2 items-start"
        // @ts-ignore
                                                       title={props.section.path!}
      >
        <div class="ml-1 text-lg w-9em shrink-0">{getNameForPath(props.section.path!, props.section.path!.split('.').pop()!, props.section.attribute?.comment?.nameZh)}</div>
        <div class="flex flex-col gap-2 w-full ws-pre-line">
          <div class="flex gap-2 h-28px items-center">
            <CheckBox v-model:value={props.sectionState.enabled}>{props.sectionState.enabled ? '开' : '关'}</CheckBox>
            {comments.shouldEnableOptions[props.section.path!] && !props.sectionState.enabled && <ProblemsDisplay problems={[t('mod.needEnableOption')]}/>}
            {props.isCommunity && <Popover trigger="hover">{{
              trigger: () => <div class="i-ic-baseline-info text-lg c-neutral-5"/>,
              default: () => <div>
                <div class="text-lg mb-2">{t('mod.community.title')}</div>
                <div class="text-sm whitespace-pre-line lh-1.7em">{t('mod.community.description')}</div>
              </div>
            }}</Popover>}
            <div class="flex-1" />
            {favoriteIcon()}
            {resetIcon()}
          </div>
          <div class="text-sm op-80">{comment.value}</div>
        </div>
      </div>}
      <TransitionVertical>
        {props.sectionState.enabled && <div class="flex flex-col gap-2 mt-2">
          {customPanelPosition === 'top' && <CustomPanel entryStates={props.entryStates} sectionState={props.sectionState} section={props.section} allSectionStates={props.allSectionStates}/>}
          {(CustomPanel && customPanelPosition === 'override') ?
            <CustomPanel entryStates={props.entryStates} sectionState={props.sectionState} section={props.section} allSectionStates={props.allSectionStates}/> :
            !!props.section.entries?.length && <div class={["flex flex-col gap-2", ENTRY_GROUP_PADDING]}>
              {props.section.entries?.filter(it => !it.attribute?.hideWhenDefault || (it.attribute?.hideWhenDefault && !props.entryStates[it.path!].isDefault))
                .map((entry) => <ConfigEntry key={entry.path!} entry={entry} entryState={props.entryStates[entry.path!]}/>)}
            </div>}
          {customPanelPosition === 'bottom' && <CustomPanel entryStates={props.entryStates} sectionState={props.sectionState} section={props.section} allSectionStates={props.allSectionStates}/>}
          </div>}
      </TransitionVertical>
    </div>;
  },
});
