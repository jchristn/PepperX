/**
 * Central locale registry.
 *
 * Adding a language means adding an entry here and a catalog in `resources.js` — no application
 * logic changes. Pseudo-locales ship alongside the real ones so expansion and bidi problems surface
 * during development rather than after translation.
 */

/** Key under which the selected locale is persisted. */
export const STORAGE_KEY = 'pepperx.locale';

/** Locale used when nothing else resolves. */
export const DEFAULT_LOCALE = 'en';

/**
 * Supported locales, keyed by BCP 47 code.
 *
 * `nativeName` is what the selector shows — people recognize their own language faster in its own
 * script than in English.
 */
export const LOCALES = {
  en: { code: 'en', englishName: 'English', nativeName: 'English', dir: 'ltr', fallback: null },
  de: { code: 'de', englishName: 'German', nativeName: 'Deutsch', dir: 'ltr', fallback: 'en' },
  ja: { code: 'ja', englishName: 'Japanese', nativeName: '日本語', dir: 'ltr', fallback: 'en' },
  'en-XA': {
    code: 'en-XA',
    englishName: 'Pseudo (expansion)',
    nativeName: 'Ƥşḗḗŭŭḓǿǿ (expansion)',
    dir: 'ltr',
    fallback: 'en',
    pseudo: true,
  },
  'ar-XB': {
    code: 'ar-XB',
    englishName: 'Pseudo (right-to-left)',
    nativeName: 'Pseudo (RTL)',
    dir: 'rtl',
    fallback: 'en',
    pseudo: true,
  },
};

/** Locale codes in the order the selector should list them. */
export const LOCALE_CODES = Object.keys(LOCALES);

/**
 * Map an arbitrary locale string onto a supported code.
 *
 * Browsers report region-qualified locales (`de-AT`, `ja-JP`), so an exact match is tried first and
 * the language subtag second.
 */
export function normalizeLocale(value) {
  if (!value) return DEFAULT_LOCALE;
  if (LOCALES[value]) return value;

  const [language] = String(value).split('-');
  if (LOCALES[language]) return language;

  const match = LOCALE_CODES.find((code) => code.toLowerCase() === String(value).toLowerCase());
  return match ?? DEFAULT_LOCALE;
}

/** Text direction for a locale. */
export function directionFor(locale) {
  return LOCALES[normalizeLocale(locale)]?.dir ?? 'ltr';
}

/** Apply the locale to the document root so CSS and assistive technology follow it. */
export function applyDocumentLocale(locale) {
  const normalized = normalizeLocale(locale);
  if (typeof document === 'undefined') return normalized;

  document.documentElement.lang = normalized;
  document.documentElement.dir = directionFor(normalized);
  return normalized;
}
