import { defineComponent, onMounted, PropType, ref } from "vue";
import { GenreXml } from "@/client/apiGen";
import { Button } from "@munet/ui";
import api, { getUrl } from "@/client/api";
import SelectFileTypeTip from "./SelectFileTypeTip";
import { globalCapture, updateAddVersionList, updateGenreList } from "@/store/refs";
import { EDIT_TYPE } from "./index";
import { useI18n } from 'vue-i18n';
import { pickFile } from "@/utils/pickFile";

export default defineComponent({
  props: {
    genre: {type: Object as PropType<GenreXml>, required: true},
    type: Number as PropType<EDIT_TYPE>,
  },
  setup(props) {
    const imageUrl = ref('');
    const showTip = ref(false);
    const { t } = useI18n();

    const refresh = async () => {
      if (!props.genre.fileName) return;
      const req = await fetch(getUrl(`GetLocalAssetApi/${props.genre.fileName}`));
      if (!req.ok) return;
      const image = await req.blob();
      imageUrl.value = URL.createObjectURL(image);
    }

    onMounted(refresh)

    const startProcess = async () => {
      showTip.value = true;
      try {
        // 分类/版本图片，使用通用单文件选择（兼容 WebKitGTK）
        const file = await pickFile('image/jpeg,image/png');
        showTip.value = false;

        if (!file) return;

        await (props.type === EDIT_TYPE.Genre ? api.SetGenreTitleImage : api.SetVersionTitleImage)({id: props.genre.id!, image: file});
        await updateGenreList();
        await updateAddVersionList();
        await refresh();
      } catch (e: any) {
        if (e.name === 'AbortError') return
        console.log(e)
        globalCapture(e, t('genre.setImageFailed'))
      } finally {
        showTip.value = false;
      }
    }

    return () => <div>
      {imageUrl.value ?
        <img src={imageUrl.value} class="max-w-full max-h-3em object-cover cursor-pointer" onClick={startProcess}/> :
        <Button variant="secondary" onClick={startProcess}>
          {t('genre.setImage')}
        </Button>
      }
      <SelectFileTypeTip show={showTip.value}/>
    </div>;
  }
})
