// Moteur commun des pages de classement : sélecteur circuit, switch best/all,
// tableau paginé (20/page), mises à jour live via SSE (repli 60 s).

import { h, clear } from '../dom.js';
import { segmented, trackSelect, boardTable, recentLapsPanel, pagination, banner, simToolbarStrip } from '../components.js';
import { setQuery, onCleanup } from '../router.js';
import { subscribeChanges, mySimulatorPseudo, isAdmin } from '../state.js';
import { boardRowManageMenu } from '../board_manage.js';

export const FALLBACK_REFRESH_MS = 60_000;
const LIVE_DEBOUNCE_MS = 1500;
const RECENT_LAPS_LIMIT = 15;

export function renderBoardPage(container, query, ctx) {
    // ctx: { head, tracks, focusTrackId, liveTrackId, showSim, sims, defaultSimId, contestId, fetchBoard }
    clear(container);

    // Meilleur par pilote par défaut ; ?best=false pour tous les tours
    const best = query.get('best') !== 'false';
    const page = Math.max(1, Number(query.get('page')) || 1);
    const trackId = pickTrack(query, ctx.tracks, ctx.focusTrackId);
    const liveTrackId = ctx.liveTrackId ?? ctx.focusTrackId ?? null;

    container.append(ctx.head);

    if (!ctx.tracks.length) {
        container.append(h('p', { class: 'lede' }, 'Aucun chrono reçu pour l’instant.'));
        return;
    }

    container.append(
        trackSelect(ctx.tracks, trackId, (id) => setQuery({ track: id, page: null }), { liveTrackId }),
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

    const simStrip = simToolbarStrip(ctx.sims, { liveTrackId: trackId });
    if (simStrip) container.append(simStrip);

    const recentSlot = h('div', { class: 'recent-laps-slot' });
    container.append(recentSlot);

    const slot = h('div', {}, h('p', { class: 'loading' }, 'Chargement du classement…'));
    container.append(slot);

    const trackName = ctx.tracks.find((t) => t.trackId === trackId)?.trackName || '';

    let loadGen = 0;
    let recentGen = 0;

    function buildManage(onDone) {
        return isAdmin()
            ? (row) => boardRowManageMenu(row, {
                simId: row.simId || ctx.defaultSimId,
                contestId: ctx.contestId ?? null,
                onDone,
            })
            : null;
    }

    async function loadRecent() {
        if (!ctx.fetchRecent) {
            clear(recentSlot);
            return;
        }
        const gen = ++recentGen;
        try {
            const data = await ctx.fetchRecent(RECENT_LAPS_LIMIT);
            if (gen !== recentGen) return;
            clear(recentSlot);
            const refreshAll = () => { loadBoard(); loadRecent(); };
            recentSlot.append(recentLapsPanel(data.rows || [], {
                showSim: ctx.showSim,
                highlightName: mySimulatorPseudo() || null,
                manage: buildManage(refreshAll),
            }));
        } catch {
            if (gen !== recentGen) return;
            clear(recentSlot);
        }
    }

    async function loadBoard() {
        const gen = ++loadGen;
        try {
            const board = await ctx.fetchBoard(trackId, best, page);
            if (gen !== loadGen) return;
            clear(slot);
            const refreshBoard = () => loadBoard();
            slot.append(
                boardTable(board.rows, {
                    showSim: ctx.showSim,
                    highlightName: mySimulatorPseudo() || null,
                    manage: buildManage(refreshBoard),
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
    loadRecent();

    const reload = () => {
        if (!document.hidden) {
            loadBoard();
            loadRecent();
        }
    };
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

    document.title = `${trackName ? `${trackName} — ` : ''}F1 Chronos — Résultats`;
}

function pickTrack(query, tracks, focusTrackId) {
    const fromQuery = Number(query.get('track'));
    if (tracks.some((t) => t.trackId === fromQuery)) return fromQuery;
    if (focusTrackId != null && tracks.some((t) => t.trackId === focusTrackId)) return focusTrackId;
    return tracks[0].trackId;
}
