import { createI18n } from 'vue-i18n'
import en from './locales/en'
import zhCN from './locales/zh-CN'

export const LANGUAGE_KEY = 'jax-ipqc-language'
export type SupportedLocale = 'en' | 'zh-CN'

export function getSavedLocale(storage: Pick<Storage, 'getItem'> = localStorage): SupportedLocale {
  return storage.getItem(LANGUAGE_KEY) === 'zh-CN' ? 'zh-CN' : 'en'
}

export function saveLocale(locale: SupportedLocale, storage: Pick<Storage, 'setItem'> = localStorage): void {
  storage.setItem(LANGUAGE_KEY, locale)
}

export const i18n = createI18n({
  legacy: false,
  locale: getSavedLocale(),
  fallbackLocale: 'en',
  messages: { en, 'zh-CN': zhCN },
})
