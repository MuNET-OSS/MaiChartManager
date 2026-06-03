import api from '@/client/api';
import { Entry, IEntryState, ISectionState, Section } from '@/client/apiGen';
import { t } from '@/locales';
import { globalCapture, modInfo, updateModInfo } from '@/store/refs';
import { useAsyncState } from '@vueuse/core';
import { Button, Select, CheckBox } from '@munet/ui';
import { defineComponent, PropType, ref, computed, watch } from 'vue';
import ConfigEntry, {optionsIoKeyMap} from '../../ConfigEntry';
import { ENTRY_GROUP_PADDING, ENTRY_LABEL_CLASS } from '../../constants';

export default defineComponent({
  props: {
    section: { type: Object as PropType<Section>, required: true },
    entryStates: {type: Object as PropType<Record<string, IEntryState>>, required: true},
    sectionState: {type: Object as PropType<ISectionState>, required: true},
  },
  setup(props, { emit }) {
    const install = useAsyncState(async () => {
      await api.InstallMmlLibs({ useLegacy: isLegacyModeEnabled.value });
      await updateModInfo();
    }, undefined, {
      immediate: false,
      onError(e) {
        globalCapture(e, t('mod.mmlIo.installFailed'));
      },
    })

    const options = optionsIoKeyMap(t);

    const PREFIX = 'GameSystem.MaimollerIO.';
    const useLegacyEntryPath = PREFIX + 'UseLegacy';

    const pathsLeft = [
      'Button1', 'Button2',
      'P1Button1', 'P1Button2', 'P1Button3', 'P1Button4',
    ].map(it => PREFIX + it);

    const pathsRight = [
      'Button3', 'Button4',
      'P2Button1', 'P2Button2', 'P2Button3', 'P2Button4',
    ].map(it => PREFIX + it);

    const knownPaths = [
      ...['P1', 'Touch1p', 'Button1p', 'Led1p', 'P2', 'Touch2p', 'Button2p', 'Led2p'].map(it => PREFIX + it),
      ...pathsLeft, ...pathsRight,
    ];

    const isLegacyModeEnabled = computed(() => {
      const legacyEntry = props.entryStates[useLegacyEntryPath];
      if (!legacyEntry) {
        return true;
      }
      return Boolean(legacyEntry.value);
    });

    const isMmlRequirementSatisfied = computed(() => {
      const isAdxHidIoModAbsent = Boolean(modInfo.value?.isAdxHidIoModAbsent);
      if (!isAdxHidIoModAbsent) {
        return false;
      }
      if (!isLegacyModeEnabled.value) {
        return true;
      }
      return Boolean(modInfo.value?.isMmlLegacyLibsInstalled);
    });

    return () => <div class={["flex flex-col gap-2", ENTRY_GROUP_PADDING]}>
      {!isMmlRequirementSatisfied.value ? <div class="flex gap-2 items-center m-l-35">
        <span class="c-orange">{t('mod.mmlIo.notInstalled')}</span>
        <Button variant="secondary" onClick={() => install.execute()} ing={install.isLoading.value}>{t('mod.mmlIo.oneClickInstall')}</Button>
      </div>
        : <div class="flex gap-2 items-center m-l-35">
          <span class="c-green-6">{t('mod.mmlIo.installed')}</span>
        </div>}
      <div class="m-l-35 op-80">
        {t('mod.mmlIo.tip')}
      </div>
      <div class="grid grid-cols-1 min-[1300px]:grid-cols-2 gap-y-12px">
        <div class="flex flex-col gap-2">
          {props.section.entries?.some(it => it.path === PREFIX + 'P1') &&
            <div class="flex gap-2 items-start">
              <div class={ENTRY_LABEL_CLASS}>{t(`mod.mmlIo.P1`)}</div>
              <div class="flex h-42px items-center">
                <CheckBox v-model:value={props.entryStates[PREFIX + 'P1'].value}>{props.entryStates[PREFIX + 'P1'].value ? t('mod.on') : t('mod.mmlIo.off')}</CheckBox>
              </div>
            </div>
          }
          {props.section.entries?.some(it => it.path === PREFIX + 'Touch1p') &&
            <div class="flex gap-2 items-start">
              <div class={ENTRY_LABEL_CLASS}>1P</div>
              <div class="flex gap-4 h-42px items-center">
                {['Touch1p', 'Button1p', 'Led1p'].map(key =>
                  <CheckBox v-model:value={props.entryStates[PREFIX + key].value}>{props.entryStates[PREFIX + key].value ? t('mod.enable') : t('mod.disable')}{t(`mod.mmlIo.${key.slice(0, -2)}`)}</CheckBox>
                )}
              </div>
            </div>
          }
          {props.section.entries?.filter(it => pathsLeft.includes(it.path!))
            .map((entry) => <ConfigEntry key={entry.path!} entry={entry} entryState={props.entryStates[entry.path!]} />)}
        </div>
        <div class="flex flex-col gap-2">
          {props.section.entries?.some(it => it.path === PREFIX + 'P2') &&
            <div class="flex gap-2 items-start">
              <div class={ENTRY_LABEL_CLASS}>{t(`mod.mmlIo.P2`)}</div>
              <div class="flex h-42px items-center">
                <CheckBox v-model:value={props.entryStates[PREFIX + 'P2'].value}>{props.entryStates[PREFIX + 'P2'].value ? t('mod.on') : t('mod.mmlIo.off')}</CheckBox>
              </div>
            </div>
          }
          {props.section.entries?.some(it => it.path === PREFIX + 'Touch2p') &&
            <div class="flex gap-2 items-start">
              <div class={ENTRY_LABEL_CLASS}>2P</div>
              <div class="flex gap-4 h-42px items-center">
                {['Touch2p', 'Button2p', 'Led2p'].map(key =>
                  <CheckBox v-model:value={props.entryStates[PREFIX + key].value}>{props.entryStates[PREFIX + key].value ? t('mod.enable') : t('mod.disable')}{t(`mod.mmlIo.${key.slice(0, -2)}`)}</CheckBox>
                )}
              </div>
            </div>
          }
          {props.section.entries?.filter(it => pathsRight.includes(it.path!))
            .map((entry) => <ConfigEntry key={entry.path!} entry={entry} entryState={props.entryStates[entry.path!]} />)}
        </div>
      </div>
      {props.section.entries?.filter(it => !knownPaths.includes(it.path!))
        .map((entry) => <ConfigEntry key={entry.path!} entry={entry} entryState={props.entryStates[entry.path!]}/>)}
    </div>;
  },
});
