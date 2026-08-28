// Moteur commun des pages de classement : chips circuit en haut, switch best/all,
// tableau complet paginé (20/page), mises à jour live via SSE (repli 60 s).

import { h, clear } from '../dom.js';
import { segmented, trackChips, boardTable, pagination, banner } from '../components.js';
import { setQuery, replace, onCleanup } from '../router.js';
import { subscribeChanges } from '../state.js';

export const FALLBACK_REFRESH_MS = 60_000;
const LIVE_DEBOUNCE_MS = 1500;

export function renderBoardPage(container, query, ctx) {
    // ctx: { head: Node, tracks: [], focusTrackId: number|null, showSim: bool,
    //        fetchBoard: (trackId, best, page) => Promise<board> }
    clear(container);

    const best = query.get('best') === 'true';
    const page = Math.max(1, Number(query.get('page')) || 1);
    const trackId = pickTrack(query, ctx.tracks, ctx.focusTrackId);

    container.append(ctx.head);

    if (!ctx.tracks.length) {
        container.append(h('p', { class: 'lede' }, 'Aucun chrono reçu pour l’instant.'));
        return;
    }

    container.append(
        trackChips(ctx.tracks, trackId, (id) => setQuery({ track: id, page: null })),
        h('div', { class: 'toolbar' },
            segmented(
                [
                    { value: 'all', label: 'Tous les tours' },
                    { value: 'best', label: 'Meilleur / joueur' },
                ],
                best ? 'best' : 'all',
                (value) => setQuery({ best: value === 'best' ? 'true' : null, page: null }),
            ),
        ),
    );

    const slot = h('div', {}, h('p', { class: 'loading' }, 'Chargement du classement…'));
    container.append(slot);

    const trackName = ctx.tracks.find((t) => t.trackId === trackId)?.trackName || '';

    async function loadBoard() {
        try {
            const board = await ctx.fetchBoard(trackId, best, page);
            clear(slot);
            slot.append(
                boardTable(board.rows, { showSim: ctx.showSim }),
                pagination(board, (p) => setQuery({ page: p > 1 ? p : null })),
            );
        } catch (err) {
            clear(slot);
            slot.append(banner(err.message || 'Erreur de chargement.', 'error'));
        }
    }

    loadBoard();

    // Live : le serveur pousse un signal à chaque sync/mutation → re-render.
    // Repli : intervalle lent si EventSource est indisponible ou déconnecté.
    const reload = () => {
        if (!document.hidden) replace(location.pathname + location.search);
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
