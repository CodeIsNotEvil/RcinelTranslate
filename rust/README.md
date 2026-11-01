# rcinel-translate

The Rust client of [RcinelTranslate](https://github.com/CodeIsNotEvil/RcinelTranslate). No
dependencies, Rust 1.70+.

```rust
use rcinel_translate::{translate, translate_with, try_load};

// Reads translations/<language>.json next to the executable.
try_load("de")?;

let title = translate("01a192e3-5bcc-7000-a8d2-327b1cb0f737", "Settings");
let toast = translate_with("01a192e3-5bd4-7007-bb84-212f497c373e", "Saved {0} settings", &[("0", &count)]);
let greeting = translate_with("01a192e3-5bd4-7008-9a54-036fafe0c7af", "Welcome back, {name}!", &[("name", &name)]);
```

| API | |
|---|---|
| `translate(id, english)` | The translation, or `english`, as an owned `String` (thread-safe across language switches). |
| `translate_with(id, english, &[("name", &value)])` | Also fills `{0}` / `{name}` placeholders; unknown ones stay. |
| `load(language)` / `load_from(language, dir)` | Loads the language with its fallback chain (`de-AT` reads `de.json`, then `de-AT.json`). `Error::NotFound` when there is no file. |
| `try_load(language)` / `try_load_from(language, dir)` | `Ok(false)` and the current language stays when there is no file (or the language is empty). |
| `set_directory(path)` | The folder `load` and `try_load` read; `translations` next to the executable otherwise. |
| `use_set(set)` | Switches to a `TranslationSet` you built, for example `TranslationSet::parse("de", include_str!("../translations/de.json"))`. |
| `on_change(listener)` / `remove_listener(id)` | Called after every switch, so a UI can redraw. |
| `language()`, `current()`, `reset()` | The loaded language, the loaded set, and back to the English texts. |
