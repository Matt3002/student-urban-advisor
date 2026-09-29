// ============================================================================
// app.js - Student Urban Accessibility Advisor (front-end)
// Logica della dashboard: mappa Leaflet multilayer, ricerca PoI vicini,
// calcolo dello score, gestione profili, raccomandazioni, analisi spaziale,
// temporale, mobilita', privacy e clustering. Comunica con l'API via fetch su /api.
// Il giorno (0 = lunedi' ... 6 = domenica) e' scelto dal selettore globale.
// Privacy: la posizione viene perturbata qui nel browser (Laplace planare o
// gaussiana); in modalita' privacy al server arriva solo il punto perturbato.
// ============================================================================

const API_BASE_URL = '/api';

const map = L.map('map').setView([44.4949, 11.3426], 14);
L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '&copy; OpenStreetMap contributors', maxZoom: 19
}).addTo(map);

const layers = {
    biblioteche: L.layerGroup().addTo(map), fermate: L.layerGroup(),
    areeverdi: L.layerGroup(), residenze: L.layerGroup(), stazioni: L.layerGroup(),
    mense: L.layerGroup(), sedi: L.layerGroup(), salestudio: L.layerGroup(), piste: L.layerGroup()
};

const catConfig = {
    biblioteche: { color: '#e74c3c', icon: '📚', label: 'Biblioteca' },
    fermate:     { color: '#3498db', icon: '🚌', label: 'Fermata Bus' },
    areeverdi:   { color: '#27ae60', icon: '🌳', label: 'Area Verde' },
    residenze:   { color: '#9b59b6', icon: '🏠', label: 'Residenza' },
    stazioni:    { color: '#f39c12', icon: '🚉', label: 'Stazione' },
    mense:       { color: '#ff5e5e', icon: '🍽️', label: 'Mensa/Ristoro' },
    sedi:        { color: '#16a085', icon: '🏛️', label: 'Sede Universitaria' },
    salestudio:  { color: '#c0392b', icon: '📖', label: 'Sala Studio' },
    piste:       { color: '#8e44ad', icon: '🚲', label: 'Pista Ciclabile' }
};

let userMarker = null, searchCircle = null, lastClickLat = null, lastClickLon = null;
let heatLayer = null, gridLayer = null, recMarkers = null;
let profiliCache = [];
let privacyMarker = null;
let clusterLayer = null;
let isocronaLayer = null;
let privacyPert = null, privacyLine = null, destinazione = null, destMarker = null;
let moranLayer = null, tradeoffChart = null;
const coloriFasce = { '0-10': '#1a9850', '10-20': '#91cf60', '20-30': '#fee08b', '30-45': '#fc8d59', '>45': '#d73027' };
const clusterColors = ['#2ecc71', '#3498db', '#e67e22', '#e74c3c', '#9b59b6', '#1abc9c'];

// Carica e disegna sulla mappa i PoI di una categoria (le piste ciclabili come polilinee).
async function loadLayer(name) {
    try {
        const res = await fetch(`${API_BASE_URL}/${name}`);
        const data = await res.json();
        const cfg = catConfig[name];
        layers[name].clearLayers();
        if (name === 'piste') {
            data.forEach(p => {
                if (!p.linee || p.linee.length === 0) return;
                L.polyline(p.linee, { color: cfg.color, weight: 3, opacity: 0.8 })
                    .bindPopup(`<b>${cfg.icon} ${cfg.label} ${p.codice || ''}</b><br><small>${p.utilizzo || ''}</small>${p.lunghezza ? `<br><small>Lunghezza: ${Math.round(p.lunghezza)} m</small>` : ''}`)
                    .addTo(layers[name]);
            });
            return;
        }
        data.forEach(item => {
            if (!item.lat || !item.lon) return;
            const marker = L.circleMarker([item.lat, item.lon], {
                radius: name === 'fermate' ? 3 : 6,
                fillColor: cfg.color, color: '#fff', weight: 1, opacity: 0.9, fillOpacity: 0.7
            });
            let popup = `<b>${cfg.icon} ${item.nome || 'N/D'}</b><br><small>${cfg.label}</small>`;
            if (item.indirizzo) popup += `<br><small>${item.indirizzo}</small>`;
            if (item.linea) popup += `<br><small>Linea: ${item.linea}</small>`;
            if (item.tipo) popup += `<br><small>Tipo: ${item.tipo}</small>`;
            if (item.posti) popup += `<br><small>Posti: ${item.posti}</small>`;
            marker.bindPopup(popup);
            layers[name].addLayer(marker);
        });
    } catch (e) { console.error(`Errore caricamento ${name}:`, e); }
}

// Attiva/disattiva un layer di categoria sulla mappa.
function toggleLayer(name) {
    const chk = document.getElementById(`chk-${name}`);
    if (chk.checked) { loadLayer(name); map.addLayer(layers[name]); }
    else { map.removeLayer(layers[name]); }
}

// Ridisegna la heatmap quando cambia la categoria selezionata.
function refreshHeatmap() {
    if (document.getElementById('chk-heatmap').checked) toggleHeatmap();
}

// Attiva/disattiva la heatmap di densita' dei servizi, eventualmente filtrata per categoria.
async function toggleHeatmap() {
    const chk = document.getElementById('chk-heatmap');
    if (heatLayer) { map.removeLayer(heatLayer); heatLayer = null; }
    if (!chk.checked) return;
    const cat = document.getElementById('heatmapCategoria').value;
    try {
        const res = await fetch(`${API_BASE_URL}/heatmap${cat ? `?categoria=${cat}` : ''}`);
        const data = await res.json();
        const pts = data.punti.map(p => [p.lat, p.lon, cat ? 1.0 : p.peso]);
        heatLayer = L.heatLayer(pts, { radius: 25, blur: 20, maxZoom: 17, max: 1.0,
            gradient: { 0.2: '#ffffb2', 0.4: '#fecc5c', 0.6: '#fd8d3c', 0.8: '#f03b20', 1: '#bd0026' }
        }).addTo(map);
    } catch (e) { console.error('Errore heatmap:', e); }
}

// Gestore del click sulla mappa: imposta il punto corrente (reale, resta nel browser) e aggiorna la scheda attiva.
map.on('click', async (e) => {
    lastClickLat = e.latlng.lat; lastClickLon = e.latlng.lng;
    if (userMarker) map.removeLayer(userMarker);
    userMarker = L.marker([lastClickLat, lastClickLon]).addTo(map).bindPopup('📍 Punto selezionato (posizione reale)').openPopup();
    aggiornaPerturbazione();

    if (!document.getElementById('tab-score').classList.contains('d-none')) await calcolaScore();
    if (!document.getElementById('tab-profilo').classList.contains('d-none')) await confrontaProfili();
    await cercaVicini();
    await caricaIndicatoriArea();
    if (!document.getElementById('tab-mobilita').classList.contains('d-none')) await caricaTempoPercorrenza();
});

// Giorno selezionato nel selettore globale (0 = lunedì ... 6 = domenica).
function giorno() {
    return document.getElementById('giornoGlobale').value;
}

// Aggiorna le schede che dipendono dal giorno quando cambia il selettore.
function onCambioGiorno() {
    if (!document.getElementById('tab-temporale').classList.contains('d-none')) loadTemporale();
    if (lastClickLat && !document.getElementById('tab-score').classList.contains('d-none')) calcolaScore();
}

// Coordinate da inviare al server: quelle perturbate se la modalità privacy è attiva, altrimenti quelle reali.
function coordInvio() {
    return privacyPert ? privacyPert : { lat: lastClickLat, lon: lastClickLon };
}

// Richiede lo score del punto selezionato e aggiorna il pannello.
async function calcolaScore() {
    if (!lastClickLat) return;
    const ora = document.getElementById('oraInput').value;
    const profiloId = document.getElementById('profiloSelect').value;
    const c = coordInvio();
    let url = `${API_BASE_URL}/ranking?lat=${c.lat}&lon=${c.lon}&ora=${ora}&giorno=${giorno()}`;
    if (profiloId) url += `&profiloId=${profiloId}`;
    try {
        const res = await fetch(url); const data = await res.json();
        const scoreEl = document.getElementById('scoreValue');
        scoreEl.textContent = data.punteggio;
        scoreEl.className = 'score-badge ' + (data.punteggio >= 70 ? 'score-high' : data.punteggio >= 40 ? 'score-mid' : 'score-low');
        document.getElementById('scoreFascia').innerHTML = `<span class="badge ${data.fascia === 'Diurna' ? 'bg-warning text-dark' : 'bg-dark'}">${data.fascia === 'Diurna' ? '🌞' : '🌙'} ${data.fascia}</span>`;
        document.getElementById('scoreProfilo').innerHTML = `Profilo: ${data.profilo} · giorno ${data.tipo_giorno}<br>🚌 ${data.contesto.corse_ora} corse/ora nel raggio · 📚 ${data.contesto.biblioteche_aperte}/${data.contesto.biblioteche} biblioteche aperte${privacyPert ? '<br><span class="text-warning">🔒 calcolato sulla posizione perturbata</span>' : ''}`;
        const container = document.getElementById('subscoresContainer');
        container.innerHTML = '';
        const items = [
            { name: '🚌 Trasporti', val: data.subscores.trasporti },
            { name: '📚 Bibl./Sale st.', val: data.subscores.biblioteche },
            { name: '🌳 Aree Verdi', val: data.subscores.aree_verdi },
            { name: '🚲 Mobilità', val: data.subscores.mobilita },
            { name: '🏠 Residenze', val: data.subscores.residenze },
            { name: '🍽️ Mense/Ristoro', val: data.subscores.mense },
            { name: '🏛️ Sedi Univ.', val: data.subscores.sedi }
        ];
        items.forEach(it => {
            const color = it.val >= 70 ? '#198754' : it.val >= 40 ? '#ffc107' : '#dc3545';
            container.innerHTML += `<div class="d-flex align-items-center gap-2 mb-1"><small style="min-width:100px">${it.name}</small><div class="subscore-bar flex-fill"><div class="subscore-fill" style="width:${it.val}%;background:${color}"></div></div><small class="fw-bold" style="min-width:25px">${it.val}</small></div>`;
        });
        document.getElementById('scoreMotivazione').textContent = data.dettaglio;
        document.getElementById('scoreResult').classList.remove('d-none');
    } catch (e) { console.error('Errore score:', e); }
}

// Cerca i servizi vicini al punto selezionato entro il raggio scelto.
async function cercaVicini() {
    if (!lastClickLat) return;
    const raggio = document.getElementById('raggioCerca').value;
    const cat = document.getElementById('categoriaCerca').value;
    const c = coordInvio();
    let url = `${API_BASE_URL}/nearby?lat=${c.lat}&lon=${c.lon}&raggio=${raggio}`;
    if (cat) url += `&categoria=${cat}`;
    if (searchCircle) map.removeLayer(searchCircle);
    searchCircle = L.circle([c.lat, c.lon], { radius: parseInt(raggio), color: '#0d6efd', fillOpacity: 0.05, weight: 1.5 }).addTo(map);
    try {
        const res = await fetch(url); const data = await res.json();
        document.getElementById('nearbyCount').textContent = data.totale;
        const list = document.getElementById('nearbyList');
        if (data.risultati.length === 0) { list.innerHTML = '<p class="text-muted small">Nessun servizio trovato.</p>'; }
        else {
            list.innerHTML = data.risultati.map(r => {
                const cfg = catConfig[r.categoria] || { icon: '📍', label: r.categoria };
                return `<div class="d-flex justify-content-between align-items-center py-1 border-bottom"><div><small>${cfg.icon} <b>${r.nome}</b></small><br><small class="text-muted">${cfg.label} — ${r.dettaglio}</small></div><span class="badge bg-light text-dark">${r.distanzaMetri}m</span></div>`;
            }).join('');
        }
        document.getElementById('nearbyResults').classList.remove('d-none');
    } catch (e) { console.error('Errore nearby:', e); }
}

// Buffer analysis: indicatori aggregati e densita' nell'area selezionata.
async function caricaIndicatoriArea() {
    if (!lastClickLat) return;
    const raggio = document.getElementById('raggioCerca').value;
    const ora = document.getElementById('oraInput').value;
    const c = coordInvio();
    try {
        const res = await fetch(`${API_BASE_URL}/area/indicatori?lat=${c.lat}&lon=${c.lon}&raggio=${raggio}&ora=${ora}&giorno=${giorno()}`);
        const data = await res.json();
        document.getElementById('indTotalePoi').textContent = data.totale_poi;
        document.getElementById('indDensita').textContent = data.densita.servizi_per_km2;
        const d = data.dettaglio;
        document.getElementById('indDettaglio').innerHTML = `
            <div class="d-flex justify-content-between border-bottom py-1"><span>📚 Biblioteche (aperte ora)</span><b>${d.biblioteche} (${d.biblioteche_aperte})</b></div>
            <div class="d-flex justify-content-between border-bottom py-1"><span>📖 Sale Studio</span><b>${d.sale_studio}</b></div>
            <div class="d-flex justify-content-between border-bottom py-1"><span>🚌 Fermate Bus</span><b>${d.fermate_bus}</b></div>
            <div class="d-flex justify-content-between border-bottom py-1"><span>🕒 Corse/ora (fermata più servita)</span><b>${data.corse_ora}</b></div>
            <div class="d-flex justify-content-between border-bottom py-1"><span>🌳 Aree Verdi</span><b>${d.aree_verdi}</b></div>
            <div class="d-flex justify-content-between border-bottom py-1"><span>🚲 Piste Ciclabili</span><b>${d.piste_km} km</b></div>
            <div class="d-flex justify-content-between border-bottom py-1"><span>🏠 Residenze</span><b>${d.residenze}</b></div>
            <div class="d-flex justify-content-between border-bottom py-1"><span>🚉 Stazioni</span><b>${d.stazioni}</b></div>
            <div class="d-flex justify-content-between border-bottom py-1"><span>🍽️ Mense/Ristoro</span><b>${d.mense}</b></div>
            <div class="d-flex justify-content-between py-1"><span>🏛️ Sedi Univ.</span><b>${d.sedi}</b></div>`;
        document.getElementById('areaIndicatori').classList.remove('d-none');
    } catch (e) { console.error('Errore indicatori area:', e); }
}

// Gestisce il cambio di scheda nella sidebar.
function switchTab(name, btn) {
    document.querySelectorAll('.tab-panel').forEach(p => p.classList.add('d-none'));
    document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
    document.getElementById(`tab-${name}`).classList.remove('d-none');
    if (btn) btn.classList.add('active');
    if (name === 'storico') loadStorico();
    if (name === 'stats') loadStatistiche();
    if (name === 'profilo') loadProfili();
    if (name === 'raccomandazioni') loadProfili();
    if (name === 'analisi') loadProfili();
    if (name === 'temporale') { loadTemporale(); loadTemporaleStats(); }
    if (name === 'mobilita' && lastClickLat) caricaTempoPercorrenza();
}

// Carica i profili utente e popola i menu a tendina.
async function loadProfili() {
    try {
        const res = await fetch(`${API_BASE_URL}/profili`);
        profiliCache = await res.json();
        const selects = ['profiloSelect','profiloEditSelect','storicoProfiloFilter','confronto1','confronto2','recProfiloSelect','gridProfiloSelect'];
        selects.forEach(selId => {
            const sel = document.getElementById(selId);
            if (!sel) return;
            const currentVal = sel.value;
            const firstOpt = sel.querySelector('option');
            sel.innerHTML = '';
            if (firstOpt) sel.appendChild(firstOpt);
            profiliCache.forEach(p => { const opt = document.createElement('option'); opt.value = p.id; opt.textContent = p.nome; sel.appendChild(opt); });
            if (currentVal && sel.querySelector(`option[value="${currentVal}"]`)) sel.value = currentVal;
        });
    } catch (e) { console.error('Errore profili:', e); }
}

// Popola il form di modifica con i pesi del profilo selezionato.
function loadProfiloEdit() {
    const id = document.getElementById('profiloEditSelect').value;
    const btn = document.getElementById('btnDeleteProfilo');
    if (id === 'new') {
        document.getElementById('profiloNome').value = '';
        ['trasporti','biblioteche','areeverdi','mobilita','residenze','mense','sedi'].forEach(k => { document.getElementById(`w-${k}`).value = 50; document.getElementById(`w-${k}`).nextElementSibling.textContent = '50'; });
        btn.style.display = 'none'; return;
    }
    const p = profiliCache.find(x => x.id == id);
    if (!p) return;
    document.getElementById('profiloNome').value = p.nome;
    document.getElementById('w-trasporti').value = p.pesoTrasporti; document.getElementById('w-trasporti').nextElementSibling.textContent = p.pesoTrasporti;
    document.getElementById('w-biblioteche').value = p.pesoBiblioteche; document.getElementById('w-biblioteche').nextElementSibling.textContent = p.pesoBiblioteche;
    document.getElementById('w-areeverdi').value = p.pesoAreeVerdi; document.getElementById('w-areeverdi').nextElementSibling.textContent = p.pesoAreeVerdi;
    document.getElementById('w-mobilita').value = p.pesoMobilitaSostenibile; document.getElementById('w-mobilita').nextElementSibling.textContent = p.pesoMobilitaSostenibile;
    document.getElementById('w-residenze').value = p.pesoResidenze; document.getElementById('w-residenze').nextElementSibling.textContent = p.pesoResidenze;
    document.getElementById('w-mense').value = p.pesoMense; document.getElementById('w-mense').nextElementSibling.textContent = p.pesoMense;
    document.getElementById('w-sedi').value = p.pesoSedi; document.getElementById('w-sedi').nextElementSibling.textContent = p.pesoSedi;
    btn.style.display = 'block';
}

// Crea o aggiorna un profilo utente.
async function salvaProfilo() {
    const selId = document.getElementById('profiloEditSelect').value;
    const body = { nome: document.getElementById('profiloNome').value || 'Nuovo Profilo',
        pesoTrasporti: parseInt(document.getElementById('w-trasporti').value),
        pesoBiblioteche: parseInt(document.getElementById('w-biblioteche').value),
        pesoAreeVerdi: parseInt(document.getElementById('w-areeverdi').value),
        pesoMobilitaSostenibile: parseInt(document.getElementById('w-mobilita').value),
        pesoResidenze: parseInt(document.getElementById('w-residenze').value),
        pesoMense: parseInt(document.getElementById('w-mense').value),
        pesoSedi: parseInt(document.getElementById('w-sedi').value)
        }
    try {
        const res = selId === 'new'
            ? await fetch(`${API_BASE_URL}/profili`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
            : await fetch(`${API_BASE_URL}/profili/${selId}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (res.ok) { document.getElementById('profiloMsg').innerHTML = '<span class="text-success">✅ Profilo salvato!</span>'; await loadProfili(); setTimeout(() => document.getElementById('profiloMsg').innerHTML = '', 2000); }
    } catch (e) { document.getElementById('profiloMsg').innerHTML = '<span class="text-danger">❌ Errore</span>'; }
}

// Elimina il profilo selezionato.
async function eliminaProfilo() {
    const id = document.getElementById('profiloEditSelect').value;
    if (id === 'new' || !confirm('Eliminare questo profilo?')) return;
    try { await fetch(`${API_BASE_URL}/profili/${id}`, { method: 'DELETE' }); document.getElementById('profiloEditSelect').value = 'new'; loadProfiloEdit(); await loadProfili(); } catch (e) { console.error(e); }
}

// Confronta due profili sullo stesso punto e mostra i risultati.
async function confrontaProfili() {
    if (!lastClickLat) return;
    const id1 = document.getElementById('confronto1').value, id2 = document.getElementById('confronto2').value;
    if (!id1 || !id2 || id1 === id2) return;
    const ora = document.getElementById('oraInput').value;
    const c = coordInvio();
    try {
        const [r1, r2] = await Promise.all([
            fetch(`${API_BASE_URL}/ranking?lat=${c.lat}&lon=${c.lon}&ora=${ora}&giorno=${giorno()}&profiloId=${id1}`).then(r=>r.json()),
            fetch(`${API_BASE_URL}/ranking?lat=${c.lat}&lon=${c.lon}&ora=${ora}&giorno=${giorno()}&profiloId=${id2}`).then(r=>r.json())
        ]);
        const container = document.getElementById('confrontoResult');
        container.classList.remove('d-none');
        container.innerHTML = `<div class="row g-2 mt-1"><div class="col-6 text-center"><div class="fw-bold">${r1.profilo}</div><div class="score-badge ${r1.punteggio>=70?'score-high':r1.punteggio>=40?'score-mid':'score-low'}">${r1.punteggio}</div></div><div class="col-6 text-center"><div class="fw-bold">${r2.profilo}</div><div class="score-badge ${r2.punteggio>=70?'score-high':r2.punteggio>=40?'score-mid':'score-low'}">${r2.punteggio}</div></div></div>
            <table class="table table-sm table-bordered mt-2 mb-0" style="font-size:0.75rem"><tr><th></th><th>${r1.profilo}</th><th>${r2.profilo}</th></tr>
            <tr><td>🚌 Trasporti</td><td>${r1.subscores.trasporti}</td><td>${r2.subscores.trasporti}</td></tr>
            <tr><td>📚 Biblioteche</td><td>${r1.subscores.biblioteche}</td><td>${r2.subscores.biblioteche}</td></tr>
            <tr><td>🌳 Aree Verdi</td><td>${r1.subscores.aree_verdi}</td><td>${r2.subscores.aree_verdi}</td></tr>
            <tr><td>🚲 Mobilità</td><td>${r1.subscores.mobilita}</td><td>${r2.subscores.mobilita}</td></tr>
            <tr><td>🏠 Residenze</td><td>${r1.subscores.residenze}</td><td>${r2.subscores.residenze}</td></tr>
            <tr><td>🍽️ Mense</td><td>${r1.subscores.mense}</td><td>${r2.subscores.mense}</td></tr>
            <tr><td>🏛️ Sedi</td><td>${r1.subscores.sedi}</td><td>${r2.subscores.sedi}</td></tr></table>`;
    } catch (e) { console.error('Errore confronto:', e); }
}

// Richiede le zone raccomandate e le mostra su mappa e lista.
async function loadRaccomandazioni() {
    const profiloId = document.getElementById('recProfiloSelect').value;
    const ora = document.getElementById('recOra').value;
    let url = `${API_BASE_URL}/raccomandazioni?ora=${ora}&giorno=${giorno()}&top=5`;
    if (profiloId) url += `&profiloId=${profiloId}`;

    document.getElementById('recList').innerHTML = '<p class="text-muted">Calcolo in corso...</p>';
    try {
        const res = await fetch(url); const data = await res.json();
        if (recMarkers) map.removeLayer(recMarkers);
        recMarkers = L.layerGroup().addTo(map);

        let html = `<div class="small text-muted mb-2">Profilo: ${data.profilo} | ${data.giorno_nome} | ${data.fascia === 'Diurna' ? '🌞' : '🌙'} ${data.fascia}</div>`;
        data.raccomandazioni.forEach(r => {
            const scoreClass = r.student_accessibility_score >= 70 ? 'score-high' : r.student_accessibility_score >= 40 ? 'score-mid' : 'score-low';
            html += `<div class="rec-card"><div class="d-flex align-items-center gap-3">
                <div class="rec-rank">${r.posizione}</div>
                <div class="flex-fill"><div class="fw-bold ${scoreClass}">Score: ${r.student_accessibility_score}/100</div>
                <small class="text-muted">${r.motivazione}</small><br>
                <small>📚${r.dettaglio.biblioteche} 📖${r.dettaglio.sale_studio} 🚌${r.dettaglio.fermate} 🌳${r.dettaglio.aree_verdi} 🚲${r.dettaglio.piste_km}km 🏠${r.dettaglio.residenze} 🍽️${r.dettaglio.mense} 🏛️${r.dettaglio.sedi}</small>
                </div></div></div>`;

            const icon = L.divIcon({ html: `<div style="background:#0d6efd;color:#fff;width:28px;height:28px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-weight:700;border:2px solid #fff;box-shadow:0 2px 6px rgba(0,0,0,.3)">${r.posizione}</div>`, className: '', iconSize: [28, 28] });
            L.marker([r.lat, r.lon], { icon }).addTo(recMarkers)
                .bindPopup(`<b>#${r.posizione} — Score ${r.student_accessibility_score}</b><br>${r.motivazione}`);
        });
        html += '';
        document.getElementById('recList').innerHTML = html;
    } catch (e) { console.error(e); document.getElementById('recList').innerHTML = '<p class="text-danger">Errore</p>'; }
}

// Richiede e disegna la griglia di densita' sulla mappa.
async function loadDensityGrid() {
    const profiloId = document.getElementById('gridProfiloSelect').value;
    const ora = document.getElementById('gridOra').value;
    let url = `${API_BASE_URL}/density/grid?celle=8&ora=${ora}&giorno=${giorno()}`;
    if (profiloId) url += `&profiloId=${profiloId}`;

    try {
        const res = await fetch(url); const data = await res.json();
        if (gridLayer) map.removeLayer(gridLayer);
        gridLayer = L.layerGroup().addTo(map);

        const scores = data.griglia.map(c => c.score);
        const maxScore = Math.max(...scores, 1);

        data.griglia.forEach(cell => {
            const intensity = cell.score / maxScore;
            const color = `rgba(${Math.round(220 * intensity)}, ${Math.round(50 * (1 - intensity))}, ${Math.round(50 * (1 - intensity))}, ${0.15 + intensity * 0.45})`;
            const stepLat = 0.05 / data.celle, stepLon = 0.08 / data.celle;
            const bounds = [[cell.lat - stepLat/2, cell.lon - stepLon/2], [cell.lat + stepLat/2, cell.lon + stepLon/2]];
            const rect = L.rectangle(bounds, { color: '#333', weight: 0.5, fillColor: color, fillOpacity: 0.6 });
            rect.bindPopup(`<b>Score: ${cell.score}/100</b><br>📚${cell.dettaglio.biblioteche} 📖${cell.dettaglio.sale_studio} 🚌${cell.dettaglio.fermate} 🌳${cell.dettaglio.aree_verdi} 🚲${cell.dettaglio.piste_km}km 🏠${cell.dettaglio.residenze} 🍽️${cell.dettaglio.mense} 🏛️${cell.dettaglio.sedi}<br>Totale PoI: ${cell.totale_poi}`);
            gridLayer.addLayer(rect);
        });

        document.getElementById('gridLegend').classList.remove('d-none');

        const avgScore = Math.round(scores.reduce((a, b) => a + b, 0) / scores.length);
        const best = data.griglia.reduce((a, b) => a.score > b.score ? a : b);
        document.getElementById('gridStats').innerHTML = `
            <div class="fw-bold">Riepilogo Griglia ${data.celle}x${data.celle}</div>
            <div>Score medio: <b>${avgScore}/100</b></div>
            <div>Zona migliore: <b>${best.score}/100</b> (${best.totale_poi} PoI)</div>
            <div>Fascia: ${data.fascia} | Profilo: ${data.profilo}</div>`;
    } catch (e) { console.error(e); }
}

// Imposta come destinazione la sede universitaria (non museo) più vicina al punto selezionato.
async function impostaDestinazione() {
    if (!lastClickLat) { document.getElementById('mobDestinazione').textContent = 'Clicca prima un punto sulla mappa.'; return; }
    const ora = document.getElementById('mobOra').value;
    try {
        const res = await fetch(`${API_BASE_URL}/mobility/tempo-percorrenza?lat=${lastClickLat}&lon=${lastClickLon}&ora=${ora}&giorno=${giorno()}`);
        const data = await res.json();
        if (!data.disponibile) { document.getElementById('mobDestinazione').textContent = data.motivo; return; }
        destinazione = { lat: data.destinazione.lat, lon: data.destinazione.lon, nome: (data.sede_destinazione || '').split(';')[0].trim() };
        if (destMarker) map.removeLayer(destMarker);
        const icon = L.divIcon({ html: '<div style="font-size:22px">🎯</div>', className: '', iconSize: [24, 24], iconAnchor: [12, 12] });
        destMarker = L.marker([destinazione.lat, destinazione.lon], { icon }).addTo(map).bindPopup(`🎯 ${destinazione.nome}`);
        document.getElementById('mobDestinazione').innerHTML = `<b>${destinazione.nome}</b>`;
    } catch (e) { console.error('Errore destinazione:', e); }
}

// Calcola e disegna le isocrone (fasce di tempo verso la destinazione) sulla griglia.
async function loadIsocrona() {
    if (!destinazione) { document.getElementById('mobRaggiungibili').innerHTML = '<p class="text-warning small">Imposta prima una destinazione.</p>'; return; }
    const modalita = document.getElementById('mobModalita').value;
    const ora = document.getElementById('mobOra').value;
    const n = 12;

    document.getElementById('mobRaggiungibili').innerHTML = `<p class="text-muted small">Calcolo in corso (${n * n} celle)...</p>`;
    try {
        const res = await fetch(`${API_BASE_URL}/mobility/isocrona?destLat=${destinazione.lat}&destLon=${destinazione.lon}&ora=${ora}&giorno=${giorno()}&modalita=${modalita}&celle=${n}`);
        const data = await res.json();
        if (!data.disponibile) { document.getElementById('mobRaggiungibili').innerHTML = `<p class="text-muted small">${data.motivo}</p>`; return; }

        if (isocronaLayer) map.removeLayer(isocronaLayer);
        isocronaLayer = L.layerGroup().addTo(map);
        const stepLat = 0.05 / data.celle, stepLon = 0.08 / data.celle;

        data.griglia.forEach(cell => {
            const bounds = [[cell.lat - stepLat/2, cell.lon - stepLon/2], [cell.lat + stepLat/2, cell.lon + stepLon/2]];
            L.rectangle(bounds, { color: '#333', weight: 0.3, fillColor: coloriFasce[cell.fascia], fillOpacity: 0.55 })
                .bindPopup(`<b>${cell.tempo_minuti} min</b> (${cell.fascia} min)<br><small>${cell.mezzo}</small>`)
                .addTo(isocronaLayer);
        });

        const legend = document.getElementById('isocronaLegend');
        legend.innerHTML = `<div class="fw-bold mb-1">Verso ${destinazione.nome} — ${data.tipo_giorno}, ${data.ora}:00</div>` +
            data.fasce.map(f => `<span class="me-2"><span style="display:inline-block;width:12px;height:12px;background:${coloriFasce[f]};border-radius:2px"></span> ${f} min</span>`).join('');
        legend.classList.remove('d-none');

        let html = `<div class="fw-bold small mb-2">🏆 Top 5 aree più raggiungibili</div>`;
        data.aree_piu_raggiungibili.forEach(r => {
            html += `<div class="d-flex justify-content-between align-items-center border-bottom py-1 small">
                <span>#${r.posizione} <span class="text-muted">${r.mezzo}</span></span><span class="fw-bold text-success">${r.tempo_minuti} min</span></div>`;
        });
        document.getElementById('mobRaggiungibili').innerHTML = html;
    } catch (e) {
        console.error('Errore isocrona:', e);
        document.getElementById('mobRaggiungibili').innerHTML = '<p class="text-danger small">Errore</p>';
    }
}


// Rimuove il layer isocrone dalla mappa.
function clearIsocrona() {
    if (isocronaLayer) { map.removeLayer(isocronaLayer); isocronaLayer = null; }
    document.getElementById('isocronaLegend').classList.add('d-none');
}

// Mostra il dettaglio multimodale (piedi/bici/TPL) dal punto cliccato verso la destinazione o la sede più vicina.
async function caricaTempoPercorrenza() {
    if (!lastClickLat) return;
    const ora = document.getElementById('mobOra').value;
    const c = coordInvio();
    let url = `${API_BASE_URL}/mobility/tempo-percorrenza?lat=${c.lat}&lon=${c.lon}&ora=${ora}&giorno=${giorno()}`;
    if (destinazione) url += `&destLat=${destinazione.lat}&destLon=${destinazione.lon}`;
    try {
        const res = await fetch(url);
        const data = await res.json();
        if (!data.disponibile) {
            document.getElementById('mobPuntoResult').innerHTML = `<p class="text-muted">${data.motivo}</p>`;
            return;
        }
        const p = data.percorso;
        const sedeBreve = (data.sede_destinazione || '').split(';')[0].trim();
        let html = `<div class="small text-muted mb-2">Verso: <b>${sedeBreve}</b> (${p.distanza_diretta_metri} m in linea d'aria, ${data.tipo_giorno} ore ${data.ora}:00)</div>`;
        html += `<div class="d-flex justify-content-between border-bottom py-1"><span>🚶 A piedi</span><b>${p.piedi.tempo_minuti} min</b></div>`;
        html += `<div class="d-flex justify-content-between border-bottom py-1"><span>🚲 Bici</span><b>${p.bici.tempo_minuti} min</b></div>`;
        const tp = p.trasporto_pubblico;
        if (tp.disponibile) {
            html += `<div class="d-flex justify-content-between py-1"><span>🚌 Trasporto Pubblico</span><b>${tp.tempo_totale_minuti} min</b></div>`;
            html += `<div class="text-muted mt-1" style="font-size:0.7rem">
                Linea ${tp.dettaglio.linee}: ${tp.dettaglio.fermata_partenza} → ${tp.dettaglio.fermata_arrivo}<br>
                piedi ${tp.dettaglio.a_piedi_fino_fermata_min} + attesa ${tp.dettaglio.attesa_media_min} (${tp.dettaglio.corse_ora} corse/ora)
                + bordo ${tp.dettaglio.a_bordo_min} + piedi ${tp.dettaglio.a_piedi_da_fermata_min} min
                ${tp.piu_veloce_a_piedi ? '<br><b>A piedi si fa prima.</b>' : ''}</div>`;
        } else {
            html += `<div class="text-muted small py-1">🚌 ${tp.motivo}</div>`;
        }
        document.getElementById('mobPuntoResult').innerHTML = html;
    } catch (e) { console.error('Errore tempo percorrenza:', e); }
}

// Rimuove la griglia di densita' dalla mappa.
function clearGrid() {
    if (gridLayer) { map.removeLayer(gridLayer); gridLayer = null; }
    document.getElementById('gridLegend').classList.add('d-none');
    document.getElementById('gridStats').innerHTML = '';
}

// Richiede la disponibilita' dei servizi per giorno e ora.
async function loadTemporale() {
    const ora = document.getElementById('tempOra').value;
    try {
        const res = await fetch(`${API_BASE_URL}/temporale/disponibilita?giorno=${giorno()}&ora=${ora}`);
        const data = await res.json();
        let html = `<div class="fw-bold mb-2">${data.giorno_nome} (${data.tipo_giorno}), ore ${data.ora}:00</div>`;

        data.servizi.forEach(s => {
            const pct = s.totale_servizi > 0 ? Math.round(s.aperti_ora / s.totale_servizi * 100) : 0;
            const barColor = pct >= 70 ? '#198754' : pct >= 40 ? '#ffc107' : '#dc3545';
            html += `<div class="mb-2"><div class="d-flex justify-content-between small"><span>${catConfig[s.categoria]?.icon || '📌'} ${s.categoria}</span><span class="fw-bold">${s.aperti_ora}/${s.totale_servizi} aperti</span></div>
                <div class="subscore-bar"><div class="subscore-fill" style="width:${pct}%;background:${barColor}"></div></div>
                <small class="text-muted">Orario: ${s.orario_tipico}</small></div>`;
        });

        if (data.distribuzione_oraria) {
            const maxAperti = Math.max(...data.distribuzione_oraria.map(d => d.aperti), 1);
            html += `<div class="mt-3"><div class="fw-bold small mb-1">Servizi aperti per ora</div><div class="d-flex align-items-end" style="height:80px">`;
            data.distribuzione_oraria.forEach(d => {
                const h = Math.max(d.aperti / maxAperti * 70, 2);
                const isNow = d.ora == ora;
                html += `<div class="hour-bar" style="height:${h}px;background:${isNow ? '#0d6efd' : '#ccc'}" title="${d.ora}:00 → ${d.aperti} aperti"></div>`;
            });
            html += `</div><div class="d-flex justify-content-between" style="font-size:0.6rem;color:#999"><span>0</span><span>6</span><span>12</span><span>18</span><span>23</span></div></div>`;
        }

        if (data.passaggi_bus_per_ora && data.passaggi_bus_per_ora.length > 0) {
            const maxC = Math.max(...data.passaggi_bus_per_ora.map(d => d.corse), 1);
            html += `<div class="mt-3"><div class="fw-bold small mb-1">Passaggi bus per ora (GTFS, ${data.tipo_giorno})</div><div class="d-flex align-items-end" style="height:80px">`;
            for (let h = 0; h < 24; h++) {
                const d = data.passaggi_bus_per_ora.find(x => x.ora === h);
                const c = d ? d.corse : 0;
                html += `<div class="hour-bar" style="height:${Math.max(c / maxC * 70, 1)}px;background:${h == ora ? '#0d6efd' : '#6ea8fe'}" title="${h}:00 → ${c} passaggi"></div>`;
            }
            html += `</div><div class="d-flex justify-content-between" style="font-size:0.6rem;color:#999"><span>0</span><span>6</span><span>12</span><span>18</span><span>23</span></div></div>`;
        }

        document.getElementById('tempResult').innerHTML = html;
    } catch (e) { console.error(e); }
}

// Mostra la distribuzione dei suggerimenti per ora.
async function loadTemporaleStats() {
    try {
        const res = await fetch(`${API_BASE_URL}/temporale/statistiche`);
        const data = await res.json();
        if (!data.per_ora || data.per_ora.length === 0) {
            document.getElementById('tempSugChart').innerHTML = '<p class="text-muted small">Nessun dato. Usa lo Score per generare suggerimenti.</p>';
            return;
        }
        const maxC = Math.max(...data.per_ora.map(d => d.conteggio), 1);
        let html = '<div class="d-flex align-items-end gap-1" style="height:60px">';
        for (let h = 0; h < 24; h++) {
            const d = data.per_ora.find(x => x.ora === h);
            const c = d ? d.conteggio : 0;
            const height = Math.max(c / maxC * 50, 1);
            html += `<div style="width:10px;height:${height}px;background:${h>=8&&h<20?'#ffc107':'#6c757d'};border-radius:2px 2px 0 0" title="${h}:00 → ${c} suggerimenti"></div>`;
        }
        html += '</div><div class="d-flex justify-content-between" style="font-size:0.6rem;color:#999"><span>0</span><span>12</span><span>23</span></div>';
        document.getElementById('tempSugChart').innerHTML = html;
    } catch (e) { console.error(e); }
}

// Carica lo storico dei suggerimenti con i pulsanti di feedback.
async function loadStorico() {
    const profiloId = document.getElementById('storicoProfiloFilter').value;
    let url = `${API_BASE_URL}/suggerimenti?limit=30`;
    if (profiloId) url += `&profiloId=${profiloId}`;
    try {
        const res = await fetch(url); const data = await res.json();
        const list = document.getElementById('storicoList');
        if (data.length === 0) { list.innerHTML = '<p class="text-muted small">Nessun suggerimento.</p>'; return; }
        list.innerHTML = data.map(s => {
            const date = new Date(s.createdAt).toLocaleString('it-IT');
            return `<div class="storico-item"><div class="d-flex justify-content-between"><small class="text-muted">${date}</small><span class="badge ${s.fascia==='Diurna'?'bg-warning text-dark':'bg-dark'}">${s.punteggio}/100</span></div><small>${(s.motivazione||'').substring(0,150)}...</small><div class="mt-1"><span class="feedback-btn ${s.feedback==='utile'?'active':''}" onclick="sendFeedback(${s.id},'utile')">👍</span><span class="feedback-btn ${s.feedback==='non_utile'?'active':''}" onclick="sendFeedback(${s.id},'non_utile')">👎</span><span class="feedback-btn ${s.feedback==='salvato'?'active':''}" onclick="sendFeedback(${s.id},'salvato')">⭐</span></div></div>`;
        }).join('');
    } catch (e) { console.error(e); }
}

// Invia il feedback dell'utente su un suggerimento.
async function sendFeedback(id, tipo) {
    try { await fetch(`${API_BASE_URL}/suggerimenti/${id}/feedback`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ feedback: tipo }) }); loadStorico(); } catch (e) { console.error(e); }
}

// Carica e mostra le statistiche aggregate di utilizzo.
async function loadStatistiche() {
    try {
        const res = await fetch(`${API_BASE_URL}/statistiche`); const data = await res.json();
        let html = `<div class="row g-2 mb-3"><div class="col-6"><div class="border rounded p-2 text-center"><div class="fs-3 fw-bold text-primary">${data.totale_suggerimenti}</div><small>Suggerimenti</small></div></div><div class="col-6"><div class="border rounded p-2 text-center"><div class="fs-3 fw-bold ${data.punteggio_medio>=70?'text-success':data.punteggio_medio>=40?'text-warning':'text-danger'}">${data.punteggio_medio}</div><small>Punteggio medio</small></div></div></div>`;
        if (data.per_fascia.length > 0) { html += '<h6 class="small fw-bold">Per Fascia Oraria</h6>'; data.per_fascia.forEach(f => { html += `<div class="d-flex justify-content-between small border-bottom py-1"><span>${f.fascia==='Diurna'?'🌞':'🌙'} ${f.fascia}</span><span>${f.conteggio} — media ${f.media}/100</span></div>`; }); }
        if (data.feedback.length > 0) { html += '<h6 class="small fw-bold mt-3">Feedback</h6>'; data.feedback.forEach(f => { const icon = f.tipo==='utile'?'👍':f.tipo==='non_utile'?'👎':'⭐'; html += `<div class="d-flex justify-content-between small border-bottom py-1"><span>${icon} ${f.tipo}</span><span>${f.conteggio}</span></div>`; }); }
        document.getElementById('statsContent').innerHTML = html;
    } catch (e) { console.error(e); }
}

// Campiona una posizione perturbata con spostamento medio atteso 'raggioMedio' (metri).
// Laplace planare (geo-indistinguishability): angolo uniforme, raggio ~ Gamma(2, 1/ε) con ε = 2 / raggioMedio.
// Gaussiana: rumore normale indipendente sui due assi, raggio ~ Rayleigh(σ) con σ = raggioMedio / sqrt(π/2).
function perturba(lat, lon, raggioMedio, meccanismo) {
    if (raggioMedio <= 0) return { lat, lon };
    const theta = 2 * Math.PI * Math.random();
    let r;
    if (meccanismo === 'gauss') {
        const sigma = raggioMedio / Math.sqrt(Math.PI / 2);
        r = sigma * Math.sqrt(-2 * Math.log(1 - Math.random()));
    } else {
        const eps = 2 / raggioMedio;
        r = -(Math.log(1 - Math.random()) + Math.log(1 - Math.random())) / eps;
    }
    return {
        lat: lat + (r * Math.cos(theta)) / 111320,
        lon: lon + (r * Math.sin(theta)) / (111320 * Math.cos(lat * Math.PI / 180))
    };
}

// Parametri correnti del meccanismo di privacy scelto nella scheda.
function parametriPrivacy() {
    return {
        raggio: parseInt(document.getElementById('privacyRaggio').value),
        meccanismo: document.getElementById('privacyMeccanismo').value
    };
}

// Aggiorna l'etichetta del livello di perturbazione (ε per Laplace, σ per la gaussiana).
function aggiornaEtichettaPrivacy() {
    const { raggio, meccanismo } = parametriPrivacy();
    document.getElementById('privacyRaggioVal').textContent = `${raggio} m`;
    const el = document.getElementById('privacyEpsilon');
    if (raggio <= 0) el.textContent = 'Nessuna perturbazione';
    else if (meccanismo === 'gauss') el.textContent = `σ = ${(raggio / Math.sqrt(Math.PI / 2)).toFixed(0)} m per asse`;
    else el.textContent = `ε = ${(2000 / raggio).toFixed(2)} km⁻¹ (ε·r = ln 4 entro ${(Math.log(4) * raggio / 2).toFixed(0)} m)`;
    if (document.getElementById('privacyAttiva').checked) aggiornaPerturbazione();
}

// Attiva/disattiva la modalità privacy (solo la posizione perturbata viene inviata al server).
function toggleModalitaPrivacy() {
    const attiva = document.getElementById('privacyAttiva').checked;
    document.getElementById('privacyBadge').classList.toggle('d-none', !attiva);
    aggiornaPerturbazione();
}

// Genera (se la modalità privacy è attiva) la posizione perturbata del punto selezionato e la mostra sulla mappa.
function aggiornaPerturbazione() {
    if (privacyMarker) { map.removeLayer(privacyMarker); privacyMarker = null; }
    if (privacyLine) { map.removeLayer(privacyLine); privacyLine = null; }
    privacyPert = null;
    if (!lastClickLat || !document.getElementById('privacyAttiva').checked) return;
    const { raggio, meccanismo } = parametriPrivacy();
    privacyPert = perturba(lastClickLat, lastClickLon, raggio, meccanismo);
    disegnaPerturbato(privacyPert, 'Posizione inviata al server');
}

// Disegna il punto perturbato e il collegamento con la posizione reale.
function disegnaPerturbato(p, etichetta) {
    if (privacyMarker) map.removeLayer(privacyMarker);
    if (privacyLine) map.removeLayer(privacyLine);
    privacyMarker = L.circleMarker([p.lat, p.lon], { radius: 9, fillColor: '#ff6b6b', color: '#c00', weight: 2, fillOpacity: 0.5 })
        .addTo(map).bindPopup(`🔒 ${etichetta}`);
    privacyLine = L.polyline([[lastClickLat, lastClickLon], [p.lat, p.lon]], { color: '#ff6b6b', dashArray: '5,10', weight: 2 }).addTo(map);
}

// Invia al server la posizione reale e i campioni perturbati per l'esperimento di valutazione.
async function valutaPrivacy(campioni) {
    const profiloId = document.getElementById('profiloSelect').value;
    const body = {
        lat: lastClickLat, lon: lastClickLon,
        ora: parseInt(document.getElementById('privacyOra').value),
        giorno: parseInt(giorno()),
        profiloId: profiloId ? parseInt(profiloId) : null,
        campioni
    };
    const res = await fetch(`${API_BASE_URL}/privacy/valutazione`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    });
    return res.json();
}

// Confronta score e servizi vicini tra posizione reale e una posizione perturbata.
async function confrontoPrivacy() {
    if (!lastClickLat) { document.getElementById('privacyResult').innerHTML = '<p class="text-warning">Clicca prima un punto sulla mappa.</p>'; return; }
    const { raggio, meccanismo } = parametriPrivacy();
    const p = privacyPert || perturba(lastClickLat, lastClickLon, raggio, meccanismo);
    try {
        const data = await valutaPrivacy([{ lat: p.lat, lon: p.lon, livello: raggio }]);
        const x = data.risultati[0];
        disegnaPerturbato(p, `Posizione perturbata (${meccanismo}, ${raggio} m)<br>Score: ${x.score}`);

        const r = data.subscores_reale, q = x.subscores;
        const riga = (ic, a, b) => `<tr><td>${ic}</td><td>${a}</td><td>${b}</td><td class="${Math.abs(a-b)>10?'text-danger':''}">${a-b}</td></tr>`;
        document.getElementById('privacyResult').innerHTML = `
            <div class="border rounded p-2 bg-light mb-2">
                <div class="d-flex justify-content-between"><b>Privacy Perturbation</b><span class="fw-bold">${x.privacy_perturbation_metri} m</span></div>
                <div class="d-flex justify-content-between"><b>Recall servizi vicini</b><span>${Math.round(x.recall_servizi * 100)}% di ${data.servizi_vicini_reali}</span></div>
            </div>
            <div class="row g-2">
                <div class="col-6 text-center"><div class="small fw-bold">📍 Reale</div>
                    <div class="score-badge ${data.score_reale>=70?'score-high':data.score_reale>=40?'score-mid':'score-low'}" style="font-size:1.8rem">${data.score_reale}</div></div>
                <div class="col-6 text-center"><div class="small fw-bold">🔒 Perturbata</div>
                    <div class="score-badge ${x.score>=70?'score-high':x.score>=40?'score-mid':'score-low'}" style="font-size:1.8rem">${x.score}</div></div>
            </div>
            <div class="text-center mt-1"><small>Quality Loss: <b class="text-danger">${x.quality_loss} punti</b></small></div>
            <table class="table table-sm mt-2" style="font-size:0.7rem">
                <tr><th></th><th>Reale</th><th>Perturbata</th><th>Δ</th></tr>
                ${riga('🚌', r.trasporti, q.trasporti)}${riga('📚', r.biblioteche, q.biblioteche)}${riga('🌳', r.aree_verdi, q.aree_verdi)}
                ${riga('🚲', r.mobilita, q.mobilita)}${riga('🏠', r.residenze, q.residenze)}${riga('🍽️', r.mense, q.mense)}${riga('🏛️', r.sedi, q.sedi)}
            </table>`;
    } catch (e) { console.error(e); }
}

// Esperimento di trade-off: per ogni livello genera 20 campioni perturbati e grafica privacy e qualità del servizio.
async function tradeoffPrivacy() {
    if (!lastClickLat) { document.getElementById('tradeoffResult').innerHTML = '<p class="text-warning">Clicca prima un punto sulla mappa.</p>'; return; }
    const { meccanismo } = parametriPrivacy();
    const livelli = [0, 50, 100, 200, 300, 500, 1000, 2000];
    const campioniPerLivello = 20;
    const campioni = [];
    livelli.forEach(l => {
        for (let i = 0; i < campioniPerLivello; i++) {
            const p = perturba(lastClickLat, lastClickLon, l, meccanismo);
            campioni.push({ lat: p.lat, lon: p.lon, livello: l });
        }
    });
    document.getElementById('tradeoffResult').innerHTML = `<p class="text-muted">Calcolo in corso (${campioni.length} campioni)...</p>`;
    try {
        const data = await valutaPrivacy(campioni);
        const media = arr => arr.reduce((a, b) => a + b, 0) / arr.length;
        const righe = livelli.map(l => {
            const g = data.risultati.filter(x => x.livello === l);
            return {
                livello: l,
                privacy: media(g.map(x => x.privacy_perturbation_metri)),
                loss: media(g.map(x => x.quality_loss)),
                recall: media(g.map(x => x.recall_servizi)) * 100
            };
        });

        const canvas = document.getElementById('tradeoffChart');
        canvas.classList.remove('d-none');
        if (tradeoffChart) tradeoffChart.destroy();
        tradeoffChart = new Chart(canvas, {
            type: 'line',
            data: {
                labels: livelli.map(l => `${l}`),
                datasets: [
                    { label: 'Privacy Perturbation (m)', data: righe.map(r => r.privacy.toFixed(0)), yAxisID: 'y', borderColor: '#0d6efd', backgroundColor: '#0d6efd' },
                    { label: 'Recall servizi vicini (%)', data: righe.map(r => r.recall.toFixed(1)), yAxisID: 'y1', borderColor: '#198754', backgroundColor: '#198754' },
                    { label: 'Perdita di score (punti)', data: righe.map(r => r.loss.toFixed(1)), yAxisID: 'y1', borderColor: '#dc3545', backgroundColor: '#dc3545' }
                ]
            },
            options: {
                responsive: true,
                interaction: { mode: 'index', intersect: false },
                plugins: { legend: { labels: { boxWidth: 10, font: { size: 10 } } } },
                scales: {
                    x: { title: { display: true, text: `Spostamento medio configurato (m) — ${meccanismo === 'gauss' ? 'gaussiana' : 'Laplace planare'}` } },
                    y: { position: 'left', title: { display: true, text: 'metri' } },
                    y1: { position: 'right', min: 0, max: 100, grid: { drawOnChartArea: false }, title: { display: true, text: '% / punti' } }
                }
            }
        });

        let html = `<div class="small text-muted mb-1">Score reale: <b>${data.score_reale}</b> · servizi entro 500 m: <b>${data.servizi_vicini_reali}</b></div>`;
        html += '<table class="table table-sm table-bordered" style="font-size:0.72rem"><tr><th>Livello (m)</th><th>Privacy (m)</th><th>Recall</th><th>Loss</th></tr>';
        righe.forEach(r => { html += `<tr><td>${r.livello}</td><td>${r.privacy.toFixed(0)}</td><td>${r.recall.toFixed(0)}%</td><td>${r.loss.toFixed(1)}</td></tr>`; });
        html += '</table>';
        document.getElementById('tradeoffResult').innerHTML = html;
    } catch (e) { console.error(e); document.getElementById('tradeoffResult').innerHTML = '<p class="text-danger">Errore</p>'; }
}



// Esegue il clustering delle zone e mostra cluster e indice di Moran.
async function loadClustering() {
    const k = document.getElementById('clusterK').value;
    const ora = document.getElementById('clusterOra').value;
    document.getElementById('clusterResult').innerHTML = '<p class="text-muted">Calcolo K-Means...</p>';
    try {
        const res = await fetch(`${API_BASE_URL}/clustering?k=${k}&ora=${ora}&giorno=${giorno()}`);
        const data = await res.json();

        if (clusterLayer) map.removeLayer(clusterLayer);
        clusterLayer = L.layerGroup().addTo(map);

        let html = '';
        data.clusters.forEach(cl => {
            const color = clusterColors[cl.cluster_id % clusterColors.length];
            html += `<div class="d-flex align-items-center gap-2 mb-2 p-2 border rounded">
                <div style="width:20px;height:20px;border-radius:50%;background:${color}"></div>
                <div class="flex-fill"><div class="fw-bold small">${cl.label}</div>
                <small class="text-muted">${cl.num_celle} zone — Score medio: ${cl.score_medio}/100</small></div></div>`;

            cl.celle.forEach(c => {
                const stepLat = 0.05 / 10, stepLon = 0.08 / 10;
                const bounds = [[c.lat - stepLat/2, c.lon - stepLon/2], [c.lat + stepLat/2, c.lon + stepLon/2]];
                L.rectangle(bounds, { color, weight: 1, fillColor: color, fillOpacity: 0.35 })
                    .bindPopup(`<b>${cl.label}</b><br>Score: ${c.score}/100`)
                    .addTo(clusterLayer);
            });
        });
        document.getElementById('clusterResult').innerHTML = html;

        const m = data.moran;
        document.getElementById('moranResult').innerHTML = boxMoran(`Moran's I — ${m.indicatore}`, m.moran_i, m.atteso, m.p_value, m.interpretazione, m.nota);
    } catch (e) { console.error(e); document.getElementById('clusterResult').innerHTML = '<p class="text-danger">Errore</p>'; }
}

// Riquadro con indice di Moran, valore atteso, p-value e interpretazione.
function boxMoran(titolo, moranI, atteso, pValue, interpretazione, nota) {
    const colore = pValue < 0.05 ? (moranI > atteso ? '#198754' : '#dc3545') : '#6c757d';
    return `<div class="border rounded p-2 bg-light">
        <div class="small fw-bold">${titolo}</div>
        <div class="text-center my-2"><span style="font-size:1.8rem;font-weight:700;color:${colore}">${moranI}</span></div>
        <div class="d-flex justify-content-between small"><span>E[I] = ${atteso}</span><span>p-value = ${pValue}</span></div>
        <div class="small mt-1">${interpretazione}</div>
        <div class="text-muted" style="font-size:0.65rem">${nota}</div>
    </div>`;
}

// Calcola l'indice di Moran per l'indicatore scelto e colora le celle della griglia in base al valore.
async function loadMoran() {
    const ind = document.getElementById('moranIndicatore').value;
    const ora = document.getElementById('clusterOra').value;
    document.getElementById('moranIndicatoreResult').innerHTML = '<p class="text-muted small">Calcolo in corso...</p>';
    try {
        const res = await fetch(`${API_BASE_URL}/moran?indicatore=${ind}&ora=${ora}&giorno=${giorno()}`);
        const data = await res.json();
        if (data.errore) { document.getElementById('moranIndicatoreResult').innerHTML = `<p class="text-danger small">${data.errore}</p>`; return; }

        if (moranLayer) map.removeLayer(moranLayer);
        moranLayer = L.layerGroup().addTo(map);
        const max = Math.max(...data.valori.map(v => v.valore), 1);
        const stepLat = 0.05 / data.celle, stepLon = 0.08 / data.celle;
        data.valori.forEach(v => {
            const t = v.valore / max;
            const bounds = [[v.lat - stepLat/2, v.lon - stepLon/2], [v.lat + stepLat/2, v.lon + stepLon/2]];
            L.rectangle(bounds, { color: '#555', weight: 0.3, fillColor: `rgb(${Math.round(255 - 180 * t)}, ${Math.round(255 - 200 * t)}, 255)`, fillOpacity: 0.6 })
                .bindPopup(`${data.indicatore}: <b>${v.valore}</b>`)
                .addTo(moranLayer);
        });
        document.getElementById('moranIndicatoreResult').innerHTML =
            boxMoran(`Moran's I — ${data.descrizione}`, data.moran_i, data.atteso, data.p_value, data.interpretazione,
                     `${data.pesi}; ${data.permutazioni} permutazioni; z = ${data.z_permutazioni}`);
    } catch (e) { console.error(e); document.getElementById('moranIndicatoreResult').innerHTML = '<p class="text-danger small">Errore</p>'; }
}

// Rimuove cluster e mappa di Moran dalla mappa.
function clearClusters() {
    if (clusterLayer) { map.removeLayer(clusterLayer); clusterLayer = null; }
    if (moranLayer) { map.removeLayer(moranLayer); moranLayer = null; }
    document.getElementById('clusterResult').innerHTML = '';
    document.getElementById('moranResult').innerHTML = '';
    document.getElementById('moranIndicatoreResult').innerHTML = '';
}

// Inizializzazione: giorno corrente, etichetta privacy, layer biblioteche e profili.
document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('giornoGlobale').value = (new Date().getDay() + 6) % 7;
    aggiornaEtichettaPrivacy();
    loadLayer('biblioteche');
    loadProfili();
});