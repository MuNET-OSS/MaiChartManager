import { defineComponent, PropType, ref, computed, h } from 'vue';
import { IEntryState, ISectionState, Section } from "@/client/apiGen";
import { Button, Select } from '@munet/ui';
import api from "@/client/api";
import { modInfo, updateModInfo } from "@/store/refs";
import { useI18n } from 'vue-i18n';
import ConfigEntry, { optionsIoKeyMap } from '../../ConfigEntry';
import { ENTRY_GROUP_PADDING, ENTRY_LABEL_CLASS } from '../../constants';

export default defineComponent({
  props: {
    section: { type: Object as PropType<Section>, required: true },
    entryStates: { type: Object as PropType<Record<string, IEntryState>>, required: true },
    sectionState: { type: Object as PropType<ISectionState>, required: true },
  },
  setup(props, { emit }) {
    const load = ref(false)
    const { t } = useI18n();


    const PREFIX = 'GameSystem.AdxHidInput.';
    const pathsLeft = [
      'Button1', 'Button2',
      'P1Button1', 'P1Button2', 'P1Button3', 'P1Button4', 'P1DisableButtons',
    ].map(it => PREFIX + it);

    const pathsRight = [
      'Button3', 'Button4',
      'P2Button1', 'P2Button2', 'P2Button3', 'P2Button4', 'P2DisableButtons',
    ].map(it => PREFIX + it);

    const knownPaths = [
      ...pathsLeft, ...pathsRight,
    ];

    const del = async () => {
      load.value = true
      await api.DeleteHidConflict();
      await updateModInfo();
      load.value = false
    }

    return () => <div class={["flex flex-col gap-2", ENTRY_GROUP_PADDING]}>
      {modInfo.value?.isHidConflictExist ? <div class="flex gap-2 items-center m-l-35">
        <span class="c-orange">{t('mod.adxHid.conflictDetected')}</span>
        <Button variant="secondary" onClick={del} ing={load.value}>{t('mod.adxHid.oneClickDelete')}</Button>
      </div>
        : <div class="flex gap-2 items-center m-l-35">
          <span class="c-green-6">{t('mod.adxHid.noConflict')}</span>
        </div>}
      <div class="grid grid-cols-1 min-[500px]:grid-cols-2 gap-y-12px">
        <div class="flex flex-col gap-2">
          {props.section.entries?.filter(it => pathsLeft.includes(it.path!))
            .map((entry) => <ConfigEntry key={entry.path!} entry={entry} entryState={props.entryStates[entry.path!]} />)}
        </div>
        <div class="flex flex-col gap-2">
          {props.section.entries?.filter(it => pathsRight.includes(it.path!))
            .map((entry) => <ConfigEntry key={entry.path!} entry={entry} entryState={props.entryStates[entry.path!]} />)}
        </div>
      </div>
      {props.section.entries?.filter(it => !knownPaths.includes(it.path!))
        .map((entry) => <ConfigEntry key={entry.path!} entry={entry} entryState={props.entryStates[entry.path!]} />)}
    </div>
  },
});
