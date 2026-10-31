// The web version of the demo app's note list.
import { translate, tryLoad } from 'rcinel-translate';

await tryLoad(navigator.language, { baseUrl: '/translations/' });

document.querySelector('#search').placeholder = translate('01a192e3-5bd9-7000-bfa7-a2e174cf19d8', 'Search notes');
document.querySelector('#new').textContent = translate('01a192e3-5bd9-7001-b370-5f9abad293a1', 'New note');
document.querySelector('#empty').textContent = translate('01a192e3-5bd9-7002-9ac8-289f9fa56e4a', 'No notes yet. Write your first one!');
