package io.github.codeisnotevil.rcinel;

/** A translation file is not a flat JSON object of strings. The message says where. */
public final class TranslationFormatException extends IllegalArgumentException {
    private static final long serialVersionUID = 1L;

    public TranslationFormatException(String message) {
        super(message);
    }

    public TranslationFormatException(String message, Throwable cause) {
        super(message, cause);
    }
}
