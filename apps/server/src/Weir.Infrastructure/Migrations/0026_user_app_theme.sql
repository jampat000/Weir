-- Weir schema 0026 (revision 0061_user_app_theme): the signed-in colour theme, per account rather than
-- per browser (#790). Null means nobody has picked one yet, so the app keeps following the system
-- setting; an upgrade leaves every existing user at null rather than guessing from what one browser
-- had stored locally.
ALTER TABLE users ADD COLUMN app_theme TEXT;
