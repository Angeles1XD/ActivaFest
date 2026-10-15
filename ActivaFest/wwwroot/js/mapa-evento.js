// =============================
// 🗺️ MAPA DEL EVENTO Y TRAZADO DE RUTA (HU04)
// Leaflet + OpenStreetMap para el mapa, OSRM para calcular la ruta desde la ubicación del usuario.
// =============================
(function () {
    'use strict';

    const contenedor = document.getElementById('mapaEvento');
    if (!contenedor || typeof L === 'undefined') return;

    const OSRM_URL = 'https://router.project-osrm.org/route/v1/driving/';
    const destino = L.latLng(parseFloat(contenedor.dataset.lat), parseFloat(contenedor.dataset.lng));
    const info = document.getElementById('rutaInfo');
    const boton = document.getElementById('btnRuta');

    const mapa = L.map(contenedor, { scrollWheelZoom: false }).setView(destino, 16);

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(mapa);

    // Popup construido con nodos de texto para no interpretar HTML del usuario
    const popup = document.createElement('div');
    const titulo = document.createElement('strong');
    titulo.textContent = contenedor.dataset.titulo;
    popup.append(titulo, document.createElement('br'), contenedor.dataset.direccion);
    L.marker(destino).addTo(mapa).bindPopup(popup).openPopup();

    let capaRuta = null;
    let marcadorUsuario = null;

    function mostrar(mensaje, esError) {
        info.textContent = mensaje;
        info.classList.toggle('error', !!esError);
    }

    function formatearDistancia(metros) {
        return metros >= 1000 ? (metros / 1000).toFixed(1) + ' km' : Math.round(metros) + ' m';
    }

    function formatearDuracion(segundos) {
        const minutos = Math.round(segundos / 60);
        if (minutos < 60) return minutos + ' min';
        return Math.floor(minutos / 60) + ' h ' + (minutos % 60) + ' min';
    }

    async function trazarRuta(origen) {
        const coords = `${origen.lng},${origen.lat};${destino.lng},${destino.lat}`;
        const respuesta = await fetch(`${OSRM_URL}${coords}?overview=full&geometries=geojson`);
        if (!respuesta.ok) throw new Error('OSRM ' + respuesta.status);

        const datos = await respuesta.json();
        if (datos.code !== 'Ok' || !datos.routes.length) throw new Error('Sin ruta');

        const ruta = datos.routes[0];

        if (capaRuta) mapa.removeLayer(capaRuta);
        if (marcadorUsuario) mapa.removeLayer(marcadorUsuario);

        capaRuta = L.geoJSON(ruta.geometry, { style: { color: '#ff006e', weight: 5, opacity: 0.85 } }).addTo(mapa);
        marcadorUsuario = L.circleMarker(origen, {
            radius: 8, color: '#ffffff', weight: 2, fillColor: '#3388ff', fillOpacity: 1
        }).addTo(mapa).bindTooltip('Tu ubicación');

        mapa.fitBounds(capaRuta.getBounds(), { padding: [30, 30] });
        mostrar(`🚗 ${formatearDistancia(ruta.distance)} · aprox. ${formatearDuracion(ruta.duration)} en auto`);
    }

    boton.addEventListener('click', function () {
        if (!navigator.geolocation) {
            mostrar('Tu navegador no permite obtener la ubicación. Usa "Abrir en Google Maps".', true);
            return;
        }

        boton.disabled = true;
        mostrar('Obteniendo tu ubicación...');

        navigator.geolocation.getCurrentPosition(
            async function (pos) {
                mostrar('Calculando la ruta...');
                try {
                    await trazarRuta(L.latLng(pos.coords.latitude, pos.coords.longitude));
                } catch (e) {
                    console.error(e);
                    mostrar('No se pudo calcular la ruta en este momento. Intenta con "Abrir en Google Maps".', true);
                } finally {
                    boton.disabled = false;
                }
            },
            function (err) {
                boton.disabled = false;
                const mensajes = {
                    1: 'Necesitamos permiso para usar tu ubicación. Actívalo en el navegador e inténtalo otra vez.',
                    2: 'No pudimos determinar tu ubicación.',
                    3: 'Se agotó el tiempo para obtener tu ubicación.'
                };
                mostrar(mensajes[err.code] || 'No se pudo obtener tu ubicación.', true);
            },
            { enableHighAccuracy: true, timeout: 15000, maximumAge: 60000 }
        );
    });
})();
