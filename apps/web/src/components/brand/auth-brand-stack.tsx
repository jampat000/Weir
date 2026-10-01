import { WeirLogo } from "./weir-logo";

/** Auth/setup — premium logo + primary blurb above the card. */
export function AuthBrandStack() {
  return (
    <div className="mm-auth-brand">
      <div className="mm-auth-brand-logo">
        <WeirLogo variant="auth" />
      </div>
      {/* Both of the things Weir does, and neither overstated: Processing also cleans files already
          sitting in a library, in place. */}
      <p className="mm-auth-brand-tagline">
        Cleans new downloads, and files already in your library.
      </p>
    </div>
  );
}
