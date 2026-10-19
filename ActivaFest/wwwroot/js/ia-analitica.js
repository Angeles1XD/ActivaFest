// =============================
// 🤖 CONSULTAS A LA IA DESDE EL DASHBOARD (HU24)
// =============================
// La respuesta trae el texto, la tabla de cifras fuente, el período y un gráfico sugerido.
// Todo se pinta con textContent (nunca innerHTML): ni la pregunta ni la respuesta pueden inyectar HTML.
(function () {
    'use strict';

    const formulario = document.getElementById('iaFormulario');
    if (!formulario) return;

    const conversacion = document.getElementById('iaConversacion');
    const pregunta = document.getElementById('iaPregunta');
    const boton = formulario.querySelector('button[type="submit"]');
    const token = formulario.querySelector('input[name="__RequestVerificationToken"]').value;
    let graficos = 0;

    function elemento(tag, clase, texto) {
        const e = document.createElement(tag);
        if (clase) e.className = clase;
        if (texto !== undefined) e.textContent = texto;
        return e;
    }

    function tabla(t) {
        const caja = elemento('div', 'tabla-contenedor');
        const table = elemento('table', 'tabla-datos ia-tabla');
        table.appendChild(elemento('caption', 'texto-tenue', t.titulo));
        const fila = elemento('tr');
        t.columnas.forEach(function (c) { fila.appendChild(elemento('th', null, c)); });
        table.appendChild(elemento('thead')).appendChild(fila);
        const cuerpo = elemento('tbody');
        if (t.filas.length === 0) {
            const vacia = elemento('td', 'texto-tenue', 'Sin datos para este período.');
            vacia.colSpan = t.columnas.length;
            cuerpo.appendChild(elemento('tr')).appendChild(vacia);
        }
        t.filas.forEach(function (f) {
            const tr = elemento('tr');
            f.forEach(function (v) { tr.appendChild(elemento('td', null, v)); });
            cuerpo.appendChild(tr);
        });
        table.appendChild(cuerpo);
        caja.appendChild(table);
        return caja;
    }

    function grafico(g) {
        if (typeof Chart === 'undefined') return null;
        const caja = elemento('div', 'grafico-chart ia-grafico');
        const canvas = elemento('canvas');
        canvas.id = 'iaGrafico' + (++graficos);
        canvas.setAttribute('role', 'img');
        canvas.setAttribute('aria-label', g.serie);
        caja.appendChild(canvas);
        requestAnimationFrame(function () {
            const colores = ['#8b5cf6', '#ec4899', '#0d9488', '#f59e0b', '#6366f1', '#22c55e', '#38bdf8', '#f97316', '#a3e635', '#e879f9'];
            new Chart(canvas, {
                type: g.tipo === 'doughnut' ? 'doughnut' : 'bar',
                data: {
                    labels: g.etiquetas,
                    datasets: [{ label: g.serie, data: g.valores, backgroundColor: g.tipo === 'doughnut' ? colores : '#8b5cf6', borderRadius: 6 }]
                },
                options: {
                    responsive: true, maintainAspectRatio: false,
                    plugins: { legend: { display: g.tipo === 'doughnut', labels: { color: '#c4c4d4' } } },
                    scales: g.tipo === 'doughnut' ? {} : {
                        x: { ticks: { color: '#a1a1b5', maxRotation: 30, autoSkip: false } },
                        y: { beginAtZero: true, ticks: { color: '#a1a1b5' }, grid: { color: 'rgba(255,255,255,.06)' } }
                    }
                }
            });
        });
        return caja;
    }

    function mostrarPregunta(texto) {
        conversacion.appendChild(elemento('div', 'ia-msg ia-msg-admin', texto));
    }

    function mostrarRespuesta(r) {
        const caja = elemento('div', 'ia-msg ia-msg-ia' + (r.fueraDeAlcance ? ' ia-fuera' : ''));
        caja.appendChild(elemento('p', 'ia-texto', r.texto));
        if (r.funcion) {
            caja.appendChild(elemento('p', 'ia-fuente',
                `Fuente: ${r.funcion} · Período: ${r.periodo}${r.redactadaPorIa ? ' · redactado con IA' : ''}`));
        }
        if (r.tabla) caja.appendChild(tabla(r.tabla));
        if (r.grafico) {
            const g = grafico(r.grafico);
            if (g) caja.appendChild(g);
        }
        if (r.sugerencias && r.sugerencias.length) {
            const chips = elemento('div', 'ia-ejemplos');
            r.sugerencias.forEach(function (s) {
                const b = elemento('button', 'ia-ejemplo', s);
                b.type = 'button';
                chips.appendChild(b);
            });
            caja.appendChild(chips);
        }
        conversacion.appendChild(caja);
        caja.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    async function preguntar(texto) {
        texto = texto.trim();
        if (!texto) return;
        mostrarPregunta(texto);
        pregunta.value = '';
        boton.disabled = true;
        const espera = elemento('div', 'ia-msg ia-msg-ia texto-tenue', 'Consultando los datos…');
        conversacion.appendChild(espera);
        try {
            const r = await fetch(formulario.dataset.url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ pregunta: texto })
            });
            if (!r.ok) throw new Error(r.status);
            espera.remove();
            mostrarRespuesta(await r.json());
        } catch (e) {
            espera.textContent = 'No se pudo hacer la consulta. Inténtalo de nuevo.';
        } finally {
            boton.disabled = false;
            pregunta.focus();
        }
    }

    formulario.addEventListener('submit', function (e) {
        e.preventDefault();
        preguntar(pregunta.value);
    });

    // Los ejemplos (y las sugerencias de cada respuesta) se preguntan con un clic
    document.querySelector('.panel-ia').addEventListener('click', function (e) {
        const chip = e.target.closest('.ia-ejemplo');
        if (chip) preguntar(chip.textContent);
    });
})();
