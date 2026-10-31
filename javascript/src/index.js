// The JavaScript client: browsers, bundlers, Node, Deno, Electron. No dependencies.
import { readFlatJson, TranslationFormatError } from './reader.js';

export { TranslationFormatError };

/**
 * The file names, without .json, that make up a language, most specific first:
 * "zh-Hant-TW" gives zh-Hant-TW, zh-Hant, zh. Underscores count as dashes.
 * @param {string} language
 * @returns {string[]}
 */
export function fallbackChain(language) {
  if (typeof language !== 'string' || language.trim() === '') {
    throw new TypeError('A language code is needed, such as "de".');
  }
  let code = language.trim().replaceAll('_', '-');
  const chain = [];
  for (;;) {
    chain.push(code);
    const dash = code.lastIndexOf('-');
    if (dash <= 0) {
      return chain;
    }
    code = code.slice(0, dash);
  }
}

/** The translations of one language. Immutable. */
export class TranslationSet {
  /** @type {Map<string, string>} */
  #texts;

  /**
   * @param {string} language
   * @param {Map<string, string>} texts
   */
  constructor(language, texts) {
    this.language = language;
    this.#texts = new Map(texts);
    Object.freeze(this);
  }

  /** Reads a set from the text of a translation file. */
  static parse(language, json) {
    return new TranslationSet(language, readFlatJson(json));
  }

  /**
   * Merges the files of a fallback chain, general first so specific files win.
   * @param {string} language
   * @param {Array<Map<string, string>>} generalToSpecific
   */
  static merge(language, generalToSpecific) {
    const merged = new Map();
    for (const texts of generalToSpecific) {
      for (const [id, text] of texts) {
        merged.set(id, text);
      }
    }
    return new TranslationSet(language, merged);
  }

  /** How many ids have a translation. */
  get size() {
    return this.#texts.size;
  }

  /** Every id that has a translation. */
  ids() {
    return [...this.#texts.keys()];
  }

  has(id) {
    return this.#texts.has(id);
  }

  /** The translation of id, or fallback when there is none. */
  get(id, fallback) {
    const text = this.#texts.get(id);
    return text === undefined ? fallback : text;
  }
}

/** An empty set: every id shows the English text of the call. */
export const EMPTY = new TranslationSet('', new Map());

/**
 * Fetches a language with its fallback chain over HTTP: "de-AT" fetches de.json and de-AT.json
 * from baseUrl. A 404 skips that file; when no file of the chain exists, it rejects.
 * @param {string} language
 * @param {{ baseUrl?: string | URL, fetch?: typeof fetch }} [options]
 * @returns {Promise<TranslationSet>}
 */
export async function fetchTranslationSet(language, options = {}) {
  const set = await tryFetchTranslationSet(language, options);
  if (!set) {
    throw new Error(`There is no translation file for "${language}" at ${options.baseUrl ?? 'translations/'} (looked for ${fallbackChain(language).join(', ')}).`);
  }
  return set;
}

/** Like fetchTranslationSet, but resolves to null when no file of the chain exists. */
export async function tryFetchTranslationSet(language, options = {}) {
  // An empty language (navigator.language can be "" in some embedded browsers) means "none".
  if (typeof language !== 'string' || language.trim() === '') {
    return null;
  }
  const doFetch = options.fetch ?? globalThis.fetch;
  const base = String(options.baseUrl ?? 'translations/');
  const prefix = base.endsWith('/') ? base : `${base}/`;
  const chain = fallbackChain(language);
  // All files of the chain at once; the order of the results is the chain's.
  const files = await Promise.all(chain.map(async (code) => {
    const url = `${prefix}${code}.json`;
    const response = await doFetch(url);
    if (response.status === 404) {
      return null;
    }
    if (!response.ok) {
      throw new Error(`Loading ${url} failed: HTTP ${response.status}.`);
    }
    try {
      return readFlatJson(await response.text());
    } catch (e) {
      if (e instanceof TranslationFormatError) {
        throw new TranslationFormatError(`${url}: ${e.message}`);
      }
      throw e;
    }
  }));
  const found = files.filter((f) => f !== null).reverse();
  return found.length === 0 ? null : TranslationSet.merge(language, found);
}

/**
 * Fills {0}, {1}, ... from an array, or {name} from an object. Unknown placeholders stay, so a
 * missing value shows up as "{name}" instead of "undefined".
 */
export function format(text, values) {
  if (values == null) {
    return text;
  }
  return text.replace(/\{([\w.]+)\}/g, (whole, key) =>
    (Object.prototype.hasOwnProperty.call(values, key) ? String(values[key]) : whole));
}

let current = EMPTY;
const listeners = new Set();

// The functions are standalone, so they can be imported by name and called bare,
// `translate("id", "Save")`, the same shape as C#'s Translate("id", "Save"). The Rcinel object
// below groups the same functions for code that prefers Rcinel.translate(...). None of them uses
// `this`, so destructuring (`const { translate } = Rcinel`) works too.

/**
 * The translation of id, or english when the loaded language has none. With values, fills
 * {0}/{name} placeholders (see format).
 * @param {string} id
 * @param {string} english
 * @param {unknown[] | Record<string, unknown>} [values]
 */
export function translate(id, english, values) {
  return format(current.get(id, english), values);
}

/** The loaded language, such as "de". Empty while the app shows its English texts. */
export function language() {
  return current.language;
}

/** Switches to a set the app built itself, for example from a bundled JSON import. */
export function use(set) {
  if (!(set instanceof TranslationSet)) {
    throw new TypeError('use needs a TranslationSet; build one with TranslationSet.parse(language, json).');
  }
  current = set;
  for (const listener of listeners) {
    listener(set);
  }
}

/**
 * Fetches language from options.baseUrl (default "translations/", relative to the page) and
 * switches to it.
 */
export async function load(language, options) {
  use(await fetchTranslationSet(language, options));
}

/**
 * Switches to language if the app ships it and resolves to true; otherwise keeps the current one
 * and resolves to false. Handy for following the browser: tryLoad(navigator.language).
 */
export async function tryLoad(language, options) {
  const set = await tryFetchTranslationSet(language, options);
  if (set) {
    use(set);
  }
  return set !== null;
}

/**
 * Switches to a language from an already parsed object, such as `import de from './de.json'`.
 * Values must be strings, as in the files.
 */
export function useObject(language, texts) {
  const map = new Map();
  for (const [id, text] of Object.entries(texts)) {
    if (typeof text !== 'string') {
      throw new TranslationFormatError(`${id}: translation files hold only strings.`);
    }
    map.set(id, text);
  }
  use(new TranslationSet(language, map));
}

/** Goes back to the English texts of the calls. */
export function reset() {
  use(EMPTY);
}

/**
 * Calls listener after every language change, so a UI can redraw. Returns a function that removes
 * it again.
 */
export function onChange(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * The app-wide entry point. Load a language once at start (and again when the user picks another),
 * then write every UI text as translate("UUIDv7", "English text"). The English text is the
 * fallback and the source of truth: `rcinel scan` collects the calls into the source file.
 */
export const Rcinel = Object.freeze({
  translate,
  load,
  tryLoad,
  use,
  useObject,
  reset,
  onChange,

  /** The loaded language, such as "de". Empty while the app shows its English texts. */
  get language() {
    return current.language;
  },

  /** The loaded set. */
  get current() {
    return current;
  },
});

export default Rcinel;
