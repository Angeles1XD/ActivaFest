// =============================
// 🪑 ELEGIR ZONA O ASIENTO EN EL PLANO AL COMPRAR (HU26)
// =============================
// Teatro: un clic en un asiento libre lo elige (otro clic lo suelta). Los grises (vendidos o bloqueados) no se
// pueden elegir. Lo elegido viaja al formulario como name="asientos"; el servidor vuelve a verificar todo.
// Otras plantillas: un clic en una zona lleva a su selector de cantidad.
(function () {
    'use strict';

    const plano = document.querySelector('.plano-interactivo');
    const formulario = document.getElementById('comprar');
    if (!plano || !formulario) return;

    const formato = new Intl.NumberFormat('es-PE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const caja = document.getElementById('asientosElegidos');

    // ---------- Zonas (campo abierto, estadio, feria) ----------
    if (!caja) {
        plano.querySelectorAll('.plano-zona:not(.plano-zona-agotada)').forEach(function (zona) {
            zona.setAttribute('tabindex', '0');
            zona.setAttribute('role', 'button');
            const elegir = function () {
                const selector = formulario.querySelector(`select[name="cantidades[${zona.dataset.zona}]"]`);
                if (!selector) return;
                formulario.scrollIntoView({ behavior: 'smooth', block: 'center' });
                if (selector.value === '0') {
                    selector.value = '1';
                    selector.dispatchEvent(new Event('change'));
                }
                selector.focus();
            };
            zona.addEventListener('click', elegir);
            zona.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); elegir(); } });
        });
        return;
    }

    // ---------- Asientos (teatro) ----------
    const maximo = parseInt(caja.dataset.max, 10) || 10;
    const lista = document.getElementById('listaAsientos');
    const inputs = document.getElementById('inputsAsientos');
    const total = document.getElementById('totalCompra');
    const boton = document.getElementById('btnComprar');
    const precios = {};
    formulario.querySelectorAll('.contador-zona').forEach(function (c) { precios[c.dataset.zona] = parseFloat(c.dataset.precio); });

    const elegidos = new Map(); // id → { zona, fila, numero }

    function actualizar() {
        lista.replaceChildren();
        inputs.replaceChildren();
        let suma = 0;
        const porZona = {};

        Array.from(elegidos.entries())
            .sort(function (a, b) { return a[1].fila.localeCompare(b[1].fila) || a[1].numero - b[1].numero; })
            .forEach(function ([id, a]) {
                const li = document.createElement('li');
                li.textContent = `🪑 Fila ${a.fila}, asiento ${a.numero}`;
                lista.appendChild(li);

                const input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'asientos';
                input.value = id;
                inputs.appendChild(input);

                suma += precios[a.zona] || 0;
                porZona[a.zona] = (porZona[a.zona] || 0) + 1;
            });

        formulario.querySelectorAll('.contador-zona').forEach(function (c) { c.textContent = porZona[c.dataset.zona] || 0; });
        total.textContent = suma === 0 && elegidos.size > 0 ? 'Gratis' : 'S/ ' + formato.format(suma);
        boton.disabled = elegidos.size === 0;
        boton.textContent = elegidos.size === 0
            ? '🎟️ Elige tus asientos en el plano'
            : `🎟️ Comprar ${elegidos.size} ${elegidos.size === 1 ? 'asiento' : 'asientos'}`;
    }

    function alternar(circulo) {
        if (circulo.dataset.libre !== '1') return;
        const id = circulo.dataset.asiento;

        if (elegidos.has(id)) {
            elegidos.delete(id);
            circulo.classList.remove('asiento-elegido');
        } else {
            if (elegidos.size >= maximo) {
                caja.querySelector('p').textContent = `Puedes elegir como máximo ${maximo} asientos por compra.`;
                return;
            }
            elegidos.set(id, { zona: circulo.dataset.zona, fila: circulo.dataset.fila, numero: parseInt(circulo.dataset.numero, 10) });
            circulo.classList.add('asiento-elegido');
        }
        circulo.setAttribute('aria-pressed', elegidos.has(id) ? 'true' : 'false');
        actualizar();
    }

    plano.querySelectorAll('circle.asiento').forEach(function (circulo) {
        if (circulo.dataset.libre !== '1') return;
        circulo.setAttribute('tabindex', '0');
        circulo.setAttribute('role', 'button');
        circulo.setAttribute('aria-pressed', 'false');
        circulo.setAttribute('aria-label', `Fila ${circulo.dataset.fila}, asiento ${circulo.dataset.numero}`);
        circulo.addEventListener('click', function () { alternar(circulo); });
        circulo.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); alternar(circulo); } });
    });

    actualizar();
})();
