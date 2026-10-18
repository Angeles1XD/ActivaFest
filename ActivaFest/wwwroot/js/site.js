// =============================
// 🧭 MENÚ MÓVIL Y 🤖 ASISTENTE VIRTUAL (comunes a todas las páginas)
// =============================
(function () {
    'use strict';

    // ---------- Menú hamburguesa ----------
    const toggle = document.getElementById('navToggle');
    const menu = document.getElementById('navMenu');
    if (toggle && menu) {
        toggle.addEventListener('click', function () {
            const abierto = menu.classList.toggle('abierto');
            toggle.setAttribute('aria-expanded', String(abierto));
            toggle.setAttribute('aria-label', abierto ? 'Cerrar menú' : 'Abrir menú');
        });
    }

    // ---------- ⏱️ Tiempo restante de un destacado (HU12) ----------
    // El servidor indica los segundos que quedan; aquí solo se descuentan (no depende del reloj del usuario)
    document.querySelectorAll('.temporizador-destacado').forEach(function (el) {
        const fin = Date.now() + (parseInt(el.dataset.segundos, 10) || 0) * 1000;
        const pintar = function () {
            const s = Math.max(0, Math.floor((fin - Date.now()) / 1000));
            const d = Math.floor(s / 86400), h = Math.floor(s % 86400 / 3600), m = Math.floor(s % 3600 / 60);
            el.textContent = s === 0 ? 'periodo terminado'
                : '⏱️ quedan ' + (d > 0 ? d + ' d ' : '') + (d > 0 || h > 0 ? h + ' h ' : '') + m + ' min' + (d === 0 && h === 0 ? ' ' + (s % 60) + ' s' : '');
            if (s === 0) clearInterval(intervalo);
        };
        const intervalo = setInterval(pintar, 1000);
        pintar();
    });

    // ---------- Chat del asistente (HU14 / RF12) ----------
    const CLAVE = 'activafest_chat_v2';
    const ventana = document.getElementById('chat-window');
    const mensajes = document.getElementById('chat-messages');
    const entrada = document.getElementById('chat-input');
    const escribiendo = document.getElementById('chat-typing');
    if (!ventana) return;

    const BIENVENIDA = {
        texto: '¡Hola! Soy el asistente de ActivaFest 👋 Pregúntame por eventos, horarios, precios, ubicación o cómo comprar.',
        sugerencias: ['¿Qué eventos hay este fin de semana?', 'Recomiéndame algo', '¿Cómo compro entradas?']
    };

    // En la ficha de un evento, el asistente sabe de qué evento se habla ("¿dónde es?")
    const contexto = document.getElementById('chat-contexto');
    let eventoId = contexto ? parseInt(contexto.dataset.evento, 10) || null : null;
    let ubicacion = null;

    function historial() {
        try { return JSON.parse(localStorage.getItem(CLAVE)) || []; } catch (e) { return []; }
    }

    function guardar(mensaje) {
        try {
            const h = historial();
            h.push(mensaje);
            while (h.length > 20) h.shift();
            localStorage.setItem(CLAVE, JSON.stringify(h));
        } catch (e) { /* sin almacenamiento: el chat sigue funcionando */ }
    }

    // Solo enlaces internos o a Google Maps: una respuesta nunca puede llevar a un sitio arbitrario
    function urlSegura(url) {
        return typeof url === 'string' &&
            ((url.startsWith('/') && !url.startsWith('//')) || url.startsWith('https://www.google.com/maps/'));
    }

    function quitarSugerencias() {
        mensajes.querySelectorAll('.chat-sugerencias').forEach(function (s) { s.remove(); });
    }

    // textContent (no innerHTML): ni el usuario ni el asistente pueden inyectar HTML
    function mostrar(m) {
        const div = document.createElement('div');
        div.className = 'msg msg-' + m.autor;
        div.textContent = m.texto;

        const enlaces = (m.enlaces || []).filter(function (e) { return urlSegura(e.url); });
        if (enlaces.length) {
            const lista = document.createElement('div');
            lista.className = 'chat-enlaces';
            enlaces.forEach(function (e) {
                const a = document.createElement('a');
                a.href = e.url;
                a.textContent = e.texto;
                if (e.url.startsWith('https://')) { a.target = '_blank'; a.rel = 'noopener noreferrer'; }
                lista.appendChild(a);
            });
            div.appendChild(lista);
        }
        mensajes.appendChild(div);

        if (m.sugerencias && m.sugerencias.length) {
            quitarSugerencias();
            const chips = document.createElement('div');
            chips.className = 'chat-sugerencias';
            m.sugerencias.forEach(function (s) {
                const b = document.createElement('button');
                b.type = 'button';
                b.textContent = s;
                b.addEventListener('click', function () { preguntar(s); });
                chips.appendChild(b);
            });
            mensajes.appendChild(chips);
        }
        mensajes.scrollTop = mensajes.scrollHeight;
    }

    function alternar() {
        const abrir = !ventana.classList.contains('abierto');
        ventana.classList.toggle('abierto', abrir);
        if (abrir) {
            entrada.focus();
            mensajes.scrollTop = mensajes.scrollHeight;
        }
    }

    // La ubicación solo se pide si el usuario pregunta por recomendaciones o cercanía, y no se guarda
    function pedirUbicacion(texto) {
        if (ubicacion || !navigator.geolocation || !/recom|sugi|cerca|plan/i.test(texto)) return Promise.resolve();
        return new Promise(function (listo) {
            navigator.geolocation.getCurrentPosition(
                function (p) { ubicacion = { latitud: p.coords.latitude, longitud: p.coords.longitude }; listo(); },
                function () { listo(); },
                { timeout: 5000, maximumAge: 600000 });
        });
    }

    async function preguntar(texto) {
        texto = texto.trim();
        if (!texto) return;

        quitarSugerencias();
        const mensajeUsuario = { autor: 'user', texto: texto };
        mostrar(mensajeUsuario);
        guardar(mensajeUsuario);
        entrada.value = '';
        escribiendo.style.display = 'block';

        try {
            await pedirUbicacion(texto);
            const r = await fetch('/api/chat', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    mensaje: texto,
                    eventoId: eventoId,
                    latitud: ubicacion ? ubicacion.latitud : null,
                    longitud: ubicacion ? ubicacion.longitud : null
                })
            });
            if (r.status === 429) throw new Error('limite');
            if (!r.ok) throw new Error('servidor');

            const datos = await r.json();
            if (datos.eventoId) eventoId = datos.eventoId; // para preguntas de seguimiento
            const respuesta = { autor: 'bot', texto: datos.reply, enlaces: datos.enlaces, sugerencias: datos.sugerencias };
            mostrar(respuesta);
            guardar(respuesta);
        } catch (err) {
            mostrar({
                autor: 'bot',
                texto: err.message === 'limite'
                    ? 'Estás enviando muchos mensajes seguidos. Espera un minuto y vuelve a intentarlo.'
                    : 'No pude conectarme con el asistente en este momento. Inténtalo de nuevo en unos minutos.'
            });
        } finally {
            escribiendo.style.display = 'none';
        }
    }

    const previos = historial();
    if (previos.length === 0) mostrar({ autor: 'bot', texto: BIENVENIDA.texto, sugerencias: BIENVENIDA.sugerencias });
    // Las sugerencias antiguas no se vuelven a mostrar: solo aplican a la última respuesta
    previos.forEach(function (m, i) {
        mostrar({ autor: m.autor, texto: m.texto, enlaces: m.enlaces, sugerencias: i === previos.length - 1 ? m.sugerencias : null });
    });

    document.getElementById('chat-toggle').addEventListener('click', alternar);
    document.getElementById('chat-cerrar').addEventListener('click', alternar);
    document.getElementById('chat-input-area').addEventListener('submit', function (e) {
        e.preventDefault();
        preguntar(entrada.value);
    });
})();
