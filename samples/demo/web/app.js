// The web version of the demo app's note list.
import Rcinel from 'rcinel-translate';

await Rcinel.tryLoad(navigator.language, { baseUrl: '/translations/' });

document.querySelector('#search').placeholder = Rcinel.translate('01a192e3-5bd9-7000-bfa7-a2e174cf19d8', 'Search notes');
document.querySelector('#new').textContent = Rcinel.translate('01a192e3-5bd9-7001-b370-5f9abad293a1', 'New note');
document.querySelector('#empty').textContent = Rcinel.translate('01a192e3-5bd9-7002-9ac8-289f9fa56e4a', 'No notes yet. Write your first one!');
