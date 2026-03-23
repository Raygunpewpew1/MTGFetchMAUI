#!/usr/bin/env python3
"""
Build AtomicCards.sqlite from MTGJSON AtomicCards.json(.zip).

Usage:
  python3 scripts/build_atomic_sqlite.py [path/to/AtomicCards.json]
If no path is given, downloads https://mtgjson.com/api/v5/AtomicCards.json.zip

Output: AtomicCards.sqlite in the current working directory (CI: repo root).
"""
from __future__ import annotations

import json
import sqlite3
import sys
import tempfile
import zipfile
from pathlib import Path
from urllib.request import urlretrieve

MTGJSON_ATOMIC_ZIP = "https://mtgjson.com/api/v5/AtomicCards.json.zip"
OUT_DB = "AtomicCards.sqlite"

# Must match CREATE TABLE atomic_cards columns except id (order matters for INSERT).
INSERT_COLUMNS = [
    "name",
    "face_index",
    "ascii_name",
    "face_name",
    "mana_cost",
    "mana_value",
    "type_line",
    "oracle_text",
    "power",
    "toughness",
    "loyalty",
    "defense",
    "layout",
    "colors",
    "color_identity",
    "keywords",
    "scryfall_id",
    "scryfall_oracle_id",
    "first_printing",
    "printings_json",
    "legalities_json",
    "rulings_json",
    "related_json",
    "leadership_json",
    "is_reserved",
    "is_funny",
]


def load_atomic_json(path: Path) -> dict:
    if path.suffix.lower() == ".zip":
        with zipfile.ZipFile(path, "r") as zf:
            names = [n for n in zf.namelist() if n.lower().endswith(".json")]
            if not names:
                raise SystemExit("No .json inside zip")
            with zf.open(names[0]) as f:
                return json.load(f)
    with path.open(encoding="utf-8") as f:
        return json.load(f)


def jcompact(obj) -> str | None:
    if obj is None:
        return None
    return json.dumps(obj, separators=(",", ":"), ensure_ascii=False)


def join_colors(arr) -> str:
    if not arr:
        return ""
    return ",".join(str(x) for x in arr)


def row_from_face(card_name: str, fi: int, face: dict) -> tuple:
    ids = face.get("identifiers") or {}
    if not isinstance(ids, dict):
        ids = {}
    scryfall_id = (ids.get("scryfallId") or "").strip()
    scryfall_oracle_id = (ids.get("scryfallOracleId") or "").strip()
    return (
        card_name,
        fi,
        (face.get("asciiName") or "").strip() or None,
        (face.get("faceName") or "").strip() or None,
        (face.get("manaCost") or "").strip() or None,
        float(face.get("manaValue") or face.get("convertedManaCost") or 0),
        (face.get("type") or "").strip() or None,
        (face.get("text") or "").strip() or None,
        (face.get("power") or "").strip() or None,
        (face.get("toughness") or "").strip() or None,
        (face.get("loyalty") or "").strip() or None,
        (face.get("defense") or "").strip() or None,
        (face.get("layout") or "").strip() or "normal",
        join_colors(face.get("colors") or []),
        join_colors(face.get("colorIdentity") or []),
        ",".join(face.get("keywords") or []) if face.get("keywords") else "",
        scryfall_id,
        scryfall_oracle_id,
        (face.get("firstPrinting") or "").strip() or None,
        jcompact(face.get("printings")),
        jcompact(face.get("legalities")),
        jcompact(face.get("rulings")),
        jcompact(face.get("relatedCards")),
        jcompact(face.get("leadershipSkills")),
        1 if face.get("isReserved") else 0,
        1 if face.get("isFunny") else 0,
    )


def main() -> None:
    work = Path.cwd()
    out_path = work / OUT_DB

    if len(sys.argv) > 1:
        src = Path(sys.argv[1])
        if not src.is_file():
            raise SystemExit(f"Not found: {src}")
        data = load_atomic_json(src)
    else:
        print("Downloading AtomicCards.json.zip …")
        with tempfile.NamedTemporaryFile(suffix=".zip", delete=False) as tmp:
            tmp_path = Path(tmp.name)
        try:
            urlretrieve(MTGJSON_ATOMIC_ZIP, tmp_path)
            data = load_atomic_json(tmp_path)
        finally:
            tmp_path.unlink(missing_ok=True)

    atomic = data.get("data") or {}
    if not isinstance(atomic, dict):
        raise SystemExit("Invalid AtomicCards: missing data object")

    if out_path.exists():
        out_path.unlink()

    conn = sqlite3.connect(str(out_path))
    try:
        cur = conn.cursor()
        cur.executescript(
            """
            PRAGMA journal_mode = OFF;
            PRAGMA synchronous = OFF;
            CREATE TABLE atomic_cards (
              id INTEGER PRIMARY KEY,
              name TEXT NOT NULL,
              face_index INTEGER NOT NULL DEFAULT 0,
              ascii_name TEXT,
              face_name TEXT,
              mana_cost TEXT,
              mana_value REAL,
              type_line TEXT,
              oracle_text TEXT,
              power TEXT,
              toughness TEXT,
              loyalty TEXT,
              defense TEXT,
              layout TEXT,
              colors TEXT,
              color_identity TEXT,
              keywords TEXT,
              scryfall_id TEXT,
              scryfall_oracle_id TEXT,
              first_printing TEXT,
              printings_json TEXT,
              legalities_json TEXT,
              rulings_json TEXT,
              related_json TEXT,
              leadership_json TEXT,
              is_reserved INTEGER DEFAULT 0,
              is_funny INTEGER DEFAULT 0,
              UNIQUE(name, face_index)
            );
            CREATE INDEX idx_atomic_name ON atomic_cards(name);
            CREATE INDEX idx_atomic_mana ON atomic_cards(mana_value);
            CREATE INDEX idx_atomic_layout ON atomic_cards(layout);
            CREATE VIRTUAL TABLE atomic_cards_fts USING fts5(
              name,
              oracle_text,
              type_line,
              keywords,
              content='atomic_cards',
              content_rowid='id',
              tokenize='unicode61'
            );
            """
        )

        rows = []
        for card_name, faces in atomic.items():
            if not isinstance(faces, list):
                continue
            for fi, face in enumerate(faces):
                if not isinstance(face, dict):
                    continue
                row = row_from_face(card_name, fi, face)
                if len(row) != len(INSERT_COLUMNS):
                    raise SystemExit(
                        f"Bug: row has {len(row)} values, INSERT_COLUMNS has {len(INSERT_COLUMNS)}"
                    )
                rows.append(row)

        cols_sql = ", ".join(INSERT_COLUMNS)
        placeholders = ",".join(["?"] * len(INSERT_COLUMNS))
        insert_sql = f"INSERT INTO atomic_cards ({cols_sql}) VALUES ({placeholders})"
        cur.executemany(insert_sql, rows)

        cur.execute("INSERT INTO atomic_cards_fts(atomic_cards_fts) VALUES('rebuild')")
        cur.execute("PRAGMA user_version = 1")
        conn.commit()
        print(f"Wrote {out_path} ({out_path.stat().st_size // 1024 // 1024} MB), rows={len(rows)}")
    finally:
        conn.close()


if __name__ == "__main__":
    main()
