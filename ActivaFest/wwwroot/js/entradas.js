// =============================
// 🗺️ "VER MI UBICACIÓN EN EL PLANO" EN MIS ENTRADAS (HU27)
// =============================
// El plano (SVG generado por el servidor, con el asiento o la zona resaltada) se pide solo al abrirlo.
(function () {
    'use strict';

    document.querySelectorAll('details.entrada-plano').forEach(function (detalle) {
        detalle.addEventListener('toggle', async function () {
            if (!detalle.open || detalle.dataset.cargado) return;
            detalle.dataset.cargado = '1';
            const lienzo = detalle.querySelector('.plano-lienzo');
            try {
                const r = await fetch(detalle.dataset.url);
                if (!r.ok) throw new Error(r.status);
                const svg = new DOMParser().parseFromString(await r.text(), 'image/svg+xml').documentElement;
                if (svg.nodeName !== 'svg') throw new Error('respuesta inválida');
                lienzo.replaceChildren(document.importNode(svg, true));
                const tuyo = lienzo.querySelector('.asiento-tuyo, .plano-zona-tuya');
                if (tuyo) tuyo.scrollIntoView({ block: 'nearest', inline: 'center' });
            } catch (e) {
                lienzo.textContent = 'No se pudo cargar el plano. Inténtalo de nuevo.';
                delete detalle.dataset.cargado;
            }
        });
    });
})();
