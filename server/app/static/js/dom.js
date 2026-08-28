// Helpers DOM — les chaînes passent par createTextNode (jamais innerHTML) : pas d'injection.

export function h(tag, attrs = {}, ...children) {
    const el = document.createElement(tag);
    for (const [key, value] of Object.entries(attrs || {})) {
        if (value == null || value === false) continue;
        if (key === 'class') el.className = value;
        else if (key === 'dataset') Object.assign(el.dataset, value);
        else if (key.startsWith('on') && typeof value === 'function')
            el.addEventListener(key.slice(2).toLowerCase(), value);
        else if (key === 'value') el.value = value;
        else if (key === 'checked' || key === 'disabled' || key === 'selected') el[key] = !!value;
        else el.setAttribute(key, String(value));
    }
    append(el, children);
    return el;
}

function append(el, children) {
    for (const child of children.flat(20)) {
        if (child == null || child === false || child === '') continue;
        el.append(child.nodeType ? child : document.createTextNode(String(child)));
    }
}

export function clear(el) {
    while (el.firstChild) el.removeChild(el.firstChild);
}

export function fmtLap(ms) {
    if (!ms || ms <= 0) return '--:--.---';
    const minutes = Math.floor(ms / 60000);
    const seconds = Math.floor((ms % 60000) / 1000);
    const millis = ms % 1000;
    return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}.${String(millis).padStart(3, '0')}`;
}

export function fmtGap(ms) {
    if (ms <= 0) return '—';
    const seconds = Math.floor(ms / 1000);
    const millis = ms % 1000;
    return `+${seconds}.${String(millis).padStart(3, '0')}`;
}

export function fmtDateTime(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '—';
    return d.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
}
