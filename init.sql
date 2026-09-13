-- ============================================================================
-- init.sql - Script di inizializzazione del database PostGIS
-- Configurazione del DB:
-- 1. Attivazione estensione PostGIS.
-- 2. Creazione tabelle definitive con tipologie geometriche (Point, MultiLineString, MultiPoint).
-- 3. Creazione tabelle di staging per parsing CSV.
-- 4. Importazione dati massiva tramite comando COPY.
-- 5. ETL spaziale: trasformazione dati grezzi in geometrie SRID 4326.
-- 6. Creazione indici spaziali GiST per l'ottimizzazione delle query.
-- 7. Popolamento tabelle di supporto: profili utente e orari servizi.
-- ============================================================================

CREATE EXTENSION IF NOT EXISTS postgis;

CREATE TABLE IF NOT EXISTS biblioteche (
    id SERIAL PRIMARY KEY,
    nome VARCHAR(255),
    indirizzo VARCHAR(255),
    quartiere VARCHAR(255),
    postazioni_lettura INTEGER,
    geom GEOMETRY(MultiPoint, 4326)
);

CREATE TABLE IF NOT EXISTS piste_ciclabili (
    id SERIAL PRIMARY KEY,
    codice VARCHAR(50),
    lunghezza NUMERIC,
    utilizzo VARCHAR(100),
    geom GEOMETRY(MultiLineString, 4326)
);

CREATE TABLE IF NOT EXISTS fermate_bus (
    codice_fermata VARCHAR(50) PRIMARY KEY,
    linea_bus VARCHAR(255),
    nome_fermata VARCHAR(255),
    geom GEOMETRY(Point, 4326)
);

CREATE TABLE IF NOT EXISTS aree_verdi (
    id SERIAL PRIMARY KEY,
    nome_area VARCHAR(255),
    tipologia VARCHAR(255),
    quartiere VARCHAR(255),
    ubicazione VARCHAR(255),
    geom GEOMETRY(Point, 4326)
);

CREATE TABLE IF NOT EXISTS residenze_universitarie (
    id VARCHAR(50) PRIMARY KEY,
    nome VARCHAR(255),
    descrizione TEXT,
    indirizzo VARCHAR(255),
    posti_letto INTEGER,
    quartiere VARCHAR(255),
    url TEXT,
    geom GEOMETRY(Point, 4326)
);

CREATE TABLE IF NOT EXISTS stazioni_ferroviarie (
    codice VARCHAR(50) PRIMARY KEY,
    denominazione VARCHAR(255),
    ubicazione VARCHAR(255),
    comune VARCHAR(100),
    geom GEOMETRY(Point, 4326)
);

CREATE TABLE IF NOT EXISTS sedi_universitarie (
    id SERIAL PRIMARY KEY,
    tipo VARCHAR(50),      -- 'unibo' (dipartimenti/uffici/laboratori) o 'museo' (Sistema Museale Ateneo)
    nome TEXT,
    indirizzo VARCHAR(255),
    url TEXT,
    geom GEOMETRY(Point, 4326)
);

CREATE TEMP TABLE stg_sedi_universitarie (
    type TEXT, name TEXT, address TEXT, city TEXT, lat TEXT, lon TEXT, url TEXT, notes TEXT
);

COPY stg_sedi_universitarie FROM '/var/lib/postgresql/csv_data/mappe.csv' DELIMITER ',' CSV HEADER QUOTE '"';

INSERT INTO sedi_universitarie (tipo, nome, indirizzo, url, geom)
SELECT type, name, address, url,
    ST_SetSRID(ST_MakePoint(NULLIF(lon,'')::FLOAT, NULLIF(lat,'')::FLOAT), 4326)
FROM stg_sedi_universitarie
WHERE city = 'Bologna'
  AND NULLIF(lat,'')::FLOAT IS NOT NULL AND NULLIF(lat,'')::FLOAT != 0
  AND NULLIF(lon,'')::FLOAT IS NOT NULL AND NULLIF(lon,'')::FLOAT != 0;

CREATE INDEX IF NOT EXISTS idx_sedi_universitarie_geom ON sedi_universitarie USING gist(geom);

CREATE TABLE IF NOT EXISTS mense (
    id SERIAL PRIMARY KEY,
    nome VARCHAR(255),
    indirizzo VARCHAR(255),
    tipo VARCHAR(50),       -- 'mensa' (pasto completo) o 'punto_ristoro' (self/microonde)
    gestore VARCHAR(255),
    geom GEOMETRY(Point, 4326)
);

INSERT INTO mense (nome, indirizzo, tipo, gestore, geom) VALUES
('Mensa Irnerio', 'Piazza Puntoni, 1 - Bologna', 'mensa', 'Cimas srl (ER.GO)',
    ST_SetSRID(ST_MakePoint(11.3536, 44.4973), 4326)),
('Punto Ristoro Piazza Verdi', 'Via Petroni, ang. Piazza Verdi - Bologna', 'punto_ristoro', 'Unibo',
    ST_SetSRID(ST_MakePoint(11.3520, 44.4957), 4326)),
('Punto Ristoro Centro Polifunzionale Unione', 'Via Azzo Gardino, 33 - Bologna', 'punto_ristoro', 'Unibo',
    ST_SetSRID(ST_MakePoint(11.3350, 44.5005), 4326)),
('Punto Ristoro Residenza Umberto Eco', 'Via San Petronio Vecchio, 32 - Bologna', 'punto_ristoro', 'Unibo / ER.GO',
    ST_SetSRID(ST_MakePoint(11.3560, 44.4935), 4326)),
('Punto Ristoro Residenza Morgagni', 'Largo Trombetti, 1/2 - Bologna', 'punto_ristoro', 'Unibo / ER.GO',
    ST_SetSRID(ST_MakePoint(11.3495, 44.4965), 4326));

CREATE INDEX IF NOT EXISTS idx_mense_geom ON mense USING gist(geom);

CREATE TEMP TABLE stg_biblioteche (
    biblioteca TEXT, tipologia TEXT, indirizzo TEXT, quartiere TEXT, rete_wifi TEXT, 
    fasciatoio TEXT, newsletter TEXT, telefono TEXT, email TEXT, pagina_web TEXT, 
    sito_web TEXT, geo_point TEXT, geo_shape TEXT, descrizione TEXT, 
    superficie_totale_mq TEXT, superficie_accessibile TEXT, postazioni TEXT, 
    accessibilita TEXT, servizi_igienici TEXT, aria_condizionata TEXT, fotocopie TEXT, 
    area_bambini TEXT, facebook TEXT, twitter TEXT, instagram TEXT, youtube TEXT, 
    area_statistica TEXT, zona_prossimita TEXT
);

CREATE TEMP TABLE stg_piste (
    codice TEXT, anno TEXT, lunghezza TEXT, utilizzo TEXT, 
    descrizione_tipologia TEXT, geo_point_2d TEXT, geo_shape TEXT, fid TEXT
);

CREATE TEMP TABLE stg_fermate (
    codice_fermata TEXT, linea_bus TEXT, nome_fermata TEXT, ubicazione TEXT, 
    comune TEXT, codice_zona TEXT, quartiere TEXT, geopoint TEXT, 
    zona_prossimita TEXT, area_statistica TEXT
);

CREATE TEMP TABLE stg_aree_verdi (
    geo_point TEXT, geo_shape TEXT, tipologia_area TEXT, nome_area TEXT, 
    quartiere TEXT, ubicazione TEXT
);

CREATE TEMP TABLE stg_residenze (
    id TEXT, nome TEXT, descrizione TEXT, indirizzo TEXT, posti_letto TEXT, 
    quartiere TEXT, orario_pubblico TEXT, contatti TEXT, url TEXT, 
    area_statistica TEXT, zona_prossimita TEXT, coordinate TEXT
);

CREATE TEMP TABLE stg_stazioni (
    codice TEXT, denominazione TEXT, ubicazione TEXT, comune TEXT, 
    geopoint TEXT, zona_prossimita TEXT
);

COPY stg_biblioteche FROM '/var/lib/postgresql/csv_data/biblioteche-comunali-di-bologna.csv' DELIMITER ';' CSV HEADER QUOTE '"';
COPY stg_piste FROM '/var/lib/postgresql/csv_data/piste-ciclopedonali.csv' DELIMITER ';' CSV HEADER QUOTE '"';
COPY stg_fermate FROM '/var/lib/postgresql/csv_data/tper-fermate-autobus.csv' DELIMITER ';' CSV HEADER QUOTE '"';
COPY stg_aree_verdi FROM '/var/lib/postgresql/csv_data/aree-verdi_entrate_centroidi.csv' DELIMITER ';' CSV HEADER QUOTE '"';
COPY stg_residenze FROM '/var/lib/postgresql/csv_data/residenze-universitarie.csv' DELIMITER ';' CSV HEADER QUOTE '"';
COPY stg_stazioni FROM '/var/lib/postgresql/csv_data/stazioniferroviarie_20210401.csv' DELIMITER ';' CSV HEADER QUOTE '"';

INSERT INTO biblioteche (nome, indirizzo, quartiere, postazioni_lettura, geom)
SELECT 
    biblioteca, indirizzo, quartiere, NULLIF(postazioni, '')::INTEGER, 
    ST_SetSRID(ST_GeomFromGeoJSON(geo_shape), 4326)
FROM stg_biblioteche;

INSERT INTO piste_ciclabili (codice, lunghezza, utilizzo, geom)
SELECT 
    codice, NULLIF(lunghezza, '')::NUMERIC, utilizzo, 
    ST_SetSRID(ST_GeomFromGeoJSON(geo_shape), 4326)
FROM stg_piste;

INSERT INTO fermate_bus (codice_fermata, linea_bus, nome_fermata, geom)
SELECT DISTINCT ON (codice_fermata)
    codice_fermata, linea_bus, nome_fermata, 
    ST_SetSRID(ST_MakePoint(
        NULLIF(TRIM(split_part(geopoint, ',', 2)), '')::FLOAT,
        NULLIF(TRIM(split_part(geopoint, ',', 1)), '')::FLOAT
    ), 4326)
FROM stg_fermate
WHERE geopoint IS NOT NULL AND geopoint != ''
ON CONFLICT (codice_fermata) DO NOTHING;

INSERT INTO aree_verdi (nome_area, tipologia, quartiere, ubicazione, geom)
SELECT 
    nome_area, tipologia_area, quartiere, ubicazione,
    ST_SetSRID(ST_GeomFromGeoJSON(geo_shape), 4326)
FROM stg_aree_verdi;

INSERT INTO residenze_universitarie (id, nome, descrizione, indirizzo, posti_letto, quartiere, url, geom)
SELECT 
    id, nome, descrizione, indirizzo, NULLIF(posti_letto, '')::INTEGER, quartiere, url,
    ST_SetSRID(ST_MakePoint(
        NULLIF(TRIM(split_part(coordinate, ',', 2)), '')::FLOAT,
        NULLIF(TRIM(split_part(coordinate, ',', 1)), '')::FLOAT
    ), 4326)
FROM stg_residenze
WHERE coordinate IS NOT NULL AND coordinate != '';

INSERT INTO stazioni_ferroviarie (codice, denominazione, ubicazione, comune, geom)
SELECT 
    codice, denominazione, ubicazione, comune,
    ST_SetSRID(ST_MakePoint(
        NULLIF(TRIM(split_part(geopoint, ',', 2)), '')::FLOAT,
        NULLIF(TRIM(split_part(geopoint, ',', 1)), '')::FLOAT
    ), 4326)
FROM stg_stazioni
WHERE geopoint IS NOT NULL AND geopoint != '';

CREATE INDEX IF NOT EXISTS idx_biblioteche_geom ON biblioteche USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_piste_geom ON piste_ciclabili USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_fermate_geom ON fermate_bus USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_aree_verdi_geom ON aree_verdi USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_residenze_geom ON residenze_universitarie USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_stazioni_geom ON stazioni_ferroviarie USING gist(geom);

CREATE TABLE IF NOT EXISTS profili_utente (
    id SERIAL PRIMARY KEY,
    nome VARCHAR(255) NOT NULL,
    peso_trasporti INTEGER DEFAULT 50 CHECK (peso_trasporti BETWEEN 0 AND 100),
    peso_biblioteche INTEGER DEFAULT 50 CHECK (peso_biblioteche BETWEEN 0 AND 100),
    peso_aree_verdi INTEGER DEFAULT 50 CHECK (peso_aree_verdi BETWEEN 0 AND 100),
    peso_mobilita_sostenibile INTEGER DEFAULT 50 CHECK (peso_mobilita_sostenibile BETWEEN 0 AND 100),
    peso_residenze INTEGER DEFAULT 50 CHECK (peso_residenze BETWEEN 0 AND 100),
    peso_mense INTEGER DEFAULT 50 CHECK (peso_mense BETWEEN 0 AND 100),
    peso_sedi INTEGER DEFAULT 50 CHECK (peso_sedi BETWEEN 0 AND 100)
);

ALTER TABLE profili_utente ADD COLUMN IF NOT EXISTS peso_mense INTEGER DEFAULT 50 CHECK (peso_mense BETWEEN 0 AND 100);
ALTER TABLE profili_utente ADD COLUMN IF NOT EXISTS peso_sedi INTEGER DEFAULT 50 CHECK (peso_sedi BETWEEN 0 AND 100);

CREATE TABLE IF NOT EXISTS suggerimenti_storico (
    id SERIAL PRIMARY KEY,
    profilo_id INTEGER REFERENCES profili_utente(id) ON DELETE SET NULL,
    lat DOUBLE PRECISION NOT NULL,
    lon DOUBLE PRECISION NOT NULL,
    ora INTEGER NOT NULL,
    punteggio INTEGER NOT NULL,
    fascia VARCHAR(50) NOT NULL,
    motivazione TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT NOW(),
    feedback VARCHAR(50)
);

INSERT INTO profili_utente (nome, peso_trasporti, peso_biblioteche, peso_aree_verdi, peso_mobilita_sostenibile, peso_residenze)
VALUES
    ('Studente Pendolare',     90, 60, 20, 30, 10),
    ('Studente Eco-Friendly',  40, 50, 80, 95, 30),
    ('Studente Fuori Sede',    70, 80, 40, 40, 90),
    ('Profilo Bilanciato',     50, 50, 50, 50, 50);

CREATE TABLE IF NOT EXISTS orari_servizi (
    id SERIAL PRIMARY KEY,
    categoria VARCHAR(50) NOT NULL,
    nome_servizio VARCHAR(255),
    giorno_settimana INTEGER NOT NULL CHECK (giorno_settimana BETWEEN 0 AND 6),
    ora_apertura TIME NOT NULL,
    ora_chiusura TIME NOT NULL
);

INSERT INTO orari_servizi (categoria, nome_servizio, giorno_settimana, ora_apertura, ora_chiusura)
SELECT 'biblioteche', nome, g.giorno,
    CASE 
        WHEN g.giorno BETWEEN 0 AND 4 THEN '08:30'::TIME
        WHEN g.giorno = 5 THEN '09:00'::TIME
        ELSE '10:00'::TIME
    END,
    CASE 
        WHEN g.giorno BETWEEN 0 AND 4 THEN '19:00'::TIME
        WHEN g.giorno = 5 THEN '13:00'::TIME
        ELSE '13:00'::TIME
    END
FROM biblioteche, generate_series(0, 5) AS g(giorno)
WHERE nome IS NOT NULL;

INSERT INTO orari_servizi (categoria, nome_servizio, giorno_settimana, ora_apertura, ora_chiusura)
SELECT 'fermate', 'Servizio TPER', g.giorno,
    CASE WHEN g.giorno <= 5 THEN '05:30'::TIME ELSE '06:30'::TIME END,
    CASE WHEN g.giorno <= 4 THEN '00:30'::TIME WHEN g.giorno = 5 THEN '02:00'::TIME ELSE '23:00'::TIME END
FROM generate_series(0, 6) AS g(giorno);

-- ============================================================================
-- GTFS TPER: fermate reali + frequenza corse per fascia oraria (giorno feriale tipo)
-- ============================================================================

CREATE TABLE IF NOT EXISTS gtfs_fermate (
    stop_id VARCHAR(50) PRIMARY KEY,
    nome VARCHAR(255),
    geom GEOMETRY(Point, 4326)
);

CREATE TABLE IF NOT EXISTS gtfs_frequenze_fermata (
    id SERIAL PRIMARY KEY,
    stop_id VARCHAR(50) REFERENCES gtfs_fermate(stop_id),
    fascia_oraria INTEGER NOT NULL CHECK (fascia_oraria BETWEEN 0 AND 23),
    numero_corse INTEGER NOT NULL,
    UNIQUE(stop_id, fascia_oraria)
);

CREATE TEMP TABLE stg_gtfs_stops (
    stop_id TEXT, stop_name TEXT, stop_lat TEXT, stop_lon TEXT, location_type TEXT, parent_station TEXT
);
CREATE TEMP TABLE stg_gtfs_trips (
    route_id TEXT, service_id TEXT, trip_id TEXT, trip_headsign TEXT, direction_id TEXT, shape_id TEXT, trip_short_name TEXT
);
CREATE TEMP TABLE stg_gtfs_stop_times (
    trip_id TEXT, arrival_time TEXT, departure_time TEXT, stop_id TEXT, stop_sequence TEXT
);
CREATE TEMP TABLE stg_gtfs_calendar (
    service_id TEXT, monday TEXT, tuesday TEXT, wednesday TEXT, thursday TEXT, friday TEXT, saturday TEXT, sunday TEXT, start_date TEXT, end_date TEXT
);

COPY stg_gtfs_stops      FROM '/var/lib/postgresql/csv_data/gtfs/stops.txt'      DELIMITER ',' CSV HEADER QUOTE '"';
COPY stg_gtfs_trips      FROM '/var/lib/postgresql/csv_data/gtfs/trips.txt'      DELIMITER ',' CSV HEADER QUOTE '"';
COPY stg_gtfs_stop_times FROM '/var/lib/postgresql/csv_data/gtfs/stop_times.txt' DELIMITER ',' CSV HEADER QUOTE '"';
COPY stg_gtfs_calendar   FROM '/var/lib/postgresql/csv_data/gtfs/calendar.txt'   DELIMITER ',' CSV HEADER QUOTE '"';

-- Fermate con coordinate valide (location_type='0' = fermata reale, non stazione aggregata)
INSERT INTO gtfs_fermate (stop_id, nome, geom)
SELECT stop_id, stop_name,
    ST_SetSRID(ST_MakePoint(NULLIF(stop_lon,'')::FLOAT, NULLIF(stop_lat,'')::FLOAT), 4326)
FROM stg_gtfs_stops
WHERE location_type = '0'
  AND NULLIF(stop_lat,'') IS NOT NULL AND NULLIF(stop_lon,'') IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_gtfs_fermate_geom ON gtfs_fermate USING gist(geom);

-- Service_id attivi tutti i 5 giorni lavorativi = "giorno feriale tipo"
CREATE TEMP TABLE feriali AS
SELECT service_id FROM stg_gtfs_calendar
WHERE monday='1' AND tuesday='1' AND wednesday='1' AND thursday='1' AND friday='1';

-- Numero di corse per fermata per ogni fascia oraria (0-23), giorno feriale tipo.
-- NB: arrival_time GTFS può superare "24:00:00" (corse dopo mezzanotte sul servizio del giorno
-- prima) quindi si usa %% 24 per normalizzare nella fascia corretta.
INSERT INTO gtfs_frequenze_fermata (stop_id, fascia_oraria, numero_corse)
SELECT st.stop_id,
       (SPLIT_PART(st.arrival_time, ':', 1)::INT) % 24 AS fascia_oraria,
       COUNT(DISTINCT st.trip_id) AS numero_corse
FROM stg_gtfs_stop_times st
JOIN stg_gtfs_trips t ON st.trip_id = t.trip_id
JOIN feriali f ON t.service_id = f.service_id
WHERE st.stop_id IN (SELECT stop_id FROM gtfs_fermate)
GROUP BY st.stop_id, fascia_oraria;