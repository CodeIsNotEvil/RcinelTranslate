//! spec/fixtures/cases.json, the cases the C#, Java, JavaScript and Python clients run too. A
//! failure here means the clients disagree about what an app shows.

use rcinel_translate::{fallback_chain, Error, TranslationSet};
use serde_json::Value;
use std::path::PathBuf;

fn fixtures() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("..").join("spec").join("fixtures")
}

fn cases() -> Value {
    serde_json::from_str(&std::fs::read_to_string(fixtures().join("cases.json")).unwrap()).unwrap()
}

fn s(v: &Value) -> &str {
    v.as_str().unwrap()
}

#[test]
fn translates_like_the_other_clients() {
    let translations = fixtures().join("translations");
    for case in cases()["translate"].as_array().unwrap() {
        let set = TranslationSet::load(s(&case["language"]), &translations).unwrap();
        assert_eq!(
            set.get_or(s(&case["id"]), s(&case["english"])),
            s(&case["expected"]),
            "{} {}",
            case["language"],
            case["id"]
        );
    }
}

#[test]
fn merged_sets_have_the_expected_size() {
    for (language, count) in cases()["counts"].as_object().unwrap() {
        let set = TranslationSet::load(language, fixtures().join("translations")).unwrap();
        assert_eq!(set.len() as u64, count.as_u64().unwrap(), "{}", language);
    }
}

#[test]
fn fallback_chains_go_from_specific_to_general() {
    for case in cases()["fallbackChains"].as_array().unwrap() {
        let expected: Vec<String> = case["chain"].as_array().unwrap().iter().map(|v| s(v).to_string()).collect();
        assert_eq!(fallback_chain(s(&case["language"])), expected);
    }
}

#[test]
fn missing_languages_fail_and_try_load_finds_nothing() {
    let translations = fixtures().join("translations");
    for language in cases()["missingLanguages"].as_array().unwrap() {
        assert!(matches!(TranslationSet::load(s(language), &translations), Err(Error::NotFound { .. })));
        assert!(TranslationSet::try_load(s(language), &translations).unwrap().is_none());
    }
}

#[test]
fn empty_languages_load_nothing_with_try_load() {
    for language in cases()["emptyLanguages"].as_array().unwrap() {
        assert!(TranslationSet::try_load(s(language), fixtures().join("translations")).unwrap().is_none());
    }
}

#[test]
fn rejects_invalid_files() {
    for name in cases()["invalid"].as_array().unwrap() {
        let path = fixtures().join("invalid").join(s(name));
        match TranslationSet::from_file("x", &path) {
            // The message must say which file, or nobody finds the broken one.
            Err(Error::Format(e)) => assert!(e.message().contains(s(name)), "{}", e),
            other => panic!("{} was accepted or failed differently: {:?}", s(name), other),
        }
    }
}
