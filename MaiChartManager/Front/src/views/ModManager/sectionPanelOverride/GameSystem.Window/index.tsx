import { computed, defineComponent, PropType } from 'vue';
import { IEntryState, ISectionState, Section } from "@/client/apiGen";
import { WhateverNaviBar } from '@munet/ui';

const WINDOW_PREFIX = 'GameSystem.Window.';

interface Preset {
  name: string;
  width: number;
  height: number;
}

const presets: Preset[] = [
  { name: '720P 1P', width: 720, height: 1280 },
  { name: '720P 2P', width: 1440, height: 1280 },
  { name: '1080P 1P', width: 1080, height: 1920 },
  { name: '1080P 2P', width: 2160, height: 1920 },
  { name: '2K 1P', width: 1440, height: 2560 },
  { name: '2K 2P', width: 2880, height: 2560 },
  { name: '4K 1P', width: 2160, height: 3840 },
  { name: '4K 2P', width: 4320, height: 3840 },
];

export default defineComponent({
  props: {
    section: { type: Object as PropType<Section>, required: true },
    entryStates: { type: Object as PropType<Record<string, IEntryState>>, required: true },
    sectionState: { type: Object as PropType<ISectionState>, required: true },
    allSectionStates: { type: Object as PropType<Record<string, ISectionState>> },
  },
  setup(props) {
    const activePreset = computed(() => {
      for (const preset of presets) {
        const widthEntry = props.entryStates[WINDOW_PREFIX + 'Width'];
        const heightEntry = props.entryStates[WINDOW_PREFIX + 'Height'];
        if (widthEntry && heightEntry &&
            Number(widthEntry.value) === preset.width &&
            Number(heightEntry.value) === preset.height) {
          return preset.name;
        }
      }
      return null;
    });

    const applyPreset = (preset: Preset) => {
      const widthEntry = props.entryStates[WINDOW_PREFIX + 'Width'];
      const heightEntry = props.entryStates[WINDOW_PREFIX + 'Height'];
      if (widthEntry) widthEntry.value = preset.width;
      if (heightEntry) heightEntry.value = preset.height;
      props.sectionState.enabled = true;
    };

    const naviItems = computed(() => presets.map(preset => ({
      name: preset.name,
      selected: activePreset.value === preset.name,
      onClick: () => applyPreset(preset),
    })));

    return () => <div class="flex flex-col gap-2">
      <div class="pl-40 flex items-center gap-2">
        预设:
        <WhateverNaviBar items={naviItems.value}/>
      </div>
    </div>;
  },
});
