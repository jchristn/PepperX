/**
 * i18next initialization.
 *
 * Imported for its side effect from `main.jsx` before the first render, so no component ever paints
 * untranslated text.
 */

import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';

import resources from './resources.js';
import { DEFAULT_LOCALE, LOCALE_CODES, STORAGE_KEY, applyDocumentLocale, normalizeLocale } from './localeRegistry.js';

if (!i18n.isInitialized) {
  i18n
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
      resources,
      fallbackLng: DEFAULT_LOCALE,
      supportedLngs: LOCALE_CODES,
      defaultNS: 'translation',
      ns: ['translation'],
      returnNull: false,
      returnEmptyString: false,
      // React escapes for us; escaping here would double-encode.
      interpolation: { escapeValue: false },
      detection: {
        // `?lang=` first so a QA link can pin a locale without touching stored preferences.
        order: ['querystring', 'localStorage', 'navigator'],
        lookupQuerystring: 'lang',
        lookupLocalStorage: STORAGE_KEY,
        caches: ['localStorage'],
        convertDetectedLanguage: normalizeLocale,
      },
    });

  applyDocumentLocale(i18n.resolvedLanguage || i18n.language);
  i18n.on('languageChanged', applyDocumentLocale);
}

/** Change the active locale, persisting it and updating the document direction. */
export function setLocale(locale) {
  return i18n.changeLanguage(normalizeLocale(locale));
}

/** The locale currently in effect. */
export function currentLocale() {
  return normalizeLocale(i18n.resolvedLanguage || i18n.language);
}

export default i18n;
