-- Weir schema 0026 (revision 0061_user_app_theme): the signed-in colour theme, per account rather than
-- per browser. Null means nobody has a saved preference yet; every existing user starts there after
-- this migration. The client, not this migration, adopts a browser's existing choice onto the
-- account the first time that browser signs in afterwards, so nobody's current choice is lost.
ALTER TABLE users ADD COLUMN app_theme TEXT;
