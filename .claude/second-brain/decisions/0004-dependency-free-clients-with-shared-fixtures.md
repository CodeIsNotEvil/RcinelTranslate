---
tags: [decision, clients]
created: 2026-10-31
updated: 2026-10-31
status: active
supersedes:
---
# 0004: Dependency-free clients, one set of test cases

**Context.** The client is what every app ships. netstandard2.0 has no JSON parser, Java has none,
and JavaScript's `JSON.parse` accepts duplicate keys (last wins) and non-string values.

**Decision.** Each client has its own small reader of flat JSON objects that rejects duplicates,
non-strings and anything that isn't JSON, and accepts a BOM. `spec/fixtures/cases.json` lists the
cases (translations, fallback chains, invalid files) and all three test suites run them.

**Alternatives.** *System.Text.Json / Gson / JSON.parse*: dependencies in the C# and Java apps, and
different behaviour on duplicates between clients.

**Consequences.** A reader change is made three times plus a fixture. Core uses the C# client's reader
for translation files, so `rcinel check` sees exactly what apps see.

Related: [[0002-flat-key-value-json]]
