import { defineComponent, ref, Teleport } from "vue";
import { isReadOnlyDir, selectedADir, selectedMusic, selectMusicId, updateMusicList } from "@/store/refs";
import api from "@/client/api";
import animation from './animation.module.sass';
import { useMagicKeys, whenever } from '@vueuse/core'
import { useI18n } from 'vue-i18n';

export default defineComponent({
  setup() {
    const isAnimationShow = ref(false);
    const { t } = useI18n();

    const save = async () => {
      // A000 只读：Ctrl+S 也要拦住，不能只靠按钮禁用
      if (isReadOnlyDir.value) return;
      await api.SaveMusic(selectMusicId.value, selectedADir.value);
      isAnimationShow.value = true;
      setTimeout(() => {
        isAnimationShow.value = false;
      }, 250);
      await updateMusicList(true);
    }

    const {ctrl_s} = useMagicKeys({
      passive: false,
      onEventFired(e) {
        if (e.ctrlKey && e.key === 's' && e.type === 'keydown')
          e.preventDefault()
      },
    })
    whenever(ctrl_s, save);

    return () => selectedMusic.value && (
      <button onClick={save} disabled={isReadOnlyDir.value} class={selectedMusic.value.modified && "bg-orange-300!"}>
        {t('common.save')}
        {isAnimationShow.value && <Teleport to="body">
          <div class={animation.box}/>
        </Teleport>}
      </button>
    );
  }
});
