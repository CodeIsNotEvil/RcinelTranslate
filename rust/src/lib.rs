//! The Rust client of RcinelTranslate.
//!
//! Load a language once at start (and again when the user picks another), then write every UI text
//! as `translate("UUIDv7", "English text")`, the same call as in the C#, Java, JavaScript and Python
//! clients. The English text is the fallback and the source of truth: `rtr scan` collects the calls
//! into the source file, and an id without a translation shows the English text.
//!
//! ```
//! use rcinel_translate::{translate, use_set, TranslationSet};
//!
//! // Usually rcinel_translate::try_load("de") with the files next to the executable.
//! let de = TranslationSet::parse("de", r#"{"01a192e3-5bcc-7000-a8d2-327b1cb0f737": "Einstellungen"}"#).unwrap();
//! use_set(de);
//! assert_eq!(translate("01a192e3-5bcc-7000-a8d2-327b1cb0f737", "Settings"), "Einstellungen");
//! assert_eq!(translate("01a192e3-5bcc-7000-a8d2-0000000000ff", "Not translated"), "Not translated");
//! # rcinel_translate::reset();
//! ```

mod reader;

use std::collections::HashMap;
use std::fmt;
use std::io;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex, OnceLock, RwLock};

pub use reader::TranslationFormatError;

/// The translations of one language, read from the shipped files. Immutable, so one set can be
/// shared by every thread of the app.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TranslationSet {
    language: String,
    texts: HashMap<String, String>,
}

/// Why loading failed.
#[derive(Debug)]
pub enum Error {
    /// No file of the language's fallback chain exists (or the language is empty).
    NotFound { language: String, directory: PathBuf, looked_for: Vec<String> },
    /// A file exists but isn't a valid translation file. The message names the file.
    Format(TranslationFormatError),
    /// Reading a file failed for another reason, such as permissions.
    Io { path: PathBuf, source: io::Error },
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Error::NotFound { language, directory, looked_for } => write!(
                f,
                "There is no translation file for \"{}\" in {} (looked for {}).",
                language,
                directory.display(),
                looked_for.join(", ")
            ),
            Error::Format(e) => fmt::Display::fmt(e, f),
            Error::Io { path, source } => write!(f, "{}: {}", path.display(), source),
        }
    }
}

impl std::error::Error for Error {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Error::Format(e) => Some(e),
            Error::Io { source, .. } => Some(source),
            Error::NotFound { .. } => None,
        }
    }
}

impl From<TranslationFormatError> for Error {
    fn from(e: TranslationFormatError) -> Self {
        Error::Format(e)
    }
}

impl TranslationSet {
    /// An empty set: every id shows the English text of the call.
    pub fn empty() -> Self {
        Self { language: String::new(), texts: HashMap::new() }
    }

    /// The language code, such as "de" or "de-AT". Empty for [`TranslationSet::empty`].
    pub fn language(&self) -> &str {
        &self.language
    }

    /// How many ids have a translation.
    pub fn len(&self) -> usize {
        self.texts.len()
    }

    /// True when no id has a translation.
    pub fn is_empty(&self) -> bool {
        self.texts.is_empty()
    }

    /// True when `id` has a translation.
    pub fn contains(&self, id: &str) -> bool {
        self.texts.contains_key(id)
    }

    /// Every id that has a translation, in no particular order.
    pub fn ids(&self) -> impl Iterator<Item = &str> {
        self.texts.keys().map(String::as_str)
    }

    /// The translation of `id`, if there is one.
    pub fn get(&self, id: &str) -> Option<&str> {
        self.texts.get(id).map(String::as_str)
    }

    /// The translation of `id`, or `fallback` when there is none.
    pub fn get_or<'a>(&'a self, id: &str, fallback: &'a str) -> &'a str {
        self.get(id).unwrap_or(fallback)
    }

    /// Reads a set from the text of a translation file, for files embedded with `include_str!`.
    pub fn parse(language: impl Into<String>, text: &str) -> Result<Self, TranslationFormatError> {
        Ok(Self { language: language.into(), texts: reader::read_flat_json(text)?.into_iter().collect() })
    }

    /// Reads a set from one translation file.
    pub fn from_file(language: impl Into<String>, path: impl AsRef<Path>) -> Result<Self, Error> {
        let path = path.as_ref();
        let text = std::fs::read_to_string(path).map_err(|source| Error::Io { path: path.to_path_buf(), source })?;
        // Without the path the log says "Line 3, column 7" and nobody knows which file.
        Self::parse(language, &text).map_err(|e| Error::Format(e.within(path.display())))
    }

    /// Loads `language` from `directory` with its fallback chain: "de-AT" reads de.json and then
    /// de-AT.json on top of it. Returns `Ok(None)` when no file of the chain exists or the language
    /// is empty (the "C" locale has none).
    pub fn try_load(language: &str, directory: impl AsRef<Path>) -> Result<Option<Self>, Error> {
        let directory = directory.as_ref();
        let chain = fallback_chain(language);
        let mut merged = HashMap::new();
        let mut found = false;
        // General first, so the specific files win.
        for code in chain.iter().rev() {
            let path = directory.join(format!("{}.json", code));
            if !path.is_file() {
                continue;
            }
            found = true;
            merged.extend(Self::from_file(code.as_str(), &path)?.texts);
        }
        Ok(if found { Some(Self { language: language.to_string(), texts: merged }) } else { None })
    }

    /// Like [`TranslationSet::try_load`], but a missing language is [`Error::NotFound`].
    pub fn load(language: &str, directory: impl AsRef<Path>) -> Result<Self, Error> {
        let directory = directory.as_ref();
        Self::try_load(language, directory)?.ok_or_else(|| Error::NotFound {
            language: language.to_string(),
            directory: directory.to_path_buf(),
            looked_for: fallback_chain(language),
        })
    }
}

/// The file names, without .json, that make up a language, most specific first: "zh-Hant-TW" gives
/// zh-Hant-TW, zh-Hant, zh. Underscores count as dashes, so a POSIX locale such as "de_AT" works
/// too. Empty for an empty language.
pub fn fallback_chain(language: &str) -> Vec<String> {
    let mut code = language.trim().replace('_', "-");
    let mut chain = Vec::new();
    if code.is_empty() {
        return chain;
    }
    loop {
        chain.push(code.clone());
        match code.rfind('-') {
            Some(dash) if dash > 0 => code.truncate(dash),
            _ => return chain,
        }
    }
}

/// Fills `{key}` placeholders from `args`: `("0", &3)` fills `{0}`, `("name", &name)` fills
/// `{name}`. Placeholders without a value stay as they are, so a typo shows up as "{nmae}".
pub fn format_text(text: &str, args: &[(&str, &dyn fmt::Display)]) -> String {
    if args.is_empty() {
        return text.to_string();
    }
    let mut out = String::with_capacity(text.len() + 16);
    let mut rest = text;
    while let Some(open) = rest.find('{') {
        out.push_str(&rest[..open]);
        let after = &rest[open + 1..];
        let close = after.find('}');
        let key = close.map(|c| &after[..c]);
        let value = key
            .filter(|k| !k.is_empty() && k.chars().all(|c| c.is_alphanumeric() || c == '_' || c == '.'))
            .and_then(|k| args.iter().find(|(name, _)| *name == k));
        match (value, close) {
            (Some((_, v)), Some(c)) => {
                out.push_str(&v.to_string());
                rest = &after[c + 1..];
            }
            _ => {
                out.push('{');
                rest = after;
            }
        }
    }
    out.push_str(rest);
    out
}

// One reference swap per language change. Readers clone the Arc under a short read lock, so a
// thread that reads during a switch still gets a complete set, old or new.
static CURRENT: RwLock<Option<Arc<TranslationSet>>> = RwLock::new(None);
static EMPTY: OnceLock<Arc<TranslationSet>> = OnceLock::new();
static DIRECTORY: RwLock<Option<PathBuf>> = RwLock::new(None);

type Listener = Arc<dyn Fn(&TranslationSet) + Send + Sync>;
static LISTENERS: Mutex<Vec<(u64, Listener)>> = Mutex::new(Vec::new());
static NEXT_LISTENER: Mutex<u64> = Mutex::new(0);

/// Identifies a listener added with [`on_change`], to remove it again.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct ListenerId(u64);

/// The loaded set. Empty until a language is loaded.
pub fn current() -> Arc<TranslationSet> {
    // A panic in a listener elsewhere must not stop translating: take the value of a poisoned lock.
    let guard = CURRENT.read().unwrap_or_else(|e| e.into_inner());
    match guard.as_ref() {
        Some(set) => Arc::clone(set),
        None => Arc::clone(EMPTY.get_or_init(|| Arc::new(TranslationSet::empty()))),
    }
}

/// The translation of `id`, or `english` when the loaded language has none.
pub fn translate(id: &str, english: &str) -> String {
    current().get_or(id, english).to_string()
}

/// [`translate`], then fills `{0}` / `{name}` placeholders (see [`format_text`]):
/// `translate_with(id, "Saved {0} settings", &[("0", &count)])`.
pub fn translate_with(id: &str, english: &str, args: &[(&str, &dyn fmt::Display)]) -> String {
    format_text(current().get_or(id, english), args)
}

/// The loaded language, such as "de". Empty while the app shows its English texts.
pub fn language() -> String {
    current().language().to_string()
}

/// Where [`load`] and [`try_load`] look: the "translations" folder next to the executable, unless
/// [`set_directory`] chose another.
pub fn directory() -> PathBuf {
    if let Some(dir) = DIRECTORY.read().unwrap_or_else(|e| e.into_inner()).as_ref() {
        return dir.clone();
    }
    std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(Path::to_path_buf))
        .unwrap_or_else(|| PathBuf::from("."))
        .join("translations")
}

/// Sets the folder [`load`] and [`try_load`] read from.
pub fn set_directory(path: impl Into<PathBuf>) {
    *DIRECTORY.write().unwrap_or_else(|e| e.into_inner()) = Some(path.into());
}

/// Switches to a set the app built itself, for example from an embedded file. (`use` is a keyword,
/// hence the name.)
pub fn use_set(set: TranslationSet) {
    let set = Arc::new(set);
    *CURRENT.write().unwrap_or_else(|e| e.into_inner()) = Some(Arc::clone(&set));
    // Call listeners outside the locks, so a listener may translate or even switch again.
    let listeners: Vec<Listener> =
        LISTENERS.lock().unwrap_or_else(|e| e.into_inner()).iter().map(|(_, l)| Arc::clone(l)).collect();
    for listener in listeners {
        listener(&set);
    }
}

/// Loads `language` from [`directory`] with its fallback chain and switches to it.
pub fn load(language: &str) -> Result<(), Error> {
    load_from(language, directory())
}

/// Loads `language` from `directory` with its fallback chain and switches to it.
pub fn load_from(language: &str, directory: impl AsRef<Path>) -> Result<(), Error> {
    use_set(TranslationSet::load(language, directory)?);
    Ok(())
}

/// Loads `language` from [`directory`] if the app ships it and returns `Ok(true)`; otherwise keeps
/// the current language and returns `Ok(false)`. Handy for following the system language.
pub fn try_load(language: &str) -> Result<bool, Error> {
    try_load_from(language, directory())
}

/// Like [`try_load`], from `directory`.
pub fn try_load_from(language: &str, directory: impl AsRef<Path>) -> Result<bool, Error> {
    match TranslationSet::try_load(language, directory)? {
        Some(set) => {
            use_set(set);
            Ok(true)
        }
        None => Ok(false),
    }
}

/// Goes back to the English texts of the calls.
pub fn reset() {
    use_set(TranslationSet::empty());
}

/// Calls `listener` after every language change, so a UI can redraw.
pub fn on_change(listener: impl Fn(&TranslationSet) + Send + Sync + 'static) -> ListenerId {
    let mut next = NEXT_LISTENER.lock().unwrap_or_else(|e| e.into_inner());
    *next += 1;
    let id = *next;
    LISTENERS.lock().unwrap_or_else(|e| e.into_inner()).push((id, Arc::new(listener)));
    ListenerId(id)
}

/// Removes a listener added with [`on_change`].
pub fn remove_listener(id: ListenerId) {
    LISTENERS.lock().unwrap_or_else(|e| e.into_inner()).retain(|(i, _)| *i != id.0);
}
