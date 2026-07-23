-- Restricted runtime role. The app connects as this, never as the owner
-- (seamline) that runs migrations. REVOKE on the owner role would be a
-- no-op — owners bypass grants in PostgreSQL — so this second role is what
-- makes the trade_history immutability guarantee in ADR-0006 real rather
-- than aspirational. Schema/table grants happen per-migration once the
-- tables exist; this script only creates the role.
CREATE ROLE seamline_app WITH LOGIN PASSWORD 'seamline_app';
GRANT CONNECT ON DATABASE seamline TO seamline_app;
