"""The catalogue of screens ``screenshot-site.py`` captures, addressed by URL — no clicking
through the UI. Split out of the main script (#747) as its own real boundary: pure data.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass
class Screen:
    """One screen to capture, addressed by its own URL — no clicking through the UI."""

    index: int
    slug: str
    path: str
    ready_selector: str
    label: str


# The three screens that only exist for a moment in a fresh install's lifecycle (first-run setup,
# a pre-session login, and the once-pending setup wizard). Captured with their own bespoke steps in
# run_scenario() rather than the plain goto-and-shoot loop below, but listed here too so the contact
# sheet can show them in the same review order as everything else.
GATED_SCREENS: list[Screen] = [
    Screen(1, "setup", "/setup", '[data-testid="setup-form"]', "First-run setup"),
    Screen(2, "login", "/login", '[data-testid="login-form"]', "Login"),
    Screen(3, "setup-wizard", "/setup-wizard", '[data-testid="setup-wizard-skip"]', "Setup wizard"),
]

# Every other screen the app has, in review order. Tabs and sub-views are addressed by their own
# query params (`?tab=`, `?view=`), exactly as an operator's bookmark would.
NORMAL_SCREENS: list[Screen] = [
    Screen(4, "home", "/", '[data-testid="shell-ready"]', "Home"),
    Screen(5, "activity", "/activity", '[data-testid="activity-feed"]', "Activity"),
    Screen(
        6,
        "processing-overview",
        "/processing?tab=overview",
        '[data-testid="processing-overview-panel"]',
        "Processing - Overview",
    ),
    Screen(
        7,
        "processing-libraries",
        "/processing?tab=libraries",
        '[data-testid="processing-libraries-section"]',
        "Processing - Libraries",
    ),
    Screen(
        8,
        "processing-audio-subtitles",
        "/processing?tab=audio-subtitles",
        '[data-testid="processing-rule-set-workspace"]',
        "Processing - Audio & subtitles",
    ),
    Screen(
        9,
        "processing-schedules",
        "/processing?tab=schedules",
        '[data-testid="processing-schedules-section"]',
        "Processing - Schedules",
    ),
    Screen(
        10,
        "processing-files",
        "/processing?tab=files",
        '[data-testid="processing-files-section"]',
        "Processing - Files",
    ),
    Screen(
        11,
        "processing-library-overview",
        "/processing?tab=library&view=overview",
        '[data-testid="processing-scope-page"]',
        "Processing - Library - Overview",
    ),
    Screen(
        12,
        "processing-library-files",
        "/processing?tab=library&view=files",
        '[data-testid="library-files-section"]',
        "Processing - Library - Files",
    ),
    Screen(
        13,
        "processing-library-codecs",
        "/processing?tab=library&view=codecs",
        '[data-testid="processing-scope-page"]',
        "Processing - Library - Codecs",
    ),
    Screen(
        14,
        "processing-library-languages",
        "/processing?tab=library&view=languages",
        '[data-testid="processing-scope-page"]',
        "Processing - Library - Languages",
    ),
    Screen(
        15,
        "processing-library-problems",
        "/processing?tab=library&view=problems",
        '[data-testid="processing-scope-page"]',
        "Processing - Library - Problems",
    ),
    Screen(
        16,
        "processing-jobs",
        "/processing?tab=jobs",
        '[data-testid="processing-jobs-inspection-section"]',
        "Processing - Jobs",
    ),
    Screen(
        17,
        "processing-maintenance",
        "/processing?tab=maintenance",
        '[data-testid="processing-maintenance-section"]',
        "Processing - Maintenance",
    ),
    Screen(18, "settings", "/settings", '[data-testid="suite-settings-page"]', "Settings - General"),
    Screen(
        19,
        "settings-security",
        "/settings?tab=security",
        '[data-testid="suite-settings-security"]',
        "Settings - Security",
    ),
    Screen(
        20,
        "settings-backup",
        "/settings?tab=backup",
        '[data-testid="suite-settings-backup-tab"]',
        "Settings - Backup and restore",
    ),
    Screen(
        21,
        "settings-upgrade",
        "/settings?tab=upgrade",
        '[data-testid="suite-settings-upgrade-tab"]',
        "Settings - Upgrade",
    ),
    Screen(
        22,
        "settings-logs",
        "/settings?tab=logs",
        '[data-testid="suite-settings-logs"]',
        "Settings - Logs",
    ),
    Screen(
        23,
        "settings-notifications",
        "/settings?tab=notifications",
        '[data-testid="suite-settings-notifications"]',
        "Settings - Notifications",
    ),
    Screen(
        24,
        "settings-media-managers",
        "/settings?tab=media-managers",
        '[data-testid="media-manager-add"]',
        "Settings - Media managers",
    ),
    Screen(
        25,
        "not-found",
        "/this-page-does-not-exist-weir-screenshot-harness",
        "text=This page doesn't exist.",
        "Not found",
    ),
]

ALL_SCREENS: list[Screen] = GATED_SCREENS + NORMAL_SCREENS
