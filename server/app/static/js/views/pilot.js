// Profil public d’un pilote (compte lié via sim_pseudo).

import { h, clear, fmtLap, fmtDateTime } from '../dom.js';
import { get } from '../api.js';
import { visibilityBadge } from '../components.js';
import { replace } from '../router.js';
import { mySimulatorPseudo } from '../state.js';
import { tenantPath, pilotPath, isAllTenant, versusPath } from '../paths.js';

const ROLE_LABEL = {
    admin: 'Administrateur',
    simracer: 'SimRacer',
    visitor: 'Visiteur',
};

export async function pilotView(container, [tenantKey, rawPseudo]) {
    clear(container);
    container.append(h('p', { class: 'loading' }, 'Chargement du profil…'));

    let pseudo;
    try {
        pseudo = decodeURIComponent(rawPseudo || '').trim();
    } catch {
        pseudo = (rawPseudo || '').trim();
    }
    if (!pseudo) {
        clear(container);
        container.append(h('p', { class: 'lede' }, 'Pseudo manquant.'));
        return;
    }

    let data;
    try {
        data = await get(`/api/v1/tenants/${tenantKey}/pilots/${encodeURIComponent(pseudo)}`);
    } catch (err) {
        clear(container);
        container.append(
            h('p', { class: 'kicker' }, 'Profil'),
            h('h1', {}, pseudo),
            h('p', { class: 'lede' }, err.message || 'Profil introuvable.'),
            h('a', { class: 'back-link', href: `/t/${tenantKey}`, 'data-link': true }, '← Retour au classement'),
        );
        return;
    }

    const { tenant, pilot, profile } = data;
    const canonical = tenant.slug || tenant.id;
    if (canonical !== tenantKey) {
        replace(pilotPath(tenant, pilot.simPseudo));
        return;
    }
    if ((pilot.simPseudo || '').toLowerCase() !== pseudo.toLowerCase()) {
        replace(pilotPath(tenant, pilot.simPseudo));
        return;
    }

    const me = (mySimulatorPseudo() || '').trim().toLowerCase() === pilot.simPseudo.toLowerCase();
    const xp = profile.experience;
    const linked = !!pilot.linked;
    document.title = `${pilot.simPseudo} — Profil — F1 Chronos`;

    const metaBits = [];
    if (linked) {
        metaBits.push(h('span', { class: `role-pill ${pilot.role || 'visitor'}` }, ROLE_LABEL[pilot.role] || pilot.role || 'Compte'));
        metaBits.push(h('span', { class: 'pilot-meta-sep' }, '·'));
        metaBits.push(h('span', {}, 'Compte lié'));
        if (pilot.memberSince) {
            metaBits.push(h('span', { class: 'pilot-meta-sep' }, '·'));
            metaBits.push(h('span', {}, `membre depuis ${fmtDateTime(pilot.memberSince)}`));
        }
    } else {
        metaBits.push(h('span', { class: 'role-pill guest' }, 'Sans compte'));
        metaBits.push(h('span', { class: 'pilot-meta-sep' }, '·'));
        metaBits.push(h('span', {}, 'Profil basé sur les chronos enregistrés'));
    }

    clear(container);
    container.append(
        h('div', { class: 'page-head pilot-profile-head' },
            h('div', { class: 'titles' },
                h('a', { class: 'back-link', href: tenantPath(tenant), 'data-link': true }, `← ${tenant.label}`),
                h('p', { class: 'kicker' }, 'Profil pilote'),
                h('h1', { class: me ? 'pilot-me' : '' },
                    pilot.simPseudo,
                    ' ',
                    visibilityBadge(tenant.visibility, { aggregate: isAllTenant(tenant) }),
                ),
                h('p', { class: 'lede pilot-meta' }, ...metaBits),
                h('p', { class: 'pilot-actions' },
                    h('a', {
                        class: 'btn btn-sm',
                        href: versusPath(tenant, pilot.simPseudo),
                        'data-link': true,
                    }, 'Comparer en Versus'),
                ),
            ),
        ),
        h('div', { class: 'pilot-stat-strip' },
            statTile('Rang XP', xp ? `P${xp.rank}` : '—'),
            statTile('Points', xp ? String(xp.points) : '0'),
            statTile('Tours', String(profile.totalLaps ?? 0)),
            statTile('Circuits', String(profile.tracksDriven ?? 0)),
            statTile('Victoires', xp ? String(xp.wins) : '0'),
            statTile('Podiums', xp ? String(xp.podiums) : '0'),
        ),
        h('section', { class: 'pilot-section' },
            h('h2', {}, 'Meilleurs chronos'),
            profile.bests?.length
                ? bestsTable(profile.bests)
                : h('p', { class: 'lede' }, 'Aucun chrono enregistré dans cette organisation.'),
        ),
        h('section', { class: 'pilot-section' },
            h('h2', {}, 'Derniers chronos'),
            profile.recent?.length
                ? recentTable(profile.recent)
                : h('p', { class: 'lede' }, 'Pas encore d’historique.'),
        ),
    );
}

function statTile(label, value) {
    return h('div', { class: 'pilot-stat' },
        h('span', { class: 'pilot-stat-label' }, label),
        h('span', { class: 'pilot-stat-value' }, value),
    );
}

function bestsTable(rows) {
    return h('div', { class: 'board-wrap' },
        h('table', { class: 'board' },
            h('thead', {}, h('tr', {},
                h('th', {}, 'Circuit'),
                h('th', { class: 'time' }, 'Meilleur'),
                h('th', {}, 'Enregistré'),
                h('th', {}, 'Simu'),
            )),
            h('tbody', {},
                rows.map((row) => h('tr', {},
                    h('td', {}, (row.trackName || '').trim() || '—'),
                    h('td', { class: 'time' }, row.formatted || fmtLap(row.bestLapMs)),
                    h('td', { class: 'muted' }, fmtDateTime(row.startedAt)),
                    h('td', { class: 'sim-tag' }, row.simLabel || '—'),
                )),
            ),
        ),
    );
}

function recentTable(rows) {
    return h('div', { class: 'board-wrap' },
        h('table', { class: 'board recent-laps-table' },
            h('thead', {}, h('tr', {},
                h('th', { class: 'recent-when' }, 'Enregistré'),
                h('th', {}, 'Circuit'),
                h('th', { class: 'time' }, 'Temps'),
                h('th', {}, 'Simu'),
            )),
            h('tbody', {},
                rows.map((row) => h('tr', {},
                    h('td', { class: 'recent-when muted' }, fmtDateTime(row.startedAt)),
                    h('td', {}, (row.trackName || '').trim() || '—'),
                    h('td', { class: 'time' }, row.formatted || fmtLap(row.bestLapMs)),
                    h('td', { class: 'sim-tag' }, row.simLabel || '—'),
                )),
            ),
        ),
    );
}
