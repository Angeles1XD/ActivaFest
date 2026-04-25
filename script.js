// ========================================
// YouthEvents - JavaScript
// Funcionalidad: Buscador, ubicación, eventos
// ========================================

// Datos de eventos (simulación de base de datos)
const eventosData = [
    {
        id: 1,
        titulo: "TechHack 2024",
        categoria: "tecnologia",
        fecha: "2024-05-15",
        precio: 25,
        ubicacion: "Madrid",
        imagen: "https://images.unsplash.com/photo-1540575467063-178a50c2df87?w=400",
        descripcion: "Hackathon de 48 horas"
    },
    {
        id: 2,
        titulo: "Summer Music Fest",
        categoria: "musica",
        fecha: "2024-06-20",
        precio: 45,
        ubicacion: "Barcelona",
        imagen: "https://images.unsplash.com/photo-1459749411175-04bf5292ceea?w=400",
        descripcion: "Festival de música urbana"
    },
    {
        id: 3,
        titulo: "Gaming Championship",
        categoria: "gaming",
        fecha: "2024-05-10",
        precio: 15,
        ubicacion: "Valencia",
        imagen: "https://images.unsplash.com/photo-1542751371-adc38448a05e?w=400",
        descripcion: "Torneo de eSports"
    },
    {
        id: 4,
        titulo: "Startup Weekend",
        categoria: "emprendimiento",
        fecha: "2024-05-25",
        precio: 30,
        ubicacion: "Sevilla",
        imagen: "https://images.unsplash.com/photo-1515187029135-18ee286d815b?w=400",
        descripcion: "Fin de semana empresarial"
    },
    {
        id: 5,
        titulo: "Street Art Expo",
        categoria: "cultura",
        fecha: "2024-06-05",
        precio: 0,
        ubicacion: "Bilbao",
        imagen: "https://images.unsplash.com/photo-1561059488-916d69792237?w=400",
        descripcion: "Exposición de arte urbano"
    },
    {
        id: 6,
        titulo: "Beach Volleyball Cup",
        categoria: "deportes",
        fecha: "2024-06-12",
        precio: 20,
        ubicacion: "Málaga",
        imagen: "https://images.unsplash.com/photo-1612872087720-bb876e2e67d1?w=400",
        descripcion: "Torneo de vóley playa"
    },
    {
        id: 7,
        titulo: "AI & ML Workshop",
        categoria: "tecnologia",
        fecha: "2024-05-18",
        precio: 0,
        ubicacion: "Madrid",
        imagen: "https://images.unsplash.com/photo-1485827404703-89b55fcc595e?w=400",
        descripcion: "Taller de inteligencia artificial"
    },
    {
        id: 8,
        titulo: "Indie Rock Night",
        categoria: "musica",
        fecha: "2024-05-22",
        precio: 12,
        ubicacion: "Barcelona",
        imagen: "https://images.unsplash.com/photo-1493225457124-a3eb161ffa5f?w=400",
        descripcion: "Noche de rock independiente"
    },
    {
        id: 9,
        titulo: "Esports League Final",
        categoria: "gaming",
        fecha: "2024-06-01",
        precio: 10,
        ubicacion: "Madrid",
        imagen: "https://images.unsplash.com/photo-1538481199705-c710c4e965fc?w=400",
        descripcion: "Final de liga de eSports"
    },
    {
        id: 10,
        titulo: "Maratón Ciudad",
        categoria: "deportes",
        fecha: "2024-06-15",
        precio: 35,
        ubicacion: "Valencia",
        imagen: "https://images.unsplash.com/photo-1452626038306-9aae5e071dd3?w=400",
        descripción: "Carrera urbana"
    },
    {
        id: 11,
        titulo: "Design Thinking Day",
        categoria: "emprendimiento",
        fecha: "2024-05-30",
        precio: 0,
        ubicacion: "Barcelona",
        imagen: "https://images.unsplash.com/photo-1531403009284-440f080d1e12?w=400",
        descripcion: "Jornada de diseño"
    },
    {
        id: 12,
        titulo: "Teatro Experimental",
        categoria: "cultura",
        fecha: "2024-06-08",
        precio: 18,
        ubicacion: "Madrid",
        imagen: "https://images.unsplash.com/photo-1503095396549-807759245b35?w=400",
        descripcion: "Obra de teatro contemporáneo"
    }
];

// Ciudades para simulación de ubicación
const ciudadesEspania = [
    { nombre: "Madrid", lat: 40.4168, lon: -3.7038 },
    { nombre: "Barcelona", lat: 41.3851, lon: 2.1734 },
    { nombre: "Valencia", lat: 39.4699, lon: -0.3763 },
    { nombre: "Sevilla", lat: 37.3891, lon: -5.9845 },
    { nombre: "Bilbao", lat: 43.2630, lon: -2.9350 },
    { nombre: "Málaga", lat: 36.7213, lon: -4.4214 },
    { nombre: "Zaragoza", lat: 41.6488, lon: -0.8891 },
    { nombre: "Murcia", lat: 37.9838, lon: -1.1440 },
    { nombre: "Palma", lat: 39.5696, lon: 2.6502 },
    { nombre: "Las Palmas", lat: 28.1235, lon: -15.4363 }
];

// Estado de la aplicación
let userLocation = null;
let eventosFiltrados = [...eventosData];

// ========================================
// Inicialización
// ========================================
document.addEventListener('DOMContentLoaded', () => {
    initParticles();
    initNavbar();
    initStats();
    initSearch();
    initCategories();
    initEvents();
    initModal();
    initLocation();
});

// ========================================
// Partículas de fondo
// ========================================
function initParticles() {
    const particlesContainer = document.getElementById('particles');
    const particleCount = 50;

    for (let i = 0; i < particleCount; i++) {
        const particle = document.createElement('div');
        particle.className = 'particle';
        
        // Posición aleatoria
        particle.style.left = Math.random() * 100 + '%';
        particle.style.animationDelay = Math.random() * 15 + 's';
        particle.style.animationDuration = (10 + Math.random() * 10) + 's';
        
        // Tamaño aleatorio
        const size = 2 + Math.random() * 4;
        particle.style.width = size + 'px';
        particle.style.height = size + 'px';
        
        particlesContainer.appendChild(particle);
    }
}

// ========================================
// Navbar scroll effect
// ========================================
function initNavbar() {
    const navbar = document.querySelector('.navbar');
    const menuToggle = document.getElementById('menuToggle');
    const navLinks = document.querySelector('.nav-links');

    // Scroll effect
    window.addEventListener('scroll', () => {
        if (window.scrollY > 50) {
            navbar.classList.add('scrolled');
        } else {
            navbar.classList.remove('scrolled');
        }
    });

    // Mobile menu toggle
    menuToggle.addEventListener('click', () => {
        navLinks.classList.toggle('active');
    });

    // Close menu on link click
    navLinks.querySelectorAll('a').forEach(link => {
        link.addEventListener('click', () => {
            navLinks.classList.remove('active');
        });
    });
}

// ========================================
// Animación de estadísticas
// ========================================
function initStats() {
    const statNumbers = document.querySelectorAll('.stat-number');
    
    const observerOptions = {
        threshold: 0.5
    };

    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                animateStats(statNumbers);
                observer.unobserve(entry.target);
            }
        });
    }, observerOptions);

    const statsSection = document.querySelector('.stats');
    if (statsSection) {
        observer.observe(statsSection);
    }
}

function animateStats(stats) {
    stats.forEach(stat => {
        const target = parseInt(stat.dataset.target);
        const duration = 2000;
        const increment = target / (duration / 16);
        let current = 0;

        const updateCounter = () => {
            current += increment;
            if (current < target) {
                stat.textContent = formatNumber(Math.floor(current));
                requestAnimationFrame(updateCounter);
            } else {
                stat.textContent = formatNumber(target);
            }
        };

        updateCounter();
    });
}

function formatNumber(num) {
    if (num >= 1000) {
        return (num / 1000).toFixed(0) + 'k';
    }
    return num.toString();
}

// ========================================
// Buscador
// ========================================
function initSearch() {
    const searchInput = document.getElementById('searchInput');
    const categoryFilter = document.getElementById('categoryFilter');
    const searchBtn = document.getElementById('searchBtn');

    // Event listeners
    searchBtn.addEventListener('click', performSearch);
    searchInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter') performSearch();
    });
    categoryFilter.addEventListener('change', performSearch);
}

function performSearch() {
    const searchTerm = document.getElementById('searchInput').value.toLowerCase();
    const category = document.getElementById('categoryFilter').value;

    eventosFiltrados = eventosData.filter(evento => {
        const matchesSearch = evento.titulo.toLowerCase().includes(searchTerm) ||
                             evento.ubicacion.toLowerCase().includes(searchTerm);
        const matchesCategory = !category || evento.categoria === category;
        
        return matchesSearch && matchesCategory;
    });

    renderEvents(eventosFiltrados, 'recommendedEvents');
    
    // Scroll to results
    document.getElementById('recomendados').scrollIntoView({ 
        behavior: 'smooth' 
    });
}

// ========================================
// Categorías
// ========================================
function initCategories() {
    const categoryCards = document.querySelectorAll('.category-card');
    
    categoryCards.forEach(card => {
        card.addEventListener('click', () => {
            const category = card.dataset.category;
            
            // Set filter
            document.getElementById('categoryFilter').value = category;
            document.getElementById('searchInput').value = '';
            
            // Perform search
            performSearch();
            
            // Scroll to search section
            document.getElementById('buscador').scrollIntoView({ 
                behavior: 'smooth' 
            });
        });
    });
}

// ========================================
// Renderizado de eventos
// ========================================
function initEvents() {
    renderEvents(eventosData, 'recommendedEvents');
    renderNearbyEvents();
}

function renderEvents(eventos, containerId) {
    const container = document.getElementById(containerId);
    
    if (eventos.length === 0) {
        container.innerHTML = `
            <div class="no-location" style="grid-column: 1 / -1;">
                <i class="fas fa-search"></i>
                <p>No se encontraron eventos</p>
            </div>
        `;
        return;
    }

    container.innerHTML = eventos.map(evento => createEventCard(evento)).join('');
}

function createEventCard(evento) {
    const categoriaIcon = getCategoryIcon(evento.categoria);
    const precioFormateado = evento.precio === 0 ? 'Gratis' : `${evento.precio}€`;
    const precioClass = evento.precio === 0 ? 'free' : '';
    const fechaFormateada = formatDate(evento.fecha);

    return `
        <div class="event-card slide-up">
            <div class="event-image" style="background: linear-gradient(135deg, var(--primary-purple), var(--primary-pink)); display: flex; align-items: center; justify-content: center;">
                <i class="${categoriaIcon}" style="font-size: 3rem; color: white;"></i>
            </div>
            <div class="event-content">
                <span class="event-category">${getCategoryName(evento.categoria)}</span>
                <h3 class="event-title">${evento.titulo}</h3>
                <div class="event-meta">
                    <span><i class="fas fa-calendar"></i> ${fechaFormateada}</span>
                    <span><i class="fas fa-map-marker-alt"></i> ${evento.ubicacion}</span>
                </div>
                <div class="event-footer">
                    <span class="event-price ${precioClass}">${precioFormateado}</span>
                    <button class="btn-view">Ver más</button>
                </div>
            </div>
        </div>
    `;
}

function getCategoryIcon(categoria) {
    const icons = {
        musica: 'fas fa-music',
        tecnologia: 'fas fa-laptop-code',
        cultura: 'fas fa-theater-masks',
        deportes: 'fas fa-running',
        gaming: 'fas fa-gamepad',
        emprendimiento: 'fas fa-rocket'
    };
    return icons[categoria] || 'fas fa-calendar';
}

function getCategoryName(categoria) {
    const names = {
        musica: '🎵 Música',
        tecnologia: '💻 Tecnología',
        cultura: '🎭 Cultura',
        deportes: '⚽ Deportes',
        gaming: '🎮 Gaming',
        emprendimiento: '🚀 Emprendimiento'
    };
    return names[categoria] || categoria;
}

function formatDate(dateString) {
    const date = new Date(dateString);
    const options = { day: 'numeric', month: 'short', year: 'numeric' };
    return date.toLocaleDateString('es-ES', options);
}

// ========================================
// Eventos cercanos (simulación de ubicación)
// ========================================
function initLocation() {
    const locationBtn = document.getElementById('locationBtn');
    const enableLocationBtn = document.getElementById('enableLocation');

    if (locationBtn) {
        locationBtn.addEventListener('click', requestLocation);
    }
    
    if (enableLocationBtn) {
        enableLocationBtn.addEventListener('click', requestLocation);
    }
}

function requestLocation() {
    const locationStatus = document.getElementById('locationStatus');
    const locationBtn = document.getElementById('locationBtn');

    if (!navigator.geolocation) {
        showLocationStatus('Tu navegador no soporta geolocalización', 'error');
        // Simular ubicación
        simulateLocation();
        return;
    }

    locationBtn.classList.add('active');
    showLocationStatus('Obteniendo tu ubicación...', 'success');

    navigator.geolocation.getCurrentPosition(
        (position) => {
            userLocation = {
                lat: position.coords.latitude,
                lon: position.coords.longitude
            };
            showLocationStatus('¡Ubicación activada!', 'success');
            renderNearbyEvents();
        },
        (error) => {
            console.log('Error de geolocalización:', error.message);
            showLocationStatus('Usando ubicación simulada', 'success');
            simulateLocation();
        },
        { timeout: 10000, enableHighAccuracy: true }
    );
}

function simulateLocation() {
    // Simular una ciudad aleatoria de España
    const randomCity = ciudadesEspania[Math.floor(Math.random() * ciudadesEspania.length)];
    userLocation = { lat: randomCity.lat, lon: randomCity.lon, nombre: randomCity.nombre };
    
    showLocationStatus(`Ubicación simulada: ${randomCity.nombre}`, 'success');
    renderNearbyEvents();
}

function showLocationStatus(message, type) {
    const status = document.getElementById('locationStatus');
    status.textContent = message;
    status.className = `location-status ${type}`;
    
    setTimeout(() => {
        status.className = 'location-status';
    }, 5000);
}

function renderNearbyEvents() {
    const container = document.getElementById('nearbyEvents');
    const noLocation = document.getElementById('noLocation');

    if (!userLocation) {
        container.style.display = 'none';
        if (noLocation) noLocation.style.display = 'block';
        return;
    }

    container.style.display = 'grid';
    if (noLocation) noLocation.style.display = 'none';

    // Simular eventos cercanos (mezclar y tomar algunos)
    const eventosCercanos = [...eventosData]
        .sort(() => Math.random() - 0.5)
        .slice(0, 6);

    renderEvents(eventosCercanos, 'nearbyEvents');
}

// ========================================
// Modal para añadir eventos
// ========================================
function initModal() {
    const fabAdd = document.getElementById('fabAdd');
    const modal = document.getElementById('addEventModal');
    const closeModal = document.getElementById('closeModal');
    const addEventForm = document.getElementById('addEventForm');

    fabAdd.addEventListener('click', () => {
        modal.classList.add('active');
        document.body.style.overflow = 'hidden';
    });

    closeModal.addEventListener('click', closeModalFunc);

    modal.addEventListener('click', (e) => {
        if (e.target === modal) {
            closeModalFunc();
        }
    });

    addEventForm.addEventListener('submit', (e) => {
        e.preventDefault();
        addNewEvent();
    });

    // Close on escape
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && modal.classList.contains('active')) {
            closeModalFunc();
        }
    });
}

function closeModalFunc() {
    const modal = document.getElementById('addEventModal');
    modal.classList.remove('active');
    document.body.style.overflow = '';
}

function addNewEvent() {
    const titulo = document.getElementById('eventTitle').value;
    const categoria = document.getElementById('eventCategory').value;
    const fecha = document.getElementById('eventDate').value;
    const precio = parseFloat(document.getElementById('eventPrice').value) || 0;
    const ubicacion = document.getElementById('eventLocation').value;
    const imagen = document.getElementById('eventImage').value;

    const nuevoEvento = {
        id: eventosData.length + 1,
        titulo,
        categoria,
        fecha,
        precio,
        ubicacion,
        imagen: imagen || null,
        descripcion: 'Nuevo evento'
    };

    // Añadir al array de eventos
    eventosData.unshift(nuevoEvento);

    // Actualizar vistas
    renderEvents(eventosData, 'recommendedEvents');
    if (userLocation) {
        renderNearbyEvents();
    }

    // Cerrar modal y mostrar éxito
    closeModalFunc();
    showLocationStatus('¡Evento creado exitosamente!', 'success');

    // Reset form
    document.getElementById('addEventForm').reset();
}

// ========================================
// Utilidades
// ========================================

// Smooth scroll para enlaces internos
document.querySelectorAll('a[href^="#"]').forEach(anchor => {
    anchor.addEventListener('click', function(e) {
        e.preventDefault();
        const target = document.querySelector(this.getAttribute('href'));
        if (target) {
            target.scrollIntoView({
                behavior: 'smooth',
                block: 'start'
            });
        }
    });
});

// Intersection Observer para animaciones al hacer scroll
const observerOptionsScroll = {
    threshold: 0.1,
    rootMargin: '0px 0px -50px 0px'
};

const scrollObserver = new IntersectionObserver((entries) => {
    entries.forEach(entry => {
        if (entry.isIntersecting) {
            entry.target.classList.add('visible');
        }
    });
}, observerOptionsScroll);

document.querySelectorAll('.event-card, .category-card, .stat-item').forEach(el => {
    scrollObserver.observe(el);
});