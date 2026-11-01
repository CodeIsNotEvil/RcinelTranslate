"""Reads a translation file: one JSON object whose values are all strings (docs/format.md).

The other clients carry a hand-written reader because their standard libraries have no JSON parser.
Python's has one, so this is json.loads with the checks it doesn't do by itself: a repeated key
(json keeps the last one silently, the other clients reject the file) and values that aren't
strings. spec/fixtures checks that the result matches the C#, Java, JavaScript and Rust clients.
"""

from __future__ import annotations

import json
from typing import Dict, List, Tuple


class TranslationFormatError(ValueError):
    """A translation file is not a flat JSON object of strings. The message says where."""


class _NotFlat(Exception):
    def __init__(self, message: str) -> None:
        super().__init__(message)
        self.message = message


def _pairs(pairs: List[Tuple[str, object]]) -> Dict[str, object]:
    result: Dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise _NotFlat(f'the key "{key}" appears twice')
        result[key] = value
    return result


def read_flat_json(text: str) -> Dict[str, str]:
    """The entries of a translation file, in file order.

    Raises TranslationFormatError when the text is not a flat JSON object of strings.
    """
    if not isinstance(text, str):
        raise TypeError("read_flat_json needs the file text as a str")
    # A BOM is not JSON, but Windows editors write one; json.loads refuses it.
    if text.startswith("\ufeff"):
        text = text[1:]
    try:
        # strict=True (the default) rejects raw control characters inside strings, like the
        # other clients; NaN and Infinity can't get through because values must be strings.
        data = json.loads(text, object_pairs_hook=_pairs)
    except _NotFlat as e:
        raise TranslationFormatError(e.message + ".") from None
    except json.JSONDecodeError as e:
        raise TranslationFormatError(f"Line {e.lineno}, column {e.colno}: {e.msg}.") from None
    if not isinstance(data, dict):
        raise TranslationFormatError("expected a JSON object")
    for key, value in data.items():
        if not isinstance(value, str):
            raise TranslationFormatError(f'"{key}" is not a string (translation files hold only strings).')
    return data  # type: ignore[return-value]
