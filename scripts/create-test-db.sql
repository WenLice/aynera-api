\set ON_ERROR_STOP on

-- Run with psql as a database administrator, connected to postgres.
-- The application role must already exist. Existing databases are left untouched.
SELECT 'CREATE DATABASE aynera_test OWNER aynera'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'aynera_test')
\gexec

SELECT datname AS database, pg_get_userbyid(datdba) AS owner
FROM pg_database
WHERE datname = 'aynera_test';
