package io.github.codeisnotevil.rcinel;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;

class RcinelTest {
    private static final Path TRANSLATIONS = Paths.get(System.getProperty("rcinel.fixtures", "../spec/fixtures")).resolve("translations");
    private static final String SAVE = "0192f3a0-1c4e-7a10-8000-000000000001";

    @AfterEach
    void reset() {
        Rcinel.reset();
    }

    @Test
    void showsTheEnglishTextBeforeAnythingIsLoaded() {
        Rcinel.reset();
        assertEquals("Save", Rcinel.translate(SAVE, "Save"));
        assertEquals("", Rcinel.language());
    }

    @Test
    void loadSwitchesTheLanguageAndTellsListeners() throws IOException {
        AtomicInteger calls = new AtomicInteger();
        Runnable listener = calls::incrementAndGet;
        Rcinel.addListener(listener);
        try {
            Rcinel.load("de-AT", TRANSLATIONS);
            assertEquals("de-AT", Rcinel.language());
            assertEquals("Sichern", Rcinel.translate(SAVE, "Save"));
            assertEquals(1, calls.get());
        } finally {
            Rcinel.removeListener(listener);
        }
    }

    @Test
    void loadsFromTheClassPath() throws IOException {
        // src/test/resources/translations/de.json
        Rcinel.load("de");
        assertEquals("Speichern (Ressource)", Rcinel.translate(SAVE, "Save"));
        assertFalse(Rcinel.tryLoad("fr"));
        assertEquals("de", Rcinel.language());
    }

    @Test
    void tryLoadKeepsTheLanguageWhenTheAppShipsNoFile() throws IOException {
        Rcinel.load("de", TRANSLATIONS);
        assertFalse(Rcinel.tryLoad("fr", TRANSLATIONS));
        assertTrue(Rcinel.tryLoad("de_AT", TRANSLATIONS));
        assertEquals("Sichern", Rcinel.translate(SAVE, "Save"));
    }

    @Test
    void fillsNumberedPlaceholdersAndLeavesTheRest() {
        Rcinel.use(TranslationSet.parse("de", "{\"a\": \"{0} von {1} für {name}, isn't {9}\"}"));
        assertEquals("3 von 5 für {name}, isn't {9}", Rcinel.translate("a", "{0} of {1}", 3, 5));
    }

    @Test
    void readsStreamsWithABom() throws IOException {
        byte[] bytes = "﻿{\"a\": \"b\"}".getBytes(StandardCharsets.UTF_8);
        assertEquals("b", TranslationSet.read("x", new ByteArrayInputStream(bytes)).get("a", "fallback"));
    }
}
