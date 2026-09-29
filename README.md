# Student Urban Accessibility Advisor — Bologna

Piattaforma context-aware (Proposta 4, Sistemi Context-Aware a.a. 2025/26) che integra open data della città di Bologna in un database PostGIS e suggerisce le zone più adatte alla vita universitaria in base a posizione, orario e preferenze dello studente.

- **Database**: PostgreSQL 15 + PostGIS 3.3
- **Back-end**: .NET 10 Minimal API + Entity Framework Core (Npgsql + NetTopologySuite)
- **Front-end**: dashboard statica (Leaflet, Bootstrap) servita da nginx
- **Deploy**: Docker Compose (3 container: `db`, `api`, `frontend`)

## Avvio rapido

Prerequisiti: Docker e Docker Compose, cartella `data/` popolata (vedi sotto).

```bash
docker compose up --build
```

| Servizio | URL |
|---|---|
| Dashboard | http://localhost:8081 |
| API REST | http://localhost:8080/api/... |
| Documento OpenAPI | http://localhost:8080/openapi/v1.json |
| PostGIS | localhost:5432 (db `urban_advisor`, utente `postgres`) |

Il database viene popolato da `init.sql` **solo al primo avvio** (volume vuoto). Dopo aver modificato `init.sql` o i file in `data/`, ricreare il volume:

```bash
docker compose down -v
docker compose up --build
```

## Dati (`data/`)

La cartella `data/` è montata nel container PostGIS in `/var/lib/postgresql/csv_data` e letta da `init.sql` con `COPY`.

| File | Contenuto | Fonte |
|---|---|---|
| `biblioteche-comunali-di-bologna.csv` | Biblioteche comunali | Open Data Comune di Bologna |
| `piste-ciclopedonali.csv` | Piste ciclabili (MultiLineString) | Open Data Comune di Bologna |
| `tper-fermate-autobus.csv` | Fermate autobus TPER | Open Data Comune di Bologna |
| `aree-verdi_entrate_centroidi.csv` | Aree verdi (centroidi degli ingressi) | Open Data Comune di Bologna |
| `residenze-universitarie.csv` | Residenze universitarie | Open Data Comune di Bologna |
| `stazioniferroviarie_20210401.csv` | Stazioni ferroviarie | Open Data Comune di Bologna |
| `mappe.csv` | Sedi Unibo e musei (punti di interesse) | dati.unibo.it |
| `sale-studio.csv` | Sale studio (facoltativo, vedi sotto) | dataset derivato |
| `gtfs/stops.txt`, `trips.txt`, `stop_times.txt`, `calendar.txt` | Orari del trasporto pubblico | GTFS TPER |

Le mense e i punti ristoro (5 elementi) sono inseriti direttamente in `init.sql` come dataset derivato manualmente.

### Formato di `sale-studio.csv`

Formato normalizzato, separatore `;`, prima riga di intestazione:

```
nome;indirizzo;lat;lon;posti;fonte
Sala studio Esempio;Via Zamboni 1, Bologna;44.4968;11.3522;120;dati.unibo.it
```

`lat`/`lon` in gradi decimali WGS84 (accettano sia il punto sia la virgola decimale). Se il file manca la tabella `sale_studio` resta vuota e l'avvio prosegue.

## Modello dati spaziale

Ogni tabella geografica ha due colonne:

- `geom` — `geometry(…, 4326)`, usata per restituire le coordinate alla mappa;
- `geog` — `geography`, generata automaticamente da `geom`, usata per **distanze e buffer in metri** (`ST_DWithin`, `ST_Distance`, `ST_Length`).

Entrambe hanno un indice GiST.

## Contesto usato dal sistema

Ogni richiesta context-aware combina: **posizione**, **ora** (0–23), **giorno della settimana** (convenzione `0 = lunedì … 6 = domenica`, scelto dal selettore in alto nella dashboard) e **profilo** dell'utente. Dal giorno si ricava il tipo di giorno GTFS: feriale (lun–ven), sabato, festivo (domenica).

## Student Accessibility Score

La formula è implementata una sola volta in `UrbanAdvisor.Api/Services/ScoringService.cs` ed è usata da ranking, griglia di densità, raccomandazioni, clustering e privacy.

1. Per ogni punto si contano i servizi entro **500 m** (distanza percorribile a piedi in circa 6 minuti) con una sola query PostGIS.
2. Sub-score 0–100 per fattore:

| Fattore | Regola | Valore che dà 100 |
|---|---|---|
| Trasporti | 50% vicinanza `100 · (1 − d/1000)` (d = distanza dalla fermata più vicina) + 50% frequenza: corse/ora GTFS della fermata più servita nel raggio, nella fascia oraria e nel tipo di giorno | fermata a 0 m; 20 corse/ora |
| Biblioteche/Sale studio | biblioteche **aperte** nel giorno e nell'ora (tabella `orari_servizi`) + sale studio | 2 |
| Aree verdi | lineare con saturazione | 3 |
| Mobilità sostenibile | km di pista ciclabile nel buffer | 1,5 km |
| Residenze | lineare con saturazione | 2 |
| Mense/Punti ristoro | lineare con saturazione | 1 |
| Sedi universitarie | lineare con saturazione | 10 |

3. **Contesto temporale**: quando l'università è chiusa (fuori dalla fascia 8–20 o di domenica) sale studio × 0,3, mense × 0,3, sedi × 0,2; di notte residenze × 1,5 (troncato a 100). La chiusura delle biblioteche è già data dagli orari.
4. **Profilo**: media pesata dei sub-score con i pesi del profilo (0–100) normalizzati a somma 1.

## Mobilità e trasporto pubblico (GTFS)

Implementazione in `Services/MobilityService.cs`; i dati GTFS vengono importati da `init.sql` nelle tabelle `gtfs_fermate`, `gtfs_trips`, `gtfs_stop_times`, `gtfs_frequenze_fermata`.

- **Periodo di riferimento**: tra le date di inizio dei servizi in `calendar.txt` si sceglie quella coperta dal maggior numero di `service_id`, per non sommare periodi diversi (es. orario estivo e invernale). Le eccezioni di `calendar_dates.txt` non sono considerate.
- **Piedi / bici**: distanza di Haversine × 1,3 (deviazione stradale), a 5 e 15 km/h.
- **Trasporto pubblico**: corse **dirette** (senza cambi) che passano da una fermata entro 500 m dall'origine e, più avanti nella stessa corsa, da una fermata entro 500 m dalla destinazione, con partenza nella fascia oraria e nel tipo di giorno richiesti. Tempo = piedi fino alla fermata + attesa media (metà dell'intervallo tra le corse) + tempo a bordo da `stop_times` + piedi fino alla destinazione. Si sceglie la coppia di fermate più rapida; nelle isocrone il tempo con TPL è il minimo tra bus e percorso a piedi.
- **Isocrone**: griglia 12 × 12 sulla bounding box; per ogni cella il tempo verso la sede universitaria scelta dall'utente, colorato per fasce 0–10, 10–20, 20–30, 30–45, >45 minuti.

## Privacy della posizione

La perturbazione avviene **nel browser** (`Frontend/app.js`), prima dell'invio: con la *modalità privacy* attiva il server riceve solo il punto perturbato (score, servizi vicini, indicatori, mobilità e storico usano quello). Due meccanismi, parametrizzati dallo **spostamento medio atteso r** così da essere confrontabili:

- **Laplace planare** (geo-indistinguishability, Andrés et al. 2013): angolo uniforme, raggio ~ Gamma(2, 1/ε) con ε = 2/r;
- **Gaussiano**: rumore normale indipendente sui due assi con σ = r / √(π/2) (raggio ~ Rayleigh(σ)).

Per la valutazione il client invia a `POST /api/privacy/valutazione` la posizione reale e i campioni perturbati (8 livelli × 20 campioni) e il server calcola:

- **Privacy Perturbation**: distanza in metri tra posizione reale e perturbata;
- **Quality of Service**: perdita di score |score reale − score perturbato| e **recall dei servizi vicini** (frazione dei PoI entro 500 m dalla posizione reale che restano entro 500 m da quella perturbata).

Il trade-off è mostrato con un grafico (Chart.js) nella scheda Privacy.

## Analisi spaziale: clustering e Moran

- **K-Means** sulle 100 celle della griglia 10 × 10, con feature standardizzate (z-score): biblioteche + sale studio, fermate, aree verdi, km di piste, residenze, mense, sedi.
- **Indice di Moran globale** (`Services/SpatialStats.cs`) con pesi di contiguità *queen* standardizzati per riga, valore atteso E[I] = −1/(N−1) e **p-value con 999 permutazioni**. Indicatori disponibili: numero di PoI per cella (celle non sovrapposte) per biblioteche + sale studio, fermate, sedi, aree verdi, residenze, mense; oppure lo Student Accessibility Score. Lo score deriva da buffer di 500 m che si sovrappongono tra celle vicine, quindi la sua autocorrelazione è in parte dovuta alla costruzione: per l'analisi conviene usare le densità per cella.

## API principali

Tutti gli endpoint context-aware accettano anche `giorno?` (0 = lunedì … 6 = domenica; default: oggi).

| Metodo e percorso | Parametri | Descrizione |
|---|---|---|
| `GET /api/{categoria}` | — | PoI di una categoria: `biblioteche`, `salestudio`, `fermate`, `areeverdi`, `residenze`, `stazioni`, `mense`, `sedi`, `piste` (con geometria) |
| `GET /api/nearby` | `lat`, `lon`, `raggio` (m), `categoria?` | PoI entro il raggio, ordinati per distanza |
| `GET /api/area/indicatori` | `lat`, `lon`, `raggio` (m), `ora?`, `giorno?` | Buffer analysis: conteggi, biblioteche aperte, km di piste, corse/ora, densità per km² |
| `GET /api/ranking` | `lat`, `lon`, `ora`, `giorno?`, `profiloId?` | Score context-aware del punto (salvato nello storico) |
| `GET /api/raccomandazioni` | `profiloId?`, `ora?`, `giorno?`, `top?` | Migliori zone con motivazione |
| `GET /api/density/grid` | `celle?`, `profiloId?`, `ora?`, `giorno?` | Griglia di densità e score |
| `GET /api/heatmap` | `categoria?` | Punti per la heatmap |
| `GET/POST/PUT/DELETE /api/profili` | — | Gestione profili utente |
| `GET /api/suggerimenti`, `PUT /api/suggerimenti/{id}/feedback` | `profiloId?`, `limit?` | Storico e feedback |
| `GET /api/statistiche` | — | Statistiche di utilizzo |
| `GET /api/temporale/disponibilita` | `giorno?`, `ora?` | Servizi aperti e passaggi bus per ora |
| `GET /api/temporale/statistiche` | — | Suggerimenti per ora |
| `GET /api/mobility/tempo-percorrenza` | `lat`, `lon`, `ora?`, `giorno?`, `destLat?`, `destLon?` | Tempi a piedi / bici / TPL verso la sede più vicina alla destinazione (o all'origine) |
| `GET /api/mobility/isocrona` | `destLat`, `destLon`, `ora?`, `giorno?`, `modalita?`, `celle?` | Isocrone su griglia verso la sede scelta |
| `POST /api/privacy/valutazione` | JSON: `lat`, `lon`, `ora`, `giorno?`, `profiloId?`, `campioni[]` (`lat`, `lon`, `livello`) | Privacy Perturbation, perdita di score e recall per ogni campione |
| `GET /api/clustering` | `k?`, `profiloId?`, `ora?`, `giorno?` | K-Means delle zone e Moran dello score |
| `GET /api/moran` | `indicatore?`, `celle?`, `profiloId?`, `ora?`, `giorno?` | Moran (queen, permutazioni) di una densità per cella o dello score |

L'elenco completo con i tipi dei parametri è nel documento OpenAPI.
