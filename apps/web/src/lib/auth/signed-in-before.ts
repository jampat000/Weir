/**
 * Whether this browser has had a signed-in session. A 401 means "your session ended" only to someone who had one: a
 * first visit, or a browser that signed out on purpose, has no session to expire, so the sign-in page says nothing of
 * the kind. The mark is set whenever Weir answers as a signed-in user and cleared by signing out.
 */

const KEY = "mm:signed-in-before";

function store(): Storage | null {
  // Private windows and blocked site data make this throw rather than return null.
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export function markSignedIn(): void {
  try {
    store()?.setItem(KEY, "1");
  } catch {
    /* Without the mark a session that ended reads as a plain sign-in, which is the quieter mistake. */
  }
}

export function clearSignedIn(): void {
  try {
    store()?.removeItem(KEY);
  } catch {
    /* ignored */
  }
}

export function wasSignedIn(): boolean {
  try {
    return store()?.getItem(KEY) === "1";
  } catch {
    return false;
  }
}

/** The sign-in page for someone who is signed out, saying a session expired only when this browser had one. */
export function signedOutPath(): string {
  return wasSignedIn() ? "/login?session=expired" : "/login";
}
