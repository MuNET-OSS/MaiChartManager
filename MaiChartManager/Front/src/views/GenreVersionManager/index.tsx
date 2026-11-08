import { computed, defineComponent, ref } from "vue";
import { CheckBox, WhateverNaviBar } from "@munet/ui";
import GenreDisplay from "./GenreDisplay";
import { addVersionList, genreList } from "@/store/refs";
import { useStorage } from "@vueuse/core";
import CreateButton from "./CreateButton";
import { useI18n } from 'vue-i18n';

export enum EDIT_TYPE {
  None,
  Genre,
  Version,
}

export default defineComponent({
  setup(props) {
    const activeTab = ref(EDIT_TYPE.Genre);
    const showBuiltIn = useStorage('showBuiltInGenre', true);
    const { t } = useI18n();
    const text = computed(() => activeTab.value === EDIT_TYPE.Genre ? t('genre.title') : t('version.title'));
    const editingId = ref(-1);

    const naviItems = computed(() => [
      {
        name: t('genre.management'),
        selected: activeTab.value === EDIT_TYPE.Genre,
        onClick: () => { activeTab.value = EDIT_TYPE.Genre; editingId.value = -1; },
      },
      {
        name: t('version.management'),
        selected: activeTab.value === EDIT_TYPE.Version,
        onClick: () => { activeTab.value = EDIT_TYPE.Version; editingId.value = -1; },
      },
    ]);

    const list = computed(() => {
      const data = activeTab.value === EDIT_TYPE.Genre ? genreList : addVersionList;
      return showBuiltIn.value ? data.value : data.value.filter(it => it.assetDir !== 'A000');
    });


    return () => (
      <div class="flex flex-col p-xy h-full min-h-0">
        <div class="flex gap-2 items-center mb-2">
          <WhateverNaviBar items={naviItems.value} />
          <div class="grow-1" />
          <CheckBox v-model:value={showBuiltIn.value}>{t('genre.showBuiltIn')}</CheckBox>
          <CreateButton setEditId={id => editingId.value = id} type={activeTab.value}/>
        </div>
        <div class="of-y-auto cst grow-1">
          <div class="flex flex-col gap-1">
            {list.value.map(it => <div key={it.id}>
              <GenreDisplay genre={it} type={activeTab.value} class={`${editingId.value >= 0 && editingId.value !== it.id && 'op-30'}`} disabled={editingId.value >= 0 && editingId.value !== it.id}
                            style={{transition: 'opacity 0.3s'}}
                            editing={editingId.value === it.id} setEdit={isEdit => editingId.value = isEdit ? it.id! : -1}/>
            </div>)}
          </div>
        </div>
      </div>
    );
  },
})
