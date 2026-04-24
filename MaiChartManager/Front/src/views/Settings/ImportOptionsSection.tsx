import { defineComponent } from "vue";
import { CheckBox, Select } from "@munet/ui";
import { useI18n } from "vue-i18n";
import { appSettings, saveSettings } from "@/store/settings";
import { MovieCodec } from "@/client/apiGen";

export default defineComponent({
  setup() {
    const { t } = useI18n();

    const movieCodecOptions = [
      { label: t('chart.import.option.codecForceH264'), value: MovieCodec.ForceH264 },
      { label: t('chart.import.option.codecForceVP9'), value: MovieCodec.ForceVP9 },
    ];

    const onSettingChange = () => {
      saveSettings();
    };

    return () => (
      <div class="mb-6">
        <div class="text-lg font-semibold mb-3 text-[var(--link-color)]">{t('settings.importOptions')}</div>
        <div class="rounded-xl bg-white/60 p-4 flex flex-col gap-4 border border-gray-200 border-solid">
          <CheckBox v-model:value={appSettings.value.ignoreLevel} onChange={onSettingChange}>{t('settings.ignoreLevel')}</CheckBox>
          <CheckBox v-model:value={appSettings.value.disableBga} onChange={onSettingChange}>{t('settings.disableBga')}</CheckBox>
          <div class="flex items-center gap-3">
            <span class="shrink-0">{t('settings.movieCodec')}</span>
            <Select v-model:value={appSettings.value.movieCodec} options={movieCodecOptions} onChange={onSettingChange} />
          </div>
          <CheckBox v-model:value={appSettings.value.yuv420p} onChange={onSettingChange}>{t('settings.yuv420p')}</CheckBox>
          <CheckBox v-model:value={appSettings.value.noScale} onChange={onSettingChange}>{t('settings.noScale')}</CheckBox>
        </div>
      </div>
    );
  },
});
