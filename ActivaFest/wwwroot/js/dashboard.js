// =============================
// 📊 GRÁFICOS DEL DASHBOARD DEL ADMINISTRADOR (HU21, Chart.js)
// =============================
(function () {
    'use strict';

    const fuente = document.getElementById('datosDashboard');
    if (!fuente || typeof Chart === 'undefined') return;

    const datos = JSON.parse(fuente.textContent);
    const estilo = getComputedStyle(document.documentElement);
    const color = (n, d) => (estilo.getPropertyValue(n) || d).trim();
    const MORADO = color('--morado', '#8b5cf6');
    const soles = v => 'S/ ' + Number(v).toLocaleString('es-PE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    Chart.defaults.color = color('--texto-2', '#a1a1aa');
    Chart.defaults.font.family = color('--fuente', 'Outfit, sans-serif');
    Chart.defaults.borderColor = 'rgba(255, 255, 255, 0.08)';

    const semanas = document.getElementById('graficoSemanas');
    if (semanas) {
        new Chart(semanas, {
            type: 'line',
            data: {
                labels: datos.semanas.map(s => s.etiqueta),
                datasets: [{ label: 'Ingresos', data: datos.semanas.map(s => s.ingresos), borderColor: MORADO, backgroundColor: 'rgba(139, 92, 246, 0.18)', fill: true, tension: 0, pointRadius: 3 }]
            },
            options: {
                maintainAspectRatio: false,
                plugins: { legend: { display: false }, tooltip: { callbacks: { label: c => soles(c.parsed.y) } } },
                scales: { x: { grid: { display: false } }, y: { beginAtZero: true, ticks: { callback: v => 'S/ ' + v } } }
            }
        });
    }

    const categorias = document.getElementById('graficoCategorias');
    if (categorias) {
        new Chart(categorias, {
            type: 'bar',
            data: {
                labels: datos.categorias.map(c => c.nombre),
                datasets: [{ label: 'Entradas', data: datos.categorias.map(c => c.entradas), backgroundColor: datos.categorias.map(c => c.color || MORADO), borderRadius: 4 }]
            },
            options: {
                indexAxis: 'y',
                maintainAspectRatio: false,
                plugins: { legend: { display: false }, tooltip: { callbacks: { afterLabel: c => 'Ingresos: ' + soles(datos.categorias[c.dataIndex].ingresos) } } },
                scales: { x: { beginAtZero: true, ticks: { precision: 0 } }, y: { grid: { display: false } } }
            }
        });
    }
})();
