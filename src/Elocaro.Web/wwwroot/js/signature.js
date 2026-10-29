// Signature field: draws with finger, pen or mouse on a canvas and hands the drawing over as PNG.
// Only what is needed for the handover protocol; no libraries.
const dirty = new WeakSet();

export function init(canvas) {
    const ratio = Math.max(window.devicePixelRatio || 1, 1);
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    canvas.width = width * ratio;
    canvas.height = height * ratio;
    const context = canvas.getContext('2d');
    context.scale(ratio, ratio);
    paintBackground(context, width, height);
    context.lineWidth = 2.2;
    context.lineCap = 'round';
    context.lineJoin = 'round';
    context.strokeStyle = '#111111';
    let drawing = false;

    const position = event => {
        const rect = canvas.getBoundingClientRect();
        return { x: event.clientX - rect.left, y: event.clientY - rect.top };
    };

    canvas.addEventListener('pointerdown', event => {
        drawing = true;
        canvas.setPointerCapture(event.pointerId);
        const { x, y } = position(event);
        context.beginPath();
        context.moveTo(x, y);
        context.lineTo(x + 0.01, y + 0.01);
        context.stroke();
        dirty.add(canvas);
        event.preventDefault();
    });
    canvas.addEventListener('pointermove', event => {
        if (!drawing) {
            return;
        }
        const { x, y } = position(event);
        context.lineTo(x, y);
        context.stroke();
        event.preventDefault();
    });
    const stop = () => { drawing = false; };
    canvas.addEventListener('pointerup', stop);
    canvas.addEventListener('pointercancel', stop);
}

function paintBackground(context, width, height) {
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, width, height);
}

export function clear(canvas) {
    const context = canvas.getContext('2d');
    context.save();
    context.setTransform(1, 0, 0, 1, 0, 0);
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.restore();
    dirty.delete(canvas);
}

export function isEmpty(canvas) {
    return !dirty.has(canvas);
}

// PNG without the data: prefix (base64).
export function toPng(canvas) {
    return canvas.toDataURL('image/png').split(',')[1];
}
