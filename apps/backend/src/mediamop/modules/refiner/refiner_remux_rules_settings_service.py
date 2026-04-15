"""Singleton remux rules row — load/save and map to :class:`RefinerRulesConfig`."""

from __future__ import annotations

from sqlalchemy import select
from sqlalchemy.orm import Session

from mediamop.modules.refiner.refiner_remux_rules_settings_model import RefinerRemuxRulesSettingsRow
from mediamop.modules.refiner.refiner_remux_rules import (
    RefinerRulesConfig,
    normalize_audio_preference_mode,
    parse_subtitle_langs_csv,
)
from mediamop.modules.refiner.refiner_remux_rules import default_refiner_remux_rules_config
from mediamop.modules.refiner.schemas_refiner_remux_rules_settings import (
    RefinerRemuxRulesSettingsOut,
    RefinerRemuxRulesSettingsPutIn,
)


def ensure_refiner_remux_rules_settings_row(db: Session) -> RefinerRemuxRulesSettingsRow:
    row = db.scalars(select(RefinerRemuxRulesSettingsRow).where(RefinerRemuxRulesSettingsRow.id == 1)).one_or_none()
    if row is None:
        d = default_refiner_remux_rules_config()
        langs = ",".join(d.subtitle_langs)
        row = RefinerRemuxRulesSettingsRow(
            id=1,
            primary_audio_lang=d.primary_audio_lang,
            secondary_audio_lang=d.secondary_audio_lang,
            tertiary_audio_lang=d.tertiary_audio_lang,
            default_audio_slot=d.default_audio_slot,
            remove_commentary=1 if d.remove_commentary else 0,
            subtitle_mode=d.subtitle_mode,
            subtitle_langs_csv=langs,
            preserve_forced_subs=1 if d.preserve_forced_subs else 0,
            preserve_default_subs=1 if d.preserve_default_subs else 0,
            audio_preference_mode=d.audio_preference_mode,
        )
        db.add(row)
        db.flush()
    return row


def row_to_rules_config(row: RefinerRemuxRulesSettingsRow) -> RefinerRulesConfig:
    return RefinerRulesConfig(
        primary_audio_lang=(row.primary_audio_lang or "").strip() or "eng",
        secondary_audio_lang=(row.secondary_audio_lang or "").strip(),
        tertiary_audio_lang=(row.tertiary_audio_lang or "").strip(),
        default_audio_slot=row.default_audio_slot if row.default_audio_slot in ("primary", "secondary") else "primary",  # type: ignore[arg-type]
        remove_commentary=bool(row.remove_commentary),
        subtitle_mode=row.subtitle_mode if row.subtitle_mode in ("remove_all", "keep_selected") else "remove_all",  # type: ignore[arg-type]
        subtitle_langs=parse_subtitle_langs_csv(row.subtitle_langs_csv or ""),
        preserve_forced_subs=bool(row.preserve_forced_subs),
        preserve_default_subs=bool(row.preserve_default_subs),
        audio_preference_mode=normalize_audio_preference_mode(row.audio_preference_mode),
    )


def load_refiner_remux_rules_config(db: Session) -> RefinerRulesConfig:
    row = ensure_refiner_remux_rules_settings_row(db)
    return row_to_rules_config(row)


def build_refiner_remux_rules_settings_out(row: RefinerRemuxRulesSettingsRow) -> RefinerRemuxRulesSettingsOut:
    return RefinerRemuxRulesSettingsOut(
        primary_audio_lang=row.primary_audio_lang,
        secondary_audio_lang=row.secondary_audio_lang,
        tertiary_audio_lang=row.tertiary_audio_lang,
        default_audio_slot=row.default_audio_slot,  # type: ignore[arg-type]
        remove_commentary=bool(row.remove_commentary),
        subtitle_mode=row.subtitle_mode,  # type: ignore[arg-type]
        subtitle_langs_csv=row.subtitle_langs_csv or "",
        preserve_forced_subs=bool(row.preserve_forced_subs),
        preserve_default_subs=bool(row.preserve_default_subs),
        audio_preference_mode=normalize_audio_preference_mode(row.audio_preference_mode),  # type: ignore[arg-type]
        updated_at=row.updated_at.isoformat() if row.updated_at else "",
    )


def apply_refiner_remux_rules_settings_put(db: Session, body: RefinerRemuxRulesSettingsPutIn) -> RefinerRemuxRulesSettingsRow:
    row = ensure_refiner_remux_rules_settings_row(db)
    if body.subtitle_mode == "keep_selected" and not (body.subtitle_langs_csv or "").strip():
        raise ValueError("When keeping subtitles, set at least one language in subtitle_langs_csv.")

    row.primary_audio_lang = body.primary_audio_lang.strip() or "eng"
    row.secondary_audio_lang = body.secondary_audio_lang.strip()
    row.tertiary_audio_lang = body.tertiary_audio_lang.strip()
    row.default_audio_slot = body.default_audio_slot
    row.remove_commentary = 1 if body.remove_commentary else 0
    row.subtitle_mode = body.subtitle_mode
    row.subtitle_langs_csv = body.subtitle_langs_csv.strip()
    row.preserve_forced_subs = 1 if body.preserve_forced_subs else 0
    row.preserve_default_subs = 1 if body.preserve_default_subs else 0
    row.audio_preference_mode = body.audio_preference_mode
    db.flush()
    return row
