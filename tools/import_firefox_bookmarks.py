import argparse
import json
import os
import shutil
import sqlite3
import tempfile
from pathlib import Path
from urllib.parse import urlparse

FIREFOX_PROFILES = Path(os.environ["APPDATA"]) / "Mozilla" / "Firefox" / "Profiles"
TOOLBAR_GUID = "toolbar_____"
OTHER_ROOT_GUIDS = {"unfiled_____": "Weitere Lesezeichen", "mobile______": "Mobile Lesezeichen"}
BOOKMARK, FOLDER = 1, 2
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
PREFERRED_ICON_WIDTH = 32


def find_profile() -> Path:
    candidates = sorted(FIREFOX_PROFILES.glob("*/places.sqlite"), key=lambda path: path.stat().st_mtime, reverse=True)
    if not candidates:
        raise SystemExit(f"Keine places.sqlite unter {FIREFOX_PROFILES} gefunden.")
    return candidates[0].parent


def query(database: Path, sql: str, *params) -> list[tuple]:
    # Firefox keeps its databases locked while running, so work on a copy.
    with tempfile.TemporaryDirectory() as temp_dir:
        copy = Path(temp_dir) / database.name
        shutil.copyfile(database, copy)
        connection = sqlite3.connect(copy)
        try:
            return connection.execute(sql, params).fetchall()
        finally:
            connection.close()


def read_tree(places_db: Path) -> tuple[dict, dict]:
    rows = query(
        places_db,
        "SELECT b.id, b.parent, b.type, b.title, b.guid, p.url FROM moz_bookmarks b "
        "LEFT JOIN moz_places p ON p.id = b.fk ORDER BY b.parent, b.position",
    )

    children: dict[int, list] = {}
    roots: dict[str, int] = {}
    for row_id, parent, row_type, title, guid, url in rows:
        children.setdefault(parent, []).append((row_id, row_type, title, url))
        roots[guid] = row_id
    return children, roots


def convert(children: dict, folder_id: int) -> list[dict]:
    nodes = []
    for row_id, row_type, title, url in children.get(folder_id, []):
        if row_type == FOLDER:
            nodes.append({"title": title or "Ordner", "children": convert(children, row_id)})
        elif row_type == BOOKMARK and url and not url.startswith("place:"):
            nodes.append({"title": title or url, "url": url})
    return nodes


def bookmark_urls(nodes: list[dict]) -> list[str]:
    urls = []
    for node in nodes:
        urls.extend(bookmark_urls(node["children"]) if "children" in node else [node["url"]])
    return urls


def export_favicons(favicons_db: Path, urls: list[str], target_dir: Path) -> int:
    icons = query(
        favicons_db,
        "SELECT p.page_url, i.icon_url, i.width, i.data FROM moz_icons i "
        "LEFT JOIN moz_icons_to_pages ip ON ip.icon_id = i.id "
        "LEFT JOIN moz_pages_w_icons p ON p.id = ip.page_id",
    )
    best_by_host: dict[str, tuple[int, bytes]] = {}
    hosts = {urlparse(url).hostname for url in urls} - {None}
    exact_pages = set(urls)

    for page_url, icon_url, width, data in icons:
        if not data or not data.startswith(PNG_SIGNATURE):
            continue
        host = urlparse(page_url or icon_url).hostname
        if host not in hosts:
            continue
        rank = (0 if page_url in exact_pages else 1, abs(width - PREFERRED_ICON_WIDTH))
        if host not in best_by_host or rank < best_by_host[host][0]:
            best_by_host[host] = (rank, data)

    target_dir.mkdir(parents=True, exist_ok=True)
    for host, (_, data) in best_by_host.items():
        (target_dir / f"{host}.png").write_bytes(data)
    return len(best_by_host)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("target", type=Path, help="Pfad zur bookmarks.json von StuderBrowser")
    args = parser.parse_args()

    profile = find_profile()
    places_db = profile / "places.sqlite"
    children, roots = read_tree(places_db)

    bar = convert(children, roots[TOOLBAR_GUID])
    for guid, title in OTHER_ROOT_GUIDS.items():
        if guid in roots and (nodes := convert(children, roots[guid])):
            bar.append({"title": title, "children": nodes})

    args.target.parent.mkdir(parents=True, exist_ok=True)
    args.target.write_text(json.dumps(bar, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Importiert aus {places_db} nach {args.target}")

    urls = bookmark_urls(bar)
    favicon_count = export_favicons(profile / "favicons.sqlite", urls, args.target.parent / "favicons")
    print(f"{favicon_count} Favicons fuer {len({urlparse(url).hostname for url in urls} - {None})} Hosts exportiert")


if __name__ == "__main__":
    main()
