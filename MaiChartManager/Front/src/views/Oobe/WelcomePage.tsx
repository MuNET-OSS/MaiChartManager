import { defineComponent } from 'vue';
import { useI18n } from 'vue-i18n';
import rei from '@/components/Splash/mcm.png';
import { setLocale, availableLocales, locale } from '@/locales/index';
import type { Locale } from '@/locales/index';
import { Select } from '@munet/ui';

export default defineComponent({
  setup() {
    const { t } = useI18n();

    const localeLabels: Record<Locale, string> = {
      'zh': '简体中文',
      'zh-TW': '繁體中文',
      'en': 'English',
    };
    const localeOptions = availableLocales.map(l => ({
      label: localeLabels[l],
      value: l,
    }));

    return () => (
      <div class="absolute inset-0 overflow-y-auto">
        <div class="flex flex-col items-center justify-center min-h-full gap-6 px-6 py-6 pb-24 box-border">
          <img src={rei} alt="MaiChartManager" class="h-48" />
          <div class="text-2xl font-bold op-90">MaiChartManager</div>
          <div class="text-lg op-70">{t('oobe.welcomeMessage')}</div>
          <div class="flex items-center gap-4">
            <div class="i-mdi-translate text-xl op-60" />
            <Select
              value={locale.value}
              options={localeOptions}
              onChange={(v: any) => setLocale(v as Locale)}
            />
          </div>
        </div>
      </div>
    );
  },
});
