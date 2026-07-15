-- Abilitiamo l'estensione spaziale PostGIS se non è già attiva
CREATE EXTENSION IF NOT EXISTS postgis;

-- ============================================================================
-- 1. CREAZIONE TABELLE REALI (Definitive con indici spaziali)
-- ============================================================================

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
    id SERIAL PRIMARY KEY,
    codice_fermata VARCHAR(50),
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

-- ============================================================================
-- 2. CREAZIONE TABELLE DI STAGING (Temporanee, mappano i CSV al 100%)
-- ============================================================================

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

-- ============================================================================
-- 3. IMPORTAZIONE DEI DATI GREZZI DAI FILE CSV
-- ============================================================================

COPY stg_biblioteche FROM '/var/lib/postgresql/csv_data/biblioteche-comunali-di-bologna.csv' DELIMITER ';' CSV HEADER QUOTE '"';
COPY stg_piste FROM '/var/lib/postgresql/csv_data/piste-ciclopedonali.csv' DELIMITER ';' CSV HEADER QUOTE '"';
COPY stg_fermate FROM '/var/lib/postgresql/csv_data/tper-fermate-autobus.csv' DELIMITER ';' CSV HEADER QUOTE '"';
COPY stg_aree_verdi FROM '/var/lib/postgresql/csv_data/aree-verdi_entrate_centroidi.csv' DELIMITER ';' CSV HEADER QUOTE '"';
COPY stg_residenze FROM '/var/lib/postgresql/csv_data/residenze-universitarie.csv' DELIMITER ';' CSV HEADER QUOTE '"';
COPY stg_stazioni FROM '/var/lib/postgresql/csv_data/stazioniferroviarie_20210401.csv' DELIMITER ';' CSV HEADER QUOTE '"';

-- ============================================================================
-- 4. ELABORAZIONE SPAZIALE E POPOLAMENTO TABELLE REALI (ETL)
-- ============================================================================

-- A) Biblioteche (da GeoJSON MultiPoint)
INSERT INTO biblioteche (nome, indirizzo, quartiere, postazioni_lettura, geom)
SELECT 
    biblioteca, indirizzo, quartiere, NULLIF(postazioni, '')::INTEGER, 
    ST_SetSRID(ST_GeomFromGeoJSON(geo_shape), 4326)
FROM stg_biblioteche;

-- B) Piste Ciclabili (da GeoJSON MultiLineString)
INSERT INTO piste_ciclabili (codice, lunghezza, utilizzo, geom)
SELECT 
    codice, NULLIF(lunghezza, '')::NUMERIC, utilizzo, 
    ST_SetSRID(ST_GeomFromGeoJSON(geo_shape), 4326)
FROM stg_piste;

-- C) Fermate Bus (da stringa Coordinate "Lat, Lon")
INSERT INTO fermate_bus (codice_fermata, linea_bus, nome_fermata, geom)
SELECT 
    codice_fermata, linea_bus, nome_fermata, 
    ST_SetSRID(ST_MakePoint(
        NULLIF(TRIM(split_part(geopoint, ',', 2)), '')::FLOAT, -- Longitudine
        NULLIF(TRIM(split_part(geopoint, ',', 1)), '')::FLOAT  -- Latitudine
    ), 4326)
FROM stg_fermate
WHERE geopoint IS NOT NULL AND geopoint != '';

-- D) Aree Verdi (da GeoJSON Point)
INSERT INTO aree_verdi (nome_area, tipologia, quartiere, ubicazione, geom)
SELECT 
    nome_area, tipologia_area, quartiere, ubicazione,
    ST_SetSRID(ST_GeomFromGeoJSON(geo_shape), 4326)
FROM stg_aree_verdi;

-- E) Residenze Universitarie (da stringa Coordinate "Lat, Lon")
INSERT INTO residenze_universitarie (id, nome, descrizione, indirizzo, posti_letto, quartiere, url, geom)
SELECT 
    id, nome, descrizione, indirizzo, NULLIF(posti_letto, '')::INTEGER, quartiere, url,
    ST_SetSRID(ST_MakePoint(
        NULLIF(TRIM(split_part(coordinate, ',', 2)), '')::FLOAT, -- Longitudine
        NULLIF(TRIM(split_part(coordinate, ',', 1)), '')::FLOAT  -- Latitudine
    ), 4326)
FROM stg_residenze
WHERE coordinate IS NOT NULL AND coordinate != '';

-- F) Stazioni Ferroviarie (da stringa Coordinate "Lat, Lon")
INSERT INTO stazioni_ferroviarie (codice, denominazione, ubicazione, comune, geom)
SELECT 
    codice, denominazione, ubicazione, comune,
    ST_SetSRID(ST_MakePoint(
        NULLIF(TRIM(split_part(geopoint, ',', 2)), '')::FLOAT, -- Longitudine
        NULLIF(TRIM(split_part(geopoint, ',', 1)), '')::FLOAT  -- Latitudine
    ), 4326)
FROM stg_stazioni
WHERE geopoint IS NOT NULL AND geopoint != '';

-- ============================================================================
-- 5. OTTIMIZZAZIONE (Creazione Indici Spaziali GiST)
-- ============================================================================
CREATE INDEX IF NOT EXISTS idx_biblioteche_geom ON biblioteche USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_piste_geom ON piste_ciclabili USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_fermate_geom ON fermate_bus USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_aree_verdi_geom ON aree_verdi USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_residenze_geom ON residenze_universitarie USING gist(geom);
CREATE INDEX IF NOT EXISTS idx_stazioni_geom ON stazioni_ferroviarie USING gist(geom);