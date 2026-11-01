"""spec/fixtures/cases.json, the cases the C#, Java, JavaScript and Rust clients run too. A failure
here means the clients disagree about what an app shows."""

import json
import shutil
import tempfile
import unittest
from pathlib import Path

from rcinel_translate import TranslationFormatError, TranslationSet, fallback_chain

FIXTURES = Path(__file__).resolve().parents[2] / "spec" / "fixtures"
TRANSLATIONS = FIXTURES / "translations"
CASES = json.loads((FIXTURES / "cases.json").read_text(encoding="utf-8"))


class ConformanceTest(unittest.TestCase):
    def test_translates_like_the_other_clients(self):
        for case in CASES["translate"]:
            with self.subTest(language=case["language"], id=case["id"]):
                loaded = TranslationSet.load(case["language"], TRANSLATIONS)
                self.assertEqual(case["expected"], loaded.get(case["id"], case["english"]))

    def test_merged_sets_have_the_expected_size(self):
        for language, count in CASES["counts"].items():
            with self.subTest(language=language):
                self.assertEqual(count, len(TranslationSet.load(language, TRANSLATIONS)))

    def test_fallback_chains_go_from_specific_to_general(self):
        for case in CASES["fallbackChains"]:
            self.assertEqual(case["chain"], fallback_chain(case["language"]))

    def test_missing_languages_raise_and_try_load_returns_none(self):
        for language in CASES["missingLanguages"]:
            with self.subTest(language=language):
                with self.assertRaises(FileNotFoundError):
                    TranslationSet.load(language, TRANSLATIONS)
                self.assertIsNone(TranslationSet.try_load(language, TRANSLATIONS))

    def test_empty_languages_load_nothing_with_try_load(self):
        for language in CASES["emptyLanguages"]:
            self.assertIsNone(TranslationSet.try_load(language, TRANSLATIONS))

    def test_rejects_invalid_files(self):
        for name in CASES["invalid"]:
            with self.subTest(file=name):
                path = FIXTURES / "invalid" / name
                with self.assertRaises(TranslationFormatError) as error:
                    TranslationSet.from_file("x", path)
                # The message must say which file, or nobody finds the broken one.
                self.assertIn(name, str(error.exception))
                # Loaded from a folder as a language, it fails the same way.
                folder = Path(tempfile.mkdtemp(prefix="rcinel-"))
                try:
                    shutil.copy(path, folder / "x.json")
                    with self.assertRaises(TranslationFormatError):
                        TranslationSet.load("x", folder)
                finally:
                    shutil.rmtree(folder)


if __name__ == "__main__":
    unittest.main()
