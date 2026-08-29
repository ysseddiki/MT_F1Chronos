// Bootstrap SPA : charge la session, monte la topbar, démarre le routeur.

import { route, setTopbarRenderer, startRouter, replace } from './router.js';
import { renderTopbar } from './components.js';
import { loadMe, state } from './state.js';
import { homeView } from './views/home.js';
import { tenantView } from './views/tenant.js';
import { simView } from './views/sim.js';
import { loginView } from './views/login.js';
import { profileView } from './views/profile.js';
import { adminView } from './views/admin.js';

// Compatibilité anciennes URLs (signets)
route(/^\/admin\/login$/, () => replace('/login'));
route(/^\/t\/([\w-]+)\/tracks\/(\d+)$/, (c, [tid, track]) => replace(`/t/${tid}?track=${track}`));
route(/^\/sim\/([\w-]+)\/tracks\/(\d+)$/, (c, [sid, track]) => replace(`/sim/${sid}?track=${track}`));
route(/^\/contests$/, (_c, _p, q) => {
    const sim = q.get('sim');
    replace(sim ? `/sim/${sim}` : '/');
});
route(/^\/sim\/([\w-]+)\/contests\/([\w-]+)$/, (_c, [sid, cid]) => replace(`/sim/${sid}?contest=${cid}`));

route(/^\/$/, (c) => homeView(c));
route(/^\/t\/([\w-]+)$/, (c, p, q) => tenantView(c, p, q));
route(/^\/sim\/([\w-]+)$/, (c, p, q) => simView(c, p, q));
route(/^\/login$/, (c) => loginView(c));
route(/^\/profile$/, (c) => profileView(c));
route(/^\/admin$/, (c, p, q) => adminView(c, p, q));

setTopbarRenderer(renderTopbar);

(async () => {
    try {
        await loadMe();
        if (state.me?.profileRequired
            && location.pathname !== '/profile'
            && location.pathname !== '/login') {
            replace('/profile');
        }
    } catch {
        // serveur momentanément injoignable : les vues afficheront l'erreur
    }
    startRouter();
})();
