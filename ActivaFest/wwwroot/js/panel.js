// =============================
// 📊 TOOLTIP DEL GRÁFICO DE VENTAS (hover y teclado)
// =============================
(function () {
    'use strict';

    const grafico = document.getElementById('graficoVentas');
    const tooltip = document.getElementById('tooltipGrafico');
    if (!grafico || !tooltip) return;

    function mostrar(columna) {
        tooltip.textContent = columna.dataset.tooltip;
        tooltip.hidden = false;

        const caja = grafico.getBoundingClientRect();
        const marca = columna.getBoundingClientRect();
        const x = marca.left - caja.left + marca.width / 2;

        // Mantiene el tooltip dentro del gráfico
        const mitad = tooltip.offsetWidth / 2;
        tooltip.style.left = Math.min(Math.max(x, mitad), caja.width - mitad) + 'px';
        tooltip.style.top = '0px';
        columna.classList.add('activa');
    }

    function ocultar(columna) {
        tooltip.hidden = true;
        columna.classList.remove('activa');
    }

    grafico.querySelectorAll('.columna').forEach(function (columna) {
        columna.addEventListener('mouseenter', function () { mostrar(columna); });
        columna.addEventListener('mouseleave', function () { ocultar(columna); });
        columna.addEventListener('focus', function () { mostrar(columna); });
        columna.addEventListener('blur', function () { ocultar(columna); });
    });
})();
