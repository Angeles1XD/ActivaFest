// =============================
// 📝 FORMULARIO DE CREACIÓN DE EVENTO
// Selector de ubicación (Leaflet + Nominatim), zonas de entrada dinámicas y vista previa de fotos.
// =============================
(function () {
    'use strict';

    const LIMA = [-12.0464, -77.0428];
    const MAX_TIPOS = 10;
    const MAX_FOTOS = 10;
    const MAX_BYTES = 5 * 1024 * 1024;

    // ---------- 🗺️ SELECTOR DE UBICACIÓN ----------
    const contenedorMapa = document.getElementById('mapaSelector');
    const inputLat = document.getElementById('Latitud');
    const inputLng = document.getElementById('Longitud');
    const inputDireccion = document.getElementById('Ubicacion');
    const info = document.getElementById('ubicacionInfo');

    function mostrar(mensaje, esError) {
        info.textContent = mensaje;
        info.classList.toggle('error', !!esError);
    }

    if (contenedorMapa && typeof L !== 'undefined') {
        const latInicial = parseFloat(contenedorMapa.dataset.lat);
        const lngInicial = parseFloat(contenedorMapa.dataset.lng);
        const tieneUbicacion = !isNaN(latInicial) && !isNaN(lngInicial);

        const mapa = L.map(contenedorMapa).setView(tieneUbicacion ? [latInicial, lngInicial] : LIMA, tieneUbicacion ? 16 : 12);

        L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
        }).addTo(mapa);

        let marcador = null;

        function fijarUbicacion(latlng, centrar) {
            const lat = latlng.lat.toFixed(6);
            const lng = latlng.lng.toFixed(6);
            inputLat.value = lat;
            inputLng.value = lng;

            if (!marcador) {
                marcador = L.marker(latlng, { draggable: true }).addTo(mapa);
                marcador.on('dragend', function () { fijarUbicacion(marcador.getLatLng(), false); });
            } else {
                marcador.setLatLng(latlng);
            }

            if (centrar) mapa.setView(latlng, 16);
            mostrar(`📍 Ubicación marcada (${lat}, ${lng})`);
        }

        if (tieneUbicacion) fijarUbicacion(L.latLng(latInicial, lngInicial), false);

        mapa.on('click', function (e) { fijarUbicacion(e.latlng, false); });

        // Geocodificación con Nominatim (OpenStreetMap). Uso liviano: solo al pulsar el botón.
        document.getElementById('btnBuscarDireccion').addEventListener('click', async function () {
            const consulta = inputDireccion.value.trim();
            if (consulta.length < 3) {
                mostrar('Escribe la dirección o el nombre del recinto.', true);
                return;
            }

            this.disabled = true;
            mostrar('Buscando dirección...');
            try {
                const url = 'https://nominatim.openstreetmap.org/search?format=json&limit=1&countrycodes=pe&accept-language=es&q='
                    + encodeURIComponent(consulta);
                const respuesta = await fetch(url, { headers: { 'Accept': 'application/json' } });
                const resultados = await respuesta.json();

                if (!resultados.length) {
                    mostrar('No encontramos esa dirección. Marca el punto directamente en el mapa.', true);
                } else {
                    fijarUbicacion(L.latLng(parseFloat(resultados[0].lat), parseFloat(resultados[0].lon)), true);
                    mostrar('📍 ' + resultados[0].display_name + ' — ajusta el marcador si hace falta.');
                }
            } catch (e) {
                console.error(e);
                mostrar('El buscador de direcciones no responde. Marca el punto en el mapa.', true);
            } finally {
                this.disabled = false;
            }
        });

        // Evita que Enter en el campo de dirección envíe el formulario
        inputDireccion.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                document.getElementById('btnBuscarDireccion').click();
            }
        });
    }

    // ---------- 🎟️ ZONAS DE ENTRADA ----------
    const lista = document.getElementById('tiposEntrada');
    const plantilla = document.getElementById('plantillaTipo');
    const aforoTotal = document.getElementById('aforoTotal');

    // Renumera los name="TiposEntrada[i].Campo" para que el model binding reciba una lista continua
    function renumerar() {
        const filas = lista.querySelectorAll('.tipo-entrada');
        filas.forEach(function (fila, i) {
            fila.querySelectorAll('input').forEach(function (input) {
                const campo = input.dataset.campo || input.name.split('.').pop();
                input.dataset.campo = campo;
                input.name = `TiposEntrada[${i}].${campo}`;
            });
            // No se puede quitar la única zona ni una zona con entradas vendidas o reservadas
            const comprometidas = parseInt(fila.dataset.comprometidas || '0', 10);
            fila.querySelector('.btn-quitar').disabled = filas.length === 1 || comprometidas > 0;
        });
        document.getElementById('btnAgregarTipo').disabled = filas.length >= MAX_TIPOS;
        recalcularAforo();
    }

    function recalcularAforo() {
        let total = 0;
        lista.querySelectorAll('input[data-campo="Capacidad"]').forEach(function (input) {
            total += parseInt(input.value, 10) || 0;
        });
        aforoTotal.textContent = total.toLocaleString('es-PE');
    }

    document.getElementById('btnAgregarTipo').addEventListener('click', function () {
        lista.appendChild(plantilla.content.cloneNode(true));
        renumerar();
        lista.lastElementChild.querySelector('input').focus();
    });

    lista.addEventListener('click', function (e) {
        if (e.target.classList.contains('btn-quitar')) {
            e.target.closest('.tipo-entrada').remove();
            renumerar();
        }
    });

    lista.addEventListener('input', recalcularAforo);
    renumerar();

    // ---------- 📸 VISTA PREVIA DE FOTOS ----------
    const inputFotos = document.getElementById('Fotos');
    const preview = document.getElementById('previewFotos');

    inputFotos.addEventListener('change', function () {
        preview.replaceChildren();
        const archivos = Array.from(inputFotos.files);
        const errores = [];

        if (archivos.length > MAX_FOTOS) errores.push(`Máximo ${MAX_FOTOS} fotos.`);

        archivos.forEach(function (archivo, i) {
            if (archivo.size > MAX_BYTES) errores.push(`${archivo.name} supera los 5 MB.`);

            const figura = document.createElement('figure');
            const img = document.createElement('img');
            img.src = URL.createObjectURL(archivo);
            img.alt = archivo.name;
            img.onload = function () { URL.revokeObjectURL(img.src); };

            const pie = document.createElement('figcaption');
            pie.textContent = i === 0 ? 'Portada' : `Foto ${i + 1}`;

            figura.append(img, pie);
            preview.appendChild(figura);
        });

        if (errores.length) {
            const aviso = document.createElement('p');
            aviso.className = 'text-danger';
            aviso.textContent = errores.join(' ');
            preview.prepend(aviso);
        }
    });
})();
