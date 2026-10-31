// spec/fixtures/cases.json, the cases the C# and Java clients run too. A failure here means the
// clients disagree about what an app shows.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { fallbackChain, TranslationFormatError, TranslationSet } from '../src/index.js';
import { loadDirectory, loadDirectorySync, tryLoadDirectory } from '../src/node.js';

const fixtures = join(fileURLToPath(new URL('.', import.meta.url)), '..', '..', 'spec', 'fixtures');
const translations = join(fixtures, 'translations');
const cases = JSON.parse(readFileSync(join(fixtures, 'cases.json'), 'utf8'));

for (const c of cases.translate) {
  test(`translates ${c.language} ${c.id} like the other clients`, async () => {
    assert.equal((await loadDirectory(c.language, translations)).get(c.id, c.english), c.expected);
    assert.equal(loadDirectorySync(c.language, translations).get(c.id, c.english), c.expected);
  });
}

test('merged sets have the expected size', async () => {
  for (const [language, count] of Object.entries(cases.counts)) {
    assert.equal((await loadDirectory(language, translations)).size, count, language);
  }
});

test('fallback chains go from specific to general', () => {
  for (const c of cases.fallbackChains) {
    assert.deepEqual(fallbackChain(c.language), c.chain);
  }
});

test('missing languages reject and tryLoad resolves to null', async () => {
  for (const language of cases.missingLanguages) {
    await assert.rejects(loadDirectory(language, translations), { code: 'ENOENT' });
    assert.equal(await tryLoadDirectory(language, translations), null);
  }
});

test('empty languages load nothing with the try variants', async () => {
  for (const language of cases.emptyLanguages) {
    assert.equal(await tryLoadDirectory(language, translations), null);
  }
});

for (const name of cases.invalid) {
  test(`rejects ${name}`, async () => {
    const text = readFileSync(join(fixtures, 'invalid', name), 'utf8');
    assert.throws(() => TranslationSet.parse('x', text), TranslationFormatError);
    // Loaded from a folder, the message names the file.
    const dir = mkdtempSync(join(tmpdir(), 'rcinel-'));
    writeFileSync(join(dir, 'x.json'), text);
    await assert.rejects(loadDirectory('x', dir), (e) => e instanceof TranslationFormatError && e.message.includes('x.json'));
    rmSync(dir, { recursive: true });
  });
}
