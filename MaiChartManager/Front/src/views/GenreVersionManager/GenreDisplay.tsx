import { computed, defineComponent, PropType, ref } from "vue";
import { GenreXml, HttpResponse } from "@/client/apiGen";
import { Button, showTransactionalDialog } from "@munet/ui";
import api from "@/client/api";
import { updateAddVersionList, updateGenreList } from "@/store/refs";
import Color from "color";
import SetImageButton from "./SetImageButton";
import { EDIT_TYPE } from "./index";
import { useI18n } from 'vue-i18n';

export default defineComponent({
  props: {
    genre: {type: Object as PropType<GenreXml>, required: true},
    editing: Boolean,
    disabled: Boolean,
    setEdit: {type: Function as PropType<(edit: boolean) => void>, required: true},
    type: Number as PropType<EDIT_TYPE>,
  },
  setup(props) {
    const _color = ref('');
    const color = computed({
      get: () => _color.value || Color([props.genre.colorR, props.genre.colorG, props.genre.colorB]).hex(),
      set: value => _color.value = value,
    })
    const { t } = useI18n();

    const save = async () => {
      props.setEdit(false);
      let newColor = [props.genre.colorR, props.genre.colorG, props.genre.colorB];
      if (_color.value) {
        newColor = Color(_color.value).rgb().array();
      }
      await (props.type === EDIT_TYPE.Genre ? api.EditGenre : api.EditVersion)(props.genre.id!, {
        name: props.genre.genreName,
        nameTwoLine: props.genre.genreNameTwoLine,
        r: newColor[0],
        g: newColor[1],
        b: newColor[2],
      });
      updateGenreList();
      updateAddVersionList();
      _color.value = '';
    }

    const del = async () => {
      deleteLoad.value = true;
      let res: HttpResponse<any>;
      if (props.type === EDIT_TYPE.Genre) {
        res = await api.DeleteGenre(props.genre.id!);
      } else {
        res = await api.DeleteVersion(props.genre.id!);
      }
      if (res.error) {
        const error = res.error as any;
        await showTransactionalDialog(t('music.delete.deleteFailed'), error.message || error, undefined, true);
        return;
      }
      updateGenreList();
      updateAddVersionList();
    }

    const confirmDelete = ref(false);
    const deleteLoad = ref(false);

    const Buttons = defineComponent({
      render() {
        if (props.genre.assetDir === 'A000')
          return <div class="i-material-symbols-edit-off text-6 c-gray-6"/>
        if (props.editing)
          return <Button variant="primary" onClick={save}><span class="i-material-symbols-done text-6 c-gray-6"/></Button>
        if (confirmDelete.value)
          return <Button danger={!deleteLoad.value} variant="secondary" onClick={del} ing={deleteLoad.value}
                          onMouseleave={() => confirmDelete.value = false}>
            {!deleteLoad.value && <span class="i-material-symbols-delete-outline text-6 c-gray-6"/>}
          </Button>
        return <div class="flex gap-2">
          <Button class="w-0 grow-1" variant="secondary" onClick={() => props.setEdit(true)}>
            <span class="i-material-symbols-edit text-6 c-gray-6"/>
          </Button>
          <Button class="w-0 grow-1" variant="secondary" onClick={() => confirmDelete.value = true}>
            <span class="i-material-symbols-delete-outline text-6 c-gray-6"/>
          </Button>
        </div>
      }
    })

    return () => (
      <div class="grid cols-[10em_2em_8em_1.3fr_1fr_7em] items-center gap-5 m-x">
        <div class="flex gap-1 c-gray-6">
          {props.genre.id}
          <span class="op-60">@</span>
          <span class="op-80">{props.genre.assetDir}</span>
        </div>
        <div class="h-6 w-6 rounded-full relative of-clip" style={{backgroundColor: color.value}}>
          <input type="color" v-model={color.value} disabled={!props.editing} class={`op-0 ${props.editing ? 'cursor-pointer' : 'cursor-default'}`}/>
        </div>
        <SetImageButton genre={props.genre} type={props.type}/>
        <input v-model={props.genre.genreName} disabled={!props.editing}
               class={`b b-gray-3 bg-white b-solid rounded-sm lh-normal text-align-center box-content ${props.editing ? 'cursor-text' : 'cursor-default'}`}/>
        {
          props.editing ?
            <textarea v-model={props.genre.genreNameTwoLine} disabled={!props.editing} rows={2}
                      class="b b-gray-3 b-solid rounded-sm h-12 lh-normal box-content text-align-center resize-none cursor-text p-0 my-4 text-1em"
                      onKeydown={e => {
                        if (e.key === 'Enter') {
                          if (props.genre.genreNameTwoLine?.includes('\n')) { // 不能有两个换行
                            e.stopPropagation();
                            e.preventDefault();
                          }
                        }
                      }}
            /> :
            // contenteditable 的换行有疑难杂症
            <pre class="b b-gray-3 b-solid rounded-sm h-12 lh-normal box-content text-align-center flex items-center justify-center my-4 bg-white">{props.genre.genreNameTwoLine}</pre>
        }
        <Buttons/>
      </div>
    );
  },
});
