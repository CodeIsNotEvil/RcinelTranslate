//! The app-wide functions. They share one global state, so every test takes the same lock.

use rcinel_translate::*;
use std::path::PathBuf;
use std::sync::{Arc, Mutex, MutexGuard};

static SERIAL: Mutex<()> = Mutex::new(());

fn serial() -> MutexGuard<'static, ()> {
    let guard = SERIAL.lock().unwrap_or_else(|e| e.into_inner());
    reset();
    guard
}

fn translations() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("..").join("spec").join("fixtures").join("translations")
}

const SAVE: &str = "0192f3a0-1c4e-7a10-8000-000000000001";

#[test]
fn shows_the_english_text_before_anything_is_loaded() {
    let _g = serial();
    assert_eq!(translate(SAVE, "Save"), "Save");
    assert_eq!(language(), "");
}

#[test]
fn load_switches_the_language_and_tells_listeners() {
    let _g = serial();
    let seen = Arc::new(Mutex::new(Vec::new()));
    let sink = Arc::clone(&seen);
    let id = on_change(move |set| sink.lock().unwrap().push(set.language().to_string()));
    load_from("de_AT", translations()).unwrap();
    remove_listener(id);
    reset();
    assert_eq!(*seen.lock().unwrap(), vec!["de_AT".to_string()]);
}

#[test]
fn translates_after_loading() {
    let _g = serial();
    load_from("de-AT", translations()).unwrap();
    assert_eq!(translate(SAVE, "Save"), "Sichern");
    assert_eq!(translate("0192f3a0-1c4e-7a10-8000-000000000002", "Cancel"), "Abbrechen");
    assert_eq!(language(), "de-AT");
}

#[test]
fn try_load_keeps_the_language_when_the_app_ships_no_file() {
    let _g = serial();
    load_from("de", translations()).unwrap();
    assert!(!try_load_from("fr", translations()).unwrap());
    assert!(!try_load_from("", translations()).unwrap());
    assert_eq!(language(), "de");
    assert!(matches!(load_from("fr", translations()), Err(Error::NotFound { .. })));
}

#[test]
fn the_default_directory_can_be_set() {
    let _g = serial();
    let old = directory();
    set_directory(translations());
    assert!(try_load("de").unwrap());
    set_directory(old);
    assert_eq!(translate(SAVE, "Save"), "Speichern");
}

#[test]
fn placeholders_are_filled_and_unknown_ones_stay() {
    let _g = serial();
    use_set(TranslationSet::parse("de", r#"{"a": "{0} von {1} für {name}, {missing} { }"}"#).unwrap());
    let name = "Alex";
    assert_eq!(
        translate_with("a", "{0} of {1}", &[("0", &3), ("1", &5), ("name", &name)]),
        "3 von 5 f\u{fc}r Alex, {missing} { }"
    );
    assert_eq!(format_text("{x}", &[]), "{x}");
}

#[test]
fn embedded_files_parse_with_a_bom() {
    let set = TranslationSet::parse("x", "\u{FEFF}{\"a\": \"b\"}").unwrap();
    assert_eq!(set.get_or("a", "fallback"), "b");
}

#[test]
fn readers_on_other_threads_always_see_a_whole_set() {
    let _g = serial();
    let a = TranslationSet::parse("a", r#"{"k": "A"}"#).unwrap();
    let b = TranslationSet::parse("b", r#"{"k": "B"}"#).unwrap();
    let stop = Arc::new(std::sync::atomic::AtomicBool::new(false));
    let flag = Arc::clone(&stop);
    let reader = std::thread::spawn(move || {
        while !flag.load(std::sync::atomic::Ordering::Relaxed) {
            let t = translate("k", "?");
            assert!(t == "A" || t == "B" || t == "?", "{}", t);
        }
    });
    for i in 0..2000 {
        use_set(if i % 2 == 0 { a.clone() } else { b.clone() });
    }
    stop.store(true, std::sync::atomic::Ordering::Relaxed);
    reader.join().unwrap();
}
