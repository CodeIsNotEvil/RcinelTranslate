package io.github.codeisnotevil.rcinel;

import java.io.FileNotFoundException;
import java.io.IOException;
import java.nio.file.Path;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;

/**
 * The app-wide entry point. Load a language once at start (and again when the user picks another),
 * then write every UI text as {@code Rcinel.translate("UUIDv7", "English text")}.
 *
 * <p>The English text in the call is the source of truth and the fallback: `rcinel scan` collects
 * the calls into the source file, and an id without a translation shows the English text. That's
 * why nothing needs configuring before the first call.
 */
public final class Rcinel {
    /** The class path folder {@link #load(String)} reads: put the project's translations folder in your resources. */
    public static final String RESOURCE_FOLDER = "translations";

    // One reference swap per language change; translate only reads it, so it needs no lock.
    private static volatile TranslationSet current = TranslationSet.EMPTY;
    private static final List<Runnable> listeners = new CopyOnWriteArrayList<>();

    private Rcinel() {
    }

    /** The translation of {@code id}, or {@code english} when the loaded language has none. */
    public static String translate(String id, String english) {
        return current.get(id, english);
    }

    /**
     * {@link #translate(String, String)}, then replaces {0}, {1}, ... with {@code args}. Only plain
     * numbered placeholders; named ones such as {name} are left for the app. Unlike
     * {@link java.text.MessageFormat} it leaves apostrophes alone, which translators would
     * otherwise have to double.
     */
    public static String translate(String id, String english, Object... args) {
        String text = translate(id, english);
        StringBuilder result = new StringBuilder(text.length() + 16);
        int i = 0;
        while (i < text.length()) {
            char c = text.charAt(i);
            if (c == '{') {
                int close = text.indexOf('}', i + 1);
                if (close > i + 1) {
                    String inside = text.substring(i + 1, close);
                    if (isDigits(inside)) {
                        int index = Integer.parseInt(inside);
                        if (index < args.length) {
                            result.append(args[index]);
                            i = close + 1;
                            continue;
                        }
                    }
                }
            }
            result.append(c);
            i++;
        }
        return result.toString();
    }

    private static boolean isDigits(String s) {
        if (s.isEmpty() || s.length() > 4) {
            return false;
        }
        for (int i = 0; i < s.length(); i++) {
            if (!Character.isDigit(s.charAt(i))) {
                return false;
            }
        }
        return true;
    }

    /** The loaded set. {@link TranslationSet#EMPTY} until a language is loaded. */
    public static TranslationSet current() {
        return current;
    }

    /** The loaded language, such as "de". Empty while the app shows its English texts. */
    public static String language() {
        return current.language();
    }

    /**
     * Loads {@code language} from class path resources in {@value #RESOURCE_FOLDER}/, with its
     * fallback chain (de-AT, then de).
     *
     * @throws FileNotFoundException when the app ships no file for the language
     */
    public static void load(String language) throws IOException {
        if (!tryLoad(language)) {
            throw new FileNotFoundException("There is no translation resource for \"" + language + "\" in "
                + RESOURCE_FOLDER + "/ (looked for " + TranslationSet.fallbackChain(language) + ").");
        }
    }

    /** Loads {@code language} from files in {@code directory}, with its fallback chain. */
    public static void load(String language, Path directory) throws IOException {
        use(TranslationSet.load(language, directory));
    }

    /**
     * Loads {@code language} from the class path if the app ships it and returns true; otherwise
     * keeps the current set and returns false. Handy for following the system language:
     * {@code Rcinel.tryLoad(Locale.getDefault().toLanguageTag())}.
     */
    public static boolean tryLoad(String language) throws IOException {
        ClassLoader loader = Thread.currentThread().getContextClassLoader();
        if (loader == null) {
            loader = Rcinel.class.getClassLoader();
        }
        TranslationSet set = TranslationSet.tryLoadResource(language, loader, RESOURCE_FOLDER);
        if (set == null) {
            return false;
        }
        use(set);
        return true;
    }

    /** Like {@link #tryLoad(String)}, from files in {@code directory}. */
    public static boolean tryLoad(String language, Path directory) throws IOException {
        TranslationSet set = TranslationSet.tryLoad(language, directory);
        if (set == null) {
            return false;
        }
        use(set);
        return true;
    }

    /** Switches to a set the app built itself, for example from Android assets. */
    public static void use(TranslationSet set) {
        if (set == null) {
            throw new NullPointerException("set");
        }
        current = set;
        for (Runnable listener : listeners) {
            listener.run();
        }
    }

    /** Goes back to the English texts of the calls. */
    public static void reset() {
        use(TranslationSet.EMPTY);
    }

    /** Runs {@code listener} after every language change, so a UI can redraw its texts. */
    public static void addListener(Runnable listener) {
        listeners.add(listener);
    }

    public static void removeListener(Runnable listener) {
        listeners.remove(listener);
    }
}
