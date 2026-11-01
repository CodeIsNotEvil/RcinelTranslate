// Reads a translation file: one JSON object whose values are all strings (docs/format.md).
// Not JSON.parse: it takes the last of two equal keys and accepts numbers or nested objects, while
// the other clients reject those files. All of them must show the same text, so this is a twin of
// FlatJsonReader (C#, Java) and reader.rs, and spec/fixtures checks that they agree.

/** Thrown when a translation file is not a flat JSON object of strings. The message says where. */
export class TranslationFormatError extends Error {
  constructor(message) {
    super(message);
    this.name = 'TranslationFormatError';
  }
}

/**
 * @param {string} text
 * @returns {Map<string, string>} the entries in file order
 */
export function readFlatJson(text) {
  if (typeof text !== 'string') {
    throw new TypeError('readFlatJson needs the file text as a string');
  }
  let position = 0;
  const result = new Map();

  const error = (message, at) => {
    let line = 1;
    let column = 1;
    for (let i = 0; i < at && i < text.length; i++) {
      if (text[i] === '\n') {
        line++;
        column = 1;
      } else {
        column++;
      }
    }
    return new TranslationFormatError(`Line ${line}, column ${column}: ${message}.`);
  };
  const peek = () => (position < text.length ? text[position] : '');
  const skipWhitespace = () => {
    while (position < text.length && ' \t\n\r'.includes(text[position])) {
      position++;
    }
  };
  const expect = (char) => {
    if (peek() !== char) {
      throw error(`expected '${char}'`, position);
    }
    position++;
  };
  const readString = (what) => {
    if (peek() !== '"') {
      throw error(`expected ${what}`, position);
    }
    position++;
    let out = '';
    for (;;) {
      if (position >= text.length) {
        throw error('a string is not closed', position);
      }
      const c = text[position++];
      if (c === '"') {
        return out;
      }
      if (c < ' ') {
        throw error('control characters (such as line breaks) must be escaped, for example as \\n', position - 1);
      }
      if (c !== '\\') {
        out += c;
        continue;
      }
      if (position >= text.length) {
        throw error('a string is not closed', position);
      }
      const escape = text[position++];
      switch (escape) {
        case '"': out += '"'; break;
        case '\\': out += '\\'; break;
        case '/': out += '/'; break;
        case 'b': out += '\b'; break;
        case 'f': out += '\f'; break;
        case 'n': out += '\n'; break;
        case 'r': out += '\r'; break;
        case 't': out += '\t'; break;
        case 'u': {
          const hex = text.slice(position, position + 4);
          if (!/^[0-9a-fA-F]{4}$/.test(hex)) {
            throw error('\\u needs four hex digits', position - 2);
          }
          // JavaScript strings are UTF-16 as well, so surrogate pairs join up by themselves.
          out += String.fromCharCode(parseInt(hex, 16));
          position += 4;
          break;
        }
        default:
          throw error(`unknown escape \\${escape}`, position - 2);
      }
    }
  };

  // A BOM is not JSON, but Windows editors write one.
  if (text.charCodeAt(0) === 0xfeff) {
    position = 1;
  }
  skipWhitespace();
  expect('{');
  skipWhitespace();
  if (peek() === '}') {
    position++;
  } else {
    for (;;) {
      skipWhitespace();
      const keyStart = position;
      const key = readString('a key in quotes');
      skipWhitespace();
      expect(':');
      skipWhitespace();
      const value = readString('a string value (translation files hold only strings)');
      if (result.has(key)) {
        throw error(`the key "${key}" appears twice`, keyStart);
      }
      result.set(key, value);
      skipWhitespace();
      const next = peek();
      if (next === ',') {
        position++;
        continue;
      }
      if (next === '}') {
        position++;
        break;
      }
      throw error("expected ',' or '}'", position);
    }
  }
  skipWhitespace();
  if (position < text.length) {
    throw error("unexpected text after the closing '}'", position);
  }
  return result;
}
