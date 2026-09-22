/**
 * The Library title is the picker (James, 22 Sep 2026): "Library › TV ▾". One library shows its name and no
 * control at all; several open a menu from the title itself, with a search box once there are enough to need
 * one. He turned down a row of tabs for this: "a lot of information without much content".
 */
import { useEffect, useRef, useState } from "react";
import type { ProcessingLibrary } from "../../lib/processing/libraries-api";

const SEARCH_FROM = 7;

export function LibraryPicker({
  libraries,
  chosenId,
  onPick,
  countFor,
}: {
  libraries: ProcessingLibrary[];
  chosenId: number;
  onPick: (id: number) => void;
  /** Files in a library, when that is known without asking the server for it. */
  countFor?: (id: number) => number | null;
}): React.ReactElement {
  const [open, setOpen] = useState(false);
  const [find, setFind] = useState("");
  const wrap = useRef<HTMLDivElement>(null);
  const chosen = libraries.find((library) => library.id === chosenId);

  useEffect(() => {
    if (!open) return undefined;
    const away = (event: MouseEvent) => {
      if (wrap.current && !wrap.current.contains(event.target as Node))
        setOpen(false);
    };
    const key = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", away);
    document.addEventListener("keydown", key);
    return () => {
      document.removeEventListener("mousedown", away);
      document.removeEventListener("keydown", key);
    };
  }, [open]);

  if (libraries.length <= 1) {
    return (
      <span className="mm-library-title" data-testid="library-picker">
        <span aria-hidden="true" className="mm-library-title__sep">
          ›
        </span>
        {chosen?.name ?? "—"}
      </span>
    );
  }

  const shown = find.trim()
    ? libraries.filter((library) =>
        library.name.toLowerCase().includes(find.trim().toLowerCase()),
      )
    : libraries;

  return (
    <div className="mm-library-title" ref={wrap} data-testid="library-picker">
      <span aria-hidden="true" className="mm-library-title__sep">
        ›
      </span>
      <button
        type="button"
        className="mm-library-title__button"
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        {chosen?.name ?? "Choose"}
        <span aria-hidden="true" className="mm-library-title__caret">
          ▾
        </span>
      </button>
      {open ? (
        <div
          className="mm-library-menu"
          role="listbox"
          aria-label="Choose a library"
        >
          {libraries.length >= SEARCH_FROM ? (
            <input
              type="search"
              className="mm-input"
              aria-label="Find a library"
              placeholder="Find a library"
              value={find}
              onChange={(event) => setFind(event.target.value)}
            />
          ) : null}
          {shown.map((library) => {
            const count = countFor?.(library.id) ?? null;
            return (
              <button
                key={library.id}
                type="button"
                role="option"
                aria-selected={library.id === chosenId}
                className={`mm-library-menu__item${library.id === chosenId ? " mm-library-menu__item--on" : ""}`}
                onClick={() => {
                  onPick(library.id);
                  setOpen(false);
                }}
              >
                <span>{library.name}</span>
                <small>
                  {library.media_type === "tv" ? "TV episodes" : "Movies"}
                  {count !== null ? ` · ${count.toLocaleString()} files` : ""}
                </small>
              </button>
            );
          })}
          {shown.length === 0 ? (
            <p className="mm-library-menu__empty">No library by that name.</p>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
