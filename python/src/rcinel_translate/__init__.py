"""The Python client of RcinelTranslate.

Load a language once at start (and again when the user picks another), then write every UI text
as ``translate("UUIDv7", "English text")``, the same call as in the C#, Java, JavaScript and Rust
clients. The English text is the fallback and the source of truth: ``rtr scan`` collects the calls
into the source file, and an id without a translation shows the English text.

    from rcinel_translate import translate, try_load

    try_load(locale.getlocale()[0] or "")
    title.set_text(translate("01a192e3-5bcc-7000-a8d2-327b1cb0f737", "Settings"))
"""

from __future__ import annotations

import os
import re
import threading
from pathlib import Path
from typing import Callable, Dict, List, Mapping, Optional, Union

from ._reader import TranslationFormatError, read_flat_json

__all__ = [
    "TranslationFormatError",
    "TranslationSet",
    "EMPTY",
    "fallback_chain",
    "format_text",
    "translate",
    "language",
    "current",
    "load",
    "try_load",
    "use",
    "reset",
    "on_change",
    "directory",
    "set_directory",
]

PathLike = Union[str, "os.PathLike[str]"]


def fallback_chain(language: str) -> List[str]:
    """The file names, without .json, that make up a language, most specific first.

    "zh-Hant-TW" gives zh-Hant-TW, zh-Hant, zh. Underscores count as dashes, so a POSIX locale
    such as "de_AT" works too.
    """
    if not isinstance(language, str) or not language.strip():
        raise ValueError('A language code is needed, such as "de".')
    code = language.strip().replace("_", "-")
    chain = []
    while True:
        chain.append(code)
        dash = code.rfind("-")
        if dash <= 0:
            return chain
        code = code[:dash]


class TranslationSet:
    """The translations of one language, read from the shipped files. Immutable."""

    __slots__ = ("_language", "_texts")

    def __init__(self, language: str, texts: Mapping[str, str]) -> None:
        self._language = language
        self._texts: Dict[str, str] = dict(texts)

    @property
    def language(self) -> str:
        """The language code, such as "de" or "de-AT". Empty for EMPTY."""
        return self._language

    def __len__(self) -> int:
        return len(self._texts)

    def __contains__(self, id: object) -> bool:
        return id in self._texts

    def ids(self) -> List[str]:
        """Every id that has a translation."""
        return list(self._texts)

    def get(self, id: str, fallback: str) -> str:
        """The translation of id, or fallback when there is none."""
        return self._texts.get(id, fallback)

    @staticmethod
    def parse(language: str, text: str) -> "TranslationSet":
        """Reads a set from the text of a translation file, for package data or embedded files."""
        return TranslationSet(language, read_flat_json(text))

    @staticmethod
    def from_file(language: str, path: PathLike) -> "TranslationSet":
        """Reads a set from one translation file."""
        path = Path(path)
        try:
            return TranslationSet.parse(language, path.read_text(encoding="utf-8"))
        except TranslationFormatError as e:
            # Without the path the log says "Line 3, column 7" and nobody knows which file.
            raise TranslationFormatError(f"{path}: {e}") from None

    @staticmethod
    def try_load(language: str, directory: PathLike) -> Optional["TranslationSet"]:
        """Loads language from directory with its fallback chain ("de-AT" reads de.json, then
        de-AT.json on top), or returns None when no file of the chain exists or the language is
        empty (the "C" locale has none)."""
        if not isinstance(language, str) or not language.strip():
            return None
        merged: Dict[str, str] = {}
        found = False
        # General first, so the specific files win.
        for code in reversed(fallback_chain(language)):
            path = Path(directory, code + ".json")
            if path.is_file():
                found = True
                merged.update(TranslationSet.from_file(code, path)._texts)
        return TranslationSet(language, merged) if found else None

    @staticmethod
    def load(language: str, directory: PathLike) -> "TranslationSet":
        """Like try_load, but raises FileNotFoundError when no file of the chain exists."""
        found = TranslationSet.try_load(language, directory)
        if found is None:
            raise FileNotFoundError(
                f'There is no translation file for "{language}" in {directory} '
                f"(looked for {', '.join(fallback_chain(language))})."
            )
        return found

    def __repr__(self) -> str:
        return f"TranslationSet({self._language!r}, {len(self._texts)} texts)"


EMPTY = TranslationSet("", {})
"""An empty set: every id shows the English text of the call."""

_PLACEHOLDER = re.compile(r"\{([\w.]+)\}")


def format_text(text: str, *args: object, **kwargs: object) -> str:
    """Fills {0}, {1}, ... from args and {name} from kwargs. (Not called "format", which
    `from rcinel_translate import *` would shadow the builtin with.)

    Unlike str.format, unknown placeholders stay as they are and other braces are left alone, so
    a translation with a typo shows "{nmae}" instead of raising KeyError in the middle of a UI.
    """
    if not args and not kwargs:
        return text

    def fill(match: "re.Match[str]") -> str:
        key = match.group(1)
        if key.isdigit() and int(key) < len(args):
            return str(args[int(key)])
        if key in kwargs:
            return str(kwargs[key])
        return match.group(0)

    return _PLACEHOLDER.sub(fill, text)


# One reference swap per language change; translate only reads it, and assigning a module global
# is atomic, so readers on other threads always see a complete set.
_current: TranslationSet = EMPTY
_listeners: List[Callable[[TranslationSet], None]] = []
_listeners_lock = threading.Lock()
_directory: Path = Path.cwd() / "translations"


def translate(id: str, english: str, *args: object, **kwargs: object) -> str:
    """The translation of id, or english when the loaded language has none.

    With args or kwargs, fills {0} / {name} placeholders (see format_text).
    """
    return format_text(_current.get(id, english), *args, **kwargs)


def language() -> str:
    """The loaded language, such as "de". Empty while the app shows its English texts."""
    return _current.language


def current() -> TranslationSet:
    """The loaded set. EMPTY until a language is loaded."""
    return _current


def directory() -> Path:
    """Where load and try_load look by default: ./translations of the working directory at import."""
    return _directory


def set_directory(path: PathLike) -> None:
    """Sets the folder load and try_load read from, for example next to the app's main module."""
    global _directory
    _directory = Path(path)


def use(set: TranslationSet) -> None:
    """Switches to a set the app built itself, for example from package data."""
    global _current
    if not isinstance(set, TranslationSet):
        raise TypeError("use needs a TranslationSet; build one with TranslationSet.parse(language, text).")
    _current = set
    with _listeners_lock:
        listeners = list(_listeners)
    for listener in listeners:
        listener(set)


def load(language: str, directory: Optional[PathLike] = None) -> None:
    """Loads language (with its fallback chain) from directory, or directory(), and switches to it.

    Raises FileNotFoundError when the app ships no file for the language.
    """
    use(TranslationSet.load(language, _directory if directory is None else directory))


def try_load(language: str, directory: Optional[PathLike] = None) -> bool:
    """Loads language if the app ships it and returns True; otherwise keeps the current language
    and returns False. Handy for following the system: try_load(locale.getlocale()[0] or "")."""
    found = TranslationSet.try_load(language, _directory if directory is None else directory)
    if found is None:
        return False
    use(found)
    return True


def reset() -> None:
    """Goes back to the English texts of the calls."""
    use(EMPTY)


def on_change(listener: Callable[[TranslationSet], None]) -> Callable[[], None]:
    """Calls listener after every language change, so a UI can redraw. Returns a function that
    removes it again."""
    with _listeners_lock:
        _listeners.append(listener)

    def remove() -> None:
        with _listeners_lock:
            if listener in _listeners:
                _listeners.remove(listener)

    return remove
