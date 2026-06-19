import type { TFunction } from 'i18next';
import { FALLBACK_LANG, SUPPORTED_LANGS } from './i18n';

export interface PokemonLanguageOption {
  value: number;
  label: string;
}

export function getUiLocale(lang: string): string {
  if (SUPPORTED_LANGS.includes(lang as (typeof SUPPORTED_LANGS)[number])) return lang;
  if (lang.startsWith('zh-')) return lang.toLowerCase().includes('hant') ? 'zh-Hant' : 'zh-Hans';
  if (lang.startsWith('es-')) return 'es-419';
  if (lang.startsWith('en-')) return 'en';
  if (lang.includes('-')) return lang.split('-')[0];
  return FALLBACK_LANG;
}

export function getIntlLocale(lang: string): string {
  switch (getUiLocale(lang)) {
    case 'zh-Hans':
      return 'zh-CN';
    case 'zh-Hant':
      return 'zh-TW';
    case 'en':
      return 'en-US';
    case 'ja':
      return 'ja-JP';
    case 'fr':
      return 'fr-FR';
    case 'it':
      return 'it-IT';
    case 'de':
      return 'de-DE';
    case 'es':
      return 'es-ES';
    case 'es-419':
      return 'es-419';
    case 'ko':
      return 'ko-KR';
    default:
      return 'en-US';
  }
}

export function formatLocaleNumber(value: number | string | undefined, lang: string): string {
  if (value == null || value === '') return '';
  const num = typeof value === 'number' ? value : Number(value);
  if (Number.isNaN(num)) return String(value);
  return new Intl.NumberFormat(getIntlLocale(lang)).format(num);
}

export function formatLocaleDateTime(value: string | number | Date, lang: string): string {
  return new Intl.DateTimeFormat(getIntlLocale(lang), {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(value));
}

export function formatLocaleTime(value: string | number | Date, lang: string): string {
  return new Intl.DateTimeFormat(getIntlLocale(lang), {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).format(new Date(value));
}

export function getLocalizedGenerationLabel(t: TFunction, generation: number, platform: string): string {
  return t('genLabel', { ns: 'games', gen: generation, platform });
}

export function getPokemonLanguageOptions(t: TFunction): PokemonLanguageOption[] {
  return [
    { value: 1, label: `${t('language.ja', { ns: 'common', defaultValue: '日本語' })} (JPN)` },
    { value: 2, label: `${t('language.en', { ns: 'common', defaultValue: 'English' })} (ENG)` },
    { value: 3, label: `${t('language.fr', { ns: 'common', defaultValue: 'Français' })} (FRE)` },
    { value: 4, label: `${t('language.it', { ns: 'common', defaultValue: 'Italiano' })} (ITA)` },
    { value: 5, label: `${t('language.de', { ns: 'common', defaultValue: 'Deutsch' })} (GER)` },
    { value: 7, label: `${t('language.es', { ns: 'common', defaultValue: 'Español' })} (SPA)` },
    { value: 8, label: `${t('language.ko', { ns: 'common', defaultValue: '한국어' })} (KOR)` },
    { value: 9, label: `${t('language.zhHans', { ns: 'common', defaultValue: '简体中文' })} (CHS)` },
    { value: 10, label: `${t('language.zhHant', { ns: 'common', defaultValue: '繁體中文' })} (CHT)` },
  ];
}
