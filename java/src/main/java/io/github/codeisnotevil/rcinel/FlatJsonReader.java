package io.github.codeisnotevil.rcinel;

import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Reads a translation file: one JSON object whose values are all strings (docs/format.md).
 * Hand-written because the JDK has no JSON parser and the client must not add a dependency to the
 * apps that use it. It is the twin of the C# client's FlatJsonReader; both run spec/fixtures, so
 * keep them in step.
 */
final class FlatJsonReader {
    private final String text;
    private int position;

    private FlatJsonReader(String text) {
        this.text = text;
    }

    /** Reads the whole object. Keys keep file order. */
    static Map<String, String> read(String text) {
        if (text == null) {
            throw new NullPointerException("text");
        }
        return new FlatJsonReader(text).readObject();
    }

    private Map<String, String> readObject() {
        Map<String, String> result = new LinkedHashMap<>();
        // A BOM is not JSON, but Windows editors write one; rejecting it would break files that
        // look fine to whoever saved them.
        if (!text.isEmpty() && text.charAt(0) == (char) 0xFEFF) {
            position = 1;
        }
        skipWhitespace();
        expect('{');
        skipWhitespace();
        if (peek() == '}') {
            position++;
        } else {
            while (true) {
                skipWhitespace();
                int keyStart = position;
                String key = readString("a key in quotes");
                skipWhitespace();
                expect(':');
                skipWhitespace();
                String value = readString("a string value (translation files hold only strings)");
                // Legal JSON, but parsers disagree on which value wins, so all three clients reject it.
                if (result.containsKey(key)) {
                    throw error("the key \"" + key + "\" appears twice", keyStart);
                }
                result.put(key, value);
                skipWhitespace();
                char next = peek();
                if (next == ',') {
                    position++;
                    continue;
                }
                if (next == '}') {
                    position++;
                    break;
                }
                throw error("expected ',' or '}'", position);
            }
        }
        skipWhitespace();
        if (position < text.length()) {
            throw error("unexpected text after the closing '}'", position);
        }
        return result;
    }

    private String readString(String what) {
        if (peek() != '"') {
            throw error("expected " + what, position);
        }
        position++;
        StringBuilder builder = new StringBuilder();
        while (true) {
            if (position >= text.length()) {
                throw error("a string is not closed", position);
            }
            char c = text.charAt(position++);
            if (c == '"') {
                return builder.toString();
            }
            if (c < ' ') {
                throw error("control characters (such as line breaks) must be escaped, for example as \\n", position - 1);
            }
            if (c != '\\') {
                builder.append(c);
                continue;
            }
            if (position >= text.length()) {
                throw error("a string is not closed", position);
            }
            char escape = text.charAt(position++);
            switch (escape) {
                case '"': builder.append('"'); break;
                case '\\': builder.append('\\'); break;
                case '/': builder.append('/'); break;
                case 'b': builder.append('\b'); break;
                case 'f': builder.append('\f'); break;
                case 'n': builder.append('\n'); break;
                case 'r': builder.append('\r'); break;
                case 't': builder.append('\t'); break;
                case 'u':
                    if (position + 4 > text.length()) {
                        throw error("\\u needs four hex digits", position - 2);
                    }
                    int code = 0;
                    for (int i = 0; i < 4; i++) {
                        int digit = Character.digit(text.charAt(position + i), 16);
                        if (digit < 0) {
                            throw error("\\u needs four hex digits", position - 2);
                        }
                        code = code * 16 + digit;
                    }
                    // Java strings are UTF-16 too, so the two halves of an emoji's surrogate pair
                    // come out right when appended one after the other.
                    builder.append((char) code);
                    position += 4;
                    break;
                default:
                    throw error("unknown escape \\" + escape, position - 2);
            }
        }
    }

    private void skipWhitespace() {
        while (position < text.length()) {
            char c = text.charAt(position);
            if (c != ' ' && c != '\t' && c != '\n' && c != '\r') {
                return;
            }
            position++;
        }
    }

    private char peek() {
        return position < text.length() ? text.charAt(position) : '\0';
    }

    private void expect(char expected) {
        if (peek() != expected) {
            throw error("expected '" + expected + "'", position);
        }
        position++;
    }

    private TranslationFormatException error(String message, int at) {
        int line = 1;
        int column = 1;
        for (int i = 0; i < at && i < text.length(); i++) {
            if (text.charAt(i) == '\n') {
                line++;
                column = 1;
            } else {
                column++;
            }
        }
        return new TranslationFormatException("Line " + line + ", column " + column + ": " + message + ".");
    }
}
