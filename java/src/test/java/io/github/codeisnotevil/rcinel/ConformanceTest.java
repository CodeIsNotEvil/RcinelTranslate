package io.github.codeisnotevil.rcinel;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.stream.Stream;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestFactory;

/**
 * spec/fixtures/cases.json, the cases the C#, JavaScript, Python and Rust clients run too. A failure
 * here means the clients disagree about what an app shows.
 */
class ConformanceTest {
    private static final Path FIXTURES = Paths.get(System.getProperty("rcinel.fixtures", "../spec/fixtures"));
    private static final Path TRANSLATIONS = FIXTURES.resolve("translations");
    private static final JsonObject CASES = readCases();

    private static JsonObject readCases() {
        try {
            return JsonParser.parseString(new String(Files.readAllBytes(FIXTURES.resolve("cases.json")), StandardCharsets.UTF_8)).getAsJsonObject();
        } catch (IOException e) {
            throw new IllegalStateException(e);
        }
    }

    @TestFactory
    Stream<DynamicTest> translatesLikeTheOtherClients() {
        List<DynamicTest> tests = new ArrayList<>();
        for (JsonElement element : CASES.getAsJsonArray("translate")) {
            JsonObject c = element.getAsJsonObject();
            String language = c.get("language").getAsString();
            String id = c.get("id").getAsString();
            tests.add(DynamicTest.dynamicTest(language + " " + id, () ->
                assertEquals(c.get("expected").getAsString(), TranslationSet.load(language, TRANSLATIONS).get(id, c.get("english").getAsString()))));
        }
        return tests.stream();
    }

    @Test
    void mergedSetsHaveTheExpectedSize() throws IOException {
        for (Map.Entry<String, JsonElement> count : CASES.getAsJsonObject("counts").entrySet()) {
            assertEquals(count.getValue().getAsInt(), TranslationSet.load(count.getKey(), TRANSLATIONS).size(), count.getKey());
        }
    }

    @Test
    void fallbackChainsGoFromSpecificToGeneral() {
        for (JsonElement element : CASES.getAsJsonArray("fallbackChains")) {
            JsonObject c = element.getAsJsonObject();
            List<String> expected = new ArrayList<>();
            for (JsonElement e : c.getAsJsonArray("chain")) {
                expected.add(e.getAsString());
            }
            assertEquals(expected, TranslationSet.fallbackChain(c.get("language").getAsString()));
        }
    }

    @Test
    void missingLanguagesThrowAndTryLoadReturnsNull() throws IOException {
        for (JsonElement language : CASES.getAsJsonArray("missingLanguages")) {
            assertThrows(FileNotFoundException.class, () -> TranslationSet.load(language.getAsString(), TRANSLATIONS));
            assertNull(TranslationSet.tryLoad(language.getAsString(), TRANSLATIONS));
        }
    }

    @Test
    void emptyLanguagesLoadNothingWithTryLoad() throws IOException {
        for (JsonElement language : CASES.getAsJsonArray("emptyLanguages")) {
            assertNull(TranslationSet.tryLoad(language.getAsString(), TRANSLATIONS));
        }
    }

    @TestFactory
    Stream<DynamicTest> rejectsInvalidFiles() {
        List<DynamicTest> tests = new ArrayList<>();
        for (JsonElement name : CASES.getAsJsonArray("invalid")) {
            String file = name.getAsString();
            tests.add(DynamicTest.dynamicTest(file, () -> {
                TranslationFormatException e = assertThrows(TranslationFormatException.class,
                    () -> TranslationSet.fromFile("x", FIXTURES.resolve("invalid").resolve(file)));
                assertTrue(e.getMessage().contains(file), e.getMessage());
            }));
        }
        return tests.stream();
    }
}
