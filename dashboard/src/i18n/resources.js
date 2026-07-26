/**
 * Translation catalogs.
 *
 * One JSON file per supported locale, in the shape i18next expects. English is the source of truth;
 * every other catalog mirrors its key structure exactly.
 */

import en from './en.json';
import es from './es.json';
import fr from './fr.json';
import de from './de.json';
import zh from './zh.json';
import ja from './ja.json';

/** Catalogs keyed by locale code, in the shape i18next expects. */
const resources = {
  en: { translation: en },
  es: { translation: es },
  fr: { translation: fr },
  de: { translation: de },
  zh: { translation: zh },
  ja: { translation: ja },
};

export default resources;
