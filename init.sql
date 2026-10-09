-- ============================================================================
-- init.sql - Script di inizializzazione del database PostGIS
-- Configurazione del DB:
-- 1. Attivazione estensione PostGIS.
-- 2. Creazione tabelle definitive con tipologie geometriche (Point, MultiLineString, MultiPoint)
--    e colonna 'geog' (geography, generata da geom) per distanze e buffer in metri.
-- 3. Creazione tabelle di staging per parsing CSV.
-- 4. Importazione dati massiva tramite comando COPY.
-- 5. ETL spaziale: trasformazione dati grezzi in geometrie SRID 4326.
-- 6. Creazione indici spaziali GiST (su geom e geog) per l'ottimizzazione delle query.
-- 7. Popolamento tabelle di supporto: profili utente e orari servizi.
-- ============================================================================

CREATE EXTENSION IF NOT EXISTS postgis;

CREATE TABLE IF NOT EXISTS biblioteche (
    id SERIAL PRIMARY KEY,
    nome VARCHAR(255),
    indirizzo VARCHAR(255),
    quartiere VARCHAR(255),
    postazioni_lettura INTEGER,
    geom GEOMETRY(MultiPoint, 4326),
    geog GEOGRAPHY GENERATED ALWAYS AS (geom::geography) STORED
);

CREATE TABLE IF NOT EXISTS piste_ciclabili (
    id SERIAL PRIMARY KEY,
    codice VARCHAR(50),
    lunghezza NUMERIC,
    utilizzo VARCHAR(100),
    geom GEOMETRY(MultiLineString, 4326),
    geog GEOGRAPHY GENERATED ALWAYS AS (geom::geography) STORED
);

CREATE TABLE IF NOT EXISTS fermate_bus (
    codice_fermata VARCHAR(50) PRIMARY KEY,
    linea_bus VARCHAR(255),
    nome_fermata VARCHAR(255),
    geom GEOMETRY(Point, 4326),
    geog GEOGRAPHY GENERATED ALWAYS AS (geom::geography) STORED
);

CREATE TABLE IF NOT EXISTS aree_verdi (
    id SERIAL PRIMARY KEY,
    nome_area VARCHAR(255),
    tipologia VARCHAR(255),
    quartiere VARCHAR(255),
    ubicazione VARCHAR(255),
    geom GEOMETRY(Point, 4326),
    geog GEOGRAPHY GENERATED ALWAYS AS (geom::geography) STORED
);

CREATE TABLE IF NOT EXISTS residenze_universitarie (
    id VARCHAR(50) PRIMARY KEY,
    nome VARCHAR(255),
    descrizione TEXT,
    indirizzo VARCHAR(255),
    posti_letto INTEGER,
    quartiere VARCHAR(255),
    url TEXT,
    geom GEOMETRY(Point, 4326),
    geog GEOGRAPHY GENERATED ALWAYS AS (geom::geography) STORED
);

CREATE TABLE IF NOT EXISTS stazioni_ferroviarie (
    codice VARCHAR(50) PRIMARY KEY,
    denominazione VARCHAR(255),
    ubicazione VARCHAR(255),
    comune VARCHAR(100),
    geom GEOMETRY(Point, 4326),
    geog GEOGRAPHY GENERATED ALWAYS AS (geom::geography) STORED
);

CREATE TABLE IF NOT EXISTS sedi_universitarie (
    id SERIAL PRIMARY KEY,
    tipo VARCHAR(50),      -- 'unibo' (dipartimenti/uffici/laboratori) o 'museo' (Sistema Museale Ateneo)
    nome TEXT,
    indirizzo VARCHAR(255),
    url TEXT,
    geom GEOMETRY(Point, 4326),
    geog GEOGRAPHY GENERATED ALWAYS AS (geom::geography) STORED
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
CREATE INDEX IF NOT EXISTS idx_sedi_universitarie_geog ON sedi_universitarie USING gist(geog);

CREATE TABLE IF NOT EXISTS mense (
    id SERIAL PRIMARY KEY,
    nome VARCHAR(255),
    indirizzo VARCHAR(255),
    tipo VARCHAR(50),       -- 'mensa' (pasto completo) o 'punto_ristoro' (self/microonde)
    gestore VARCHAR(255),
    geom GEOMETRY(Point, 4326),
    geog GEOGRAPHY GENERATED ALWAYS AS (geom::geography) STORED
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
CREATE INDEX IF NOT EXISTS idx_mense_geog ON mense USING gist(geog);

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
CREATE INDEX IF NOT EXISTS idx_biblioteche_geog ON biblioteche USING gist(geog);
CREATE INDEX IF NOT EXISTS idx_piste_geog ON piste_ciclabili USING gist(geog);
CREATE INDEX IF NOT EXISTS idx_fermate_geog ON fermate_bus USING gist(geog);
CREATE INDEX IF NOT EXISTS idx_aree_verdi_geog ON aree_verdi USING gist(geog);
CREATE INDEX IF NOT EXISTS idx_residenze_geog ON residenze_universitarie USING gist(geog);
CREATE INDEX IF NOT EXISTS idx_stazioni_geog ON stazioni_ferroviarie USING gist(geog);

-- ============================================================================
-- Sale studio e biblioteche universitarie:
-- 1) biblioteche di Ateneo estratte dai Punti di interesse Unibo (mappe.csv),
--    un punto per edificio, nome = la voce "Biblioteca ..." dell'elenco;
-- 2) import opzionale di altre sale studio da data/sale-studio.csv (formato
--    normalizzato, separatore ';', intestazione: nome;indirizzo;lat;lon;posti;fonte).
--    Se il file non esiste l'inizializzazione prosegue.
-- ============================================================================

CREATE TABLE IF NOT EXISTS sale_studio (
    id SERIAL PRIMARY KEY,
    nome VARCHAR(255),
    indirizzo VARCHAR(255),
    posti INTEGER,
    fonte VARCHAR(255),
    geom GEOMETRY(Point, 4326),
    geog GEOGRAPHY GENERATED ALWAYS AS (geom::geography) STORED
);

CREATE TEMP TABLE stg_sale_studio (
    nome TEXT, indirizzo TEXT, lat TEXT, lon TEXT, posti TEXT, fonte TEXT
);

DO $$
BEGIN
    COPY stg_sale_studio FROM '/var/lib/postgresql/csv_data/sale-studio.csv' DELIMITER ';' CSV HEADER QUOTE '"';
EXCEPTION WHEN undefined_file THEN
    RAISE NOTICE 'sale-studio.csv non trovato: tabella sale_studio lasciata vuota';
END $$;

INSERT INTO sale_studio (nome, indirizzo, posti, fonte, geom)
SELECT nome, indirizzo, NULLIF(TRIM(posti), '')::INTEGER, fonte,
    ST_SetSRID(ST_MakePoint(REPLACE(TRIM(lon), ',', '.')::FLOAT, REPLACE(TRIM(lat), ',', '.')::FLOAT), 4326)
FROM stg_sale_studio
WHERE NULLIF(TRIM(lat), '') IS NOT NULL AND NULLIF(TRIM(lon), '') IS NOT NULL;

INSERT INTO sale_studio (nome, indirizzo, posti, fonte, geom)
SELECT DISTINCT ON (ROUND(lat::NUMERIC, 4), ROUND(lon::NUMERIC, 4))
    TRIM(SUBSTRING(name FROM '(Biblioteca[^;]*)')),
    address, NULL, 'dati.unibo.it - Punti di interesse',
    ST_SetSRID(ST_MakePoint(lon::FLOAT, lat::FLOAT), 4326)
FROM stg_sedi_universitarie
WHERE city = 'Bologna'
  AND name ~ 'Settore Biblioteca'
  AND NULLIF(lat, '')::FLOAT IS NOT NULL AND NULLIF(lat, '')::FLOAT != 0
  AND NULLIF(lon, '')::FLOAT IS NOT NULL AND NULLIF(lon, '')::FLOAT != 0
ORDER BY ROUND(lat::NUMERIC, 4), ROUND(lon::NUMERIC, 4), name;

CREATE INDEX IF NOT EXISTS idx_sale_studio_geom ON sale_studio USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_sale_studio_geog ON sale_studio USING gist(geog);

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
    giorno INTEGER CHECK (giorno BETWEEN 0 AND 6),
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
-- GTFS TPER: fermate, corse e orari di passaggio per tipo di giorno
-- (feriale / sabato / festivo), frequenza per fascia oraria.
-- Per ogni tipo di giorno si sceglie una DATA DI RIFERIMENTO reale: tra i primi
-- 60 giorni del calendario, quella di quel tipo con il maggior numero di corse
-- (esclude festivita' e periodi ridotti). Una corsa e' attiva se il suo servizio
-- lo e' in quella data secondo calendar.txt e le eccezioni di calendar_dates.txt.
-- Se calendar_dates.txt manca, varianti dello stesso servizio risultano attive
-- insieme: per ogni linea e tipo di giorno si tiene allora solo il service_id
-- con piu' corse, per non contare la stessa corsa piu' volte.
-- ============================================================================

CREATE TABLE IF NOT EXISTS gtfs_fermate (
    stop_id VARCHAR(50) PRIMARY KEY,
    nome VARCHAR(255),
    geom GEOMETRY(Point, 4326),
    geog GEOGRAPHY GENERATED ALWAYS AS (geom::geography) STORED
);

CREATE TABLE IF NOT EXISTS gtfs_trips (
    trip_id VARCHAR(100) PRIMARY KEY,
    route_id VARCHAR(50),
    feriale BOOLEAN NOT NULL,
    sabato BOOLEAN NOT NULL,
    festivo BOOLEAN NOT NULL
);

CREATE TABLE IF NOT EXISTS gtfs_stop_times (
    trip_id VARCHAR(100) NOT NULL,
    stop_id VARCHAR(50) NOT NULL,
    stop_sequence INTEGER NOT NULL,
    arr_sec INTEGER NOT NULL,
    dep_sec INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS gtfs_frequenze_fermata (
    id SERIAL PRIMARY KEY,
    stop_id VARCHAR(50) REFERENCES gtfs_fermate(stop_id),
    tipo_giorno VARCHAR(10) NOT NULL CHECK (tipo_giorno IN ('feriale', 'sabato', 'festivo')),
    fascia_oraria INTEGER NOT NULL CHECK (fascia_oraria BETWEEN 0 AND 23),
    numero_corse INTEGER NOT NULL,
    UNIQUE(stop_id, tipo_giorno, fascia_oraria)
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

INSERT INTO gtfs_fermate (stop_id, nome, geom)
SELECT stop_id, stop_name,
    ST_SetSRID(ST_MakePoint(NULLIF(stop_lon,'')::FLOAT, NULLIF(stop_lat,'')::FLOAT), 4326)
FROM stg_gtfs_stops
WHERE location_type = '0'
  AND NULLIF(stop_lat,'') IS NOT NULL AND NULLIF(stop_lon,'') IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_gtfs_fermate_geom ON gtfs_fermate USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_gtfs_fermate_geog ON gtfs_fermate USING gist(geog);

CREATE TEMP TABLE stg_gtfs_calendar_dates (
    service_id TEXT, date TEXT, exception_type TEXT
);

DO $$
BEGIN
    COPY stg_gtfs_calendar_dates FROM '/var/lib/postgresql/csv_data/gtfs/calendar_dates.txt' DELIMITER ',' CSV HEADER QUOTE '"';
EXCEPTION WHEN undefined_file THEN
    RAISE NOTICE 'calendar_dates.txt non trovato: si usa un solo service_id per linea e tipo di giorno';
END $$;

CREATE TEMP TABLE cal AS
SELECT service_id,
    ARRAY[monday='1', tuesday='1', wednesday='1', thursday='1', friday='1', saturday='1', sunday='1'] AS giorni,
    TO_DATE(start_date, 'YYYYMMDD') AS inizio,
    TO_DATE(end_date, 'YYYYMMDD') AS fine
FROM stg_gtfs_calendar;

CREATE TEMP TABLE date_candidate AS
SELECT d::DATE AS giorno
FROM generate_series((SELECT MIN(inizio) FROM cal), (SELECT MIN(inizio) FROM cal) + 60, INTERVAL '1 day') AS d;

CREATE TEMP TABLE servizio_giorno AS
SELECT dc.giorno, c.service_id
FROM date_candidate dc
JOIN cal c ON dc.giorno BETWEEN c.inizio AND c.fine AND c.giorni[EXTRACT(ISODOW FROM dc.giorno)::INT]
WHERE NOT EXISTS (SELECT 1 FROM stg_gtfs_calendar_dates x
                  WHERE x.service_id = c.service_id AND x.date = TO_CHAR(dc.giorno, 'YYYYMMDD') AND x.exception_type = '2')
UNION
SELECT TO_DATE(x.date, 'YYYYMMDD'), x.service_id
FROM stg_gtfs_calendar_dates x
WHERE x.exception_type = '1' AND TO_DATE(x.date, 'YYYYMMDD') IN (SELECT giorno FROM date_candidate);

CREATE TABLE IF NOT EXISTS gtfs_giorni_riferimento (
    tipo_giorno VARCHAR(10) PRIMARY KEY,
    giorno DATE NOT NULL
);

INSERT INTO gtfs_giorni_riferimento (tipo_giorno, giorno)
SELECT DISTINCT ON (tipo) tipo, giorno
FROM (
    SELECT CASE EXTRACT(ISODOW FROM sg.giorno) WHEN 7 THEN 'festivo' WHEN 6 THEN 'sabato' ELSE 'feriale' END AS tipo,
           sg.giorno, COUNT(t.trip_id) AS corse
    FROM servizio_giorno sg
    JOIN stg_gtfs_trips t ON t.service_id = sg.service_id
    GROUP BY 1, 2
) z
ORDER BY tipo, corse DESC, giorno;

CREATE TEMP TABLE trip_tipo AS
SELECT DISTINCT t.trip_id, t.route_id, t.service_id, r.tipo_giorno AS tipo
FROM stg_gtfs_trips t
JOIN servizio_giorno sg ON sg.service_id = t.service_id
JOIN gtfs_giorni_riferimento r ON r.giorno = sg.giorno;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM stg_gtfs_calendar_dates) THEN
        CREATE TEMP TABLE servizio_dominante AS
        SELECT DISTINCT ON (route_id, tipo) route_id, tipo, service_id
        FROM (SELECT route_id, tipo, service_id, COUNT(*) AS corse FROM trip_tipo GROUP BY 1, 2, 3) z
        ORDER BY route_id, tipo, corse DESC, service_id;

        DELETE FROM trip_tipo tt
        USING servizio_dominante d
        WHERE tt.route_id = d.route_id AND tt.tipo = d.tipo AND tt.service_id <> d.service_id;
    END IF;
END $$;

INSERT INTO gtfs_trips (trip_id, route_id, feriale, sabato, festivo)
SELECT trip_id, MAX(route_id), BOOL_OR(tipo = 'feriale'), BOOL_OR(tipo = 'sabato'), BOOL_OR(tipo = 'festivo')
FROM trip_tipo
GROUP BY trip_id;

INSERT INTO gtfs_stop_times (trip_id, stop_id, stop_sequence, arr_sec, dep_sec)
SELECT st.trip_id, st.stop_id, st.stop_sequence::INT,
    SPLIT_PART(st.arrival_time, ':', 1)::INT * 3600 + SPLIT_PART(st.arrival_time, ':', 2)::INT * 60 + SPLIT_PART(st.arrival_time, ':', 3)::INT,
    SPLIT_PART(st.departure_time, ':', 1)::INT * 3600 + SPLIT_PART(st.departure_time, ':', 2)::INT * 60 + SPLIT_PART(st.departure_time, ':', 3)::INT
FROM stg_gtfs_stop_times st
WHERE st.trip_id IN (SELECT trip_id FROM gtfs_trips)
  AND st.stop_id IN (SELECT stop_id FROM gtfs_fermate)
  AND NULLIF(st.departure_time, '') IS NOT NULL AND NULLIF(st.arrival_time, '') IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_gtfs_stop_times_stop ON gtfs_stop_times (stop_id);
CREATE INDEX IF NOT EXISTS idx_gtfs_stop_times_trip ON gtfs_stop_times (trip_id, stop_sequence);

-- Numero di corse distinte per fermata, tipo di giorno e fascia oraria (0-23).
-- Gli orari GTFS possono superare le 24:00 (corse dopo mezzanotte): si normalizza con % 24.
INSERT INTO gtfs_frequenze_fermata (stop_id, tipo_giorno, fascia_oraria, numero_corse)
SELECT st.stop_id, g.tipo, (st.dep_sec / 3600) % 24, COUNT(DISTINCT st.trip_id)
FROM gtfs_stop_times st
JOIN gtfs_trips t ON t.trip_id = st.trip_id
CROSS JOIN LATERAL (VALUES ('feriale', t.feriale), ('sabato', t.sabato), ('festivo', t.festivo)) AS g(tipo, attivo)
WHERE g.attivo
GROUP BY st.stop_id, g.tipo, (st.dep_sec / 3600) % 24;

CREATE INDEX IF NOT EXISTS idx_gtfs_frequenze ON gtfs_frequenze_fermata (tipo_giorno, fascia_oraria, stop_id);

-- ============================================================================
-- Vista unificata dei PoI puntuali (per recall dei servizi vicini e indicatori per cella).
-- ============================================================================

CREATE OR REPLACE VIEW poi_tutti AS
SELECT 'biblioteche'::TEXT AS categoria, id::TEXT AS id, nome::TEXT AS nome, geom::GEOMETRY AS geom, geog FROM biblioteche
UNION ALL SELECT 'salestudio', id::TEXT, nome, geom::GEOMETRY, geog FROM sale_studio
UNION ALL SELECT 'fermate', codice_fermata, nome_fermata, geom::GEOMETRY, geog FROM fermate_bus
UNION ALL SELECT 'areeverdi', id::TEXT, nome_area, geom::GEOMETRY, geog FROM aree_verdi
UNION ALL SELECT 'residenze', id, nome, geom::GEOMETRY, geog FROM residenze_universitarie
UNION ALL SELECT 'stazioni', codice, denominazione, geom::GEOMETRY, geog FROM stazioni_ferroviarie
UNION ALL SELECT 'mense', id::TEXT, nome, geom::GEOMETRY, geog FROM mense
UNION ALL SELECT 'sedi', id::TEXT, nome, geom::GEOMETRY, geog FROM sedi_universitarie;