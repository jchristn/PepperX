/**
 * Translation catalogs.
 *
 * The two pseudo-locales are generated from the English source at load time rather than maintained
 * by hand. They exist so layout breakage from longer languages and bidi problems show up while the
 * UI is being written, instead of after real translations arrive.
 */

import en from './en.json';
import de from './de.json';
import ja from './ja.json';

/** Latin characters swapped for accented look-alikes: still readable, obviously not English. */
const ACCENTS = {
  a: 'ȧ', b: 'ƀ', c: 'ƈ', d: 'ḓ', e: 'ḗ', f: 'ƒ', g: 'ɠ', h: 'ħ', i: 'ī', j: 'ĵ', k: 'ķ', l: 'ŀ',
  m: 'ḿ', n: 'ƞ', o: 'ǿ', p: 'ƥ', q: 'ɋ', r: 'ř', s: 'ş', t: 'ŧ', u: 'ŭ', v: 'ṽ', w: 'ẇ', x: 'ẋ',
  y: 'ẏ', z: 'ẑ',
  A: 'Ȧ', B: 'Ɓ', C: 'Ƈ', D: 'Ḓ', E: 'Ḗ', F: 'Ƒ', G: 'Ɠ', H: 'Ħ', I: 'Ī', J: 'Ĵ', K: 'Ķ', L: 'Ŀ',
  M: 'Ḿ', N: 'Ƞ', O: 'Ǿ', P: 'Ƥ', Q: 'Ɋ', R: 'Ř', S: 'Ş', T: 'Ŧ', U: 'Ŭ', V: 'Ṽ', W: 'Ẇ', X: 'Ẋ',
  Y: 'Ẏ', Z: 'Ẑ',
};

/** Unicode bidi controls that force a run of Latin text to lay out right to left. */
const RTL_EMBED_START = '‫';
const RTL_EMBED_END = '‬';

/** Split on interpolation placeholders so `{{count}}` survives transformation intact. */
function splitPlaceholders(text) {
  return String(text).split(/(\{\{[^}]+\}\})/g);
}

function isPlaceholder(segment) {
  return segment.startsWith('{{') && segment.endsWith('}}');
}

/**
 * Accent the letters and pad the string by roughly 40%.
 *
 * German and Finnish routinely run 30–40% longer than English, so a layout that survives this
 * locale will survive real translation.
 */
function pseudoExpand(text) {
  const accented = splitPlaceholders(text)
    .map((segment) =>
      isPlaceholder(segment)
        ? segment
        : segment.replace(/[A-Za-z]/g, (character) => ACCENTS[character] ?? character),
    )
    .join('');

  const padding = Math.max(2, Math.round(accented.length * 0.4));
  return `[${accented}${'·'.repeat(padding)}]`;
}

/** Wrap the string in bidi controls so it renders right to left without translating it. */
function pseudoRtl(text) {
  const marked = splitPlaceholders(text)
    .map((segment) => (isPlaceholder(segment) ? segment : segment))
    .join('');
  return `${RTL_EMBED_START}${marked}${RTL_EMBED_END}`;
}

/** Walk a catalog, applying `transform` to every leaf string. */
function mapCatalog(source, transform) {
  const result = {};
  for (const [key, value] of Object.entries(source)) {
    result[key] = typeof value === 'string' ? transform(value) : mapCatalog(value, transform);
  }
  return result;
}

/** Catalogs keyed by locale code, in the shape i18next expects. */
const resources = {
  en: { translation: en },
  de: { translation: de },
  ja: { translation: ja },
  'en-XA': { translation: mapCatalog(en, pseudoExpand) },
  'ar-XB': { translation: mapCatalog(en, pseudoRtl) },
};

export default resources;
