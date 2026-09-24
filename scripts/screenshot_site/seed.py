"""Seeds a running Weir server's SQLite database with representative data for the "seeded"
screenshot scenario. Split out of the main script (#747); the actual seed statements live in
``seed_catalog``, ``seed_library_view`` and ``seed_activity``, called here inside one transaction
so a partial seed can never be mistaken for a complete one.
"""

from __future__ import annotations

import sqlite3

from tests.e2e.weir.utils import db_path_for_home

from . import seed_activity, seed_catalog, seed_library_view


def seed_representative_data(home: str) -> None:
    """Plain SQL against the server's own SQLite file — no fixtures added to the product.

    Follows tests/e2e/weir/utils.py's pattern: the server keeps running and does not cache these
    rows, so they show up the same way a second operator's process writing to the same file would.
    """

    conn = sqlite3.connect(str(db_path_for_home(home)), timeout=30)
    conn.execute("PRAGMA busy_timeout = 30000")
    try:
        with conn:
            seed_catalog.seed_libraries(conn)
            seed_catalog.seed_files(conn)
            seed_catalog.seed_jobs(conn)
            seed_library_view.seed_library_files(conn)
            seed_activity.seed_activity_events(conn)
            seed_activity.seed_notifications_and_connections(conn)
    finally:
        conn.close()
