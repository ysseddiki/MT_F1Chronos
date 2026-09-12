// Versus — comparaison tête-à-tête de deux pilotes.

import { h, clear, fmtLap } from '../dom.js';
import { get } from '../api.js';
import { visibilityBadge, toast } from '../components.js';
import { replace, setQuery } from '../router.js';
import { tenantPath, versusPath, pilotPath, isAllTenant } from '../paths.js';

export async function versusView(container, [tenantKey], query) {
    clear(container);
    container.append(h('p', { class: 'loading' }, 'Chargement…'));

    let tenantData, namesData;
    try {
        [tenantData, namesData] = await Promise.all([
            get(`/api/v1/tenants/${tenantKey}`),
            get(`/api/v1/tenants/${tenantKey}/pilot-names`),
        ]);
    } catch (err) {
        clear(container);
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }

    const { tenant } = tenantData;
    const canonical = tenant.slug || tenant.id;
    if (canonical !== tenantKey) {
        const qs = location.search || '';
        replace(`/t/${canonical}/versus${qs}`);
        return;
    }

    const names = namesData.names || [];
    const a0 = (query.get('a') || '').trim();
    const b0 = (query.get('b') || '').trim();
    const aggregate = isAllTenant(tenant);

    document.title = `Versus — ${tenant.label} — F1 Chronos`;

    const selectA = pilotSelect(names, a0, 'Pilote A');
    const selectB = pilotSelect(names, b0, 'Pilote B');
    const resultSlot = h('div', { class: 'versus-results' });

    const form = h('form', {
        class: 'panel versus-picker',
        onsubmit: (e) => {
            e.preventDefault();
            const a = selectA.value.trim();
            const b = selectB.value.trim();
            if (!a || !b) {
                toast('Choisissez deux pilotes.', 'error');
                return;
            }
            if (a.toLowerCase() === b.toLowerCase()) {
                toast('Choisissez deux pilotes différents.', 'error');
                return;
            }
            setQuery({ a, b });
        },
    },
        h('h2', {}, 'Choisir les adversaires'),
        h('div', { class: 'versus-picker-row' },
            h('div', { class: 'field' }, h('label', {}, 'Pilote A'), selectA),
            h('div', { class: 'versus-vs', 'aria-hidden': 'true' }, 'VS'),
            h('div', { class: 'field' }, h('label', {}, 'Pilote B'), selectB),
        ),
        h('button', { type: 'submit', class: 'btn-primary' }, 'Comparer'),
    );

    clear(container);
    container.append(
        h('div', { class: 'page-head' },
            h('div', { class: 'titles' },
                h('a', { class: 'back-link', href: tenantPath(tenant), 'data-link': true }, `← ${tenant.label}`),
                h('p', { class: 'kicker' }, 'Duel'),
                h('h1', {}, 'Versus', ' ', visibilityBadge(tenant.visibility, { aggregate })),
                h('p', { class: 'lede' },
                    aggregate
                        ? 'Comparez deux pilotes sur tous les circuits, toutes organisations confondues.'
                        : 'Comparez deux pilotes circuit par circuit : écarts, % relatif et niveau.'),
            ),
        ),
        form,
        resultSlot,
    );

    if (a0 && b0 && a0.toLowerCase() !== b0.toLowerCase()) {
        loadVersus(resultSlot, tenant, a0, b0);
    } else if (!names.length) {
        resultSlot.append(h('p', { class: 'lede' }, 'Aucun chrono enregistré pour lancer un versus.'));
    }
}

function pilotSelect(names, current, label) {
    const opts = [
        h('option', { value: '', selected: !current }, `— ${label} —`),
        ...names.map((n) => h('option', {
            value: n,
            selected: current.toLowerCase() === n.toLowerCase(),
        }, n)),
    ];
    // Si le pseudo n’est plus dans la liste mais demandé en query, l’ajouter
    if (current && !names.some((n) => n.toLowerCase() === current.toLowerCase())) {
        opts.splice(1, 0, h('option', { value: current, selected: true }, current));
    }
    return h('select', { class: 'versus-pilot-select', 'aria-label': label }, opts);
}

async function loadVersus(slot, tenant, a, b) {
    clear(slot);
    slot.append(h('p', { class: 'loading' }, 'Calcul du duel…'));
    try {
        const qs = new URLSearchParams({ a, b });
        const data = await get(`/api/v1/tenants/${tenant.id}/versus?${qs}`);
        clear(slot);
        slot.append(renderVersus(tenant, data));
    } catch (err) {
        clear(slot);
        slot.append(h('p', { class: 'banner error' }, err.message || 'Erreur.'));
    }
}

function renderVersus(tenant, data) {
    const { a, b, summary, tracks } = data;
    const wrap = h('div', { class: 'versus-body' });

    wrap.append(h('div', { class: 'versus-heroes' },
        pilotCard(tenant, a, 'A', summary.faster === 'a'),
        h('div', { class: 'versus-center' },
            summaryBlock(summary, a.name, b.name),
        ),
        pilotCard(tenant, b, 'B', summary.faster === 'b'),
    ));

    if (!summary.commonTracks) {
        wrap.append(h('p', { class: 'lede' },
            'Aucun circuit en commun pour l’instant — chaque pilote a roulé sur des pistes différentes.'));
    }

    wrap.append(
        h('section', { class: 'versus-section' },
            h('h2', {}, 'Par circuit'),
            tracksTable(tenant, a.name, b.name, tracks),
        ),
    );

    return wrap;
}

function pilotCard(tenant, pilot, side, isFaster) {
    const xp = pilot.experience;
    return h('div', { class: `versus-card${isFaster ? ' faster' : ''}` },
        h('span', { class: 'versus-side' }, `Pilote ${side}`),
        h('a', {
            class: 'versus-name pilot-link',
            href: pilotPath(tenant, pilot.name),
            'data-link': true,
        }, pilot.name),
        h('div', { class: 'versus-card-stats' },
            h('div', {}, h('span', { class: 'muted' }, 'Tours'), h('strong', {}, String(pilot.totalLaps))),
            h('div', {}, h('span', { class: 'muted' }, 'Circuits'), h('strong', {}, String(pilot.tracksDriven))),
            h('div', {}, h('span', { class: 'muted' }, 'XP'), h('strong', {}, xp ? String(xp.points) : '—')),
            h('div', {}, h('span', { class: 'muted' }, 'Rang XP'), h('strong', {}, xp ? `P${xp.rank}` : '—')),
        ),
    );
}

function summaryBlock(summary, nameA, nameB) {
    const bits = [];
    bits.push(h('div', { class: 'versus-score' },
        h('span', { class: summary.faster === 'a' ? 'win' : '' }, String(summary.winsA)),
        h('span', { class: 'versus-score-sep' }, '—'),
        h('span', { class: summary.faster === 'b' ? 'win' : '' }, String(summary.winsB)),
    ));
    bits.push(h('p', { class: 'versus-score-label' },
        `Victoires circuits${summary.ties ? ` · ${summary.ties} égalité${summary.ties > 1 ? 's' : ''}` : ''}`));

    if (summary.avgRelativePct != null) {
        const pct = summary.avgRelativePct;
        const abs = Math.abs(pct).toFixed(2);
        let verdict;
        if (pct < -0.05) verdict = `${nameA} est en moyenne ${abs} % plus rapide`;
        else if (pct > 0.05) verdict = `${nameB} est en moyenne ${abs} % plus rapide`;
        else verdict = 'Niveau quasi égal sur les circuits communs';
        bits.push(h('p', { class: 'versus-relative' }, verdict));
        if (summary.avgGapMs != null) {
            bits.push(h('p', { class: 'versus-gap' },
                `Écart moyen : ${fmtSignedGap(summary.avgGapMs)} s`));
        }
        if (summary.levelIndex != null) {
            bits.push(h('p', { class: 'versus-level hint' },
                `Indice de niveau : ${summary.levelIndex}`));
        }
    } else {
        bits.push(h('p', { class: 'versus-relative muted' }, 'Pas assez de circuits communs pour un écart relatif.'));
    }

    bits.push(h('p', { class: 'hint' }, `${summary.commonTracks} circuit${summary.commonTracks > 1 ? 's' : ''} en commun`));
    return h('div', { class: 'versus-summary' }, ...bits);
}

function tracksTable(tenant, nameA, nameB, tracks) {
    if (!tracks?.length) {
        return h('p', { class: 'lede' }, 'Aucun circuit à afficher.');
    }

    const thead = h('thead', {}, h('tr', {},
        h('th', {}, 'Circuit'),
        h('th', { class: 'time' }, nameA),
        h('th', { class: 'time' }, nameB),
        h('th', {}, 'Écart'),
        h('th', {}, 'Relatif'),
        h('th', {}, 'Avantage'),
    ));

    const body = h('tbody', {},
        tracks.map((t) => {
            const gapLabel = t.gapMs == null ? '—' : fmtSignedGap(t.gapMs);
            const relLabel = t.relativePct == null
                ? '—'
                : `${t.relativePct > 0 ? '+' : ''}${t.relativePct.toFixed(2)} %`;
            let winnerLabel = '—';
            let winnerClass = '';
            if (t.winner === 'a') { winnerLabel = nameA; winnerClass = 'winner-a'; }
            else if (t.winner === 'b') { winnerLabel = nameB; winnerClass = 'winner-b'; }
            else if (t.winner === 'tie') { winnerLabel = 'Égalité'; winnerClass = 'winner-tie'; }
            else if (t.aMs != null && t.bMs == null) { winnerLabel = `${nameA} seul`; }
            else if (t.bMs != null && t.aMs == null) { winnerLabel = `${nameB} seul`; }

            return h('tr', { class: winnerClass },
                h('td', {}, t.trackName),
                h('td', { class: `time${t.winner === 'a' ? ' best' : ''}` },
                    t.aFormatted || (t.aMs != null ? fmtLap(t.aMs) : '—')),
                h('td', { class: `time${t.winner === 'b' ? ' best' : ''}` },
                    t.bFormatted || (t.bMs != null ? fmtLap(t.bMs) : '—')),
                h('td', { class: 'gap' }, gapLabel),
                h('td', { class: 'versus-rel' }, relLabel),
                h('td', {}, winnerLabel),
            );
        }),
    );

    return h('div', { class: 'board-wrap' },
        h('table', { class: 'board versus-table' }, thead, body),
    );
}

function fmtSignedGap(ms) {
    if (ms === 0) return '0.000';
    const sign = ms > 0 ? '+' : '−';
    const abs = Math.abs(ms);
    const seconds = Math.floor(abs / 1000);
    const millis = abs % 1000;
    return `${sign}${seconds}.${String(millis).padStart(3, '0')}`;
}

/** Redirection /versus → org courante (all si multi). */
export async function versusIndexView(container) {
    clear(container);
    const { loadTenants } = await import('../state.js');
    let tenants;
    try {
        tenants = await loadTenants(true);
    } catch (err) {
        container.append(h('p', { class: 'lede' }, err.message));
        return;
    }
    const all = tenants.find((t) => isAllTenant(t));
    if (all) {
        replace(versusPath(all));
        return;
    }
    if (tenants.length === 1) {
        replace(versusPath(tenants[0]));
        return;
    }
    document.title = 'Versus — F1 Chronos';
    container.append(
        h('p', { class: 'kicker' }, 'Duel'),
        h('h1', {}, 'Versus'),
        h('p', { class: 'lede' }, 'Choisissez une organisation.'),
        !tenants.length
            ? h('p', { class: 'lede' }, 'Aucune organisation visible.')
            : h('div', { class: 'grid' },
                tenants.map((t) => h('a', { class: 'card', href: versusPath(t), 'data-link': true },
                    h('h2', {}, t.label),
                )),
            ),
    );
}
