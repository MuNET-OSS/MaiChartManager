import { defineComponent, watch } from "vue";
import { Modal } from "@munet/ui";
import { useI18n } from "vue-i18n";
import { VueMarkdownIt } from "@f3ve/vue-markdown-it";
import { version } from "@/store/refs";
import {
  changelogAutoPopupDone,
  changelogContent,
  changelogTargetVersion,
  getCleanVersion,
  lastShownChangelogVersion,
  openChangelog,
  showChangelogModal,
} from "@/store/appUpdate";
import style from "@/components/VersionInfo/style.module.sass";

export default defineComponent({
  props: {
    ready: {
      type: Boolean,
      required: true,
    },
  },
  setup(props) {
    const { t } = useI18n();

    watch(
      () => [props.ready, version.value?.version] as const,
      async ([ready, ver]) => {
        if (!ready || !ver) return;
        if (changelogAutoPopupDone.value) return;
        changelogAutoPopupDone.value = true;

        const cleanVer = getCleanVersion(ver);
        if (cleanVer === lastShownChangelogVersion.value) return;

        await new Promise(resolve => setTimeout(resolve, 200));
        const shown = await openChangelog(ver, {
          showAfterLoaded: true,
          skipIfEmpty: true,
        });
        if (shown) {
          lastShownChangelogVersion.value = cleanVer;
        }
      },
      { immediate: true },
    );

    return () => <Modal
      width="min(85vw,50em)"
      title={`${t('about.changelogTitle')} - v${changelogTargetVersion.value}`}
      v-model:show={showChangelogModal.value}
    >
      <div class={[style.mdContent, 'cst of-x-hidden']}>
        {changelogContent.value
          ? <VueMarkdownIt source={changelogContent.value} />
          : <div class="text-center py-4 op-60">{t('common.loading')}</div>
        }
      </div>
    </Modal>;
  },
})
