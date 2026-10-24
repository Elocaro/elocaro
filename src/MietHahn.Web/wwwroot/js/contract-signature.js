// Signing page: optional drawn signature next to the typed name. Without this script the name alone is the signature.
(function () {
    const canvas = document.getElementById('signature-canvas');
    const holder = document.getElementById('signature-draw');
    const field = document.getElementById('signature-data');
    if (!canvas || !holder || !field || !canvas.getContext) {
        return;
    }

    holder.hidden = false;
    const ratio = Math.max(window.devicePixelRatio || 1, 1);
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    canvas.width = width * ratio;
    canvas.height = height * ratio;
    const context = canvas.getContext('2d');
    context.scale(ratio, ratio);
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, width, height);
    context.lineWidth = 2.2;
    context.lineCap = 'round';
    context.lineJoin = 'round';
    context.strokeStyle = '#111111';
    let drawing = false;
    let dirty = false;

    const position = event => {
        const rect = canvas.getBoundingClientRect();
        return { x: event.clientX - rect.left, y: event.clientY - rect.top };
    };

    canvas.addEventListener('pointerdown', event => {
        drawing = true;
        dirty = true;
        canvas.setPointerCapture(event.pointerId);
        const { x, y } = position(event);
        context.beginPath();
        context.moveTo(x, y);
        context.lineTo(x + 0.01, y + 0.01);
        context.stroke();
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

    const clear = document.getElementById('signature-clear');
    if (clear) {
        clear.addEventListener('click', () => {
            context.save();
            context.setTransform(1, 0, 0, 1, 0, 0);
            context.fillStyle = '#ffffff';
            context.fillRect(0, 0, canvas.width, canvas.height);
            context.restore();
            dirty = false;
            field.value = '';
        });
    }

    const form = canvas.closest('form');
    if (form) {
        form.addEventListener('submit', () => {
            field.value = dirty ? canvas.toDataURL('image/png').split(',')[1] : '';
        });
    }
})();
