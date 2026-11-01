# rcinel-translate

The Python client of [RcinelTranslate](https://github.com/CodeIsNotEvil/RcinelTranslate): loads a
translation set for a language and translates with `translate("UUIDv7", "English text")`, the same
call as in the C#, Java, JavaScript and Rust clients. Standard library only, Python 3.9+, typed.

```python
import locale
from rcinel_translate import translate, try_load

try_load(locale.getlocale()[0] or "", "translations")
title = translate("01a192e3-5bcc-7000-a8d2-327b1cb0f737", "Settings")
toast = translate("01a192e3-5bd4-7007-bb84-212f497c373e", "Saved {0} settings", count)
greeting = translate("01a192e3-5bd4-7008-9a54-036fafe0c7af", "Welcome back, {name}!", name=name)
```

| API | |
|---|---|
| `translate(id, english, *args, **kwargs)` | The translation, or `english`. Fills `{0}` from args and `{name}` from kwargs; unknown placeholders stay. |
| `load(language, directory=None)` | Loads the language with its fallback chain (`de-AT` reads `de.json`, then `de-AT.json`); raises `FileNotFoundError` when there is no file. |
| `try_load(language, directory=None)` | Like `load`, but returns `False` and keeps the current language when there is no file (or the language is empty). |
| `set_directory(path)` | The default folder for `load` and `try_load`; `./translations` otherwise. |
| `use(set)` | Switches to a `TranslationSet` you built, for example `TranslationSet.parse("de", files("myapp").joinpath("translations/de.json").read_text())` for package data. |
| `on_change(listener)` | Called after every switch; returns a function that removes the listener. |
| `language()`, `current()`, `reset()` | The loaded language, the loaded set, and back to the English texts. |

The file format and the rest of the tooling are described in the main repository.
