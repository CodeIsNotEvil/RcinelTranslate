/** Thrown when a translation file is not a flat JSON object of strings. The message says where. */
export class TranslationFormatError extends Error {}

/** The translations of one language. Immutable. */
export class TranslationSet {
  constructor(language: string, texts: Map<string, string>);
  /** Reads a set from the text of a translation file. */
  static parse(language: string, json: string): TranslationSet;
  /** Merges the files of a fallback chain, general first so specific files win. */
  static merge(language: string, generalToSpecific: Array<Map<string, string>>): TranslationSet;
  readonly language: string;
  readonly size: number;
  ids(): string[];
  has(id: string): boolean;
  get(id: string, fallback: string): string;
}

/** An empty set: every id shows the English text of the call. */
export const EMPTY: TranslationSet;

/** "zh-Hant-TW" gives ["zh-Hant-TW", "zh-Hant", "zh"]. Underscores count as dashes. */
export function fallbackChain(language: string): string[];

export interface FetchOptions {
  /** Where the files are, default "translations/" relative to the page. */
  baseUrl?: string | URL;
  /** A fetch implementation, default globalThis.fetch. */
  fetch?: typeof fetch;
}

export function fetchTranslationSet(language: string, options?: FetchOptions): Promise<TranslationSet>;
export function tryFetchTranslationSet(language: string, options?: FetchOptions): Promise<TranslationSet | null>;

export type Values = readonly unknown[] | Readonly<Record<string, unknown>>;

/** Fills {0}, {1}, ... from an array, or {name} from an object. Unknown placeholders stay. */
export function format(text: string, values?: Values): string;

export interface RcinelApi {
  /** The translation of id, or english when the loaded language has none. */
  translate(id: string, english: string, values?: Values): string;
  /** The loaded language, such as "de". Empty while the app shows its English texts. */
  readonly language: string;
  readonly current: TranslationSet;
  load(language: string, options?: FetchOptions): Promise<void>;
  tryLoad(language: string, options?: FetchOptions): Promise<boolean>;
  use(set: TranslationSet): void;
  useObject(language: string, texts: Readonly<Record<string, string>>): void;
  reset(): void;
  /** Returns a function that removes the listener. */
  onChange(listener: (set: TranslationSet) => void): () => void;
}

export const Rcinel: RcinelApi;
export default Rcinel;
