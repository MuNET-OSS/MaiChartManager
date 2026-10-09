import { IEntryState, Entry } from "@/client/apiGen";
import { Select, CheckBox, TextInput, NumberInput } from '@munet/ui';
import { computed, defineComponent, PropType } from "vue";
import ProblemsDisplay from "../../components/ProblemsDisplay";
import { KeyCodeName } from "./types/KeyCodeName";
import { getNameForPath } from "./utils";
import comments from "./modComments.yaml";
import { KeyCodeID } from "./types/KeyCodeID";
import { VKCode } from "./types/VKCode";
import { useI18n } from 'vue-i18n';
import { locale } from "@/locales";
import { ENTRY_LABEL_CLASS } from "./constants";

/* t需要传入是Vue的i18n翻译函数 */
export function optionsIoKeyMap(t: (key: string) => string): { label: string, value: string }[] {
  return [
    { label: t('mod.ioKeyMap.disabled'), value: 'None' },
    { label: t('mod.ioKeyMap.select'), value: 'Select' },
    { label: t('mod.ioKeyMap.select1P'), value: 'Select1P' },
    { label: t('mod.ioKeyMap.select2P'), value: 'Select2P' },
    { label: t('mod.ioKeyMap.service'), value: 'Service' },
    { label: t('mod.ioKeyMap.test'), value: 'Test' },
    { label: t('mod.ioKeyMap.customFn') + "1", value: 'CustomFn1' },
    { label: t('mod.ioKeyMap.customFn') + "2", value: 'CustomFn2' },
    { label: t('mod.ioKeyMap.customFn') + "3", value: 'CustomFn3' },
    { label: t('mod.ioKeyMap.customFn') + "4", value: 'CustomFn4' },
  ];
}

export function optionsKeyCodeOrName(t: (key: string) => string): { label: string, value: string }[] {
  return Object.entries(KeyCodeName).map(([label, value]) => {
    if (label.startsWith("CustomFn")) { // "自定义功能键"需要单独i18n
      const suffix = label.slice('CustomFn'.length);
      return { label: t('mod.ioKeyMap.customFn') + suffix, value };
    }
    return { label, value }; // 其他的则沿用枚举中的值
  });
}

export function optionsVKCode(t: (key: string) => string): { label: string, value: string }[] {
  return Object.entries(VKCode).map(([label, value]) => {
    if (label == "None") { // 单独翻译为“禁用”
      return { label: t('mod.disable'), value };
    }
    return { label, value }; // 其他的则沿用枚举中的值
  });
}

export default defineComponent({
  props: {
    entry: { type: Object as PropType<Entry>, required: true },
    entryState: { type: Object as PropType<IEntryState>, required: true },
  },
  setup(props, { emit }) {
    const { t, te } = useI18n();

    const optionsSoundChannel = ['None', 'P1SpeakerLeft', 'P1SpeakerRight', 'P1HeadphoneLeft', 'P1HeadphoneRight', 'P2SpeakerLeft', 'P2SpeakerRight', 'P2HeadphoneLeft', 'P2HeadphoneRight']
      .map(channel => ({ label: t('mod.soundChannel.' + channel), value: channel }));

    const comment = computed(() => {
      const localeKey = 'mod.commentOverrides.' + props.entry.path!.replace(/\./g, '_');
      if (te(localeKey)) {
        return t(localeKey);
      }
      if (locale.value.startsWith('zh')) {
        return props.entry.attribute?.comment?.commentZh;
      }
      return props.entry.attribute?.comment?.commentEn;
    })

    return () => <div class="flex gap-2 items-start"
                      title={props.entry.path!}
    >
      <div class={ENTRY_LABEL_CLASS}>{getNameForPath(props.entry.path!, props.entry.name!, props.entry.attribute?.comment?.nameZh)}</div>
      <div class="flex flex-col gap-2 w-full ws-pre-line">
        <div class="flex gap-2 h-42px items-center">
          {(() => {
            const choices = comments.options[props.entry.path!]
            if (choices) {
              return <Select v-model:value={props.entryState.value} options={choices}/>
            }
            switch (props.entry.fieldType) {
              case 'System.Boolean':
                return <CheckBox v-model:value={props.entryState.value}>{props.entryState.value ? '开' : '关'}</CheckBox>;
              case 'System.String':
                return <TextInput class="w-full" innerClass="h-42px!" v-model:value={props.entryState.value} placeholder=""/>;
              case 'System.Int32':
              case 'System.Int64':
                return <NumberInput innerClass="h-42px!" v-model:value={props.entryState.value} decimal={0} step={1}/>;
              case 'System.UInt32':
              case 'System.UInt64':
                return <NumberInput innerClass="h-42px!" v-model:value={props.entryState.value} decimal={0} step={1} min={0}/>;
              case 'System.Byte':
                return <NumberInput innerClass="h-42px!" v-model:value={props.entryState.value} decimal={0} step={1} min={0} max={255}/>;
              case 'System.UInt16':
                return <NumberInput innerClass="h-42px!" v-model:value={props.entryState.value} decimal={0} step={1} min={0} max={65535}/>;
              case 'System.Int16':
                return <NumberInput innerClass="h-42px!" v-model:value={props.entryState.value} decimal={0} step={1} min={-32768} max={32767}/>;
              case 'System.Double':
              case 'System.Single':
                return <NumberInput innerClass="h-42px!" v-model:value={props.entryState.value} step={.1} decimal={4}/>;
              case 'AquaMai.Config.Types.KeyCodeOrName':
                return <Select v-model:value={props.entryState.value} options={optionsKeyCodeOrName(t)}/>;
              case 'AquaMai.Config.Types.KeyCodeID':
                return <Select v-model:value={props.entryState.value} options={Object.entries(KeyCodeID).map(([label, value]) => ({label, value}))}/>;
              case 'AquaMai.Config.Types.IOKeyMap':
              case 'AquaMai.Config.Types.AdxKeyMap':
                return <Select v-model:value={props.entryState.value} options={optionsIoKeyMap(t)}/>;
              case 'AquaMai.Config.Types.VKCode':
                return <Select v-model:value={props.entryState.value} options={optionsVKCode(t)}/>;
              case 'AquaMai.Config.Types.SoundChannel':
                return <Select v-model:value={props.entryState.value} options={optionsSoundChannel}/>;
            }
            return t('mod.unsupportedType', { type: props.entry.fieldType });
          })()}
          {comments.shouldEnableOptions[props.entry.path!] && !props.entryState.value && <ProblemsDisplay problems={[t('mod.needEnableOption')]}/>}
        </div>
        <div class="text-sm op-80">{comment.value}</div>
      </div>
    </div>;
  },
});
