import { computed, defineComponent, PropType } from "vue";
import { GenreXml } from "@/client/apiGen";
import { Select } from "@munet/ui";

export default defineComponent({
  props: {
    options: {type: Array as PropType<GenreXml[]>, required: true},
    value: Number
  },
  // 声明 emits 才能在 TSX 里用 onUpdate:value 精确拦截用户的选择
  emits: ['update:value'],
  setup(props, {emit}) {
    const options = computed(() => props.options.map(genre => ({label: ()=><GenreOption genre={props.options.find(it => it.id === genre.id)!}/>, value: genre.id})));
    const value = computed({
      get: () => props.value,
      set: (v) => emit('update:value', v)
    })

    return () => <Select options={options.value as any} v-model:value={value.value} />
  }
})

export const GenreOption = defineComponent({
  props: {
    genre: {type: Object as PropType<GenreXml>},
  },
  setup(props) {
    return () => <div class="flex gap-2 items-center">
      <div class="h-4 w-4 rounded-full" style={{backgroundColor: props.genre ? `rgb(${props.genre.colorR}, ${props.genre.colorG}, ${props.genre.colorB})` : 'white'}}/>
      {props.genre ? props.genre.genreName : '???'}
    </div>;
  },
})
