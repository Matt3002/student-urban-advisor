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
// GESTIONE DEI LIVELLI (LAYERS) E DATI
// ==========================================

// Creiamo i gruppi logici per i vari layer
let residenzeLayer = L.layerGroup();
let areeVerdiLayer = L.layerGroup();
let fermateLayer = L.layerGroup();

// Funzione per caricare le Residenze Universitarie (Pin gialli)
async function caricaResidenze() {
    try {
        const response = await fetch(`${API_BASE_URL}/residenze`);
        const data = await response.json();
        
        // Icona personalizzata per le residenze
        const iconaResidenza = L.icon({
            iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-gold.png',
            shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/0.7.7/images/marker-shadow.png',
            iconSize: [25, 41], iconAnchor: [12, 41], popupAnchor: [1, -34], shadowSize: [41, 41]
        });

        data.forEach(item => {
            if (item.lat !== 0 && item.lon !== 0) {
                L.marker([item.lat, item.lon], { icon: iconaResidenza })
                    .bindPopup(`<b>${item.nome}</b><br>Posti Letto: ${item.posti || 'N/D'}`)
                    .addTo(residenzeLayer);
            }
        });
    } catch (error) { console.error("Errore Residenze:", error); }
}

// Funzione per caricare le Aree Verdi (Punti verdi)
async function caricaAreeVerdi() {
    try {
        const response = await fetch(`${API_BASE_URL}/areeverdi`);
        const data = await response.json();
        data.forEach(item => {
            if (item.lat !== 0 && item.lon !== 0) {
                // Usiamo un CircleMarker per non ingombrare troppo
                L.circleMarker([item.lat, item.lon], { color: 'green', radius: 5, fillOpacity: 0.7 })
                    .bindPopup(`<b>${item.nome || 'Area Verde'}</b><br>Tipo: ${item.tipo || 'N/D'}`)
                    .addTo(areeVerdiLayer);
            }
        });
    } catch (error) { console.error("Errore Aree Verdi:", error); }
}

// Funzione per caricare le Fermate (Punti arancioni)
async function caricaFermate() {
    try {
        const response = await fetch(`${API_BASE_URL}/fermate`);
        const data = await response.json();
        data.forEach(item => {
            if (item.lat !== 0 && item.lon !== 0) {
                L.circleMarker([item.lat, item.lon], { color: '#fd7e14', radius: 4, stroke: false, fillOpacity: 0.8 })
                    .bindPopup(`<b>Fermata: ${item.nome}</b><br>Linee: ${item.linea}`)
                    .addTo(fermateLayer);
            }
        });
    } catch (error) { console.error("Errore Fermate:", error); }
}

// ==========================================
// LOGICA DEGLI INTERRUTTORI (TOGGLES)
// ==========================================

// Mappa che collega il nome del data-layer nel HTML alla variabile JavaScript corrispondente
const layerMap = {
    'biblioteche': bibliotecheLayer,
    'residenze': residenzeLayer,
    'areeverdi': areeVerdiLayer,
    'fermate': fermateLayer
};

// Aggiungiamo un "ascoltatore" a tutti i toggle switch
document.querySelectorAll('.layer-toggle').forEach(toggle => {
    toggle.addEventListener('change', function(e) {
        const layerNome = this.getAttribute('data-layer');
        const layerOggetto = layerMap[layerNome];
        
        // Se è spuntato aggiungiamo il layer alla mappa, altrimenti lo rimuoviamo
        if (this.checked) {
            map.addLayer(layerOggetto);
        } else {
            map.removeLayer(layerOggetto);
        }
    });
});

// Avviamo i caricamenti in background all'apertura della pagina
caricaResidenze();
caricaAreeVerdi();
caricaFermate();

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