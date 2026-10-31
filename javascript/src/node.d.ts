import { TranslationSet } from './index.js';

/** Reads a language with its fallback chain from a folder. Resolves to null when no file exists. */
export function tryLoadDirectory(language: string, directory: string): Promise<TranslationSet | null>;
/** Reads a language with its fallback chain from a folder; rejects when no file exists. */
export function loadDirectory(language: string, directory: string): Promise<TranslationSet>;
/** The synchronous variant, for start-up code that can't await. */
export function loadDirectorySync(language: string, directory: string): TranslationSet;
/** Loads language from directory and switches Rcinel to it. */
export function useDirectory(language: string, directory: string): Promise<void>;
