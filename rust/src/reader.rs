//! Reads a translation file: one JSON object whose values are all strings (docs/format.md).
//!
//! Hand-written, because the client must not pull a JSON crate into the apps that use it. It is a
//! twin of the C# and Java FlatJsonReader and of reader.js; spec/fixtures checks that all of them
//! accept and reject the same files, so keep them in step.

use std::fmt;

/// A translation file is not a flat JSON object of strings. The message says where.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TranslationFormatError {
    message: String,
}

impl TranslationFormatError {
    pub(crate) fn new(message: impl Into<String>) -> Self {
        Self { message: message.into() }
    }

    /// The same error with `prefix` (usually the file path) in front of the message.
    pub(crate) fn within(self, prefix: impl fmt::Display) -> Self {
        Self::new(format!("{}: {}", prefix, self.message))
    }

    /// What is wrong, with line and column.
    pub fn message(&self) -> &str {
        &self.message
    }
}

impl fmt::Display for TranslationFormatError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.message)
    }
}

impl std::error::Error for TranslationFormatError {}

/// The entries of a translation file, in file order.
pub(crate) fn read_flat_json(text: &str) -> Result<Vec<(String, String)>, TranslationFormatError> {
    Reader { chars: text.chars().collect(), position: 0 }.read_object()
}

struct Reader {
    // Characters, not bytes: positions in error messages count what an editor shows.
    chars: Vec<char>,
    position: usize,
}

impl Reader {
    fn read_object(mut self) -> Result<Vec<(String, String)>, TranslationFormatError> {
        let mut result: Vec<(String, String)> = Vec::new();
        let mut seen = std::collections::HashSet::new();
        // A BOM is not JSON, but Windows editors write one.
        if self.chars.first() == Some(&'\u{FEFF}') {
            self.position = 1;
        }
        self.skip_whitespace();
        self.expect('{')?;
        self.skip_whitespace();
        if self.peek() == Some('}') {
            self.position += 1;
        } else {
            loop {
                self.skip_whitespace();
                let key_start = self.position;
                let key = self.read_string("a key in quotes")?;
                self.skip_whitespace();
                self.expect(':')?;
                self.skip_whitespace();
                let value = self.read_string("a string value (translation files hold only strings)")?;
                // Legal JSON, but parsers disagree on which value wins, so every client rejects it.
                if !seen.insert(key.clone()) {
                    return Err(self.error(&format!("the key \"{}\" appears twice", key), key_start));
                }
                result.push((key, value));
                self.skip_whitespace();
                match self.peek() {
                    Some(',') => {
                        self.position += 1;
                    }
                    Some('}') => {
                        self.position += 1;
                        break;
                    }
                    _ => return Err(self.error("expected ',' or '}'", self.position)),
                }
            }
        }
        self.skip_whitespace();
        if self.position < self.chars.len() {
            return Err(self.error("unexpected text after the closing '}'", self.position));
        }
        Ok(result)
    }

    fn read_string(&mut self, what: &str) -> Result<String, TranslationFormatError> {
        if self.peek() != Some('"') {
            return Err(self.error(&format!("expected {}", what), self.position));
        }
        self.position += 1;
        let mut out = String::new();
        loop {
            let c = match self.next() {
                Some(c) => c,
                None => return Err(self.error("a string is not closed", self.position)),
            };
            match c {
                '"' => return Ok(out),
                c if (c as u32) < 0x20 => {
                    return Err(self.error(
                        "control characters (such as line breaks) must be escaped, for example as \\n",
                        self.position - 1,
                    ))
                }
                '\\' => {
                    let escape = match self.next() {
                        Some(e) => e,
                        None => return Err(self.error("a string is not closed", self.position)),
                    };
                    match escape {
                        '"' => out.push('"'),
                        '\\' => out.push('\\'),
                        '/' => out.push('/'),
                        'b' => out.push('\u{8}'),
                        'f' => out.push('\u{c}'),
                        'n' => out.push('\n'),
                        'r' => out.push('\r'),
                        't' => out.push('\t'),
                        'u' => {
                            let code = self.read_hex4()?;
                            out.push(self.decode_unicode(code));
                        }
                        other => return Err(self.error(&format!("unknown escape \\{}", other), self.position - 2)),
                    }
                }
                c => out.push(c),
            }
        }
    }

    // JSON writes characters outside the Basic Multilingual Plane (emoji) as two \u escapes, a
    // surrogate pair. Rust strings can't hold half a pair, so a lone surrogate becomes U+FFFD; the
    // UTF-16 clients keep it, but no real translation contains one.
    fn decode_unicode(&mut self, code: u32) -> char {
        if (0xD800..0xDC00).contains(&code)
            && self.chars.get(self.position) == Some(&'\\')
            && self.chars.get(self.position + 1) == Some(&'u')
        {
            let saved = self.position;
            self.position += 2;
            if let Ok(low) = self.read_hex4() {
                if (0xDC00..0xE000).contains(&low) {
                    let combined = 0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00);
                    return char::from_u32(combined).unwrap_or('\u{FFFD}');
                }
            }
            self.position = saved;
        }
        char::from_u32(code).unwrap_or('\u{FFFD}')
    }

    fn read_hex4(&mut self) -> Result<u32, TranslationFormatError> {
        let start = self.position;
        if start + 4 > self.chars.len() {
            return Err(self.error("\\u needs four hex digits", start.saturating_sub(2)));
        }
        let mut code = 0u32;
        for i in 0..4 {
            match self.chars[start + i].to_digit(16) {
                Some(d) => code = code * 16 + d,
                None => return Err(self.error("\\u needs four hex digits", start.saturating_sub(2))),
            }
        }
        self.position += 4;
        Ok(code)
    }

    fn skip_whitespace(&mut self) {
        while matches!(self.peek(), Some(' ' | '\t' | '\n' | '\r')) {
            self.position += 1;
        }
    }

    fn peek(&self) -> Option<char> {
        self.chars.get(self.position).copied()
    }

    fn next(&mut self) -> Option<char> {
        let c = self.peek();
        if c.is_some() {
            self.position += 1;
        }
        c
    }

    fn expect(&mut self, expected: char) -> Result<(), TranslationFormatError> {
        if self.peek() != Some(expected) {
            return Err(self.error(&format!("expected '{}'", expected), self.position));
        }
        self.position += 1;
        Ok(())
    }

    fn error(&self, message: &str, at: usize) -> TranslationFormatError {
        let mut line = 1;
        let mut column = 1;
        for c in self.chars.iter().take(at) {
            if *c == '\n' {
                line += 1;
                column = 1;
            } else {
                column += 1;
            }
        }
        TranslationFormatError::new(format!("Line {}, column {}: {}.", line, column, message))
    }
}
