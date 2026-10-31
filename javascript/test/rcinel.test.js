import { test, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import Rcinel, { format, TranslationSet } from '../src/index.js';
import { useDirectory } from '../src/node.js';

const translations = join(fileURLToPath(new URL('.', import.meta.url)), '..', '..', 'spec', 'fixtures', 'translations');
const SAVE = '0192f3a0-1c4e-7a10-8000-000000000001';

// A fetch that serves the fixture folder, the way a web server would.
async function fakeFetch(url) {
  try {
    const text = await readFile(join(translations, String(url).split('/').pop()), 'utf8');
    return { ok: true, status: 200, text: async () => text };
  } catch {
    return { ok: false, status: 404, text: async () => '' };
  }
}

afterEach(() => Rcinel.reset());

test('shows the English text before anything is loaded', () => {
  assert.equal(Rcinel.translate(SAVE, 'Save'), 'Save');
  assert.equal(Rcinel.language, '');
});

test('load fetches the fallback chain and tells listeners', async () => {
  let calls = 0;
  const off = Rcinel.onChange(() => calls++);
  await Rcinel.load('de-AT', { baseUrl: '/static/translations', fetch: fakeFetch });
  off();
  assert.equal(Rcinel.language, 'de-AT');
  assert.equal(Rcinel.translate(SAVE, 'Save'), 'Sichern');
  assert.equal(Rcinel.translate('0192f3a0-1c4e-7a10-8000-000000000002', 'Cancel'), 'Abbrechen');
  assert.equal(calls, 1);
});

test('tryLoad keeps the language when the app ships no file', async () => {
  await useDirectory('de', translations);
  assert.equal(await Rcinel.tryLoad('fr', { fetch: fakeFetch }), false);
  assert.equal(Rcinel.language, 'de');
});

test('server errors are not mistaken for missing files', async () => {
  const failing = async () => ({ ok: false, status: 500, text: async () => '' });
  await assert.rejects(Rcinel.load('de', { fetch: failing }), /HTTP 500/);
});

test('placeholders are filled from arrays and objects', () => {
  Rcinel.use(TranslationSet.parse('de', '{"a": "{0} von {1}", "b": "Hallo {name}, {missing}"}'));
  assert.equal(Rcinel.translate('a', '{0} of {1}', [3, 5]), '3 von 5');
  assert.equal(Rcinel.translate('b', 'Hello {name}', { name: 'Alex' }), 'Hallo Alex, {missing}');
  assert.equal(format('{x}', undefined), '{x}');
});

test('useObject takes bundled JSON imports', () => {
  Rcinel.useObject('de', { [SAVE]: 'Speichern' });
  assert.equal(Rcinel.translate(SAVE, 'Save'), 'Speichern');
  assert.throws(() => Rcinel.useObject('de', { [SAVE]: 1 }), /only strings/);
});
