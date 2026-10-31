// Loading from the file system, for Node and Electron. Its own entry point ("rcinel-translate/node")
// so browser bundles never see a node:fs import.
import { readFile } from 'node:fs/promises';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { readFlatJson, TranslationFormatError } from './reader.js';
import { fallbackChain, Rcinel, TranslationSet } from './index.js';

/** The chain's files, most specific first: { path, text } or null where the file doesn't exist. */
function paths(language, directory) {
  return fallbackChain(language).map((code) => join(directory, `${code}.json`));
}

function orNull(error) {
  if (error?.code === 'ENOENT') {
    return null;
  }
  throw error;
}

function merge(language, files) {
  // General first, so the specific files win.
  const found = files.filter((f) => f !== null).reverse().map(({ path, text }) => {
    try {
      return readFlatJson(text);
    } catch (e) {
      if (e instanceof TranslationFormatError) {
        throw new TranslationFormatError(`${path}: ${e.message}`);
      }
      throw e;
    }
  });
  return found.length === 0 ? null : TranslationSet.merge(language, found);
}

function missing(language, directory) {
  const error = new Error(`There is no translation file for "${language}" in ${directory} (looked for ${fallbackChain(language).join(', ')}).`);
  error.code = 'ENOENT';
  return error;
}

/** Reads a language with its fallback chain from a folder. Resolves to null when no file exists. */
export async function tryLoadDirectory(language, directory) {
  if (typeof language !== 'string' || language.trim() === '') {
    return null;
  }
  const files = await Promise.all(paths(language, directory).map((path) =>
    readFile(path, 'utf8').then((text) => ({ path, text }), orNull)));
  return merge(language, files);
}

/** Reads a language with its fallback chain from a folder; rejects when no file exists. */
export async function loadDirectory(language, directory) {
  const set = await tryLoadDirectory(language, directory);
  if (!set) {
    throw missing(language, directory);
  }
  return set;
}

/** The synchronous variant, for start-up code that can't await. */
export function loadDirectorySync(language, directory) {
  const files = paths(language, directory).map((path) => {
    try {
      return { path, text: readFileSync(path, 'utf8') };
    } catch (e) {
      return orNull(e);
    }
  });
  const set = merge(language, files);
  if (!set) {
    throw missing(language, directory);
  }
  return set;
}

/** Loads language from directory and switches Rcinel to it. */
export async function useDirectory(language, directory) {
  Rcinel.use(await loadDirectory(language, directory));
}
