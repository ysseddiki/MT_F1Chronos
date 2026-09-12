// Moteur commun des pages de classement : sélecteur circuit, switch best/all,
// tableau paginé (20/page), mises à jour live via SSE (repli 60 s).

import { h, clear } from '../dom.js';
import { segmented, trackSelect, boardTable, pagination, banner, simToolbarStrip } from '../components.js';
import { setQuery, onCleanup, replace } from '../router.js';
import { subscribeChanges, mySimulatorPseudo, isAdmin } from '../state.js';
import { boardRowManageMenu } from '../board_manage.js';

export const FALLBACK_REFRESH_MS = 60_000;
const LIVE_DEBOUNCE_MS = 1500;

export function renderBoardPage(container, query, ctx) {
    // ctx: { head, tracks, focusTrackId, showSim, sims, defaultSimId, contestId, fetchBoard }
    clear(container);

    // Compat : ancien ?view=recent → page topbar /recent
    if (query.get('view') === 'recent') {
        const m = location.pathname.match(/^\/t\/([\w-]+)/);
        replace(m ? `/t/${m[1]}/recent` : '/recent');
        return;
    }

    const best = query.get('best') !== 'false';
    const page = Math.max(1, Number(query.get('page')) || 1);
    const trackId = pickTrack(query, ctx.tracks, ctx.focusTrackId);

    container.append(ctx.head);

    if (!ctx.tracks.length) {
        container.append(h('p', { class: 'lede' }, 'Aucun chrono reçu pour l’instant.'));
        return;
    }

    const simStrip = simToolbarStrip(ctx.sims);
    if (simStrip) container.append(simStrip);

    container.append(
        trackSelect(
            ctx.tracks,
            trackId,
            (id) => setQuery({ track: id, page: null }),
            { liveTracks: collectLiveTracks(ctx.sims, ctx.tracks) },
        ),
        h('div', { class: 'toolbar' },
            segmented(
                [
                    { value: 'best', label: 'Meilleur / joueur' },
                    { value: 'all', label: 'Tous les tours' },
                ],
                best ? 'best' : 'all',
                (value) => setQuery({ best: value === 'all' ? 'false' : null, page: null }),
            ),
        ),
    );

    const slot = h('div', {}, h('p', { class: 'loading' }, 'Chargement du classement…'));
    container.append(slot);

    const trackName = ctx.tracks.find((t) => t.trackId === trackId)?.trackName || '';
    let loadGen = 0;

    function buildManage(onDone) {
        return isAdmin()
            ? (row) => boardRowManageMenu(row, {
                simId: row.simId || ctx.defaultSimId,
                contestId: ctx.contestId ?? null,
                onDone,
            })
            : null;
    }

    async function loadBoard() {
        const gen = ++loadGen;
        try {
            const board = await ctx.fetchBoard(trackId, best, page);
            if (gen !== loadGen) return;
            clear(slot);
            slot.append(
                boardTable(board.rows, {
                    showSim: ctx.showSim,
                    highlightName: mySimulatorPseudo() || null,
                    manage: buildManage(() => loadBoard()),
                }),
                pagination(board, (p) => setQuery({ page: p > 1 ? p : null })),
            );
        } catch (err) {
            if (gen !== loadGen) return;
            clear(slot);
            slot.append(banner(err.message || 'Erreur de chargement.', 'error'));
        }
    }

    loadBoard();

    const reload = () => { if (!document.hidden) loadBoard(); };
    let lastLiveEvent = 0;
    const unsubscribe = subscribeChanges(() => {
        const now = Date.now();
        if (now - lastLiveEvent < LIVE_DEBOUNCE_MS) return;
        lastLiveEvent = now;
        reload();
    });
    const fallback = setInterval(reload, FALLBACK_REFRESH_MS);
    const onVisible = () => { if (!document.hidden) reload(); };
    document.addEventListener('visibilitychange', onVisible);
    onCleanup(() => {
        unsubscribe();
        clearInterval(fallback);
        document.removeEventListener('visibilitychange', onVisible);
    });

    document.title = `${trackName ? `${trackName} — ` : ''}Classement — F1 Chronos`;
}

function pickTrack(query, tracks, focusTrackId) {
    const fromQuery = Number(query.get('track'));
    if (tracks.some((t) => t.trackId === fromQuery)) return fromQuery;
    if (focusTrackId != null && tracks.some((t) => t.trackId === focusTrackId)) return focusTrackId;
    return tracks[0].trackId;
}

function collectLiveTracks(sims, tracks) {
    const known = new Set((tracks || []).map((t) => t.trackId));
    const seen = new Set();
    const out = [];
    for (const sim of sims || []) {
        if (sim.currentTrackId == null || sim.currentTrackId < 0) continue;
        if (!known.has(sim.currentTrackId)) continue;
        if (seen.has(sim.currentTrackId)) continue;
        seen.add(sim.currentTrackId);
        out.push({
            trackId: sim.currentTrackId,
            trackName: (sim.currentTrackName || '').trim() || `Circuit ${sim.currentTrackId}`,
            simLabel: sims.length > 1 ? sim.label : null,
        });
    }
    return out;
}
