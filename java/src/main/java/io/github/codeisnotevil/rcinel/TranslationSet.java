package io.github.codeisnotevil.rcinel;

import java.io.ByteArrayOutputStream;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * The translations of one language, read from the shipped files. Immutable, so one set can be
 * shared by every thread of the app.
 */
public final class TranslationSet {
    /** An empty set: every id falls back to the English text of the call. */
    public static final TranslationSet EMPTY = new TranslationSet("", Collections.<String, String>emptyMap());

    private final String language;
    private final Map<String, String> texts;

    private TranslationSet(String language, Map<String, String> texts) {
        this.language = language;
        this.texts = Collections.unmodifiableMap(texts);
    }

    /** The language code, such as "de" or "de-AT". Empty for {@link #EMPTY}. */
    public String language() {
        return language;
    }

    /** How many ids have a translation. */
    public int size() {
        return texts.size();
    }

    /** Every id that has a translation. */
    public Set<String> ids() {
        return texts.keySet();
    }

    public boolean contains(String id) {
        return id != null && texts.containsKey(id);
    }

    /** The translation of {@code id}, or {@code fallback} when there is none. */
    public String get(String id, String fallback) {
        String text = id == null ? null : texts.get(id);
        return text != null ? text : fallback;
    }

    /** Reads a set from the text of a translation file. */
    public static TranslationSet parse(String language, String json) {
        if (language == null) {
            throw new NullPointerException("language");
        }
        return new TranslationSet(language, new HashMap<>(FlatJsonReader.read(json)));
    }

    /** Reads a set from a stream as UTF-8, for Android assets or class path resources. The stream is not closed. */
    public static TranslationSet read(String language, InputStream in) throws IOException {
        ByteArrayOutputStream buffer = new ByteArrayOutputStream();
        byte[] chunk = new byte[8192];
        int n;
        while ((n = in.read(chunk)) != -1) {
            buffer.write(chunk, 0, n);
        }
        return parse(language, new String(buffer.toByteArray(), StandardCharsets.UTF_8));
    }

    /** Reads a set from one translation file. */
    public static TranslationSet fromFile(String language, Path file) throws IOException {
        try {
            return parse(language, new String(Files.readAllBytes(file), StandardCharsets.UTF_8));
        } catch (TranslationFormatException e) {
            // Without the path the log says "Line 3, column 7" and nobody knows which file.
            throw new TranslationFormatException(file + ": " + e.getMessage(), e);
        }
    }

    /**
     * Loads {@code language} from {@code directory} with its fallback chain: "de-AT" reads de.json
     * and then de-AT.json on top of it, so a regional file only needs the strings that differ.
     *
     * @throws FileNotFoundException when no file of the chain exists
     */
    public static TranslationSet load(String language, Path directory) throws IOException {
        TranslationSet set = tryLoad(language, directory);
        if (set == null) {
            throw new FileNotFoundException("There is no translation file for \"" + language + "\" in " + directory
                + " (looked for " + fallbackChain(language) + ").");
        }
        return set;
    }

    /** Like {@link #load(String, Path)}, but returns null when no file of the chain exists. */
    public static TranslationSet tryLoad(String language, Path directory) throws IOException {
        Map<String, String> merged = new HashMap<>();
        boolean found = false;
        List<String> chain = fallbackChain(language);
        // General first, so the specific files win.
        for (int i = chain.size() - 1; i >= 0; i--) {
            Path file = directory.resolve(chain.get(i) + ".json");
            if (Files.isRegularFile(file)) {
                found = true;
                merged.putAll(fromFile(chain.get(i), file).texts);
            }
        }
        return found ? new TranslationSet(language, merged) : null;
    }

    /**
     * Loads {@code language} from class path resources in {@code folder} (such as "translations"),
     * with the same fallback chain. Returns null when no resource of the chain exists.
     */
    public static TranslationSet tryLoadResource(String language, ClassLoader loader, String folder) throws IOException {
        Map<String, String> merged = new HashMap<>();
        boolean found = false;
        List<String> chain = fallbackChain(language);
        String prefix = folder.isEmpty() || folder.endsWith("/") ? folder : folder + "/";
        for (int i = chain.size() - 1; i >= 0; i--) {
            String name = prefix + chain.get(i) + ".json";
            try (InputStream in = loader.getResourceAsStream(name)) {
                if (in == null) {
                    continue;
                }
                found = true;
                try {
                    merged.putAll(read(chain.get(i), in).texts);
                } catch (TranslationFormatException e) {
                    throw new TranslationFormatException(name + ": " + e.getMessage(), e);
                }
            }
        }
        return found ? new TranslationSet(language, merged) : null;
    }

    /**
     * The file names, without .json, that make up {@code language}, most specific first:
     * "zh-Hant-TW" gives zh-Hant-TW, zh-Hant, zh. Underscores count as dashes, so Java's
     * {@code Locale.toString()} ("de_AT") works too.
     */
    public static List<String> fallbackChain(String language) {
        if (language == null || language.trim().isEmpty()) {
            throw new IllegalArgumentException("A language code is needed, such as \"de\".");
        }
        String code = language.trim().replace('_', '-');
        List<String> chain = new ArrayList<>();
        while (true) {
            chain.add(code);
            int dash = code.lastIndexOf('-');
            if (dash <= 0) {
                return chain;
            }
            code = code.substring(0, dash);
        }
    }
}
