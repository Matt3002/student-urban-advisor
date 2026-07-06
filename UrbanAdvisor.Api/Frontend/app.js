// URL base del nostro backend .NET (Sostituisci con la porta corretta se diversa)
const API_BASE_URL = 'http://localhost:5257/api';

// 1. Inizializzazione della mappa
// Coordinate di Bologna: 44.4949 Lat, 11.3426 Lon. Zoom iniziale: 14
const map = L.map('map').setView([44.4949, 11.3426], 14);

// 2. Aggiungiamo il livello base (le "piastrelle" della mappa da OpenStreetMap)
L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '© OpenStreetMap contributors'
}).addTo(map);

// Variabili per conservare i layer dei marcatori
let bibliotecheLayer = L.layerGroup().addTo(map); // Visibile di default

// 3. Funzione per caricare e disegnare le biblioteche
async function caricaBiblioteche() {
    try {
        const response = await fetch(`${API_BASE_URL}/biblioteche`);
        const biblioteche = await response.json();

        biblioteche.forEach(biblio => {
            // Se le coordinate sono valide (diverse da 0)
            if (biblio.lat !== 0 && biblio.lon !== 0) {
                const marker = L.marker([biblio.lat, biblio.lon]);
                
                // Popup interattivo
                marker.bindPopup(`
                    <b>${biblio.nome}</b><br>
                    Quartiere: ${biblio.quartiere}<br>
                    Posti Lettura: ${biblio.postazioni || 'N/D'}
                `);
                
                bibliotecheLayer.addLayer(marker);
            }
        });
    } catch (error) {
        console.error("Errore nel caricamento delle biblioteche:", error);
    }
}

// ==========================================
// 4. GESTIONE DEL CLICK E ALGORITMO CONTEXT-AWARE
// ==========================================

let markerUtente = null; // Variabile per tenere traccia del click dell'utente

// Icona personalizzata (rossa) per distinguere il punto cliccato dalle biblioteche
const iconaUtente = L.icon({
    iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-red.png',
    shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/0.7.7/images/marker-shadow.png',
    iconSize: [25, 41],
    iconAnchor: [12, 41],
    popupAnchor: [1, -34],
    shadowSize: [41, 41]
});

map.on('click', async function(e) {
    const lat = e.latlng.lat;
    const lon = e.latlng.lng;
    // Leggiamo l'ora inserita nell'interfaccia HTML
    const ora = document.getElementById('oraInput').value; 

    // Se esiste già un marker utente, lo spostiamo, altrimenti lo creiamo
    if (markerUtente) {
        markerUtente.setLatLng(e.latlng);
    } else {
        markerUtente = L.marker(e.latlng, { icon: iconaUtente }).addTo(map);
    }

    try {
        // Mostriamo un messaggio di caricamento mentre il backend pensa
        document.getElementById('scoreDisplay').innerText = "...";
        document.getElementById('scoreDettaglio').innerText = "Calcolo in corso...";

        // Chiamata API al nostro algoritmo spaziale
        const response = await fetch(`${API_BASE_URL}/accessibility/score?lat=${lat}&lon=${lon}&ora=${ora}`);
        const data = await response.json();

        // 5. Aggiornamento dell'Interfaccia (UI)
        const scoreElement = document.getElementById('scoreDisplay');
        scoreElement.innerText = `${data.punteggio}/100`;
        document.getElementById('scoreDettaglio').innerText = data.dettaglio;

        // Cambiamo il colore del testo in base al voto (Verde > 70, Giallo > 40, Rosso <= 40)
        if (data.punteggio >= 70) {
            scoreElement.className = 'display-4 text-success';
        } else if (data.punteggio >= 40) {
            scoreElement.className = 'display-4 text-warning';
        } else {
            scoreElement.className = 'display-4 text-danger';
        }

        // Aggiungiamo un popup al marker rosso
        markerUtente.bindPopup(`<b>Punteggio: ${data.punteggio}/100</b><br>Fascia: ${data.fascia}`).openPopup();

    } catch (error) {
        console.error("Errore di connessione API:", error);
        document.getElementById('scoreDettaglio').innerText = "Errore di connessione al server backend.";
    }
});

// Avvio automatico al caricamento della pagina
caricaBiblioteche();