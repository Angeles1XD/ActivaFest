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

    // ---------- Chat del asistente (RF12) ----------
    const CLAVE = 'activafest_chat_history';
    const ventana = document.getElementById('chat-window');
    const mensajes = document.getElementById('chat-messages');
    const entrada = document.getElementById('chat-input');
    const escribiendo = document.getElementById('chat-typing');
    if (!ventana) return;

    function historial() {
        try { return JSON.parse(localStorage.getItem(CLAVE)) || []; } catch (e) { return []; }
    }

    function guardar(texto, autor) {
        try {
            const h = historial();
            h.push({ text: texto, sender: autor });
            while (h.length > 20) h.shift();
            localStorage.setItem(CLAVE, JSON.stringify(h));
        } catch (e) { /* sin almacenamiento: el chat sigue funcionando */ }
    }

    // textContent (no innerHTML): el texto del usuario o del modelo nunca se interpreta como HTML
    function mostrar(texto, autor) {
        const div = document.createElement('div');
        div.className = 'msg msg-' + autor;
        div.textContent = texto;
        mensajes.appendChild(div);
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

    async function enviar(e) {
        e.preventDefault();
        const texto = entrada.value.trim();
        if (!texto) return;

        mostrar(texto, 'user');
        guardar(texto, 'user');
        entrada.value = '';
        escribiendo.style.display = 'block';

        try {
            const r = await fetch('/api/chat', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ message: texto })
            });
            if (!r.ok) throw new Error('Error en el servidor');
            const datos = await r.json();
            mostrar(datos.reply, 'bot');
            guardar(datos.reply, 'bot');
        } catch (err) {
            mostrar('No pude conectarme con el asistente en este momento. Inténtalo de nuevo en unos minutos.', 'bot');
        } finally {
            escribiendo.style.display = 'none';
        }
    }

    historial().forEach(function (m) { mostrar(m.text, m.sender); });
    if (historial().length === 0)
        mostrar('¡Hola! Soy el asistente de ActivaFest. ¿En qué evento te puedo ayudar hoy?', 'bot');

    document.getElementById('chat-toggle').addEventListener('click', alternar);
    document.getElementById('chat-cerrar').addEventListener('click', alternar);
    document.getElementById('chat-input-area').addEventListener('submit', enviar);
})();
