// Editor for document templates (HTML with Scriban tags {{ … }}): syntax highlighting behind a transparent textarea, a toolbar,
// keyboard shortcuts and the live preview frame. No external libraries; the text stays a plain textarea (works with the browser's
// undo, spell check off, accessible by keyboard).

const KEYWORDS = new Set(['if', 'else', 'end', 'for', 'while', 'case', 'when', 'include', 'capture', 'with', 'func', 'ret', 'break', 'continue', 'in', 'and', 'or', 'not', 'empty', 'null', 'true', 'false']);
const escapeHtml = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const span = (cls, text) => `<span class="tok-${cls}">${escapeHtml(text)}</span>`;

// Scriban expression inside {{ … }}: keywords, strings, numbers, names.
const highlightExpression = (code) => {
    let out = '';
    const pattern = /('(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*")|(\d+(?:[.,]\d+)?)|([A-Za-z_][\w.]*)|([\s\S])/g;
    for (let m; (m = pattern.exec(code)) !== null;) {
        if (m[1]) { out += span('string', m[1]); }
        else if (m[2]) { out += span('number', m[2]); }
        else if (m[3]) { out += KEYWORDS.has(m[3]) ? span('keyword', m[3]) : span('name', m[3]); }
        else { out += escapeHtml(m[4]); }
    }

    return out;
};

// Scriban tag at position i: returns the end index (exclusive) or -1.
const scribanEnd = (text, i) => {
    if (text[i] !== '{' || text[i + 1] !== '{') { return -1; }
    const end = text.indexOf('}}', i + 2);
    return end === -1 ? -1 : end + 2;
};

const highlightScriban = (tag) => {
    if (tag.startsWith('{{#')) { return span('comment', tag); }
    const open = tag.startsWith('{{~') ? '{{~' : '{{';
    const close = tag.endsWith('~}}') ? '~}}' : '}}';
    return span('delim', open) + highlightExpression(tag.slice(open.length, tag.length - close.length)) + span('delim', close);
};

// Text with Scriban tags between (inside attribute values and text content).
const highlightText = (text, cls) => {
    let out = '';
    for (let i = 0; i < text.length;) {
        const end = scribanEnd(text, i);
        if (end !== -1) { out += highlightScriban(text.slice(i, end)); i = end; continue; }
        let next = text.indexOf('{{', i + 1);
        if (next === -1) { next = text.length; }
        const chunk = text.slice(i, next);
        out += cls ? span(cls, chunk) : escapeHtml(chunk);
        i = next;
    }

    return out;
};

export function highlight(text) {
    let out = '';
    let i = 0;
    while (i < text.length) {
        const rest = text.slice(i);
        let end = scribanEnd(text, i);
        if (end !== -1) { out += highlightScriban(text.slice(i, end)); i = end; continue; }

        if (rest.startsWith('<!--')) {
            end = text.indexOf('-->', i + 4);
            end = end === -1 ? text.length : end + 3;
            out += span('comment', text.slice(i, end)); i = end; continue;
        }

        const tag = /^<\/?([A-Za-z][\w-]*)/.exec(rest);
        if (tag) {
            out += span('tag', tag[0]); i += tag[0].length;
            // attributes up to the closing >
            while (i < text.length && text[i] !== '>') {
                const attr = /^([^\s=>{\/]+)/.exec(text.slice(i));
                const quoted = /^=\s*("[^"]*"|'[^']*')/.exec(text.slice(i));
                const e = scribanEnd(text, i);
                if (e !== -1) { out += highlightScriban(text.slice(i, e)); i = e; }
                else if (quoted) { out += span('delim', '=') + highlightText(quoted[0].slice(1).trimStart(), 'attrval'); i += quoted[0].length; }
                else if (attr) { out += span('attr', attr[1]); i += attr[1].length; }
                else { out += escapeHtml(text[i]); i++; }
            }

            if (i < text.length) { out += span('tag', text[i]); i++; }
            continue;
        }

        const entity = /^&[#\w]+;/.exec(rest);
        if (entity) { out += span('entity', entity[0]); i += entity[0].length; continue; }

        // plain text up to the next special character
        const stop = rest.slice(1).search(/[<{&]/);
        const length = stop === -1 ? rest.length : stop + 1;
        out += escapeHtml(rest.slice(0, length));
        i += length;
    }

    // A trailing newline needs a character, otherwise the last empty line is not as high as in the textarea.
    return out.endsWith('\n') ? out + ' ' : out;
}

const insertText = (area, text) => {
    area.focus();
    // execCommand keeps the native undo stack; setRangeText is the fallback.
    if (!document.execCommand('insertText', false, text)) {
        area.setRangeText(text, area.selectionStart, area.selectionEnd, 'end');
        area.dispatchEvent(new Event('input', { bubbles: true }));
    }
};

// Wraps the selection (or a placeholder text) and selects what the user is most likely to replace.
const wrap = (area, before, after, placeholder) => {
    const start = area.selectionStart;
    const selected = area.value.slice(start, area.selectionEnd) || placeholder;
    insertText(area, before + selected + after);
    area.setSelectionRange(start + before.length, start + before.length + selected.length);
};

const lines = (area) => {
    const selected = area.value.slice(area.selectionStart, area.selectionEnd);
    return selected ? selected.split('\n') : ['Eintrag'];
};

const COMMANDS = {
    bold: (a) => wrap(a, '<strong>', '</strong>', 'fett'),
    italic: (a) => wrap(a, '<em>', '</em>', 'kursiv'),
    underline: (a) => wrap(a, '<u>', '</u>', 'unterstrichen'),
    paragraph: (a) => wrap(a, '<p>', '</p>', 'Text'),
    h2: (a) => wrap(a, '<h2>', '</h2>', 'Überschrift'),
    h3: (a) => wrap(a, '<h3>', '</h3>', 'Zwischenüberschrift'),
    br: (a) => insertText(a, '<br>\n'),
    hr: (a) => insertText(a, '\n<hr>\n'),
    ul: (a) => insertText(a, '<ul>\n' + lines(a).map((l) => `  <li>${l}</li>`).join('\n') + '\n</ul>\n'),
    ol: (a) => insertText(a, '<ol>\n' + lines(a).map((l) => `  <li>${l}</li>`).join('\n') + '\n</ol>\n'),
    table: (a) => insertText(a, '<table>\n  <thead><tr><th>Bezeichnung</th><th class="number">Betrag</th></tr></thead>\n  <tbody>\n    <tr><td>Position</td><td class="number">0,00 €</td></tr>\n  </tbody>\n</table>\n'),
    pagebreak: (a) => insertText(a, '<div class="seitenumbruch"></div>\n'),
    if: (a) => wrap(a, '{{ if bedingung }}\n', '\n{{ end }}', 'Text'),
    ifelse: (a) => wrap(a, '{{ if bedingung }}\n', '\n{{ else }}\n\n{{ end }}', 'Text'),
    for: (a) => wrap(a, '{{ for eintrag in liste }}\n', '\n{{ end }}', 'Text'),
    undo: () => document.execCommand('undo'),
    redo: () => document.execCommand('redo'),
};

const SHORTCUTS = { b: 'bold', i: 'italic', u: 'underline' };

export function init(root, dotNet, initialText) {
    const area = root.querySelector('textarea');
    const layer = root.querySelector('.code-highlight code');
    const pre = root.querySelector('.code-highlight');
    let timer = null;
    let sent = initialText ?? '';

    const render = () => { layer.innerHTML = highlight(area.value); pre.scrollTop = area.scrollTop; pre.scrollLeft = area.scrollLeft; };
    const flush = () => {
        clearTimeout(timer);
        if (area.value !== sent) { sent = area.value; dotNet.invokeMethodAsync('OnText', area.value); }
    };

    area.value = initialText ?? '';
    render();
    area.addEventListener('input', () => { render(); clearTimeout(timer); timer = setTimeout(flush, 450); });
    area.addEventListener('scroll', () => { pre.scrollTop = area.scrollTop; pre.scrollLeft = area.scrollLeft; });
    area.addEventListener('blur', flush);
    area.addEventListener('keydown', (event) => {
        if (!(event.ctrlKey || event.metaKey)) { return; }
        const key = event.key.toLowerCase();
        if (SHORTCUTS[key]) { event.preventDefault(); COMMANDS[SHORTCUTS[key]](area); }
        else if (key === 's') { event.preventDefault(); flush(); area.form?.requestSubmit(); }
    });

    const toolbar = root.querySelector('.code-toolbar');
    toolbar.addEventListener('click', (event) => {
        const button = event.target instanceof Element ? event.target.closest('[data-cmd]') : null;
        if (button && COMMANDS[button.dataset.cmd]) { COMMANDS[button.dataset.cmd](area); }
    });
    toolbar.addEventListener('change', (event) => {
        const select = event.target;
        if (select instanceof HTMLSelectElement && select.value) {
            insertText(area, select.dataset.insert === 'snippet' ? `{{ include '${select.value}' }}` : select.value);
            select.value = '';
        }
    });

    return {
        setValue(text) { area.value = text ?? ''; sent = area.value; render(); },
        dispose() { clearTimeout(timer); },
    };
}

// Live preview: writes the HTML into the frame (no reload, scroll position kept) and scales the sheet to the width of the frame.
export function setPreview(frame, html) {
    const doc = frame.contentDocument;
    if (!doc) { return; }
    const top = doc.documentElement?.scrollTop ?? 0;
    const screen = '<style>html{background:#dcd6d2}body{box-sizing:border-box;width:210mm;min-height:297mm;margin:6mm auto;background:#fff;box-shadow:0 1px 6px rgb(0 0 0/25%);padding-top:20mm}</style>';
    doc.open();
    doc.write(html.includes('</head>') ? html.replace('</head>', screen + '</head>') : screen + html);
    doc.close();
    if (!frame.__observed) { frame.__observed = true; new ResizeObserver(() => fitPreview(frame)).observe(frame); }
    fitPreview(frame);
    requestAnimationFrame(() => { doc.documentElement.scrollTop = top; });
}

let factor = 1;

export function fitPreview(frame) {
    const doc = frame.contentDocument;
    if (!doc?.documentElement || frame.clientWidth === 0) { return; }
    const sheet = 210 / 25.4 * 96 + 24; // A4 in CSS pixels plus the margin at the sides
    doc.documentElement.style.zoom = String(Math.min(3, Math.min(1.2, frame.clientWidth / sheet) * factor));
}

// Size buttons of the preview (−, fit, +).
document.addEventListener('click', (event) => {
    const button = event.target instanceof Element ? event.target.closest('[data-preview-zoom]') : null;
    const frame = document.querySelector('iframe.template-preview');
    if (!button || !frame) { return; }
    const way = button.dataset.previewZoom;
    factor = way === 'in' ? Math.min(2.5, factor * 1.25) : way === 'out' ? Math.max(0.5, factor / 1.25) : 1;
    fitPreview(frame);
});
