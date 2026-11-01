import threading
import unittest
from pathlib import Path

import rcinel_translate
from rcinel_translate import (
    TranslationSet,
    format_text,
    language,
    load,
    on_change,
    reset,
    translate,
    try_load,
    use,
)

TRANSLATIONS = Path(__file__).resolve().parents[2] / "spec" / "fixtures" / "translations"
SAVE = "0192f3a0-1c4e-7a10-8000-000000000001"


class RcinelTest(unittest.TestCase):
    def tearDown(self):
        reset()

    def test_shows_the_english_text_before_anything_is_loaded(self):
        reset()
        self.assertEqual("Save", translate(SAVE, "Save"))
        self.assertEqual("", language())

    def test_load_switches_the_language_and_tells_listeners(self):
        seen = []
        remove = on_change(lambda s: seen.append(s.language))
        load("de-AT", TRANSLATIONS)
        remove()
        reset()
        self.assertEqual(["de-AT"], seen)

    def test_translates_after_loading(self):
        load("de_AT", TRANSLATIONS)
        self.assertEqual("de_AT", language())
        self.assertEqual("Sichern", translate(SAVE, "Save"))
        self.assertEqual("Abbrechen", translate("0192f3a0-1c4e-7a10-8000-000000000002", "Cancel"))

    def test_try_load_keeps_the_language_when_the_app_ships_no_file(self):
        load("de", TRANSLATIONS)
        self.assertFalse(try_load("fr", TRANSLATIONS))
        self.assertFalse(try_load("", TRANSLATIONS))
        self.assertEqual("de", language())

    def test_the_default_directory_can_be_set(self):
        old = rcinel_translate.directory()
        try:
            rcinel_translate.set_directory(TRANSLATIONS)
            self.assertTrue(try_load("de"))
            self.assertEqual("Speichern", translate(SAVE, "Save"))
        finally:
            rcinel_translate.set_directory(old)

    def test_placeholders_from_args_and_kwargs_and_unknown_ones_stay(self):
        use(TranslationSet.parse("de", '{"a": "{0} von {1} f\\u00fcr {name}, {missing} {"}'))
        self.assertEqual("3 von 5 für Alex, {missing} {", translate("a", "{0} of {1}", 3, 5, name="Alex"))
        self.assertEqual("{x}", format_text("{x}"))

    def test_use_needs_a_translation_set(self):
        with self.assertRaises(TypeError):
            use({"a": "b"})

    def test_parse_reads_package_data_with_a_bom(self):
        self.assertEqual("b", TranslationSet.parse("x", '﻿{"a": "b"}').get("a", "fallback"))

    def test_readers_on_other_threads_always_see_a_whole_set(self):
        sets = [TranslationSet.parse("a", '{"k": "A"}'), TranslationSet.parse("b", '{"k": "B"}')]
        stop = threading.Event()
        bad = []

        def read():
            while not stop.is_set():
                if translate("k", "?") not in ("A", "B", "?"):
                    bad.append(True)

        reader = threading.Thread(target=read)
        reader.start()
        for i in range(2000):
            use(sets[i % 2])
        stop.set()
        reader.join()
        self.assertEqual([], bad)


if __name__ == "__main__":
    unittest.main()
