import { defineComponent } from "vue";
import { selectedADir } from "@/store/refs";
import { t } from "@/locales";
import { Button } from "@munet/ui";
import { openWindow } from "@/utils/openWindow";

export type ChartSide = "L" | "R";

export default defineComponent({
  props: {
    songId: {type: Number, required: true},
    level: {type: Number, required: true},
    side: {type: String as () => ChartSide | undefined},
  },
  setup(props) {
    const openPreview = () => {
      const query: Record<string, string> = {
        assetDir: selectedADir.value!,
        songId: String(props.songId),
        level: String(props.level),
      };
      if (props.side) query.side = props.side;
      openWindow({
        path: '/chart-preview',
        query,
        title: t('music.edit.previewChart'),
        width: 960,
        height: 640,
      });
    };

    return () => (
      <Button onClick={openPreview}>
        {t('music.edit.previewChart')}
      </Button>
    );
  },
});
