# rcinel-translate

The JavaScript client of [RcinelTranslate](https://github.com/CodeIsNotEvil/RcinelTranslate). For
browsers, bundlers, Node, Deno and Electron. No dependencies; TypeScript types included.

```js
import { translate, tryLoad } from 'rcinel-translate';

await tryLoad(navigator.language, { baseUrl: '/translations/' });
title.textContent = translate('01a192e3-5bcc-7000-a8d2-327b1cb0f737', 'Settings');
greeting.textContent = translate('01a192e3-5bd4-7008-9a54-036fafe0c7af', 'Welcome back, {name}!', { name });
```

All functions are named exports; the default export `Rcinel` groups them. In Node, load from disk:

```js
import { useDirectory } from 'rcinel-translate/node';

await useDirectory('de', new URL('./translations', import.meta.url).pathname);
```

| API | |
|---|---|
| `translate(id, english, values?)` | The translation, or `english`. `values` fills `{0}` from an array or `{name}` from an object. |
| `load(language, { baseUrl, fetch })` | Fetches the language with its fallback chain (`de-AT` reads `de.json`, then `de-AT.json`). |
| `tryLoad(language, options)` | Like `load`, but resolves to `false` and keeps the current language when there is no file. |
| `use(set)` / `useObject(language, json)` | Switches to a set you built, for example from a bundled JSON import. |
| `onChange(listener)` | Called after every switch; returns a function that removes the listener. |
| `language()` | The loaded language, such as `"de"`; empty while the English texts show. |
| `reset()` | Back to the English texts. |
