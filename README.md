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

## Student Accessibility Score

La formula è implementata una sola volta in `UrbanAdvisor.Api/Services/ScoringService.cs` ed è usata da ranking, griglia di densità, raccomandazioni, clustering e privacy.

1. Per ogni punto si contano i servizi entro **500 m** (distanza percorribile a piedi in circa 6 minuti) con una sola query PostGIS.
2. Sub-score 0–100 per fattore:

| Fattore | Regola | Valore che dà 100 |
|---|---|---|
| Trasporti | `100 · (1 − d/1000)`, con d = distanza dalla fermata più vicina | fermata a 0 m (0 oltre 1 km) |
| Biblioteche/Sale studio | lineare con saturazione | 2 |
| Aree verdi | lineare con saturazione | 3 |
| Mobilità sostenibile | km di pista ciclabile nel buffer | 1,5 km |
| Residenze | lineare con saturazione | 2 |
| Mense/Punti ristoro | lineare con saturazione | 1 |
| Sedi universitarie | lineare con saturazione | 10 |

3. **Contesto orario**: fuori dalla fascia 8–20, studio × 0,3, mense × 0,3, sedi × 0,2, trasporti × 1,3, residenze × 1,5 (tutti troncati a 100).
4. **Profilo**: media pesata dei sub-score con i pesi del profilo (0–100) normalizzati a somma 1.

## API principali

| Metodo e percorso | Parametri | Descrizione |
|---|---|---|
| `GET /api/{categoria}` | — | PoI di una categoria: `biblioteche`, `salestudio`, `fermate`, `areeverdi`, `residenze`, `stazioni`, `mense`, `sedi`, `piste` (con geometria) |
| `GET /api/nearby` | `lat`, `lon`, `raggio` (m), `categoria?` | PoI entro il raggio, ordinati per distanza |
| `GET /api/area/indicatori` | `lat`, `lon`, `raggio` (m) | Buffer analysis: conteggi, km di piste, densità per km² |
| `GET /api/ranking` | `lat`, `lon`, `ora`, `profiloId?` | Score context-aware del punto (salvato nello storico) |
| `GET /api/raccomandazioni` | `profiloId?`, `ora?`, `top?` | Migliori zone con motivazione |
| `GET /api/density/grid` | `celle?`, `profiloId?`, `ora?` | Griglia di densità e score |
| `GET /api/heatmap` | `categoria?` | Punti per la heatmap |
| `GET/POST/PUT/DELETE /api/profili` | — | Gestione profili utente |
| `GET /api/suggerimenti`, `PUT /api/suggerimenti/{id}/feedback` | `profiloId?`, `limit?` | Storico e feedback |
| `GET /api/statistiche` | — | Statistiche di utilizzo |
| `GET /api/temporale/disponibilita` | `giorno?`, `ora?` | Servizi aperti per giorno e ora |
| `GET /api/temporale/statistiche` | — | Suggerimenti per ora |
| `GET /api/mobility/tempo-percorrenza` | `lat`, `lon`, `ora?` | Tempi a piedi / bici / TPL verso la sede più vicina |
| `GET /api/mobility/isocrona` | `ora?`, `modalita?` | Tempi di percorrenza su griglia |
| `GET /api/privacy/confronto` | `lat`, `lon`, `ora`, `sigma`, `profiloId?` | Posizione reale vs perturbata |
| `GET /api/privacy/tradeoff` | `lat`, `lon`, `ora`, `profiloId?` | Trade-off privacy / qualità del servizio |
| `GET /api/clustering` | `k?`, `profiloId?`, `ora?` | K-Means delle zone e indice di Moran |

L'elenco completo con i tipi dei parametri è nel documento OpenAPI.
